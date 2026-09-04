using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum MothersHouseInteriorPathKind
    {
        EntryApproach = 0,
        MainPassage = 1,
        TableApproach = 2,
        UpperCorridorRun = 3,
        UpperNorthApproach = 4,
        UpperSouthApproach = 5
    }

    public enum MothersHouseInteriorFixtureKind
    {
        LowTable = 0,
        RockingChair = 1,
        Sofa = 2,
        Fireplace = 3,
        Cupboard = 4,
        YarnBasket = 5,
        FloorLamp = 6,
        UpperChimney = 7,
        UpperNorthBed = 8,
        UpperNorthChest = 9,
        UpperNorthBedside = 10,
        UpperSouthBed = 11,
        UpperSouthChair = 12,
        UpperCorridorChest = 13,
        UpperNorthWardrobe = 14,
        UpperNorthTrunk = 15,
        UpperNorthChair = 16,
        UpperSouthLinenPress = 17,
        UpperSouthTable = 18,
        UpperSouthTrunk = 19,
        UpperSouthBasket = 20,
        UpperCorridorPail = 21
    }

    public readonly struct MothersHouseInteriorPathPlan
    {
        internal MothersHouseInteriorPathPlan(
            string id,
            MothersHouseInteriorPathKind kind,
            Rect bounds,
            float minimumClearance,
            float floorElevation = 0f)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            MinimumClearance = minimumClearance;
            FloorElevation = floorElevation;
        }

        public string Id { get; }
        public MothersHouseInteriorPathKind Kind { get; }
        public Rect Bounds { get; }
        public float MinimumClearance { get; }

        /// <summary>
        /// Which floor this route belongs to. The two storeys overlap in
        /// <c>X/Z</c>, so a route may only be measured against the furniture
        /// standing on its own floor.
        /// </summary>
        public float FloorElevation { get; }
    }

    public readonly struct MothersHouseInteriorFixturePlan
    {
        internal MothersHouseInteriorFixturePlan(
            string id,
            MothersHouseInteriorFixtureKind kind,
            Rect bounds,
            float baseHeight,
            float height,
            bool blocksMovement)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            BaseHeight = baseHeight;
            Height = height;
            BlocksMovement = blocksMovement;
        }

        public string Id { get; }
        public MothersHouseInteriorFixtureKind Kind { get; }
        public Rect Bounds { get; }
        public float BaseHeight { get; }
        public float Height { get; }
        public bool BlocksMovement { get; }
        public Vector3 Center => new Vector3(
            Bounds.center.x,
            BaseHeight + Height * 0.5f,
            Bounds.center.y);
        public Vector3 Size => new Vector3(
            Bounds.width,
            Height,
            Bounds.height);
    }

    public sealed class MothersHouseInteriorUpperFloorPlan
    {
        internal MothersHouseInteriorUpperFloorPlan(
            float floorElevation,
            float ceilingHeight,
            StairwellFlightPlan stairFlight,
            Rect stairOpeningBounds,
            Rect corridorBounds,
            Rect southRoomBounds,
            Rect northRoomBounds,
            float partitionX,
            float partitionThickness,
            float roomDividerZ,
            float doorOpeningWidth,
            float doorOpeningHeight,
            float southDoorCenterZ,
            float northDoorCenterZ,
            Vector3 northWindowPosition,
            Vector3 southWindowPosition,
            Vector3 northLampPosition,
            Vector3 southLampPosition)
        {
            NorthWindowPosition = northWindowPosition;
            SouthWindowPosition = southWindowPosition;
            NorthLampPosition = northLampPosition;
            SouthLampPosition = southLampPosition;
            FloorElevation = floorElevation;
            CeilingHeight = ceilingHeight;
            StairFlight = stairFlight;
            StairOpeningBounds = stairOpeningBounds;
            CorridorBounds = corridorBounds;
            SouthRoomBounds = southRoomBounds;
            NorthRoomBounds = northRoomBounds;
            PartitionX = partitionX;
            PartitionThickness = partitionThickness;
            RoomDividerZ = roomDividerZ;
            DoorOpeningWidth = doorOpeningWidth;
            DoorOpeningHeight = doorOpeningHeight;
            SouthDoorCenterZ = southDoorCenterZ;
            NorthDoorCenterZ = northDoorCenterZ;
        }

        public float FloorElevation { get; }
        public float CeilingHeight { get; }
        public StairwellFlightPlan StairFlight { get; }
        public Rect StairOpeningBounds { get; }
        public Rect CorridorBounds { get; }
        public Rect SouthRoomBounds { get; }
        public Rect NorthRoomBounds { get; }
        public float PartitionX { get; }
        public float PartitionThickness { get; }
        public float RoomDividerZ { get; }
        public float DoorOpeningWidth { get; }
        public float DoorOpeningHeight { get; }
        public float SouthDoorCenterZ { get; }
        public float NorthDoorCenterZ { get; }

        /// <summary>
        /// One window per bedroom, in that room's own outer wall. The summit
        /// house already lights one upper pane on each of those two facades
        /// from the village side.
        /// </summary>
        public Vector3 NorthWindowPosition { get; }
        public Vector3 SouthWindowPosition { get; }

        /// <summary>
        /// One hanging light per bedroom. The parents' room carries an enamel
        /// bowl shade; the childhood room's flex carries a bare bulb, because
        /// the shade came off it when the room went out of use.
        /// </summary>
        public Vector3 NorthLampPosition { get; }
        public Vector3 SouthLampPosition { get; }

        public Vector3 SouthRoomCenter => new Vector3(
            SouthRoomBounds.center.x,
            FloorElevation + PlayerFactory.GroundedRootOffset,
            SouthRoomBounds.center.y);

        public Vector3 NorthRoomCenter => new Vector3(
            NorthRoomBounds.center.x,
            FloorElevation + PlayerFactory.GroundedRootOffset,
            NorthRoomBounds.center.y);
    }

    public sealed class MothersHouseInteriorLayoutPlan
    {
        internal MothersHouseInteriorLayoutPlan(
            Vector2 roomSize,
            float roomHeight,
            float wallThickness,
            float doorOpeningWidth,
            Rect roomBounds,
            Bounds modelLocalBounds,
            Rect walkableBounds,
            Vector3 entryPosition,
            Vector3 playerSpawn,
            Vector3 exitPosition,
            Vector3 exitTriggerSize,
            Vector3 cameraTarget,
            IList<HomeCameraShot> cameraShots,
            MothersHouseInteriorUpperFloorPlan upperFloor,
            Vector3 westWindowPosition,
            Vector3 eastWindowPosition,
            Vector3 fireplacePosition,
            Vector3 fireLightPosition,
            Vector3 floorLampLightPosition,
            Vector3 tabletopPosition,
            Vector3 teapotDockPosition,
            string modelResourcePath,
            IList<MothersHouseInteriorPathPlan> paths,
            IList<MothersHouseInteriorFixturePlan> fixtures)
        {
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            WallThickness = wallThickness;
            DoorOpeningWidth = doorOpeningWidth;
            RoomBounds = roomBounds;
            ModelLocalBounds = modelLocalBounds;
            WalkableBounds = walkableBounds;
            EntryPosition = entryPosition;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitTriggerSize = exitTriggerSize;
            CameraTarget = cameraTarget;
            CameraShots = Copy(cameraShots, nameof(cameraShots));
            if (!TryGetCameraShot(
                    HomeCameraShotKind.MainRoom,
                    out HomeCameraShot groundShot))
            {
                throw new ArgumentException(
                    "The mother's house requires its ground-floor camera shot.",
                    nameof(cameraShots));
            }

            CameraShot = groundShot;
            UpperFloor = upperFloor ??
                throw new ArgumentNullException(nameof(upperFloor));
            WestWindowPosition = westWindowPosition;
            EastWindowPosition = eastWindowPosition;
            FireplacePosition = fireplacePosition;
            FireLightPosition = fireLightPosition;
            FloorLampLightPosition = floorLampLightPosition;
            TabletopPosition = tabletopPosition;
            TeapotDockPosition = teapotDockPosition;
            ModelResourcePath = modelResourcePath ?? string.Empty;
            Paths = Copy(paths, nameof(paths));
            Fixtures = Copy(fixtures, nameof(fixtures));
        }

        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public float WallThickness { get; }
        public float DoorOpeningWidth { get; }
        public Rect RoomBounds { get; }
        public Bounds ModelLocalBounds { get; }
        public Rect WalkableBounds { get; }
        public Vector3 EntryPosition { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public Vector3 CameraTarget { get; }
        public HomeCameraShot CameraShot { get; }
        public IReadOnlyList<HomeCameraShot> CameraShots { get; }
        public MothersHouseInteriorUpperFloorPlan UpperFloor { get; }
        public Vector3 WestWindowPosition { get; }
        public Vector3 EastWindowPosition { get; }
        public Vector3 FireplacePosition { get; }
        public Vector3 FireLightPosition { get; }
        public Vector3 FloorLampLightPosition { get; }
        public Vector3 TabletopPosition { get; }
        public Vector3 TeapotDockPosition { get; }
        public string ModelResourcePath { get; }
        public IReadOnlyList<MothersHouseInteriorPathPlan> Paths { get; }
        public IReadOnlyList<MothersHouseInteriorFixturePlan> Fixtures { get; }

        public bool TryGetCameraShot(
            HomeCameraShotKind kind,
            out HomeCameraShot shot)
        {
            for (int index = 0; index < CameraShots.Count; index++)
            {
                if (CameraShots[index].Kind == kind)
                {
                    shot = CameraShots[index];
                    return true;
                }
            }

            shot = default;
            return false;
        }

        public bool TryGetPath(
            MothersHouseInteriorPathKind kind,
            out MothersHouseInteriorPathPlan path)
        {
            for (int index = 0; index < Paths.Count; index++)
            {
                if (Paths[index].Kind == kind)
                {
                    path = Paths[index];
                    return true;
                }
            }

            path = default;
            return false;
        }

        public bool TryGetFixture(
            MothersHouseInteriorFixtureKind kind,
            out MothersHouseInteriorFixturePlan fixture)
        {
            for (int index = 0; index < Fixtures.Count; index++)
            {
                if (Fixtures[index].Kind == kind)
                {
                    fixture = Fixtures[index];
                    return true;
                }
            }

            fixture = default;
            return false;
        }

        private static IReadOnlyList<T> Copy<T>(
            IList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(new List<T>(source));
        }
    }
}
