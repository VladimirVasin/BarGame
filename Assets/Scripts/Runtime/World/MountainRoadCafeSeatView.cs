using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Measured first-person view from the cafe's free counter stool. The
    /// action hip is the seated pelvis target shared with the bus-seat clips,
    /// so the eye dimensions follow the existing passenger-camera precedent.
    /// </summary>
    public static class MountainRoadCafeSeatViewPlan
    {
        public const float EyeHeightAbovePelvis = 0.78f;
        public const float EyeForwardMeters = 0.12f;
        public const float FieldOfView = 62f;
        public const float BasePitchDegrees = 19f;
        public const float MaximumYawOffsetDegrees = 70f;
        public const float MinimumPitchDegrees = -25f;
        public const float MaximumPitchDegrees = 55f;
        public const float MenuFocusDistanceMeters = 0.50f;
        public const float MenuSurfaceLiftMeters = 0.018f;
        public const float MenuFocusFieldOfView = 40f;
        public const float MenuFocusBlendSeconds = 0.45f;

        public static void EvaluateCamera(
            Vector3 pelvisPosition,
            Vector3 facing,
            float yawOffsetDegrees,
            float pitchDegrees,
            out Vector3 position,
            out Quaternion rotation)
        {
            Vector3 planar = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (planar.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The cafe seat view needs a horizontal facing.",
                    nameof(facing));
            }

            planar.Normalize();
            position = pelvisPosition +
                Vector3.up * EyeHeightAbovePelvis +
                planar * EyeForwardMeters;
            float yaw = Mathf.Clamp(
                yawOffsetDegrees,
                -MaximumYawOffsetDegrees,
                MaximumYawOffsetDegrees);
            float pitch = Mathf.Clamp(
                pitchDegrees,
                MinimumPitchDegrees,
                MaximumPitchDegrees);
            rotation = Quaternion.LookRotation(planar, Vector3.up) *
                Quaternion.Euler(pitch, yaw, 0f);
        }

        public static void EvaluateMenuCamera(
            Vector3 menuRootPosition,
            Vector3 pageNormal,
            Vector3 pageUp,
            Vector3 viewerPosition,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (pageNormal.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The cafe menu view needs a page normal.",
                    nameof(pageNormal));
            }

            pageNormal.Normalize();
            if (Vector3.Dot(pageNormal, Vector3.up) < 0f)
            {
                pageNormal = -pageNormal;
            }

            pageUp = Vector3.ProjectOnPlane(pageUp, pageNormal);
            if (pageUp.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The cafe menu view needs a page up axis.",
                    nameof(pageUp));
            }

            pageUp.Normalize();
            Vector3 target = menuRootPosition +
                pageNormal * MenuSurfaceLiftMeters;
            Vector3 towardViewer = viewerPosition - target;
            if (towardViewer.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The cafe menu view needs a distinct viewer position.",
                    nameof(viewerPosition));
            }

            // Approach the page along the player's existing sight line. A
            // page-normal camera turns the seated view into an abrupt overhead
            // shot and can inherit an upside-down imported page basis.
            position = target +
                towardViewer.normalized * MenuFocusDistanceMeters;
            Vector3 forward = (target - position).normalized;
            Vector3 cameraUp = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (cameraUp.sqrMagnitude < 0.000001f)
            {
                cameraUp = Vector3.ProjectOnPlane(pageUp, forward);
            }

            if (cameraUp.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The cafe menu view cannot resolve an upright camera.",
                    nameof(pageUp));
            }

            rotation = Quaternion.LookRotation(forward, cameraUp.normalized);
        }
    }

    /// <summary>
    /// Owns the camera only while the hero is settled on the cafe stool.
    /// Entry and exit remain visible in the ordinary follow camera; the
    /// looping seated pose uses the hero's eyes, hides every head-bound mesh,
    /// owns the menu close-up and restores the exact previous camera state on
    /// stand/cancel/unload.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeSeatView : MonoBehaviour
    {
        private CityBenchSitInteraction seat;
        private PlayerCameraFollow cameraFollow;
        private Player3DAssetRegistry playerRegistry;
        private Player3DHeadVisibility hiddenHead;
        private bool previousCinematicMotion;
        private bool previousFixedPose;
        private Pose previousFixedCameraPose;
        private float previousFixedFieldOfView;
        private float viewYaw;
        private float viewPitch;
        private Pose menuFocusPose;
        private float menuFocusWeight;
        private bool menuFocusRequested;

        public bool IsInitialized { get; private set; }
        public bool IsFirstPerson { get; private set; }
        public bool IsMenuFocusLocked => menuFocusRequested ||
            menuFocusWeight > 0.0001f;
        public bool IsMenuFocusComplete => menuFocusRequested &&
            menuFocusWeight >= 0.9999f;
        public float MenuFocusWeight => menuFocusWeight;
        public Pose MenuFocusPose => menuFocusPose;
        public Vector3 CurrentCameraPosition => cameraFollow != null
            ? cameraFollow.transform.position
            : Vector3.zero;
        public int HiddenHeadRendererCount =>
            hiddenHead?.HiddenRendererCount ?? 0;

        public void Initialize(
            CityBenchSitInteraction configuredSeat,
            PlayerRuntime player,
            PlayerCameraFollow follow)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The mountain cafe seat view is already initialized.");
            }

            if (configuredSeat == null)
            {
                throw new ArgumentNullException(nameof(configuredSeat));
            }

            if (player.GameObject == null ||
                !(player.Visual is Player3DCharacterPresentation presentation) ||
                presentation.Registry == null ||
                presentation.Registry.Anchors.Pelvis == null)
            {
                throw new ArgumentException(
                    "The cafe seat view requires the authored 3D player rig.",
                    nameof(player));
            }

            if (follow == null)
            {
                throw new ArgumentNullException(nameof(follow));
            }

            seat = configuredSeat;
            cameraFollow = follow;
            playerRegistry = presentation.Registry;
            seat.SeatedChanged += HandleSeatedChanged;
            IsInitialized = true;
            if (seat.IsSeated)
            {
                BeginView();
            }
        }

        private void LateUpdate()
        {
            if (!IsFirstPerson)
            {
                return;
            }

            if (seat == null || !seat.IsSeated)
            {
                EndView();
                return;
            }

            float focusTarget = menuFocusRequested ? 1f : 0f;
            menuFocusWeight = MountainRoadCafeSeatViewPlan
                    .MenuFocusBlendSeconds > 0f
                ? Mathf.MoveTowards(
                    menuFocusWeight,
                    focusTarget,
                    Time.unscaledDeltaTime /
                    MountainRoadCafeSeatViewPlan.MenuFocusBlendSeconds)
                : focusTarget;

            if (!PauseMenuController.IsAnyPaused && !IsMenuFocusLocked)
            {
                Vector2 look = cameraFollow.SampleOrbitInputDegrees(
                    Time.unscaledDeltaTime);
                viewYaw = Mathf.Clamp(
                    viewYaw + look.x,
                    -MountainRoadCafeSeatViewPlan
                        .MaximumYawOffsetDegrees,
                    MountainRoadCafeSeatViewPlan
                        .MaximumYawOffsetDegrees);
                viewPitch = Mathf.Clamp(
                    viewPitch + look.y,
                    MountainRoadCafeSeatViewPlan.MinimumPitchDegrees,
                    MountainRoadCafeSeatViewPlan.MaximumPitchDegrees);
            }

            ApplyView();
        }

        public bool BeginMenuFocus(Pose focusPose)
        {
            if (!IsFirstPerson || cameraFollow == null)
            {
                return false;
            }

            menuFocusPose = focusPose;
            menuFocusRequested = true;
            return true;
        }

        public void EndMenuFocus()
        {
            menuFocusRequested = false;
        }

        private void HandleSeatedChanged(
            CityBenchSitInteraction changedSeat,
            bool seated)
        {
            if (changedSeat != seat)
            {
                return;
            }

            if (seated)
            {
                BeginView();
            }
            else
            {
                EndView();
            }
        }

        private void BeginView()
        {
            if (IsFirstPerson || cameraFollow == null || playerRegistry == null)
            {
                return;
            }

            previousCinematicMotion = cameraFollow.CinematicMotionEnabled;
            previousFixedPose = cameraFollow.FixedPoseActive;
            previousFixedCameraPose = cameraFollow.FixedBasePose;
            previousFixedFieldOfView = cameraFollow.FixedBaseFieldOfView;
            cameraFollow.SetCinematicMotionEnabled(false);
            viewYaw = 0f;
            viewPitch = MountainRoadCafeSeatViewPlan.BasePitchDegrees;
            menuFocusWeight = 0f;
            menuFocusRequested = false;
            hiddenHead = Player3DHeadVisibility.Hide(playerRegistry);
            IsFirstPerson = true;
            ApplyView();
        }

        private void ApplyView()
        {
            Transform pelvis = playerRegistry?.Anchors.Pelvis;
            if (pelvis == null || seat == null || cameraFollow == null)
            {
                return;
            }

            MountainRoadCafeSeatViewPlan.EvaluateCamera(
                pelvis.position,
                seat.Plan.EntryRotation * Vector3.forward,
                viewYaw,
                viewPitch,
                out Vector3 position,
                out Quaternion rotation);
            float fieldOfView = MountainRoadCafeSeatViewPlan.FieldOfView;
            if (menuFocusWeight > 0f)
            {
                float amount = Mathf.SmoothStep(0f, 1f, menuFocusWeight);
                position = Vector3.Lerp(
                    position,
                    menuFocusPose.position,
                    amount);
                Quaternion blendedRotation = Quaternion.Slerp(
                    rotation,
                    menuFocusPose.rotation,
                    amount);
                Vector3 blendedForward =
                    blendedRotation * Vector3.forward;
                Vector3 blendedUp = Vector3.ProjectOnPlane(
                    Vector3.up,
                    blendedForward);
                rotation = blendedUp.sqrMagnitude > 0.000001f
                    ? Quaternion.LookRotation(
                        blendedForward,
                        blendedUp.normalized)
                    : blendedRotation;
                fieldOfView = Mathf.Lerp(
                    fieldOfView,
                    MountainRoadCafeSeatViewPlan.MenuFocusFieldOfView,
                    amount);
            }

            cameraFollow.SetFixedPose(position, rotation, fieldOfView);
        }

        private void EndView()
        {
            if (!IsFirstPerson)
            {
                return;
            }

            IsFirstPerson = false;
            menuFocusRequested = false;
            menuFocusWeight = 0f;
            hiddenHead?.Restore();
            hiddenHead = null;
            if (cameraFollow == null)
            {
                return;
            }

            if (previousFixedPose)
            {
                cameraFollow.SetFixedPose(
                    previousFixedCameraPose.position,
                    previousFixedCameraPose.rotation,
                    previousFixedFieldOfView);
            }
            else
            {
                cameraFollow.ClearFixedPose();
            }

            cameraFollow.SetCinematicMotionEnabled(
                previousCinematicMotion);
        }

        private void OnEnable()
        {
            if (IsInitialized && seat != null && seat.IsSeated)
            {
                BeginView();
            }
        }

        private void OnDisable()
        {
            EndView();
        }

        private void OnDestroy()
        {
            EndView();
            if (seat != null)
            {
                seat.SeatedChanged -= HandleSeatedChanged;
            }
        }
    }
}
