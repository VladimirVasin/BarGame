using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed class MountainRoadTerrainMeshes
    {
        internal MountainRoadTerrainMeshes(Mesh soil, Mesh snow)
        {
            Soil = soil;
            Snow = snow;
        }

        public Mesh Soil { get; }
        public Mesh Snow { get; }
    }

    public static class MountainRoadTerrainMeshFactory
    {
        public const float GridSpacing = 1.6f;

        /// <summary>
        /// Soil and snow are cut out of one vertex grid and therefore share
        /// one UV array, so the two recipes must agree on the pitch. Both
        /// are read from the packaged contract rather than restated here.
        /// </summary>
        public static float MetersPerTile
        {
            get
            {
                float soil = MountainRoadSurfaceAppearance.GetRecipe(
                    MountainRoadSurfaceKind.ForestFloor).MetersPerTile;
                float snow = MountainRoadSurfaceAppearance.GetRecipe(
                    MountainRoadSurfaceKind.WindSnow).MetersPerTile;
                if (!Mathf.Approximately(soil, snow))
                {
                    throw new InvalidOperationException(
                        "The mountain forest floor and wind snow share one " +
                        "vertex grid, so their sheets must share one metre " +
                        $"pitch; got {soil} and {snow}.");
                }

                return soil;
            }
        }

        public static MountainRoadTerrainMeshes Create(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadValidator.ValidateOrThrow(plan);
            float tile = MetersPerTile;
            Rect bounds = plan.TerrainBoundsXZ;
            int xSteps = Mathf.CeilToInt(bounds.width / GridSpacing);
            int zSteps = Mathf.CeilToInt(bounds.height / GridSpacing);
            float xPitch = bounds.width / xSteps;
            float zPitch = bounds.height / zSteps;
            var vertices = new List<Vector3>((xSteps + 1) * (zSteps + 1));
            var uvs = new List<Vector2>(vertices.Capacity);
            for (int z = 0; z <= zSteps; z++)
            {
                float worldZ = Mathf.Lerp(bounds.yMin, bounds.yMax, z / (float)zSteps);
                for (int x = 0; x <= xSteps; x++)
                {
                    float worldX = Mathf.Lerp(bounds.xMin, bounds.xMax, x / (float)xSteps);
                    float y = MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plan.Plateau,
                        new Vector2(worldX, worldZ));
                    vertices.Add(new Vector3(worldX, y, worldZ));
                    uvs.Add(new Vector2(worldX / tile, worldZ / tile));
                }
            }

            var soilTriangles = new List<int>(xSteps * zSteps * 6);
            var snowTriangles = new List<int>(xSteps * zSteps);
            int row = xSteps + 1;
            for (int z = 0; z < zSteps; z++)
            {
                for (int x = 0; x < xSteps; x++)
                {
                    int a = z * row + x;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    Vector3 center =
                        (vertices[a] + vertices[b] + vertices[c] + vertices[d]) *
                        0.25f;
                    IList<int> target = IsSnowCell(plan, center)
                        ? snowTriangles
                        : soilTriangles;
                    target.Add(a);
                    target.Add(c);
                    target.Add(b);
                    target.Add(b);
                    target.Add(c);
                    target.Add(d);
                }
            }

            // Soil and snow are two cuts of one surface, so their normals
            // are averaged once over BOTH triangle sets. Letting each mesh
            // recalculate its own would give the vertices along the snow
            // line two different normals for the same ground and light the
            // boundary as a seam that is not there.
            List<Vector3> normals = CreateSharedNormals(
                vertices,
                soilTriangles,
                snowTriangles);
            return new MountainRoadTerrainMeshes(
                CreateMesh(
                    "Mountain Road Soil",
                    vertices,
                    uvs,
                    normals,
                    soilTriangles),
                CreateMesh(
                    "Mountain Road Snow",
                    vertices,
                    uvs,
                    normals,
                    snowTriangles));
        }

        private static List<Vector3> CreateSharedNormals(
            List<Vector3> vertices,
            List<int> soilTriangles,
            List<int> snowTriangles)
        {
            var normals = new List<Vector3>(vertices.Count);
            for (int index = 0; index < vertices.Count; index++)
            {
                normals.Add(Vector3.zero);
            }

            AccumulateFaceNormals(vertices, soilTriangles, normals);
            AccumulateFaceNormals(vertices, snowTriangles, normals);
            for (int index = 0; index < normals.Count; index++)
            {
                Vector3 normal = normals[index];
                normals[index] = normal.sqrMagnitude > 1e-12f
                    ? normal.normalized
                    : Vector3.up;
            }

            return normals;
        }

        private static void AccumulateFaceNormals(
            List<Vector3> vertices,
            List<int> triangles,
            List<Vector3> normals)
        {
            for (int index = 0; index + 2 < triangles.Count; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];

                // Unweighted cross products, so a vertex takes the area-
                // weighted average Unity's own RecalculateNormals produces.
                Vector3 face = Vector3.Cross(
                    vertices[b] - vertices[a],
                    vertices[c] - vertices[a]);
                normals[a] += face;
                normals[b] += face;
                normals[c] += face;
            }
        }

        private static bool IsSnowCell(
            MountainRoadPlan plan,
            Vector3 center)
        {
            float snowLine = plan.Route.Start.y +
                             plan.Route.ElevationGain * 0.72f;
            float horizontalProgress = Mathf.InverseLerp(
                plan.Route.Start.x,
                plan.Route.End.x,
                center.x);
            if (center.y < snowLine - 0.3f || horizontalProgress < 0.62f)
            {
                return false;
            }

            float brokenEdge = Mathf.Sin(center.x * 0.67f) +
                               Mathf.Cos(center.z * 0.49f);
            return center.y + brokenEdge * 0.32f > snowLine;
        }

        private static Mesh CreateMesh(
            string name,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<Vector3> normals,
            List<int> triangles)
        {
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertices.Count > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
