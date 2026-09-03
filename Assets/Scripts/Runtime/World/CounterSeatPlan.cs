using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// World-space contract for one counter seat. Entry and exit are kept as
    /// independent poses even when both use the same authored standing dock.
    /// The camera is stored relative to the action pelvis so it follows the
    /// live rig instead of a second, detached seat transform.
    /// </summary>
    public sealed class CounterSeatPlan
    {
        public const float SeatClearance = 0.03f;
        public const float FallbackSeatTopHeight = 0.8975f;
        public const float FallbackSeatDepth = 0.48f;
        public const float FallbackDockClearance = 0.52f;
        public const float ApproachVerticalTolerance = 0.25f;
        public const float MinimumFieldOfView = 35f;
        public const float MaximumFieldOfView = 75f;
        public const int MaximumApproachWaypoints = 2;

        private readonly Vector3[] approachWaypoints;

        public CounterSeatPlan(
            string id,
            Vector3 interactionPosition,
            PlayerAnimatedInteractionPose entryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose exitPose,
            PlayerAnimatedInteractionPelvisTransition pelvisTransition,
            Vector3 cameraPosition,
            Quaternion cameraRotation,
            float cameraFieldOfView,
            IReadOnlyList<Vector3> authoredApproachWaypoints = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A counter seat needs a stable id.",
                    nameof(id));
            }

            entryPose.Validate(nameof(entryPose));
            exitPose.Validate(nameof(exitPose));
            pelvisTransition.Validate(nameof(pelvisTransition));
            RequireFinite(interactionPosition, nameof(interactionPosition));
            RequireFinite(actionHipPosition, nameof(actionHipPosition));
            RequireFinite(cameraPosition, nameof(cameraPosition));
            RequireRotation(cameraRotation, nameof(cameraRotation));
            if (!IsFinite(cameraFieldOfView) ||
                cameraFieldOfView < MinimumFieldOfView ||
                cameraFieldOfView > MaximumFieldOfView)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cameraFieldOfView),
                    cameraFieldOfView,
                    "The counter-seat camera field of view is invalid.");
            }

            int waypointCount = authoredApproachWaypoints?.Count ?? 0;
            if (waypointCount > MaximumApproachWaypoints)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(authoredApproachWaypoints),
                    waypointCount,
                    $"A counter seat supports at most " +
                    $"{MaximumApproachWaypoints} approach waypoints.");
            }

            approachWaypoints = new Vector3[waypointCount];
            for (int index = 0; index < waypointCount; index++)
            {
                Vector3 waypoint = authoredApproachWaypoints[index];
                RequireFinite(waypoint, nameof(authoredApproachWaypoints));
                approachWaypoints[index] = waypoint;
            }

            Id = id;
            InteractionPosition = interactionPosition;
            EntryPose = entryPose;
            ActionHipPosition = actionHipPosition;
            ExitPose = exitPose;
            PelvisTransition = pelvisTransition;
            CameraOffsetFromActionHip = cameraPosition - actionHipPosition;
            CameraRotation = Normalize(cameraRotation);
            CameraFieldOfView = cameraFieldOfView;
        }

        public string Id { get; }
        public Vector3 InteractionPosition { get; }
        public PlayerAnimatedInteractionPose EntryPose { get; }
        public Vector3 ActionHipPosition { get; }
        public PlayerAnimatedInteractionPose ExitPose { get; }
        public PlayerAnimatedInteractionPelvisTransition PelvisTransition
        {
            get;
        }

        public Vector3 CameraOffsetFromActionHip { get; }
        public Quaternion CameraRotation { get; }
        public float CameraFieldOfView { get; }
        public int ApproachWaypointCount => approachWaypoints.Length;
        public IReadOnlyList<Vector3> ApproachWaypoints =>
            approachWaypoints;

        /// <summary>
        /// Copies only the approach corners the current position still needs.
        /// A player already between the standing dock and the counter must not
        /// backtrack to the outer approach anchor before taking one short step
        /// to the dock.
        /// </summary>
        public int BuildApproachWaypoints(
            Vector3 fromPosition,
            Vector3[] buffer)
        {
            if (buffer == null ||
                buffer.Length < MaximumApproachWaypoints)
            {
                throw new ArgumentException(
                    $"The counter-seat waypoint buffer must hold " +
                    $"{MaximumApproachWaypoints} positions.",
                    nameof(buffer));
            }

            if (approachWaypoints.Length == 0)
            {
                return 0;
            }

            Vector3 toEntry = EntryPose.RootPosition - fromPosition;
            Vector3 toFirst = approachWaypoints[0] - fromPosition;
            toEntry.y = 0f;
            toFirst.y = 0f;
            Vector3 entryForward =
                EntryPose.RootRotation * Vector3.forward;
            entryForward.y = 0f;
            Vector3 entryToPlayer =
                fromPosition - EntryPose.RootPosition;
            entryToPlayer.y = 0f;
            bool alreadyCounterSide =
                entryForward.sqrMagnitude > 0.0001f &&
                Vector3.Dot(
                    entryToPlayer,
                    entryForward.normalized) >= -0.02f;
            int sourceStart = alreadyCounterSide &&
                              toEntry.sqrMagnitude <=
                              toFirst.sqrMagnitude
                ? 1
                : 0;
            int copied = 0;
            for (int index = sourceStart;
                 index < approachWaypoints.Length;
                 index++)
            {
                buffer[copied++] = approachWaypoints[index];
            }

            return copied;
        }

        /// <summary>
        /// Creates the seat from the existing local drink-service plan. This
        /// is the compatibility path for the v2 room: its stool root is on the
        /// floor and its seat top is derived from the measured stool height.
        /// </summary>
        public static CounterSeatPlan FromService(
            Transform serviceSpace,
            BarDrinkServicePlan servicePlan)
        {
            return FromServicePoses(
                serviceSpace,
                servicePlan,
                authoredSeatTopPose: null,
                authoredApproachGroundPosition: null,
                authoredEntryGroundPose: null,
                authoredExitGroundPose: null,
                authoredCameraPose: null,
                authoredCameraLookAt: null);
        }

        /// <summary>
        /// Creates the production seat from Blender anchors. Seat is the top
        /// contact point; approach/entry/exit positions are floor contacts and
        /// receive the real grounded player-root offset. A missing anchor uses
        /// the measured service-plan fallback, so an older bar remains usable.
        /// </summary>
        public static CounterSeatPlan FromServiceAnchors(
            Transform serviceSpace,
            BarDrinkServicePlan servicePlan,
            Transform seatTopAnchor,
            Transform approachGroundAnchor,
            Transform entryGroundAnchor,
            Transform exitGroundAnchor = null,
            Transform cameraAnchor = null,
            Transform cameraLookAtAnchor = null)
        {
            if (serviceSpace == null)
            {
                throw new ArgumentNullException(nameof(serviceSpace));
            }

            if (servicePlan == null)
            {
                throw new ArgumentNullException(nameof(servicePlan));
            }

            Quaternion facing = ResolveAnchorFacing(
                serviceSpace,
                servicePlan,
                seatTopAnchor,
                entryGroundAnchor);
            Pose? seat = ToPose(seatTopAnchor, facing);
            Vector3? approach = approachGroundAnchor != null
                ? approachGroundAnchor.position
                : (Vector3?)null;
            Pose? entry = ToPose(entryGroundAnchor, facing);
            Pose? exit = ToPose(exitGroundAnchor, facing);
            Pose? camera = ToPose(cameraAnchor);
            Vector3? cameraLookAt = cameraLookAtAnchor != null
                ? cameraLookAtAnchor.position
                : (Vector3?)null;
            return FromServicePoses(
                serviceSpace,
                servicePlan,
                seat,
                approach,
                entry,
                exit,
                camera,
                cameraLookAt);
        }

        /// <summary>
        /// Value-only authoring boundary used by model registries and tests.
        /// Nullable values deliberately express the legacy fallback contract.
        /// </summary>
        public static CounterSeatPlan FromServicePoses(
            Transform serviceSpace,
            BarDrinkServicePlan servicePlan,
            Pose? authoredSeatTopPose,
            Vector3? authoredApproachGroundPosition,
            Pose? authoredEntryGroundPose,
            Pose? authoredExitGroundPose,
            Pose? authoredCameraPose,
            Vector3? authoredCameraLookAt)
        {
            if (serviceSpace == null)
            {
                throw new ArgumentNullException(nameof(serviceSpace));
            }

            if (servicePlan == null)
            {
                throw new ArgumentNullException(nameof(servicePlan));
            }

            Pose fallbackSeat = new Pose(
                serviceSpace.TransformPoint(
                    servicePlan.SeatPose.Position +
                    Vector3.up * FallbackSeatTopHeight),
                serviceSpace.rotation * servicePlan.SeatPose.Rotation);
            Pose seat = authoredSeatTopPose ?? fallbackSeat;
            RequireFinite(seat.position, nameof(authoredSeatTopPose));
            RequireRotation(seat.rotation, nameof(authoredSeatTopPose));

            Vector3 facing = Vector3.ProjectOnPlane(
                seat.rotation * Vector3.forward,
                Vector3.up);
            if (facing.sqrMagnitude < 0.0001f)
            {
                throw new ArgumentException(
                    "The counter seat must face horizontally.",
                    nameof(authoredSeatTopPose));
            }

            facing.Normalize();
            Quaternion seatFacing = Quaternion.LookRotation(
                facing,
                Vector3.up);
            Vector3 fallbackFloor = serviceSpace.TransformPoint(
                servicePlan.ServiceStoolPosition);
            Vector3 fallbackEntryRoot = seat.position -
                facing * (FallbackSeatDepth * 0.5f +
                          FallbackDockClearance);
            fallbackEntryRoot.y = fallbackFloor.y +
                PlayerFactory.GroundedRootOffset;

            Pose entryGround = authoredEntryGroundPose ?? new Pose(
                fallbackEntryRoot -
                Vector3.up * PlayerFactory.GroundedRootOffset,
                seatFacing);
            Pose exitGround = authoredExitGroundPose ?? entryGround;
            PlayerAnimatedInteractionPose entry = CreateRootPose(
                entryGround,
                seatFacing);
            PlayerAnimatedInteractionPose exit = CreateRootPose(
                exitGround,
                seatFacing);
            Vector3 actionHip = seat.position +
                Vector3.up * SeatClearance;

            Vector3 transitionGround = seat.position -
                facing * (FallbackSeatDepth * 0.5f + 0.10f);
            transitionGround.y = entry.RootPosition.y;
            var transition =
                new PlayerAnimatedInteractionPelvisTransition(
                    PlayerCharacterDimensions
                        .GetUprightPelvisPosition(transitionGround),
                    enterArrivalProgress: 0.42f,
                    enterDepartureProgress: 0.54f,
                    exitArrivalProgress: 0.46f,
                    exitDepartureProgress: 0.60f);

            Vector3 fallbackCameraPosition =
                serviceSpace.TransformPoint(servicePlan.CameraPosition);
            Quaternion fallbackCameraRotation =
                serviceSpace.rotation * servicePlan.CameraRotation;
            Pose camera = authoredCameraPose ?? new Pose(
                fallbackCameraPosition,
                fallbackCameraRotation);
            if (authoredCameraLookAt.HasValue)
            {
                Vector3 look = authoredCameraLookAt.Value - camera.position;
                if (look.sqrMagnitude < 0.0001f)
                {
                    throw new ArgumentException(
                        "The counter camera and look-at anchors must differ.",
                        nameof(authoredCameraLookAt));
                }

                camera.rotation = Quaternion.LookRotation(
                    look,
                    Vector3.up);
            }

            var waypoints = new List<Vector3>(1);
            if (authoredApproachGroundPosition.HasValue)
            {
                Vector3 waypoint = authoredApproachGroundPosition.Value;
                RequireFinite(
                    waypoint,
                    nameof(authoredApproachGroundPosition));
                waypoint.y += PlayerFactory.GroundedRootOffset;
                waypoints.Add(waypoint);
            }

            return new CounterSeatPlan(
                servicePlan.BarId + "-counter-seat",
                seat.position,
                entry,
                actionHip,
                exit,
                transition,
                camera.position,
                camera.rotation,
                servicePlan.CameraFieldOfView,
                waypoints);
        }

        public void EvaluateCamera(
            Vector3 livePelvisPosition,
            float yawOffsetDegrees,
            float pitchOffsetDegrees,
            out Vector3 position,
            out Quaternion rotation)
        {
            RequireFinite(livePelvisPosition, nameof(livePelvisPosition));
            position = livePelvisPosition + CameraOffsetFromActionHip;
            rotation = CameraRotation * Quaternion.Euler(
                Mathf.Clamp(
                    pitchOffsetDegrees,
                    CounterSeatView.MinimumPitchDegrees,
                    CounterSeatView.MaximumPitchDegrees),
                Mathf.Clamp(
                    yawOffsetDegrees,
                    -CounterSeatView.MaximumYawOffsetDegrees,
                    CounterSeatView.MaximumYawOffsetDegrees),
                0f);
        }

        private static PlayerAnimatedInteractionPose CreateRootPose(
            Pose groundPose,
            Quaternion fallbackRotation)
        {
            RequireFinite(groundPose.position, nameof(groundPose));
            Quaternion rotation = IsValidRotation(groundPose.rotation)
                ? groundPose.rotation
                : fallbackRotation;
            Vector3 root = groundPose.position +
                Vector3.up * PlayerFactory.GroundedRootOffset;
            return new PlayerAnimatedInteractionPose(
                root,
                rotation,
                PlayerCharacterDimensions.GetUprightPelvisPosition(root));
        }

        private static Pose? ToPose(Transform value)
        {
            return value != null
                ? new Pose(value.position, value.rotation)
                : (Pose?)null;
        }

        private static Pose? ToPose(
            Transform value,
            Quaternion rotation)
        {
            return value != null
                ? new Pose(value.position, rotation)
                : (Pose?)null;
        }

        private static Quaternion ResolveAnchorFacing(
            Transform serviceSpace,
            BarDrinkServicePlan servicePlan,
            Transform seatTopAnchor,
            Transform entryGroundAnchor)
        {
            Vector3 facing = Vector3.zero;
            if (seatTopAnchor != null && entryGroundAnchor != null)
            {
                facing = seatTopAnchor.position -
                         entryGroundAnchor.position;
                facing.y = 0f;
            }

            if (!IsFinite(facing.x) ||
                !IsFinite(facing.y) ||
                !IsFinite(facing.z) ||
                facing.sqrMagnitude < 0.0001f)
            {
                facing = serviceSpace.rotation *
                         servicePlan.SeatPose.Rotation *
                         Vector3.forward;
                facing.y = 0f;
            }

            if (!IsFinite(facing.x) ||
                !IsFinite(facing.y) ||
                !IsFinite(facing.z) ||
                facing.sqrMagnitude < 0.0001f)
            {
                throw new ArgumentException(
                    "The bar seat anchors need a horizontal facing.",
                    nameof(servicePlan));
            }

            return Quaternion.LookRotation(
                facing.normalized,
                Vector3.up);
        }

        private static void RequireFinite(
            Vector3 value,
            string parameterName)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z))
            {
                throw new ArgumentException(
                    "The counter-seat value must be finite.",
                    parameterName);
            }
        }

        private static void RequireRotation(
            Quaternion value,
            string parameterName)
        {
            if (!IsValidRotation(value))
            {
                throw new ArgumentException(
                    "The counter-seat rotation must be finite and non-zero.",
                    parameterName);
            }
        }

        private static bool IsValidRotation(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w) &&
                   value.x * value.x + value.y * value.y +
                   value.z * value.z + value.w * value.w > 0.000001f;
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float inverseMagnitude = 1f / Mathf.Sqrt(
                value.x * value.x + value.y * value.y +
                value.z * value.z + value.w * value.w);
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
