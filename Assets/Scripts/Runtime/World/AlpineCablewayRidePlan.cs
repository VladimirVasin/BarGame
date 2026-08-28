using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the hero stands to board a cabin, and where the prompt lives.
    ///
    /// Every point here is WORLD space and every one of them is solved
    /// against a station, not against a cabin. That split is deliberate: a
    /// station does not move, so this can be built once per scene, while the
    /// seat itself is read from the cabin's own anchor every frame because the
    /// cabin does move. Solving the seat here instead is exactly the mistake
    /// that sealed a passenger inside the Ferryman's car six hundred metres
    /// from where the plan was made.
    /// </summary>
    public readonly struct AlpineCablewayCabinSeatPlan
    {
        /// <summary>
        /// Looser than the interior default, like the car's. The hero stands
        /// on a raised boarding strip and the prompt has to survive the step
        /// up onto it.
        /// </summary>
        public const float ApproachVerticalTolerance = 0.3f;

        public const float TriggerHeight = 1.9f;

        private AlpineCablewayCabinSeatPlan(
            Vector3 entryRootPosition,
            Quaternion entryRotation,
            Vector3 entryHipPosition,
            Vector3 doorwayWaypoint,
            Vector3 triggerCenter,
            Quaternion triggerRotation,
            Vector3 triggerSize,
            float boardingLoopDistance)
        {
            IsPresent = true;
            EntryRootPosition = entryRootPosition;
            EntryRotation = entryRotation;
            EntryHipPosition = entryHipPosition;
            DoorwayWaypoint = doorwayWaypoint;
            TriggerCenter = triggerCenter;
            TriggerRotation = triggerRotation;
            TriggerSize = triggerSize;
            BoardingLoopDistance = boardingLoopDistance;
        }

        public bool IsPresent { get; }
        public Vector3 EntryRootPosition { get; }
        public Quaternion EntryRotation { get; }
        public Vector3 EntryHipPosition { get; }

        /// <summary>
        /// The pelvis passes through the doorway rather than cutting the
        /// corner of the shell on its way to the bench.
        /// </summary>
        public Vector3 DoorwayWaypoint { get; }

        public Vector3 TriggerCenter { get; }
        public Quaternion TriggerRotation { get; }
        public Vector3 TriggerSize { get; }

        /// <summary>Loop distance the line is asked to dock a cabin at.
        /// </summary>
        public float BoardingLoopDistance { get; }

        public Vector3 InteractionPosition => EntryRootPosition;

        public PlayerAnimatedInteractionPose EntryPose =>
            new PlayerAnimatedInteractionPose(
                EntryRootPosition,
                EntryRotation,
                EntryHipPosition);

        /// <summary>
        /// The doorway is an open aperture, so the pelvis needs no hold while
        /// a leaf swings: the stock `0`/`1` markers are correct here, and
        /// giving it a pause it does not need is how the car ended up walking
        /// its hero through a shut door.
        /// </summary>
        public PlayerAnimatedInteractionPelvisTransition PelvisTransition =>
            new PlayerAnimatedInteractionPelvisTransition(
                DoorwayWaypoint,
                enterArrivalProgress: 0.46f,
                enterDepartureProgress: 0.62f,
                exitArrivalProgress: 0.34f,
                exitDepartureProgress: 0.54f);

        public static AlpineCablewayCabinSeatPlan Create(
            MountainRoadCablewayPlan cableway)
        {
            if (cableway == null)
            {
                return default;
            }

            Vector3 dock = cableway.BoardingDockPosition;
            Vector3 facing = cableway.BoardingFacing;
            var rotation = Quaternion.LookRotation(facing, Vector3.up);

            Vector3 root = dock;
            root.y = cableway.BoardingPlatformTopY +
                     PlayerFactory.GroundedRootOffset;

            Vector3 doorway = cableway.BoardingCabinFloorCenter -
                              facing * (cableway.CabinSize.x * 0.5f);
            doorway.y = cableway.CabinFloorY +
                        PlayerCharacterDimensions.PelvisHeight;

            Vector3 triggerCenter = Vector3.Lerp(root, doorway, 0.35f);
            triggerCenter.y = root.y + TriggerHeight * 0.5f;
            return new AlpineCablewayCabinSeatPlan(
                root,
                rotation,
                PlayerCharacterDimensions.GetUprightPelvisPosition(root),
                doorway,
                triggerCenter,
                rotation,
                new Vector3(2.2f, TriggerHeight, 2.6f),
                cableway.BoardingLoopDistance);
        }
    }

    /// <summary>
    /// The first-person eye inside the cabin.
    ///
    /// The car's arrangement rather than the bus's: a `1.75 m` box is a
    /// telephone booth, and a lens behind the passenger's shoulder in one is
    /// just the back of his own head. The axes are held WORLD-level while the
    /// position rides the swaying cabin - axes taken off a rocking body couple
    /// mouse yaw into pitch and tilt the horizon, which the bus already
    /// learned once.
    /// </summary>
    public static class AlpineCablewayCabinViewPlan
    {
        public const float EyeHeightAboveSeat = 0.76f;
        public const float EyeForwardMeters = 0.14f;
        public const float FieldOfView = 64f;

        /// <summary>
        /// Wide enough to look back down the valley the cabin is leaving and
        /// then round to the slope it is climbing, which is the whole reason
        /// the ride is played rather than faded.
        /// </summary>
        public const float MaximumYawOffsetDegrees = 120f;

        public const float MinimumPitchDegrees = -38f;
        public const float MaximumPitchDegrees = 40f;

        public static void EvaluateCamera(
            Vector3 seatAnchor,
            Vector3 facing,
            float yawOffsetDegrees,
            float pitchDegrees,
            out Vector3 position,
            out Quaternion rotation)
        {
            Vector3 level = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (level.sqrMagnitude < 0.0001f)
            {
                level = Vector3.forward;
            }

            level.Normalize();
            position = seatAnchor +
                       Vector3.up * EyeHeightAboveSeat +
                       level * EyeForwardMeters;
            float yaw = Mathf.Clamp(
                yawOffsetDegrees,
                -MaximumYawOffsetDegrees,
                MaximumYawOffsetDegrees);
            float pitch = Mathf.Clamp(
                pitchDegrees,
                MinimumPitchDegrees,
                MaximumPitchDegrees);
            rotation = Quaternion.LookRotation(level, Vector3.up) *
                       Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
