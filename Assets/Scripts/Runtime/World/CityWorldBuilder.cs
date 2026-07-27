using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityWorldBuilder
    {
        private static readonly Color Asphalt = new Color(0.175f, 0.195f, 0.195f);
        private static readonly Color RoadPaint = new Color(0.58f, 0.52f, 0.34f);
        private static readonly Color Ground = new Color(0.170f, 0.205f, 0.185f);
        private static readonly Color Sidewalk = new Color(0.31f, 0.33f, 0.305f);
        private static readonly Color WindowOff = new Color(0.025f, 0.035f, 0.040f);
        private static readonly Color ColdWindow = new Color(0.24f, 0.43f, 0.56f);
        private static readonly Color WarmWindow = new Color(0.88f, 0.48f, 0.20f);
        private static readonly Color BarWindow = new Color(1.35f, 0.72f, 0.28f);
        private static readonly Color BarTrim = new Color(0.84f, 0.55f, 0.18f);
        private static readonly Color BarAwning = new Color(0.24f, 0.018f, 0.045f);
        private static readonly Color DoorColor = new Color(0.055f, 0.025f, 0.022f);

        public static CityWorldResult Build(
            Transform parent,
            CityLayout layout,
            CityGenerationSettings settings)
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
            Transform world = new GameObject("Generated City").transform;
            world.SetParent(parent, false);
            Material emissiveMaterial = CityNightResources.EmissiveMaterial;
            RoadWalkableArea walkableArea = RoadWalkableArea.FromLayout(layout);
            Bounds bounds = BuildGround(world, layout, settings);
            BuildRoads(world, layout, settings);

            var bars = new List<BarEntrance>(settings.BarCount);
            for (int i = 0; i < layout.BuildingLots.Count; i++)
            {
                BuildBuilding(
                    world,
                    layout.BuildingLots[i],
                    layout.Seed,
                    emissiveMaterial,
                    walkableArea,
                    bars);
            }

            return new CityWorldResult(
                world.gameObject,
                walkableArea,
                bars,
                bounds);
        }

        private static Bounds BuildGround(
            Transform parent,
            CityLayout layout,
            CityGenerationSettings settings)
        {
            Vector3 minimum = layout.GetNodeWorldPosition(Vector2Int.zero);
            Vector3 maximum = layout.GetNodeWorldPosition(layout.BlockCount);
            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 size = new Vector3(
                maximum.x - minimum.x + settings.RoadWidth + 10f,
                0.32f,
                maximum.z - minimum.z + settings.RoadWidth + 10f);
            RuntimePrimitiveFactory.CreateBox(
                "City Ground",
                parent,
                center + (Vector3.down * 0.24f),
                size,
                Ground);
            return new Bounds(center, new Vector3(size.x, 20f, size.z));
        }

        private static void BuildRoads(
            Transform parent,
            CityLayout layout,
            CityGenerationSettings settings)
        {
            Transform roads = new GameObject("Road Network").transform;
            roads.SetParent(parent, false);

            for (int i = 0; i < layout.RoadEdges.Count; i++)
            {
                RoadEdge edge = layout.RoadEdges[i];
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Vector3 center = (start + end) * 0.5f;
                Vector3 delta = end - start;
                Vector3 size = edge.IsHorizontal
                    ? new Vector3(Mathf.Abs(delta.x) + settings.RoadWidth, 0.16f, settings.RoadWidth)
                    : new Vector3(settings.RoadWidth, 0.16f, Mathf.Abs(delta.z) + settings.RoadWidth);
                RuntimePrimitiveFactory.CreateBox(
                    $"Road {edge}",
                    roads,
                    center,
                    size,
                    Asphalt);
                BuildRoadDashes(roads, start, end, edge.IsHorizontal);
            }
        }

        private static void BuildRoadDashes(
            Transform parent,
            Vector3 start,
            Vector3 end,
            bool horizontal)
        {
            float length = Vector3.Distance(start, end);
            int dashCount = Mathf.Max(2, Mathf.FloorToInt(length / 5f));

            for (int i = 0; i < dashCount; i++)
            {
                float t = (i + 0.5f) / dashCount;
                Vector3 position = Vector3.Lerp(start, end, t);
                Vector3 size = horizontal
                    ? new Vector3(Mathf.Min(2.1f, length / dashCount * 0.48f), 0.025f, 0.13f)
                    : new Vector3(0.13f, 0.025f, Mathf.Min(2.1f, length / dashCount * 0.48f));
                RuntimePrimitiveFactory.CreateBox(
                    "Road Dash",
                    parent,
                    position + (Vector3.up * 0.095f),
                    size,
                    RoadPaint,
                    false);
            }
        }

        private static void BuildBuilding(
            Transform parent,
            BuildingLot lot,
            int citySeed,
            Material emissiveMaterial,
            RoadWalkableArea walkableArea,
            IList<BarEntrance> bars)
        {
            Transform building = new GameObject(
                lot.IsBar ? $"Bar {lot.BarId}" : $"Building {lot.Cell.x}-{lot.Cell.y}").transform;
            building.SetParent(parent, false);

            Color facadeColor = CreateNightFacadeColor(lot);
            RuntimePrimitiveFactory.CreateBox(
                "Building Mass",
                building,
                lot.Center + (Vector3.up * (lot.Height * 0.5f + 0.08f)),
                new Vector3(lot.Size.x, lot.Height, lot.Size.y),
                facadeColor);
            RuntimePrimitiveFactory.CreateBox(
                "Roof",
                building,
                lot.Center + (Vector3.up * (lot.Height + 0.22f)),
                new Vector3(lot.Size.x + 0.35f, 0.28f, lot.Size.y + 0.35f),
                Darken(facadeColor, 0.055f),
                false);
            BuildWindowBands(building, lot, citySeed, emissiveMaterial);

            if (!lot.IsBar)
            {
                return;
            }

            BuildBarFront(building, lot, walkableArea, bars);
        }

        private static void BuildWindowBands(
            Transform parent,
            BuildingLot lot,
            int citySeed,
            Material emissiveMaterial)
        {
            int floorCount = Mathf.Clamp(Mathf.FloorToInt(lot.Height / 2.6f), 1, 4);
            for (int floor = 0; floor < floorCount; floor++)
            {
                float y = 1.5f + (floor * 2.35f);
                if (y >= lot.Height - 0.35f)
                {
                    break;
                }

                Vector3 frontPosition;
                Vector3 backPosition;
                Vector3 windowSize;
                if (lot.IsBar)
                {
                    Vector3 frontage = new Vector3(
                        lot.FrontageDirection.x,
                        0f,
                        lot.FrontageDirection.y);
                    bool frontageIsX = Mathf.Abs(frontage.x) > 0.5f;
                    float facadeDistance = frontageIsX
                        ? lot.Size.x * 0.5f + 0.012f
                        : lot.Size.y * 0.5f + 0.012f;
                    Vector3 facadeOffset = frontage * facadeDistance;
                    frontPosition =
                        lot.Center + facadeOffset + (Vector3.up * y);
                    backPosition =
                        lot.Center - facadeOffset + (Vector3.up * y);
                    windowSize = frontageIsX
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
                    frontPosition = lot.Center + new Vector3(
                        0f,
                        y,
                        -(lot.Size.y * 0.5f + 0.012f));
                    backPosition = lot.Center + new Vector3(
                        0f,
                        y,
                        lot.Size.y * 0.5f + 0.012f);
                    windowSize = new Vector3(
                        lot.Size.x * 0.68f,
                        0.7f,
                        0.035f);
                }

                BuildWindowRow(
                    parent,
                    "Front Windows",
                    frontPosition,
                    windowSize,
                    lot,
                    citySeed,
                    floor,
                    0,
                    emissiveMaterial);
                BuildWindowRow(
                    parent,
                    "Back Windows",
                    backPosition,
                    windowSize,
                    lot,
                    citySeed,
                    floor,
                    1,
                    emissiveMaterial);
            }
        }

        private static void BuildWindowRow(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 rowSize,
            BuildingLot lot,
            int citySeed,
            int floor,
            int side,
            Material emissiveMaterial)
        {
            Transform row = new GameObject(name).transform;
            row.SetParent(parent, false);
            row.localPosition = position;

            bool runsAlongX = rowSize.x > rowSize.z;
            float rowLength = runsAlongX ? rowSize.x : rowSize.z;
            int paneCount = Mathf.Clamp(Mathf.FloorToInt(rowLength / 1.90f), 4, 8);
            const float gap = 0.28f;
            float paneLength =
                (rowLength - ((paneCount - 1) * gap)) / paneCount;
            float paneHeight = lot.IsBar ? 0.60f : 0.48f;

            for (int pane = 0; pane < paneCount; pane++)
            {
                float offset =
                    -rowLength * 0.5f +
                    paneLength * 0.5f +
                    pane * (paneLength + gap);
                Vector3 panePosition = runsAlongX
                    ? new Vector3(offset, 0f, 0f)
                    : new Vector3(0f, 0f, offset);
                Vector3 paneSize = runsAlongX
                    ? new Vector3(paneLength, paneHeight, rowSize.z)
                    : new Vector3(rowSize.x, paneHeight, paneLength);
                Color color = ResolveWindowColor(
                    lot,
                    citySeed,
                    floor,
                    pane,
                    side,
                    out bool emissive);

                if (emissive)
                {
                    RuntimePrimitiveFactory.CreateBox(
                        $"Window {floor}-{pane}",
                        row,
                        panePosition,
                        paneSize,
                        color,
                        emissiveMaterial,
                        false);
                }
                else
                {
                    RuntimePrimitiveFactory.CreateBox(
                        $"Window {floor}-{pane}",
                        row,
                        panePosition,
                        paneSize,
                        color,
                        false);
                }
            }
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
            int selection = (int)(hash % 100u);
            if (selection < 65)
            {
                emissive = false;
                return WindowOff;
            }

            emissive = true;
            return selection < 90 ? ColdWindow : WarmWindow;
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
            Vector3 doorCenter = lot.DoorPosition + (direction * 0.045f) + (Vector3.up * 1.05f);
            Vector3 doorSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(0.12f, 2.1f, 1.45f)
                : new Vector3(1.45f, 2.1f, 0.12f);

            RuntimePrimitiveFactory.CreateBox(
                "Bar Door",
                parent,
                doorCenter,
                doorSize,
                DoorColor,
                false);
            BuildBarEntranceFrame(parent, lot.DoorPosition, direction);
            BuildBarLandmark(parent, lot, direction);

            Vector3 apronCenter = (lot.DoorPosition + lot.ReturnPosition) * 0.5f;
            float apronLength = Vector3.Distance(lot.DoorPosition, lot.ReturnPosition);
            Vector3 apronSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(apronLength, 0.08f, 2.25f)
                : new Vector3(2.25f, 0.08f, apronLength);
            RuntimePrimitiveFactory.CreateBox(
                "Bar Entrance Walkway",
                parent,
                apronCenter + (Vector3.up * 0.10f),
                apronSize,
                Sidewalk);
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
                lot.ReturnPosition + (Vector3.up * 0.12f));
            bars.Add(entrance);
        }

        private static void BuildBarEntranceFrame(
            Transform parent,
            Vector3 doorPosition,
            Vector3 direction)
        {
            Vector3 tangent = new Vector3(-direction.z, 0f, direction.x);
            Vector3 verticalSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(0.18f, 2.35f, 0.16f)
                : new Vector3(0.16f, 2.35f, 0.18f);
            Vector3 headerSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(0.18f, 0.22f, 2.05f)
                : new Vector3(2.05f, 0.22f, 0.18f);
            Vector3 canopySize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(0.82f, 0.18f, 2.85f)
                : new Vector3(2.85f, 0.18f, 0.82f);

            for (int side = -1; side <= 1; side += 2)
            {
                RuntimePrimitiveFactory.CreateBox(
                    "Bar Door Frame",
                    parent,
                    doorPosition +
                    (direction * 0.10f) +
                    (tangent * side * 0.86f) +
                    (Vector3.up * 1.14f),
                    verticalSize,
                    BarTrim,
                    false);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Bar Door Header",
                parent,
                doorPosition + (direction * 0.10f) + (Vector3.up * 2.30f),
                headerSize,
                BarTrim,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Bar Entrance Canopy",
                parent,
                doorPosition + (direction * 0.38f) + (Vector3.up * 2.52f),
                canopySize,
                BarTrim,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Bar Entrance Canopy Inset",
                parent,
                doorPosition + (direction * 0.40f) + (Vector3.up * 2.46f),
                Vector3.Scale(
                    canopySize,
                    new Vector3(0.88f, 0.55f, 0.88f)),
                BarAwning,
                false);
        }

        private static void BuildBarLandmark(
            Transform parent,
            BuildingLot lot,
            Vector3 direction)
        {
            Vector3 markerPosition =
                lot.DoorPosition +
                (direction * 0.74f) +
                (Vector3.up * 3.42f);
            Vector3 bracketSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(1.25f, 0.10f, 0.10f)
                : new Vector3(0.10f, 0.10f, 1.25f);
            RuntimePrimitiveFactory.CreateBox(
                "Bar Sign Bracket",
                parent,
                lot.DoorPosition +
                (direction * 0.34f) +
                (Vector3.up * 4.10f),
                bracketSize,
                BarTrim,
                false);

            GameObject markerObject = new GameObject("Bar Landmark Marker");
            markerObject.transform.SetParent(parent, false);
            markerObject.transform.position = markerPosition;
            BarBuildingMarker marker =
                markerObject.AddComponent<BarBuildingMarker>();
            marker.Initialize(lot.BarId, Camera.main);
        }

        private static Rect RectFromCenter(Vector3 center, float width, float depth)
        {
            return Rect.MinMaxRect(
                center.x - (width * 0.5f),
                center.z - (depth * 0.5f),
                center.x + (width * 0.5f),
                center.z + (depth * 0.5f));
        }

        private static Color CreateNightFacadeColor(BuildingLot lot)
        {
            if (lot.IsBar)
            {
                return new Color(
                    lot.Color.r * 0.70f,
                    lot.Color.g * 0.65f,
                    lot.Color.b * 0.68f,
                    1f);
            }

            float value =
                (lot.Color.r + lot.Color.g + lot.Color.b) / 3f;
            return new Color(
                value * 0.68f,
                value * 0.73f,
                value * 0.70f,
                1f);
        }

        private static uint StableHash(
            int seed,
            int x,
            int z,
            int floor,
            int pane,
            int side)
        {
            uint hash = unchecked((uint)seed) ^ 0x9E3779B9u;
            hash = Mix(hash, unchecked((uint)x));
            hash = Mix(hash, unchecked((uint)z));
            hash = Mix(hash, unchecked((uint)floor));
            hash = Mix(hash, unchecked((uint)pane));
            return Mix(hash, unchecked((uint)side));
        }

        private static uint Mix(uint first, uint second)
        {
            uint hash = first;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        private static Color Darken(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
        }
    }
}
