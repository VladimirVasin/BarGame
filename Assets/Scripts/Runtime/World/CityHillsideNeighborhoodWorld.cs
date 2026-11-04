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
                BuildHouse(court, house, layout.Seed);
                CityHillsideHousePresentation.BuildApron(court, paving, house);
            }
            CityTerracePilotWorld.BuildHillsideShortcuts(root, access);
        }

        private static void BuildHouse(Transform parent, CityHillsideBuilding house, int seed)
        {
            Transform placed = new GameObject("Building " + house.Lot.Cell).transform;
            placed.SetParent(parent, false);
            var building = CityBuildingPrototypeWorldBuilder.BuildCity(placed, house.Lot, seed, .9f);
            CityHillsideHousePresentation.Build(placed, house, building);
        }

        private static void BuildGround(Transform parent, CityHillsideGroundGrid grid)
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
                uv[i] = new Vector2(p.x, p.z) * .35f;
                if (col + 1 == columns || row + 1 == grid.Rows) continue;
                var surface = grid.CellSurface(col, row);
                if (surface != CityHillsideGroundGrid.Surface.Cut)
                    grid.AppendCellTriangles(surface == CityHillsideGroundGrid.Surface.Concrete ? concrete : earth, col, row);
            }

            var mesh = new Mesh { name = "Hillside banks and door aprons", subMeshCount = 2 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.SetTriangles(earth, 0); mesh.SetTriangles(concrete, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("Terraced ground and courts");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { RuntimePrimitiveFactory.DefaultMaterial,
                CityPortAssetProvider.GetSurfaceMaterial("Concrete") };
            CityExteriorAppearance.ApplyGroundSurface(renderer, grid.Road.Bank == CityTerraceTransportBank.East
                ? new Color(.51f, .53f, .43f) : new Color(.49f, .46f, .40f), 0);
            GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, 1, true);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var kinds = new FootstepGroundKind[(earth.Count + concrete.Count) / 3];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = i < earth.Count / 3 ? FootstepGroundKind.Soil : FootstepGroundKind.Concrete;
            FootstepGround.Stamp(go, FootstepGroundKind.Soil).SetTriangleKinds(kinds);

        }
    }
}
