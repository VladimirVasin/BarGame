using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityWorldBuilder
    {
        private static IEnumerator BuildRidgeSteps(Transform parent, CityLayout layout,
            CityDistrictStreamingPlan partition, Action<CityWorldResult> completed)
        {
            var root = new GameObject("Generated Ridge").transform;
            root.SetParent(parent, false);
            CityGroundSurfaceSystem ground = CityGroundSurfaceSystem.Begin(root, talusLayout: layout);
            var streets = CityStreetSurfacePlanner.Create(layout);
            yield return RuntimeComposition.Range(CityStreetSurfaceWorldBuilder.BuildSteps(
                "Ridge Street Surface", root, layout, streets,
                include: partition.IncludeRidgeStreet, incremental: true), .10f, .22f);
            CityStreetSurfaceWorldBuilder.BuildPaint("Pawnshop Pedestrian Crossings", root,
                CityPawnshopCrosswalkPlan.ForLayout(layout).Markings);
            yield return new CompositionStep("ridge_streets", .22f);

            CityMountainBoundaryPlan mountain = CityMountainBoundaryPlanner.Create(layout);
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout, mountain);
            HomeEntrance home = null;
            yield return BuildRidgeGroundAndHomes(root, layout, walkable, value => home = value, true);
            yield return new CompositionStep("ridge_terrain_and_homes", .57f);

            var shelters = new List<Collider>();
            var railRests = new List<CityRailRestSpot>();
            BuildRidgeDressing(root, layout, shelters, railRests);
            foreach (var kiosk in CityKioskSites.ForLayout(layout).Sites)
                if (kiosk.IsRidge)
                {
                    RidgeKioskWorld.Build(root, kiosk, true, out var kioskShelters);
                    shelters.AddRange(kioskShelters);
                }
            CityCoastalInfrastructureWorldBuilder.Build(root, CityCoastalInfrastructurePlan.ForLayout(layout), partition.IsRidge);
            CityWaterNetworkWorld.Build(root, CityWaterNetwork.ForLayout(layout), partition.IsRidge);
            CityResidentialEntranceLighting.Build(root);
            var vegetation = CityRidgeVegetationPlan.ForLayout(layout);
            vegetation.Build(root);
            walkable.ExcludePolygons(vegetation.Trunks);
            yield return new CompositionStep("ridge_dressing", .88f);

            ground.SetGrassContext(layout);
            yield return ground.FinalizeSurfaceSteps(true);
            completed(new CityWorldResult(root.gameObject, home, walkable, shelters, layout, mountain, railRests));
            yield return new CompositionStep("ridge_complete", 1f);
        }

        private static IEnumerator BuildRidgeGroundAndHomes(Transform parent, CityLayout layout,
            RoadWalkableArea walkable, Action<HomeEntrance> completed, bool incremental)
        {
            yield return CityHillsideNeighborhoodWorld.BuildSteps(parent, layout, incremental);
            HomeEntrance home = null;
            if (layout.HasSummitHome)
            {
                var bars = new List<BarEntrance>();
                SupermarketEntrance supermarket = null;
                BuildBuilding(parent, layout, layout.PlayerHome, layout.Seed, CityNightResources.EmissiveMaterial,
                    walkable, bars, ref home, ref supermarket);
            }
            if (incremental) yield return new CompositionStep("ridge_home", .50f);
            CityHillsideQuarterGround.Build(parent, layout);
            completed(home);
        }
    }
}
