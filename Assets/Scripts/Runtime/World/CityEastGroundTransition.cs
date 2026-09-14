using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Partitions the existing visible terrain triangles, never adds
    /// a competing sheet. Collision keeps the original continuous terrain.
    /// Two submeshes share PS1 Lit; only their packaged albedo/UVs differ.</summary>
    internal static class CityEastGroundTransition
    {
        private const float Epsilon = .00001f;
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        // Scene activation can unload a texture referenced only by a native
        // property block. Match the other surface recipes' managed resource
        // ownership so both submeshes retain their packaged albedo.
        private static Texture2D cachedTexture;

        internal static void Apply(Transform surfaces, CityLayout layout)
        {
            CityEastGroundTransitionPlan plan = CityEastGroundTransitionPlan.Create(layout);
            if (!plan.IsEnabled) return;
            if (cachedTexture == null)
                cachedTexture = Resources.Load<Texture2D>(CityEastGroundTransitionPlan.TextureResource);
            Texture2D texture = cachedTexture;
            if (texture == null) throw new InvalidOperationException("Missing packaged eastern ground transition.");
            Transform church = surfaces.Find(CityChurchGroundWorldBuilder.ObjectName);
            Transform yard = surfaces.Find(CityFringeYardGroundWorldBuilder.GenericGroundObjectName);
            IReadOnlyList<Vertex> seam = CreateSharedSeam(church, yard, plan);
            Apply(church, plan, texture,
                CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn).MetersPerTile, seam);
            Apply(yard, plan, texture,
                CityFringeYardSurfaceAppearance.GetRecipe(CityFringeYardSurfaceKind.ForefieldGround).MetersPerTile, seam);
            surfaces.gameObject.AddComponent<CityEastGroundFootsteps>().Configure(layout, plan, FootstepGroundKind.Soil);
            surfaces.gameObject.AddComponent<CityEastGroundFootsteps>().Configure(layout, plan, FootstepGroundKind.Grass);
        }

        private static void Apply(Transform surface, CityEastGroundTransitionPlan plan,
            Texture2D texture, float originalPitch, IReadOnlyList<Vertex> seam)
        {
            if (surface == null) throw new InvalidOperationException("Eastern ground transition needs both terrain owners.");
            MeshFilter filter = surface.GetComponent<MeshFilter>();
            Mesh source = filter.sharedMesh;
            Vector3[] sourceVertices = source.vertices, sourceNormals = source.normals;
            Vector2[] sourceUvs = source.uv;
            int[] sourceTriangles = source.triangles;
            var vertices = new List<Vector3>(sourceVertices.Length);
            var normals = new List<Vector3>(sourceVertices.Length);
            var uvs = new List<Vector2>(sourceVertices.Length);
            var ordinary = new List<int>(sourceTriangles.Length);
            var transition = new List<int>();
            var remaining = new List<Vertex>(7);
            var inside = new List<Vertex>(7);
            var outside = new List<Vertex>(7);
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                remaining.Clear();
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = sourceTriangles[i + corner];
                    remaining.Add(new Vertex(sourceVertices[index], sourceNormals[index], sourceUvs[index]));
                }
                Vector3 center = (remaining[0].Position + remaining[1].Position + remaining[2].Position) / 3f;
                bool upward = (remaining[0].Normal.y + remaining[1].Normal.y + remaining[2].Normal.y) > .6f;
                // The church's old outer skirt is now an internal face.
                // Keep it in the original collider, never in the visible join.
                bool withinEast = center.x >= plan.Bounds.xMin && center.x <= plan.Bounds.xMax;
                if (withinEast && !upward && OnSeam(remaining[0], plan.SeamZ) &&
                    OnSeam(remaining[1], plan.SeamZ) && OnSeam(remaining[2], plan.SeamZ)) continue;
                if (withinEast && upward) StitchSeam(remaining, seam, plan.SeamZ);
                float low = Mathf.Min(remaining[0].Position.z, remaining[1].Position.z, remaining[2].Position.z);
                float high = Mathf.Max(remaining[0].Position.z, remaining[1].Position.z, remaining[2].Position.z);
                if (!upward || high <= plan.Bounds.yMin || low >= plan.Bounds.yMax ||
                    center.x < plan.Bounds.xMin || center.x > plan.Bounds.xMax)
                {
                    Append(remaining, false);
                    continue;
                }
                // Both source owners end exactly at the common X bounds.
                // Clipping also handles a triangle straddling a strip edge;
                // interpolation retains its exact original ground plane.
                Split(remaining, plan.Bounds.yMin, true, inside, outside);
                Append(outside, false);
                remaining.Clear(); remaining.AddRange(inside);
                Split(remaining, plan.Bounds.yMax, false, inside, outside);
                Append(outside, false);
                Append(inside, true);
            }
            if (transition.Count == 0) throw new InvalidOperationException("Eastern terrain did not intersect its transition band.");
            var mesh = new Mesh { name = source.name + " Garden–Yard Transition",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(ordinary, 0); mesh.SetTriangles(transition, 1);
            mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            filter.sharedMesh = mesh;
            surface.gameObject.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);

            Renderer renderer = surface.GetComponent<Renderer>();
            var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original);
            original.SetVector(BaseMapST, new Vector4(1f, 1f, 0f, -plan.SeamZ / originalPitch));
            renderer.sharedMaterials = new[] { RuntimePrimitiveFactory.DefaultMaterial, RuntimePrimitiveFactory.DefaultMaterial };
            renderer.SetPropertyBlock(null);
            renderer.SetPropertyBlock(original, 0);
            var blend = new MaterialPropertyBlock();
            blend.SetTexture(BaseMap, texture);
            blend.SetVector(BaseMapST, new Vector4(1f, 1f, 0f, 0f));
            blend.SetColor(BaseColor, Color.white); blend.SetColor(ColorId, Color.white);
            blend.SetFloat("_Smoothness", .0275f); blend.SetFloat("_Metallic", 0f);
            renderer.SetPropertyBlock(blend, 1);

            void Append(List<Vertex> polygon, bool blended)
            {
                if (polygon.Count < 3) return;
                // Starting on the seam would discard collinear inserted
                // vertices as degenerate fan triangles and recreate the
                // very T-junction this shared edge removes under PS1 snap.
                int origin = 0;
                for (int index = 1; index < polygon.Count; index++)
                    if (Mathf.Abs(polygon[index].Position.z - plan.SeamZ) >
                        Mathf.Abs(polygon[origin].Position.z - plan.SeamZ)) origin = index;
                for (int corner = 1; corner < polygon.Count - 1; corner++)
                {
                    Vertex a = polygon[origin], b = polygon[(origin + corner) % polygon.Count],
                        c = polygon[(origin + corner + 1) % polygon.Count];
                    if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).sqrMagnitude < 1e-12f) continue;
                    Add(a); Add(b); Add(c);
                }
                void Add(Vertex vertex)
                {
                    (blended ? transition : ordinary).Add(vertices.Count);
                    vertices.Add(vertex.Position); normals.Add(vertex.Normal);
                    uvs.Add(blended ? new Vector2(vertex.Position.x / CityEastGroundTransitionPlan.WorldRepeat,
                        (vertex.Position.z - plan.Bounds.yMin) / (2f * CityEastGroundTransitionPlan.HalfDepth)) : vertex.Uv);
                }
            }
        }

        private static bool OnSeam(Vertex vertex, float seamZ) =>
            Mathf.Abs(vertex.Position.z - seamZ) <= Epsilon;

        private static IReadOnlyList<Vertex> CreateSharedSeam(Transform church, Transform yard,
            CityEastGroundTransitionPlan plan)
        {
            if (church == null || yard == null)
                throw new InvalidOperationException("Eastern ground transition needs both terrain owners.");
            List<SeamEdge> churchEdges = Collect(church), yardEdges = Collect(yard);
            var coordinates = new List<float>();
            foreach (SeamEdge edge in churchEdges) { coordinates.Add(edge.A.Position.x); coordinates.Add(edge.B.Position.x); }
            foreach (SeamEdge edge in yardEdges) { coordinates.Add(edge.A.Position.x); coordinates.Add(edge.B.Position.x); }
            coordinates.Sort();
            var result = new List<Vertex>();
            foreach (float x in coordinates)
            {
                if (result.Count > 0 && Mathf.Abs(result[result.Count - 1].Position.x - x) <= Epsilon) continue;
                if (!TrySample(churchEdges, x, out Vertex garden) || !TrySample(yardEdges, x, out Vertex field))
                    throw new InvalidOperationException("Eastern ground edge has no matching neighbour at X=" + x);
                // One exact XYZ is shared by both visible meshes. The original
                // colliders remain untouched; a geometric mismatch beyond the
                // established surface tolerance is a plan defect, not a seam fix.
                if (Mathf.Abs(garden.Position.y - field.Position.y) > .015f)
                    throw new InvalidOperationException("Eastern shared edge departs from original collision at X=" + x);
                result.Add(new Vertex(new Vector3(x, field.Position.y, plan.SeamZ),
                    (garden.Normal + field.Normal).normalized, field.Uv));
            }
            if (result.Count < 2) throw new InvalidOperationException("Eastern ground needs a continuous shared edge.");
            return result;

            List<SeamEdge> Collect(Transform surface)
            {
                Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] positions = mesh.vertices, normals = mesh.normals;
                Vector2[] uv = mesh.uv;
                int[] indices = mesh.triangles;
                var edges = new List<SeamEdge>();
                for (int index = 0; index < indices.Length; index += 3)
                {
                    if (normals[indices[index]].y + normals[indices[index + 1]].y + normals[indices[index + 2]].y <= .6f) continue;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int a = indices[index + corner], b = indices[index + (corner + 1) % 3];
                        if (Mathf.Abs(positions[a].z - plan.SeamZ) > Epsilon ||
                            Mathf.Abs(positions[b].z - plan.SeamZ) > Epsilon ||
                            Mathf.Min(positions[a].x, positions[b].x) < plan.Bounds.xMin - Epsilon ||
                            Mathf.Max(positions[a].x, positions[b].x) > plan.Bounds.xMax + Epsilon ||
                            Mathf.Abs(positions[a].x - positions[b].x) <= Epsilon) continue;
                        edges.Add(new SeamEdge(new Vertex(positions[a], normals[a], uv[a]),
                            new Vertex(positions[b], normals[b], uv[b])));
                    }
                }
                return edges;
            }
        }

        private static bool TrySample(IReadOnlyList<SeamEdge> edges, float x, out Vertex sample)
        {
            foreach (SeamEdge edge in edges)
            {
                float a = edge.A.Position.x, b = edge.B.Position.x;
                if (x < Mathf.Min(a, b) - Epsilon || x > Mathf.Max(a, b) + Epsilon) continue;
                sample = Vertex.Lerp(edge.A, edge.B, Mathf.Clamp01((x - a) / (b - a)));
                return true;
            }
            sample = default;
            return false;
        }

        private static void StitchSeam(List<Vertex> triangle, IReadOnlyList<Vertex> seam, float seamZ)
        {
            if (!OnSeam(triangle[0], seamZ) && !OnSeam(triangle[1], seamZ) && !OnSeam(triangle[2], seamZ)) return;
            var stitched = new List<Vertex>(triangle.Count + 8);
            bool touched = false;
            for (int index = 0; index < triangle.Count; index++)
            {
                Vertex a = triangle[index], b = triangle[(index + 1) % triangle.Count];
                bool aOnSeam = OnSeam(a, seamZ), bOnSeam = OnSeam(b, seamZ);
                if (aOnSeam)
                {
                    touched = true;
                    foreach (Vertex shared in seam)
                        if (Mathf.Abs(shared.Position.x - a.Position.x) <= Epsilon)
                        { a = new Vertex(shared.Position, shared.Normal, a.Uv); break; }
                }
                stitched.Add(a);
                if (!aOnSeam || !bOnSeam) continue;
                bool forward = b.Position.x > a.Position.x;
                for (int ordinal = 0; ordinal < seam.Count; ordinal++)
                {
                    Vertex shared = seam[forward ? ordinal : seam.Count - 1 - ordinal];
                    if (shared.Position.x <= Mathf.Min(a.Position.x, b.Position.x) + Epsilon ||
                        shared.Position.x >= Mathf.Max(a.Position.x, b.Position.x) - Epsilon) continue;
                    float t = (shared.Position.x - a.Position.x) / (b.Position.x - a.Position.x);
                    stitched.Add(new Vertex(shared.Position, shared.Normal, Vector2.Lerp(a.Uv, b.Uv, t)));
                }
            }
            if (!touched) return;
            triangle.Clear(); triangle.AddRange(stitched);
        }

        private readonly struct SeamEdge
        {
            internal SeamEdge(Vertex a, Vertex b) { A = a; B = b; }
            internal Vertex A { get; }
            internal Vertex B { get; }
        }

        private static void Split(List<Vertex> source, float edge, bool keepAbove,
            List<Vertex> inside, List<Vertex> outside)
        {
            inside.Clear(); outside.Clear();
            if (source.Count == 0) return;
            Vertex previous = source[source.Count - 1];
            float previousDistance = (previous.Position.z - edge) * (keepAbove ? 1f : -1f);
            foreach (Vertex current in source)
            {
                float distance = (current.Position.z - edge) * (keepAbove ? 1f : -1f);
                if ((distance > Epsilon && previousDistance < -Epsilon) ||
                    (distance < -Epsilon && previousDistance > Epsilon))
                {
                    Vertex intersection = Vertex.Lerp(previous, current, previousDistance / (previousDistance - distance));
                    inside.Add(intersection); outside.Add(intersection);
                }
                if (distance >= -Epsilon) inside.Add(current);
                if (distance <= Epsilon) outside.Add(current);
                previous = current; previousDistance = distance;
            }
        }

        private readonly struct Vertex
        {
            internal Vertex(Vector3 position, Vector3 normal, Vector2 uv) { Position = position; Normal = normal; Uv = uv; }
            internal Vector3 Position { get; }
            internal Vector3 Normal { get; }
            internal Vector2 Uv { get; }
            internal static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex(
                Vector3.Lerp(a.Position, b.Position, t), Vector3.Lerp(a.Normal, b.Normal, t).normalized, Vector2.Lerp(a.Uv, b.Uv, t));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResource() => cachedTexture = null;
    }
}
