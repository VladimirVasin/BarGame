using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Clipping a terrain face can leave a vertex halfway along its
    /// neighbour's edge. Split that neighbour too: PS1 vertex snapping otherwise
    /// opens bright cracks even when the unsnapped triangles are coplanar.</summary>
    internal static class AlpineVillageJunctionSeams
    {
        private const float BucketSize = .5f;
        private const float Tolerance = .00015f;

        internal static void Conform(AlpineVillagePlan plan, List<Vector3> positions,
            List<Vector3> normals, List<Vector2> uvs, List<int>[] surfaces, IReadOnlyList<Mesh> paths)
        {
            var regions = new List<Rect>();
            foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
            {
                Rect bounds = junction.Bounds;
                // Include the original ground faces sharing the outside edge.
                regions.Add(Rect.MinMaxRect(bounds.xMin - 2f, bounds.yMin - 2f,
                    bounds.xMax + 2f, bounds.yMax + 2f));
            }
            var buckets = new Dictionary<Vector2Int, List<Vector3>>();
            foreach (List<int> surface in surfaces)
                for (int i = 0; i < surface.Count; i += 3)
                {
                    Vector3 a = positions[surface[i]], b = positions[surface[i + 1]], c = positions[surface[i + 2]];
                    if (!Affected(a, b, c)) continue;
                    AddPoint(a); AddPoint(b); AddPoint(c);
                }
            foreach (Mesh path in paths)
            {
                Vector3[] points = path.vertices;
                var used = new HashSet<int>(path.triangles);
                foreach (int vertex in used)
                    foreach (Rect region in regions)
                        if (region.Contains(XZ(points[vertex]))) { AddPoint(points[vertex]); break; }
            }

            var ring = new List<int>();
            var cuts = new List<Cut>();
            Refine();
            // Ground and incoming path rims need the SAME edge samples. Fixing
            // only one side would leave a bright transverse seam at the mouth.
            foreach (Mesh path in paths)
            {
                positions = new List<Vector3>(path.vertices);
                normals = new List<Vector3>(path.normals);
                uvs = new List<Vector2>(path.uv);
                surfaces = new[] { new List<int>(path.triangles) };
                Refine();
                path.indexFormat = positions.Count > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                path.SetVertices(positions);
                path.SetNormals(normals);
                path.SetUVs(0, uvs);
                path.SetTriangles(surfaces[0], 0);
                path.RecalculateBounds();
            }

            void Refine()
            {
                for (int slot = 0; slot < surfaces.Length; slot++)
                {
                    List<int> source = surfaces[slot];
                    var output = new List<int>(source.Count);
                    for (int i = 0; i < source.Count; i += 3)
                    {
                        int a = source[i], b = source[i + 1], c = source[i + 2];
                        if (!Affected(positions[a], positions[b], positions[c]))
                        { output.Add(a); output.Add(b); output.Add(c); continue; }
                        ring.Clear();
                        Edge(a, b); Edge(b, c); Edge(c, a);
                        if (ring.Count == 3)
                        { output.Add(a); output.Add(b); output.Add(c); continue; }
                        int centre = positions.Count;
                        positions.Add((positions[a] + positions[b] + positions[c]) / 3f);
                        normals.Add((normals[a] + normals[b] + normals[c]).normalized);
                        uvs.Add((uvs[a] + uvs[b] + uvs[c]) / 3f);
                        for (int edge = 0; edge < ring.Count; edge++)
                        {
                            int first = ring[edge], next = ring[(edge + 1) % ring.Count];
                            if (Vector3.Cross(positions[first] - positions[centre],
                                positions[next] - positions[centre]).sqrMagnitude < 1e-14f) continue;
                            output.Add(centre); output.Add(first); output.Add(next);
                        }
                    }
                    surfaces[slot] = output;
                }
            }

            void Edge(int first, int last)
            {
                ring.Add(first);
                Vector3 a = positions[first], b = positions[last];
                Vector2 delta = XZ(b - a);
                float squaredLength = delta.sqrMagnitude;
                if (squaredLength < Tolerance * Tolerance) return;
                float length = Mathf.Sqrt(squaredLength);
                cuts.Clear();
                Vector2Int min = Bucket(new Vector3(Mathf.Min(a.x, b.x) - Tolerance, 0f,
                    Mathf.Min(a.z, b.z) - Tolerance));
                Vector2Int max = Bucket(new Vector3(Mathf.Max(a.x, b.x) + Tolerance, 0f,
                    Mathf.Max(a.z, b.z) + Tolerance));
                for (int x = min.x; x <= max.x; x++)
                for (int z = min.y; z <= max.y; z++)
                {
                    if (!buckets.TryGetValue(new Vector2Int(x, z), out List<Vector3> points)) continue;
                    foreach (Vector3 point in points)
                    {
                        float t = Vector2.Dot(XZ(point - a), delta) / squaredLength;
                        if (t * length <= Tolerance || (1f - t) * length <= Tolerance) continue;
                        Vector3 onEdge = Vector3.LerpUnclamped(a, b, t);
                        if ((XZ(point - onEdge)).sqrMagnitude > Tolerance * Tolerance ||
                            Mathf.Abs(point.y - onEdge.y) > .001f) continue;
                        cuts.Add(new Cut(t, point));
                    }
                }
                cuts.Sort((left, right) => left.T.CompareTo(right.T));
                float previous = -1f;
                foreach (Cut cut in cuts)
                {
                    if ((cut.T - previous) * length <= Tolerance) continue;
                    previous = cut.T;
                    ring.Add(positions.Count);
                    // Keep this face's height: a nearby path point carries
                    // its own small lift, which must not pull ground upward.
                    positions.Add(new Vector3(cut.Point.x,
                        Mathf.LerpUnclamped(a.y, b.y, cut.T), cut.Point.z));
                    normals.Add(Vector3.LerpUnclamped(normals[first], normals[last], cut.T).normalized);
                    uvs.Add(Vector2.LerpUnclamped(uvs[first], uvs[last], cut.T));
                }
            }

            bool Affected(Vector3 a, Vector3 b, Vector3 c)
            {
                var bounds = Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.z, b.z, c.z),
                    Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.z, b.z, c.z));
                foreach (Rect region in regions) if (region.Overlaps(bounds)) return true;
                return false;
            }

            void AddPoint(Vector3 point)
            {
                Vector2Int key = Bucket(point);
                if (!buckets.TryGetValue(key, out List<Vector3> points))
                    buckets.Add(key, points = new List<Vector3>());
                foreach (Vector3 existing in points)
                    if ((point - existing).sqrMagnitude < Tolerance * Tolerance) return;
                points.Add(point);
            }
        }

        private readonly struct Cut
        {
            internal readonly float T;
            internal readonly Vector3 Point;
            internal Cut(float t, Vector3 point) { T = t; Point = point; }
        }
        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static Vector2Int Bucket(Vector3 point) => new Vector2Int(
            Mathf.FloorToInt(point.x / BucketSize), Mathf.FloorToInt(point.z / BucketSize));
    }
}
