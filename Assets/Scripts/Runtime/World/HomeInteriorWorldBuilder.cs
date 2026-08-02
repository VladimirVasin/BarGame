using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeInteriorWorldBuilder
    {
        internal const float BedDressingSurfaceHeight = 0.67f;
        internal const float KitchenDressingSurfaceHeight = 1.03f;
        internal const float TableDressingSurfaceHeight = 0.88f;
        internal const float BookcaseDressingSurfaceHeight = 2.25f;
        internal const string BedOccluderId = "home.furniture.bed";
        internal const string KitchenLeftOccluderId =
            "home.furniture.kitchen.left";
        internal const string KitchenRightOccluderId =
            "home.furniture.kitchen.right";
        internal const string RefrigeratorOccluderId =
            "home.furniture.refrigerator";
        internal const string SofaOccluderId = "home.furniture.sofa";
        internal const string TableOccluderId = "home.furniture.table";
        internal const string BookcaseOccluderId =
            "home.furniture.bookcase";
        internal const string CameraJunkOccluderId =
            "home.furniture.camera-junk";

        private const float FurnitureMinimumVisibility = 0.23f;
        private const float InteractiveMinimumVisibility = 0.23f;

        private static readonly Color Floor =
            new Color(0.115f, 0.085f, 0.065f);
        private static readonly Color Wall =
            new Color(0.255f, 0.225f, 0.175f);
        private static readonly Color Ceiling =
            new Color(0.16f, 0.14f, 0.11f);
        private static readonly Color Trim =
            new Color(0.37f, 0.27f, 0.16f);
        private static readonly Color DarkWood =
            new Color(0.115f, 0.055f, 0.036f);
        private static readonly Color Fabric =
            new Color(0.14f, 0.20f, 0.18f);
        private static readonly Color DirtyLinen =
            new Color(0.43f, 0.39f, 0.29f);

        public static Transform Build(
            Transform parent,
            HomeInteriorLayoutPlan plan)
        {
            return Build(
                parent,
                plan,
                null,
                null);
        }

        public static Transform Build(
            Transform parent,
            HomeInteriorLayoutPlan plan,
            HomeBalconyLayoutPlan balcony)
        {
            return Build(
                parent,
                plan,
                balcony,
                null);
        }

        public static Transform Build(
            Transform parent,
            HomeInteriorLayoutPlan plan,
            HomeBalconyLayoutPlan balcony,
            HomeExteriorContextPlan exterior)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            HomeInteriorLayoutValidator.ValidateOrThrow(plan);
            if (balcony != null)
            {
                HomeBalconyLayoutValidator.ValidateOrThrow(
                    plan,
                    balcony);
            }

            if (exterior != null && balcony == null)
            {
                throw new ArgumentException(
                    "A home exterior view requires a balcony layout.",
                    nameof(exterior));
            }

            HomeRefrigeratorPlan refrigeratorPlan =
                HomeRefrigeratorPlan.Create(plan);

            Transform room =
                new GameObject("Home Interior").transform;
            room.SetParent(parent, false);
            HomeOcclusionRegistry occlusionRegistry =
                room.gameObject.AddComponent<HomeOcclusionRegistry>();
            BuildShell(room, plan, balcony);
            BuildPracticalFixtures(room);
            for (int index = 0;
                 index < plan.Furniture.Count;
                 index++)
            {
                HomeFurnitureKind kind =
                    plan.Furniture[index].Kind;
                if (kind == HomeFurnitureKind.Toilet ||
                    kind == HomeFurnitureKind.Shower ||
                    kind == HomeFurnitureKind.Sink)
                {
                    continue;
                }

                BuildFurniture(
                    room,
                    plan.Furniture[index],
                    refrigeratorPlan,
                    occlusionRegistry);
            }

            HomeBathroomBuilder.Build(
                room,
                plan,
                occlusionRegistry);
            HomeInteriorDressingBuilder.Build(
                room,
                plan,
                occlusionRegistry);
            if (balcony != null)
            {
                HomeBalconyWorldBuilder.Build(
                    room,
                    plan,
                    balcony,
                    occlusionRegistry);
            }

            if (exterior != null)
            {
                HomeExteriorViewBuilder.Build(
                    room,
                    balcony,
                    exterior);
            }

            return room;
        }

        private static void BuildShell(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeBalconyLayoutPlan balcony)
        {
            float halfWidth = plan.RoomSize.x * 0.5f;
            float halfDepth = plan.RoomSize.y * 0.5f;
            float wallThickness =
                PlayerHomeBalconyGeometry.WallThickness;
            RuntimePrimitiveFactory.CreateBox(
                "Home Floor",
                room,
                new Vector3(0f, -0.08f, 0f),
                new Vector3(
                    plan.RoomSize.x,
                    0.16f,
                    plan.RoomSize.y),
                Floor);
            GameObject ceiling =
                RuntimePrimitiveFactory.CreateBox(
                    "Home Ceiling",
                    room,
                    new Vector3(
                        0f,
                        plan.RoomHeight + 0.04f,
                        0f),
                    new Vector3(
                        plan.RoomSize.x +
                        wallThickness * 2f,
                        0.16f,
                        plan.RoomSize.y +
                        wallThickness * 2f),
                    Ceiling,
                    false);
            Renderer ceilingRenderer =
                ceiling.GetComponent<Renderer>();
            ceilingRenderer.shadowCastingMode =
                UnityEngine.Rendering
                    .ShadowCastingMode.Off;
            ceilingRenderer.receiveShadows = false;
            RuntimePrimitiveFactory.CreateBox(
                "Home Back Wall",
                room,
                new Vector3(
                    0f,
                    plan.RoomHeight * 0.5f,
                    halfDepth),
                new Vector3(
                    plan.RoomSize.x,
                    plan.RoomHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Left Wall",
                room,
                new Vector3(
                    -halfWidth,
                    plan.RoomHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    plan.RoomHeight,
                    plan.RoomSize.y),
                Wall);
            if (balcony == null)
            {
                RuntimePrimitiveFactory.CreateBox(
                    "Home Right Wall",
                    room,
                    new Vector3(
                        halfWidth,
                        plan.RoomHeight * 0.5f,
                        0f),
                    new Vector3(
                        wallThickness,
                        plan.RoomHeight,
                        plan.RoomSize.y),
                    Wall);
            }
            else
            {
                BuildBalconyFacadeWall(
                    room,
                    plan,
                    balcony,
                    halfDepth,
                    wallThickness);
            }
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Wall Left",
                room,
                new Vector3(
                    -3.15f,
                    plan.RoomHeight * 0.5f,
                    -halfDepth),
                new Vector3(
                    3.70f,
                    plan.RoomHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Wall Right",
                room,
                new Vector3(
                    3.15f,
                    plan.RoomHeight * 0.5f,
                    -halfDepth),
                new Vector3(
                    3.70f,
                    plan.RoomHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Lintel",
                room,
                new Vector3(
                    0f,
                    3.02f,
                    -halfDepth),
                new Vector3(
                    2.60f,
                    0.76f,
                    wallThickness),
                Trim);
            const float entryOpeningHalfWidth = 1.30f;
            const float exitDoorWidth = 1.60f;
            const float exitDoorHeight = 2.30f;
            const float entryLintelBottom = 2.64f;
            float sideInfillWidth =
                entryOpeningHalfWidth -
                exitDoorWidth * 0.5f;
            float sideInfillCenter =
                exitDoorWidth * 0.5f +
                sideInfillWidth * 0.5f;
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Wall Left Door Infill",
                room,
                new Vector3(
                    -sideInfillCenter,
                    entryLintelBottom * 0.5f,
                    -halfDepth),
                new Vector3(
                    sideInfillWidth,
                    entryLintelBottom,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Wall Right Door Infill",
                room,
                new Vector3(
                    sideInfillCenter,
                    entryLintelBottom * 0.5f,
                    -halfDepth),
                new Vector3(
                    sideInfillWidth,
                    entryLintelBottom,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Door Transom Infill",
                room,
                new Vector3(
                    0f,
                    exitDoorHeight +
                    (entryLintelBottom -
                     exitDoorHeight) *
                    0.5f,
                    -halfDepth),
                new Vector3(
                    exitDoorWidth,
                    entryLintelBottom -
                    exitDoorHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Rug",
                room,
                new Vector3(0f, 0.015f, -2.65f),
                new Vector3(1.55f, 0.03f, 1.35f),
                new Color(0.22f, 0.075f, 0.065f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Rug Stain",
                room,
                new Vector3(0.28f, 0.033f, -2.48f),
                new Vector3(0.62f, 0.012f, 0.42f),
                new Color(0.085f, 0.055f, 0.042f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Exit Door",
                room,
                new Vector3(
                    0f,
                    exitDoorHeight * 0.5f,
                    -3.86f),
                new Vector3(
                    exitDoorWidth,
                    exitDoorHeight,
                    0.12f),
                DarkWood,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Exit Door Patch",
                room,
                new Vector3(-0.26f, 1.22f, -3.785f),
                new Vector3(0.54f, 0.72f, 0.025f),
                new Color(0.21f, 0.125f, 0.072f),
                false);
        }

        private static void BuildBalconyFacadeWall(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeBalconyLayoutPlan balcony,
            float halfDepth,
            float wallThickness)
        {
            float facadeX =
                PlayerHomeBalconyGeometry.HomeFacadeX;
            float windowMinimum =
                balcony.WindowCenter.z -
                balcony.WindowSize.z * 0.5f;
            float windowMaximum =
                balcony.WindowCenter.z +
                balcony.WindowSize.z * 0.5f;
            float doorMinimum =
                balcony.DoorCenter.z -
                balcony.DoorSize.z * 0.5f;
            float doorMaximum =
                balcony.DoorCenter.z +
                balcony.DoorSize.z * 0.5f;

            CreateFacadePier(
                "Home Right Wall South Pier",
                room,
                facadeX,
                -halfDepth,
                windowMinimum,
                plan.RoomHeight,
                wallThickness);
            CreateFacadePier(
                "Home Right Wall Middle Pier",
                room,
                facadeX,
                windowMaximum,
                doorMinimum,
                plan.RoomHeight,
                wallThickness);
            CreateFacadePier(
                "Home Right Wall North Pier",
                room,
                facadeX,
                doorMaximum,
                halfDepth,
                plan.RoomHeight,
                wallThickness);

            float windowBottom =
                balcony.WindowCenter.y -
                balcony.WindowSize.y * 0.5f;
            float windowTop =
                balcony.WindowCenter.y +
                balcony.WindowSize.y * 0.5f;
            CreateFacadeHorizontalSegment(
                "Home Right Wall Window Sill",
                room,
                facadeX,
                balcony.WindowCenter.z,
                windowBottom,
                balcony.WindowSize.z,
                wallThickness);
            CreateFacadeHorizontalSegment(
                "Home Right Wall Window Lintel",
                room,
                facadeX,
                balcony.WindowCenter.z,
                plan.RoomHeight - windowTop,
                balcony.WindowSize.z,
                wallThickness,
                windowTop);
            CreateFacadeHorizontalSegment(
                "Home Right Wall Door Lintel",
                room,
                facadeX,
                balcony.DoorCenter.z,
                plan.RoomHeight - balcony.DoorSize.y,
                balcony.DoorSize.z,
                wallThickness,
                balcony.DoorSize.y);

            float exteriorReturnDepth =
                PlayerHomeBalconyGeometry.BalconyDepth +
                0.65f;
            CreateExteriorReturnWall(
                "Home South Exterior Return Wall",
                room,
                facadeX,
                -halfDepth,
                plan.RoomHeight,
                exteriorReturnDepth,
                wallThickness);
            CreateExteriorReturnWall(
                "Home North Exterior Return Wall",
                room,
                facadeX,
                halfDepth,
                plan.RoomHeight,
                exteriorReturnDepth,
                wallThickness);
        }

        private static void CreateExteriorReturnWall(
            string name,
            Transform room,
            float facadeX,
            float z,
            float roomHeight,
            float depth,
            float wallThickness)
        {
            RuntimePrimitiveFactory.CreateBox(
                name,
                room,
                new Vector3(
                    facadeX + depth * 0.5f,
                    roomHeight * 0.5f,
                    z),
                new Vector3(
                    depth,
                    roomHeight,
                    wallThickness),
                Wall,
                false);
        }

        private static void CreateFacadePier(
            string name,
            Transform room,
            float facadeX,
            float minimumZ,
            float maximumZ,
            float roomHeight,
            float wallThickness)
        {
            float depth = maximumZ - minimumZ;
            if (depth <= 0f)
            {
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                room,
                new Vector3(
                    facadeX,
                    roomHeight * 0.5f,
                    (minimumZ + maximumZ) * 0.5f),
                new Vector3(
                    wallThickness,
                    roomHeight,
                    depth),
                Wall);
        }

        private static void CreateFacadeHorizontalSegment(
            string name,
            Transform room,
            float facadeX,
            float centerZ,
            float height,
            float depth,
            float wallThickness,
            float bottom = 0f)
        {
            if (height <= 0f)
            {
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                room,
                new Vector3(
                    facadeX,
                    bottom + height * 0.5f,
                    centerZ),
                new Vector3(
                    wallThickness,
                    height,
                    depth),
                Wall);
        }

        private static void BuildPracticalFixtures(
            Transform room)
        {
            Vector3 mainBulb =
                HomeInteriorAtmosphere.MainEmitterPosition;
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Main Lamp Shade",
                room,
                mainBulb + Vector3.up * 0.31f,
                new Vector3(0.58f, 0.11f, 0.58f),
                new Color(0.105f, 0.07f, 0.045f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Main Lamp Socket",
                room,
                mainBulb + Vector3.up * 0.17f,
                new Vector3(0.11f, 0.07f, 0.11f),
                new Color(0.075f, 0.055f, 0.040f),
                false);
            GameObject bulb = RuntimePrimitiveFactory.CreateCylinder(
                "Home Main Dirty Bulb",
                room,
                mainBulb,
                new Vector3(0.22f, 0.12f, 0.22f),
                new Color(3.20f, 1.45f, 0.38f),
                CityNightResources.EmissiveMaterial,
                false);
            AddPracticalHalo(
                bulb.transform,
                "Home Main Bulb Halo",
                0.32f,
                0.74f,
                new Color(1f, 0.58f, 0.24f, 0.12f),
                new Color(0.78f, 0.30f, 0.08f, 0.035f));
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Main Lamp Wire",
                room,
                new Vector3(
                    mainBulb.x,
                    2.78f,
                    mainBulb.z),
                new Vector3(0.028f, 0.31f, 0.028f),
                new Color(0.045f, 0.038f, 0.032f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Main Lamp Ceiling Rose",
                room,
                new Vector3(
                    mainBulb.x,
                    3.16f,
                    mainBulb.z),
                new Vector3(0.22f, 0.055f, 0.22f),
                new Color(0.075f, 0.055f, 0.040f),
                false);

            RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Cold Fixture",
                room,
                HomeInteriorAtmosphere.BathroomEmitterPosition +
                new Vector3(0f, 0.04f, 0.065f),
                new Vector3(1.00f, 0.18f, 0.10f),
                new Color(0.14f, 0.17f, 0.16f),
                false);
            Color coldTubeColor = new Color(1.35f, 2.40f, 2.20f);
            GameObject coldTube = RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Cold Tube",
                room,
                HomeInteriorAtmosphere.BathroomEmitterPosition,
                new Vector3(0.80f, 0.09f, 0.035f),
                coldTubeColor,
                CityNightResources.EmissiveMaterial,
                false);
            CityLightHalo bathroomHalo = AddPracticalHalo(
                coldTube.transform,
                "Home Bathroom Tube Halo",
                0.38f,
                0.82f,
                new Color(0.55f, 0.82f, 0.80f, 0.10f),
                new Color(0.20f, 0.52f, 0.52f, 0.028f));
            HomeBathroomLightFixture fixture =
                coldTube.AddComponent<HomeBathroomLightFixture>();
            fixture.Initialize(
                coldTube.GetComponent<Renderer>(),
                bathroomHalo,
                coldTubeColor);
        }

        private static CityLightHalo AddPracticalHalo(
            Transform emitter,
            string haloName,
            float innerSize,
            float outerSize,
            Color innerColor,
            Color outerColor)
        {
            GameObject haloObject = new GameObject(haloName);
            haloObject.transform.SetParent(emitter.parent, false);
            haloObject.transform.localPosition =
                emitter.localPosition;
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                innerSize,
                outerSize,
                innerColor,
                outerColor);
            return halo;
        }

        private static void BuildFurniture(
            Transform room,
            HomeFurnitureFootprint furniture,
            HomeRefrigeratorPlan refrigeratorPlan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Rect bounds = furniture.Bounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            switch (furniture.Kind)
            {
                case HomeFurnitureKind.Bed:
                    BuildBed(
                        room,
                        center,
                        bounds,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.Kitchen:
                    BuildKitchen(
                        room,
                        bounds,
                        refrigeratorPlan,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.Sofa:
                    BuildSofa(
                        room,
                        center,
                        bounds,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.Table:
                    BuildTable(
                        room,
                        center,
                        bounds,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.Bookcase:
                    BuildBookcase(
                        room,
                        center,
                        bounds,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.CameraCornerJunk:
                    BuildCameraCornerJunk(
                        room,
                        center,
                        bounds,
                        occlusionRegistry);
                    break;
            }
        }

        private static void BuildBed(
            Transform room,
            Vector3 center,
            Rect bounds,
            HomeOcclusionRegistry occlusionRegistry)
        {
            var parts = new List<GameObject>();
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Bed Frame",
                room,
                center + (Vector3.up * 0.22f),
                new Vector3(
                    bounds.width,
                    0.38f,
                    bounds.height),
                DarkWood));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Bed Mattress",
                room,
                center + (Vector3.up * 0.47f),
                new Vector3(
                    bounds.width - 0.16f,
                    0.18f,
                    bounds.height - 0.18f),
                DirtyLinen,
                false));
            GameObject blanket = RuntimePrimitiveFactory.CreateBox(
                "Home Bed Crooked Blanket",
                room,
                center +
                new Vector3(0.24f, 0.61f, 0.10f),
                new Vector3(
                    bounds.width * 0.62f,
                    0.10f,
                    bounds.height * 0.82f),
                new Color(0.17f, 0.255f, 0.23f),
                false);
            parts.Add(blanket);
            blanket.transform.localRotation =
                Quaternion.Euler(0f, 5f, -2f);
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Pillow",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.28f,
                    0.64f,
                    0f),
                new Vector3(0.62f, 0.18f, 1.05f),
                new Color(0.47f, 0.43f, 0.34f),
                false));
            RegisterFurnitureGroup(
                occlusionRegistry,
                BedOccluderId,
                parts);
        }

        private static void BuildKitchen(
            Transform room,
            Rect bounds,
            HomeRefrigeratorPlan refrigeratorPlan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            const float refrigeratorGap = 0.035f;
            Rect footprint = refrigeratorPlan.Footprint;
            Rect leftCounter = Rect.MinMaxRect(
                bounds.xMin,
                bounds.yMin,
                footprint.xMin - refrigeratorGap,
                bounds.yMax);
            Rect rightCounter = Rect.MinMaxRect(
                footprint.xMax + refrigeratorGap,
                bounds.yMin,
                bounds.xMax,
                bounds.yMax);
            List<GameObject> leftParts =
                BuildKitchenCounterSection(
                room,
                "Left",
                leftCounter);
            List<GameObject> rightParts =
                BuildKitchenCounterSection(
                room,
                "Right",
                rightCounter);
            leftParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Sink",
                room,
                new Vector3(
                    leftCounter.center.x -
                    leftCounter.width * 0.06f,
                    1.05f,
                    leftCounter.center.y),
                new Vector3(
                    Mathf.Min(0.85f, leftCounter.width - 0.16f),
                    0.06f,
                    0.55f),
                new Color(0.32f, 0.38f, 0.36f),
                false));
            leftParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Kitchen Broken Door",
                room,
                new Vector3(
                    leftCounter.center.x,
                    0.43f,
                    leftCounter.yMin - 0.01f),
                new Vector3(
                    Mathf.Min(0.58f, leftCounter.width * 0.72f),
                    0.70f,
                    0.055f),
                new Color(0.12f, 0.14f, 0.12f),
                false));
            RegisterFurnitureGroup(
                occlusionRegistry,
                KitchenLeftOccluderId,
                leftParts);
            RegisterFurnitureGroup(
                occlusionRegistry,
                KitchenRightOccluderId,
                rightParts);

            HomeRefrigeratorView refrigerator =
                HomeRefrigeratorWorldBuilder.Build(
                room,
                refrigeratorPlan);
            RegisterRefrigerator(
                occlusionRegistry,
                refrigerator);
        }

        private static List<GameObject> BuildKitchenCounterSection(
            Transform room,
            string sectionName,
            Rect section)
        {
            var parts = new List<GameObject>();
            if (section.width <= 0.05f ||
                section.height <= 0.05f)
            {
                return parts;
            }

            Vector3 center = new Vector3(
                section.center.x,
                0.48f,
                section.center.y);
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                $"Home Kitchen Counter {sectionName}",
                room,
                center,
                new Vector3(
                    section.width,
                    0.92f,
                    section.height),
                new Color(0.16f, 0.18f, 0.16f)));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                $"Home Kitchen Top {sectionName}",
                room,
                new Vector3(center.x, 0.98f, center.z),
                new Vector3(
                    section.width + 0.04f,
                    0.10f,
                    section.height + 0.08f),
                Trim,
                false));
            return parts;
        }

        private static void BuildSofa(
            Transform room,
            Vector3 center,
            Rect bounds,
            HomeOcclusionRegistry occlusionRegistry)
        {
            var parts = new List<GameObject>();
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Sofa Base",
                room,
                center + (Vector3.up * 0.35f),
                new Vector3(
                    bounds.width,
                    0.56f,
                    bounds.height),
                Fabric));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Sofa Back",
                room,
                center +
                new Vector3(
                    bounds.width * 0.38f,
                    0.94f,
                    0f),
                new Vector3(
                    0.25f,
                    1.08f,
                    bounds.height),
                Fabric));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Sofa Sunken Cushion",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.08f,
                    0.67f,
                    0f),
                new Vector3(
                    bounds.width * 0.70f,
                    0.16f,
                    bounds.height * 0.78f),
                new Color(0.18f, 0.235f, 0.20f),
                false));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Sofa Tear",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.43f,
                    0.75f,
                    bounds.height * 0.18f),
                new Vector3(0.025f, 0.36f, 0.48f),
                new Color(0.33f, 0.25f, 0.16f),
                false));
            RegisterFurnitureGroup(
                occlusionRegistry,
                SofaOccluderId,
                parts);
        }

        private static void BuildTable(
            Transform room,
            Vector3 center,
            Rect bounds,
            HomeOcclusionRegistry occlusionRegistry)
        {
            var parts = new List<GameObject>();
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Scarred Table",
                room,
                center + (Vector3.up * 0.82f),
                new Vector3(
                    bounds.width,
                    0.12f,
                    bounds.height),
                Trim));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Table Base Crooked",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.10f,
                    0.40f,
                    bounds.height * 0.08f),
                new Vector3(0.28f, 0.80f, 0.28f),
                DarkWood));
            RegisterFurnitureGroup(
                occlusionRegistry,
                TableOccluderId,
                parts);
        }

        private static void BuildBookcase(
            Transform room,
            Vector3 center,
            Rect bounds,
            HomeOcclusionRegistry occlusionRegistry)
        {
            var parts = new List<GameObject>();
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Battered Cabinet",
                room,
                center +
                Vector3.up *
                (BookcaseDressingSurfaceHeight * 0.5f),
                new Vector3(
                    bounds.width,
                    BookcaseDressingSurfaceHeight,
                    bounds.height),
                DarkWood));
            for (int shelf = 0; shelf < 2; shelf++)
            {
                parts.Add(RuntimePrimitiveFactory.CreateBox(
                    $"Home Cabinet Shelf {shelf + 1}",
                    room,
                    center +
                    new Vector3(
                        -0.05f,
                        0.46f + shelf * 0.62f,
                        -bounds.height * 0.43f),
                    new Vector3(
                        bounds.width * 0.72f,
                        0.10f,
                        0.08f),
                    Trim,
                    false));
            }

            RegisterFurnitureGroup(
                occlusionRegistry,
                BookcaseOccluderId,
                parts);
        }

        private static void BuildCameraCornerJunk(
            Transform room,
            Vector3 center,
            Rect bounds,
            HomeOcclusionRegistry occlusionRegistry)
        {
            var parts = new List<GameObject>();
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Camera Corner Junk Base",
                room,
                center + Vector3.up * 0.22f,
                new Vector3(
                    bounds.width,
                    0.44f,
                    bounds.height),
                new Color(0.10f, 0.055f, 0.035f)));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Camera Corner Broken Wardrobe Door",
                room,
                center +
                new Vector3(0.10f, 0.50f, 0.18f),
                new Vector3(
                    bounds.width * 0.78f,
                    0.12f,
                    bounds.height * 0.68f),
                new Color(0.19f, 0.105f, 0.060f),
                false));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Camera Corner Suitcase",
                room,
                center +
                new Vector3(0.22f, 0.69f, -0.36f),
                new Vector3(0.72f, 0.24f, 0.62f),
                new Color(0.16f, 0.12f, 0.075f),
                false));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Camera Corner Old Coat",
                room,
                center +
                new Vector3(-0.24f, 0.86f, 0.38f),
                new Vector3(0.78f, 0.10f, 0.72f),
                new Color(0.105f, 0.14f, 0.13f),
                false));
            RegisterFurnitureGroup(
                occlusionRegistry,
                CameraJunkOccluderId,
                parts);
        }

        private static void RegisterFurnitureGroup(
            HomeOcclusionRegistry registry,
            string id,
            List<GameObject> parts)
        {
            if (registry == null || parts == null || parts.Count == 0)
            {
                return;
            }

            registry.Register(
                id,
                HomeOccluderKind.FurnitureBlocker,
                FurnitureMinimumVisibility,
                parts.ToArray());
        }

        private static void RegisterRefrigerator(
            HomeOcclusionRegistry registry,
            HomeRefrigeratorView refrigerator)
        {
            if (registry == null || refrigerator == null)
            {
                return;
            }

            Renderer[] descendants =
                refrigerator.GetComponentsInChildren<Renderer>(true);
            var renderers = new List<Renderer>(descendants.Length);
            Transform haloRoot = refrigerator.InteriorHalo != null
                ? refrigerator.InteriorHalo.transform
                : null;
            for (int index = 0; index < descendants.Length; index++)
            {
                Renderer renderer = descendants[index];
                if (renderer == refrigerator.InteriorLightStrip ||
                    (haloRoot != null &&
                     renderer.transform.IsChildOf(haloRoot)))
                {
                    continue;
                }

                renderers.Add(renderer);
            }

            if (renderers.Count == 0)
            {
                return;
            }

            registry.Register(
                RefrigeratorOccluderId,
                HomeOccluderKind.InteractiveProtected,
                InteractiveMinimumVisibility,
                renderers.ToArray());
        }
    }
}
