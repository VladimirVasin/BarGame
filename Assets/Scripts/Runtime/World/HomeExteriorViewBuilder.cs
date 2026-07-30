using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Rebuilds a bounded, collider-free view of the player's real seeded
    /// street in Home coordinates. It deliberately creates no city gameplay
    /// root, player, camera, entrance or realtime street-light pool.
    /// </summary>
    public static class HomeExteriorViewBuilder
    {
        internal const float ExteriorMinimumX =
            PlayerHomeBalconyGeometry.HomeFacadeX +
            PlayerHomeBalconyGeometry.WallThickness *
            0.5f +
            0.01f;
        private const float StreetLampExteriorClearance =
            0.90f;
        private const float TrafficSignalExteriorClearance =
            0.65f;

        private static readonly Color Ground =
            new Color(0.105f, 0.135f, 0.125f);
        private static readonly Color Asphalt =
            new Color(0.155f, 0.175f, 0.175f);
        private static readonly Color ParkPath =
            new Color(0.31f, 0.28f, 0.21f);
        private static readonly Color WindowOff =
            new Color(0.018f, 0.028f, 0.032f);
        private static readonly Color ColdWindow =
            new Color(0.19f, 0.37f, 0.50f);
        private static readonly Color WarmWindow =
            new Color(0.72f, 0.38f, 0.15f);
        private static readonly Color BarWindow =
            new Color(1.08f, 0.57f, 0.21f);
        private static readonly Color TerminalHaze =
            new Color(0.050f, 0.073f, 0.071f);
        private static readonly Color TerminalHazeSide =
            new Color(0.042f, 0.061f, 0.060f);

        public static Transform Build(
            Transform parent,
            HomeBalconyLayoutPlan balcony,
            HomeExteriorContextPlan context)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Transform root =
                new GameObject("Home Exterior View").transform;
            root.SetParent(parent, false);

            BuildTerminalEnvironment(root, balcony);
            BuildRoads(root, balcony, context);
            BuildBuildings(root, context);
            BuildNightFixtures(root, context);
            return root;
        }

        private static void BuildTerminalEnvironment(
            Transform parent,
            HomeBalconyLayoutPlan balcony)
        {
            float radius =
                HomeExteriorContextPlanner.ViewRadius;
            float facadeX =
                PlayerHomeBalconyGeometry.HomeFacadeX;
            float groundWidth = radius * 2.5f;
            CreateExteriorBox(
                "Home Exterior Ground",
                parent,
                new Bounds(
                    new Vector3(
                        facadeX + radius * 0.18f,
                        balcony.StreetGroundY - 0.24f,
                        0f),
                    new Vector3(
                        groundWidth,
                        0.32f,
                        groundWidth)),
                Ground,
                CityNightResources.EmissiveMaterial);

            float horizonX = facadeX + radius + 22f;
            float horizonHeight = 25f;
            float horizonY =
                balcony.StreetGroundY +
                horizonHeight * 0.5f;
            CreateExteriorBox(
                "Home Exterior Terminal Haze",
                parent,
                new Bounds(
                    new Vector3(
                        horizonX,
                        horizonY,
                        0f),
                    new Vector3(
                        0.75f,
                        horizonHeight,
                        groundWidth + 10f)),
                TerminalHaze,
                CityNightResources.EmissiveMaterial);
            CreateExteriorBox(
                "Home Exterior Terminal Haze South",
                parent,
                new Bounds(
                    new Vector3(
                        facadeX + radius * 0.45f,
                        horizonY,
                        -groundWidth * 0.5f),
                    new Vector3(
                        radius * 1.55f,
                        horizonHeight,
                        0.75f)),
                TerminalHazeSide,
                CityNightResources.EmissiveMaterial);
            CreateExteriorBox(
                "Home Exterior Terminal Haze North",
                parent,
                new Bounds(
                    new Vector3(
                        facadeX + radius * 0.45f,
                        horizonY,
                        groundWidth * 0.5f),
                    new Vector3(
                        radius * 1.55f,
                        horizonHeight,
                        0.75f)),
                TerminalHazeSide,
                CityNightResources.EmissiveMaterial);
        }

        private static void BuildRoads(
            Transform parent,
            HomeBalconyLayoutPlan balcony,
            HomeExteriorContextPlan context)
        {
            var streets = new List<Bounds>(
                context.NearbyRoads.Count);
            var parkPaths = new List<Bounds>();
            for (int index = 0;
                 index < context.NearbyRoads.Count;
                 index++)
            {
                RoadEdge edge = context.NearbyRoads[index];
                Rect cityRect =
                    context.Layout.GetRoadRect(edge);
                Rect localRect =
                    PlayerHomeBalconyGeometry
                        .ToHomeLocalRect(
                            context.PlayerHome,
                            cityRect);
                var surface = new Bounds(
                    new Vector3(
                        localRect.center.x,
                        balcony.StreetGroundY,
                        localRect.center.y),
                    new Vector3(
                        localRect.width,
                        0.16f,
                        localRect.height));
                if (!TryClipToExteriorHalfSpace(
                        surface,
                        out Bounds exteriorSurface))
                {
                    continue;
                }

                if (context.Layout.GetPathKind(edge) ==
                    CityPathKind.ParkPath)
                {
                    parkPaths.Add(exteriorSurface);
                }
                else
                {
                    streets.Add(exteriorSurface);
                }
            }

            BuildCombinedBoxesIfAny(
                "Home Exterior Street Surfaces",
                parent,
                streets,
                Asphalt);
            BuildCombinedBoxesIfAny(
                "Home Exterior Park Paths",
                parent,
                parkPaths,
                ParkPath);
        }

        private static void BuildBuildings(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            Transform buildings =
                new GameObject(
                    "Home Exterior Building Silhouettes")
                    .transform;
            buildings.SetParent(parent, false);
            for (int index = 0;
                 index < context.NearbyLots.Count;
                 index++)
            {
                BuildingLot lot =
                    context.NearbyLots[index];
                if (lot.IsPlayerHome)
                {
                    continue;
                }

                Vector3 cityCenter =
                    lot.Center +
                    Vector3.up *
                    (lot.Height * 0.5f + 0.08f);
                Vector3 localCenter =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        cityCenter);
                Vector3 localSize =
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        new Vector3(
                            lot.Size.x,
                            lot.Height,
                            lot.Size.y));
                Color facade =
                    CreateNightFacadeColor(lot);
                if (!TryClipToExteriorHalfSpace(
                        new Bounds(
                            localCenter,
                            localSize),
                        out Bounds exteriorMass))
                {
                    continue;
                }

                Transform building =
                    new GameObject(
                        lot.IsBar
                            ? $"Exterior Bar {lot.BarId}"
                            : $"Exterior Building {lot.Cell.x}-{lot.Cell.y}")
                        .transform;
                building.SetParent(buildings, false);
                RuntimePrimitiveFactory.CreateBox(
                    "Exterior Building Mass",
                    building,
                    exteriorMass.center,
                    exteriorMass.size,
                    facade,
                    CityNightResources.EmissiveMaterial,
                    false);

                Vector3 roofCenter =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        lot.Center +
                        Vector3.up *
                        (lot.Height + 0.22f));
                Vector3 roofSize =
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        new Vector3(
                            lot.Size.x + 0.35f,
                            0.28f,
                            lot.Size.y + 0.35f));
                if (TryClipToExteriorHalfSpace(
                        new Bounds(
                            roofCenter,
                            roofSize),
                        out Bounds exteriorRoof))
                {
                    RuntimePrimitiveFactory.CreateBox(
                        "Exterior Roof",
                        building,
                        exteriorRoof.center,
                        exteriorRoof.size,
                        Darken(facade, 0.055f),
                        CityNightResources.EmissiveMaterial,
                        false);
                }
                BuildWindowBands(
                    building,
                    context,
                    lot);
            }
        }

        private static void BuildWindowBands(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            int floorCount = Mathf.Clamp(
                Mathf.FloorToInt(lot.Height / 2.6f),
                1,
                4);
            for (int floor = 0;
                 floor < floorCount;
                 floor++)
            {
                float y = 1.5f + floor * 2.35f;
                if (y >= lot.Height - 0.35f)
                {
                    break;
                }

                Vector3 frontPosition;
                Vector3 backPosition;
                Vector3 rowSize;
                if (lot.IsBar || lot.IsPlayerHome)
                {
                    Vector3 frontage = new Vector3(
                        lot.FrontageDirection.x,
                        0f,
                        lot.FrontageDirection.y);
                    bool frontageIsX =
                        Mathf.Abs(frontage.x) > 0.5f;
                    float facadeDistance =
                        frontageIsX
                            ? lot.Size.x * 0.5f + 0.012f
                            : lot.Size.y * 0.5f + 0.012f;
                    Vector3 offset =
                        frontage * facadeDistance;
                    frontPosition =
                        lot.Center +
                        offset +
                        Vector3.up * y;
                    backPosition =
                        lot.Center -
                        offset +
                        Vector3.up * y;
                    rowSize =
                        frontageIsX
                            ? new Vector3(
                                0.035f,
                                0.7f,
                                lot.Size.y * 0.68f)
                            : new Vector3(
                                lot.Size.x * 0.68f,
                                0.7f,
                                0.035f);
                }
                else
                {
                    frontPosition =
                        lot.Center +
                        new Vector3(
                            0f,
                            y,
                            -(lot.Size.y * 0.5f +
                              0.012f));
                    backPosition =
                        lot.Center +
                        new Vector3(
                            0f,
                            y,
                            lot.Size.y * 0.5f +
                            0.012f);
                    rowSize = new Vector3(
                        lot.Size.x * 0.68f,
                        0.7f,
                        0.035f);
                }

                BuildWindowRow(
                    parent,
                    context,
                    lot,
                    frontPosition,
                    rowSize,
                    floor,
                    0);
                BuildWindowRow(
                    parent,
                    context,
                    lot,
                    backPosition,
                    rowSize,
                    floor,
                    1);
            }
        }

        private static void BuildWindowRow(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot,
            Vector3 cityPosition,
            Vector3 cityRowSize,
            int floor,
            int side)
        {
            bool runsAlongX =
                cityRowSize.x > cityRowSize.z;
            float rowLength =
                runsAlongX
                    ? cityRowSize.x
                    : cityRowSize.z;
            int paneCount = Mathf.Clamp(
                Mathf.FloorToInt(rowLength / 1.90f),
                4,
                8);
            const float gap = 0.28f;
            float paneLength =
                (rowLength -
                 (paneCount - 1) * gap) /
                paneCount;
            float paneHeight =
                lot.IsBar || lot.IsPlayerHome
                    ? 0.60f
                    : 0.48f;

            for (int pane = 0;
                 pane < paneCount;
                 pane++)
            {
                float offset =
                    -rowLength * 0.5f +
                    paneLength * 0.5f +
                    pane *
                    (paneLength + gap);
                Vector3 panePosition =
                    cityPosition +
                    (runsAlongX
                        ? new Vector3(offset, 0f, 0f)
                        : new Vector3(0f, 0f, offset));
                Vector3 paneSize =
                    runsAlongX
                        ? new Vector3(
                            paneLength,
                            paneHeight,
                            cityRowSize.z)
                        : new Vector3(
                            cityRowSize.x,
                            paneHeight,
                            paneLength);
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        panePosition);
                Vector3 localSize =
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        paneSize);
                if (!TryClipToExteriorHalfSpace(
                        new Bounds(
                            localPosition,
                            localSize),
                        out Bounds exteriorPane))
                {
                    continue;
                }

                Color color = ResolveWindowColor(
                    lot,
                    context.Layout.Seed,
                    floor,
                    pane,
                    side,
                    out bool emissive);

                if (emissive)
                {
                    RuntimePrimitiveFactory.CreateBox(
                        $"Exterior Window {floor}-{side}-{pane}",
                        parent,
                        exteriorPane.center,
                        exteriorPane.size,
                        color,
                        CityNightResources.EmissiveMaterial,
                        false);
                }
                else
                {
                    RuntimePrimitiveFactory.CreateBox(
                        $"Exterior Window {floor}-{side}-{pane}",
                        parent,
                        exteriorPane.center,
                        exteriorPane.size,
                        color,
                        false);
                }
            }
        }

        private static void BuildNightFixtures(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            var lamps =
                new List<StreetLampDescriptor>(
                    context.NearbyStreetLamps.Count);
            for (int index = 0;
                 index <
                 context.NearbyStreetLamps.Count;
                 index++)
            {
                StreetLampDescriptor source =
                    context.NearbyStreetLamps[index];
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry
                        .ToHomeLocal(
                            context.PlayerHome,
                            source.Position);
                if (localPosition.x -
                    StreetLampExteriorClearance <
                    ExteriorMinimumX)
                {
                    continue;
                }

                lamps.Add(new StreetLampDescriptor(
                    source.Edge,
                    source.EdgeT,
                    source.Side,
                    localPosition,
                    PlayerHomeBalconyGeometry
                        .ToHomeLocalDirection(
                            context.PlayerHome,
                            source.Forward)));
            }

            var signals =
                new List<TrafficSignalDescriptor>(
                    context.NearbyTrafficSignals.Count);
            for (int index = 0;
                 index <
                 context.NearbyTrafficSignals.Count;
                 index++)
            {
                TrafficSignalDescriptor source =
                    context.NearbyTrafficSignals[index];
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry
                        .ToHomeLocal(
                            context.PlayerHome,
                            source.Position);
                if (localPosition.x -
                    TrafficSignalExteriorClearance <
                    ExteriorMinimumX)
                {
                    continue;
                }

                signals.Add(new TrafficSignalDescriptor(
                    source.IntersectionNode,
                    source.PairIndex,
                    localPosition,
                    PlayerHomeBalconyGeometry
                        .ToHomeLocalDirection(
                            context.PlayerHome,
                            source.Forward),
                    source.BlinkPhase01));
            }

            var fixturePlan =
                new CityNightFixturePlan(
                    lamps,
                    signals);
            CityNightWorldResult result =
                CityNightWorldBuilder.Build(
                    parent,
                    fixturePlan,
                    Array.Empty<BarEntrance>());
            result.Root.name =
                "Home Exterior Night Fixtures";
        }

        private static Color ResolveWindowColor(
            BuildingLot lot,
            int citySeed,
            int floor,
            int pane,
            int side,
            out bool emissive)
        {
            if (lot.IsBar)
            {
                emissive = true;
                return BarWindow;
            }

            uint hash = StableHash(
                citySeed,
                lot.Cell.x,
                lot.Cell.y,
                floor,
                pane,
                side);
            int selection =
                (int)(hash % 100u);
            if (selection < 65)
            {
                emissive = false;
                return WindowOff;
            }

            emissive = true;
            return selection < 90
                ? ColdWindow
                : WarmWindow;
        }

        private static Color CreateNightFacadeColor(
            BuildingLot lot)
        {
            float value =
                (lot.Color.r +
                 lot.Color.g +
                 lot.Color.b) /
                3f;
            float nightValue =
                Mathf.Clamp(
                    value * 0.46f,
                    0.085f,
                    0.24f);
            if (lot.IsBar)
            {
                return new Color(
                    nightValue * 1.08f,
                    nightValue * 0.78f,
                    nightValue * 0.72f,
                    1f);
            }

            return new Color(
                nightValue * 0.88f,
                nightValue,
                nightValue * 0.96f,
                1f);
        }

        private static Color Darken(
            Color color,
            float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
        }

        private static uint StableHash(
            int seed,
            int x,
            int z,
            int floor,
            int pane,
            int side)
        {
            uint hash =
                unchecked((uint)seed) ^
                0x9E3779B9u;
            hash = Mix(
                hash,
                unchecked((uint)x));
            hash = Mix(
                hash,
                unchecked((uint)z));
            hash = Mix(
                hash,
                unchecked((uint)floor));
            hash = Mix(
                hash,
                unchecked((uint)pane));
            return Mix(
                hash,
                unchecked((uint)side));
        }

        private static uint Mix(
            uint first,
            uint second)
        {
            uint hash = first;
            hash ^=
                second +
                0x85EBCA6Bu +
                (hash << 6) +
                (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u
                ? 0xA341316Cu
                : hash;
        }

        internal static bool TryClipToExteriorHalfSpace(
            Bounds source,
            out Bounds clipped)
        {
            Vector3 maximum = source.max;
            if (maximum.x <= ExteriorMinimumX)
            {
                clipped = default;
                return false;
            }

            Vector3 minimum = source.min;
            minimum.x =
                Mathf.Max(
                    minimum.x,
                    ExteriorMinimumX);
            clipped = new Bounds();
            clipped.SetMinMax(
                minimum,
                maximum);
            return true;
        }

        private static void CreateExteriorBox(
            string name,
            Transform parent,
            Bounds bounds,
            Color color,
            Material material)
        {
            if (!TryClipToExteriorHalfSpace(
                    bounds,
                    out Bounds exteriorBounds))
            {
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                exteriorBounds.center,
                exteriorBounds.size,
                color,
                material,
                false);
        }

        private static void BuildCombinedBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes,
            Color color)
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
                CityNightResources.EmissiveMaterial);
        }
    }
}
