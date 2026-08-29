using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityRiverWorldBuilder
    {
        internal const string RootName = "City River";
        internal const string LandingCutRetainingWallsName =
            "Granite Landing Cut Retaining Walls";

        /// <summary>
        /// How far the channel floor sits below the water top. The water
        /// is transparent now, so this is the distance the depth fade,
        /// the refraction and the bank foam all read against; it is also
        /// the only reason the river does not show the skybox through
        /// itself, the terrain having been cut away under the channel.
        /// </summary>
        internal const float RiverBedDepth = 1.10f;

        /// <summary>
        /// Where the submerged sides of the channel start, below the
        /// water top. A full quay wall's underside lands at
        /// <c>waterTop - 0.12</c>, so starting at <c>0.08</c> laps the
        /// two by four centimetres. Any positive overlap will do; what
        /// is not allowed is a gap, because a gap at the foot of the
        /// granite is a hole straight through the world once the water
        /// stops being opaque.
        /// </summary>
        internal const float SubmergedSideTop = 0.08f;

        private const float SubmergedSideThickness = 0.44f;

        /// <summary>
        /// How far the water sheet runs past the channel and into the
        /// quay walls on each side. A wave trough at the very edge would
        /// otherwise pull the surface back off the granite and show the
        /// seam behind it.
        /// </summary>
        private const float WaterWallOverlap = 0.15f;

        private const float PromenadeThickness = 0.18f;
        private const float RailHeight = 1.05f;
        private const float RailThickness = 0.14f;
        private const float LandingRetainingWallThickness = 0.24f;
        private const float MinimumParapetOpening = 1.2f;
        internal const float SurfaceClearance = 0.03f;
        internal const float QuayWallLandwardDepth = 0.44f;
        internal const float QuayWallWaterReveal = SurfaceClearance;
        private const float QuayWallThickness =
            QuayWallLandwardDepth + QuayWallWaterReveal;
        private const float QuayWallCenterOffset =
            (QuayWallLandwardDepth - QuayWallWaterReveal) * 0.5f;

        private static readonly Color Granite =
            new Color(0.34f, 0.36f, 0.34f);
        private static readonly Color GraniteEdge =
            new Color(0.25f, 0.28f, 0.27f);
        private static readonly Color Iron =
            new Color(0.075f, 0.10f, 0.105f);
        private static readonly Color Riverbed =
            new Color(0.185f, 0.19f, 0.155f);
        private static readonly Color WorksSteel =
            new Color(0.17f, 0.20f, 0.20f);
        private static readonly Color WorksAccent =
            new Color(0.40f, 0.25f, 0.16f);
        private static readonly Color MouthStone =
            new Color(0.42f, 0.43f, 0.39f);
        private static readonly Color Timber =
            new Color(0.36f, 0.24f, 0.13f);
        private static readonly Color TimberEdge =
            new Color(0.18f, 0.13f, 0.09f);
        private static readonly Color LampGlow =
            new Color(1.30f, 0.72f, 0.31f);

        // The waterside lanterns hang low on the quay wall faces so
        // the row reads from the parapet. At this pitch two or three
        // burn inside the fog's ~30 m of legibility and the farther
        // ones dissolve into it - a rhythm, where the old 52 m of the
        // upper lamps only ever showed a lone dot. The lens rides the
        // water datum, which falls toward the sea.
        private const float QuayWallLampPitch = 13f;
        private const float QuayWallLampHeightAboveWater = 1.02f;
        private const float QuayWallLampBridgeClearance = 6f;
        private const float QuayWallLampLandingClearance = 1.0f;

        // Each waterside lantern carries its own always-on fog halo: the lens
        // alone is a couple of pixels the fog swallows by twenty
        // metres, where the halo billboard is the blurred ball of
        // light a lamp actually is at a distance in fog - the row
        // stays legible from the bridges and the parapet. Warm HDR
        // multiples of the lamp glow, sized past the pooled lights'
        // halos because out there the halo IS the fixture.
        private const float QuayWallLampHaloInnerSize = 0.85f;
        private const float QuayWallLampHaloOuterSize = 2.40f;
        private static readonly Color QuayWallLampHaloInner =
            new Color(3.38f, 1.87f, 0.81f, 0.20f);
        private static readonly Color QuayWallLampHaloOuter =
            new Color(1.95f, 1.08f, 0.47f, 0.055f);

        internal static GameObject Build(
            Transform parent,
            CityLayout layout)
        {
            return Build(
                parent,
                layout,
                CityMountainBoundaryPlanner.Create(layout));
        }

        internal static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityMountainBoundaryPlan mountainPlan)
        {
            return Build(parent, layout, mountainPlan, out _);
        }

        internal static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityMountainBoundaryPlan mountainPlan,
            out IReadOnlyList<Transform> quayLampAnchors)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (mountainPlan == null)
            {
                throw new ArgumentNullException(nameof(mountainPlan));
            }

            var anchors = new List<Transform>();
            quayLampAnchors = anchors;
            if (!layout.River.IsEnabled)
            {
                return null;
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            CityWaterResources.SetNightFactor(
                CityNightGlowRegistry.NightFactor);
            CityWaterResources.SetRainIntensity(
                CityEternalRainShaper.FloorIntensity(
                    GameWeatherRules.EvaluateCurrent().RainIntensity));

            BuildRiverbed(root, layout.River);
            BuildWater(root, layout.River);
            BuildPromenades(root, layout);
            BuildRetainingWalls(root, layout);
            BuildUpperQuayRails(root, layout, mountainPlan);
            if (mountainPlan.HasRiverCave)
            {
                BuildRiverCaveExtension(
                    root,
                    mountainPlan.RiverCave);
            }
            BuildBridges(root, layout);
            BuildLandings(root, layout);
            BuildPromenadeLights(root, layout, anchors);
            return root.gameObject;
        }

        private static void BuildWater(
            Transform parent,
            CityRiverPlan plan)
        {
            Transform water = new GameObject("Flowing Water").transform;
            water.SetParent(parent, false);
            for (int index = 0; index < plan.Segments.Count; index++)
            {
                CityRiverSegmentDescriptor segment = plan.Segments[index];
                Rect bounds = segment.WaterBounds;
                CityWaterSurfaceFactory.CreateSlopedSurface(
                    $"River Water {segment.Cell.y}",
                    water,
                    Rect.MinMaxRect(
                        bounds.xMin - WaterWallOverlap,
                        bounds.yMin,
                        bounds.xMax + WaterWallOverlap,
                        bounds.yMax),
                    ResolveWaterTopY(segment, bounds.yMin),
                    ResolveWaterTopY(segment, bounds.yMax),
                    CityRiverResources.WaterMaterial);
            }
        }

        /// <summary>
        /// The channel floor and the two submerged sides that close it
        /// against the quay walls.
        ///
        /// None of this existed while the water was opaque: the city
        /// deliberately emits no terrain under a river cell, so the
        /// channel was a hole with a lid on it. The lid is now glass.
        /// The floor is what the water's depth fade measures against and
        /// what its refraction shows, and the sides are what stops the
        /// world showing through the four-centimetre band between the
        /// underside of the granite and the floor.
        /// </summary>
        private static void BuildRiverbed(
            Transform parent,
            CityRiverPlan plan)
        {
            Transform bed = new GameObject("Channel Floor").transform;
            bed.SetParent(parent, false);
            for (int index = 0; index < plan.Segments.Count; index++)
            {
                CityRiverSegmentDescriptor segment = plan.Segments[index];
                Rect bounds = segment.WaterBounds;
                float southFloorY =
                    ResolveWaterTopY(segment, bounds.yMin) - RiverBedDepth;
                float northFloorY =
                    ResolveWaterTopY(segment, bounds.yMax) - RiverBedDepth;

                // Wider than the channel, so its edges run under the
                // submerged sides rather than meeting them at a line.
                CreateSlopedSurface(
                    $"River Floor {segment.Cell.y}",
                    bed,
                    Rect.MinMaxRect(
                        bounds.xMin - SubmergedSideThickness,
                        bounds.yMin,
                        bounds.xMax + SubmergedSideThickness,
                        bounds.yMax),
                    southFloorY,
                    northFloorY,
                    0.30f,
                    Riverbed,
                    null,
                    false,
                    CityRiverSurfaceKind.Bed);

                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side < 0
                        ? bounds.xMin - SubmergedSideThickness * 0.5f
                        : bounds.xMax + SubmergedSideThickness * 0.5f;
                    float southTopY =
                        ResolveWaterTopY(segment, bounds.yMin) -
                        SubmergedSideTop;
                    float northTopY =
                        ResolveWaterTopY(segment, bounds.yMax) -
                        SubmergedSideTop;
                    float height = Mathf.Max(
                        southTopY - southFloorY,
                        northTopY - northFloorY);
                    CreateBeamBetween(
                        $"River Submerged Side " +
                        $"{(side < 0 ? "West" : "East")} {segment.Cell.y}",
                        bed,
                        new Vector3(
                            x,
                            southTopY - height * 0.5f,
                            bounds.yMin),
                        new Vector3(
                            x,
                            northTopY - height * 0.5f,
                            bounds.yMax),
                        SubmergedSideThickness,
                        height,
                        Riverbed,
                        false,
                        CityRiverSurfaceKind.Bed);
                }
            }
        }

        /// <summary>
        /// The water surface elevation at a point along a segment. The
        /// plan's per-node values are the datum; the visible top sits
        /// <see cref="CitySurfaceDescriptor.WaterTopOffset"/> under it.
        /// </summary>
        private static float ResolveWaterTopY(
            CityRiverSegmentDescriptor segment,
            float z)
        {
            float amount = Mathf.InverseLerp(
                segment.WaterBounds.yMin,
                segment.WaterBounds.yMax,
                z);
            return Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                amount) + CitySurfaceDescriptor.WaterTopOffset;
        }

        private static void BuildPromenades(
            Transform parent,
            CityLayout layout)
        {
            Transform root = new GameObject("Upper Embankments").transform;
            root.SetParent(parent, false);
            for (int bankIndex = 0;
                 bankIndex < layout.River.Promenades.Count;
                 bankIndex++)
            {
                CityRiverPromenadeDescriptor promenade =
                    layout.River.Promenades[bankIndex];
                Rect physicalBounds = ResolvePhysicalPromenadeBounds(
                    layout.River,
                    promenade);
                List<Rect> cuts = CreateLandingCuts(
                    layout.River,
                    promenade.WestBank);
                float segmentLength = layout.NodeSpacing.y;
                int segmentCount = Mathf.CeilToInt(
                    physicalBounds.height / segmentLength);
                for (int segmentIndex = 0;
                     segmentIndex < segmentCount;
                     segmentIndex++)
                {
                    float zMin = physicalBounds.yMin +
                                  segmentIndex * segmentLength;
                    float zMax = Mathf.Min(
                        physicalBounds.yMax,
                        zMin + segmentLength);
                    var patches = new List<Rect>
                    {
                        Rect.MinMaxRect(
                            physicalBounds.xMin,
                            zMin,
                            physicalBounds.xMax,
                            zMax)
                    };
                    for (int cutIndex = 0;
                         cutIndex < cuts.Count;
                         cutIndex++)
                    {
                        SubtractFromAll(patches, cuts[cutIndex]);
                    }

                    for (int patchIndex = 0;
                         patchIndex < patches.Count;
                         patchIndex++)
                    {
                        Rect patch = patches[patchIndex];
                        CreateSlopedSurface(
                            $"{promenade.Id} {segmentIndex + 1}-" +
                            $"{patchIndex + 1}",
                            root,
                            patch,
                            SamplePromenadeY(promenade, patch.yMin),
                            SamplePromenadeY(promenade, patch.yMax),
                            PromenadeThickness,
                            Granite,
                            null,
                            true,
                            CityRiverSurfaceKind.Paving);
                    }
                }
            }
        }

        private static void BuildRetainingWalls(
            Transform parent,
            CityLayout layout)
        {
            Transform walls = new GameObject("Granite Quay Walls").transform;
            walls.SetParent(parent, false);
            CityRiverDefinition definition = layout.River.Definition;
            for (int index = 0; index < layout.River.Segments.Count; index++)
            {
                CityRiverSegmentDescriptor segment =
                    layout.River.Segments[index];
                if (segment.Cell.y >= definition.CoreMaximumZExclusive)
                {
                    continue;
                }

                int southZ = segment.Cell.y;
                int northZ = southZ + 1;
                for (int bank = 0; bank < 2; bank++)
                {
                    bool west = bank == 0;
                    int nodeX = definition.CorridorCellX + (west ? 0 : 1);
                    float southBankY = layout.ElevationPlan.GetNodeElevation(
                        new Vector2Int(nodeX, southZ));
                    float northBankY = layout.ElevationPlan.GetNodeElevation(
                        new Vector2Int(nodeX, northZ));
                    float waterEdgeX = west
                        ? segment.WaterBounds.xMin
                        : segment.WaterBounds.xMax;
                    float x = ResolveQuayWallCenterX(
                        waterEdgeX,
                        west);
                    var frontageRanges = new List<AxisRange>();
                    for (int landingIndex = 0;
                         landingIndex < layout.River.Landings.Count;
                         landingIndex++)
                    {
                        CityRiverLandingDescriptor landing =
                            layout.River.Landings[landingIndex];
                        if (landing.WestBank != west ||
                            landing.PlatformBounds.yMax <=
                            segment.WaterBounds.yMin ||
                            landing.PlatformBounds.yMin >=
                            segment.WaterBounds.yMax)
                        {
                            continue;
                        }

                        float frontageMin = Mathf.Max(
                            segment.WaterBounds.yMin,
                            landing.PlatformBounds.yMin);
                        float frontageMax = Mathf.Min(
                            segment.WaterBounds.yMax,
                            landing.PlatformBounds.yMax);
                        frontageRanges.Add(new AxisRange(
                            frontageMin,
                            frontageMax));
                        BuildLoweredQuayWallSpan(
                            walls,
                            $"{(west ? "West" : "East")} Lower Quay " +
                            $"Frontage {landing.Id} {index + 1}",
                            x,
                            frontageMin,
                            frontageMax,
                            segment,
                            landing.LowerY);
                    }

                    List<AxisRange> fullHeightSpans = SubtractRanges(
                        segment.WaterBounds.yMin,
                        segment.WaterBounds.yMax,
                        frontageRanges);
                    for (int spanIndex = 0;
                         spanIndex < fullHeightSpans.Count;
                         spanIndex++)
                    {
                        AxisRange span = fullHeightSpans[spanIndex];
                        BuildFullQuayWallSpan(
                            walls,
                            $"{(west ? "West" : "East")} Quay Wall " +
                            $"{index + 1}-{spanIndex + 1}",
                            x,
                            span.Minimum,
                            span.Maximum,
                            segment,
                            southBankY,
                            northBankY);
                    }
                }
            }
        }

        private static void BuildUpperQuayRails(
            Transform parent,
            CityLayout layout,
            CityMountainBoundaryPlan mountainPlan)
        {
            Transform rails = new GameObject("Quay Guard Rails").transform;
            rails.SetParent(parent, false);
            CityRiverDefinition definition = layout.River.Definition;
            for (int bankIndex = 0;
                 bankIndex < layout.River.Promenades.Count;
                 bankIndex++)
            {
                CityRiverPromenadeDescriptor promenade =
                    layout.River.Promenades[bankIndex];
                Rect physicalBounds = ResolvePhysicalPromenadeBounds(
                    layout.River,
                    promenade);
                float railX = promenade.WestBank
                    ? layout.River.Segments[0].WaterBounds.xMin -
                      CityRiverPlanner.QuayEdgeOffset
                    : layout.River.Segments[0].WaterBounds.xMax +
                      CityRiverPlanner.QuayEdgeOffset;
                List<AxisRange> openings = CreateQuayOpeningRanges(
                    layout.River,
                    promenade.WestBank);
                List<AxisRange> spans = SubtractRanges(
                    promenade.Bounds.yMin,
                    promenade.Bounds.yMax,
                    openings);
                for (int spanIndex = 0;
                     spanIndex < spans.Count;
                     spanIndex++)
                {
                    AxisRange span = spans[spanIndex];
                    BuildSlopedRailSpan(
                        rails,
                        $"{(promenade.WestBank ? "West" : "East")} " +
                        $"Quay Rail {spanIndex + 1}",
                        railX,
                        span.Minimum,
                        span.Maximum,
                        SamplePromenadeY(promenade, span.Minimum),
                        SamplePromenadeY(promenade, span.Maximum),
                        Iron);
                }

                if (!mountainPlan.HasRiverCave)
                {
                    BuildTransverseQuayRail(
                        rails,
                        $"{(promenade.WestBank ? "West" : "East")} " +
                        "Quay South End Rail",
                        physicalBounds.xMin,
                        physicalBounds.xMax,
                        physicalBounds.yMin,
                        SamplePromenadeY(
                            promenade,
                            physicalBounds.yMin));
                }
                // The coast opens exactly the logical three-metre
                // promenade. Its paving has an extra structural lip
                // up to the waterside rail; cap that lip visibly so it
                // cannot masquerade as another route.
                bool hasSeacoast =
                    CitySeacoastPlanner.HasDressableSeacoast(layout);
                if (hasSeacoast)
                {
                    float lipMinimum = promenade.WestBank
                        ? promenade.Bounds.xMax + RailThickness * 0.5f
                        : physicalBounds.xMin + RailThickness * 0.5f;
                    float lipMaximum = promenade.WestBank
                        ? physicalBounds.xMax - RailThickness * 0.5f
                        : promenade.Bounds.xMin - RailThickness * 0.5f;
                    if (lipMaximum > lipMinimum)
                    {
                        BuildTransverseQuayRail(
                            rails,
                            $"{(promenade.WestBank ? "West" : "East")} " +
                            "Quay North Water Lip Rail",
                            lipMinimum,
                            lipMaximum,
                            physicalBounds.yMax,
                            SamplePromenadeY(
                                promenade,
                                physicalBounds.yMax));
                    }
                }
                else
                {
                    BuildTransverseQuayRail(
                        rails,
                        $"{(promenade.WestBank ? "West" : "East")} " +
                        "Quay North End Rail",
                        physicalBounds.xMin,
                        physicalBounds.xMax,
                        physicalBounds.yMax,
                        SamplePromenadeY(promenade, physicalBounds.yMax));
                }
            }
        }

        private static void BuildRiverCaveExtension(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            Transform root = new GameObject("River Cave Extension").transform;
            root.SetParent(parent, false);
            BuildRiverCaveWater(root, cave);
            BuildRiverCaveBed(root, cave);
            BuildRiverCaveBanks(root, cave);
            BuildRiverCaveWalls(root, cave);
            BuildRiverCaveRails(root, cave);
        }

        private static void BuildRiverCaveWater(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            Transform water = new GameObject("Flowing Water").transform;
            water.SetParent(parent, false);
            float waterTopY = cave.BaseY +
                              CitySurfaceDescriptor.WaterTopOffset;
            CreateCaveWaterSurface(
                "River Cave Water Approach",
                water,
                cave.WaterApproachBounds,
                waterTopY,
                true);
            CreateCaveWaterSurface(
                "River Cave Water Throat",
                water,
                cave.ThroatWaterBounds,
                waterTopY,
                false);
        }

        private static void CreateCaveWaterSurface(
            string name,
            Transform parent,
            Rect sourceBounds,
            float waterTopY,
            bool overlapCitySeam)
        {
            const float jointOverlap = 0.04f;
            Rect bounds = Rect.MinMaxRect(
                sourceBounds.xMin - WaterWallOverlap,
                sourceBounds.yMin -
                (overlapCitySeam ? jointOverlap : 0f),
                sourceBounds.xMax + WaterWallOverlap,
                sourceBounds.yMax + jointOverlap);
            CityWaterSurfaceFactory.CreateSlopedSurface(
                name,
                parent,
                bounds,
                waterTopY,
                waterTopY,
                CityRiverResources.WaterMaterial);
        }

        private static void BuildRiverCaveBed(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            Transform bed = new GameObject("Channel Floor").transform;
            bed.SetParent(parent, false);
            BuildRiverCaveBedSpan(
                bed,
                "River Cave Bed Approach",
                cave.WaterApproachBounds,
                cave.BaseY,
                true);
            BuildRiverCaveBedSpan(
                bed,
                "River Cave Bed Throat",
                cave.ThroatWaterBounds,
                cave.BaseY,
                false);
        }

        private static void BuildRiverCaveBedSpan(
            Transform parent,
            string name,
            Rect sourceBounds,
            float waterDatumY,
            bool overlapCitySeam)
        {
            const float jointOverlap = 0.04f;
            float waterTopY = waterDatumY +
                              CitySurfaceDescriptor.WaterTopOffset;
            float floorY = waterTopY - RiverBedDepth;
            float zMin = sourceBounds.yMin -
                         (overlapCitySeam ? jointOverlap : 0f);
            float zMax = sourceBounds.yMax + jointOverlap;
            CreateSlopedSurface(
                name,
                parent,
                Rect.MinMaxRect(
                    sourceBounds.xMin - SubmergedSideThickness,
                    zMin,
                    sourceBounds.xMax + SubmergedSideThickness,
                    zMax),
                floorY,
                floorY,
                0.30f,
                Riverbed,
                null,
                false,
                CityRiverSurfaceKind.Bed);

            float sideTopY = waterTopY - SubmergedSideTop;
            float sideHeight = sideTopY - floorY;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side < 0
                    ? sourceBounds.xMin -
                      SubmergedSideThickness * 0.5f
                    : sourceBounds.xMax +
                      SubmergedSideThickness * 0.5f;
                CreateBeamBetween(
                    $"{name} {(side < 0 ? "West" : "East")} Side",
                    parent,
                    new Vector3(
                        x,
                        sideTopY - sideHeight * 0.5f,
                        zMin),
                    new Vector3(
                        x,
                        sideTopY - sideHeight * 0.5f,
                        zMax),
                    SubmergedSideThickness,
                    sideHeight,
                    Riverbed,
                    false,
                    CityRiverSurfaceKind.Bed);
            }
        }

        private static void BuildRiverCaveBanks(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            Transform banks = new GameObject("Upper Embankments").transform;
            banks.SetParent(parent, false);
            BuildRiverCaveBank(
                banks,
                "River Cave West Bank Approach",
                "river-promenade-west-cave-approach",
                cave.WestBankBounds,
                cave.WestMouthBankY,
                cave.WestCityBankY);
            BuildRiverCaveBank(
                banks,
                "River Cave East Bank Approach",
                "river-promenade-east-cave-approach",
                cave.EastBankBounds,
                cave.EastMouthBankY,
                cave.EastCityBankY);
        }

        private static void BuildRiverCaveBank(
            Transform parent,
            string rootName,
            string name,
            Rect sourceBounds,
            float mouthY,
            float cityY)
        {
            const float seamOverlap = 0.04f;
            Transform root = new GameObject(rootName).transform;
            root.SetParent(parent, false);
            CreateSlopedSurface(
                name,
                root,
                Rect.MinMaxRect(
                    sourceBounds.xMin,
                    sourceBounds.yMin,
                    sourceBounds.xMax,
                    sourceBounds.yMax + seamOverlap),
                mouthY,
                cityY,
                PromenadeThickness,
                Granite,
                null,
                true,
                CityRiverSurfaceKind.Paving);
        }

        private static void BuildRiverCaveWalls(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            Transform walls = new GameObject("Granite Quay Walls").transform;
            walls.SetParent(parent, false);
            BuildRiverCaveWall(
                walls,
                "West River Cave Quay Wall",
                ResolveQuayWallCenterX(
                    cave.WaterApproachBounds.xMin,
                    true),
                cave.WaterApproachBounds.yMin,
                cave.WaterApproachBounds.yMax + 0.04f,
                cave.WestMouthBankY,
                cave.WestCityBankY,
                cave.BaseY);
            BuildRiverCaveWall(
                walls,
                "East River Cave Quay Wall",
                ResolveQuayWallCenterX(
                    cave.WaterApproachBounds.xMax,
                    false),
                cave.WaterApproachBounds.yMin,
                cave.WaterApproachBounds.yMax + 0.04f,
                cave.EastMouthBankY,
                cave.EastCityBankY,
                cave.BaseY);
        }

        private static void BuildRiverCaveWall(
            Transform parent,
            string name,
            float x,
            float zMin,
            float zMax,
            float mouthBankY,
            float cityBankY,
            float waterDatumY)
        {
            float height = Mathf.Max(
                mouthBankY - waterDatumY,
                cityBankY - waterDatumY) + 0.32f;
            CreateBeamBetween(
                name,
                parent,
                new Vector3(
                    x,
                    (mouthBankY + waterDatumY) * 0.5f - 0.08f,
                    zMin),
                new Vector3(
                    x,
                    (cityBankY + waterDatumY) * 0.5f - 0.08f,
                    zMax),
                QuayWallThickness,
                height,
                GraniteEdge,
                true,
                CityRiverSurfaceKind.Quay);
        }

        private static void BuildRiverCaveRails(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            Transform rails = new GameObject("Quay Guard Rails").transform;
            rails.SetParent(parent, false);
            BuildSlopedRailSpan(
                rails,
                "West River Cave Quay Rail",
                cave.WaterApproachBounds.xMin -
                CityRiverPlanner.QuayEdgeOffset,
                cave.WestBankBounds.yMin,
                cave.WestBankBounds.yMax,
                cave.WestMouthBankY,
                cave.WestCityBankY,
                Iron);
            BuildSlopedRailSpan(
                rails,
                "East River Cave Quay Rail",
                cave.WaterApproachBounds.xMax +
                CityRiverPlanner.QuayEdgeOffset,
                cave.EastBankBounds.yMin,
                cave.EastBankBounds.yMax,
                cave.EastMouthBankY,
                cave.EastCityBankY,
                Iron);
        }

        private static void BuildBridges(
            Transform parent,
            CityLayout layout)
        {
            Transform bridges = new GameObject("River Bridges").transform;
            bridges.SetParent(parent, false);
            for (int index = 0; index < layout.River.Bridges.Count; index++)
            {
                CityRiverBridgeDescriptor bridge =
                    layout.River.Bridges[index];
                if (bridge.Definition.Role ==
                    CityBridgeRole.ParkFootbridge)
                {
                    BuildTimberFootbridge(bridges, bridge);
                }
                else
                {
                    BuildRoadBridge(bridges, layout, bridge);
                }
            }
        }

        private static void BuildRoadBridge(
            Transform parent,
            CityLayout layout,
            CityRiverBridgeDescriptor bridge)
        {
            Transform root = new GameObject(
                $"{bridge.Definition.Id} Road Bridge").transform;
            root.SetParent(parent, false);
            Rect span = bridge.SpanBounds;
            float deckY = bridge.AverageY + CityStreetSurfacePlanner.RoadTop;
            bool works = bridge.Definition.Style == CityBridgeStyle.Works;
            Color structure = works ? WorksSteel : MouthStone;
            Color accent = works ? WorksAccent : GraniteEdge;
            CityRiverSurfaceKind surface = ResolveBridgeSurface(
                bridge.Definition.Style);

            CreateBox(
                "Bridge Underside",
                root,
                new Vector3(span.center.x, deckY - 0.34f, span.center.y),
                new Vector3(
                    span.width - SurfaceClearance * 2f,
                    0.52f,
                    span.height - SurfaceClearance * 2f),
                structure,
                true,
                surface);
            CreateBox(
                "North Girder",
                root,
                new Vector3(span.center.x, deckY - 0.68f, span.yMax - 0.22f),
                new Vector3(span.width, 0.72f, 0.34f),
                accent,
                false,
                surface);
            CreateBox(
                "South Girder",
                root,
                new Vector3(span.center.x, deckY - 0.68f, span.yMin + 0.22f),
                new Vector3(span.width, 0.72f, 0.34f),
                accent,
                false,
                surface);

            // A pier stops on the channel floor, not on the waterline.
            // It used to end at the datum, which is 12 cm clear of the
            // water top - hidden while the surface was opaque, and a pier
            // hanging in mid-air the moment it stopped being.
            float waterY = ResolveBridgeWaterY(layout, bridge);
            float pierBottomY = waterY +
                                CitySurfaceDescriptor.WaterTopOffset -
                                RiverBedDepth;
            float pierHeight = Mathf.Max(
                0.5f,
                deckY - 0.42f - pierBottomY);
            for (int pier = -1; pier <= 1; pier += 2)
            {
                CreateBox(
                    $"Bridge Pier {(pier < 0 ? "West" : "East")}",
                    root,
                    new Vector3(
                        span.center.x + pier * span.width * 0.30f,
                        pierBottomY + pierHeight * 0.5f,
                        span.center.y),
                    new Vector3(0.82f, pierHeight, span.height - 1.2f),
                    structure,
                    true,
                    surface);
            }

            bool innerNorth = bridge.Definition.InteriorDirection.y > 0;
            float innerZ = innerNorth ? span.yMax : span.yMin;
            float outerZ = innerNorth ? span.yMin : span.yMax;
            AxisRange guardRange = CreateBridgeGuardRange(bridge);
            List<AxisRange> landingGaps = CreateBridgeLandingGaps(
                layout.River,
                bridge.Definition.Id,
                guardRange.Minimum,
                guardRange.Maximum);
            BuildBridgeRail(
                root,
                "Outer Parapet",
                bridge,
                outerZ,
                guardRange.Minimum,
                guardRange.Maximum,
                Array.Empty<AxisRange>(),
                structure,
                surface);
            BuildBridgeRail(
                root,
                "Landing Parapet",
                bridge,
                innerZ,
                guardRange.Minimum,
                guardRange.Maximum,
                landingGaps,
                structure,
                surface);
        }

        /// <summary>
        /// A bridge is textured as what it is made of: the works crossing
        /// takes the bank's iron, the mouth crossing its quay stone. The
        /// park footbridge is timber and takes the park's own sheet.
        /// </summary>
        private static CityRiverSurfaceKind ResolveBridgeSurface(
            CityBridgeStyle style)
        {
            return style == CityBridgeStyle.Works
                ? CityRiverSurfaceKind.Iron
                : CityRiverSurfaceKind.Quay;
        }

        private static void BuildBridgeRail(
            Transform parent,
            string name,
            CityRiverBridgeDescriptor bridge,
            float z,
            float minimum,
            float maximum,
            IReadOnlyList<AxisRange> gaps,
            Color color,
            CityRiverSurfaceKind surface)
        {
            List<AxisRange> spans = SubtractRanges(
                minimum,
                maximum,
                gaps);
            var boxes = new List<Bounds>();
            bool solid = bridge.Definition.Style == CityBridgeStyle.Mouth;
            for (int index = 0; index < spans.Count; index++)
            {
                AxisRange span = spans[index];
                float length = span.Maximum - span.Minimum;
                if (solid)
                {
                    boxes.Add(new Bounds(
                        new Vector3(
                            (span.Minimum + span.Maximum) * 0.5f,
                            bridge.AverageY + 0.58f,
                            z),
                        new Vector3(length, 1.05f, 0.32f)));
                    continue;
                }

                boxes.Add(new Bounds(
                    new Vector3(
                        (span.Minimum + span.Maximum) * 0.5f,
                        bridge.AverageY + RailHeight,
                        z),
                    new Vector3(length, RailThickness, 0.18f)));
                boxes.Add(new Bounds(
                    new Vector3(
                        (span.Minimum + span.Maximum) * 0.5f,
                        bridge.AverageY + 0.55f,
                        z),
                    new Vector3(length, RailThickness, 0.14f)));
                AddRailPostsAlongX(
                    boxes,
                    span,
                    z,
                    bridge.AverageY,
                    2.4f);
            }

            if (boxes.Count > 0)
            {
                GameObject rail =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        name,
                        parent,
                        boxes,
                        color,
                        true,
                        CityRiverSurfaceAppearance
                            .GetRecipe(surface)
                            .MetersPerTile,
                        RuntimeWorldUvMode.BoxProjected);
                CityRiverSurfaceAppearance.ApplyCombined(
                    rail.GetComponent<Renderer>(),
                    surface,
                    color);
            }
        }

        private static void BuildTimberFootbridge(
            Transform parent,
            CityRiverBridgeDescriptor bridge)
        {
            Transform root = new GameObject(
                "Central Park Timber Footbridge").transform;
            root.SetParent(parent, false);
            Rect span = bridge.SpanBounds;
            float deckY = bridge.AverageY +
                          CityStreetSurfacePlanner.RoadTop;
            float plankTopY = deckY + SurfaceClearance;
            int plankCount = Mathf.CeilToInt(span.width / 0.78f);
            float plankWidth = span.width / plankCount;
            var planks = new List<Bounds>(plankCount);
            for (int index = 0; index < plankCount; index++)
            {
                planks.Add(new Bounds(
                    new Vector3(
                        span.xMin + (index + 0.5f) * plankWidth,
                        plankTopY - 0.055f,
                        span.center.y),
                    new Vector3(
                        Mathf.Max(0.08f, plankWidth - 0.045f),
                        0.11f,
                        span.height + SurfaceClearance * 2f)));
            }

            CreateTimberBatch(
                "Timber Deck Planks",
                root,
                planks,
                Timber,
                false);
            var structure = new List<Bounds>
            {
                new Bounds(
                    new Vector3(
                        span.center.x,
                        deckY - 0.30f,
                        span.center.y - span.height * 0.30f),
                    new Vector3(span.width, 0.38f, 0.22f)),
                new Bounds(
                    new Vector3(
                        span.center.x,
                        deckY - 0.30f,
                        span.center.y + span.height * 0.30f),
                    new Vector3(span.width, 0.38f, 0.22f))
            };
            AxisRange guardRange = CreateBridgeGuardRange(bridge);
            float guardCenter =
                (guardRange.Minimum + guardRange.Maximum) * 0.5f;
            float guardLength =
                guardRange.Maximum - guardRange.Minimum;
            for (int side = -1; side <= 1; side += 2)
            {
                float z = span.center.y + side * (span.height * 0.5f - 0.10f);
                structure.Add(new Bounds(
                    new Vector3(guardCenter, deckY + RailHeight, z),
                    new Vector3(guardLength, 0.13f, 0.13f)));
                AddRailPostsAlongX(
                    structure,
                    guardRange,
                    z,
                    bridge.AverageY,
                    2.1f);
            }

            CreateTimberBatch(
                "Timber Bridge Structure",
                root,
                structure,
                TimberEdge,
                true);
        }

        /// <summary>
        /// One combined footbridge batch on the park's timber sheet, baked
        /// at its pitch and box projected: the deck is read from above and
        /// the handrail from the side, so no single plane serves both.
        /// </summary>
        private static void CreateTimberBatch(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color,
            bool collider)
        {
            const CityParkSurfaceKind timber = CityParkSurfaceKind.Timber;
            GameObject batch = RuntimePrimitiveFactory.CreateCombinedBoxes(
                name,
                parent,
                boxes,
                color,
                collider,
                CityParkSurfaceAppearance.GetRecipe(timber).MetersPerTile,
                CityParkSurfaceAppearance.GetUvMode(timber));
            CityParkSurfaceAppearance.ApplyCombined(
                batch.GetComponent<Renderer>(),
                timber,
                color);
        }

        private static AxisRange CreateBridgeGuardRange(
            CityRiverBridgeDescriptor bridge)
        {
            float inset = RailThickness * 0.5f;
            return new AxisRange(
                bridge.SpanBounds.xMin + inset,
                bridge.SpanBounds.xMax - inset);
        }

        private static void BuildLandings(
            Transform parent,
            CityLayout layout)
        {
            Transform root = new GameObject(
                "Lower River Landings").transform;
            root.SetParent(parent, false);
            for (int index = 0; index < layout.River.Landings.Count; index++)
            {
                CityRiverLandingDescriptor landing =
                    layout.River.Landings[index];
                BuildLanding(root, landing);
            }
        }

        private static void BuildLanding(
            Transform parent,
            CityRiverLandingDescriptor landing)
        {
            Transform root = new GameObject(landing.Id).transform;
            root.SetParent(parent, false);
            float direction = Mathf.Sign(landing.DescentDirection.z);
            float upperEdgeZ = landing.StairBounds.center.y -
                               direction * landing.StairBounds.height * 0.5f;
            float tread = landing.StairBounds.height / landing.StepCount;
            float foundationY = landing.LowerY - 0.20f;
            var steps = new List<Bounds>(landing.StepCount);
            for (int index = 0; index < landing.StepCount; index++)
            {
                float amount = (index + 1f) / landing.StepCount;
                float topY = Mathf.Lerp(
                    landing.UpperY,
                    landing.LowerY,
                    amount);
                float height = Mathf.Max(0.16f, topY - foundationY);
                steps.Add(new Bounds(
                    new Vector3(
                        landing.StairBounds.center.x,
                        foundationY + height * 0.5f,
                        upperEdgeZ + direction * (index + 0.5f) * tread),
                    new Vector3(
                        landing.StairBounds.width,
                        height,
                        tread + 0.025f)));
            }

            GameObject flight = RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Granite Stair Flight",
                root,
                steps,
                Granite,
                true,
                CityRiverSurfaceAppearance
                    .GetRecipe(CityRiverSurfaceKind.Paving)
                    .MetersPerTile,
                RuntimeWorldUvMode.BoxProjected);
            CityRiverSurfaceAppearance.ApplyCombined(
                flight.GetComponent<Renderer>(),
                CityRiverSurfaceKind.Paving,
                Granite);

            GameObject platform =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Lower Waterside Platform",
                    root,
                    new[]
                    {
                        new Bounds(
                            new Vector3(
                                landing.PlatformBounds.center.x,
                                landing.LowerY - 0.12f,
                                landing.PlatformBounds.center.y),
                            new Vector3(
                                landing.PlatformBounds.width,
                                0.24f,
                                landing.PlatformBounds.height))
                    },
                    Granite,
                    true,
                    CityRiverSurfaceAppearance
                        .GetRecipe(CityRiverSurfaceKind.Paving)
                        .MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            CityRiverSurfaceAppearance.ApplyCombined(
                platform.GetComponent<Renderer>(),
                CityRiverSurfaceKind.Paving,
                Granite);

            float lowerEdgeZ = landing.StairBounds.center.y +
                               direction * landing.StairBounds.height * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = landing.StairBounds.center.x +
                          side * (landing.StairBounds.width * 0.5f - 0.08f);
                CreateBeamBetween(
                    $"Stair Rail {(side < 0 ? "West" : "East")}",
                    root,
                    new Vector3(x, landing.UpperY + RailHeight, upperEdgeZ),
                    new Vector3(x, landing.LowerY + RailHeight, lowerEdgeZ),
                    RailThickness,
                    RailThickness,
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
                CreateBox(
                    $"Upper Rail Post {side}",
                    root,
                    new Vector3(x, landing.UpperY + RailHeight * 0.5f, upperEdgeZ),
                    new Vector3(RailThickness, RailHeight, RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
                CreateBox(
                    $"Lower Rail Post {side}",
                    root,
                    new Vector3(x, landing.LowerY + RailHeight * 0.5f, lowerEdgeZ),
                    new Vector3(RailThickness, RailHeight, RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
            }

            float waterEdgeX = landing.WestBank
                ? landing.PlatformBounds.xMax
                : landing.PlatformBounds.xMin;
            CreateBox(
                "Platform Waterside Rail",
                root,
                new Vector3(
                    waterEdgeX,
                    landing.LowerY + RailHeight,
                    landing.PlatformBounds.center.y),
                new Vector3(
                    RailThickness,
                    RailThickness,
                    landing.PlatformBounds.height),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            for (int post = -1; post <= 1; post++)
            {
                CreateBox(
                    $"Platform Rail Post {post + 2}",
                    root,
                    new Vector3(
                        waterEdgeX,
                        landing.LowerY + RailHeight * 0.5f,
                        landing.PlatformBounds.center.y +
                        post * landing.PlatformBounds.height * 0.42f),
                    new Vector3(RailThickness, RailHeight, RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
            }

            float landwardEdgeX = landing.WestBank
                ? landing.PlatformBounds.xMin
                : landing.PlatformBounds.xMax;
            CreateBox(
                "Platform Landward Rail",
                root,
                new Vector3(
                    landwardEdgeX,
                    landing.LowerY + RailHeight,
                    landing.PlatformBounds.center.y),
                new Vector3(
                    RailThickness,
                    RailThickness,
                    landing.PlatformBounds.height),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            float terminalZ = landing.PlatformBounds.center.y +
                              direction *
                              landing.PlatformBounds.height * 0.5f;
            BuildLandingCutRetainingWalls(
                root,
                landing,
                steps,
                tread,
                direction,
                landwardEdgeX,
                terminalZ);
            CreateBox(
                "Platform End Rail",
                root,
                new Vector3(
                    landing.PlatformBounds.center.x,
                    landing.LowerY + RailHeight,
                    terminalZ),
                new Vector3(
                    landing.PlatformBounds.width,
                    RailThickness,
                    RailThickness),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            for (int post = -1; post <= 1; post++)
            {
                float z = landing.PlatformBounds.center.y +
                          post * landing.PlatformBounds.height * 0.42f;
                CreateBox(
                    $"Landward Rail Post {post + 2}",
                    root,
                    new Vector3(
                        landwardEdgeX,
                        landing.LowerY + RailHeight * 0.5f,
                        z),
                    new Vector3(
                        RailThickness,
                        RailHeight,
                        RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
                float x = landing.PlatformBounds.center.x +
                          post * landing.PlatformBounds.width * 0.42f;
                CreateBox(
                    $"End Rail Post {post + 2}",
                    root,
                    new Vector3(
                        x,
                        landing.LowerY + RailHeight * 0.5f,
                        terminalZ),
                    new Vector3(
                        RailThickness,
                        RailHeight,
                        RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
            }

            BuildUpperPlatformCutGuards(
                root,
                landing,
                landwardEdgeX,
                terminalZ);

            for (int bollard = -1; bollard <= 1; bollard += 2)
            {
                GameObject post = RuntimePrimitiveFactory.CreateCylinder(
                    $"Mooring Bollard {bollard}",
                    root,
                    new Vector3(
                        landing.PlatformBounds.center.x,
                        landing.LowerY + 0.26f,
                        landing.PlatformBounds.center.y +
                        bollard * landing.PlatformBounds.height * 0.30f),
                    new Vector3(0.22f, 0.26f, 0.22f),
                    Iron,
                    true);
                CityRiverSurfaceAppearance.Apply(
                    post.GetComponent<Renderer>(),
                    CityRiverSurfaceKind.Iron,
                    SurfaceProjection.CylinderSide,
                    Iron);
            }
        }

        private static void BuildLandingCutRetainingWalls(
            Transform parent,
            CityRiverLandingDescriptor landing,
            IReadOnlyList<Bounds> steps,
            float tread,
            float descentDirection,
            float landwardEdgeX,
            float terminalZ)
        {
            float landwardDirection = landing.WestBank ? -1f : 1f;
            var walls = new List<Bounds>(steps.Count + 2);

            // The promenade is cut away for the stair flight. Its landward
            // edge needs an actual inward-facing retaining surface from each
            // tread back up to the untouched promenade datum. The waterside
            // remains open, so the rail still looks out over the river.
            for (int index = 0; index < steps.Count; index++)
            {
                Bounds step = steps[index];
                float bottomY = step.max.y;
                float height = landing.UpperY - bottomY;
                walls.Add(new Bounds(
                    new Vector3(
                        landwardEdgeX + landwardDirection *
                        LandingRetainingWallThickness * 0.5f,
                        bottomY + height * 0.5f,
                        step.center.z),
                    new Vector3(
                        LandingRetainingWallThickness,
                        height,
                        tread)));
            }

            float platformHeight = landing.UpperY - landing.LowerY;
            walls.Add(new Bounds(
                new Vector3(
                    landwardEdgeX + landwardDirection *
                    LandingRetainingWallThickness * 0.5f,
                    landing.LowerY + platformHeight * 0.5f,
                    landing.PlatformBounds.center.y),
                new Vector3(
                    LandingRetainingWallThickness,
                    platformHeight,
                    landing.PlatformBounds.height)));

            // Extend the terminal panel only toward land so the two walls
            // meet at a sealed corner without intruding on the river side.
            walls.Add(new Bounds(
                new Vector3(
                    landing.PlatformBounds.center.x + landwardDirection *
                    LandingRetainingWallThickness * 0.5f,
                    landing.LowerY + platformHeight * 0.5f,
                    terminalZ + descentDirection *
                    LandingRetainingWallThickness * 0.5f),
                new Vector3(
                    landing.PlatformBounds.width +
                    LandingRetainingWallThickness,
                    platformHeight,
                    LandingRetainingWallThickness)));

            GameObject result = RuntimePrimitiveFactory.CreateCombinedBoxes(
                LandingCutRetainingWallsName,
                parent,
                walls,
                GraniteEdge,
                true,
                CityRiverSurfaceAppearance
                    .GetRecipe(CityRiverSurfaceKind.Quay)
                    .MetersPerTile,
                RuntimeWorldUvMode.BoxProjected);
            CityRiverSurfaceAppearance.ApplyCombined(
                result.GetComponent<Renderer>(),
                CityRiverSurfaceKind.Quay,
                GraniteEdge);
        }

        private static void BuildPromenadeLights(
            Transform parent,
            CityLayout layout,
            ICollection<Transform> quayLampAnchorSink)
        {
            Transform lights = new GameObject(
                "Embankment Lamps").transform;
            lights.SetParent(parent, false);
            var posts = new List<Bounds>();
            var promenadeBulbs = new List<Bounds>();
            var quayWallBulbs = new List<Bounds>();
            var brackets = new List<Bounds>();
            IReadOnlyList<Vector3> positions = CreatePromenadeLampPositions(
                layout);
            for (int index = 0; index < positions.Count; index++)
            {
                Vector3 position = positions[index];
                posts.Add(new Bounds(
                    position + Vector3.up * 1.25f,
                    new Vector3(0.16f, 2.5f, 0.16f)));
                promenadeBulbs.Add(new Bounds(
                    position + Vector3.up * 2.62f,
                    new Vector3(0.42f, 0.24f, 0.42f)));
                CityLightHalo.CreateNightRegistered(
                    lights,
                    position + Vector3.up * 2.62f,
                    QuayWallLampHaloInnerSize,
                    QuayWallLampHaloOuterSize,
                    QuayWallLampHaloInner,
                    QuayWallLampHaloOuter);
            }

            // The waterside lanterns: a back plate, an arm, a hood
            // and a lens hung low on the wall face, plus a pool
            // anchor apiece so the nearest few add real spill at night.
            // The wall is not walkable, so none of it carries a
            // collider. These municipal wall fixtures burn around
            // the clock, so their lenses and halos stay separate from
            // the night-gated upper plafonds.
            IReadOnlyList<Vector3> wallPositions =
                CreateQuayWallLampPositions(layout);
            float channelCenterX = layout.River.Segments.Count > 0
                ? layout.River.Segments[0].WaterBounds.center.x
                : 0f;
            for (int index = 0; index < wallPositions.Count; index++)
            {
                Vector3 p = wallPositions[index];
                float sign = p.x < channelCenterX ? 1f : -1f;
                brackets.Add(new Bounds(
                    new Vector3(
                        p.x + sign * 0.03f, p.y + 0.08f, p.z),
                    new Vector3(0.06f, 0.34f, 0.24f)));
                brackets.Add(new Bounds(
                    new Vector3(
                        p.x + sign * 0.17f, p.y + 0.15f, p.z),
                    new Vector3(0.34f, 0.08f, 0.08f)));
                brackets.Add(new Bounds(
                    new Vector3(
                        p.x + sign * 0.30f, p.y + 0.15f, p.z),
                    new Vector3(0.34f, 0.10f, 0.34f)));
                quayWallBulbs.Add(new Bounds(
                    new Vector3(p.x + sign * 0.30f, p.y, p.z),
                    new Vector3(0.24f, 0.20f, 0.24f)));

                // A step off the lens toward the water, so the soft
                // particle's depth fade meets the channel behind it
                // rather than the fixture it hangs on.
                CreateAlwaysOnQuayWallHalo(
                    lights,
                    new Vector3(
                        p.x + sign * 0.55f,
                        p.y,
                        p.z));

                if (quayLampAnchorSink != null)
                {
                    Transform anchor = new GameObject(
                        $"Quay Lamp Anchor {index + 1}").transform;
                    anchor.SetParent(lights, false);
                    // Just off the lens, aimed down-and-across the
                    // channel so a pooled spot grazes the wall face
                    // and lays its pool on the water.
                    anchor.SetPositionAndRotation(
                        new Vector3(
                            p.x + sign * 0.45f,
                            p.y + 0.20f,
                            p.z),
                        Quaternion.LookRotation(
                            new Vector3(
                                sign * 0.78f,
                                -0.63f,
                                0f).normalized,
                            Vector3.up));
                    quayLampAnchorSink.Add(anchor);
                }
            }

            if (posts.Count > 0)
            {
                GameObject lampPosts =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Embankment Lamp Posts",
                        lights,
                        posts,
                        Iron,
                        true,
                        CityRiverSurfaceAppearance
                            .GetRecipe(CityRiverSurfaceKind.Iron)
                            .MetersPerTile,
                        RuntimeWorldUvMode.BoxProjected);
                CityRiverSurfaceAppearance.ApplyCombined(
                    lampPosts.GetComponent<Renderer>(),
                    CityRiverSurfaceKind.Iron,
                    Iron);
            }

            if (brackets.Count > 0)
            {
                GameObject lanternBrackets =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Waterside Lantern Brackets",
                        lights,
                        brackets,
                        Iron,
                        false,
                        CityRiverSurfaceAppearance
                            .GetRecipe(CityRiverSurfaceKind.Iron)
                            .MetersPerTile,
                        RuntimeWorldUvMode.BoxProjected);
                CityRiverSurfaceAppearance.ApplyCombined(
                    lanternBrackets.GetComponent<Renderer>(),
                    CityRiverSurfaceKind.Iron,
                    Iron);
            }

            if (promenadeBulbs.Count > 0)
            {
                GameObject glow =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        "Promenade Lamp Glow",
                        lights,
                        promenadeBulbs,
                        LampGlow,
                        CityNightResources.EmissiveMaterial);
                CityNightGlowRegistry.Register(
                    glow.GetComponent<Renderer>(),
                    LampGlow);
            }

            if (quayWallBulbs.Count > 0)
            {
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    "Quay Wall Lamp Glow",
                    lights,
                    quayWallBulbs,
                    LampGlow,
                    CityNightResources.EmissiveMaterial);
            }
        }

        private static void CreateAlwaysOnQuayWallHalo(
            Transform parent,
            Vector3 localPosition)
        {
            var haloObject = new GameObject("Quay Wall Lamp Halo");
            haloObject.transform.SetParent(parent, false);
            haloObject.transform.localPosition = localPosition;
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                QuayWallLampHaloInnerSize,
                QuayWallLampHaloOuterSize,
                QuayWallLampHaloInner,
                QuayWallLampHaloOuter);
        }

        internal static IReadOnlyList<Vector3> CreatePromenadeLampPositions(CityLayout layout)
        {
            var result = new List<Vector3>();
            for (int bankIndex = 0;
                 bankIndex < layout.River.Promenades.Count;
                 bankIndex++)
            {
                CityRiverPromenadeDescriptor promenade =
                    layout.River.Promenades[bankIndex];
                float x = promenade.WestBank
                    ? promenade.Bounds.xMax - 0.52f
                    : promenade.Bounds.xMin + 0.52f;
                for (float z = promenade.Bounds.yMin + 13f;
                     z < promenade.Bounds.yMax - 5f;
                     z += 52f)
                {
                    if (IsNearBridge(layout.River, z, 7f) ||
                        IsNearLanding(layout.River, promenade.WestBank, x, z,
                            CityGroundTraversalPlanner.MaximumAgentRadius +
                            0.10f))
                    {
                        continue;
                    }

                    result.Add(new Vector3(
                        x,
                        SamplePromenadeY(promenade, z),
                        z));
                }
            }

            return result.AsReadOnly();
        }

        /// <summary>
        /// Where the waterside lanterns hang: both quay wall faces at
        /// an even pitch down the channel, lens height riding the
        /// water datum as it falls toward the sea. Skips the bridge
        /// openings, the landing frontages (people walk under the
        /// wall there) and the south cave approach, which the art
        /// bible keeps dark.
        /// </summary>
        internal static IReadOnlyList<Vector3> CreateQuayWallLampPositions(
            CityLayout layout)
        {
            var result = new List<Vector3>();
            CityRiverPlan plan = layout.River;
            if (plan.Segments.Count == 0)
            {
                return result.AsReadOnly();
            }

            for (int bankIndex = 0;
                 bankIndex < plan.Promenades.Count;
                 bankIndex++)
            {
                CityRiverPromenadeDescriptor promenade =
                    plan.Promenades[bankIndex];
                float x = promenade.WestBank
                    ? plan.Segments[0].WaterBounds.xMin
                    : plan.Segments[0].WaterBounds.xMax;
                for (float z = promenade.Bounds.yMin + QuayWallLampPitch;
                     z < promenade.Bounds.yMax - 5f;
                     z += QuayWallLampPitch)
                {
                    if (IsNearBridge(
                            plan,
                            z,
                            QuayWallLampBridgeClearance) ||
                        IsNearLanding(
                            plan,
                            promenade.WestBank,
                            x,
                            z,
                            QuayWallLampLandingClearance))
                    {
                        continue;
                    }

                    result.Add(new Vector3(
                        x,
                        SampleWaterDatumY(plan, z) +
                        QuayWallLampHeightAboveWater,
                        z));
                }
            }

            return result.AsReadOnly();
        }

        // The datum, not the visible top: the wall spans and the
        // lantern row both hang off the plan's water elevation, which
        // is globally linear, so the per-segment lerp is exact.
        private static float SampleWaterDatumY(
            CityRiverPlan plan,
            float z)
        {
            CityRiverSegmentDescriptor segment = plan.Segments[0];
            for (int index = 0; index < plan.Segments.Count; index++)
            {
                segment = plan.Segments[index];
                if (z <= segment.WaterBounds.yMax)
                {
                    break;
                }
            }

            float amount = Mathf.InverseLerp(
                segment.WaterBounds.yMin,
                segment.WaterBounds.yMax,
                z);
            return Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                amount);
        }

        private static void BuildFullQuayWallSpan(
            Transform parent,
            string name,
            float x,
            float zMin,
            float zMax,
            CityRiverSegmentDescriptor segment,
            float southBankY,
            float northBankY)
        {
            float startAmount = Mathf.InverseLerp(
                segment.WaterBounds.yMin,
                segment.WaterBounds.yMax,
                zMin);
            float endAmount = Mathf.InverseLerp(
                segment.WaterBounds.yMin,
                segment.WaterBounds.yMax,
                zMax);
            float startBankY = Mathf.Lerp(
                southBankY,
                northBankY,
                startAmount);
            float endBankY = Mathf.Lerp(
                southBankY,
                northBankY,
                endAmount);
            float startWaterY = Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                startAmount);
            float endWaterY = Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                endAmount);
            float height = Mathf.Max(
                startBankY - startWaterY,
                endBankY - endWaterY) + 0.32f;
            CreateBeamBetween(
                name,
                parent,
                new Vector3(
                    x,
                    (startBankY + startWaterY) * 0.5f - 0.08f,
                    zMin),
                new Vector3(
                    x,
                    (endBankY + endWaterY) * 0.5f - 0.08f,
                    zMax),
                QuayWallThickness,
                height,
                GraniteEdge,
                true,
                CityRiverSurfaceKind.Quay);
        }

        private static void BuildLoweredQuayWallSpan(
            Transform parent,
            string name,
            float x,
            float zMin,
            float zMax,
            CityRiverSegmentDescriptor segment,
            float platformY)
        {
            float startAmount = Mathf.InverseLerp(
                segment.WaterBounds.yMin,
                segment.WaterBounds.yMax,
                zMin);
            float endAmount = Mathf.InverseLerp(
                segment.WaterBounds.yMin,
                segment.WaterBounds.yMax,
                zMax);
            float startBottomY = Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                startAmount) +
                CitySurfaceDescriptor.WaterTopOffset - 0.24f;
            float endBottomY = Mathf.Lerp(
                segment.SouthWaterY,
                segment.NorthWaterY,
                endAmount) +
                CitySurfaceDescriptor.WaterTopOffset - 0.24f;
            float topY = platformY - 0.04f;
            float height = Mathf.Max(
                topY - startBottomY,
                topY - endBottomY);
            CreateBeamBetween(
                name,
                parent,
                new Vector3(x, (topY + startBottomY) * 0.5f, zMin),
                new Vector3(x, (topY + endBottomY) * 0.5f, zMax),
                QuayWallThickness,
                Mathf.Max(0.16f, height),
                GraniteEdge,
                true,
                CityRiverSurfaceKind.Quay);
        }

        /// <summary>
        /// Keeps the wall's landward seat fixed under the iron rail while
        /// bringing its visible face slightly into the channel. Paving,
        /// landing and submerged-bed boxes all terminate at the water edge;
        /// a proud Quay face hides those side faces instead of sharing their
        /// depth plane and flickering against them.
        /// </summary>
        private static float ResolveQuayWallCenterX(
            float waterEdgeX,
            bool westBank)
        {
            return waterEdgeX +
                   (westBank
                       ? -QuayWallCenterOffset
                       : QuayWallCenterOffset);
        }

        private static void BuildTransverseQuayRail(
            Transform parent,
            string name,
            float xMin,
            float xMax,
            float z,
            float baseY)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            float width = xMax - xMin;
            float centerX = (xMin + xMax) * 0.5f;
            CreateBox(
                "Top Rail",
                root,
                new Vector3(centerX, baseY + RailHeight, z),
                new Vector3(width, RailThickness, RailThickness),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            CreateBox(
                "Middle Rail",
                root,
                new Vector3(centerX, baseY + 0.55f, z),
                new Vector3(width, 0.11f, 0.11f),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            int postCount = Mathf.Max(1, Mathf.CeilToInt(width / 2.8f));
            for (int index = 0; index <= postCount; index++)
            {
                float x = Mathf.Lerp(xMin, xMax, index / (float)postCount);
                CreateBox(
                    $"Rail Post {index + 1}",
                    root,
                    new Vector3(x, baseY + RailHeight * 0.5f, z),
                    new Vector3(RailThickness, RailHeight, RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
            }
        }

        private static void BuildUpperPlatformCutGuards(
            Transform parent,
            CityRiverLandingDescriptor landing,
            float landwardEdgeX,
            float terminalZ)
        {
            Transform root = new GameObject(
                "Upper Platform Cut Guards").transform;
            root.SetParent(parent, false);
            CreateBox(
                "Upper Landward Top Rail",
                root,
                new Vector3(
                    landwardEdgeX,
                    landing.UpperY + RailHeight,
                    landing.PlatformBounds.center.y),
                new Vector3(
                    RailThickness,
                    RailThickness,
                    landing.PlatformBounds.height),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            CreateBox(
                "Upper Landward Middle Rail",
                root,
                new Vector3(
                    landwardEdgeX,
                    landing.UpperY + 0.55f,
                    landing.PlatformBounds.center.y),
                new Vector3(
                    0.11f,
                    0.11f,
                    landing.PlatformBounds.height),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            CreateBox(
                "Upper Terminal Top Rail",
                root,
                new Vector3(
                    landing.PlatformBounds.center.x,
                    landing.UpperY + RailHeight,
                    terminalZ),
                new Vector3(
                    landing.PlatformBounds.width,
                    RailThickness,
                    RailThickness),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            CreateBox(
                "Upper Terminal Middle Rail",
                root,
                new Vector3(
                    landing.PlatformBounds.center.x,
                    landing.UpperY + 0.55f,
                    terminalZ),
                new Vector3(
                    landing.PlatformBounds.width,
                    0.11f,
                    0.11f),
                Iron,
                true,
                CityRiverSurfaceKind.Iron);
            for (int post = -1; post <= 1; post++)
            {
                float z = landing.PlatformBounds.center.y +
                          post * landing.PlatformBounds.height * 0.42f;
                CreateBox(
                    $"Upper Landward Post {post + 2}",
                    root,
                    new Vector3(
                        landwardEdgeX,
                        landing.UpperY + RailHeight * 0.5f,
                        z),
                    new Vector3(
                        RailThickness,
                        RailHeight,
                        RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
                float x = landing.PlatformBounds.center.x +
                          post * landing.PlatformBounds.width * 0.42f;
                CreateBox(
                    $"Upper Terminal Post {post + 2}",
                    root,
                    new Vector3(
                        x,
                        landing.UpperY + RailHeight * 0.5f,
                        terminalZ),
                    new Vector3(
                        RailThickness,
                        RailHeight,
                        RailThickness),
                    Iron,
                    true,
                    CityRiverSurfaceKind.Iron);
            }
        }

        private static Rect ResolvePhysicalPromenadeBounds(
            CityRiverPlan plan,
            CityRiverPromenadeDescriptor promenade)
        {
            if (plan.Segments.Count == 0)
            {
                return promenade.Bounds;
            }

            Rect waterBounds = plan.Segments[0].WaterBounds;
            return promenade.WestBank
                ? Rect.MinMaxRect(
                    promenade.Bounds.xMin,
                    promenade.Bounds.yMin,
                    waterBounds.xMin,
                    promenade.Bounds.yMax)
                : Rect.MinMaxRect(
                    waterBounds.xMax,
                    promenade.Bounds.yMin,
                    promenade.Bounds.xMax,
                    promenade.Bounds.yMax);
        }

        private static void BuildSlopedRailSpan(
            Transform parent,
            string name,
            float x,
            float zMin,
            float zMax,
            float southY,
            float northY,
            Color color)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            CreateBeamBetween(
                "Top Rail",
                root,
                new Vector3(x, southY + RailHeight, zMin),
                new Vector3(x, northY + RailHeight, zMax),
                RailThickness,
                RailThickness,
                color,
                true,
                CityRiverSurfaceKind.Iron);
            CreateBeamBetween(
                "Middle Rail",
                root,
                new Vector3(x, southY + 0.55f, zMin),
                new Vector3(x, northY + 0.55f, zMax),
                0.11f,
                0.11f,
                color,
                true,
                CityRiverSurfaceKind.Iron);
            int postCount = Mathf.Max(1, Mathf.CeilToInt((zMax - zMin) / 2.8f));
            for (int index = 0; index <= postCount; index++)
            {
                float amount = index / (float)postCount;
                float z = Mathf.Lerp(zMin, zMax, amount);
                float y = Mathf.Lerp(southY, northY, amount);
                CreateBox(
                    $"Rail Post {index + 1}",
                    root,
                    new Vector3(x, y + RailHeight * 0.5f, z),
                    new Vector3(RailThickness, RailHeight, RailThickness),
                    color,
                    true,
                    CityRiverSurfaceKind.Iron);
            }
        }

        private static void AddRailPostsAlongX(
            ICollection<Bounds> target,
            AxisRange span,
            float z,
            float baseY,
            float spacing)
        {
            int count = Mathf.Max(
                1,
                Mathf.CeilToInt((span.Maximum - span.Minimum) / spacing));
            for (int index = 0; index <= count; index++)
            {
                float x = Mathf.Lerp(
                    span.Minimum,
                    span.Maximum,
                    index / (float)count);
                target.Add(new Bounds(
                    new Vector3(x, baseY + RailHeight * 0.5f, z),
                    new Vector3(RailThickness, RailHeight, RailThickness)));
            }
        }

        private static List<Rect> CreateLandingCuts(
            CityRiverPlan plan,
            bool westBank)
        {
            var result = new List<Rect>();
            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityRiverLandingDescriptor landing = plan.Landings[index];
                if (landing.WestBank != westBank)
                {
                    continue;
                }

                result.Add(landing.StairBounds);
                result.Add(landing.PlatformBounds);
            }

            return result;
        }

        private static List<AxisRange> CreateQuayOpeningRanges(
            CityRiverPlan plan,
            bool westBank)
        {
            var result = new List<AxisRange>();
            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityRiverLandingDescriptor landing = plan.Landings[index];
                if (landing.WestBank == westBank)
                {
                    result.Add(new AxisRange(
                        Mathf.Min(
                            landing.StairBounds.yMin,
                            landing.PlatformBounds.yMin) - 0.18f,
                        Mathf.Max(
                            landing.StairBounds.yMax,
                            landing.PlatformBounds.yMax) + 0.18f));
                }
            }

            for (int index = 0; index < plan.Bridges.Count; index++)
            {
                Rect deck = plan.Bridges[index].DeckBounds;
                result.Add(new AxisRange(
                    deck.yMin - 0.24f,
                    deck.yMax + 0.24f));
            }

            return result;
        }

        private static List<AxisRange> CreateBridgeLandingGaps(
            CityRiverPlan plan,
            string bridgeId,
            float minimum,
            float maximum)
        {
            var result = new List<AxisRange>();
            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityRiverLandingDescriptor landing = plan.Landings[index];
                if (!string.Equals(
                        landing.BridgeId,
                        bridgeId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                float gapMinimum = Mathf.Max(
                    minimum,
                    landing.StairBounds.xMin - 0.12f);
                float gapMaximum = Mathf.Min(
                    maximum,
                    landing.StairBounds.xMax + 0.12f);
                if (gapMaximum - gapMinimum >= MinimumParapetOpening)
                {
                    result.Add(new AxisRange(gapMinimum, gapMaximum));
                }
            }

            return result;
        }

        private static List<AxisRange> SubtractRanges(
            float minimum,
            float maximum,
            IReadOnlyList<AxisRange> sourceGaps)
        {
            var gaps = new List<AxisRange>(sourceGaps);
            gaps.Sort((left, right) => left.Minimum.CompareTo(right.Minimum));
            var result = new List<AxisRange>();
            float cursor = minimum;
            for (int index = 0; index < gaps.Count; index++)
            {
                float gapMin = Mathf.Clamp(gaps[index].Minimum, minimum, maximum);
                float gapMax = Mathf.Clamp(gaps[index].Maximum, minimum, maximum);
                if (gapMin > cursor + 0.01f)
                {
                    result.Add(new AxisRange(cursor, gapMin));
                }

                cursor = Mathf.Max(cursor, gapMax);
            }

            if (cursor < maximum - 0.01f)
            {
                result.Add(new AxisRange(cursor, maximum));
            }

            return result;
        }

        private static void SubtractFromAll(
            List<Rect> source,
            Rect cut)
        {
            var next = new List<Rect>();
            for (int index = 0; index < source.Count; index++)
            {
                Subtract(source[index], cut, next);
            }

            source.Clear();
            source.AddRange(next);
        }

        private static void Subtract(
            Rect source,
            Rect cut,
            ICollection<Rect> target)
        {
            float xMin = Mathf.Max(source.xMin, cut.xMin);
            float xMax = Mathf.Min(source.xMax, cut.xMax);
            float zMin = Mathf.Max(source.yMin, cut.yMin);
            float zMax = Mathf.Min(source.yMax, cut.yMax);
            if (xMax <= xMin || zMax <= zMin)
            {
                target.Add(source);
                return;
            }

            AddRect(target, source.xMin, source.yMin, xMin, source.yMax);
            AddRect(target, xMax, source.yMin, source.xMax, source.yMax);
            AddRect(target, xMin, source.yMin, xMax, zMin);
            AddRect(target, xMin, zMax, xMax, source.yMax);
        }

        private static void AddRect(
            ICollection<Rect> target,
            float xMin,
            float zMin,
            float xMax,
            float zMax)
        {
            if (xMax - xMin > 0.01f && zMax - zMin > 0.01f)
            {
                target.Add(Rect.MinMaxRect(xMin, zMin, xMax, zMax));
            }
        }

        private static float SamplePromenadeY(
            CityRiverPromenadeDescriptor promenade,
            float z)
        {
            float amount = Mathf.InverseLerp(
                promenade.Bounds.yMin,
                promenade.Bounds.yMax,
                z);
            return Mathf.Lerp(promenade.SouthY, promenade.NorthY, amount);
        }

        private static float ResolveBridgeWaterY(
            CityLayout layout,
            CityRiverBridgeDescriptor bridge)
        {
            int z = bridge.Definition.CrossingEdge.A.y;
            return CityRiverPlanner.ResolveWaterY(
                layout.River.Definition,
                z);
        }

        private static bool IsNearBridge(
            CityRiverPlan plan,
            float z,
            float clearance)
        {
            for (int index = 0; index < plan.Bridges.Count; index++)
            {
                if (Mathf.Abs(plan.Bridges[index].DeckBounds.center.y - z) <
                    clearance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNearLanding(
            CityRiverPlan plan,
            bool westBank,
            float x,
            float z,
            float clearance)
        {
            for (int index = 0; index < plan.Landings.Count; index++)
            {
                CityRiverLandingDescriptor landing = plan.Landings[index];
                if (landing.WestBank == westBank &&
                    (ContainsWithClearance(
                         landing.StairBounds, x, z, clearance) ||
                     ContainsWithClearance(
                         landing.PlatformBounds, x, z, clearance)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsWithClearance(
            Rect bounds,
            float x,
            float z,
            float clearance) =>
            x >= bounds.xMin - clearance &&
            x <= bounds.xMax + clearance &&
            z >= bounds.yMin - clearance &&
            z <= bounds.yMax + clearance;

        private static GameObject CreateSlopedSurface(
            string name,
            Transform parent,
            Rect bounds,
            float southTopY,
            float northTopY,
            float thickness,
            Color color,
            Material material,
            bool collider,
            CityRiverSurfaceKind? surface = null)
        {
            return CreateBeamBetween(
                name,
                parent,
                new Vector3(
                    bounds.center.x,
                    southTopY - thickness * 0.5f,
                    bounds.yMin),
                new Vector3(
                    bounds.center.x,
                    northTopY - thickness * 0.5f,
                    bounds.yMax),
                bounds.width,
                thickness,
                color,
                material,
                collider,
                surface);
        }

        private static GameObject CreateBeamBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float width,
            float height,
            Color color,
            bool collider,
            CityRiverSurfaceKind? surface = null)
        {
            return CreateBeamBetween(
                name,
                parent,
                start,
                end,
                width,
                height,
                color,
                null,
                collider,
                surface);
        }

        private static GameObject CreateBeamBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float width,
            float height,
            Color color,
            Material material,
            bool collider,
            CityRiverSurfaceKind? surface = null)
        {
            Vector3 delta = end - start;
            float length = delta.magnitude;
            if (length <= 0.001f)
            {
                return null;
            }

            GameObject result = material == null
                ? RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    (start + end) * 0.5f,
                    new Vector3(width, height, length),
                    color,
                    collider)
                : RuntimePrimitiveFactory.CreateMaterialBox(
                    name,
                    parent,
                    (start + end) * 0.5f,
                    new Vector3(width, height, length),
                    material,
                    collider);
            result.transform.localRotation = Quaternion.LookRotation(
                delta.normalized,
                Vector3.up);
            TextureSurface(result, surface, color);
            return result;
        }

        private static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color,
            bool collider,
            CityRiverSurfaceKind? surface = null)
        {
            GameObject result = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                position,
                size,
                color,
                collider);
            TextureSurface(result, surface, color);
            return result;
        }

        /// <summary>
        /// Gives one embankment or bridge primitive its sheet. A caller
        /// with nothing to sample - the water, the lamp glow - passes no
        /// surface and keeps its flat colour.
        /// </summary>
        private static void TextureSurface(
            GameObject instance,
            CityRiverSurfaceKind? surface,
            Color tint)
        {
            if (instance == null || !surface.HasValue)
            {
                return;
            }

            CityRiverSurfaceAppearance.Apply(
                instance.GetComponent<Renderer>(),
                surface.Value,
                tint);
        }

        private readonly struct AxisRange
        {
            internal AxisRange(float minimum, float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            internal float Minimum { get; }
            internal float Maximum { get; }
        }
    }
}
