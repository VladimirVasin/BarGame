using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideNeighborhoodWorld
    {
        // One terrain skin per bank. Breaks follow each porch and stair opening,
        // so physics, paving and bare earth use the same triangles at their seams.
        internal static void Build(Transform root, CityLayout layout)
        {
            var streets = CityHillsideStreetPlan.ForLayout(layout);
            if (streets.Routes.Count == 0) return;
            var district = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var access = CityHillsideAccessPlan.ForLayout(layout);
            foreach (var grid in CityHillsideGroundGrid.ForLayout(layout))
            {
                var road = grid.Road;
                var bank = new GameObject(road.Bank == CityTerraceTransportBank.East
                    ? "Balcony Courts" : "Stepped Residences").transform;
                bank.SetParent(root, false);
                foreach (var house in district.Buildings)
                {
                    if (house.Bank != road.Bank) continue;
                    BuildHouse(bank, house, layout.Seed);
                }
                BuildGround(bank, grid);
            }
            var court = new GameObject("Shared passage court").transform;
            court.SetParent(root, false);
            var paving = Resources.Load<GameObject>(CityTerracePilotWorld.ResourceFolder + "TerraceCourt");
            foreach (var house in district.SharedCourtBuildings)
            {
                if (!house.Lot.IsPlayerHome) BuildHouse(court, house, layout.Seed);
                CityHillsideHousePresentation.BuildApron(court, paving, house);
            }
            CityTerracePilotWorld.BuildHillsideShortcuts(root, access);
        }

        internal static void BuildHouse(Transform parent, CityHillsideBuilding house, int seed)
        {
            Transform placed = new GameObject("Building " + house.Lot.Cell).transform;
            placed.SetParent(parent, false);
            var building = CityBuildingPrototypeWorldBuilder.BuildCity(placed, house.Lot, seed, .9f);
            CityHillsideHousePresentation.Build(placed, house, building);
        }

        internal static void BuildGround(Transform parent, CityHillsideGroundGrid grid, Rect? retained = null)
        {
            int columns = grid.Columns;
            var vertices = new Vector3[columns * grid.Rows];
            var uv = new Vector2[vertices.Length];
            var earth = new List<int>();
            var concrete = new List<int>();
            for (int row = 0; row < grid.Rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int i = row * columns + col;
                Vector3 p = grid.Vertex(col, row);
                vertices[i] = p;
                uv[i] = new Vector2(p.x, p.z) / CityExteriorAppearance.GroundTextureTileSize;
                if (col + 1 == columns || row + 1 == grid.Rows) continue;
                if (retained.HasValue)
                {
                    Vector3 far = grid.Vertex(col + 1, row + 1);
                    if (!retained.Value.Overlaps(Rect.MinMaxRect(p.x, p.z, far.x, far.z))) continue;
                }
                var surface = grid.CellSurface(col, row);
                if (surface != CityHillsideGroundGrid.Surface.Cut)
                    grid.AppendCellTriangles(surface == CityHillsideGroundGrid.Surface.Concrete ? concrete : earth, col, row);
            }

            var mesh = CityHillsidePerimeterSurface.Build(grid.Layout, "Hillside banks and door aprons",
                vertices, uv, earth, concrete);
            CityPawnshopBenchTerraceWorld.CutGroundMesh(grid.Layout,mesh);
            var go = new GameObject("Terraced ground and courts");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var material = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.sharedMaterials = new[] { material, CityPortAssetProvider.GetSurfaceMaterial("Concrete"),
                material, material, material };
            CityExteriorAppearance.ApplyGroundSurface(renderer, CityHillsidePerimeterSurface.ResidentialSoil, 0);
            GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, 1, true);
            // The same two-metre PortConcrete tiles continue onto the imported porch.
            CityHillsidePerimeterSurface.SetTilePitch(renderer, 1, CityExteriorAppearance.GroundTextureTileSize, 2f);
            CityExteriorAppearance.ApplyRoadSurface(renderer, 2);
            CityHillsidePerimeterSurface.ApplyVerges(renderer, 3, 4);
            if (!retained.HasValue) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            if (!retained.HasValue) CityHillsidePerimeterSurface.Stamp(go, mesh, FootstepGroundKind.Soil,
                FootstepGroundKind.Concrete, FootstepGroundKind.Concrete, FootstepGroundKind.Grass, FootstepGroundKind.Stone);

        }
    }
}
