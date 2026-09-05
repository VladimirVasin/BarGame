using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct HomeBedInteractionPlan
    {
        private const float ApproachOffset = 0.48f;
        private const float TriggerDepth = 0.72f;
        private const float TriggerHeight = 1.80f;
        private const float TriggerLength = 0.90f;
        internal const float ActionHipFootwardOffset = 0.135f;
        internal const float DoorSideSeatInset = 0.16f;

        // Both hip heights are the mattress plus the measured distance the
        // authored pose hangs below the pelvis bone. Nothing here is a
        // clearance guess: too little and he sinks into the bedding, too much
        // and he sits in mid-air over it. The sleeping hip additionally
        // descends by the mattress dent: the surface gives under him, and he
        // must lie in that dent rather than hover over it. The seated hip
        // does NOT sink — it is pinned by both boots on the floor.
        internal const float SeatedHipHeight =
            HomeInteriorWorldBuilder.BedMattressSurfaceHeight +
            PlayerCharacterDimensions.SeatedPelvisSupportOffset;
        internal const float SleepingHipHeight =
            HomeInteriorWorldBuilder.BedMattressSurfaceHeight +
            PlayerCharacterDimensions.SupinePelvisSupportOffset -
            HomeInteriorWorldBuilder.BedSleeperSinkDepth;

        // The window each clip actually spends sitting on the edge. Waking is
        // the longer one: he is seated from the half-crouch at 0.50 until his
        // weight goes over his feet at 0.88, with the legs leaving the bed one
        // at a time in between. These mirror the BedEnter and BedExit
        // landmarks in tools/player_3d_model_common.py, which publishes them as
        // `bed_contract` in the model manifest so drift fails a test rather
        // than silently sliding the hero across the mattress.
        internal const float EnterSeatArrivalProgress = 0.28f;
        internal const float EnterSeatDepartureProgress = 0.44f;
        internal const float ExitSeatArrivalProgress = 0.50f;
        internal const float ExitSeatDepartureProgress = 0.88f;
        // Let the hands and head prepare while the pelvis keeps its support;
        // on entry it arrives before the final shoulder/head settle.
        internal const float EnterHoldProgress = 0.05f;
        internal const float EnterSettleProgress = 0.96f;
        // Sit up over the sleeping support before moving toward the room;
        // the legs leave the bed only after the seated waypoint is reached.
        internal const float ExitHoldProgress = 0.30f;
        internal const float ExitSettleProgress = 1f;
        public const float UprightVisualOffset = 0.005f;

        private HomeBedInteractionPlan(
            Rect bedBounds,
            Vector3 interactionPosition,
            Vector3 entryRootPosition,
            Vector3 exitRootPosition,
            Vector3 entryHipPosition,
            Vector3 exitHipPosition,
            Vector3 seatHipPosition,
            Vector3 entryFacingDirection,
            Vector3 exitFacingDirection,
            Vector3 actionHipPosition,
            Vector3 headToFootAxis,
            Vector3 triggerCenter,
            Vector3 triggerSize)
        {
            BedBounds = bedBounds;
            InteractionPosition = interactionPosition;
            EntryRootPosition = entryRootPosition;
            ExitRootPosition = exitRootPosition;
            EntryHipPosition = entryHipPosition;
            ExitHipPosition = exitHipPosition;
            SeatHipPosition = seatHipPosition;
            EntryFacingDirection = entryFacingDirection;
            ExitFacingDirection = exitFacingDirection;
            ActionHipPosition = actionHipPosition;
            HeadToFootAxis = headToFootAxis;
            TriggerCenter = triggerCenter;
            TriggerSize = triggerSize;
        }

        public Rect BedBounds { get; }
        public Vector3 InteractionPosition { get; }
        public Vector3 EntryRootPosition { get; }
        public Vector3 ExitRootPosition { get; }
        public Vector3 EntryHipPosition { get; }
        public Vector3 ExitHipPosition { get; }
        public Vector3 SeatHipPosition { get; }
        public Vector3 EntryFacingDirection { get; }
        public Vector3 ExitFacingDirection { get; }
        public Quaternion EntryFacingRotation =>
            Quaternion.LookRotation(
                EntryFacingDirection,
                Vector3.up);
        public Quaternion ExitFacingRotation =>
            Quaternion.LookRotation(
                ExitFacingDirection,
                Vector3.up);
        public Quaternion EntryRotation => EntryFacingRotation;
        public Quaternion ExitRotation => ExitFacingRotation;
        public Vector3 ApproachRootPosition => EntryRootPosition;
        public Vector3 StandHipPosition => EntryHipPosition;
        public Vector3 ActionHipPosition { get; }
        public Vector3 HeadToFootAxis { get; }
        public Vector3 TriggerCenter { get; }
        public Vector3 TriggerSize { get; }
        public PlayerAnimatedInteractionPelvisTransition PelvisTransition =>
            new PlayerAnimatedInteractionPelvisTransition(
                SeatHipPosition,
                EnterSeatArrivalProgress,
                EnterSeatDepartureProgress,
                ExitSeatArrivalProgress,
                ExitSeatDepartureProgress,
                EnterHoldProgress,
                EnterSettleProgress,
                ExitHoldProgress,
                ExitSettleProgress);

        public static HomeBedInteractionPlan Create(
            HomeInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!layout.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed))
            {
                throw new InvalidOperationException(
                    "The home layout must contain exactly one bed.");
            }

            Rect bounds = bed.Bounds;
            float doorSideEdge = bounds.yMin;
            // Sit opposite the sleeping pelvis near the middle of the long
            // side. Sharing this longitudinal coordinate lets the torso
            // reach the pillow by reclining, without sliding along the bed.
            float dockX = bounds.center.x + ActionHipFootwardOffset;
            float centerZ = bounds.center.y;
            Vector3 entryRoot = new Vector3(
                dockX,
                PlayerFactory.GroundedRootOffset,
                doorSideEdge - ApproachOffset);
            Vector3 exitRoot = entryRoot;
            var walkable = new RoadWalkableArea(
                new[] { layout.WalkableBounds });
            if (!walkable.Contains(
                    entryRoot,
                    HomeInteriorLayoutValidator.PlayerClearanceRadius) ||
                !walkable.Contains(
                    exitRoot,
                    HomeInteriorLayoutValidator.PlayerClearanceRadius))
            {
                throw new InvalidOperationException(
                    "The bed entry and exit docks must preserve player " +
                    "clearance inside the walkable room.");
            }

            Vector3 entryHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    entryRoot,
                    UprightVisualOffset);
            Vector3 exitHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    exitRoot,
                    UprightVisualOffset);
            Vector3 seatHip = new Vector3(
                dockX,
                SeatedHipHeight,
                doorSideEdge + DoorSideSeatInset);
            // The dock leaves the character's back toward the mattress. The
            // authored clip can therefore sit straight down on the long edge
            // nearest the apartment door and finish standing into the room.
            Vector3 dockFacingDirection = Vector3.back;
            Vector3 headToFootAxis = Vector3.right;
            Vector3 actionHip = new Vector3(
                bounds.center.x +
                (headToFootAxis.x * ActionHipFootwardOffset),
                SleepingHipHeight,
                centerZ +
                (headToFootAxis.z * ActionHipFootwardOffset));
            Vector3 triggerSize = new Vector3(
                Mathf.Min(TriggerLength, bounds.width),
                TriggerHeight,
                TriggerDepth);
            Vector3 triggerCenter = new Vector3(
                dockX,
                TriggerHeight * 0.5f,
                doorSideEdge - (TriggerDepth * 0.5f));

            return new HomeBedInteractionPlan(
                bounds,
                entryRoot,
                entryRoot,
                exitRoot,
                entryHip,
                exitHip,
                seatHip,
                dockFacingDirection,
                dockFacingDirection,
                actionHip,
                headToFootAxis,
                triggerCenter,
                triggerSize);
        }
    }
}
