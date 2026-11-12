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
                    BuildHouse(bank, layout, house);
                }
                BuildGround(bank, grid);
            }
            var court = new GameObject("Shared passage court").transform;
            court.SetParent(root, false);
            var paving = Resources.Load<GameObject>(CityTerracePilotWorld.ResourceFolder + "TerraceCourt");
            foreach (var house in district.SharedCourtBuildings)
            {
                if (!house.Lot.IsPlayerHome) BuildHouse(court, layout, house);
                CityHillsideHousePresentation.BuildApron(court, paving, house);
            }
            CityTerracePilotWorld.BuildHillsideShortcuts(root, access);
        }

        internal static void BuildHouse(Transform parent, CityLayout layout, CityHillsideBuilding house)
        {
            Transform placed = new GameObject("Building " + house.Lot.Cell).transform;
            placed.SetParent(parent, false);
            var building = CityBuildingPrototypeWorldBuilder.BuildCity(placed, house.Lot, layout.Seed, FoundationDepth(layout, house));
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
                vertices, uv, true, earth, concrete);
            CityPawnshopBenchTerraceWorld.CutGroundMesh(grid.Layout,mesh);
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

            if (grid.Road.Bank == CityTerraceTransportBank.East)
                CloseEastStairBank(parent, grid, retained);
        }

        private static void CloseEastStairBank(Transform parent, CityHillsideGroundGrid grid, Rect? retained)
        {
            foreach (var stair in CityHillsideAccessPlan.ForLayout(grid.Layout).Shortcuts)
            {
                if (stair.Bank != grid.Road.Bank || stair.LowerLane != CityHillsideStreetRoute.LaneCount - 2 ||
                    retained.HasValue && !retained.Value.Overlaps(stair.Bounds)) continue;

                // The cut removes the terrain's top faces. Continue the actual
                // western rim down beside the stair so the bank has a solid side.
                float bottom = stair.RouteWorld[0].y - 1f;
                var points = new List<Vector3>();
                var uv = new List<Vector2>();
                var indices = new List<int>();
                foreach (Vector3 top in grid.WestStairCutEdge(stair.Bounds))
                {
                    int index = points.Count;
                    points.Add(top);
                    points.Add(new Vector3(top.x, Mathf.Min(bottom, top.y - .1f), top.z));
                    uv.Add(new Vector2(top.z, top.y) / CityExteriorAppearance.GroundTextureTileSize);
                    uv.Add(new Vector2(top.z, points[index + 1].y) / CityExteriorAppearance.GroundTextureTileSize);
                    if (index == 0) continue;
                    indices.AddRange(new[] { index - 2, index, index - 1, index, index + 1, index - 1 });
                }
                var mesh = new Mesh { name = "East stair soil cut" };
                mesh.SetVertices(points);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                var bank = new GameObject(mesh.name);
                bank.transform.SetParent(parent, false);
                bank.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = bank.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                CityExteriorAppearance.ApplyGroundSurface(renderer, CityHillsidePerimeterSurface.ResidentialSoil);
                bank.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
                if (!retained.HasValue) bank.AddComponent<MeshCollider>().sharedMesh = mesh;
                return;
            }
        }
    }
}
