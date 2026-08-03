using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct HomeBedInteractionPlan
    {
        private const float ApproachOffset = 0.48f;
        private const float TriggerDepth = 0.72f;
        private const float TriggerHeight = 1.80f;
        private const float TriggerInset = 0.12f;
        internal const float BedSurfaceClearance = 0.045f;
        internal const float ActionHipFootwardOffset = 0.135f;
        private const float UprightVisualOffset = 0.005f;

        private HomeBedInteractionPlan(
            Rect bedBounds,
            Vector3 interactionPosition,
            Vector3 entryRootPosition,
            Vector3 exitRootPosition,
            Vector3 entryHipPosition,
            Vector3 exitHipPosition,
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
            float accessibleEdge = bounds.xMax;
            float centerZ = bounds.center.y;
            Vector3 entryRoot = new Vector3(
                accessibleEdge + ApproachOffset,
                PlayerFactory.GroundedRootOffset,
                centerZ);
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

            float uprightHipHeight =
                (PlayerAnimatedInteractionController.HipPivotYPixels -
                 PlayerSpriteRig.FeetPivotPixels) /
                PlayerAnimatedInteractionController.PixelsPerUnit +
                UprightVisualOffset;
            Vector3 entryHip =
                entryRoot + (Vector3.up * uprightHipHeight);
            Vector3 exitHip =
                exitRoot + (Vector3.up * uprightHipHeight);
            Vector3 dockFacingDirection = Vector3.left;
            Vector3 headToFootAxis = Vector3.right;
            Vector3 actionHip = new Vector3(
                bounds.center.x +
                (headToFootAxis.x * ActionHipFootwardOffset),
                HomeInteriorWorldBuilder.BedDressingSurfaceHeight +
                BedSurfaceClearance,
                centerZ +
                (headToFootAxis.z * ActionHipFootwardOffset));
            Vector3 triggerSize = new Vector3(
                TriggerDepth,
                TriggerHeight,
                Mathf.Max(0.10f, bounds.height - (TriggerInset * 2f)));
            Vector3 triggerCenter = new Vector3(
                accessibleEdge + (TriggerDepth * 0.5f),
                TriggerHeight * 0.5f,
                centerZ);

            return new HomeBedInteractionPlan(
                bounds,
                entryRoot,
                entryRoot,
                exitRoot,
                entryHip,
                exitHip,
                dockFacingDirection,
                dockFacingDirection,
                actionHip,
                headToFootAxis,
                triggerCenter,
                triggerSize);
        }
    }
}
