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
            Vector3 playerRootLocalPosition,
            Vector3 standHipLocalPosition,
            Vector3 actionHipLocalPosition,
            Vector3 facingLocalDirection)
        {
            PlayerRootLocalPosition = playerRootLocalPosition;
            StandHipLocalPosition = standHipLocalPosition;
            ActionHipLocalPosition = actionHipLocalPosition;
            FacingLocalDirection = facingLocalDirection;
        }

        public Vector3 PlayerRootLocalPosition { get; }
        public Vector3 StandHipLocalPosition { get; }
        public Vector3 ActionHipLocalPosition { get; }
        public Vector3 FacingLocalDirection { get; }
        public Quaternion FacingLocalRotation =>
            Quaternion.LookRotation(
                FacingLocalDirection,
                Vector3.up);

        public static StairwellCatFeedingPlan Create(
            StairwellLayoutPlan stairwell,
            StairwellCatPlan cat)
        {
            if (stairwell == null)
            {
                throw new ArgumentNullException(nameof(stairwell));
            }

            Vector3 playerRoot = cat.InteractionLocalPosition;
            var walkable = new RoadWalkableArea(
                stairwell.WalkableRectangles);
            if (!walkable.Contains(
                    playerRoot,
                    StairwellLayoutValidator.PlayerRadius))
            {
                throw new InvalidOperationException(
                    "The cat feeding dock must preserve player " +
                    "clearance on the middle landing.");
            }

            var shotSelector =
                new StairwellCameraShotSelector(
                    StairwellFixedCameraController
                        .CreateDefaultShots(stairwell));
            if (shotSelector.Select(playerRoot).Kind !=
                RequiredCameraShotKind)
            {
                throw new InvalidOperationException(
                    "The cat feeding dock must select the middle " +
                    "stairwell camera shot.");
            }

            Vector3 facing =
                cat.VisualLocalPosition - playerRoot;
            facing.y = 0f;
            if (facing.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "The cat feeding dock must face the cat.");
            }

            facing.Normalize();
            float hipHeight =
                (PlayerAnimatedInteractionController
                    .HipPivotYPixels -
                 PlayerSpriteRig.FeetPivotPixels) /
                PlayerAnimatedInteractionController
                    .PixelsPerUnit +
                UprightVisualOffset;
            Vector3 actionHip =
                playerRoot + (Vector3.up * hipHeight);
            return new StairwellCatFeedingPlan(
                playerRoot,
                actionHip,
                actionHip,
                facing);
        }
    }
}
