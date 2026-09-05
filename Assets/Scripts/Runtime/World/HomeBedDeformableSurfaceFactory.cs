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
    /// Clones one imported Blender grid for the live mattress or pillow.
    /// The import step preserves/reorders the independent top quads; runtime
    /// only deforms that topology and gives its bounds room for the dent.
    /// </summary>
    internal static class HomeBedDeformableSurfaceFactory
    {
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
            HomeAuthoredPart authored = null;
            foreach (HomeAuthoredPart candidate in HomeInteriorModelLibrary.Load().Parts)
                if (candidate.role == "grid" && Vector3.Distance(candidate.Size, size) <= 0.001f)
                { authored = candidate; break; }
            if (authored == null)
                throw new System.InvalidOperationException($"No authored bed grid matches '{name}' ({size}).");
            // The exported mesh owns tessellation. Re-rounding a half-cell in
            // float here can disagree with Blender's double precision.
            int columns = authored.grid_columns;
            int rows = authored.grid_rows;
            Mesh mesh = Object.Instantiate(authored.mesh);
            mesh.name = $"{name} Deformable Mesh";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.MarkDynamic();

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
            HomeAuthoredVisualFactory.ApplySurface(
                renderer,
                authored,
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

    }
}
