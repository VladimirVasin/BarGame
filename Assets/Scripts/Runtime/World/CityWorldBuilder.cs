using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityWorldBuilder
    {
        private const float WorldChunkSize = 48f;

        private static readonly Color RoadPaint = new Color(0.58f, 0.52f, 0.34f);
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
            RoadWalkableArea walkableArea = RoadWalkableArea.FromLayout(layout);
            RoadFencePlan fencePlan =
                RoadFencePlanner.CreatePlan(layout);
            CityDecorationPlan decorationPlan =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    fencePlan,
                    nightPlan);
            Bounds bounds = BuildGround(world, layout, settings);
            BuildRoads(world, layout, settings);
            RoadFenceWorldBuilder.Build(world, fencePlan);
            GameObject parkRoot = BuildPark(world, layout.Park);
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

            var bars = new List<BarEntrance>(settings.BarCount);
            HomeEntrance playerHome = null;
            SupermarketEntrance supermarket = null;
            for (int i = 0; i < layout.BuildingLots.Count; i++)
            {
                BuildBuilding(
                    world,
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
                decorationPlan,
                decorationRoot,
                bounds);
        }

        private static Bounds BuildGround(
            Transform parent,
            CityLayout layout,
            CityGenerationSettings settings)
        {
            Transform surfaces = new GameObject("City Surfaces").transform;
            surfaces.SetParent(parent, false);
            var buildable = new List<Bounds>();
            var beach = new List<Bounds>();
            var lakeShore = new List<Bounds>();
            var cemetery = new List<Bounds>();
            var water = new List<Bounds>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                bool isWater = surface.IsWater;
                float height = isWater ? 0.10f : 0.32f;
                float centerY = isWater ? -0.17f : -0.24f;
                var bounds = new Bounds(
                    new Vector3(
                        surface.WorldBounds.center.x,
                        centerY,
                        surface.WorldBounds.center.y),
                    new Vector3(
                        surface.WorldBounds.width,
                        height,
                        surface.WorldBounds.height));
                switch (surface.Kind)
                {
                    case CitySurfaceKind.Beach:
                        beach.Add(bounds);
                        break;
                    case CitySurfaceKind.LakeShore:
                        lakeShore.Add(bounds);
                        break;
                    case CitySurfaceKind.CemeteryGround:
                        cemetery.Add(bounds);
                        break;
                    case CitySurfaceKind.Water:
                        water.Add(bounds);
                        break;
                    default:
                        buildable.Add(bounds);
                        break;
                }
            }

            BuildCombinedBoxesIfAny(
                "Active Land",
                surfaces,
                buildable,
                CityExteriorAppearance.Ground,
                true);
            BuildCombinedBoxesIfAny(
                "Beach",
                surfaces,
                beach,
                CityExteriorAppearance.BeachSand,
                true);
            BuildCombinedBoxesIfAny(
                "Lake Shore",
                surfaces,
                lakeShore,
                CityExteriorAppearance.LakeShore,
                true);
            BuildCombinedBoxesIfAny(
                "Cemetery Ground",
                surfaces,
                cemetery,
                CityExteriorAppearance.CemeteryGround,
                true);
            BuildCombinedBoxesIfAny(
                "Water",
                surfaces,
                water,
                CityExteriorAppearance.Water);

            Rect footprint = layout.WorldXZBounds;
            return new Bounds(
                new Vector3(
                    footprint.center.x,
                    0f,
                    footprint.center.y),
                new Vector3(
                    footprint.width,
                    20f,
                    footprint.height));
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
                    CityExteriorAppearance.Asphalt,
                    true);
                BuildCombinedBoxesIfAny(
                    "Park Paths",
                    chunk,
                    geometry.ParkPaths,
                    CityExteriorAppearance.ParkPath,
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
                CityExteriorAppearance.Darken(
                    facadeColor,
                    0.055f),
                false);
            BuildWindowBands(building, lot, citySeed, emissiveMaterial);

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
                if (lot.HasRoadFrontage)
                {
                    Vector3 frontage = ResolveFacadeDirection(lot);
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
                        0,
                        emissiveMaterial);
                }

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
                lot.IsBar ||
                lot.IsPlayerHome ||
                lot.IsSupermarket
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
                Color color =
                    CityExteriorAppearance.ResolveWindowColor(
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
                (lot.DoorPosition + lot.ReturnPosition) * 0.5f;
            float apronLength = Vector3.Distance(
                lot.DoorPosition,
                lot.ReturnPosition);
            Vector3 apronSize = Mathf.Abs(direction.x) > 0.5f
                ? new Vector3(
                    apronLength,
                    0.08f,
                    SupermarketEntranceGeometry.WalkwayWidth)
                : new Vector3(
                    SupermarketEntranceGeometry.WalkwayWidth,
                    0.08f,
                    apronLength);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Entrance Walkway",
                parent,
                apronCenter + Vector3.up * 0.10f,
                apronSize,
                Sidewalk);
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
                lot.ReturnPosition + Vector3.up * 0.12f);
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
                RuntimePrimitiveFactory.CreateBox(
                    name,
                    parent,
                    worldPosition,
                    worldSize,
                    color,
                    sharedMaterial,
                    false);
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
            RuntimePrimitiveFactory.CreateBox(
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
            public readonly List<Bounds> Dashes = new List<Bounds>();
        }
    }
}
