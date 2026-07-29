using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityWorldBuilder
    {
        private const float WorldChunkSize = 48f;

        private static readonly Color Asphalt = new Color(0.175f, 0.195f, 0.195f);
        private static readonly Color ParkPath =
            new Color(0.39f, 0.34f, 0.24f);
        private static readonly Color RoadPaint = new Color(0.58f, 0.52f, 0.34f);
        private static readonly Color Ground = new Color(0.170f, 0.205f, 0.185f);
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
        private static readonly Color Sidewalk = new Color(0.31f, 0.33f, 0.305f);
        private static readonly Color WindowOff = new Color(0.025f, 0.035f, 0.040f);
        private static readonly Color ColdWindow = new Color(0.24f, 0.43f, 0.56f);
        private static readonly Color WarmWindow = new Color(0.88f, 0.48f, 0.20f);
        private static readonly Color BarWindow = new Color(1.35f, 0.72f, 0.28f);
        private static readonly Color HomeWindow = new Color(0.82f, 1.10f, 1.22f);
        private static readonly Color BarTrim = new Color(0.84f, 0.55f, 0.18f);
        private static readonly Color BarAwning = new Color(0.24f, 0.018f, 0.045f);
        private static readonly Color DoorColor = new Color(0.055f, 0.025f, 0.022f);
        private static readonly Color HomeTrim = new Color(0.66f, 0.82f, 0.80f);
        private static readonly Color HomeDoor =
            new Color(0.08f, 0.20f, 0.22f);

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
            RoadFencePlan fencePlan =
                RoadFencePlanner.CreatePlan(layout);
            Bounds bounds = BuildGround(world, layout, settings);
            BuildRoads(world, layout, settings);
            RoadFenceWorldBuilder.Build(world, fencePlan);
            GameObject parkRoot = BuildPark(world, layout.Park);

            var bars = new List<BarEntrance>(settings.BarCount);
            HomeEntrance playerHome = null;
            for (int i = 0; i < layout.BuildingLots.Count; i++)
            {
                BuildBuilding(
                    world,
                    layout.BuildingLots[i],
                    layout.Seed,
                    emissiveMaterial,
                    walkableArea,
                    bars,
                    ref playerHome);
            }

            return new CityWorldResult(
                world.gameObject,
                walkableArea,
                bars,
                playerHome,
                fencePlan,
                parkRoot,
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
            var chunks =
                new Dictionary<WorldChunkKey, RoadChunkGeometry>();

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
                WorldChunkKey key = WorldChunkKey.FromPosition(center);
                if (!chunks.TryGetValue(
                        key,
                        out RoadChunkGeometry geometry))
                {
                    geometry = new RoadChunkGeometry();
                    chunks.Add(key, geometry);
                }

                Bounds surface = new Bounds(center, size);
                if (layout.GetPathKind(edge) == CityPathKind.ParkPath)
                {
                    geometry.ParkPaths.Add(surface);
                }
                else
                {
                    geometry.Streets.Add(surface);
                    AddRoadDashes(
                        geometry.Dashes,
                        start,
                        end,
                        edge.IsHorizontal);
                }
            }

            var keys = new List<WorldChunkKey>(chunks.Keys);
            keys.Sort(WorldChunkKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                WorldChunkKey key = keys[index];
                RoadChunkGeometry geometry = chunks[key];
                Transform chunk = new GameObject(
                    $"Road Chunk {key.X}-{key.Z}").transform;
                chunk.SetParent(roads, false);
                BuildCombinedBoxesIfAny(
                    "Street Surfaces",
                    chunk,
                    geometry.Streets,
                    Asphalt,
                    true);
                BuildCombinedBoxesIfAny(
                    "Park Paths",
                    chunk,
                    geometry.ParkPaths,
                    ParkPath,
                    true);
                BuildCombinedBoxesIfAny(
                    "Road Dashes",
                    chunk,
                    geometry.Dashes,
                    RoadPaint);
            }
        }

        private static void AddRoadDashes(
            ICollection<Bounds> target,
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
                target.Add(new Bounds(
                    position + (Vector3.up * 0.095f),
                    size));
            }
        }

        private static GameObject BuildPark(
            Transform parent,
            CityParkPlan plan)
        {
            if (plan == null || !plan.IsEnabled)
            {
                return null;
            }

            Transform park = new GameObject("Central Park").transform;
            park.SetParent(parent, false);
            Rect bounds = plan.WalkableBounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            RuntimePrimitiveFactory.CreateBox(
                "Park Lawn",
                park,
                center,
                new Vector3(bounds.width, 0.08f, bounds.height),
                ParkGrass);
            GameObject plaza = RuntimePrimitiveFactory.CreateCylinder(
                "Park Central Plaza",
                park,
                plan.Center + (Vector3.up * 0.065f),
                new Vector3(8.5f, 0.035f, 8.5f),
                ParkPlaza,
                false);
            MeshCollider plazaCollider =
                plaza.AddComponent<MeshCollider>();
            plazaCollider.sharedMesh =
                plaza.GetComponent<MeshFilter>().sharedMesh;

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

            BuildCombinedBoxesIfAny(
                "Park Tree Trunks",
                park,
                trunks,
                ParkTrunk);
            BuildCombinedBoxesIfAny(
                "Park Tree Canopies",
                park,
                canopies,
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

            BuildCombinedBoxesIfAny(
                "Park Benches",
                park,
                benchParts,
                ParkBench);
            BuildParkHedges(park, plan);
            return park.gameObject;
        }

        private static void BuildParkHedges(
            Transform parent,
            CityParkPlan plan)
        {
            Rect bounds = plan.WalkableBounds;
            float gateWidth = plan.Gates.Count > 0
                ? plan.Gates[0].Width
                : 6f;
            float halfGate = gateWidth * 0.5f;
            var hedges = new List<Bounds>(8);
            AddHorizontalBoundaryParts(
                hedges,
                bounds.xMin,
                bounds.xMax,
                bounds.center.x,
                bounds.yMin,
                halfGate);
            AddHorizontalBoundaryParts(
                hedges,
                bounds.xMin,
                bounds.xMax,
                bounds.center.x,
                bounds.yMax,
                halfGate);
            AddVerticalBoundaryParts(
                hedges,
                bounds.yMin,
                bounds.yMax,
                bounds.center.y,
                bounds.xMin,
                halfGate);
            AddVerticalBoundaryParts(
                hedges,
                bounds.yMin,
                bounds.yMax,
                bounds.center.y,
                bounds.xMax,
                halfGate);
            BuildCombinedBoxesIfAny(
                "Park Boundary Hedges",
                parent,
                hedges,
                ParkHedge);
        }

        private static void AddHorizontalBoundaryParts(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float gateCenter,
            float fixedZ,
            float halfGate)
        {
            AddHorizontalBoundaryPart(
                target,
                minimum,
                gateCenter - halfGate,
                fixedZ);
            AddHorizontalBoundaryPart(
                target,
                gateCenter + halfGate,
                maximum,
                fixedZ);
        }

        private static void AddHorizontalBoundaryPart(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float fixedZ)
        {
            if (maximum <= minimum)
            {
                return;
            }

            target.Add(new Bounds(
                new Vector3(
                    (minimum + maximum) * 0.5f,
                    0.58f,
                    fixedZ),
                new Vector3(maximum - minimum, 1.16f, 0.72f)));
        }

        private static void AddVerticalBoundaryParts(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float gateCenter,
            float fixedX,
            float halfGate)
        {
            AddVerticalBoundaryPart(
                target,
                minimum,
                gateCenter - halfGate,
                fixedX);
            AddVerticalBoundaryPart(
                target,
                gateCenter + halfGate,
                maximum,
                fixedX);
        }

        private static void AddVerticalBoundaryPart(
            ICollection<Bounds> target,
            float minimum,
            float maximum,
            float fixedX)
        {
            if (maximum <= minimum)
            {
                return;
            }

            target.Add(new Bounds(
                new Vector3(
                    fixedX,
                    0.58f,
                    (minimum + maximum) * 0.5f),
                new Vector3(0.72f, 1.16f, maximum - minimum)));
        }

        private static void BuildBuilding(
            Transform parent,
            BuildingLot lot,
            int citySeed,
            Material emissiveMaterial,
            RoadWalkableArea walkableArea,
            IList<BarEntrance> bars,
            ref HomeEntrance playerHome)
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
                        : $"Building {lot.Cell.x}-{lot.Cell.y}").transform;
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

            if (lot.IsPlayerHome)
            {
                BuildHomeFront(
                    building,
                    lot,
                    walkableArea,
                    ref playerHome);
                return;
            }

            if (!lot.IsBar)
            {
                BuildDistrictDetails(
                    building,
                    lot,
                    emissiveMaterial);
                return;
            }

            BuildBarFront(building, lot, walkableArea, bars);
        }

        private static void BuildDistrictDetails(
            Transform parent,
            BuildingLot lot,
            Material emissiveMaterial)
        {
            switch (lot.District)
            {
                case CityDistrictKind.OldTown:
                    RuntimePrimitiveFactory.CreateBox(
                        "Old Town Cornice",
                        parent,
                        lot.Center +
                        (Vector3.up * (lot.Height - 0.42f)),
                        new Vector3(
                            lot.Size.x + 0.42f,
                            0.30f,
                            lot.Size.y + 0.42f),
                        new Color(0.28f, 0.23f, 0.18f),
                        false);
                    break;
                case CityDistrictKind.Residential:
                    BuildResidentialPlanters(parent, lot);
                    break;
                case CityDistrictKind.Industrial:
                    BuildIndustrialRoofDetails(parent, lot);
                    break;
                case CityDistrictKind.Nightlife:
                    BuildNightlifeSign(
                        parent,
                        lot,
                        emissiveMaterial);
                    break;
            }
        }

        private static void BuildResidentialPlanters(
            Transform parent,
            BuildingLot lot)
        {
            float z = lot.Center.z - lot.Size.y * 0.5f - 0.35f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = new Vector3(
                    lot.Center.x + side * lot.Size.x * 0.28f,
                    0.32f,
                    z);
                RuntimePrimitiveFactory.CreateBox(
                    "Residential Planter",
                    parent,
                    position,
                    new Vector3(1.8f, 0.52f, 0.58f),
                    new Color(0.24f, 0.25f, 0.22f),
                    false);
                RuntimePrimitiveFactory.CreateBox(
                    "Residential Shrub",
                    parent,
                    position + (Vector3.up * 0.58f),
                    new Vector3(1.45f, 0.72f, 0.48f),
                    new Color(0.12f, 0.29f, 0.17f),
                    false);
            }
        }

        private static void BuildIndustrialRoofDetails(
            Transform parent,
            BuildingLot lot)
        {
            for (int index = 0; index < 2; index++)
            {
                float xOffset = index == 0 ? -1.7f : 1.7f;
                RuntimePrimitiveFactory.CreateCylinder(
                    "Industrial Roof Vent",
                    parent,
                    lot.Center +
                    new Vector3(
                        xOffset,
                        lot.Height + 0.85f,
                        0f),
                    new Vector3(0.46f, 0.72f, 0.46f),
                    new Color(0.22f, 0.25f, 0.24f),
                    false);
            }
        }

        private static void BuildNightlifeSign(
            Transform parent,
            BuildingLot lot,
            Material emissiveMaterial)
        {
            Color signColor = (lot.Cell.x + lot.Cell.y) % 2 == 0
                ? new Color(1.2f, 0.22f, 0.72f)
                : new Color(0.18f, 0.78f, 1.25f);
            RuntimePrimitiveFactory.CreateBox(
                "Nightlife Neon Sign",
                parent,
                lot.Center +
                new Vector3(
                    0f,
                    Mathf.Min(lot.Height - 1f, 4.4f),
                    -(lot.Size.y * 0.5f + 0.05f)),
                new Vector3(
                    Mathf.Min(4.8f, lot.Size.x * 0.45f),
                    0.42f,
                    0.08f),
                signColor,
                emissiveMaterial,
                false);
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
                if (lot.IsBar || lot.IsPlayerHome)
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
            float paneHeight =
                lot.IsBar || lot.IsPlayerHome
                    ? 0.60f
                    : 0.48f;

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

            if (lot.IsPlayerHome)
            {
                emissive = true;
                return HomeWindow;
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
                ? new Vector3(
                    apronLength,
                    0.08f,
                    BarEntranceGeometry.WalkwayWidth)
                : new Vector3(
                    BarEntranceGeometry.WalkwayWidth,
                    0.08f,
                    apronLength);
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

        private static void BuildHomeFront(
            Transform parent,
            BuildingLot lot,
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
                (lot.DoorPosition + lot.ReturnPosition) * 0.5f;
            float apronLength = Vector3.Distance(
                lot.DoorPosition,
                lot.ReturnPosition);
            Vector3 apronSize = frontageIsX
                ? new Vector3(
                    apronLength,
                    0.08f,
                    PlayerHomeEntranceGeometry.WalkwayWidth)
                : new Vector3(
                    PlayerHomeEntranceGeometry.WalkwayWidth,
                    0.08f,
                    apronLength);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entrance Walkway",
                parent,
                apronCenter + (Vector3.up * 0.10f),
                apronSize,
                Sidewalk);
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
                lot.ReturnPosition + (Vector3.up * 0.12f));
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
            RuntimePrimitiveFactory.CreateBox(
                "Home Porch Light",
                parent,
                doorPosition +
                (direction * 0.18f) +
                (tangent * 1.16f) +
                (Vector3.up * 2.18f),
                new Vector3(0.28f, 0.38f, 0.28f),
                HomeWindow * 1.35f,
                CityNightResources.EmissiveMaterial,
                false);
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
                ? new Vector3(
                    0.82f,
                    0.18f,
                    BarEntranceGeometry.CanopyWidth)
                : new Vector3(
                    BarEntranceGeometry.CanopyWidth,
                    0.18f,
                    0.82f);

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

            if (lot.IsPlayerHome)
            {
                return new Color(
                    lot.Color.r * 0.72f,
                    lot.Color.g * 0.78f,
                    lot.Color.b * 0.80f,
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

        private static Color Darken(Color color, float amount)
        {
            return new Color(
                Mathf.Clamp01(color.r - amount),
                Mathf.Clamp01(color.g - amount),
                Mathf.Clamp01(color.b - amount),
                color.a);
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
            public readonly List<Bounds> Dashes = new List<Bounds>();
        }
    }
}
