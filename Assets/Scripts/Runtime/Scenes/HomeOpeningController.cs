using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum HomeOpeningMenuOption
    {
        WakeUp = 0,
        Quit = 1
    }

    [DefaultExecutionOrder(250)]
    [DisallowMultipleComponent]
    public sealed class HomeOpeningController : MonoBehaviour
    {
        private enum OpeningCameraPose
        {
            None = 0,
            Clock = 1,
            Sleeper = 2
        }

        private const float ClockFieldOfView = 42f;
        private const float SleeperFieldOfView = 57f;
        public const float OpeningWakeDurationMultiplier = 3f;
        public const float WakeCameraPathHeight = 0.32f;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private HomeInteriorRoot home;
        private HomeOpeningTimeline timeline;
        private OpeningCameraPose appliedCameraPose;
        private GUIStyle titleStyle;
        private GUIStyle titleShadowStyle;
        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;
        private bool initialClockFramePending;
        private bool restored;
        private Vector3 wakeCameraStartPosition;
        private Quaternion wakeCameraStartRotation;
        private float wakeCameraStartFieldOfView;
        private Vector3 wakeCameraPathControlPosition;
        private Vector3 wakeCameraTargetPosition;
        private Quaternion wakeCameraTargetRotation;
        private float wakeCameraTargetFieldOfView;
        private Vector3 wakeCameraFinalPosition;
        private Quaternion wakeCameraFinalRotation;
        private float wakeCameraFinalFieldOfView;
        private float wakeCameraMotionDurationSeconds;

        public bool IsInitialized { get; private set; }
        public bool QuitRequested { get; private set; }
        public HomeOpeningTimeline Timeline => timeline;
        public HomeOpeningPhase Phase =>
            timeline != null
                ? timeline.Phase
                : HomeOpeningPhase.NotStarted;
        public HomeOpeningMenuOption SelectedOption
        {
            get;
            private set;
        } = HomeOpeningMenuOption.WakeUp;
        public bool IsCameraTransitioning =>
            timeline != null &&
            timeline.Phase == HomeOpeningPhase.Waking &&
            timeline.PhaseElapsedSeconds <
            wakeCameraMotionDurationSeconds;
        public Vector3 WakeCameraTargetPosition =>
            wakeCameraTargetPosition;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            if (homeRoot.Player.GameObject == null ||
                homeRoot.Bed == null ||
                homeRoot.AlarmClock == null ||
                homeRoot.CameraFollow == null ||
                homeRoot.FixedCamera == null)
            {
                throw new InvalidOperationException(
                    "The Home opening requires the complete room, player, " +
                    "bed, alarm clock and fixed camera.");
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home opening is already initialized.");
            }

            home = homeRoot;
            if (!modalLock.TryCaptureAndDisable(
                    home.Player.Interactor,
                    home.CameraFollow,
                    home.IntoxicationHud))
            {
                throw new InvalidOperationException(
                    "The Home opening could not capture modal input.");
            }

            bool sleepStarted = false;
            try
            {
                sleepStarted = home.Bed.BeginSleeping();
                if (!sleepStarted)
                {
                    throw new InvalidOperationException(
                        "The player could not start in the sleeping loop.");
                }

                timeline = new HomeOpeningTimeline();
                timeline.Begin();
                ApplyClockDisplay();
                ApplyCameraPose(OpeningCameraPose.Clock);
                initialClockFramePending = true;
                IsInitialized = true;
            }
            catch
            {
                if (sleepStarted)
                {
                    home.AnimatedInteraction?
                        .CancelActiveInteraction();
                }

                modalLock.Restore();
                home = null;
                throw;
            }
        }

        public bool TryWake()
        {
            if (!IsInitialized ||
                restored ||
                QuitRequested ||
                timeline == null ||
                timeline.Phase !=
                HomeOpeningPhase.AwaitingWake ||
                home == null ||
                home.AnimatedInteraction.Phase !=
                PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            if (!timeline.RequestWake())
            {
                return false;
            }

            ApplyClockDisplay();
            home.AlarmClock.StartRinging();
            return true;
        }

        public bool TryQuit()
        {
            if (!IsInitialized ||
                timeline == null ||
                timeline.Phase !=
                HomeOpeningPhase.AwaitingWake ||
                QuitRequested)
            {
                return false;
            }

            QuitRequested = true;
            home?.AlarmClock?.StopRinging();
            Application.Quit();
            return true;
        }

        private void OnEnable()
        {
            if (restored)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!IsInitialized ||
                timeline == null ||
                timeline.Phase ==
                HomeOpeningPhase.Complete)
            {
                return;
            }

            float openingDeltaTime =
                initialClockFramePending &&
                timeline.Phase ==
                HomeOpeningPhase.ClockHold
                    ? 0f
                    : timeline.Phase ==
                      HomeOpeningPhase.Waking
                        ? Time.deltaTime
                        : Time.unscaledDeltaTime;
            initialClockFramePending = false;
            timeline.Advance(openingDeltaTime);
            if (timeline.WakeAnimationReady &&
                !TryBeginWakeAnimation())
            {
                AbortOpening();
                return;
            }

            ApplyClockDisplay();
            if (timeline.ShouldRing &&
                !QuitRequested &&
                !home.AlarmClock.IsRinging)
            {
                home.AlarmClock.StartRinging();
            }
            else if ((!timeline.ShouldRing ||
                      QuitRequested) &&
                     home.AlarmClock.IsRinging)
            {
                home.AlarmClock.StopRinging();
            }

            if (timeline.Phase ==
                HomeOpeningPhase.Waking)
            {
                ApplyWakeCameraTransition();
            }
            else
            {
                ApplyCameraPose(OpeningCameraPose.Clock);
            }

            if (timeline.Phase ==
                HomeOpeningPhase.AwaitingWake)
            {
                if (home.AnimatedInteraction.Phase !=
                    PlayerAnimatedInteractionPhase.Looping)
                {
                    AbortOpening();
                    return;
                }

                ReadMenuInput();
            }
            else if (timeline.Phase ==
                     HomeOpeningPhase.Waking &&
                     home.AnimatedInteraction.Phase ==
                     PlayerAnimatedInteractionPhase.Idle)
            {
                timeline.CompleteWake();
                CompleteOpening();
            }
            else if (timeline.Phase !=
                     HomeOpeningPhase.Waking &&
                     home.AnimatedInteraction.Phase !=
                     PlayerAnimatedInteractionPhase.Looping)
            {
                AbortOpening();
            }
        }

        private bool TryBeginWakeAnimation()
        {
            if (!timeline.WakeAnimationReady ||
                !home.Bed.RequestWake(
                    OpeningWakeDurationMultiplier))
            {
                return false;
            }

            if (!timeline.BeginWakeAnimation())
            {
                return false;
            }

            BeginWakeCameraTransition();
            return true;
        }

        private void ReadMenuInput()
        {
            if (QuitRequested)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            bool previous =
                keyboard != null &&
                (keyboard.upArrowKey.wasPressedThisFrame ||
                 keyboard.wKey.wasPressedThisFrame) ||
                gamepad != null &&
                gamepad.dpad.up.wasPressedThisFrame;
            bool next =
                keyboard != null &&
                (keyboard.downArrowKey.wasPressedThisFrame ||
                 keyboard.sKey.wasPressedThisFrame) ||
                gamepad != null &&
                gamepad.dpad.down.wasPressedThisFrame;
            if (previous || next)
            {
                SelectedOption =
                    SelectedOption ==
                    HomeOpeningMenuOption.WakeUp
                        ? HomeOpeningMenuOption.Quit
                        : HomeOpeningMenuOption.WakeUp;
            }

            bool confirm =
                keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame) ||
                gamepad != null &&
                gamepad.buttonSouth.wasPressedThisFrame;
            if (confirm)
            {
                ActivateSelectedOption();
            }

            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame ||
                gamepad != null &&
                gamepad.buttonEast.wasPressedThisFrame)
            {
                SelectedOption = HomeOpeningMenuOption.Quit;
            }
        }

        private void ActivateSelectedOption()
        {
            if (SelectedOption ==
                HomeOpeningMenuOption.WakeUp)
            {
                TryWake();
            }
            else
            {
                TryQuit();
            }
        }

        private void ApplyCameraPose(
            OpeningCameraPose pose)
        {
            if (home == null ||
                home.CameraFollow == null ||
                pose == OpeningCameraPose.None ||
                pose == appliedCameraPose)
            {
                return;
            }

            ResolveCameraPose(
                pose,
                out Vector3 position,
                out Quaternion rotation,
                out float fieldOfView);
            home.CameraFollow.SetFixedPose(
                position,
                rotation,
                fieldOfView);
            appliedCameraPose = pose;
        }

        private void BeginWakeCameraTransition()
        {
            wakeCameraStartPosition =
                home.CameraFollow.FixedBasePosition;
            wakeCameraStartRotation =
                home.CameraFollow.FixedBaseRotation;
            wakeCameraStartFieldOfView =
                home.CameraFollow.FixedBaseFieldOfView;
            ResolveCameraPose(
                OpeningCameraPose.Sleeper,
                out wakeCameraTargetPosition,
                out wakeCameraTargetRotation,
                out wakeCameraTargetFieldOfView);
            wakeCameraPathControlPosition =
                Vector3.Lerp(
                    wakeCameraStartPosition,
                    wakeCameraTargetPosition,
                    0.5f) +
                Vector3.up * WakeCameraPathHeight;
            HomeCameraShot gameplayShot =
                home.FixedCamera.ActiveShot;
            wakeCameraFinalPosition =
                gameplayShot.Position;
            wakeCameraFinalRotation =
                gameplayShot.Rotation;
            wakeCameraFinalFieldOfView =
                gameplayShot.FieldOfView;
            wakeCameraMotionDurationSeconds =
                Mathf.Max(
                    HomeOpeningTimeline
                        .WakeCameraTransitionSeconds,
                    (float)home.AnimatedInteraction
                        .ExitDurationSeconds);
        }

        private void ApplyWakeCameraTransition()
        {
            if (timeline.WakeCameraTransitionComplete)
            {
                ApplyWakeCameraSettle();
                return;
            }

            float blend =
                timeline.WakeCameraTransitionBlend;
            float inverseBlend = 1f - blend;
            Vector3 position =
                inverseBlend *
                inverseBlend *
                wakeCameraStartPosition +
                2f *
                inverseBlend *
                blend *
                wakeCameraPathControlPosition +
                blend *
                blend *
                wakeCameraTargetPosition;
            home.CameraFollow.SetFixedPose(
                position,
                Quaternion.Slerp(
                    wakeCameraStartRotation,
                    wakeCameraTargetRotation,
                    blend),
                Mathf.Lerp(
                    wakeCameraStartFieldOfView,
                    wakeCameraTargetFieldOfView,
                    blend));
        }

        private void ApplyWakeCameraSettle()
        {
            float settleDuration =
                wakeCameraMotionDurationSeconds -
                HomeOpeningTimeline
                    .WakeCameraTransitionSeconds;
            float progress =
                settleDuration > 0f
                    ? (timeline.PhaseElapsedSeconds -
                       HomeOpeningTimeline
                           .WakeCameraTransitionSeconds) /
                      settleDuration
                    : 1f;
            float blend = SmootherStep(progress);
            home.CameraFollow.SetFixedPose(
                Vector3.Lerp(
                    wakeCameraTargetPosition,
                    wakeCameraFinalPosition,
                    blend),
                Quaternion.Slerp(
                    wakeCameraTargetRotation,
                    wakeCameraFinalRotation,
                    blend),
                Mathf.Lerp(
                    wakeCameraTargetFieldOfView,
                    wakeCameraFinalFieldOfView,
                    blend));
            appliedCameraPose =
                blend < 1f
                    ? OpeningCameraPose.Sleeper
                    : OpeningCameraPose.None;
        }

        private void ResolveCameraPose(
            OpeningCameraPose pose,
            out Vector3 position,
            out Quaternion rotation,
            out float fieldOfView)
        {
            if (pose == OpeningCameraPose.Clock)
            {
                Vector3 target =
                    home.AlarmClock.transform.position +
                    new Vector3(0f, -0.015f, 0f);
                position =
                    target +
                    new Vector3(-0.45f, 0.39f, -1.18f);
                rotation = LookAt(position, target);
                fieldOfView = ClockFieldOfView;
            }
            else
            {
                Vector3 target =
                    home.BedInteractionPlan.ActionHipPosition +
                    new Vector3(0f, 0.08f, 0f);
                position =
                    new Vector3(-4.54f, 2.25f, -2.82f);
                rotation = LookAt(position, target);
                fieldOfView = SleeperFieldOfView;
            }
        }

        private void ApplyClockDisplay()
        {
            if (home == null ||
                home.AlarmClock == null ||
                timeline == null)
            {
                return;
            }

            home.AlarmClock.SetDisplayTime(
                timeline.DisplayHour,
                timeline.DisplayMinute);
            home.AlarmClock.SetDisplayVisible(
                timeline.DisplayVisible);
        }

        private void CompleteOpening()
        {
            if (restored)
            {
                return;
            }

            home.AlarmClock.StopRinging();
            modalLock.Restore();
            home.FixedCamera.ReapplyActiveShot();
            restored = true;
            appliedCameraPose = OpeningCameraPose.None;
            enabled = false;
        }

        private void AbortOpening()
        {
            if (restored)
            {
                return;
            }

            timeline?.Cancel();
            ApplyClockDisplay();
            home?.AlarmClock?.StopRinging();
            home?.AnimatedInteraction?
                .CancelActiveInteraction();
            modalLock.Restore();
            if (home != null &&
                home.FixedCamera != null &&
                home.FixedCamera.isActiveAndEnabled)
            {
                home.FixedCamera.ReapplyActiveShot();
            }

            restored = true;
            appliedCameraPose = OpeningCameraPose.None;
            enabled = false;
        }

        private void OnDisable()
        {
            if (!IsInitialized || restored)
            {
                return;
            }

            home?.AlarmClock?.StopRinging();
            home?.AnimatedInteraction?
                .CancelActiveInteraction();
            timeline?.Cancel();
            ApplyClockDisplay();
            modalLock.Restore();
            if (home != null &&
                home.FixedCamera != null &&
                home.FixedCamera.isActiveAndEnabled)
            {
                home.FixedCamera.ReapplyActiveShot();
            }

            restored = true;
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        private void OnGUI()
        {
            if (!IsInitialized ||
                restored ||
                timeline == null ||
                timeline.Phase ==
                HomeOpeningPhase.Complete)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -120;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawPresentation(canvas);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawPresentation(RetroUiCanvas canvas)
        {
            Rect screen = new Rect(
                0f,
                0f,
                RetroUiTheme.LogicalWidth,
                RetroUiTheme.LogicalHeight);
            if (timeline.MenuVisible)
            {
                RetroUiTheme.FillRect(
                    new Rect(0f, 0f, 640f, 25f),
                    new Color(0f, 0f, 0f, 0.82f));
                RetroUiTheme.FillRect(
                    new Rect(0f, 333f, 640f, 27f),
                    new Color(0f, 0f, 0f, 0.86f));
                RetroUiTheme.DrawDither(
                    screen,
                    new Color(0.72f, 0.63f, 0.50f, 0.025f));
                DrawMenu(canvas);
            }

        }

        private void DrawMenu(RetroUiCanvas canvas)
        {
            const float titleX = 34f;
            const float titleY = 270f;
            const float titleWidth = 355f;
            GUI.Label(
                new Rect(
                    titleX + 2f,
                    titleY + 2f,
                    titleWidth,
                    32f),
                LocalizationService.Get("opening.title"),
                titleShadowStyle);
            GUI.Label(
                new Rect(
                    titleX,
                    titleY,
                    titleWidth,
                    32f),
                LocalizationService.Get("opening.title"),
                titleStyle);

            Rect wakeRect = new Rect(425f, 260f, 184f, 28f);
            Rect quitRect = new Rect(425f, 292f, 184f, 24f);
            DrawOption(
                canvas,
                wakeRect,
                HomeOpeningMenuOption.WakeUp,
                "opening.wake_up");
            DrawOption(
                canvas,
                quitRect,
                HomeOpeningMenuOption.Quit,
                "opening.quit");
        }

        private void DrawOption(
            RetroUiCanvas canvas,
            Rect rect,
            HomeOpeningMenuOption option,
            string localizationKey)
        {
            Vector2 mouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            if (rect.Contains(mouse))
            {
                SelectedOption = option;
            }

            bool selected = SelectedOption == option;
            if (selected)
            {
                RetroUiTheme.DrawPanel(
                    rect,
                    new Color(0.08f, 0.055f, 0.06f, 0.92f),
                    RetroUiTheme.Accent,
                    true,
                    1f,
                    1f);
            }

            string prefix = selected ? "> " : "  ";
            if (GUI.Button(
                    rect,
                    prefix +
                    LocalizationService.Get(localizationKey),
                    selected
                        ? selectedStyle
                        : optionStyle))
            {
                SelectedOption = option;
                ActivateSelectedOption();
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                22,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
            titleShadowStyle = new GUIStyle(titleStyle);
            titleShadowStyle.normal.textColor =
                new Color(0f, 0f, 0f, 0.92f);
            selectedStyle = RetroUiTheme.CreateButtonStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            optionStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Muted,
                true);
        }

        private static Quaternion LookAt(
            Vector3 position,
            Vector3 target)
        {
            Vector3 forward = target - position;
            if (forward.sqrMagnitude < 0.000001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped *
                    (clamped * 6f - 15f) +
                    10f);
        }
    }
}
