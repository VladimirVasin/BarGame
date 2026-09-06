using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// One ground-fitted surface per consecutive route chain. A single contour
    /// contains inner offset-line corners, outer round joins and free round
    /// ends; no cap or joint is laid over another ribbon. The terrain keeps
    /// all collision and the path descriptors keep their capsule snow mask.
    /// </summary>
    internal static class AlpineVillagePathSurfaceBuilder
    {
        internal const float MaximumEdgeLength = .4f;
        private const float BoundaryStep = .20f;
        private const float Epsilon = .00001f;

        public static void Build(Transform parent, AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> paths, Color tint)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            for (int index = 0; index < paths.Count;)
            {
                var chain = new List<AlpineVillagePathDescriptor> { paths[index++] };
                while (index < paths.Count && Continues(chain[chain.Count - 1], paths[index]))
                    chain.Add(paths[index++]);
                Mesh mesh = CreateMesh(plan, chain);
                var host = new GameObject("Visible Path - " + chain[0].StableId);
                host.transform.SetParent(parent, false);
                host.AddComponent<MeshFilter>().sharedMesh = mesh;
                host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                MountainRoadSurfaceAppearance.ApplyCombined(renderer,
                    MountainRoadSurfaceKind.ForestFloor, tint);
            }
        }

        private static bool Continues(AlpineVillagePathDescriptor first,
            AlpineVillagePathDescriptor next)
        {
            return first.Kind == next.Kind && first.OwnerPlotStableId == next.OwnerPlotStableId &&
                Mathf.Abs(first.SurfaceHalfWidth - next.SurfaceHalfWidth) < Epsilon &&
                (XZ(first.End) - XZ(next.Start)).sqrMagnitude < .000001f;
        }

        /// <summary>Exposed to the existing capture's focused mesh contract.</summary>
        internal static Mesh CreateMesh(AlpineVillagePlan plan,
            IReadOnlyList<AlpineVillagePathDescriptor> chain)
        {
            if (chain == null || chain.Count == 0)
                throw new ArgumentException("A visible route needs a segment.", nameof(chain));
            // Keep polygon arithmetic near zero, avoiding loss of precision
            // around the village's 800-metre world-space offset.
            Vector2 origin = XZ(chain[0].Start);
            var centres = new List<Vector2> { Vector2.zero };
            for (int index = 0; index < chain.Count; index++)
            {
                if (index > 0 && !Continues(chain[index - 1], chain[index]))
                    throw new ArgumentException("The visible route chain is disconnected.", nameof(chain));
                Vector2 point = XZ(chain[index].End) - origin;
                if ((point - centres[centres.Count - 1]).sqrMagnitude > Epsilon * Epsilon)
                    centres.Add(point);
            }
            if (centres.Count < 2)
                throw new ArgumentException("The visible route has zero length.", nameof(chain));
            float radius = chain[0].SurfaceHalfWidth;
            if (radius <= 0f) throw new ArgumentException("The visible route has no width.", nameof(chain));
            List<Vector2> points = Contour(centres, radius);
            List<int> triangles = Triangulate(points);
            Refine(points, ref triangles);
            ValidateCoverage(points, triangles, origin, chain);
            var vertices = new List<Vector3>(points.Count);
            var uvs = new List<Vector2>(points.Count);
            float pitch = MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.ForestFloor).MetersPerTile;
            foreach (Vector2 local in points)
            {
                Vector2 world = local + origin;
                float ground = Mathf.Max(
                    AlpineVillageTerrainSampler.SampleHeight(plan, world),
                    AlpineVillageTerrainSampler.SampleMeshHeight(plan, world));
                vertices.Add(new Vector3(world.x,
                    ground + AlpineVillageWorldBuilder.LaneSkinLift, world.y));
                uvs.Add(world / pitch);
            }
            // A positive XZ contour has its 3D normal downward.
            for (int index = 0; index < triangles.Count; index += 3)
            {
                int second = triangles[index + 1];
                triangles[index + 1] = triangles[index + 2];
                triangles[index + 2] = second;
            }
            var mesh = new Mesh { name = chain[0].StableId + " Surface" };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Vector2> Contour(List<Vector2> centres, float radius)
        {
            var left = new List<Vector2>();
            var right = new List<Vector2>();
            Vector2 first = (centres[1] - centres[0]).normalized;
            left.Add(centres[0] + Left(first) * radius);
            right.Add(centres[0] - Left(first) * radius);
            for (int index = 1; index < centres.Count - 1; index++)
            {
                Vector2 incoming = (centres[index] - centres[index - 1]).normalized;
                Vector2 outgoing = (centres[index + 1] - centres[index]).normalized;
                float turn = Mathf.Atan2(Cross(incoming, outgoing), Vector2.Dot(incoming, outgoing));
                float denominator = 1f + Vector2.Dot(incoming, outgoing);
                if (denominator < .001f)
                    throw new InvalidOperationException("A village route reverses inside its own width.");
                Vector2 inner = (Left(incoming) + Left(outgoing)) * (radius / denominator);
                if (turn > Epsilon)
                {
                    left.Add(centres[index] + inner);
                    Arc(right, centres[index], -Left(incoming) * radius, turn);
                }
                else if (turn < -Epsilon)
                {
                    Arc(left, centres[index], Left(incoming) * radius, turn);
                    right.Add(centres[index] - inner);
                }
                else
                {
                    left.Add(centres[index] + inner);
                    right.Add(centres[index] - inner);
                }
            }
            Vector2 last = (centres[centres.Count - 1] - centres[centres.Count - 2]).normalized;
            Vector2 end = centres[centres.Count - 1];
            left.Add(end + Left(last) * radius);
            right.Add(end - Left(last) * radius);
            var contour = new List<Vector2>(left);
            Arc(contour, end, Left(last) * radius, -Mathf.PI);
            for (int index = right.Count - 1; index >= 0; index--) contour.Add(right[index]);
            Arc(contour, centres[0], -Left(first) * radius, -Mathf.PI);
            for (int index = contour.Count - 1; index >= 0; index--)
            {
                int previous = (index + contour.Count - 1) % contour.Count;
                if ((contour[index] - contour[previous]).sqrMagnitude < Epsilon * Epsilon)
                    contour.RemoveAt(index);
            }
            // Collinear samples do not help the silhouette. Removing them
            // before triangulation keeps its shared edges unambiguous.
            bool removed;
            do
            {
                removed = false;
                for (int index = 0; index < contour.Count && contour.Count > 3; index++)
                {
                    Vector2 before = contour[index] - contour[(index + contour.Count - 1) % contour.Count];
                    Vector2 after = contour[(index + 1) % contour.Count] - contour[index];
                    if (Mathf.Abs(Cross(before, after)) < Epsilon && Vector2.Dot(before, after) >= 0)
                    { contour.RemoveAt(index); removed = true; break; }
                }
            } while (removed);
            float area = 0f;
            for (int index = 0; index < contour.Count; index++)
                area += Cross(contour[index], contour[(index + 1) % contour.Count]);
            if (area < 0) contour.Reverse();
            return contour;
        }

        private static void Arc(List<Vector2> points, Vector2 centre, Vector2 start, float angle)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angle) * start.magnitude / BoundaryStep));
            for (int step = 0; step <= steps; step++)
            {
                float amount = angle * step / steps;
                float sin = Mathf.Sin(amount), cos = Mathf.Cos(amount);
                points.Add(centre + new Vector2(start.x * cos - start.y * sin,
                    start.x * sin + start.y * cos));
            }
        }

        private static List<int> Triangulate(List<Vector2> points)
        {
            var remaining = new List<int>(points.Count);
            for (int index = 0; index < points.Count; index++) remaining.Add(index);
            var triangles = new List<int>();
            while (remaining.Count > 3)
            {
                bool found = false;
                for (int index = 0; index < remaining.Count; index++)
                {
                    int a = remaining[(index + remaining.Count - 1) % remaining.Count];
                    int b = remaining[index], c = remaining[(index + 1) % remaining.Count];
                    if (Cross(points[b] - points[a], points[c] - points[a]) <= Epsilon) continue;
                    bool contains = false;
                    foreach (int candidate in remaining)
                    {
                        if (candidate == a || candidate == b || candidate == c) continue;
                        Vector2 p = points[candidate];
                        if (Cross(points[b] - points[a], p - points[a]) >= -Epsilon &&
                            Cross(points[c] - points[b], p - points[b]) >= -Epsilon &&
                            Cross(points[a] - points[c], p - points[c]) >= -Epsilon)
                        { contains = true; break; }
                    }
                    if (contains) continue;
                    Triangle(triangles, a, b, c);
                    remaining.RemoveAt(index);
                    found = true;
                    break;
                }
                if (!found) throw new InvalidOperationException("A village route contour crosses itself.");
            }
            Triangle(triangles, remaining[0], remaining[1], remaining[2]);
            return triangles;
        }

        private static void Refine(List<Vector2> points, ref List<int> triangles)
        {
            float limit = MaximumEdgeLength * MaximumEdgeLength;
            for (int pass = 0; pass < 20; pass++)
            {
                var mids = new Dictionary<ulong, int>();
                for (int index = 0; index < triangles.Count; index += 3)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int a = triangles[index + edge], b = triangles[index + (edge + 1) % 3];
                        ulong key = Edge(a, b);
                        if ((points[a] - points[b]).sqrMagnitude <= limit || mids.ContainsKey(key)) continue;
                        mids.Add(key, points.Count);
                        points.Add((points[a] + points[b]) * .5f);
                    }
                if (mids.Count == 0) return;
                var next = new List<int>(triangles.Count * 2);
                for (int index = 0; index < triangles.Count; index += 3)
                {
                    int a = triangles[index], b = triangles[index + 1], c = triangles[index + 2];
                    int mask = mids.TryGetValue(Edge(a, b), out int ab) ? 1 : 0;
                    if (mids.TryGetValue(Edge(b, c), out int bc)) mask |= 2;
                    if (mids.TryGetValue(Edge(c, a), out int ca)) mask |= 4;
                    switch (mask)
                    {
                        case 0: Triangle(next, a, b, c); break;
                        case 1: Triangle(next, a, ab, c); Triangle(next, ab, b, c); break;
                        case 2: Triangle(next, b, bc, a); Triangle(next, bc, c, a); break;
                        case 4: Triangle(next, c, ca, b); Triangle(next, ca, a, b); break;
                        case 3: Triangle(next, b, bc, ab); Triangle(next, a, ab, c); Triangle(next, ab, bc, c); break;
                        case 5: Triangle(next, a, ab, ca); Triangle(next, ab, b, c); Triangle(next, ab, c, ca); break;
                        case 6: Triangle(next, c, ca, bc); Triangle(next, a, b, ca); Triangle(next, b, bc, ca); break;
                        default:
                            Triangle(next, a, ab, ca); Triangle(next, ab, b, bc);
                            Triangle(next, ca, bc, c); Triangle(next, ab, bc, ca); break;
                    }
                }
                triangles = next;
            }
            throw new InvalidOperationException("The visible village route could not reach its terrain sampling pitch.");
        }

        private static void ValidateCoverage(List<Vector2> points, List<int> triangles,
            Vector2 origin, IReadOnlyList<AlpineVillagePathDescriptor> chain)
        {
            // A miter on the OUTER corner would look plausible while drawing
            // into snow that the shared capsule field says is untouched.
            // Check both the contour and triangle interiors in that contract.
            foreach (Vector2 point in points) RequireInsideRoute(point + origin, chain);
            for (int index = 0; index < triangles.Count; index += 3)
            {
                Vector2 a = points[triangles[index]], b = points[triangles[index + 1]],
                    c = points[triangles[index + 2]];
                RequireInsideRoute((a + b + c) / 3f + origin, chain);
                if (Cross(b - a, c - a) < -.000001f ||
                    (a - b).magnitude > MaximumEdgeLength + .0002f ||
                    (b - c).magnitude > MaximumEdgeLength + .0002f ||
                    (c - a).magnitude > MaximumEdgeLength + .0002f)
                    throw new InvalidOperationException("The visible route lost its upward, ground-fitted tessellation.");
            }
        }

        private static void RequireInsideRoute(Vector2 point,
            IReadOnlyList<AlpineVillagePathDescriptor> chain)
        {
            for (int index = 0; index < chain.Count; index++)
                if (chain[index].DistanceToCenterline(point) <= chain[index].SurfaceHalfWidth + .003f)
                    return;
            throw new InvalidOperationException("The visible route leaves its capsule snow-clearance envelope: " +
                chain[0].StableId + " at " + point);
        }

        private static ulong Edge(int a, int b) =>
            ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        private static void Triangle(List<int> triangles, int a, int b, int c)
        { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        private static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);
        private static Vector2 Left(Vector2 direction) => new Vector2(-direction.y, direction.x);
        private static float Cross(Vector2 first, Vector2 second) => first.x * second.y - first.y * second.x;
    }
}
