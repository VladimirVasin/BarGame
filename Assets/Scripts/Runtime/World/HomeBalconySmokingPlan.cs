using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One Home-local sample of the slow shoulder-to-profile camera orbit.
    /// </summary>
    public readonly struct HomeBalconySmokingCameraPose
    {
        public HomeBalconySmokingCameraPose(Vector3 position, Vector3 lookAt, float orbitFraction)
        {
            Position = position;
            LookAt = lookAt;
            Rotation = Quaternion.LookRotation(lookAt - position, Vector3.up);
            OrbitFraction = orbitFraction;
        }

        public Vector3 Position { get; }
        public Vector3 LookAt { get; }
        public Quaternion Rotation { get; }
        public float OrbitFraction { get; }
    }

    /// <summary>Balcony-derived grounded action endpoints and city-facing camera staging.</summary>
    public sealed class HomeBalconySmokingPlan
    {
        public const int EnterFrameCount = 24;
        public const float EnterFramesPerSecond = 6f;
        public const int LoopFrameCount = 24;
        public const float LoopFramesPerSecond = 6f;
        public const int ExitFrameCount = 16;
        public const float ExitFramesPerSecond = 8f;

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
        public const float AshtrayFlickOffsetZ = -0.22f;
        public const float InteractionHeightTolerance =
            PlayerMotor.InteractionVerticalTolerance;
        public const float UprightVisualOffset = 0.005f;

        public const float CameraFieldOfView = 48f;
        public const float CameraCityLookOffset = 1.05f;
        public const float CameraHeight = 1.64f;
        public const float CameraLookHeight = 1.60f;
        public const float CameraRadius = 1.16f;
        public const float CameraFaceRadius = 1f;
        public const float CameraFaceHeight = 1.70f;
        public const float CameraFaceLookOffset = .20f;
        public const float CameraShoulderAngle = 37f;
        public const float CameraFaceAngle = 125f;
        // Four smoke loops: the near-profile hold meets the calm exhale,
        // rather than allowing the raised smoking hand to cover his face.
        public const double CameraOrbitPeriodSeconds = 38d;
        public static readonly Vector3 FacingDirection =
            Vector3.right;

        private HomeBalconySmokingPlan(
            Vector3 entryRootPosition,
            Vector3 exitRootPosition,
            Vector3 entryHipPosition,
            Vector3 exitHipPosition,
            Vector3 entryFacingDirection,
            Vector3 exitFacingDirection,
            Vector3 actionHipPosition,
            Vector3 triggerCenter,
            Vector3 triggerSize,
            Vector3 ashtrayPosition,
            Vector3 cameraLookAt)
        {
            EntryRootPosition = entryRootPosition;
            ExitRootPosition = exitRootPosition;
            EntryHipPosition = entryHipPosition;
            ExitHipPosition = exitHipPosition;
            EntryFacingDirection = entryFacingDirection;
            ExitFacingDirection = exitFacingDirection;
            ActionHipPosition = actionHipPosition;
            TriggerCenter = triggerCenter;
            TriggerSize = triggerSize;
            AshtrayPosition = ashtrayPosition;
            CameraLookAt = cameraLookAt;
            InteractionBounds = new Rect(
                triggerCenter.x - triggerSize.x * 0.5f,
                triggerCenter.z - triggerSize.z * 0.5f,
                triggerSize.x,
                triggerSize.z);
        }

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
        public Vector3 DockRootPosition => EntryRootPosition;
        public Vector3 StandHipPosition => EntryHipPosition;
        public Vector3 ActionHipPosition { get; }
        public Vector3 TriggerCenter { get; }
        public Vector3 TriggerSize { get; }
        public Vector3 AshtrayPosition { get; }
        public Rect InteractionBounds { get; }
        public Vector3 CameraLookAt { get; }
        public Quaternion FacingRotation => EntryFacingRotation;

        public HomeBalconySmokingCameraPose EvaluateCamera(double presentationElapsedSeconds)
        {
            if (double.IsNaN(presentationElapsedSeconds) ||
                double.IsInfinity(presentationElapsedSeconds) || presentationElapsedSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(presentationElapsedSeconds));

            double elapsed = Math.Max(0d,
                presentationElapsedSeconds - HomeBalconySmokingTimeline.EnterDurationSeconds);
            double phase = elapsed % CameraOrbitPeriodSeconds;
            float orbit = phase < 6d || phase >= 32d ? 0f :
                phase < 18d ? SmoothOrbit((float)((phase - 6d) / 12d)) :
                phase < 20d ? 1f : 1f - SmoothOrbit((float)((phase - 20d) / 12d));
            float angle = Mathf.Lerp(CameraShoulderAngle, CameraFaceAngle, orbit) * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(CameraRadius, CameraFaceRadius, orbit);
            Vector3 position = EntryRootPosition + new Vector3(
                -Mathf.Cos(angle) * radius, Mathf.Lerp(CameraHeight, CameraFaceHeight, orbit),
                -Mathf.Sin(angle) * radius);
            Vector3 lookAt = EntryRootPosition + new Vector3(
                Mathf.Lerp(CameraCityLookOffset, CameraFaceLookOffset, orbit), CameraLookHeight, 0f);
            return new HomeBalconySmokingCameraPose(position, lookAt, orbit);
        }

        private static float SmoothOrbit(float value) => value * value * (3f - 2f * value);

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
            Vector3 entryRoot = new Vector3(
                bounds.xMax - DockRailInset,
                PlayerFactory.GroundedRootOffset,
                bounds.center.y);
            Vector3 exitRoot = entryRoot;
            var walkable = new RoadWalkableArea(
                balcony.WalkableRectangles);
            if (!walkable.Contains(
                    entryRoot,
                    HomeInteriorLayoutValidator.PlayerClearanceRadius) ||
                !walkable.Contains(
                    exitRoot,
                    HomeInteriorLayoutValidator.PlayerClearanceRadius))
            {
                throw new InvalidOperationException(
                    "The balcony smoking entry and exit docks must " +
                    "preserve player clearance inside the walkable " +
                    "balcony.");
            }

            Vector3 entryHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    entryRoot,
                    UprightVisualOffset);
            Vector3 exitHip =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    exitRoot,
                    UprightVisualOffset);
            Vector3 actionHip = entryHip;
            Vector3 triggerSize = new Vector3(
                TriggerWidth,
                TriggerHeight,
                TriggerDepth);
            Vector3 triggerCenter = new Vector3(
                entryRoot.x - TriggerRearOffset,
                TriggerHeight * 0.5f,
                entryRoot.z);
            Vector3 cameraLookAt = entryRoot +
                new Vector3(CameraCityLookOffset, CameraLookHeight, 0f);
            Vector3 ashtrayPosition =
                ResolveAshtrayPosition(balcony);

            return new HomeBalconySmokingPlan(
                entryRoot,
                exitRoot,
                entryHip,
                exitHip,
                FacingDirection,
                FacingDirection,
                actionHip,
                triggerCenter,
                triggerSize,
                ashtrayPosition,
                cameraLookAt);
        }

        public static Vector3 ResolveAshtrayPosition(
            HomeBalconyLayoutPlan balcony)
        {
            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            Rect bounds = balcony.BalconyBounds;
            return new Vector3(
                bounds.xMax -
                PlayerHomeBalconyGeometry.RailingThickness * 0.5f,
                PlayerHomeBalconyGeometry.RailingHeight +
                PlayerHomeBalconyGeometry.RailingCapHeight,
                bounds.center.y + AshtrayFlickOffsetZ);
        }

        public bool CanInteractAt(Vector3 localRootPosition)
        {
            return IsFinite(localRootPosition) &&
                   Mathf.Abs(
                       localRootPosition.y - EntryRootPosition.y) <=
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
                "SmokeEnter",
                "SmokeLoop",
                "SmokeExit",
                enterFrameCount: EnterFrameCount,
                enterFramesPerSecond: EnterFramesPerSecond,
                loopFrameCount: LoopFrameCount,
                loopFramesPerSecond: LoopFramesPerSecond,
                exitFrameCount: ExitFrameCount,
                exitFramesPerSecond: ExitFramesPerSecond,
                loopFrameExtraHoldSeconds:
                    CreateLoopFrameHolds(),
                requiresMouthAccess: true);
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
