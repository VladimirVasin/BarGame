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
        internal const float BedSurfaceClearance = 0.045f;
        internal const float ActionHipFootwardOffset = 0.135f;
        internal const float DoorSideDockFootInset = 0.45f;
        internal const float DoorSideSeatInset = 0.07f;
        internal const float SeatedHipHeight =
            HomeInteriorWorldBuilder.BedDressingSurfaceHeight - 0.015f;
        internal const float EnterSeatArrivalProgress = 0.28f;
        internal const float EnterSeatDepartureProgress = 0.38f;
        internal const float ExitSeatArrivalProgress = 0.63f;
        internal const float ExitSeatDepartureProgress = 0.78f;
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
                ExitSeatDepartureProgress);

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
            float dockX = bounds.xMax - DoorSideDockFootInset;
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
                HomeInteriorWorldBuilder.BedDressingSurfaceHeight +
                BedSurfaceClearance,
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
