using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stairwell-local staging for the paired player/cat feeding sequence.
    /// Docking at this point keeps the player inside the middle landing and
    /// therefore under its existing fixed-camera shot.
    /// </summary>
    public readonly struct StairwellCatFeedingPlan
    {
        public const float UprightVisualOffset = 0.005f;
        public const StairwellCameraShotKind RequiredCameraShotKind =
            StairwellCameraShotKind.MiddleFlight;

        private StairwellCatFeedingPlan(
            Vector3 entryRootLocalPosition,
            Vector3 exitRootLocalPosition,
            Vector3 entryHipLocalPosition,
            Vector3 exitHipLocalPosition,
            Vector3 actionHipLocalPosition,
            Vector3 entryFacingLocalDirection,
            Vector3 exitFacingLocalDirection)
        {
            EntryRootLocalPosition = entryRootLocalPosition;
            ExitRootLocalPosition = exitRootLocalPosition;
            EntryHipLocalPosition = entryHipLocalPosition;
            ExitHipLocalPosition = exitHipLocalPosition;
            ActionHipLocalPosition = actionHipLocalPosition;
            EntryFacingLocalDirection = entryFacingLocalDirection;
            ExitFacingLocalDirection = exitFacingLocalDirection;
        }

        public Vector3 EntryRootLocalPosition { get; }
        public Vector3 ExitRootLocalPosition { get; }
        public Vector3 EntryHipLocalPosition { get; }
        public Vector3 ExitHipLocalPosition { get; }
        public Vector3 ActionHipLocalPosition { get; }
        public Vector3 EntryFacingLocalDirection { get; }
        public Vector3 ExitFacingLocalDirection { get; }
        public Quaternion EntryFacingLocalRotation =>
            Quaternion.LookRotation(
                EntryFacingLocalDirection,
                Vector3.up);
        public Quaternion ExitFacingLocalRotation =>
            Quaternion.LookRotation(
                ExitFacingLocalDirection,
                Vector3.up);
        public Quaternion EntryLocalRotation =>
            EntryFacingLocalRotation;
        public Quaternion ExitLocalRotation =>
            ExitFacingLocalRotation;
        public Vector3 PlayerRootLocalPosition => EntryRootLocalPosition;
        public Vector3 StandHipLocalPosition => EntryHipLocalPosition;
        public Vector3 FacingLocalDirection =>
            EntryFacingLocalDirection;
        public Quaternion FacingLocalRotation =>
            EntryFacingLocalRotation;

        public static StairwellCatFeedingPlan Create(
            StairwellLayoutPlan stairwell,
            StairwellCatPlan cat)
        {
            if (stairwell == null)
            {
                throw new ArgumentNullException(nameof(stairwell));
            }

            Vector3 entryRoot = cat.InteractionLocalPosition;
            Vector3 exitRoot = cat.InteractionLocalPosition;
            var walkable = new RoadWalkableArea(
                stairwell.WalkableRectangles);
            if (!walkable.Contains(
                    entryRoot,
                    StairwellLayoutValidator.PlayerRadius) ||
                !walkable.Contains(
                    exitRoot,
                    StairwellLayoutValidator.PlayerRadius))
            {
                throw new InvalidOperationException(
                    "The cat feeding entry and exit docks must preserve " +
                    "player " +
                    "clearance on the middle landing.");
            }

            var shotSelector =
                new StairwellCameraShotSelector(
                    StairwellFixedCameraController
                        .CreateDefaultShots(stairwell));
            if (shotSelector.Select(entryRoot).Kind !=
                    RequiredCameraShotKind ||
                shotSelector.Select(exitRoot).Kind !=
                    RequiredCameraShotKind)
            {
                throw new InvalidOperationException(
                    "The cat feeding entry and exit docks must select " +
                    "the middle " +
                    "stairwell camera shot.");
            }

            Vector3 entryFacing = CreateFacingDirection(
                entryRoot,
                cat.VisualLocalPosition);
            Vector3 exitFacing = CreateFacingDirection(
                exitRoot,
                cat.VisualLocalPosition);
            Vector3 entryHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    entryRoot,
                    UprightVisualOffset);
            Vector3 exitHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    exitRoot,
                    UprightVisualOffset);
            Vector3 actionHip = entryHip;
            return new StairwellCatFeedingPlan(
                entryRoot,
                exitRoot,
                entryHip,
                exitHip,
                actionHip,
                entryFacing,
                exitFacing);
        }

        private static Vector3 CreateFacingDirection(
            Vector3 rootPosition,
            Vector3 targetPosition)
        {
            Vector3 facing = targetPosition - rootPosition;
            facing.y = 0f;
            if (facing.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "The cat feeding dock must face the cat.");
            }

            return facing.normalized;
        }
    }
}
