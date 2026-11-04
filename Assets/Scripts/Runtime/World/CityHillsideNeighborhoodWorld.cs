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
            foreach (var road in streets.Routes)
            {
                var bank = new GameObject(road.Bank == CityTerraceTransportBank.East
                    ? "Upper Residential Streets" : "Upper Working Streets").transform;
                bank.SetParent(root, false);
                var houses = new List<CityHillsideBuilding>();
                foreach (var house in district.Buildings)
                {
                    if (house.Bank != road.Bank) continue;
                    houses.Add(house);
                    Transform placed = new GameObject("Building " + house.Lot.Cell).transform;
                    placed.SetParent(bank, false);
                    CityBuildingPrototypeWorldBuilder.BuildCity(placed, house.Lot, layout.Seed, .9f);
                }
                BuildGround(bank, layout, road, houses, access);
            }
            CityTerracePilotWorld.BuildHillsideShortcuts(root, access);
        }

        private static void BuildGround(Transform parent, CityLayout layout, CityHillsideStreetRoute road,
            List<CityHillsideBuilding> houses, CityHillsideAccessPlan access)
        {
            Rect bounds = CityHillsideNeighborhoodPlan.TerrainBounds(road);
            var xs = new SortedSet<float>();
            var zs = new SortedSet<float>();
            for (float x = bounds.xMin; x < bounds.xMax; x += 3f) xs.Add(x);
            for (float z = bounds.yMin; z < bounds.yMax; z += 3f) zs.Add(z);
            AddBreaks(bounds);
            foreach (var house in houses)
            {
                AddBreaks(house.FoundationBounds);
                AddBreaks(house.CourtBounds);
                AddBreaks(house.ApproachBounds);
            }
            var cuts = new List<Rect>();
            foreach (var stair in access.Shortcuts)
                if (stair.Bank == road.Bank) { cuts.Add(stair.Bounds); AddBreaks(stair.Bounds); }
            for (int i = 0; i < CityHillsideStreetRoute.LaneCount; i++)
            {
                zs.Add(road.GetLaneZ(i) - CityHillsideStreetRoute.OuterHalfWidth);
                zs.Add(road.GetLaneZ(i) + CityHillsideStreetRoute.OuterHalfWidth);
            }
            var xAxis = new List<float>(xs);
            var zAxis = new List<float>(zs);
            int columns = xAxis.Count;
            var vertices = new Vector3[columns * zAxis.Count];
            var uv = new Vector2[vertices.Length];
            var earth = new List<int>();
            var concrete = new List<int>();
            for (int row = 0; row < zAxis.Count; row++)
                for (int col = 0; col < columns; col++)
                {
                    int a = row * columns + col;
                    Vector2 point = new Vector2(xAxis[col], zAxis[row]);
                    float y = CityHillsideNeighborhoodPlan.SampleBank(layout, road, point);
                    var projection = road.Path.Project(point);
                    if (projection.DistanceSquared < 49f)
                        y = Mathf.Min(y, road.Profile.SampleDistance(projection.DistanceAlong).Height - .18f);
                    foreach (var house in houses)
                    {
                        if (house.TrySampleApproach(point, out float apron))
                        {
                            // The inner metre overlaps the existing pavement; recess the
                            // apron there while matching the exact outer edge at five metres.
                            float edgeZ = house.StreetContactWorld.z - 1f;
                            y = apron - .10f * Mathf.InverseLerp(edgeZ, house.StreetContactWorld.z, point.y);
                            break;
                        }
                        Rect body = house.FoundationBounds;
                        float dx = Mathf.Max(0f, Mathf.Max(body.xMin - point.x, point.x - body.xMax));
                        float dz = Mathf.Max(0f, Mathf.Max(body.yMin - point.y, point.y - body.yMax));
                        float shoulder = Mathf.Max(dx, dz);
                        if (shoulder < 1.3f)
                            y = Mathf.Lerp(house.DoorWorld.y - .10f, y, shoulder / 1.3f);
                    }
                    vertices[a] = new Vector3(point.x, y, point.y);
                    uv[a] = point * .35f;
                    if (col + 1 >= columns || row + 1 >= zAxis.Count) continue;
                    var centre = new Vector2((point.x + xAxis[col + 1]) * .5f, (point.y + zAxis[row + 1]) * .5f);
                    bool cut = false;
                    foreach (var area in cuts) if (area.Contains(centre)) { cut = true; break; }
                    if (cut) continue;
                    List<int> faces = earth;
                    foreach (var house in houses)
                        if (house.TrySampleApproach(centre, out _)) { faces = concrete; break; }
                    int b = a + columns;
                    faces.Add(a); faces.Add(b); faces.Add(a + 1);
                    faces.Add(a + 1); faces.Add(b); faces.Add(b + 1);
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
            CityExteriorAppearance.ApplyGroundSurface(renderer, road.Bank == CityTerraceTransportBank.East
                ? new Color(.51f, .53f, .43f) : new Color(.49f, .46f, .40f), 0);
            GroundAuthoredSurfaceAppearance.Apply(renderer, GroundSurfaceKind.Concrete, 1, true);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var kinds = new FootstepGroundKind[(earth.Count + concrete.Count) / 3];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = i < earth.Count / 3 ? FootstepGroundKind.Soil : FootstepGroundKind.Concrete;
            FootstepGround.Stamp(go, FootstepGroundKind.Soil).SetTriangleKinds(kinds);

            void AddBreaks(Rect rect)
            {
                xs.Add(rect.xMin); xs.Add(rect.xMax); zs.Add(rect.yMin); zs.Add(rect.yMax);
            }
        }
    }
}
