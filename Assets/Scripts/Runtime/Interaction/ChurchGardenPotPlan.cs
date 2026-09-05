using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured two-hand action, two grounded docks and a clear stance.</summary>
    public sealed class ChurchGardenPotPlan
    {
        public const float DockSideOffset = 0.34f;
        public const float DockHeight = 0.65f;
        public const float DockForwardOffset = 0.56f;
        public const float LedgeForwardOffset = 0.62f;
        public const float GripHeight = 0.255f;
        public const float GripRadius = 0.165f;
        public const float ContactProgress = 0.5f;
        public const float ApproachVerticalTolerance = 0.35f;

        public ChurchGardenPotPlan(
            string sessionKey,
            Vector3 standingGroundPosition,
            Quaternion facing)
        {
            if (string.IsNullOrEmpty(sessionKey))
            {
                throw new ArgumentException("A garden pot needs a stable session key.", nameof(sessionKey));
            }

            SessionKey = sessionKey;
            StandingGroundPosition = standingGroundPosition;
            Vector3 root = standingGroundPosition + Vector3.up * PlayerFactory.GroundedRootOffset;
            Vector3 hip = PlayerCharacterDimensions.GetUprightPelvisPosition(root);
            // Independent values retain the full entry/action/exit contract.
            EntryPose = new PlayerAnimatedInteractionPose(root, facing, hip);
            ExitPose = new PlayerAnimatedInteractionPose(root, facing, hip);
            ActionHipPosition = hip;
            Facing = EntryPose.RootRotation;
            if (Vector3.Dot(Facing * Vector3.up, Vector3.up) < 0.999f)
            {
                throw new ArgumentException("Garden pot docks require a level facing.", nameof(facing));
            }
        }

        public string SessionKey { get; }
        public Vector3 StandingGroundPosition { get; }
        public Quaternion Facing { get; }
        public PlayerAnimatedInteractionPose EntryPose { get; }
        public PlayerAnimatedInteractionPose ExitPose { get; }
        public Vector3 ActionHipPosition { get; }
        public Vector3 PottingLedgePosition => StandingGroundPosition + Facing * Vector3.forward * LedgeForwardOffset;

        public Vector3 GetDockPosition(int index)
        {
            ValidateDockIndex(index);
            return StandingGroundPosition + Facing * new Vector3(
                index == 0 ? -DockSideOffset : DockSideOffset,
                DockHeight,
                DockForwardOffset);
        }

        public static void ValidateDockIndex(int index)
        {
            if (index < 0 || index > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static PlayerAnimatedInteractionDefinition CreateDefinition(int sourceDock)
        {
            ValidateDockIndex(sourceDock);
            return new PlayerAnimatedInteractionDefinition(
                sourceDock == 0 ? "ChurchPotPickupLeft" : "ChurchPotPickupRight",
                "ChurchPotInspectLoop",
                sourceDock == 0 ? "ChurchPotPlaceLeft" : "ChurchPotPlaceRight",
                enterFrameCount: 36,
                enterFramesPerSecond: 12f,
                loopFrameCount: 40,
                loopFramesPerSecond: 8f,
                exitFrameCount: 36,
                exitFramesPerSecond: 12f);
        }
    }
}
