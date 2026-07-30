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
            Bounds upperBlockerBounds)
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
            walkableRectangles =
                new List<Rect>
                {
                    streetLobbyBounds,
                    lowerFlightBounds,
                    middleLandingBounds,
                    apartmentFlightBounds,
                    apartmentLandingBounds,
                    upperFlightBounds
                }.AsReadOnly();
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
        public IReadOnlyList<Rect> WalkableRectangles =>
            walkableRectangles;

        public Vector3 GetSpawn(StairwellArrivalKind arrival)
        {
            switch (arrival)
            {
                case StairwellArrivalKind.StreetDoor:
                    return StreetSpawn;
                case StairwellArrivalKind.ApartmentDoor:
                    return ApartmentSpawn;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(arrival));
            }
        }
    }
}
