using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A continuous church terrain skin, a solid outer skirt and imported
    /// transparent iron on its genuinely closed edges. The collider uses
    /// the rendered terrain mesh and the same authored fence spans.
    /// </summary>
    public static class CityChurchGroundWorldBuilder
    {
        public const string ObjectName = "Church Ground";
        public const string FenceObjectName = "Church Garden Boundary";
        public const float TerrainBottomDrop = 0.32f;
        public const float MinimumSlabHeight = 0.32f;
        private const float Tolerance = 0.001f;
        private static readonly Color GardenGrass =
            new Color(0.20f, 0.26f, 0.17f);

        public static GameObject Build(Transform parent, CityLayout layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            CityChurchPlan church = CityChurchPlanner.Create(layout);
            if (church == null)
            {
                return null;
            }

            GameObject ground = CityTerrainSurfaceWorldBuilder.Build(
                ObjectName,
                parent,
                layout,
                CitySurfaceKind.ChurchGround,
                GardenGrass,
                false,
                CityParkSurfaceAppearance.GetRecipe(CityParkSurfaceKind.Lawn)
                    .MetersPerTile);
            CityParkSurfaceAppearance.ApplyCombined(
                ground.GetComponent<Renderer>(),
                CityParkSurfaceKind.Lawn,
                GardenGrass);
            CloseTerrainSkirt(ground, layout, church.Grounds);
            BuildFence(ground.transform,
                CityChurchGroundPlan.CreateFenceSpans(layout, church));
            return ground;
        }

        private static void CloseTerrainSkirt(
            GameObject ground,
            CityLayout layout,
            Rect bounds)
        {
            Mesh mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            var vertices = new List<Vector3>(mesh.vertices);
            var normals = new List<Vector3>(mesh.normals);
            var uvs = new List<Vector2>(mesh.uv);
            var triangles = new List<int>(mesh.triangles);
            Vector3[] top = mesh.vertices;
            float bottom = Mathf.Min(
                layout.ElevationPlan.MinimumElevation - TerrainBottomDrop,
                mesh.bounds.min.y - MinimumSlabHeight);
            AppendSkirt(top, false, bounds.xMin, bottom, Vector3.left,
                vertices, normals, uvs, triangles);
            AppendSkirt(top, false, bounds.xMax, bottom, Vector3.right,
                vertices, normals, uvs, triangles);
            AppendSkirt(top, true, bounds.yMin, bottom, Vector3.back,
                vertices, normals, uvs, triangles);
            AppendSkirt(top, true, bounds.yMax, bottom, Vector3.forward,
                vertices, normals, uvs, triangles);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            MeshCollider collider = ground.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            mesh.UploadMeshData(false);
        }

        private static void AppendSkirt(
            IReadOnlyList<Vector3> top,
            bool horizontal,
            float coordinate,
            float bottom,
            Vector3 normal,
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> triangles)
        {
            var edge = new SortedDictionary<float, Vector3>();
            for (int index = 0; index < top.Count; index++)
            {
                Vector3 point = top[index];
                if (Mathf.Abs((horizontal ? point.z : point.x) -
                    coordinate) < Tolerance)
                {
                    edge[horizontal ? point.x : point.z] = point;
                }
            }

            bool hasPrevious = false;
            Vector3 previous = default;
            float pitch = 1f / CityParkSurfaceAppearance
                .GetRecipe(CityParkSurfaceKind.Lawn).MetersPerTile;
            foreach (Vector3 point in edge.Values)
            {
                if (hasPrevious)
                {
                    int first = vertices.Count;
                    vertices.Add(previous);
                    vertices.Add(point);
                    vertices.Add(new Vector3(point.x, bottom, point.z));
                    vertices.Add(new Vector3(previous.x, bottom, previous.z));
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector3 vertex = vertices[first + corner];
                        normals.Add(normal);
                        uvs.Add(new Vector2(horizontal ? vertex.x : vertex.z,
                            vertex.y) * pitch);
                    }

                    bool forward = Vector3.Dot(Vector3.Cross(
                        vertices[first + 1] - vertices[first],
                        vertices[first + 2] - vertices[first]), normal) > 0f;
                    triangles.Add(first);
                    triangles.Add(first + (forward ? 1 : 2));
                    triangles.Add(first + (forward ? 2 : 1));
                    triangles.Add(first);
                    triangles.Add(first + (forward ? 2 : 3));
                    triangles.Add(first + (forward ? 3 : 2));
                }

                previous = point;
                hasPrevious = true;
            }
        }

        private static void BuildFence(
            Transform parent,
            IReadOnlyList<CityChurchGroundFenceSpan> spans)
        {
            if (spans.Count == 0)
            {
                return;
            }

            CityMiscAssetProvider provider = CityMiscAssetProvider.LoadOrThrow();
            Mesh post = provider.GetPartOrThrow(
                CityMiscKind.CemeteryFencePost, 0, 0).Mesh;
            Mesh rail = provider.GetPartOrThrow(
                CityMiscKind.CemeteryFenceRail, 0, 0).Mesh;
            var placements = new List<RuntimeMeshPlacement>();
            var posts = new HashSet<Vector3>();
            Transform root = new GameObject(FenceObjectName).transform;
            root.SetParent(parent, false);
            for (int index = 0; index < spans.Count; index++)
            {
                CityChurchGroundFenceSpan span = spans[index];
                AppendPost(post, span.First, span.FirstTopY, posts, placements);
                AppendPost(post, span.Second, span.SecondTopY, posts, placements);
                AppendRail(rail, span, 0.42f, placements);
                AppendRail(rail, span, 0.90f, placements);

                Vector3 delta = span.Second - span.First;
                Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
                float bottom = Mathf.Min(span.First.y, span.Second.y);
                float top = Mathf.Max(span.FirstTopY, span.SecondTopY);
                var collision = new GameObject($"Garden Fence Collision {index:00}");
                collision.transform.SetParent(root, false);
                collision.transform.localPosition = new Vector3(
                    (span.First.x + span.Second.x) * 0.5f,
                    (bottom + top) * 0.5f,
                    (span.First.z + span.Second.z) * 0.5f);
                collision.transform.localRotation =
                    Quaternion.FromToRotation(Vector3.right, horizontal.normalized);
                collision.AddComponent<BoxCollider>().size = new Vector3(
                    horizontal.magnitude, top - bottom,
                    CityChurchGroundPlan.FenceThickness);
            }

            RuntimePrimitiveFactory.CreateCombinedMeshes(
                "Garden Iron Fence", root, placements,
                new Color(0.16f, 0.17f, 0.15f), false);
        }

        private static void AppendPost(
            Mesh mesh,
            Vector3 ground,
            float topY,
            ISet<Vector3> used,
            ICollection<RuntimeMeshPlacement> placements)
        {
            if (used.Add(ground))
            {
                placements.Add(new RuntimeMeshPlacement(mesh, ground,
                    Quaternion.identity,
                    new Vector3(1f, (topY - ground.y) / 1.48f, 1f)));
            }
        }

        private static void AppendRail(
            Mesh mesh,
            CityChurchGroundFenceSpan span,
            float fraction,
            ICollection<RuntimeMeshPlacement> placements)
        {
            Vector3 first = span.First;
            first.y = Mathf.Lerp(first.y, span.FirstTopY, fraction);
            Vector3 second = span.Second;
            second.y = Mathf.Lerp(second.y, span.SecondTopY, fraction);
            Vector3 delta = second - first;
            Quaternion rotation = Quaternion.FromToRotation(
                Vector3.right, delta.normalized);
            placements.Add(new RuntimeMeshPlacement(mesh,
                (first + second) * 0.5f - rotation * Vector3.up * 0.06f,
                rotation, new Vector3(delta.magnitude, 1f, 1f)));
        }
    }
}
