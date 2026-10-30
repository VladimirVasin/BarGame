using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class StairwellLayoutPlan
    {
        private readonly ReadOnlyCollection<Rect>
            walkableRectangles;

        public StairwellLayoutPlan(
            Vector2 roomSize,
            float roomHeight,
            float streetElevation,
            float middleElevation,
            float apartmentElevation,
            Rect streetLobbyBounds,
            Rect lowerFlightBounds,
            Rect middleLandingBounds,
            Rect apartmentFlightBounds,
            Rect apartmentLandingBounds,
            Rect upperFlightBounds,
            StairwellFlightPlan lowerFlight,
            StairwellFlightPlan apartmentFlight,
            StairwellFlightPlan upperFlight,
            Vector3 streetSpawn,
            Vector3 apartmentSpawn,
            Vector3 streetExitPosition,
            Vector3 streetExitTriggerSize,
            Vector3 apartmentEntrancePosition,
            Vector3 apartmentEntranceTriggerSize,
            Bounds upperBlockerBounds,
            bool isCorrupted = false)
        {
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            StreetElevation = streetElevation;
            MiddleElevation = middleElevation;
            ApartmentElevation = apartmentElevation;
            StreetLobbyBounds = streetLobbyBounds;
            LowerFlightBounds = lowerFlightBounds;
            MiddleLandingBounds = middleLandingBounds;
            ApartmentFlightBounds = apartmentFlightBounds;
            ApartmentLandingBounds = apartmentLandingBounds;
            UpperFlightBounds = upperFlightBounds;
            LowerFlight = lowerFlight;
            ApartmentFlight = apartmentFlight;
            UpperFlight = upperFlight;
            StreetSpawn = streetSpawn;
            ApartmentSpawn = apartmentSpawn;
            StreetExitPosition = streetExitPosition;
            StreetExitTriggerSize = streetExitTriggerSize;
            ApartmentEntrancePosition = apartmentEntrancePosition;
            ApartmentEntranceTriggerSize =
                apartmentEntranceTriggerSize;
            UpperBlockerBounds = upperBlockerBounds;
            IsCorrupted = isCorrupted;
            Labyrinth = isCorrupted ? new StairwellLabyrinthPlan() : null;
            var walkable = isCorrupted
                ? new List<Rect> { apartmentLandingBounds, upperFlightBounds, UpperLandingBounds }
                : new List<Rect>
                {
                    streetLobbyBounds,
                    lowerFlightBounds,
                    middleLandingBounds,
                    apartmentFlightBounds,
                    apartmentLandingBounds,
                    upperFlightBounds
                };
            if (Labyrinth != null)
            {
                foreach (StairwellLabyrinthSupportPlan surface in Labyrinth.Supports)
                    walkable.Add(surface.Bounds);
            }
            walkableRectangles = walkable.AsReadOnly();
        }

        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public float StreetElevation { get; }
        public float MiddleElevation { get; }
        public float ApartmentElevation { get; }
        public Rect StreetLobbyBounds { get; }
        public Rect LowerFlightBounds { get; }
        public Rect MiddleLandingBounds { get; }
        public Rect ApartmentFlightBounds { get; }
        public Rect ApartmentLandingBounds { get; }
        public Rect UpperFlightBounds { get; }
        public StairwellFlightPlan LowerFlight { get; }
        public StairwellFlightPlan ApartmentFlight { get; }
        public StairwellFlightPlan UpperFlight { get; }
        public Vector3 StreetSpawn { get; }
        public Vector3 ApartmentSpawn { get; }
        public Vector3 StreetExitPosition { get; }
        public Vector3 StreetExitTriggerSize { get; }
        public Vector3 ApartmentEntrancePosition { get; }
        public Vector3 ApartmentEntranceTriggerSize { get; }
        public Bounds UpperBlockerBounds { get; }
        public bool IsCorrupted { get; }
        public StairwellLabyrinthPlan Labyrinth { get; }
        public const int CorruptedDistantModules = 3;
        public static readonly Vector3 CatReliefPosition = new Vector3(1.45f, 4.65f, -3.135f);
        public static readonly Vector3 CatReliefSize = new Vector3(.80f, .56f, .25f);
        public Rect UpperLandingBounds => new Rect(-2.225f, .8f, 1.55f, 1.45f);
        public Bounds DescentWallBounds => new Bounds(
            new Vector3(1.45f, 5.25f, -2.88f), new Vector3(2.05f, 4.10f, .30f));
        public IReadOnlyList<Rect> WalkableRectangles =>
            walkableRectangles;

        // Measured production world-rig DoorUseLoop grip, not an IK target.
        // Put each escutcheon on its full-depth latch stile; the source dock
        // aligns the unchanged right-hand press with the lever's front face.
        private static readonly Vector3 DoorUseGrip = new Vector3(.3017243f, 1.1148247f, .6069756f);
        private const float StreetLeverRight = .710f;
        private const float ApartmentLeverRight = .4725f;
        private const float HardwareNormalOffset = .1005f;
        private const float LeverFrontOffset = .0375f;
        private const float LeverHeightOffset = .025f;
        public Vector3 StreetDoorHardwareLocal => new Vector3(-StreetLeverRight,
            PlayerFactory.GroundedRootOffset + DoorUseGrip.y + LeverHeightOffset, HardwareNormalOffset);
        public Vector3 ApartmentDoorHardwareLocal => new Vector3(-HardwareNormalOffset,
            PlayerFactory.GroundedRootOffset + DoorUseGrip.y + LeverHeightOffset, -ApartmentLeverRight);
        public Quaternion StreetDoorHardwareRotation => Quaternion.Euler(0f, 0f, 180f);
        public Quaternion ApartmentDoorHardwareRotation =>
            Quaternion.Euler(0f, -90f, 0f) * StreetDoorHardwareRotation;
        public Vector3 StreetDoorActionRoot => new Vector3(
            StreetExitPosition.x - StreetLeverRight + DoorUseGrip.x,
            StreetElevation + PlayerFactory.GroundedRootOffset,
            -RoomSize.y * .5f + .13f + HardwareNormalOffset + LeverFrontOffset + DoorUseGrip.z);
        public Vector3 ApartmentDoorActionRoot => new Vector3(
            RoomSize.x * .5f - .13f - HardwareNormalOffset - LeverFrontOffset - DoorUseGrip.z,
            ApartmentElevation + PlayerFactory.GroundedRootOffset,
            ApartmentEntrancePosition.z - ApartmentLeverRight + DoorUseGrip.x);

        public Vector3 GetSpawn(StairwellArrivalKind arrival)
        {
            switch (arrival)
            {
                case StairwellArrivalKind.StreetDoor:
                    // The lower stairwell remains visible but unreachable.
                    // Debug/stale arrivals start beside the surviving Home door.
                    return IsCorrupted ? ApartmentSpawn : StreetSpawn;
                case StairwellArrivalKind.ApartmentDoor:
                    return ApartmentSpawn;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(arrival));
            }
        }
    }
}
