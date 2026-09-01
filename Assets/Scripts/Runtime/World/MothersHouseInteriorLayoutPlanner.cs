using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class MothersHouseInteriorLayoutPlanner
    {
        public const float RoomWidth = 10f;
        public const float RoomDepth = 8f;
        public const float RoomHeight = 3.4f;
        public const float UpperFloorElevation = 3.54f;
        public const float UpperCeilingHeight = 5.9f;
        public const float WallThickness = 0.24f;
        public const float DoorOpeningWidth = 1.3f;
        public const float DoorOpeningHeight = 2.2f;
        public const float UpperPartitionThickness = 0.16f;
        public const float UpperPartitionX = -1.75f;
        public const float UpperRoomDividerZ = 0f;
        public const float UpperDoorOpeningWidth = 1.2f;
        public const float UpperDoorOpeningHeight = 2.2f;
        public const float UpperSouthDoorCenterZ = -1.85f;
        public const float UpperNorthDoorCenterZ = 1.85f;
        public const int StairStepCount = 19;
        public const float StairStepRise =
            UpperFloorElevation / StairStepCount;
        public const float StairStepDepth = 0.25f;
        public const float StairWidth = 1.3f;
        public const float CameraVerticalFieldOfView = 60f;
        public const float UpperCameraVerticalFieldOfView = 58f;
        public const string ModelResourcePath =
            "MothersHouse/MothersHouseInterior3D";

        public static readonly Bounds ModelLocalBounds = new Bounds(
            new Vector3(0f, 2.93f, 0f),
            new Vector3(10.24f, 6.22f, 8.24f));
        public static readonly Rect RoomBounds =
            new Rect(-5f, -4f, RoomWidth, RoomDepth);
        public static readonly Rect WalkableBounds =
            new Rect(-4.65f, -3.65f, 9.3f, 7.3f);
        public static readonly Vector3 EntryAnchorPosition =
            new Vector3(0f, 0f, -3.86f);
        public static readonly Vector3 SpawnAnchorPosition =
            new Vector3(0f, 0f, -2.45f);
        public static readonly Vector3 ExitAnchorPosition =
            new Vector3(0f, 0f, -3.15f);
        public static readonly Vector3 CameraPosition =
            new Vector3(5.8f, 2.75f, -2.8f);
        public static readonly Vector3 CameraTarget =
            new Vector3(-0.2f, 0.8f, 1f);
        public static readonly Vector3 StairCameraPosition =
            new Vector3(-5.9f, 5.1f, 2.65f);
        public static readonly Vector3 StairCameraTarget =
            new Vector3(-2.85f, 4.25f, -0.3f);
        public static readonly Vector3 SouthRoomCameraPosition =
            new Vector3(5.8f, 5.1f, -3.05f);
        public static readonly Vector3 SouthRoomCameraTarget =
            new Vector3(1.25f, 4.25f, -1.9f);
        public static readonly Vector3 NorthRoomCameraPosition =
            new Vector3(5.8f, 5.1f, 3.05f);
        public static readonly Vector3 NorthRoomCameraTarget =
            new Vector3(1.25f, 4.25f, 1.9f);
        public static readonly Vector3 WestWindowPosition =
            new Vector3(-2.72f, 1.55f, 3.82f);
        public static readonly Vector3 EastWindowPosition =
            new Vector3(2.72f, 1.55f, 3.82f);
        public static readonly Vector3 FireplaceAnchorPosition =
            new Vector3(0f, 0f, 3.61f);
        public static readonly Vector3 FireLightAnchorPosition =
            new Vector3(0f, 0.78f, 3.28f);
        public static readonly Vector3 FloorLampLightAnchorPosition =
            new Vector3(-1.72f, 1.5f, 1.45f);
        public static readonly Vector3 TabletopAnchorPosition =
            new Vector3(0f, 0.48f, 0f);
        public static readonly Vector3 TeapotDockAnchorPosition =
            new Vector3(0.18f, 0.51f, 0.05f);
        public static readonly Rect CupboardBounds = Rect.MinMaxRect(
            1.88f,
            -3.95f,
            3.68f,
            -3.34f);
        public const float CupboardBaseHeight = 0f;
        public const float CupboardHeight = 2.05f;
        public static readonly Rect YarnBasketBounds = Rect.MinMaxRect(
            0.9016f,
            1.388323f,
            1.4584f,
            1.851677f);
        public const float YarnBasketBaseHeight = 0.02f;
        public const float YarnBasketHeight = 0.5f;
        public static readonly Rect FloorLampBounds = Rect.MinMaxRect(
            -2.02f,
            1.15f,
            -1.42f,
            1.75f);
        public const float FloorLampHeight = 1.82f;
        public static readonly Rect StairOpeningBounds = Rect.MinMaxRect(
            -4.88f,
            -3.05f,
            -3.18f,
            1.82f);
        public static readonly Rect UpperCorridorBounds = Rect.MinMaxRect(
            -3.18f,
            -3.65f,
            -1.83f,
            3.65f);
        public static readonly Rect UpperSouthRoomBounds = Rect.MinMaxRect(
            -1.67f,
            -3.88f,
            4.88f,
            -0.08f);
        public static readonly Rect UpperNorthRoomBounds = Rect.MinMaxRect(
            -1.67f,
            0.08f,
            4.88f,
            3.88f);

        public static MothersHouseInteriorLayoutPlan Generate()
        {
            List<HomeCameraShot> cameraShots = CreateCameraShots();
            var upperFloor = new MothersHouseInteriorUpperFloorPlan(
                UpperFloorElevation,
                UpperCeilingHeight,
                new StairwellFlightPlan(
                    "mother-house-stair",
                    new Vector2(-4f, 1.80f),
                    Vector2.down,
                    0f,
                    StairStepCount,
                    StairStepRise,
                    StairStepDepth,
                    StairWidth),
                StairOpeningBounds,
                UpperCorridorBounds,
                UpperSouthRoomBounds,
                UpperNorthRoomBounds,
                UpperPartitionX,
                UpperPartitionThickness,
                UpperRoomDividerZ,
                UpperDoorOpeningWidth,
                UpperDoorOpeningHeight,
                UpperSouthDoorCenterZ,
                UpperNorthDoorCenterZ);

            var plan = new MothersHouseInteriorLayoutPlan(
                new Vector2(RoomWidth, RoomDepth),
                RoomHeight,
                WallThickness,
                DoorOpeningWidth,
                RoomBounds,
                ModelLocalBounds,
                WalkableBounds,
                EntryAnchorPosition,
                SpawnAnchorPosition +
                    Vector3.up * PlayerFactory.GroundedRootOffset,
                ExitAnchorPosition + Vector3.up * 0.95f,
                new Vector3(1.15f, 1.9f, 1.25f),
                CameraTarget,
                cameraShots,
                upperFloor,
                WestWindowPosition,
                EastWindowPosition,
                FireplaceAnchorPosition,
                FireLightAnchorPosition,
                FloorLampLightAnchorPosition,
                TabletopAnchorPosition,
                TeapotDockAnchorPosition,
                ModelResourcePath,
                CreatePaths(),
                CreateFixtures());
            MothersHouseInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }

        private static List<HomeCameraShot> CreateCameraShots()
        {
            Rect stairAndCorridorBounds = Rect.MinMaxRect(
                -4.65f,
                -3.65f,
                -1.67f,
                3.65f);
            Rect stairAndCorridorHold = Rect.MinMaxRect(
                -4.65f,
                -3.65f,
                -1.30f,
                3.65f);
            Rect southRoomActivation = Rect.MinMaxRect(
                -1.65f,
                -3.65f,
                4.65f,
                -0.09f);
            Rect southRoomHold = Rect.MinMaxRect(
                -1.95f,
                -3.65f,
                4.65f,
                0.05f);
            Rect northRoomActivation = Rect.MinMaxRect(
                -1.65f,
                0.09f,
                4.65f,
                3.65f);
            Rect northRoomHold = Rect.MinMaxRect(
                -1.95f,
                -0.05f,
                4.65f,
                3.65f);
            Vector2 upperActivationHeight = new Vector2(3.48f, 5.5f);
            Vector2 upperHoldHeight = new Vector2(3.42f, 5.6f);

            return new List<HomeCameraShot>
            {
                CreateShot(
                    HomeCameraShotKind.MainRoom,
                    WalkableBounds,
                    WalkableBounds,
                    new Vector2(-0.1f, 1.58f),
                    new Vector2(-0.1f, 1.72f),
                    CameraPosition,
                    CameraTarget,
                    CameraVerticalFieldOfView),
                CreateShot(
                    HomeCameraShotKind.StairAndUpperCorridor,
                    stairAndCorridorBounds,
                    stairAndCorridorHold,
                    new Vector2(1.6f, 5.5f),
                    new Vector2(1.5f, 5.6f),
                    StairCameraPosition,
                    StairCameraTarget,
                    UpperCameraVerticalFieldOfView),
                CreateShot(
                    HomeCameraShotKind.UpperSouthRoom,
                    southRoomActivation,
                    southRoomHold,
                    upperActivationHeight,
                    upperHoldHeight,
                    SouthRoomCameraPosition,
                    SouthRoomCameraTarget,
                    UpperCameraVerticalFieldOfView),
                CreateShot(
                    HomeCameraShotKind.UpperNorthRoom,
                    northRoomActivation,
                    northRoomHold,
                    upperActivationHeight,
                    upperHoldHeight,
                    NorthRoomCameraPosition,
                    NorthRoomCameraTarget,
                    UpperCameraVerticalFieldOfView)
            };
        }

        private static HomeCameraShot CreateShot(
            HomeCameraShotKind kind,
            Rect activationBounds,
            Rect holdBounds,
            Vector2 activationHeightRange,
            Vector2 holdHeightRange,
            Vector3 position,
            Vector3 target,
            float fieldOfView)
        {
            return new HomeCameraShot(
                kind,
                activationBounds,
                holdBounds,
                activationHeightRange,
                holdHeightRange,
                position,
                Quaternion.LookRotation(target - position, Vector3.up),
                fieldOfView);
        }

        private static List<MothersHouseInteriorPathPlan> CreatePaths()
        {
            return new List<MothersHouseInteriorPathPlan>
            {
                new MothersHouseInteriorPathPlan(
                    "entry-approach",
                    MothersHouseInteriorPathKind.EntryApproach,
                    new Rect(-0.75f, -3.3f, 2.2f, 1.65f),
                    1.2f),
                new MothersHouseInteriorPathPlan(
                    "main-passage",
                    MothersHouseInteriorPathKind.MainPassage,
                    new Rect(0.73f, -2.3f, 1.3f, 2.95f),
                    1.2f),
                new MothersHouseInteriorPathPlan(
                    "table-approach",
                    MothersHouseInteriorPathKind.TableApproach,
                    new Rect(0.73f, -0.65f, 2.4f, 1.3f),
                    1.2f)
            };
        }

        private static List<MothersHouseInteriorFixturePlan> CreateFixtures()
        {
            return new List<MothersHouseInteriorFixturePlan>
            {
                new MothersHouseInteriorFixturePlan(
                    "low-table",
                    MothersHouseInteriorFixtureKind.LowTable,
                    CenteredRect(0f, 0f, 1.45f, 0.9f),
                    0f,
                    0.48f,
                    true),
                new MothersHouseInteriorFixturePlan(
                    "rocking-chair",
                    MothersHouseInteriorFixtureKind.RockingChair,
                    CenteredRect(0f, 1.55f, 0.8f, 1.3f),
                    0f,
                    1.54f,
                    true),
                new MothersHouseInteriorFixturePlan(
                    "sofa",
                    MothersHouseInteriorFixtureKind.Sofa,
                    CenteredRect(-2.475f, -0.08f, 0.9f, 2.25f),
                    0f,
                    1.33f,
                    true),
                new MothersHouseInteriorFixturePlan(
                    "fireplace",
                    MothersHouseInteriorFixtureKind.Fireplace,
                    CenteredRect(0f, 3.46f, 2.35f, 1.08f),
                    0f,
                    3.39f,
                    true),
                new MothersHouseInteriorFixturePlan(
                    "old-cupboard",
                    MothersHouseInteriorFixtureKind.Cupboard,
                    CupboardBounds,
                    CupboardBaseHeight,
                    CupboardHeight,
                    true),
                new MothersHouseInteriorFixturePlan(
                    "yarn-basket",
                    MothersHouseInteriorFixtureKind.YarnBasket,
                    YarnBasketBounds,
                    YarnBasketBaseHeight,
                    YarnBasketHeight,
                    true),
                new MothersHouseInteriorFixturePlan(
                    "floor-lamp",
                    MothersHouseInteriorFixtureKind.FloorLamp,
                    FloorLampBounds,
                    0f,
                    FloorLampHeight,
                    true)
            };
        }

        private static Rect CenteredRect(
            float x,
            float z,
            float width,
            float depth)
        {
            return new Rect(
                x - width * 0.5f,
                z - depth * 0.5f,
                width,
                depth);
        }
    }
}
