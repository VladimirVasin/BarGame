using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Neutral eye-level view for any physically occupied counter seat. The
    /// visible entry and exit stay in the ordinary follow camera. Only the
    /// looping seated phase owns this fixed view, and only head-bound meshes
    /// are hidden while the camera is inside the production hero rig. A
    /// locked close action starts from the player's current gaze and keeps the
    /// lens at its captured eye-space offset throughout the animated head pose.
    /// Drinking can admit a small manual look offset without moving that rig;
    /// an order gesture uses the ordinary seated look range in both axes.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class CounterSeatView : MonoBehaviour
    {
        public const float MaximumYawOffsetDegrees = 70f;
        public const float MinimumPitchDegrees = -25f;
        public const float MaximumPitchDegrees = 55f;
        public const float ActionNearClipPlane = 0.03f;
        public const float ActionEyeClearanceMetres = 0.02f;
        public const float MaximumActionYawOffsetDegrees = 12f;
        public const float MaximumActionPitchOffsetDegrees = 10f;

        private CounterSeatInteraction seat;
        private PlayerCameraFollow cameraFollow;
        private Camera viewCamera;
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
        private float actionEyeClearanceWeight;
        private bool actionLimitedLookEnabled;
        private bool actionFullSeatedLookEnabled;
        private float actionLookYaw, actionLookPitch;
        private Vector3 actionHeadPositionInPelvis;
        private bool actionHeadPositionCaptured;
        private Vector3 actionCameraPositionInHead;
        private bool actionCameraPositionCaptured;
        private Quaternion actionCameraRotation;
        private Quaternion actionCameraRotationInHead;
        private bool actionCameraRotationInHeadCaptured;
        private bool actionCameraRotationCaptured;
        private Camera actionCamera;
        private float actionPreviousNearClipPlane;
        private bool actionNearClipCaptured;

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
        public bool IsActionLimitedLookEnabled => actionLookLocked && actionLimitedLookEnabled;
        public float ActionLookYaw => actionLookYaw;
        public float ActionLookPitch => actionLookPitch;
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

        public void SetActionLookLocked(bool locked, bool allowLimitedLook = false,
            bool allowFullSeatedLook = false)
        {
            if (!IsFirstPerson || !locked)
            {
                if (IsFirstPerson && actionLookLocked && actionLimitedLookEnabled)
                {
                    // Carry the player's chosen gaze into the ordinary seated
                    // view rather than snapping back when the drink finishes.
                    viewYaw = Mathf.Clamp(viewYaw + actionLookYaw, -MaximumYawOffsetDegrees, MaximumYawOffsetDegrees);
                    viewPitch = Mathf.Clamp(viewPitch + actionLookPitch, MinimumPitchDegrees, MaximumPitchDegrees);
                }
                actionLookLocked = false;
                ReleaseActionCameraTracking();
                return;
            }

            if (actionLookLocked)
            {
                return;
            }

            actionLookLocked = true;
            actionLimitedLookEnabled = allowLimitedLook;
            actionFullSeatedLookEnabled = allowLimitedLook && allowFullSeatedLook;
            actionLookYaw = actionLookPitch = 0f;
            actionEyeClearanceWeight = 0f;
            CaptureActionCameraTracking();
        }

        public void SetActionEyeClearance(float weight)
        {
            actionEyeClearanceWeight = actionLookLocked && IsFirstPerson
                ? Mathf.Clamp01(weight)
                : 0f;
        }

        /// <summary>Finalizes the existing camera after a seated world-rig reach moves the head.</summary>
        public void RefreshActionCameraPose()
        {
            if (IsFirstPerson && actionLookLocked) ApplyView();
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
            viewCamera = follow.GetComponent<Camera>();
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
                !GameTimeScaleRuntime.IsPaused &&
                !IsMenuFocusLocked &&
                (!actionLookLocked || actionLimitedLookEnabled))
            {
                Vector2 look = cameraFollow.SampleOrbitInputDegrees(
                    Time.unscaledDeltaTime);
                if (actionLookLocked)
                {
                    actionLookYaw = Mathf.Clamp(actionLookYaw + look.x,
                        actionFullSeatedLookEnabled ? -MaximumYawOffsetDegrees - viewYaw :
                            Mathf.Max(-MaximumActionYawOffsetDegrees, -MaximumYawOffsetDegrees - viewYaw),
                        actionFullSeatedLookEnabled ? MaximumYawOffsetDegrees - viewYaw :
                            Mathf.Min(MaximumActionYawOffsetDegrees, MaximumYawOffsetDegrees - viewYaw));
                    // Ordering keeps the same full view as sitting normally,
                    // including when the menu or glass set a downward gaze.
                    actionLookPitch = Mathf.Clamp(actionLookPitch + look.y,
                        actionFullSeatedLookEnabled ? MinimumPitchDegrees - viewPitch :
                            Mathf.Max(-MaximumActionPitchOffsetDegrees, MinimumPitchDegrees - viewPitch),
                        actionFullSeatedLookEnabled ? MaximumPitchDegrees - viewPitch :
                            Mathf.Min(MaximumActionPitchOffsetDegrees, MaximumPitchDegrees - viewPitch));
                }
                else
                {
                    viewYaw = Mathf.Clamp(viewYaw + look.x, -MaximumYawOffsetDegrees, MaximumYawOffsetDegrees);
                    viewPitch = Mathf.Clamp(viewPitch + look.y, MinimumPitchDegrees, MaximumPitchDegrees);
                }
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

            ReleaseActionCameraTracking();
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

            if (actionLookLocked)
            {
                Transform head = playerRegistry?.Anchors.Head;
                if (head != null)
                {
                    if (actionCameraPositionCaptured)
                    {
                        // Keep the lens at its captured eye-space offset. The
                        // head rotation then moves the camera around the neck
                        // with the face, so the mouth and mug cannot pass
                        // through a camera which only copied translation.
                        position = head.TransformPoint(
                            actionCameraPositionInHead);
                    }
                    else if (actionHeadPositionCaptured)
                    {
                        Vector3 baselineHeadPosition =
                            pelvis.TransformPoint(actionHeadPositionInPelvis);
                        position += head.position - baselineHeadPosition;
                    }
                }
            }

            if (actionLookLocked && actionCameraRotationCaptured)
            {
                Transform head = playerRegistry?.Anchors.Head;
                rotation = head != null &&
                           actionCameraRotationInHeadCaptured
                    ? head.rotation * actionCameraRotationInHead
                    : actionCameraRotation;
                position -= rotation * Vector3.forward *
                    (ActionEyeClearanceMetres *
                     actionEyeClearanceWeight);
                if (actionLimitedLookEnabled)
                {
                    // Use the same pitch/yaw convention as CounterSeatPlan.
                    // The relative change follows the animated head, and its
                    // terminal gaze is identical to the ordinary seated view.
                    Quaternion initialLook = Quaternion.Euler(viewPitch, viewYaw, 0f);
                    Quaternion manualLook = Quaternion.Euler(viewPitch + actionLookPitch, viewYaw + actionLookYaw, 0f);
                    rotation *= Quaternion.Inverse(initialLook) * manualLook;
                }
            }

            cameraFollow.SetFixedPose(position, rotation, fieldOfView);
        }

        private void EndView()
        {
            ReleaseActionCameraTracking();

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

        private void CaptureActionCameraTracking()
        {
            Transform pelvis = playerRegistry?.Anchors.Pelvis;
            Transform head = playerRegistry?.Anchors.Head;
            if (pelvis != null && head != null)
            {
                actionHeadPositionInPelvis =
                    pelvis.InverseTransformPoint(head.position);
                actionHeadPositionCaptured = true;
            }

            Camera targetCamera = viewCamera;
            if (targetCamera == null)
            {
                return;
            }

            actionCamera = targetCamera;
            actionCameraRotation = targetCamera.transform.rotation;
            if (head != null)
            {
                actionCameraPositionInHead =
                    head.InverseTransformPoint(targetCamera.transform.position);
                actionCameraPositionCaptured = true;
                actionCameraRotationInHead =
                    Quaternion.Inverse(head.rotation) *
                    targetCamera.transform.rotation;
                actionCameraRotationInHeadCaptured = true;
            }

            actionCameraRotationCaptured = true;
            actionPreviousNearClipPlane = targetCamera.nearClipPlane;
            actionNearClipCaptured = true;
            targetCamera.nearClipPlane = Mathf.Min(
                targetCamera.nearClipPlane,
                ActionNearClipPlane);
        }

        private void ReleaseActionCameraTracking()
        {
            if (actionNearClipCaptured && actionCamera != null)
            {
                actionCamera.nearClipPlane = actionPreviousNearClipPlane;
            }

            actionCamera = null;
            actionCameraRotation = Quaternion.identity;
            actionCameraRotationInHead = Quaternion.identity;
            actionCameraRotationInHeadCaptured = false;
            actionCameraRotationCaptured = false;
            actionPreviousNearClipPlane = 0f;
            actionNearClipCaptured = false;
            actionHeadPositionInPelvis = Vector3.zero;
            actionHeadPositionCaptured = false;
            actionEyeClearanceWeight = 0f;
            actionLimitedLookEnabled = false;
            actionFullSeatedLookEnabled = false;
            actionLookYaw = actionLookPitch = 0f;
            actionCameraPositionInHead = Vector3.zero;
            actionCameraPositionCaptured = false;
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
