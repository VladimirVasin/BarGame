using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Build-time description of one deformable bed surface: the private
    /// mesh, its grid layout and its rest geometry. Data-first — the runtime
    /// deformer receives this instead of reconstructing anything by name.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeBedDeformableSurface : MonoBehaviour
    {
        private Mesh mesh;
        private Vector3[] baseVertices;

        // Reused per-frame while the surface settles; GetNormals fills it
        // in place, so the shading pass allocates nothing steady-state.
        private List<Vector3> normalScratch;
        private int columns;
        private int rows;
        private float sizeX;
        private float sizeZ;
        private float restTopLocalY;
        private float maxDepth;

        public Mesh Mesh => mesh;
        public int Columns => columns;
        public int Rows => rows;
        public float SizeX => sizeX;
        public float SizeZ => sizeZ;
        public float MaxDepth => maxDepth;
        public float Thickness => restTopLocalY * 2f;
        /// <summary>
        /// The top face is built as independent per-cell quads, so light is
        /// caught facet by facet — a smooth-normal bowl on this project's
        /// noisy albedos reads as nothing at all (verified by capture).
        /// </summary>
        public int TopVertexCount => columns * rows * 4;
        public float RestTopWorldY =>
            transform.TransformPoint(
                new Vector3(0f, restTopLocalY, 0f)).y;

        public void Initialize(
            Mesh surfaceMesh,
            Vector3[] restVertices,
            int gridColumns,
            int gridRows,
            float surfaceSizeX,
            float surfaceSizeZ,
            float topLocalY,
            float maximumDepth)
        {
            mesh = surfaceMesh;
            baseVertices = restVertices;
            columns = gridColumns;
            rows = gridRows;
            sizeX = surfaceSizeX;
            sizeZ = surfaceSizeZ;
            restTopLocalY = topLocalY;
            maxDepth = maximumDepth;
        }

        public void CopyBaseVertices(Vector3[] target)
        {
            System.Array.Copy(
                baseVertices,
                target,
                baseVertices.Length);
        }

        public int VertexCount => baseVertices?.Length ?? 0;

        public Vector2 WorldToLocalPlanar(Vector3 worldPosition)
        {
            Vector3 local =
                transform.InverseTransformPoint(worldPosition);
            return new Vector2(local.x, local.z);
        }

        public bool ContainsPlanar(Vector3 worldPosition)
        {
            Vector2 local = WorldToLocalPlanar(worldPosition);
            return Mathf.Abs(local.x) <= sizeX * 0.5f &&
                   Mathf.Abs(local.y) <= sizeZ * 0.5f;
        }

        /// <summary>
        /// Writes the model's current depths into the mesh. The top face is
        /// per-cell quads; each quad corner takes the depth of its grid
        /// vertex, so the four corners of neighbouring cells stay welded in
        /// position while every facet keeps its own normal.
        /// </summary>
        public void ApplyDepths(
            HomeBedSurfaceDepressionModel model,
            Vector3[] buffer)
        {
            if (mesh == null || baseVertices == null)
            {
                return;
            }

            CopyBaseVertices(buffer);
            int vertex = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    buffer[vertex].y -=
                        model.GetDepth(column, row);
                    buffer[vertex + 1].y -=
                        model.GetDepth(column + 1, row);
                    buffer[vertex + 2].y -=
                        model.GetDepth(column, row + 1);
                    buffer[vertex + 3].y -=
                        model.GetDepth(column + 1, row + 1);
                    vertex += 4;
                }
            }

            mesh.SetVertices(buffer);
            mesh.RecalculateNormals();
            ExaggerateDentShading(buffer);
        }

        /// <summary>
        /// A 4.5 cm bowl tilts its facets under ten degrees, which is a
        /// few percent of brightness — invisible on this project's noisy
        /// albedos (verified by capture). Steepening the lateral component
        /// of dented facet normals is the PS1-legitimate cheat: geometry
        /// stays honest, the light answers it three times louder.
        /// </summary>
        private void ExaggerateDentShading(Vector3[] current)
        {
            const float lateralGain = 3.25f;
            normalScratch ??= new List<Vector3>(mesh.vertexCount);
            mesh.GetNormals(normalScratch);
            int topCount = TopVertexCount;
            bool touched = false;
            for (int index = 0; index < topCount; index++)
            {
                if (Mathf.Abs(
                        current[index].y -
                        baseVertices[index].y) <= 0.0005f)
                {
                    continue;
                }

                Vector3 normal = normalScratch[index];
                normal.x *= lateralGain;
                normal.z *= lateralGain;
                normalScratch[index] = normal.normalized;
                touched = true;
            }

            if (touched)
            {
                mesh.SetNormals(normalScratch);
            }
        }

        /// <summary>Restores the rest mesh; safe to call repeatedly.</summary>
        public void RestoreRestState()
        {
            if (mesh == null || baseVertices == null)
            {
                return;
            }

            mesh.SetVertices(baseVertices);
            mesh.RecalculateNormals();
        }
    }

    /// <summary>
    /// Builds a closed box whose top face is a vertex grid the runtime can
    /// dent. The outermost top ring never moves, so the single-quad sides
    /// and bottom stay welded to it and the rest silhouette is exactly the
    /// old primitive box. This is the project's first per-frame-written
    /// mesh; the build idioms follow CityWaterSurfaceFactory (grid, manual
    /// bounds allowance) and CityLakeBankMeshFactory (renderer wiring).
    /// </summary>
    internal static class HomeBedDeformableSurfaceFactory
    {
        // Coarse on purpose: at PS1 fidelity a dent reads through hard
        // per-facet light steps, and finer cells only smooth it back into
        // the albedo noise.
        private const float TargetCellSize = 0.14f;

        public static GameObject CreateDeformableSurface(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 size,
            Color color,
            HomeSurfaceKind surfaceKind,
            SurfaceProjection projection,
            float maxDepth)
        {
            int columns = Mathf.Max(
                2,
                Mathf.RoundToInt(size.x / TargetCellSize));
            int rows = Mathf.Max(
                2,
                Mathf.RoundToInt(size.z / TargetCellSize));
            Mesh mesh = BuildMesh(name, size, columns, rows);

            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.transform.localPosition = localPosition;
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer =
                result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                RuntimePrimitiveFactory.DefaultMaterial;
            result.AddComponent<RuntimeGeneratedMeshOwner>()
                .Initialize(mesh);
            RuntimePrimitiveFactory.SetColor(renderer, color);
            HomeSurfaceAppearance.Apply(
                renderer,
                surfaceKind,
                projection,
                color);

            // The dent moves vertices the natural bounds know nothing
            // about, and the rim welt rises past the rest top, so the
            // AABB grows a full dent depth both ways - the welt must not
            // cull itself any more than the hollow may.
            Bounds bounds = mesh.bounds;
            bounds.min -= new Vector3(0f, maxDepth, 0f);
            bounds.max += new Vector3(0f, maxDepth, 0f);
            mesh.bounds = bounds;

            HomeBedDeformableSurface surface =
                result.AddComponent<HomeBedDeformableSurface>();
            surface.Initialize(
                mesh,
                mesh.vertices,
                columns,
                rows,
                size.x,
                size.z,
                size.y * 0.5f,
                maxDepth);
            return result;
        }

        private static Mesh BuildMesh(
            string name,
            Vector3 size,
            int columns,
            int rows)
        {
            float halfX = size.x * 0.5f;
            float halfY = size.y * 0.5f;
            float halfZ = size.z * 0.5f;
            int topVertexCount = columns * rows * 4;

            // The top is independent per-cell quads (faceted shading — a
            // smooth bowl vanishes on this project's noisy albedos), then
            // 5 flat faces at 4 vertices each.
            var vertices = new Vector3[topVertexCount + 20];
            var uvs = new Vector2[vertices.Length];
            var triangles =
                new int[(columns * rows * 6) + (5 * 6)];

            float cellX = size.x / columns;
            float cellZ = size.z / rows;
            int topVertex = 0;
            int triangle = 0;
            for (int row = 0; row < rows; row++)
            {
                float z0 = -halfZ + (row * cellZ);
                float z1 = z0 + cellZ;
                float v0 = (float)row / rows;
                float v1 = (float)(row + 1) / rows;
                for (int column = 0; column < columns; column++)
                {
                    float x0 = -halfX + (column * cellX);
                    float x1 = x0 + cellX;
                    float u0 = (float)column / columns;
                    float u1 = (float)(column + 1) / columns;
                    vertices[topVertex] =
                        new Vector3(x0, halfY, z0);
                    vertices[topVertex + 1] =
                        new Vector3(x1, halfY, z0);
                    vertices[topVertex + 2] =
                        new Vector3(x0, halfY, z1);
                    vertices[topVertex + 3] =
                        new Vector3(x1, halfY, z1);
                    uvs[topVertex] = new Vector2(u0, v0);
                    uvs[topVertex + 1] = new Vector2(u1, v0);
                    uvs[topVertex + 2] = new Vector2(u0, v1);
                    uvs[topVertex + 3] = new Vector2(u1, v1);

                    // Same proven up-facing winding as the shared grid:
                    // SW, NW, SE / SE, NW, NE.
                    triangles[triangle++] = topVertex;
                    triangles[triangle++] = topVertex + 2;
                    triangles[triangle++] = topVertex + 1;
                    triangles[triangle++] = topVertex + 1;
                    triangles[triangle++] = topVertex + 2;
                    triangles[triangle++] = topVertex + 3;
                    topVertex += 4;
                }
            }

            int vertex = topVertexCount;
            AddQuad(
                vertices, uvs, triangles, ref vertex, ref triangle,
                new Vector3(-halfX, -halfY, -halfZ),
                new Vector3(halfX, -halfY, -halfZ),
                new Vector3(halfX, -halfY, halfZ),
                new Vector3(-halfX, -halfY, halfZ));
            AddQuad(
                vertices, uvs, triangles, ref vertex, ref triangle,
                new Vector3(-halfX, -halfY, halfZ),
                new Vector3(halfX, -halfY, halfZ),
                new Vector3(halfX, halfY, halfZ),
                new Vector3(-halfX, halfY, halfZ));
            AddQuad(
                vertices, uvs, triangles, ref vertex, ref triangle,
                new Vector3(halfX, -halfY, -halfZ),
                new Vector3(-halfX, -halfY, -halfZ),
                new Vector3(-halfX, halfY, -halfZ),
                new Vector3(halfX, halfY, -halfZ));
            AddQuad(
                vertices, uvs, triangles, ref vertex, ref triangle,
                new Vector3(-halfX, -halfY, -halfZ),
                new Vector3(-halfX, -halfY, halfZ),
                new Vector3(-halfX, halfY, halfZ),
                new Vector3(-halfX, halfY, -halfZ));
            AddQuad(
                vertices, uvs, triangles, ref vertex, ref triangle,
                new Vector3(halfX, -halfY, halfZ),
                new Vector3(halfX, -halfY, -halfZ),
                new Vector3(halfX, halfY, -halfZ),
                new Vector3(halfX, halfY, halfZ));

            var mesh = new Mesh
            {
                name = $"{name} Deformable Mesh",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.MarkDynamic();

            // Never UploadMeshData(true) here: it discards the CPU copy
            // and every later SetVertices would throw.
            return mesh;
        }

        private static void AddQuad(
            Vector3[] vertices,
            Vector2[] uvs,
            int[] triangles,
            ref int vertex,
            ref int triangle,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            vertices[vertex] = a;
            vertices[vertex + 1] = b;
            vertices[vertex + 2] = c;
            vertices[vertex + 3] = d;
            uvs[vertex] = new Vector2(0f, 0f);
            uvs[vertex + 1] = new Vector2(1f, 0f);
            uvs[vertex + 2] = new Vector2(1f, 1f);
            uvs[vertex + 3] = new Vector2(0f, 1f);
            // Fan (0,1,2)(0,2,3): with the corner orders used above this
            // winds every face outward (verified against the water sheet's
            // proven up-facing grid winding).
            triangles[triangle++] = vertex;
            triangles[triangle++] = vertex + 1;
            triangles[triangle++] = vertex + 2;
            triangles[triangle++] = vertex;
            triangles[triangle++] = vertex + 2;
            triangles[triangle++] = vertex + 3;
            vertex += 4;
        }
    }
}
