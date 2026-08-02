using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stable staging data for the balcony smoking interaction. Positions are
    /// expressed in Home-local space and are derived from the generated
    /// balcony rather than from a serialized scene object.
    /// </summary>
    public sealed class HomeBalconySmokingPlan
    {
        public const string AtlasResourcePath =
            "Player/PlayerBalconySmokingAtlas";
        public const int EnterFrameCount = 24;
        public const float EnterFramesPerSecond = 6f;
        public const int LoopFrameCount = 24;
        public const float LoopFramesPerSecond = 6f;
        public const int ExitFrameCount = 16;
        public const float ExitFramesPerSecond = 8f;
        public const float VisualCrossfadeDurationSeconds = 0.35f;

        public const int RestHoldLoopFrame = 3;
        public const float RestHoldSeconds = 2f;
        public const int InhaleHoldLoopFrame = 11;
        public const float InhaleHoldSeconds = 0.65f;
        public const int BreathHoldLoopFrame = 14;
        public const float BreathHoldSeconds = 0.55f;
        public const int ExhaleHoldLoopFrame = 23;
        public const float ExhaleHoldSeconds = 2.30f;

        public const float TriggerWidth = 0.70f;
        public const float TriggerHeight = 1.80f;
        public const float TriggerDepth = 1.20f;
        public const float DockRailInset = 0.70f;
        public const float TriggerRearOffset = 0.10f;
        public const float InteractionHeightTolerance = 0.36f;
        public const float UprightVisualOffset = 0.005f;

        public const float CameraFieldOfView = 38f;
        public const float CameraCityLookOffset = 0.33f;
        public static readonly Vector3 CameraPosition =
            new Vector3(6.55f, 2.25f, -3.08f);
        public static readonly Vector3 FacingDirection =
            Vector3.right;

        private HomeBalconySmokingPlan(
            Vector3 dockRootPosition,
            Vector3 actionHipPosition,
            Vector3 triggerCenter,
            Vector3 triggerSize,
            Vector3 cameraLookAt)
        {
            DockRootPosition = dockRootPosition;
            StandHipPosition = actionHipPosition;
            ActionHipPosition = actionHipPosition;
            TriggerCenter = triggerCenter;
            TriggerSize = triggerSize;
            CameraLookAt = cameraLookAt;
            InteractionBounds = new Rect(
                triggerCenter.x - triggerSize.x * 0.5f,
                triggerCenter.z - triggerSize.z * 0.5f,
                triggerSize.x,
                triggerSize.z);
        }

        public Vector3 DockRootPosition { get; }
        public Vector3 StandHipPosition { get; }
        public Vector3 ActionHipPosition { get; }
        public Vector3 TriggerCenter { get; }
        public Vector3 TriggerSize { get; }
        public Rect InteractionBounds { get; }
        public Vector3 CameraLookAt { get; }
        public Quaternion FacingRotation =>
            Quaternion.LookRotation(FacingDirection, Vector3.up);

        public static HomeBalconySmokingPlan Create(
            HomeInteriorLayoutPlan interior,
            HomeBalconyLayoutPlan balcony)
        {
            if (interior == null)
            {
                throw new ArgumentNullException(nameof(interior));
            }

            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            Rect bounds = balcony.BalconyBounds;
            Vector3 dockRoot = new Vector3(
                bounds.xMax - DockRailInset,
                interior.PlayerSpawn.y,
                bounds.center.y);
            var walkable = new RoadWalkableArea(
                balcony.WalkableRectangles);
            if (!walkable.Contains(
                    dockRoot,
                    HomeInteriorLayoutValidator.PlayerClearanceRadius))
            {
                throw new InvalidOperationException(
                    "The balcony smoking dock must preserve player " +
                    "clearance inside the walkable balcony.");
            }

            float hipHeight =
                (PlayerAnimatedInteractionController.HipPivotYPixels -
                 PlayerSpriteRig.FeetPivotPixels) /
                PlayerAnimatedInteractionController.PixelsPerUnit +
                UprightVisualOffset;
            Vector3 actionHip =
                dockRoot + Vector3.up * hipHeight;
            Vector3 triggerSize = new Vector3(
                TriggerWidth,
                TriggerHeight,
                TriggerDepth);
            Vector3 triggerCenter = new Vector3(
                dockRoot.x - TriggerRearOffset,
                TriggerHeight * 0.5f,
                dockRoot.z);
            Vector3 cameraLookAt =
                actionHip +
                new Vector3(CameraCityLookOffset, 0.50f, 0f);

            return new HomeBalconySmokingPlan(
                dockRoot,
                actionHip,
                triggerCenter,
                triggerSize,
                cameraLookAt);
        }

        public bool CanInteractAt(Vector3 localRootPosition)
        {
            return IsFinite(localRootPosition) &&
                   Mathf.Abs(
                       localRootPosition.y - DockRootPosition.y) <=
                   InteractionHeightTolerance &&
                   localRootPosition.x >= InteractionBounds.xMin &&
                   localRootPosition.x <= InteractionBounds.xMax &&
                   localRootPosition.z >= InteractionBounds.yMin &&
                   localRootPosition.z <= InteractionBounds.yMax;
        }

        public PlayerAnimatedInteractionDefinition
            CreateAnimationDefinition()
        {
            return new PlayerAnimatedInteractionDefinition(
                AtlasResourcePath,
                EnterFrameCount,
                EnterFramesPerSecond,
                LoopFrameCount,
                LoopFramesPerSecond,
                ExitFrameCount,
                ExitFramesPerSecond,
                renderAboveSceneDepth: false,
                loopFrameExtraHoldSeconds:
                    CreateLoopFrameHolds(),
                textureFlipX: false,
                visualCrossfadeDurationSeconds:
                    VisualCrossfadeDurationSeconds,
                alignBillboardToCameraPlane: false);
        }

        private static float[] CreateLoopFrameHolds()
        {
            var holds = new float[LoopFrameCount];
            holds[RestHoldLoopFrame] = RestHoldSeconds;
            holds[InhaleHoldLoopFrame] = InhaleHoldSeconds;
            holds[BreathHoldLoopFrame] = BreathHoldSeconds;
            holds[ExhaleHoldLoopFrame] = ExhaleHoldSeconds;
            return holds;
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
}
