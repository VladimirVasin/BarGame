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
    }

    /// <summary>
    /// Owns the camera only while the hero is settled on the cafe stool.
    /// Entry and exit remain visible in the ordinary follow camera; the
    /// looping seated pose uses the hero's eyes, hides every head-bound mesh
    /// and restores the exact previous camera state on stand/cancel/unload.
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

        public bool IsInitialized { get; private set; }
        public bool IsFirstPerson { get; private set; }
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

            if (!PauseMenuController.IsAnyPaused)
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
            cameraFollow.SetFixedPose(
                position,
                rotation,
                MountainRoadCafeSeatViewPlan.FieldOfView);
        }

        private void EndView()
        {
            if (!IsFirstPerson)
            {
                return;
            }

            IsFirstPerson = false;
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
