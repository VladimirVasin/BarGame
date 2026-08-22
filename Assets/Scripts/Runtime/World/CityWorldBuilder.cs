using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityWorldBuilder
    {
        private const float WorldChunkSize = 48f;
        private const float MinimumBuildingFoundationDepth = 0.32f;
        private const float ParkPlazaRadius = 4.25f;
        private const float ParkPlazaTopOffset = 0.10f;
        private const float ParkPlazaThickness = 0.16f;

        private static readonly Color ParkGrass =
            new Color(0.16f, 0.30f, 0.18f);
        private static readonly Color ParkPlaza =
            new Color(0.38f, 0.35f, 0.29f);
        private static readonly Color ParkTrunk =
            new Color(0.20f, 0.12f, 0.07f);
        private static readonly Color ParkCanopy =
            new Color(0.12f, 0.27f, 0.15f);
        private static readonly Color ParkBench =
            new Color(0.38f, 0.22f, 0.10f);
        private static readonly Color ParkHedge =
            new Color(0.10f, 0.24f, 0.13f);
        private static readonly Color HomeTrim = new Color(0.66f, 0.82f, 0.80f);
        private static readonly Color HomeDoor =
            new Color(0.08f, 0.20f, 0.22f);
        private static readonly Color HomeBalconyConcrete =
            new Color(0.27f, 0.31f, 0.30f);
        private static readonly Color HomeBalconyRail =
            new Color(0.18f, 0.25f, 0.25f);

        public static CityWorldResult Build(
            Transform parent,
            CityLayout layout,
            CityGenerationSettings settings,
            CityNightFixturePlan nightPlan = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            layout.ValidateOrThrow();
            if (nightPlan == null)
            {
                nightPlan = CityNightFixturePlanner.CreatePlan(layout);
            }

            Transform world = new GameObject("Generated City").transform;
            world.SetParent(parent, false);
            Material emissiveMaterial = CityNightResources.EmissiveMaterial;
            RoadFencePlan fencePlan =
                RoadFencePlanner.CreatePlan(layout);
            CityMountainBoundaryPlan mountainBoundaryPlan =
                CityMountainBoundaryPlanner.Create(layout);
            RoadWalkableArea walkableArea =
                RoadWalkableArea.FromLayout(
                    layout,
                    mountainBoundaryPlan);
            CityFringeYardPlan fringeYardPlan =
                CityFringeYardPlanner.Create(
                    layout,
                    mountainBoundaryPlan);
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    fencePlan,
                    nightPlan);
            // Planned before the ground pass: when the seacoast will
            // draw its own animated sea, the ground pass must not lay
            // the flat municipal slab under it.
            CitySeacoastPlan seacoastPlan =
                CitySeacoastPlanner.Create(layout);
            Bounds bounds = BuildGround(
                world,
                layout,
                fringeYardPlan,
                settings,
                seacoastPlan != null,
                out GameObject parkLawn,
                out CityCemeteryGroundExcavation cemeteryExcavation);
            CityTerrainSafetyWorldBuilder.Build(
                world,
                layout);
            GameObject mountainBoundaryRoot =
                CityMountainBoundaryWorldBuilder.Build(
                    world,
                    layout,
                    mountainBoundaryPlan);
            CityFringeYardWorldResult fringeYard =
                CityFringeYardWorldBuilder.Build(
                    world,
                    fringeYardPlan);
            CityMountainBackdropWorldResult mountainBackdrop =
                mountainBoundaryPlan.IsEnabled
                    ? CityMountainBackdropWorldBuilder.Build(world)
                    : null;
            BuildRoads(world, layout);
            GameObject riverRoot = CityRiverWorldBuilder.Build(
                world,
                layout,
                mountainBoundaryPlan);
            BuildElevationStructures(world, layout);
            RoadFenceWorldBuilder.Build(world, fencePlan);
            GameObject parkRoot = BuildPark(
                world,
                layout,
                parkLawn);
            GameObject districtPointOfInterestRoot =
                CityDistrictPointOfInterestWorldBuilder.Build(
                    world,
                    layout);
            CityOpenAreaDecorationPlan openAreaDecorationPlan =
                CityOpenAreaDecorationPlanner.Create(layout);
            GameObject openAreaDecorationRoot =
                CityOpenAreaWorldBuilder.Build(
                    world,
                    openAreaDecorationPlan);
            CityCemeteryPlan cemeteryPlan =
                CityCemeteryPlanner.Create(layout);
            if (cemeteryPlan != null)
            {
                CityCemeteryWorldBuilder.Build(world, cemeteryPlan);
            }

            CityLakePlan lakePlan = CityLakePlanner.Create(layout);
            if (lakePlan != null)
            {
                CityLakeWorldBuilder.Build(world, lakePlan);
            }

            if (seacoastPlan != null)
            {
                CitySeacoastWorldBuilder.Build(world, seacoastPlan);
            }

            var bars = new List<BarEntrance>(settings.BarCount);
            HomeEntrance playerHome = null;
            SupermarketEntrance supermarket = null;
            for (int i = 0; i < layout.BuildingLots.Count; i++)
            {
                BuildBuilding(
                    world,
                    layout,
                    layout.BuildingLots[i],
                    layout.Seed,
                    emissiveMaterial,
                    walkableArea,
                    bars,
                    ref playerHome,
                    ref supermarket);
            }

            GameObject decorationRoot =
                CityDecorationWorldBuilder.Build(
                    world,
                    layout,
                    decorationPlan);

            // The playground's seats hang outside the batched decoration
            // layer on purpose: they are the one piece of it that moves.
            CityPlaygroundSwingBuilder.Build(
                world,
                layout,
                decorationPlan,
                CityDecorationWorldBuilder.MasonryBatchColor,
                CityDecorationWorldBuilder.StreetBatchColor);

            return new CityWorldResult(
                world.gameObject,
                walkableArea,
                bars,
                playerHome,
                supermarket,
                fencePlan,
                parkRoot,
                districtPointOfInterestRoot,
                openAreaDecorationPlan,
                openAreaDecorationRoot,
                cemeteryPlan,
                cemeteryExcavation,
                lakePlan,
                seacoastPlan,
                decorationPlan,
                decorationRoot,
                riverRoot,
                mountainBoundaryPlan,
                mountainBoundaryRoot,
                fringeYardPlan,
                fringeYard,
                mountainBackdrop,
                bounds);
        }

        private static Bounds BuildGround(
            Transform parent,
            CityLayout layout,
            CityFringeYardPlan fringeYardPlan,
            CityGenerationSettings settings,
            bool seacoastBuildsTheSea,
            out GameObject parkLawn,
            out CityCemeteryGroundExcavation cemeteryExcavation)
        {
            Transform surfaces = new GameObject("City Surfaces").transform;
            surfaces.SetParent(parent, false);
            var lakeShore = new List<Bounds>();
            var water = new List<Bounds>();
            float terrainBottom =
                layout.ElevationPlan.MinimumElevation - 0.32f;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (CityTerrainSurfacePlan.UsesContinuousTop(surface))
                {
                    continue;
                }

                bool isWater = surface.IsWater;
                float topY = surface.PhysicalTopY;
                float height = isWater
                    ? 0.10f
                    : Mathf.Max(0.32f, topY - terrainBottom);
                float centerY = isWater
                    ? surface.DatumY - 0.17f
                    : topY - height * 0.5f;
                List<Rect> patches = CreateSurfacePatches(
                    layout,
                    surface);
                for (int patchIndex = 0;
                     patchIndex < patches.Count;
                     patchIndex++)
                {
                    Rect patch = patches[patchIndex];
                    var bounds = new Bounds(
                        new Vector3(
                            patch.center.x,
                            centerY,
                            patch.center.y),
                        new Vector3(
                            patch.width,
                            height,
                            patch.height));
                    switch (surface.Kind)
                    {
                        case CitySurfaceKind.LakeShore:
                            lakeShore.Add(bounds);
                            break;
                        case CitySurfaceKind.Water:
                            // The lake's water is drawn by
                            // CityLakeWorldBuilder, which insets it to
                            // a cut-cornered waterline and gives it a
                            // bank and a bed, and the sea's by
                            // CitySeacoastWorldBuilder, which gives it
                            // swell, a shore shelf and a foam line -
                            // the same hand-off the river already
                            // gets. The flat slab survives only where
                            // no precinct claims the water.
                            if (surface.Feature !=
                                    CityAreaFeatureKind.Lake &&
                                !(seacoastBuildsTheSea &&
                                  surface.Feature ==
                                  CityAreaFeatureKind.NorthWaterfront))
                            {
                                water.Add(bounds);
                            }

                            break;
                        case CitySurfaceKind.RiverWater:
                            break;
                    }
                }
            }

            CityTerrainSurfaceWorldBuilder.Build(
                "Active Land",
                surfaces,
                layout,
                CitySurfaceKind.BuildableGround,
                Color.white,
                true);
            parkLawn = BuildParkLawn(surfaces, layout);
            CityFringeYardGroundWorldBuilder.Build(
                surfaces,
                layout,
                fringeYardPlan);
            // The sand carries the seacoast's tide-banded sheet over
            // UVs baked at its metre pitch; the tint stays the flat
            // colour the map and the compensation were solved against.
            GameObject beach = CityTerrainSurfaceWorldBuilder.Build(
                "Beach",
                surfaces,
                layout,
                CitySurfaceKind.Beach,
                CityExteriorAppearance.BeachSand,
                false,
                CitySeacoastSurfaceAppearance.GetRecipe(
                    CitySeacoastSurfaceKind.Sand).MetersPerTile);
            if (beach != null)
            {
                CitySeacoastSurfaceAppearance.ApplyCombined(
                    beach.GetComponent<Renderer>(),
                    CitySeacoastSurfaceKind.Sand,
                    CityExteriorAppearance.BeachSand);
            }
            if (lakeShore.Count > 0)
            {
                // The shore ring takes the same trodden-clay sheet the
                // authored bank inside it carries, so the walk from the
                // street to the water crosses one ground rather than a
                // lawn meeting a ramp.
                GameObject lakeShoreGround =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Lake Shore",
                        surfaces,
                        lakeShore,
                        CityExteriorAppearance.LakeShore,
                        true,
                        CityLakeSurfaceAppearance.GetRecipe(
                            CityLakeSurfaceKind.Bank).MetersPerTile);
                CityLakeSurfaceAppearance.ApplyCombined(
                    lakeShoreGround.GetComponent<Renderer>(),
                    CityLakeSurfaceKind.Bank,
                    CityExteriorAppearance.LakeShore);
            }
            // The cemetery slab is built apart from the other
            // surfaces because it is the one ground in the city that
            // changes after the world is built: a dug grave is a
            // rectangle taken out of it and the slab comes back
            // without that rectangle. The excavation register that
            // owns those rebuilds rides on the surfaces root.
            GameObject cemeteryGround =
                CityCemeteryGroundWorldBuilder.Build(
                    surfaces,
                    layout,
                    null);
            cemeteryExcavation = cemeteryGround == null
                ? null
                : CityCemeteryGroundExcavation.Attach(
                    surfaces.gameObject,
                    layout,
                    cemeteryGround);
            BuildCombinedBoxesIfAny(
                "Water",
                surfaces,
                water,
                CityExteriorAppearance.Water);

            Rect footprint = layout.WorldXZBounds;
            float minimumY = layout.ElevationPlan.MinimumElevation - 0.5f;
            float maximumY = layout.ElevationPlan.MaximumElevation +
                             Mathf.Max(
                                 settings.MaximumBuildingHeight,
                                 settings.MaximumOrdinaryBuildingHeight) +
                             2f;
            return new Bounds(
                new Vector3(
                    footprint.center.x,
                    (minimumY + maximumY) * 0.5f,
                    footprint.center.y),
                new Vector3(
                    footprint.width,
                    maximumY - minimumY,
                    footprint.height));
        }

        /// <summary>
        /// The park's continuous ground. It is the one terrain surface
        /// that does not use the city soil sheet: trodden turf on its
        /// own metre pitch, tinted with the green the flat lawn always
        /// had. Null when the layout has no park cells.
        /// </summary>
        internal static GameObject BuildParkLawn(
            Transform parent,
            CityLayout layout)
        {
            GameObject lawn = CityTerrainSurfaceWorldBuilder.Build(
                "Park Lawn",
                parent,
                layout,
                CitySurfaceKind.ParkGround,
                ParkGrass,
                false,
                CityParkSurfaceAppearance
                    .GetRecipe(CityParkSurfaceKind.Lawn)
                    .MetersPerTile);
            if (lawn != null)
            {
                CityParkSurfaceAppearance.ApplyCombined(
                    lawn.GetComponent<Renderer>(),
                    CityParkSurfaceKind.Lawn,
                    ParkGrass);
            }

            return lawn;
        }

        private static List<Rect> CreateSurfacePatches(
            CityLayout layout,
            CitySurfaceDescriptor surface)
        {
            return CityTerrainSurfaceWorldBuilder.CreateSurfacePatches(
                layout,
                surface);
        }

        private static void BuildRoads(
            Transform parent,
            CityLayout layout)
        {
            Transform roads = new GameObject("Road Network").transform;
            roads.SetParent(parent, false);
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(layout);
            BuildOrientedSurfaceBoxesIfAny(
                "Street Surfaces",
                roads,
                plan.StreetGeometry,
                CityExteriorAppearance.Asphalt,
                true,
                CityExteriorAppearance.RoadTextureTileSize,
                CityExteriorAppearance.ApplyRoadSurface);
            BuildOrientedSurfaceBoxesIfAny(
                "Park Paths",
                roads,
                plan.ParkPathGeometry,
                CityExteriorAppearance.ParkPath,
                true,
                CityParkSurfaceAppearance
                    .GetRecipe(CityParkSurfaceKind.Path)
                    .MetersPerTile,
                renderer => CityParkSurfaceAppearance.ApplyCombined(
                    renderer,
                    CityParkSurfaceKind.Path,
                    CityExteriorAppearance.ParkPath));
            BuildOrientedSurfaceBoxesIfAny(
                "Sidewalk Surfaces",
                roads,
                plan.SidewalkGeometry,
                Color.white,
                true,
                CityExteriorAppearance.SidewalkTextureTileSize,
                CityExteriorAppearance.ApplySidewalkSurface);
            BuildOrientedSurfaceBoxesIfAny(
                "Road Center Markings",
                roads,
                plan.CenterMarkingGeometry,
                Color.white,
                false,
                CityExteriorAppearance.RoadMarkingTextureTileSize,
                CityExteriorAppearance.ApplyRoadMarkingSurface);
            BuildOrientedSurfaceBoxesIfAny(
                "Pedestrian Crossings",
                roads,
                plan.CrosswalkMarkingGeometry,
                Color.white,
                false,
                CityExteriorAppearance.RoadMarkingTextureTileSize,
                CityExteriorAppearance.ApplyRoadMarkingSurface);
        }

        private static void AddRoadGeometry(
            IDictionary<WorldChunkKey, RoadChunkGeometry> chunks,
            IReadOnlyList<Bounds> source,
            Action<RoadChunkGeometry, Bounds> add)
        {
            for (int index = 0; index < source.Count; index++)
            {
                Bounds bounds = source[index];
                WorldChunkKey key =
                    WorldChunkKey.FromPosition(bounds.center);
                if (!chunks.TryGetValue(
                        key,
                        out RoadChunkGeometry geometry))
                {
                    geometry = new RoadChunkGeometry();
                    chunks.Add(key, geometry);
                }

                add(geometry, bounds);
            }
        }

        private static void BuildElevationStructures(
            Transform parent,
            CityLayout layout)
        {
            if (layout.ElevationPlan.SignatureStairs.Count == 0)
            {
                return;
            }

            Transform root = new GameObject(
                "City Elevation Structures").transform;
            root.SetParent(parent, false);
            for (int index = 0;
                 index < layout.ElevationPlan.SignatureStairs.Count;
                 index++)
            {
                CityElevationStairDescriptor stair =
                    layout.ElevationPlan.SignatureStairs[index];
                CityElevationStairPlacement placement =
                    CityElevationStairPlacementPlanner.Create(
                        layout,
                        stair);
                CityExteriorStairWorldBuilder.Build(
                    root,
                    placement.ExteriorPlan);
                CityExteriorStairWorldBuilder.BuildRails(
                    root,
                    new[]
                    {
                        placement.LowerInnerRail,
                        placement.UpperInnerRail
                    },
                    $"{stair.Id} Approach Guard Rails");
            }
        }

        internal static GameObject BuildPark(
            Transform parent,
            CityLayout layout,
            GameObject parkLawn)
        {
            CityParkPlan plan = layout?.Park;
            if (plan == null || !plan.IsEnabled)
            {
                return null;
            }

            Transform park = new GameObject("Central Park").transform;
            park.SetParent(parent, false);
            if (parkLawn != null)
            {
                parkLawn.transform.SetParent(park, false);
            }

            Rect bounds = plan.WalkableBounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                plan.Center.y,
                bounds.center.y);
            for (int regionIndex = 0;
                 regionIndex < plan.Regions.Count;
                 regionIndex++)
            {
                CityParkRegionPlan region = plan.Regions[regionIndex];
                GameObject plaza =
                    CityTerrainSurfaceWorldBuilder.BuildConformingDisc(
                        $"Park Plaza {regionIndex + 1}",
                        park,
                        layout,
                        region.PlazaPosition,
                        ParkPlazaRadius,
                        ParkPlazaTopOffset,
                        ParkPlazaThickness,
                        ParkPlaza,
                        CityParkSurfaceAppearance
                            .GetRecipe(CityParkSurfaceKind.Plaza)
                            .MetersPerTile);
                CityParkSurfaceAppearance.ApplyCombined(
                    plaza.GetComponent<Renderer>(),
                    CityParkSurfaceKind.Plaza,
                    ParkPlaza);
            }

            var trunks = new List<Bounds>(plan.TreePositions.Count);
            var canopies = new List<Bounds>(plan.TreePositions.Count);
            Transform colliders =
                new GameObject("Park Tree Colliders").transform;
            colliders.SetParent(park, false);
            for (int index = 0;
                 index < plan.TreePositions.Count;
                 index++)
            {
                Vector3 position = plan.TreePositions[index];
                float height = 2.8f + (index % 4) * 0.24f;
                trunks.Add(new Bounds(
                    position + (Vector3.up * (height * 0.5f)),
                    new Vector3(0.52f, height, 0.52f)));
                canopies.Add(new Bounds(
                    position + (Vector3.up * (height + 1.25f)),
                    new Vector3(2.8f, 2.5f, 2.8f)));

                GameObject colliderObject =
                    new GameObject($"Tree Collider {index + 1}");
                colliderObject.transform.SetParent(colliders, false);
                colliderObject.transform.position =
                    position + (Vector3.up * 1.15f);
                BoxCollider collider =
                    colliderObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.62f, 2.3f, 0.62f);
            }

            BuildParkSurfaceBoxesIfAny(
                "Park Tree Trunks",
                park,
                trunks,
                CityParkSurfaceKind.Bark,
                ParkTrunk);
            BuildParkSurfaceBoxesIfAny(
                "Park Tree Canopies",
                park,
                canopies,
                CityParkSurfaceKind.Foliage,
                ParkCanopy);

            var benchParts =
                new List<Bounds>(plan.BenchPositions.Count * 3);
            for (int index = 0;
                 index < plan.BenchPositions.Count;
                 index++)
            {
                Vector3 position = plan.BenchPositions[index];
                benchParts.Add(new Bounds(
                    position + (Vector3.up * 0.62f),
                    new Vector3(2.2f, 0.18f, 0.58f)));
                benchParts.Add(new Bounds(
                    position + new Vector3(-0.72f, 0.30f, 0f),
                    new Vector3(0.18f, 0.60f, 0.46f)));
                benchParts.Add(new Bounds(
                    position + new Vector3(0.72f, 0.30f, 0f),
                    new Vector3(0.18f, 0.60f, 0.46f)));
            }

            BuildParkSurfaceBoxesIfAny(
                "Park Benches",
                park,
                benchParts,
                CityParkSurfaceKind.Timber,
                ParkBench);
            CityStaticCollisionBuilder.AddParkBenchColliders(
                park,
                plan.BenchPositions);
            if (layout.HasParkBoundaryHedges)
            {
                BuildParkHedges(park, plan);
            }
            return park.gameObject;
        }

        private static void BuildParkHedges(
            Transform parent,
            CityParkPlan plan)
        {
            var hedges = new List<Bounds>(plan.Regions.Count * 8);
            for (int index = 0; index < plan.Regions.Count; index++)
            {
                CityParkRegionPlan region = plan.Regions[index];
                Rect bounds = region.WalkableBounds;
                float gateWidth = region.Gates.Count > 0
                    ? region.Gates[0].Width
                    : 6f;
                float halfGate = gateWidth * 0.5f;
                AddHorizontalBoundaryParts(
                    hedges,
                    bounds.xMin,
                    bounds.xMax,
                    bounds.center.x,
                    bounds.yMin,
                    halfGate,
                    region.Center.y);
                AddHorizontalBoundaryParts(
                    hedges,
                    bounds.xMin,
                    bounds.xMax,
                    bounds.center.x,
                    bounds.yMax,
                    halfGate,
                    region.Center.y);
                AddVerticalBoundaryParts(
                    hedges,
                    bounds.yMin,
                    bounds.yMax,
                    bounds.center.y,
                    bounds.xMin,
                    halfGate,
                    region.Center.y);
                AddVerticalBoundaryParts(
                    hedges,
                    bounds.yMin,
                    bounds.yMax,
                    bounds.center.y,
                    bounds.xMax,
                    halfGate,
                    region.Center.y);
            }

            BuildParkSurfaceBoxesIfAny(
                "Park Boundary Hedges",
                parent,
                hedges,
                CityParkSurfaceKind.Foliage,
                ParkHedge);
            CityStaticCollisionBuilder.AddColliderGroup(
                parent,
                "Park Hedge Colliders",
                hedges);
        }

        private static void AddHorizontalBoundaryParts(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float gateCenter,
            float fixedZ,
            float halfGate,
            float elevation)
        {
            AddHorizontalBoundaryPart(
                target,
                minimum,
                gateCenter - halfGate,
                fixedZ,
                elevation);
            AddHorizontalBoundaryPart(
                target,
                gateCenter + halfGate,
                maximum,
                fixedZ,
                elevation);
        }

        private static void AddHorizontalBoundaryPart(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float fixedZ,
            float elevation)
        {
            if (maximum <= minimum)
            {
                return;
            }

            target.Add(new Bounds(
                new Vector3(
                    (minimum + maximum) * 0.5f,
                    elevation + 0.58f,
                    fixedZ),
                new Vector3(maximum - minimum, 1.16f, 0.72f)));
        }

        private static void AddVerticalBoundaryParts(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float gateCenter,
            float fixedX,
            float halfGate,
            float elevation)
        {
            AddVerticalBoundaryPart(
                target,
                minimum,
                gateCenter - halfGate,
                fixedX,
                elevation);
            AddVerticalBoundaryPart(
                target,
                gateCenter + halfGate,
                maximum,
                fixedX,
                elevation);
        }

        private static void AddVerticalBoundaryPart(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float fixedX,
            float elevation)
        {
            if (maximum <= minimum)
            {
                return;
            }

            target.Add(new Bounds(
                new Vector3(
                    fixedX,
                    elevation + 0.58f,
                    (minimum + maximum) * 0.5f),
                new Vector3(0.72f, 1.16f, maximum - minimum)));
        }

        private static void BuildBuilding(
            Transform parent,
            CityLayout layout,
            BuildingLot lot,
            int citySeed,
            Material emissiveMaterial,
            RoadWalkableArea walkableArea,
            IList<BarEntrance> bars,
            ref HomeEntrance playerHome,
            ref SupermarketEntrance supermarket)
        {
            if (!lot.HasBuilding)
            {
                return;
            }

            Transform building = new GameObject(
                lot.IsBar
                    ? $"Bar {lot.BarId}"
                    : lot.IsPlayerHome
                        ? "Player Home"
                        : lot.IsSupermarket
                            ? "Supermarket"
                            : $"Building {lot.Cell.x}-{lot.Cell.y}").transform;
            building.SetParent(parent, false);

            Color facadeColor =
                CityExteriorAppearance.CreateNightFacadeColor(lot);
            float foundationDepth = ResolveBuildingFoundationDepth(
                layout,
                lot);
            GameObject mass = RuntimePrimitiveFactory.CreateBox(
                "Building Mass",
                building,
                lot.Center +
                (Vector3.up *
                 (lot.Height * 0.5f +
                  CityFacadeGrid.MassBaseElevation -
                  foundationDepth * 0.5f)),
                new Vector3(
                    lot.Size.x,
                    lot.Height + foundationDepth,
                    lot.Size.y),
                facadeColor);
            CityFacadeAppearance.Apply(
                mass.GetComponent<Renderer>(),
                lot,
                citySeed,
                facadeColor,
                new CityFacadePlacement(
                    CityFacadeGrid.FrontageRunsAlongX(lot)
                        ? CityFacadeProjection.BoxZY
                        : CityFacadeProjection.BoxXY,
                    0f,
                    CityFacadeGrid.MassBaseElevation));
            Color roofColor = CityExteriorAppearance.Darken(
                facadeColor,
                0.055f);
            GameObject roof = RuntimePrimitiveFactory.CreateBox(
                "Roof",
                building,
                lot.Center + (Vector3.up * (lot.Height + 0.22f)),
                new Vector3(lot.Size.x + 0.35f, 0.28f, lot.Size.y + 0.35f),
                roofColor,
                false);
            CityFacadeAppearance.ApplyRoof(
                roof.GetComponent<Renderer>(),
                roofColor);
            BuildWindowBands(building, lot, citySeed);

            if (lot.IsPlayerHome)
            {
                BuildHomeFront(
                    building,
                    lot,
                    emissiveMaterial,
                    walkableArea,
                    ref playerHome);
                return;
            }

            if (lot.IsSupermarket)
            {
                BuildSupermarketFront(
                    building,
                    lot,
                    walkableArea,
                    ref supermarket);
                return;
            }

            if (!lot.IsBar)
            {
                return;
            }

            BuildBarFront(building, lot, walkableArea, bars);
        }

        private static float ResolveBuildingFoundationDepth(
            CityLayout layout,
            BuildingLot lot)
        {
            CitySurfaceDescriptor surface = default;
            bool found = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                if (layout.Surfaces[index].Cell != lot.Cell)
                {
                    continue;
                }

                surface = layout.Surfaces[index];
                found = true;
                break;
            }

            if (!found || !CityTerrainSurfacePlan.UsesContinuousTop(surface))
            {
                return MinimumBuildingFoundationDepth;
            }

            float halfWidth = lot.Size.x * 0.5f;
            float halfDepth = lot.Size.y * 0.5f;
            Vector2[] samples =
            {
                new Vector2(
                    lot.Center.x - halfWidth,
                    lot.Center.z - halfDepth),
                new Vector2(
                    lot.Center.x + halfWidth,
                    lot.Center.z - halfDepth),
                new Vector2(
                    lot.Center.x - halfWidth,
                    lot.Center.z + halfDepth),
                new Vector2(
                    lot.Center.x + halfWidth,
                    lot.Center.z + halfDepth)
            };
            float lowestTop = float.PositiveInfinity;
            for (int index = 0; index < samples.Length; index++)
            {
                lowestTop = Mathf.Min(
                    lowestTop,
                    CityTerrainSurfacePlan.SampleTop(
                        layout,
                        surface,
                        samples[index]));
            }

            float authoredBase = lot.Center.y +
                                 CityFacadeGrid.MassBaseElevation;
            return Mathf.Max(
                MinimumBuildingFoundationDepth,
                authoredBase - lowestTop + 0.16f);
        }

        private static void BuildWindowBands(
            Transform parent,
            BuildingLot lot,
            int citySeed)
        {
            int floorCount = CityFacadeGrid.ResolveFloorCount(lot.Height);
            for (int floor = 0; floor < floorCount; floor++)
            {
                float y = CityFacadeGrid.ResolveFloorCenterY(floor);
                if (!CityFacadeGrid.IsFloorWithinHeight(floor, lot.Height))
                {
                    break;
                }

                Vector3 frontPosition;
                Vector3 backPosition;
                Vector3 windowSize;
                if (lot.HasRoadFrontage)
                {
                    Vector3 frontage = ResolveFacadeDirection(lot);
                    bool frontageIsX = Mathf.Abs(frontage.x) > 0.5f;
                    float facadeDistance = frontageIsX
                        ? lot.Size.x * 0.5f + CityFacadeGrid.FacadeProudOffset
                        : lot.Size.y * 0.5f + CityFacadeGrid.FacadeProudOffset;
                    Vector3 facadeOffset = frontage * facadeDistance;
                    frontPosition =
                        lot.Center + facadeOffset + (Vector3.up * y);
                    backPosition =
                        lot.Center - facadeOffset + (Vector3.up * y);
                    windowSize = frontageIsX
                        ? new Vector3(
                            CityFacadeGrid.PaneThickness,
                            0.7f,
                            CityFacadeGrid.ResolveRowLength(lot.Size.y))
                        : new Vector3(
                            CityFacadeGrid.ResolveRowLength(lot.Size.x),
                            0.7f,
                            CityFacadeGrid.PaneThickness);
                }
                else
                {
                    frontPosition = lot.Center + new Vector3(
                        0f,
                        y,
                        -(lot.Size.y * 0.5f +
                          CityFacadeGrid.FacadeProudOffset));
                    backPosition = lot.Center + new Vector3(
                        0f,
                        y,
                        lot.Size.y * 0.5f + CityFacadeGrid.FacadeProudOffset);
                    windowSize = new Vector3(
                        CityFacadeGrid.ResolveRowLength(lot.Size.x),
                        0.7f,
                        CityFacadeGrid.PaneThickness);
                }

                if (ShouldBuildGenericFrontWindowRow(lot, y))
                {
                    BuildWindowRow(
                        parent,
                        "Front Windows",
                        frontPosition,
                        windowSize,
                        lot,
                        citySeed,
                        floor,
                        0);
                }

                BuildWindowRow(
                    parent,
                    "Back Windows",
                    backPosition,
                    windowSize,
                    lot,
                    citySeed,
                    floor,
                    1);
            }
        }

        internal static bool ShouldBuildGenericFrontWindowRow(
            BuildingLot lot,
            float centerY)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            if (lot.IsSupermarket && centerY < 2.30f)
            {
                return false;
            }

            if (!lot.IsPlayerHome ||
                !PlayerHomeBalconyGeometry.SupportsThirdFloor(
                    lot.Height))
            {
                return true;
            }

            const float genericWindowHalfHeight = 0.35f;
            float openingBottom =
                PlayerHomeBalconyGeometry.ApartmentFloorElevation;
            float openingTop =
                PlayerHomeBalconyGeometry.ApartmentFloorElevation +
                Mathf.Max(
                    PlayerHomeBalconyGeometry.DoorHeight,
                    PlayerHomeBalconyGeometry.WindowCenterY +
                    PlayerHomeBalconyGeometry.WindowHeight * 0.5f);
            return centerY + genericWindowHalfHeight <=
                   openingBottom ||
                   centerY - genericWindowHalfHeight >=
                   openingTop;
        }

        private static void BuildWindowRow(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 rowSize,
            BuildingLot lot,
            int citySeed,
            int floor,
            int side)
        {
            Transform row = new GameObject(name).transform;
            row.SetParent(parent, false);
            row.localPosition = position;

            bool runsAlongX = rowSize.x > rowSize.z;
            float rowLength = runsAlongX ? rowSize.x : rowSize.z;
            int paneCount = CityFacadeGrid.ResolvePaneCount(rowLength);
            float paneLength =
                CityFacadeGrid.ResolvePaneLength(rowLength, paneCount);
            float paneHeight = CityFacadeGrid.ResolvePaneHeight(lot);

            for (int pane = 0; pane < paneCount; pane++)
            {
                float offset = CityFacadeGrid.ResolvePaneOffset(
                    rowLength,
                    paneCount,
                    pane);
                Vector3 panePosition = runsAlongX
                    ? new Vector3(offset, 0f, 0f)
                    : new Vector3(0f, 0f, offset);
                Vector3 paneSize = runsAlongX
                    ? new Vector3(paneLength, paneHeight, rowSize.z)
                    : new Vector3(rowSize.x, paneHeight, paneLength);
                CityWindowFamily family =
                    CityExteriorAppearance.ResolveWindowFamily(
                        lot,
                        citySeed,
                        floor,
                        pane,
                        side,
                        out uint paneHash);

                GameObject paneObject;
                if (family == CityWindowFamily.Off)
                {
                    paneObject = RuntimePrimitiveFactory.CreateBox(
                        $"Window {floor}-{pane}",
                        row,
                        panePosition,
                        paneSize,
                        CityExteriorAppearance.WindowOff,
                        false);
                    CityWindowAppearance.ApplyDarkPane(
                        paneObject.GetComponent<Renderer>(),
                        paneHash);
                }
                else
                {
                    paneObject =
                        RuntimePrimitiveFactory.CreateMaterialBox(
                            $"Window {floor}-{pane}",
                            row,
                            panePosition,
                            paneSize,
                            CityWindowAppearance.ResolveLitMaterial(
                                family),
                            false);
                    CityWindowAppearance.ApplyLitPane(
                        paneObject.GetComponent<Renderer>(),
                        paneHash);
                }
            }
        }

        private static void BuildBarFront(
            Transform parent,
            BuildingLot lot,
            RoadWalkableArea walkableArea,
            IList<BarEntrance> bars)
        {
            Vector3 direction = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            CityBarFacadeWorldBuilder.BuildCity(parent, lot);

            Vector3 apronCenter =
                (lot.DoorPosition + lot.SidewalkArrivalPosition) * 0.5f;
            float apronLength = Vector3.Distance(
                lot.DoorPosition,
                lot.SidewalkArrivalPosition);
            Vector3 apronSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(
                    apronLength,
                    CityStreetSurfacePlanner.SidewalkTop -
                    CityStreetSurfacePlanner.RoadTop,
                    BarEntranceGeometry.WalkwayWidth)
                : new Vector3(
                    BarEntranceGeometry.WalkwayWidth,
                    CityStreetSurfacePlanner.SidewalkTop -
                    CityStreetSurfacePlanner.RoadTop,
                    apronLength);
            BuildSidewalkBox(
                "Bar Entrance Walkway",
                parent,
                new Bounds(
                    apronCenter +
                    (Vector3.up *
                     ((CityStreetSurfacePlanner.RoadTop +
                       CityStreetSurfacePlanner.SidewalkTop) * 0.5f)),
                    apronSize));
            walkableArea.Add(RectFromCenter(apronCenter, apronSize.x, apronSize.z));

            GameObject entranceObject = new GameObject("Interactive Bar Entrance");
            entranceObject.transform.SetParent(parent, false);
            entranceObject.transform.position =
                lot.DoorPosition + (direction * 0.72f) + (Vector3.up * 0.82f);
            SphereCollider trigger = entranceObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.78f;
            BarEntrance entrance = entranceObject.AddComponent<BarEntrance>();
            entrance.Configure(
                lot.BarId,
                lot.BarActivity,
                lot.District,
                lot.SidewalkArrivalPosition +
                (Vector3.up *
                 (CityStreetSurfacePlanner.SidewalkTop +
                  PlayerFactory.GroundedRootOffset)));
            PlayerDoorActionTarget doorAction =
                entranceObject.AddComponent<PlayerDoorActionTarget>();
            Vector3 doorDock =
                lot.DoorPosition +
                direction * 0.72f;
            doorDock.y =
                apronCenter.y +
                CityStreetSurfacePlanner.SidewalkTop +
                PlayerFactory.GroundedRootOffset;
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    entranceObject.transform.position,
                    doorDock,
                    -direction));
            bars.Add(entrance);
        }

        private static void BuildSupermarketFront(
            Transform parent,
            BuildingLot lot,
            RoadWalkableArea walkableArea,
            ref SupermarketEntrance supermarket)
        {
            Vector3 direction = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            CitySupermarketFacadeWorldBuilder.BuildCity(parent, lot);

            Vector3 apronCenter =
                (lot.DoorPosition + lot.SidewalkArrivalPosition) * 0.5f;
            float apronLength = Vector3.Distance(
                lot.DoorPosition,
                lot.SidewalkArrivalPosition);
            Vector3 apronSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(
                    apronLength,
                    CityStreetSurfacePlanner.SidewalkTop -
                    CityStreetSurfacePlanner.RoadTop,
                    SupermarketEntranceGeometry.WalkwayWidth)
                : new Vector3(
                    SupermarketEntranceGeometry.WalkwayWidth,
                    CityStreetSurfacePlanner.SidewalkTop -
                    CityStreetSurfacePlanner.RoadTop,
                    apronLength);
            BuildSidewalkBox(
                "Supermarket Entrance Walkway",
                parent,
                new Bounds(
                    apronCenter +
                    Vector3.up *
                    ((CityStreetSurfacePlanner.RoadTop +
                      CityStreetSurfacePlanner.SidewalkTop) * 0.5f),
                    apronSize));
            walkableArea.Add(
                RectFromCenter(
                    apronCenter,
                    apronSize.x,
                    apronSize.z));

            GameObject entranceObject = new GameObject(
                "Interactive Supermarket Entrance");
            entranceObject.transform.SetParent(parent, false);
            entranceObject.transform.position =
                lot.DoorPosition +
                direction * 0.82f +
                Vector3.up * 0.82f;
            SphereCollider trigger =
                entranceObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.05f;
            supermarket =
                entranceObject.AddComponent<SupermarketEntrance>();
            supermarket.Configure(
                lot.SidewalkArrivalPosition +
                Vector3.up *
                (CityStreetSurfacePlanner.SidewalkTop +
                 PlayerFactory.GroundedRootOffset));
            PlayerDoorActionTarget doorAction =
                entranceObject.AddComponent<PlayerDoorActionTarget>();
            Vector3 doorDock =
                lot.DoorPosition +
                direction * 0.82f;
            doorDock.y =
                apronCenter.y +
                CityStreetSurfacePlanner.SidewalkTop +
                PlayerFactory.GroundedRootOffset;
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    entranceObject.transform.position,
                    doorDock,
                    -direction));
        }

        private static void BuildHomeFront(
            Transform parent,
            BuildingLot lot,
            Material emissiveMaterial,
            RoadWalkableArea walkableArea,
            ref HomeEntrance playerHome)
        {
            Vector3 direction = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            Vector3 tangent =
                new Vector3(-direction.z, 0f, direction.x);
            bool frontageIsX =
                Mathf.Abs(direction.x) > 0.5f;
            Vector3 doorSize = frontageIsX
                ? new Vector3(0.12f, 2.15f, 1.35f)
                : new Vector3(1.35f, 2.15f, 0.12f);

            BuildHomeBalconyFacade(
                parent,
                lot,
                emissiveMaterial);
            RuntimePrimitiveFactory.CreateBox(
                "Home Door",
                parent,
                lot.DoorPosition +
                (direction * 0.045f) +
                (Vector3.up * 1.075f),
                doorSize,
                HomeDoor,
                false);
            BuildHomeDoorFrame(
                parent,
                lot.DoorPosition,
                direction,
                tangent);

            Vector3 apronCenter =
                (lot.DoorPosition + lot.SidewalkArrivalPosition) * 0.5f;
            float apronLength = Vector3.Distance(
                lot.DoorPosition,
                lot.SidewalkArrivalPosition);
            Vector3 apronSize = frontageIsX
                ? new Vector3(
                    apronLength,
                    CityStreetSurfacePlanner.SidewalkTop -
                    CityStreetSurfacePlanner.RoadTop,
                    PlayerHomeEntranceGeometry.WalkwayWidth)
                : new Vector3(
                    PlayerHomeEntranceGeometry.WalkwayWidth,
                    CityStreetSurfacePlanner.SidewalkTop -
                    CityStreetSurfacePlanner.RoadTop,
                    apronLength);
            BuildSidewalkBox(
                "Home Entrance Walkway",
                parent,
                new Bounds(
                    apronCenter +
                    (Vector3.up *
                     ((CityStreetSurfacePlanner.RoadTop +
                       CityStreetSurfacePlanner.SidewalkTop) * 0.5f)),
                    apronSize));
            walkableArea.Add(
                RectFromCenter(
                    apronCenter,
                    apronSize.x,
                    apronSize.z));

            Vector3 mailboxBase =
                lot.DoorPosition +
                (direction * 1.05f) +
                (tangent * 1.35f);
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Mailbox Post",
                parent,
                mailboxBase + (Vector3.up * 0.46f),
                new Vector3(0.09f, 0.46f, 0.09f),
                HomeTrim,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Mailbox",
                parent,
                mailboxBase + (Vector3.up * 1.02f),
                frontageIsX
                    ? new Vector3(0.52f, 0.34f, 0.78f)
                    : new Vector3(0.78f, 0.34f, 0.52f),
                HomeDoor,
                false);
            CityStaticCollisionBuilder.AddHomeMailboxCollider(
                parent,
                mailboxBase,
                frontageIsX);
            RuntimePrimitiveFactory.CreateBox(
                "Home Roof Accent",
                parent,
                lot.Center +
                (Vector3.up * (lot.Height + 0.48f)),
                new Vector3(
                    lot.Size.x + 0.75f,
                    0.38f,
                    lot.Size.y + 0.75f),
                HomeDoor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Chimney",
                parent,
                lot.Center +
                new Vector3(
                    -lot.Size.x * 0.28f,
                    lot.Height + 1.05f,
                    lot.Size.y * 0.20f),
                new Vector3(0.68f, 1.55f, 0.68f),
                new Color(0.24f, 0.19f, 0.17f),
                false);

            BuildHomeAnchor(
                parent,
                lot,
                direction,
                tangent,
                frontageIsX,
                emissiveMaterial);

            GameObject entranceObject =
                new GameObject("Interactive Home Entrance");
            entranceObject.transform.SetParent(parent, false);
            entranceObject.transform.position =
                lot.DoorPosition +
                (direction * 0.72f) +
                (Vector3.up * 0.82f);
            SphereCollider trigger =
                entranceObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.78f;
            playerHome =
                entranceObject.AddComponent<HomeEntrance>();
            playerHome.Configure(
                lot.SidewalkArrivalPosition +
                (Vector3.up *
                 (CityStreetSurfacePlanner.SidewalkTop +
                  PlayerFactory.GroundedRootOffset)));
            PlayerDoorActionTarget doorAction =
                entranceObject.AddComponent<PlayerDoorActionTarget>();
            Vector3 doorDock =
                lot.DoorPosition +
                direction * 0.72f;
            doorDock.y =
                apronCenter.y +
                CityStreetSurfacePlanner.SidewalkTop +
                PlayerFactory.GroundedRootOffset;
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    entranceObject.transform.position,
                    doorDock,
                    -direction));
        }

        /// <summary>The digit on the hero's lit house-number plaque.</summary>
        public const string HomeHouseNumber = "7";

        /// <summary>
        /// The landmarks that make the hero's building findable: a warm
        /// entrance lamp under a small canopy with the lit blue house
        /// number beside the door for street level, and a rooftop
        /// antenna mast with a red beacon for anywhere in the city.
        /// Every glow rides the night registry, so it dims by day with
        /// the rest of the city's electricity.
        /// </summary>
        private static void BuildHomeAnchor(
            Transform parent,
            BuildingLot lot,
            Vector3 direction,
            Vector3 tangent,
            bool frontageIsX,
            Material emissiveMaterial)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Entrance Canopy",
                parent,
                lot.DoorPosition +
                (direction * 0.44f) +
                (Vector3.up * 2.46f),
                frontageIsX
                    ? new Vector3(0.85f, 0.10f, 2.05f)
                    : new Vector3(2.05f, 0.10f, 0.85f),
                HomeTrim,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entrance Lamp Housing",
                parent,
                lot.DoorPosition +
                (direction * 0.50f) +
                (Vector3.up * 2.36f),
                new Vector3(0.16f, 0.10f, 0.16f),
                HomeDoor,
                false);
            Color lampColor = new Color(1.35f, 0.95f, 0.55f);
            GameObject lamp = RuntimePrimitiveFactory.CreateBox(
                "Home Entrance Lamp",
                parent,
                lot.DoorPosition +
                (direction * 0.50f) +
                (Vector3.up * 2.27f),
                new Vector3(0.12f, 0.09f, 0.12f),
                lampColor,
                emissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lamp.GetComponent<Renderer>(),
                lampColor);

            // The Soviet enamel plaque: deep blue, lit from within.
            Vector3 plaqueCenter =
                lot.DoorPosition +
                (direction * 0.10f) +
                (tangent * 1.18f) +
                (Vector3.up * 2.32f);
            RuntimePrimitiveFactory.CreateBox(
                "Home Number Plaque",
                parent,
                plaqueCenter,
                frontageIsX
                    ? new Vector3(0.07f, 0.52f, 0.42f)
                    : new Vector3(0.42f, 0.52f, 0.07f),
                new Color(0.07f, 0.11f, 0.30f),
                false);
            Color digitColor = new Color(1.05f, 1.12f, 1.30f);
            IReadOnlyList<SignSegmentRect> digit =
                CitySignLettering.Layout(
                    HomeHouseNumber,
                    0.24f,
                    0.34f,
                    1f);
            for (int index = 0; index < digit.Count; index++)
            {
                SignSegmentRect segment = digit[index];
                GameObject stroke = RuntimePrimitiveFactory.CreateBox(
                    "Home Number Digit Segment",
                    parent,
                    plaqueCenter +
                    (direction * 0.045f) +
                    (tangent * segment.Center.x) +
                    (Vector3.up * segment.Center.y),
                    frontageIsX
                        ? new Vector3(
                            0.03f,
                            segment.Size.y,
                            segment.Size.x)
                        : new Vector3(
                            segment.Size.x,
                            segment.Size.y,
                            0.03f),
                    digitColor,
                    emissiveMaterial,
                    false);
                CityNightGlowRegistry.Register(
                    stroke.GetComponent<Renderer>(),
                    digitColor);
            }

            // The rooftop beacon: a thin antenna mast with a red
            // aircraft lamp, readable from any street in the city.
            Vector3 mastBase = lot.Center + new Vector3(
                lot.Size.x * 0.30f,
                lot.Height + 0.62f,
                -lot.Size.y * 0.22f);
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Beacon Mast",
                parent,
                mastBase + (Vector3.up * 1.70f),
                new Vector3(0.08f, 1.70f, 0.08f),
                HomeDoor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Beacon Crossarm",
                parent,
                mastBase + (Vector3.up * 2.42f),
                new Vector3(0.66f, 0.05f, 0.05f),
                HomeDoor,
                false);

            // Big and hot enough to survive the city fog from blocks
            // away — the one red point on the skyline is the hero's.
            Color beaconColor = new Color(2.30f, 0.26f, 0.18f);
            GameObject beacon = RuntimePrimitiveFactory.CreateBox(
                "Home Roof Beacon",
                parent,
                mastBase + (Vector3.up * 3.52f),
                new Vector3(0.30f, 0.30f, 0.30f),
                beaconColor,
                emissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                beacon.GetComponent<Renderer>(),
                beaconColor);
        }

        internal static void BuildHomeBalconyFacade(
            Transform parent,
            BuildingLot lot,
            Material emissiveMaterial)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            PlayerHomeBalconyGeometry.GetFrontageDirection(lot);
            if (!PlayerHomeBalconyGeometry.SupportsThirdFloor(
                    lot.Height))
            {
                return;
            }

            CreateHomeBalconyBox(
                "Home Balcony Slab",
                parent,
                lot,
                new Vector3(
                    PlayerHomeBalconyGeometry.HomeFacadeX +
                    PlayerHomeBalconyGeometry.BalconyDepth * 0.5f,
                    -PlayerHomeBalconyGeometry
                        .BalconySlabThickness * 0.5f,
                    PlayerHomeBalconyGeometry.BalconyCenterZ),
                new Vector3(
                    PlayerHomeBalconyGeometry.BalconyDepth,
                    PlayerHomeBalconyGeometry
                        .BalconySlabThickness,
                    PlayerHomeBalconyGeometry.BalconyWidth),
                HomeBalconyConcrete);

            BuildHomeBalconyOpening(
                parent,
                lot,
                "Home Balcony Door",
                PlayerHomeBalconyGeometry
                    .DoorHeight * 0.5f,
                PlayerHomeBalconyGeometry.DoorCenterZ,
                PlayerHomeBalconyGeometry.DoorWidth,
                PlayerHomeBalconyGeometry.DoorHeight,
                HomeDoor,
                null);
            BuildHomeBalconyOpening(
                parent,
                lot,
                "Home Balcony Window",
                PlayerHomeBalconyGeometry.WindowCenterY,
                PlayerHomeBalconyGeometry.WindowCenterZ,
                PlayerHomeBalconyGeometry.WindowWidth,
                PlayerHomeBalconyGeometry.WindowHeight,
                CityExteriorAppearance.HomeWindow,
                emissiveMaterial);
            BuildHomeBalconyOpeningFrame(
                parent,
                lot,
                "Home Balcony Door",
                PlayerHomeBalconyGeometry
                    .DoorHeight * 0.5f,
                PlayerHomeBalconyGeometry.DoorCenterZ,
                PlayerHomeBalconyGeometry.DoorWidth,
                PlayerHomeBalconyGeometry.DoorHeight,
                false);
            BuildHomeBalconyOpeningFrame(
                parent,
                lot,
                "Home Balcony Window",
                PlayerHomeBalconyGeometry.WindowCenterY,
                PlayerHomeBalconyGeometry.WindowCenterZ,
                PlayerHomeBalconyGeometry.WindowWidth,
                PlayerHomeBalconyGeometry.WindowHeight,
                true);
            BuildHomeBalconyRails(parent, lot);
        }

        private static void BuildHomeBalconyOpening(
            Transform parent,
            BuildingLot lot,
            string name,
            float centerY,
            float centerZ,
            float width,
            float height,
            Color color,
            Material sharedMaterial)
        {
            CreateHomeBalconyBox(
                name,
                parent,
                lot,
                new Vector3(
                    PlayerHomeBalconyGeometry.HomeFacadeX +
                    0.035f,
                    centerY,
                    centerZ),
                new Vector3(0.07f, height, width),
                color,
                sharedMaterial);
        }

        private static void BuildHomeBalconyOpeningFrame(
            Transform parent,
            BuildingLot lot,
            string name,
            float centerY,
            float centerZ,
            float width,
            float height,
            bool includeSill)
        {
            const float frameWidth = 0.12f;
            const float normalOffset = 0.09f;
            for (int side = -1; side <= 1; side += 2)
            {
                CreateHomeBalconyBox(
                    name + " Frame",
                    parent,
                    lot,
                    new Vector3(
                        PlayerHomeBalconyGeometry.HomeFacadeX +
                        normalOffset,
                        centerY,
                        centerZ +
                        side *
                        (width + frameWidth) * 0.5f),
                    new Vector3(
                        0.12f,
                        height + frameWidth * 2f,
                        frameWidth),
                    HomeTrim);
            }

            Vector3 horizontalSize = new Vector3(
                0.12f,
                frameWidth,
                width + frameWidth * 2f);
            CreateHomeBalconyBox(
                name + " Header",
                parent,
                lot,
                new Vector3(
                    PlayerHomeBalconyGeometry.HomeFacadeX +
                    normalOffset,
                    centerY +
                    (height + frameWidth) * 0.5f,
                    centerZ),
                horizontalSize,
                HomeTrim);
            if (!includeSill)
            {
                return;
            }

            CreateHomeBalconyBox(
                name + " Sill",
                parent,
                lot,
                new Vector3(
                    PlayerHomeBalconyGeometry.HomeFacadeX +
                    normalOffset,
                    centerY -
                    (height + frameWidth) * 0.5f,
                    centerZ),
                horizontalSize,
                HomeTrim);
            CreateHomeBalconyBox(
                name + " Mullion",
                parent,
                lot,
                new Vector3(
                    PlayerHomeBalconyGeometry.HomeFacadeX +
                    normalOffset + 0.01f,
                    centerY,
                    centerZ),
                new Vector3(
                    0.13f,
                    height,
                    frameWidth * 0.72f),
                HomeTrim);
        }

        private static void BuildHomeBalconyRails(
            Transform parent,
            BuildingLot lot)
        {
            float depth = PlayerHomeBalconyGeometry.BalconyDepth;
            float width = PlayerHomeBalconyGeometry.BalconyWidth;
            float halfWidth = width * 0.5f;
            float thickness = PlayerHomeBalconyGeometry.RailingThickness;
            float height = PlayerHomeBalconyGeometry.RailingHeight;
            float outerX =
                PlayerHomeBalconyGeometry.HomeFacadeX +
                depth -
                thickness * 0.5f;
            float topY = height - thickness * 0.5f;

            CreateHomeBalconyBox(
                "Home Balcony Front Rail",
                parent,
                lot,
                new Vector3(
                    outerX,
                    topY,
                    PlayerHomeBalconyGeometry.BalconyCenterZ),
                new Vector3(thickness, thickness, width),
                HomeBalconyRail);
            for (int post = 0; post < 5; post++)
            {
                float z =
                    PlayerHomeBalconyGeometry.BalconyCenterZ -
                    halfWidth +
                    thickness * 0.5f +
                    post *
                    (width - thickness) * 0.25f;
                CreateHomeBalconyPost(
                    parent,
                    lot,
                    outerX,
                    z,
                    "Home Balcony Front Post");
            }

            for (int side = -1; side <= 1; side += 2)
            {
                float z =
                    PlayerHomeBalconyGeometry.BalconyCenterZ +
                    side *
                    (halfWidth - thickness * 0.5f);
                CreateHomeBalconyBox(
                    side < 0
                        ? "Home Balcony Side Rail Left"
                        : "Home Balcony Side Rail Right",
                    parent,
                    lot,
                    new Vector3(
                        PlayerHomeBalconyGeometry.HomeFacadeX +
                        depth * 0.5f,
                        topY,
                        z),
                    new Vector3(depth, thickness, thickness),
                    HomeBalconyRail);
                CreateHomeBalconyPost(
                    parent,
                    lot,
                    PlayerHomeBalconyGeometry.HomeFacadeX +
                    depth * 0.52f,
                    z,
                    "Home Balcony Side Post");
            }
        }

        private static void CreateHomeBalconyPost(
            Transform parent,
            BuildingLot lot,
            float localX,
            float localZ,
            string name)
        {
            float thickness = PlayerHomeBalconyGeometry.RailingThickness;
            float height = PlayerHomeBalconyGeometry.RailingHeight;
            CreateHomeBalconyBox(
                name,
                parent,
                lot,
                new Vector3(localX, height * 0.5f, localZ),
                new Vector3(thickness, height, thickness),
                HomeBalconyRail);
        }

        private static void CreateHomeBalconyBox(
            string name,
            Transform parent,
            BuildingLot lot,
            Vector3 localPosition,
            Vector3 localSize,
            Color color,
            Material sharedMaterial = null)
        {
            Vector3 direction =
                PlayerHomeBalconyGeometry.GetFrontageDirection(
                    lot);
            Vector3 worldSize = Mathf.Abs(direction.x) > 0.5f
                ? localSize
                : new Vector3(
                    localSize.z,
                    localSize.y,
                    localSize.x);
            Vector3 worldPosition =
                PlayerHomeBalconyGeometry.ToCityWorld(
                    lot,
                    localPosition);
            if (sharedMaterial != null)
            {
                GameObject glowBox = RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    worldPosition,
                    worldSize,
                    color,
                    sharedMaterial,
                    false);

                // The only material passed here is the emissive one, on
                // the hero's own lit balcony window: it follows the
                // night clock like every other electric glow.
                CityNightGlowRegistry.Register(
                    glowBox.GetComponent<Renderer>(),
                    color);
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                worldPosition,
                worldSize,
                color,
                false);
        }

        private static void BuildHomeDoorFrame(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction,
            Vector3 tangent)
        {
            bool frontageIsX =
                Mathf.Abs(direction.x) > 0.5f;
            Vector3 verticalSize = frontageIsX
                ? new Vector3(0.18f, 2.38f, 0.16f)
                : new Vector3(0.16f, 2.38f, 0.18f);
            Vector3 headerSize = frontageIsX
                ? new Vector3(0.18f, 0.20f, 1.85f)
                : new Vector3(1.85f, 0.20f, 0.18f);
            for (int side = -1; side <= 1; side += 2)
            {
                RuntimePrimitiveFactory.CreateBox(
                    "Home Door Frame",
                    parent,
                    doorPosition +
                    (direction * 0.10f) +
                    (tangent * side * 0.78f) +
                    (Vector3.up * 1.16f),
                    verticalSize,
                    HomeTrim,
                    false);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Home Door Header",
                parent,
                doorPosition +
                (direction * 0.10f) +
                (Vector3.up * 2.32f),
                headerSize,
                HomeTrim,
                false);
            GameObject porchLight = RuntimePrimitiveFactory.CreateBox(
                "Home Porch Light",
                parent,
                doorPosition +
                (direction * 0.18f) +
                (tangent * 1.16f) +
                (Vector3.up * 2.18f),
                new Vector3(0.28f, 0.38f, 0.28f),
                CityExteriorAppearance.HomeWindow * 1.35f,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                porchLight.GetComponent<Renderer>(),
                CityExteriorAppearance.HomeWindow * 1.35f);
        }

        private static Rect RectFromCenter(Vector3 center, float width, float depth)
        {
            return Rect.MinMaxRect(
                center.x - (width * 0.5f),
                center.z - (depth * 0.5f),
                center.x + (width * 0.5f),
                center.z + (depth * 0.5f));
        }

        private static Vector3 ResolveFacadeDirection(BuildingLot lot)
        {
            return lot.HasRoadFrontage
                ? new Vector3(
                    lot.FrontageDirection.x,
                    0f,
                    lot.FrontageDirection.y)
                : Vector3.back;
        }

        private static void BuildOrientedSurfaceBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<RuntimeOrientedBox> boxes,
            Color color,
            bool collider,
            float? xzPlanarUvTileSize,
            Action<Renderer> applyAppearance)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    name,
                    parent,
                    boxes,
                    color,
                    collider,
                    xzPlanarUvTileSize);
            applyAppearance?.Invoke(surface.GetComponent<Renderer>());
        }

        private static void BuildRoadSurfaceBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            bool collider)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    CityExteriorAppearance.Asphalt,
                    collider,
                    CityExteriorAppearance.RoadTextureTileSize);
            CityExteriorAppearance.ApplyRoadSurface(
                surface.GetComponent<Renderer>());
        }

        private static void BuildSidewalkSurfaceBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            bool collider)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    Color.white,
                    collider,
                    CityExteriorAppearance.SidewalkTextureTileSize);
            CityExteriorAppearance.ApplySidewalkSurface(
                surface.GetComponent<Renderer>());
        }

        private static void BuildRoadMarkingBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject markings =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    Color.white,
                    false,
                    CityExteriorAppearance.RoadMarkingTextureTileSize);
            CityExteriorAppearance.ApplyRoadMarkingSurface(
                markings.GetComponent<Renderer>());
        }

        private static void BuildSidewalkBox(
            string name,
            Transform parent,
            Bounds bounds)
        {
            var boxes = new[] { bounds };
            BuildSidewalkSurfaceBoxesIfAny(
                name,
                parent,
                boxes,
                true);
        }

        /// <summary>
        /// One batched group of upright park objects - trunks, canopies,
        /// bench timbers, hedge runs - textured from the park's own
        /// packaged sheets. The mesh is unwrapped per face at the
        /// recipe's metre pitch, so a long hedge shows leaves along its
        /// whole length instead of one line of the sheet stretched flat.
        /// </summary>
        private static void BuildParkSurfaceBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            CityParkSurfaceKind kind,
            Color color)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject group = RuntimePrimitiveFactory.CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                false,
                CityParkSurfaceAppearance.GetRecipe(kind).MetersPerTile,
                CityParkSurfaceAppearance.GetUvMode(kind));
            CityParkSurfaceAppearance.ApplyCombined(
                group.GetComponent<Renderer>(),
                kind,
                color);
        }

        private static void BuildCombinedBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            bool collider = false)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            RuntimePrimitiveFactory.CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                collider);
        }

        private readonly struct WorldChunkKey
        {
            public WorldChunkKey(int x, int z)
            {
                X = x;
                Z = z;
            }

            public int X { get; }
            public int Z { get; }

            public static WorldChunkKey FromPosition(Vector3 position)
            {
                return new WorldChunkKey(
                    Mathf.FloorToInt(position.x / WorldChunkSize),
                    Mathf.FloorToInt(position.z / WorldChunkSize));
            }

            public static int Compare(
                WorldChunkKey left,
                WorldChunkKey right)
            {
                int zComparison = left.Z.CompareTo(right.Z);
                return zComparison != 0
                    ? zComparison
                    : left.X.CompareTo(right.X);
            }
        }

        private sealed class RoadChunkGeometry
        {
            public readonly List<Bounds> Streets = new List<Bounds>();
            public readonly List<Bounds> ParkPaths = new List<Bounds>();
            public readonly List<Bounds> Sidewalks = new List<Bounds>();
            public readonly List<Bounds> CenterMarkings =
                new List<Bounds>();
            public readonly List<Bounds> CrosswalkMarkings =
                new List<Bounds>();
        }
    }
}
