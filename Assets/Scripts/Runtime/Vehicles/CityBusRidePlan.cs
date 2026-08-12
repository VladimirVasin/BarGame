using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBusPassengerDoor
    {
        Front = 0,
        Rear
    }

    /// <summary>
    /// One deterministic passenger-door transfer and fixed passenger-seat plan.
    /// Grounded entry/exit stay in City space while the seat, doorway and
    /// camera remain bound to the live sprung bus presentation.
    /// </summary>
    public sealed class CityBusRidePlan
    {
        // Seat 07 is the first window seat on the lateral side opposite the
        // production driver's seat and remains practical from either door.
        public const int PassengerSeatIndex = CityBusActor.PlayerSeatIndex;
        // Keep the waiting capsule outside the bus obstacle corridor so the
        // route actor can finish pulling into its stop instead of yielding to
        // the passenger it is about to collect.
        public const float DoorBodyClearance = 0.34f;
        public const float ExitLongitudinalOffset = 0.70f;
        public const float CameraFieldOfView = 60f;
        private const float SurfaceContainmentEpsilon = 0.001f;

        private static readonly float[] EntryDockOffsets =
        {
            0f,
            0.45f,
            -0.45f,
            0.90f,
            -0.90f
        };

        private static readonly float[] ExitDockOffsets =
        {
            ExitLongitudinalOffset,
            -ExitLongitudinalOffset,
            1.15f,
            -1.15f,
            0f
        };

        private CityBusRidePlan(
            CityBusStopDescriptor stop,
            CityBusPassengerDoor passengerDoor,
            int seatIndex,
            Transform actorRoot,
            Transform body,
            Transform doorAnchor,
            Transform seatAnchor,
            PlayerAnimatedInteractionPose entryPose,
            Vector3 doorHipPosition,
            Vector3 rideRootLocalPosition,
            Quaternion rideRootLocalRotation,
            PlayerAnimatedInteractionPose exitPose,
            PlayerAnimatedInteractionPelvisTransition transition)
        {
            Stop = stop;
            PassengerDoor = passengerDoor;
            SeatIndex = seatIndex;
            ActorRoot = actorRoot;
            Body = body;
            DoorAnchor = doorAnchor;
            SeatAnchor = seatAnchor;
            EntryPose = entryPose;
            DoorHipPosition = doorHipPosition;
            RideRootLocalPosition = rideRootLocalPosition;
            RideRootLocalRotation = rideRootLocalRotation;
            ExitPose = exitPose;
            PelvisTransition = transition;
        }

        public CityBusStopDescriptor Stop { get; }
        public CityBusPassengerDoor PassengerDoor { get; }
        public int SeatIndex { get; }
        public Transform ActorRoot { get; }
        public Transform Body { get; }
        public Transform DoorAnchor { get; }
        public Transform SeatAnchor { get; }
        public PlayerAnimatedInteractionPose EntryPose { get; }
        public Vector3 DoorHipPosition { get; }
        public Vector3 RideRootLocalPosition { get; }
        public Quaternion RideRootLocalRotation { get; }
        public PlayerAnimatedInteractionPose ExitPose { get; }
        public PlayerAnimatedInteractionPelvisTransition PelvisTransition
        {
            get;
        }

        public Vector3 InteractionPosition => EntryPose.RootPosition;
        public Vector3 ActionHipPosition => SeatAnchor.position;

        public static bool TryCreate(
            CityBusActor actor,
            IWalkableArea walkableArea,
            Vector3 neutralPelvisLocalPosition,
            float playerRadius,
            out CityBusRidePlan plan)
        {
            return TryCreate(
                actor,
                walkableArea,
                neutralPelvisLocalPosition,
                playerRadius,
                CityBusPassengerDoor.Front,
                out plan);
        }

        public static bool TryCreate(
            CityBusActor actor,
            IWalkableArea walkableArea,
            Vector3 neutralPelvisLocalPosition,
            float playerRadius,
            CityBusPassengerDoor passengerDoor,
            out CityBusRidePlan plan)
        {
            return TryCreate(
                actor,
                walkableArea,
                neutralPelvisLocalPosition,
                playerRadius,
                passengerDoor,
                null,
                out plan);
        }

        public static bool TryCreate(
            CityBusActor actor,
            IWalkableArea walkableArea,
            Vector3 neutralPelvisLocalPosition,
            float playerRadius,
            CityBusPassengerDoor passengerDoor,
            CityStreetSurfacePlan streetSurfacePlan,
            out CityBusRidePlan plan)
        {
            return TryCreate(
                actor,
                walkableArea,
                neutralPelvisLocalPosition,
                playerRadius,
                passengerDoor,
                streetSurfacePlan,
                PassengerSeatIndex,
                PlayerFactory.GroundedRootOffset,
                true,
                out plan);
        }

        /// <summary>
        /// The complete transfer plan. An ambient passenger passes its own
        /// seat index, capsule radius and grounded-root offset and receives
        /// the same validated docks.
        /// <para>
        /// <paramref name="requireOppositeDriverSide"/> guards the hero alone.
        /// Seat `07` must sit opposite the driver because his authored
        /// `BusRideLoop` and the seated camera that looks through the nearest
        /// side window are built around that lateral side. An ambient
        /// passenger has neither, and half the cabin — the whole driver-side
        /// row — is on the other side, so enforcing it there rejects every
        /// plan and nobody ever boards or gets off.
        /// </para>
        /// </summary>
        public static bool TryCreate(
            CityBusActor actor,
            IWalkableArea walkableArea,
            Vector3 neutralPelvisLocalPosition,
            float agentRadius,
            CityBusPassengerDoor passengerDoor,
            CityStreetSurfacePlan streetSurfacePlan,
            int seatIndex,
            float groundedRootOffset,
            bool requireOppositeDriverSide,
            out CityBusRidePlan plan)
        {
            plan = null;
            if (actor == null ||
                !actor.IsSpawned ||
                actor.CurrentStop == null ||
                actor.Presentation == null ||
                actor.Presentation.Registry == null ||
                walkableArea == null ||
                !IsFinite(neutralPelvisLocalPosition) ||
                !IsFinite(agentRadius) ||
                agentRadius <= 0f ||
                !IsFinite(groundedRootOffset) ||
                seatIndex < 0)
            {
                return false;
            }

            float playerRadius = agentRadius;
            CityBusAssetRegistry registry =
                actor.Presentation.Registry;
            if (registry.Body == null ||
                registry.DriverSeatAnchor == null ||
                registry.FrontDoorEntryAnchor == null ||
                registry.RearDoorEntryAnchor == null ||
                registry.PassengerSeatAnchors == null ||
                registry.PassengerSeatAnchors.Count <= seatIndex)
            {
                return false;
            }

            Transform seat = registry.PassengerSeatAnchors[seatIndex];
            if (seat == null)
            {
                return false;
            }

            Transform actorRoot = actor.transform;
            Vector3 actorUp = Vector3.up;
            Vector3 actorForward = Vector3.ProjectOnPlane(
                actorRoot.forward,
                actorUp);
            if (actorForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            actorForward.Normalize();
            Vector3 actorRight =
                Vector3.Cross(actorUp, actorForward).normalized;
            float driverSide = Vector3.Dot(
                registry.DriverSeatAnchor.position - actorRoot.position,
                actorRight);
            float passengerSide = Vector3.Dot(
                seat.position - actorRoot.position,
                actorRight);
            if (Mathf.Abs(driverSide) <= 0.01f ||
                Mathf.Abs(passengerSide) <= 0.01f)
            {
                return false;
            }

            if (requireOppositeDriverSide &&
                driverSide * passengerSide >= 0f)
            {
                return false;
            }

            Transform door = passengerDoor == CityBusPassengerDoor.Front
                ? registry.FrontDoorEntryAnchor
                : registry.RearDoorEntryAnchor;
            Vector3 up = actorRoot.up;
            Vector3 forward = Vector3.ProjectOnPlane(
                actorRoot.forward,
                up);
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            forward.Normalize();
            Vector3 doorOffset = Vector3.ProjectOnPlane(
                door.position - actorRoot.position,
                up);
            Vector3 outward = doorOffset -
                forward * Vector3.Dot(doorOffset, forward);
            if (outward.sqrMagnitude <= 0.0001f)
            {
                float side = Vector3.Dot(
                    doorOffset,
                    actorRoot.right);
                outward = actorRoot.right *
                    (side < 0f ? -1f : 1f);
            }

            outward.Normalize();
            float currentDoorDepth = Vector3.Dot(
                door.position - actorRoot.position,
                outward);
            float desiredDoorDepth =
                registry.Dimensions.Width * 0.5f +
                playerRadius +
                DoorBodyClearance;
            Vector3 outsideDoor = door.position +
                outward * Mathf.Max(
                    0f,
                    desiredDoorDepth - currentDoorDepth);
            outsideDoor.y =
                ResolveGroundedRootY(
                    outsideDoor,
                    streetSurfacePlan,
                    groundedRootOffset);

            if (!TrySelectDock(
                    outsideDoor,
                    forward,
                    EntryDockOffsets,
                    walkableArea,
                    playerRadius,
                    streetSurfacePlan,
                    groundedRootOffset,
                    out Vector3 entryRoot) ||
                !TrySelectDock(
                    outsideDoor,
                    forward,
                    ExitDockOffsets,
                    walkableArea,
                    playerRadius,
                    streetSurfacePlan,
                    groundedRootOffset,
                    out Vector3 exitRoot))
            {
                return false;
            }

            Quaternion facing = Quaternion.LookRotation(forward, up);
            Vector3 entryHip = entryRoot +
                facing * neutralPelvisLocalPosition;
            Vector3 exitHip = exitRoot +
                facing * neutralPelvisLocalPosition;
            Vector3 doorRoot = door.position;
            Vector3 doorHip = doorRoot +
                facing * neutralPelvisLocalPosition;
            Vector3 seatFloor = seat.position +
                up * Vector3.Dot(
                    doorRoot - seat.position,
                    up);
            Vector3 rideRootLocal =
                actorRoot.InverseTransformPoint(seatFloor);
            Quaternion rideRootLocalRotation =
                Quaternion.Inverse(actorRoot.rotation) * facing;

            var entryPose = new PlayerAnimatedInteractionPose(
                entryRoot,
                facing,
                entryHip);
            var exitPose = new PlayerAnimatedInteractionPose(
                exitRoot,
                facing,
                exitHip);
            var transition =
                new PlayerAnimatedInteractionPelvisTransition(
                    doorHip,
                    enterArrivalProgress: 0.42f,
                    enterDepartureProgress: 0.54f,
                    exitArrivalProgress: 0.46f,
                    exitDepartureProgress: 0.60f);
            plan = new CityBusRidePlan(
                actor.CurrentStop,
                passengerDoor,
                seatIndex,
                actorRoot,
                registry.Body,
                door,
                seat,
                entryPose,
                doorHip,
                rideRootLocal,
                rideRootLocalRotation,
                exitPose,
                transition);
            return true;
        }

        /// <summary>
        /// Resolves the actor-local floor pose of one passenger seat without
        /// a transfer. A full ride plan needs a served stop and two validated
        /// roadside docks, none of which exist while the bus is cruising, but
        /// a passenger who is simply already aboard needs neither.
        /// </summary>
        public static bool TryCreateSeatedPose(
            CityBusActor actor,
            int seatIndex,
            out Vector3 rideRootLocalPosition,
            out Quaternion rideRootLocalRotation,
            out Transform seatAnchor)
        {
            rideRootLocalPosition = Vector3.zero;
            rideRootLocalRotation = Quaternion.identity;
            seatAnchor = null;
            if (actor == null ||
                !actor.IsSpawned ||
                actor.Presentation == null ||
                actor.Presentation.Registry == null ||
                seatIndex < 0)
            {
                return false;
            }

            CityBusAssetRegistry registry = actor.Presentation.Registry;
            if (registry.PassengerSeatAnchors == null ||
                registry.PassengerSeatAnchors.Count <= seatIndex ||
                registry.FrontDoorEntryAnchor == null)
            {
                return false;
            }

            Transform seat = registry.PassengerSeatAnchors[seatIndex];
            if (seat == null)
            {
                return false;
            }

            Transform actorRoot = actor.transform;
            Vector3 up = actorRoot.up;
            // The door entry anchor sits on the cabin floor, which is the same
            // plane a seated passenger's root stands on.
            Vector3 floorReference = registry.FrontDoorEntryAnchor.position;
            Vector3 seatFloor = seat.position +
                up * Vector3.Dot(floorReference - seat.position, up);
            rideRootLocalPosition =
                actorRoot.InverseTransformPoint(seatFloor);
            rideRootLocalRotation = Quaternion.identity;
            seatAnchor = seat;
            return true;
        }

        public void EvaluateRideCamera(
            out Vector3 position,
            out Quaternion rotation)
        {
            EvaluateRideCamera(
                0f,
                0f,
                out position,
                out rotation);
        }

        public void EvaluateRideCamera(
            float yawOffsetDegrees,
            float pitchDegrees,
            out Vector3 position,
            out Quaternion rotation)
        {
            // The seat position follows the sprung body, but view axes remain
            // world-level. Otherwise suspension pitch/roll couples mouse yaw
            // into pitch and visibly tilts the horizon.
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(
                ActorRoot.forward,
                up);
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 pelvis = SeatAnchor.position;
            float seatSide = Vector3.Dot(
                pelvis - ActorRoot.position,
                right);
            Vector3 windowOutward = seatSide < 0f
                ? -right
                : right;
            position = pelvis +
                up * 0.78f -
                forward * 0.52f -
                windowOutward * 0.56f;
            Vector3 defaultDirection =
                (windowOutward + forward * 0.18f).normalized;
            Vector3 planarDirection =
                Quaternion.AngleAxis(yawOffsetDegrees, up) *
                defaultDirection;
            Vector3 viewRight =
                Vector3.Cross(up, planarDirection).normalized;
            Vector3 viewDirection =
                Quaternion.AngleAxis(pitchDegrees, viewRight) *
                planarDirection;
            rotation = Quaternion.LookRotation(
                viewDirection,
                up);
        }

        private static bool TrySelectDock(
            Vector3 origin,
            Vector3 forward,
            float[] offsets,
            IWalkableArea walkableArea,
            float playerRadius,
            CityStreetSurfacePlan streetSurfacePlan,
            float groundedRootOffset,
            out Vector3 selected)
        {
            for (int index = 0; index < offsets.Length; index++)
            {
                Vector3 candidate = origin + forward * offsets[index];
                candidate.y = ResolveGroundedRootY(
                    candidate,
                    streetSurfacePlan,
                    groundedRootOffset);
                if (walkableArea.Contains(candidate, playerRadius))
                {
                    selected = candidate;
                    return true;
                }
            }

            selected = default;
            return false;
        }

        /// <summary>
        /// The same deterministic street surface plan that builds the physical
        /// City geometry, so a door on a flat bus apron never targets the
        /// raised curb.
        /// </summary>
        public static float ResolveGroundedRootY(
            Vector3 position,
            CityStreetSurfacePlan streetSurfacePlan,
            float groundedRootOffset)
        {
            if (streetSurfacePlan == null)
            {
                return CityStreetSurfacePlanner.SidewalkTop +
                       groundedRootOffset;
            }

            float surfaceTop = CityStreetSurfacePlanner.RoadTop;
            IReadOnlyList<Bounds> sidewalks =
                streetSurfacePlan.Sidewalks;
            for (int index = 0; index < sidewalks.Count; index++)
            {
                Bounds sidewalk = sidewalks[index];
                if (position.x < sidewalk.min.x -
                        SurfaceContainmentEpsilon ||
                    position.x > sidewalk.max.x +
                        SurfaceContainmentEpsilon ||
                    position.z < sidewalk.min.z -
                        SurfaceContainmentEpsilon ||
                    position.z > sidewalk.max.z +
                        SurfaceContainmentEpsilon)
                {
                    continue;
                }

                surfaceTop = Mathf.Max(surfaceTop, sidewalk.max.y);
            }

            return surfaceTop + groundedRootOffset;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
