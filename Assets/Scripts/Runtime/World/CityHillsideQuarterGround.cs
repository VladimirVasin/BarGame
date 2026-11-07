using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideQuarterGround
    {
        // Same terrain skin as the adjacent switchbacks. All stair openings and
        // existing bank break lines are vertices in the visual and physical mesh.
        public static void Build(Transform parent, CityLayout layout, Rect? retained = null)
        {
            var plan = CityHillsideQuarterPlan.ForLayout(layout);
            if (plan.Links.Count == 0) return;
            var grid = CityHillsideQuarterGroundGrid.ForLayout(layout);
            int columns = grid.Columns;
            int gridSize = columns * grid.Rows;
            var vertices = new Vector3[gridSize * 2];
            var uv = new Vector2[vertices.Length];
            var faces = new List<int>();
            var rock = new List<int>();
            var scree = new List<int>();
            for (int row = 0; row < grid.Rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int a = row * columns + col;
                Vector3 vertex = grid.Vertex(col, row);
                Vector2 point = new(vertex.x, vertex.z);
                vertices[a] = vertices[a + gridSize] = vertex;
                uv[a] = point / CityExteriorAppearance.GroundTextureTileSize;
                var section = CityHillsideQuarterRavine.At(point.y);
                float u = Mathf.InverseLerp(section.y, section.z, point.x);
                float depth = CityHillsideQuarterRavine.Drop(point);
                uv[a + gridSize] = new Vector2(point.y,
                    point.x + (u < .5f ? depth : 2f * section.w - depth)) / CityMountainSurfaceAppearance.MetersPerTile;
                if (col + 1 == columns || row + 1 == grid.Rows) continue;
                var kind = grid.CellSurface(col, row);
                if (kind == CityHillsideQuarterGroundGrid.Surface.Cut) continue;
                Vector3 far = grid.Vertex(col + 1, row + 1);
                if (retained.HasValue && !retained.Value.Overlaps(Rect.MinMaxRect(
                    Mathf.Min(vertex.x, grid.Vertex(col, row + 1).x), point.y,
                    Mathf.Max(grid.Vertex(col + 1, row).x, far.x), far.z))) continue;
                bool stone = kind == CityHillsideQuarterGroundGrid.Surface.Rock || kind == CityHillsideQuarterGroundGrid.Surface.Scree;
                var triangles = kind == CityHillsideQuarterGroundGrid.Surface.Scree ? scree : stone ? rock : faces;
                if (stone) a += gridSize;
                int b = a + columns;
                triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
            }
            var mesh = CityHillsidePerimeterSurface.Build(layout, "Upper quarter saddle and dry rock cut",
                vertices, uv, faces, rock, scree);
            CityPawnshopBenchTerraceWorld.CutGroundMesh(layout,mesh);
            var ground = new GameObject("Shared residential hillside");
            ground.transform.SetParent(parent, false);
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ground.AddComponent<MeshRenderer>();
            var material = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.sharedMaterials = new[] { material, material, material, material, material, material };
            CityExteriorAppearance.ApplyGroundSurface(renderer, CityHillsidePerimeterSurface.ResidentialSoil, 0);
            CityMountainSurfaceAppearance.ApplyCombined(renderer, new Color(.36f, .375f, .355f), 1);
            CityCemeterySurfaceAppearance.ApplyCombined(renderer, CityCemeterySurfaceKind.Gravel,
                new Color(.31f, .305f, .28f), 2);
            CityHillsidePerimeterSurface.SetTilePitch(renderer, 2, CityMountainSurfaceAppearance.MetersPerTile,
                CityCemeterySurfaceAppearance.GetRecipe(CityCemeterySurfaceKind.Gravel).MetersPerTile);
            CityExteriorAppearance.ApplyRoadSurface(renderer, 3);
            CityHillsidePerimeterSurface.ApplyVerges(renderer, 4, 5);
            if (!retained.HasValue) ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            ground.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            if (!retained.HasValue) CityHillsidePerimeterSurface.Stamp(ground, mesh, FootstepGroundKind.Soil,
                FootstepGroundKind.Stone, FootstepGroundKind.Stone, FootstepGroundKind.Concrete,
                FootstepGroundKind.Grass, FootstepGroundKind.Stone);
            CityHillsideQuarterBridgeWorld.Build(parent, plan, retained);
        }
    }
}
