using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Exact source-scene handoff poses for a door action. Entry and exit are
    /// stored independently even while today's doors return to the same dock.
    /// </summary>
    public readonly struct PlayerDoorActionPlan
    {
        // CharacterController radius (0.32 m) plus a small authored margin.
        public const float DockBoundaryClearance = 0.40f;

        public PlayerDoorActionPlan(
            Vector3 interactionPosition,
            Vector3 entryRootPosition,
            Vector3 entryFacingDirection,
            Vector3 entryHipPosition,
            Vector3 actionHipPosition,
            Vector3 exitRootPosition,
            Vector3 exitFacingDirection,
            Vector3 exitHipPosition)
        {
            InteractionPosition = interactionPosition;
            EntryRootPosition = entryRootPosition;
            EntryFacingDirection = NormalizePlanarDirection(
                entryFacingDirection,
                nameof(entryFacingDirection));
            EntryHipPosition = entryHipPosition;
            ActionHipPosition = actionHipPosition;
            ExitRootPosition = exitRootPosition;
            ExitFacingDirection = NormalizePlanarDirection(
                exitFacingDirection,
                nameof(exitFacingDirection));
            ExitHipPosition = exitHipPosition;
            Validate(nameof(interactionPosition));
        }

        public Vector3 InteractionPosition { get; }
        public Vector3 EntryRootPosition { get; }
        public Vector3 EntryFacingDirection { get; }
        public Vector3 EntryHipPosition { get; }
        public Vector3 ActionHipPosition { get; }
        public Vector3 ExitRootPosition { get; }
        public Vector3 ExitFacingDirection { get; }
        public Vector3 ExitHipPosition { get; }
        public Quaternion EntryRotation =>
            Quaternion.LookRotation(EntryFacingDirection, Vector3.up);
        public Quaternion ExitRotation =>
            Quaternion.LookRotation(ExitFacingDirection, Vector3.up);
        public PlayerAnimatedInteractionPose EntryPose =>
            new PlayerAnimatedInteractionPose(
                EntryRootPosition,
                EntryRotation,
                EntryHipPosition);
        public PlayerAnimatedInteractionPose ExitPose =>
            new PlayerAnimatedInteractionPose(
                ExitRootPosition,
                ExitRotation,
                ExitHipPosition);

        public static PlayerDoorActionPlan CreateStationary(
            Vector3 interactionPosition,
            Vector3 groundedDockRoot,
            Vector3 doorwardFacingDirection)
        {
            Vector3 uprightHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    groundedDockRoot);
            return new PlayerDoorActionPlan(
                interactionPosition,
                groundedDockRoot,
                doorwardFacingDirection,
                uprightHip,
                uprightHip,
                groundedDockRoot,
                doorwardFacingDirection,
                uprightHip);
        }

        internal void Validate(string parameterName)
        {
            if (!IsFinite(InteractionPosition) ||
                !IsFinite(EntryRootPosition) ||
                !IsFinite(EntryHipPosition) ||
                !IsFinite(ActionHipPosition) ||
                !IsFinite(ExitRootPosition) ||
                !IsFinite(ExitHipPosition) ||
                !IsPlanarUnitDirection(EntryFacingDirection) ||
                !IsPlanarUnitDirection(ExitFacingDirection))
            {
                throw new ArgumentException(
                    "A door action requires finite independent entry, " +
                    "action and exit poses with planar facing directions.",
                    parameterName);
            }
        }

        private static Vector3 NormalizePlanarDirection(
            Vector3 direction,
            string parameterName)
        {
            if (!IsFinite(direction))
            {
                throw new ArgumentException(
                    "The door-facing direction must be finite.",
                    parameterName);
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                throw new ArgumentException(
                    "The door-facing direction must have a planar axis.",
                    parameterName);
            }

            return direction.normalized;
        }

        private static bool IsPlanarUnitDirection(Vector3 direction)
        {
            return IsFinite(direction) &&
                   Mathf.Abs(direction.y) <= 0.0001f &&
                   Mathf.Abs(direction.sqrMagnitude - 1f) <= 0.0001f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Destination-owned pose used after crossing out through a building
    /// door. The exterior door action faces toward its leaf, so the arrival
    /// faces along the exact opposite axis and leaves the leaf behind the
    /// player. Applying this before <see cref="PlayerCameraFollow.Initialize"/>
    /// also seeds the chase camera behind the player's shoulder.
    /// </summary>
    public readonly struct PlayerDoorArrivalPose
    {
        private PlayerDoorArrivalPose(
            Vector3 rootPosition,
            Vector3 forward)
        {
            RootPosition = rootPosition;
            Forward = forward;
        }

        public Vector3 RootPosition { get; }
        public Vector3 Forward { get; }
        public Quaternion Rotation =>
            Quaternion.LookRotation(Forward, Vector3.up);

        public static PlayerDoorArrivalPose FromDestinationDoor(
            Vector3 returnRootPosition,
            PlayerDoorActionPlan destinationDoorPlan)
        {
            destinationDoorPlan.Validate(nameof(destinationDoorPlan));
            if (!IsFinite(returnRootPosition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(returnRootPosition),
                    "A door arrival requires a finite return position.");
            }

            return new PlayerDoorArrivalPose(
                returnRootPosition,
                -destinationDoorPlan.EntryFacingDirection);
        }

        public static PlayerDoorArrivalPose FromDestinationDoor(
            Vector3 returnRootPosition,
            PlayerDoorActionTarget destinationDoor)
        {
            if (destinationDoor == null)
            {
                throw new ArgumentNullException(nameof(destinationDoor));
            }

            if (!destinationDoor.IsConfigured)
            {
                throw new InvalidOperationException(
                    "The destination door must be configured before " +
                    "resolving an arrival pose.");
            }

            return FromDestinationDoor(
                returnRootPosition,
                destinationDoor.Plan);
        }

        public void ApplyTo(Transform playerRoot)
        {
            if (playerRoot == null)
            {
                throw new ArgumentNullException(nameof(playerRoot));
            }

            playerRoot.SetPositionAndRotation(
                RootPosition,
                Rotation);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }
    }
}
