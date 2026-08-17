using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityRiverWorldBuilder
    {
        internal const string RootName = "City River";

        private const float WaterThickness = 0.10f;
        private const float PromenadeThickness = 0.18f;
        private const float RailHeight = 1.05f;
        private const float RailThickness = 0.14f;
        private const float MinimumParapetOpening = 1.2f;
        internal const float SurfaceClearance = 0.03f;

        private static readonly Color Granite =
            new Color(0.34f, 0.36f, 0.34f);
        private static readonly Color GraniteEdge =
            new Color(0.25f, 0.28f, 0.27f);
        private static readonly Color Iron =
            new Color(0.075f, 0.10f, 0.105f);
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

        internal static GameObject Build(
            Transform parent,
            CityLayout layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!layout.River.IsEnabled)
            {
                return null;
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            CityRiverResources.SetNightFactor(
                CityNightGlowRegistry.NightFactor);
            CityRiverResources.SetRainIntensity(
                GameWeatherRules.EvaluateCurrent().RainIntensity);

            BuildWater(root, layout.River);
            BuildPromenades(root, layout);
            BuildRetainingWalls(root, layout);
            BuildUpperQuayRails(root, layout);
            BuildBridges(root, layout);
            BuildLandings(root, layout);
            BuildPromenadeLights(root, layout);
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
                CreateSlopedSurface(
                    $"River Water {segment.Cell.y}",
                    water,
                    segment.WaterBounds,
                    segment.SouthWaterY +
                    CitySurfaceDescriptor.WaterTopOffset,
                    segment.NorthWaterY +
                    CitySurfaceDescriptor.WaterTopOffset,
                    WaterThickness,
                    Color.white,
                    CityRiverResources.WaterMaterial,
                    false);
            }
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
                    float x = west
                        ? segment.WaterBounds.xMin - 0.22f
                        : segment.WaterBounds.xMax + 0.22f;
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
            CityLayout layout)
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

                BuildTransverseQuayRail(
                    rails,
                    $"{(promenade.WestBank ? "West" : "East")} " +
                    "Quay South End Rail",
                    physicalBounds.xMin,
                    physicalBounds.xMax,
                    physicalBounds.yMin,
                    SamplePromenadeY(promenade, physicalBounds.yMin));
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
            Color structure = bridge.Definition.Style == CityBridgeStyle.Works
                ? WorksSteel
                : MouthStone;
            Color accent = bridge.Definition.Style == CityBridgeStyle.Works
                ? WorksAccent
                : GraniteEdge;

            CreateBox(
                "Bridge Underside",
                root,
                new Vector3(span.center.x, deckY - 0.34f, span.center.y),
                new Vector3(
                    span.width,
                    0.52f,
                    span.height - SurfaceClearance * 2f),
                structure,
                true);
            CreateBox(
                "North Girder",
                root,
                new Vector3(span.center.x, deckY - 0.68f, span.yMax - 0.22f),
                new Vector3(span.width, 0.72f, 0.34f),
                accent,
                false);
            CreateBox(
                "South Girder",
                root,
                new Vector3(span.center.x, deckY - 0.68f, span.yMin + 0.22f),
                new Vector3(span.width, 0.72f, 0.34f),
                accent,
                false);

            float waterY = ResolveBridgeWaterY(layout, bridge);
            float pierHeight = Mathf.Max(0.5f, deckY - waterY - 0.42f);
            for (int pier = -1; pier <= 1; pier += 2)
            {
                CreateBox(
                    $"Bridge Pier {(pier < 0 ? "West" : "East")}",
                    root,
                    new Vector3(
                        span.center.x + pier * span.width * 0.30f,
                        waterY + pierHeight * 0.5f,
                        span.center.y),
                    new Vector3(0.82f, pierHeight, span.height - 1.2f),
                    structure,
                    true);
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
                structure);
            BuildBridgeRail(
                root,
                "Landing Parapet",
                bridge,
                innerZ,
                guardRange.Minimum,
                guardRange.Maximum,
                landingGaps,
                structure);
        }

        private static void BuildBridgeRail(
            Transform parent,
            string name,
            CityRiverBridgeDescriptor bridge,
            float z,
            float minimum,
            float maximum,
            IReadOnlyList<AxisRange> gaps,
            Color color)
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
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    color,
                    true);
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

            RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Timber Deck Planks",
                root,
                planks,
                Timber);
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

            RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Timber Bridge Structure",
                root,
                structure,
                TimberEdge,
                true);
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
            CreateBox(
                "Lower Waterside Platform",
                root,
                new Vector3(
                    landing.PlatformBounds.center.x,
                    landing.LowerY - 0.12f,
                    landing.PlatformBounds.center.y),
                new Vector3(
                    landing.PlatformBounds.width,
                    0.24f,
                    landing.PlatformBounds.height),
                Granite,
                true,
                CityRiverSurfaceKind.Paving);

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

        private static void BuildPromenadeLights(
            Transform parent,
            CityLayout layout)
        {
            Transform lights = new GameObject(
                "Embankment Lamps").transform;
            lights.SetParent(parent, false);
            var posts = new List<Bounds>();
            var bulbs = new List<Bounds>();
            IReadOnlyList<Vector3> positions = CreatePromenadeLampPositions(
                layout);
            for (int index = 0; index < positions.Count; index++)
            {
                Vector3 position = positions[index];
                posts.Add(new Bounds(
                    position + Vector3.up * 1.25f,
                    new Vector3(0.16f, 2.5f, 0.16f)));
                bulbs.Add(new Bounds(
                    position + Vector3.up * 2.62f,
                    new Vector3(0.42f, 0.24f, 0.42f)));
            }

            if (posts.Count == 0)
            {
                return;
            }

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
            GameObject glow = RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Embankment Lamp Glow",
                lights,
                bulbs,
                LampGlow,
                CityNightResources.EmissiveMaterial);
            CityNightGlowRegistry.Register(
                glow.GetComponent<Renderer>(),
                LampGlow);
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
                0.44f,
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
                0.44f,
                Mathf.Max(0.16f, height),
                GraniteEdge,
                true,
                CityRiverSurfaceKind.Quay);
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
        /// Gives one embankment primitive its sheet. The bridges pass no
        /// surface: their steel, stone and timber are their own styles,
        /// and the granite and iron sheets belong to the banks.
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
