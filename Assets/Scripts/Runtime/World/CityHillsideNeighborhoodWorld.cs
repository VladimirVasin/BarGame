using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityHillsideNeighborhoodWorld
    {
        // One terrain skin per bank. Breaks follow each porch and stair opening,
        // so physics, paving and bare earth use the same triangles at their seams.
        internal static IEnumerator BuildSteps(Transform root, CityLayout layout, bool incremental)
        {
            var streets = CityHillsideStreetPlan.ForLayout(layout);
            if (streets.Routes.Count == 0) yield break;
            var district = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var access = CityHillsideAccessPlan.ForLayout(layout);
            foreach (var grid in CityHillsideGroundGrid.ForSurface(layout))
            {
                var road = grid.Road;
                var bank = new GameObject(road.Bank == CityTerraceTransportBank.East
                    ? "Balcony Courts" : "Stepped Residences").transform;
                bank.SetParent(root, false);
                foreach (var house in district.Buildings)
                {
                    if (house.Bank != road.Bank) continue;
                    BuildHouse(bank, layout, house);
                    if (incremental) yield return new CompositionStep("ridge_bank_house", .43f);
                }
                BuildGround(bank, grid);
                if (incremental) yield return new CompositionStep("ridge_bank_ground", .45f);
            }
            var court = new GameObject("Shared passage court").transform;
            court.SetParent(root, false);
            var paving = Resources.Load<GameObject>(CityTerracePilotWorld.ResourceFolder + "TerraceCourt");
            foreach (var house in district.SharedCourtBuildings)
            {
                if (!house.Lot.IsPlayerHome) BuildHouse(court, layout, house);
                CityHillsideHousePresentation.BuildApron(court, paving, house);
                if (incremental) yield return new CompositionStep("ridge_shared_court", .47f);
            }
            CityTerracePilotWorld.BuildHillsideShortcuts(root, access);
        }

        internal static void BuildHouse(Transform parent, CityLayout layout, CityHillsideBuilding house)
        {
            Transform placed = new GameObject("Building " + house.Lot.Cell).transform;
            placed.SetParent(parent, false);
            var building = CityBuildingPrototypeWorldBuilder.BuildCity(placed, house.Lot, layout.Seed, FoundationDepth(layout, house), layout);
            foreach (Transform part in placed)
                if (part.name.StartsWith(CityBuildingPrototypeWorldBuilder.FoundationObjectName, System.StringComparison.Ordinal))
                    RuntimePrimitiveFactory.ProfileStructuralBox(part.gameObject, .04f);
            CityHillsideHousePresentation.Build(placed, house, building);
        }

        internal static float FoundationDepth(CityLayout layout, CityHillsideBuilding house)
        {
            float depth = CityHillsideGroundGrid.MinimumFoundationDepth;
            foreach (var grid in CityHillsideGroundGrid.ForLayout(layout))
                if (grid.Road.Bank == house.Bank) depth = grid.FoundationDepth(house);
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            if (quarter.Links.Count > 0 && quarter.TerrainBounds.Overlaps(house.FoundationBounds))
                depth = Mathf.Max(depth, CityHillsideQuarterGroundGrid.ForLayout(layout).FoundationDepth(house));
            return depth;
        }

        internal static void BuildGround(Transform parent, CityHillsideGroundGrid grid, Rect? retained = null)
        {
            int columns = grid.Columns;
            var vertices = new Vector3[columns * grid.Rows];
            var uv = new Vector2[vertices.Length];
            var earth = new List<int>();
            var concrete = new List<int>();
            var access = CityHillsideAccessPlan.ForLayout(grid.Layout);
            var stairCuts = new CityHillsideStairCut();
            System.Func<int, int, Vector3> vertex = grid.Vertex;
            System.Func<int, int, bool> isCut = (col, row) => grid.CellSurface(col, row) == CityHillsideGroundGrid.Surface.Cut;
            for (int row = 0; row < grid.Rows; row++)
            for (int col = 0; col < columns; col++)
            {
                int i = row * columns + col;
                Vector3 p = grid.Vertex(col, row);
                vertices[i] = p;
                uv[i] = new Vector2(p.x, p.z) / CityExteriorAppearance.GroundTextureTileSize;
                if (col + 1 == columns || row + 1 == grid.Rows) continue;
                var surface = grid.CellSurface(col, row);
                Vector3 far = retained.HasValue || surface == CityHillsideGroundGrid.Surface.Cut
                    ? grid.Vertex(col + 1, row + 1) : default;
                if (retained.HasValue && !retained.Value.Overlaps(Rect.MinMaxRect(p.x, p.z, far.x, far.z))) continue;
                if (surface == CityHillsideGroundGrid.Surface.Cut)
                {
                    Vector2 center = new((p.x + far.x) * .5f, (p.z + far.z) * .5f);
                    foreach (var stair in access.AllStairs)
                    {
                        if (stair == access.SharedCourt || stair.Bank != grid.Road.Bank || !stair.Bounds.Contains(center)) continue;
                        stairCuts.AddCell(col, row, columns, grid.Rows, vertex, isCut, stair);
                        break;
                    }
                    continue;
                }
                grid.AppendCellTriangles(surface == CityHillsideGroundGrid.Surface.Concrete ? concrete : earth, col, row);
            }

            var mesh = CityHillsidePerimeterSurface.Build(grid.Layout, "Hillside banks and door aprons",
                vertices, uv, true, earth, concrete);
            CityPawnshopBenchTerraceWorld.CutGroundMesh(grid.Layout, mesh, stairCuts);
            CityMountainTalusAppearance.BakeGround(mesh, grid.Layout);
            var go = new GameObject("Terraced ground and courts");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var material = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.sharedMaterials = new[] { material, CityPortAssetProvider.GetSurfaceMaterial("Concrete"),
                material, material, material, material };
            CityExteriorAppearance.ApplyGroundSurface(renderer, CityHillsidePerimeterSurface.ResidentialSoil, 0);
            CityMountainTalusAppearance.ApplyGround(renderer);
            GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, 1, true);
            // The same two-metre PortConcrete tiles continue onto the imported porch.
            CityHillsidePerimeterSurface.SetTilePitch(renderer, 1, CityExteriorAppearance.GroundTextureTileSize, 2f);
            CityExteriorAppearance.ApplyRoadSurface(renderer, 2);
            CityHillsidePerimeterSurface.ApplyVerges(renderer, 3, 4);
            CityHillsidePerimeterSurface.ApplyPaving(renderer, 5);
            CityGroundSurfaceSystem.RegisterJointScenery(renderer);
            if (!retained.HasValue) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            if (!retained.HasValue) CityHillsidePerimeterSurface.Stamp(go, mesh, FootstepGroundKind.Soil,
                FootstepGroundKind.Concrete, FootstepGroundKind.Concrete, FootstepGroundKind.Grass,
                FootstepGroundKind.Stone, FootstepGroundKind.Stone);

            stairCuts.Build(parent, retained.HasValue);
        }
    }
}
