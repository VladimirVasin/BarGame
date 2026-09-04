using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Neutral eye-level view for any physically occupied counter seat. The
    /// visible entry and exit stay in the ordinary follow camera. Only the
    /// looping seated phase owns this fixed view, and only head-bound meshes
    /// are hidden while the camera is inside the production hero rig.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class CounterSeatView : MonoBehaviour
    {
        public const float MaximumYawOffsetDegrees = 70f;
        public const float MinimumPitchDegrees = -25f;
        public const float MaximumPitchDegrees = 55f;

        private CounterSeatInteraction seat;
        private PlayerCameraFollow cameraFollow;
        private Player3DAssetRegistry playerRegistry;
        private Player3DHeadVisibility hiddenHead;
        private bool previousCinematicMotion;
        private bool previousOrbitInput;
        private bool previousFixedPose;
        private Pose previousFixedCameraPose;
        private float previousFixedFieldOfView;
        private float viewYaw;
        private float viewPitch;
        private Pose menuFocusPose;
        private float menuFocusWeight;
        private float menuFocusFieldOfView =
            CounterMenuCameraPlan.FocusFieldOfView;
        private bool menuFocusRequested;
        private bool actionLookLocked;

        public bool IsInitialized { get; private set; }
        public bool IsFirstPerson { get; private set; }
        public int HiddenHeadRendererCount =>
            hiddenHead?.HiddenRendererCount ?? 0;
        public float ViewYaw => viewYaw;
        public float ViewPitch => viewPitch;
        public bool IsMenuFocusLocked => menuFocusRequested ||
            menuFocusWeight > 0.0001f;
        public bool IsMenuFocusComplete => menuFocusRequested &&
            menuFocusWeight >= 0.9999f;
        public bool IsActionLookLocked => actionLookLocked;
        public float MenuFocusWeight => menuFocusWeight;
        public Pose MenuFocusPose => menuFocusPose;
        public float MenuFocusFieldOfView => menuFocusFieldOfView;
        public Vector3 CurrentCameraPosition => cameraFollow != null
            ? cameraFollow.transform.position
            : Vector3.zero;
        public CounterSeatInteraction Seat => seat;
        public PlayerCameraFollow CameraFollow => cameraFollow;

        /// <summary>
        /// Allows an adapter subscribed before this view to establish camera
        /// ownership before opening its modal. The seat event will call the
        /// same idempotent path immediately afterwards.
        /// </summary>
        public bool BeginSeatedView()
        {
            if (!IsInitialized || seat == null || !seat.IsSeated)
            {
                return false;
            }

            BeginView();
            return IsFirstPerson;
        }

        public bool BeginMenuFocus(Pose focusPose)
        {
            return BeginMenuFocus(
                focusPose,
                CounterMenuCameraPlan.FocusFieldOfView);
        }

        public bool BeginMenuFocus(
            Pose focusPose,
            float focusFieldOfView)
        {
            if (float.IsNaN(focusFieldOfView) ||
                float.IsInfinity(focusFieldOfView) ||
                focusFieldOfView < CounterSeatPlan.MinimumFieldOfView ||
                focusFieldOfView > CounterSeatPlan.MaximumFieldOfView)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(focusFieldOfView),
                    focusFieldOfView,
                    "The counter-menu camera field of view is invalid.");
            }

            if (!IsFirstPerson || cameraFollow == null)
            {
                return false;
            }

            menuFocusPose = focusPose;
            menuFocusFieldOfView = focusFieldOfView;
            menuFocusRequested = true;
            return true;
        }

        public void EndMenuFocus()
        {
            menuFocusRequested = false;
        }

        public void SetActionLookLocked(bool locked)
        {
            actionLookLocked = IsFirstPerson && locked;
        }

        public void Initialize(
            CounterSeatInteraction configuredSeat,
            PlayerRuntime player,
            PlayerCameraFollow follow)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The counter seat view is already initialized.");
            }

            if (configuredSeat == null || !configuredSeat.IsInitialized)
            {
                throw new ArgumentException(
                    "The counter seat view requires an initialized seat.",
                    nameof(configuredSeat));
            }

            if (player.GameObject == null ||
                !(player.Visual is Player3DCharacterPresentation presentation) ||
                presentation.Registry == null ||
                presentation.Registry.Anchors.Pelvis == null)
            {
                throw new ArgumentException(
                    "The counter seat view requires the authored 3D hero rig.",
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
            menuFocusWeight = CounterMenuCameraPlan.FocusBlendSeconds > 0f
                ? Mathf.MoveTowards(
                    menuFocusWeight,
                    focusTarget,
                    Time.unscaledDeltaTime /
                    CounterMenuCameraPlan.FocusBlendSeconds)
                : focusTarget;

            if (!PauseMenuController.IsAnyPaused &&
                !IsMenuFocusLocked &&
                !actionLookLocked)
            {
                Vector2 look = cameraFollow.SampleOrbitInputDegrees(
                    Time.unscaledDeltaTime);
                viewYaw = Mathf.Clamp(
                    viewYaw + look.x,
                    -MaximumYawOffsetDegrees,
                    MaximumYawOffsetDegrees);
                viewPitch = Mathf.Clamp(
                    viewPitch + look.y,
                    MinimumPitchDegrees,
                    MaximumPitchDegrees);
            }

            ApplyView();
        }

        private void HandleSeatedChanged(
            CounterSeatInteraction changedSeat,
            bool isSeated)
        {
            if (changedSeat != seat)
            {
                return;
            }

            if (isSeated)
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
            if (IsFirstPerson ||
                seat == null ||
                cameraFollow == null ||
                playerRegistry == null)
            {
                return;
            }

            previousCinematicMotion = cameraFollow.CinematicMotionEnabled;
            previousOrbitInput = cameraFollow.OrbitInputEnabled;
            previousFixedPose = cameraFollow.FixedPoseActive;
            previousFixedCameraPose = cameraFollow.FixedBasePose;
            previousFixedFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
            cameraFollow.SetCinematicMotionEnabled(false);
            viewYaw = 0f;
            viewPitch = 0f;
            menuFocusWeight = 0f;
            menuFocusFieldOfView =
                CounterMenuCameraPlan.FocusFieldOfView;
            menuFocusRequested = false;
            actionLookLocked = false;
            hiddenHead = Player3DHeadVisibility.Hide(playerRegistry);
            IsFirstPerson = true;
            ApplyView();
        }

        private void ApplyView()
        {
            Transform pelvis = playerRegistry?.Anchors.Pelvis;
            CounterSeatPlan plan = seat?.Plan;
            if (pelvis == null || plan == null || cameraFollow == null)
            {
                return;
            }

            plan.EvaluateCamera(
                pelvis.position,
                viewYaw,
                viewPitch,
                out Vector3 position,
                out Quaternion rotation);
            float fieldOfView = plan.CameraFieldOfView;
            if (menuFocusWeight > 0f)
            {
                float amount = Mathf.SmoothStep(
                    0f,
                    1f,
                    menuFocusWeight);
                position = Vector3.Lerp(
                    position,
                    menuFocusPose.position,
                    amount);
                Quaternion blended = Quaternion.Slerp(
                    rotation,
                    menuFocusPose.rotation,
                    amount);
                Vector3 forward = blended * Vector3.forward;
                Vector3 upright = Vector3.ProjectOnPlane(
                    Vector3.up,
                    forward);
                rotation = upright.sqrMagnitude > 0.000001f
                    ? Quaternion.LookRotation(forward, upright.normalized)
                    : blended;
                fieldOfView = Mathf.Lerp(
                    fieldOfView,
                    menuFocusFieldOfView,
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
            actionLookLocked = false;
            menuFocusWeight = 0f;
            menuFocusFieldOfView =
                CounterMenuCameraPlan.FocusFieldOfView;
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

            cameraFollow.SetOrbitInputEnabled(previousOrbitInput);
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

            IsInitialized = false;
        }
    }
}
