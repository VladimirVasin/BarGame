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
        public const float MetersPerTile = 5f;

        public static MountainRoadTerrainMeshes Create(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadValidator.ValidateOrThrow(plan);
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
                    uvs.Add(new Vector2(
                        worldX / MetersPerTile,
                        worldZ / MetersPerTile));
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

            return new MountainRoadTerrainMeshes(
                CreateMesh("Mountain Road Soil", vertices, uvs, soilTriangles),
                CreateMesh("Mountain Road Snow", vertices, uvs, snowTriangles));
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
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
