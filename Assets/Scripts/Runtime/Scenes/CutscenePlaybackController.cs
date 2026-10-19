using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scripted and debug playback in the loaded level, with an optional live gameplay handoff.</summary>
    [DefaultExecutionOrder(-850)]
    [DisallowMultipleComponent]
    public sealed class CutscenePlaybackController : MonoBehaviour
    {
        private readonly BarMinigameModalLock modalLock = new BarMinigameModalLock();
        private PlayerRuntime player;
        private PlayerCameraFollow follow;
        private IntoxicationHudView hud;
        private PauseMenuController menu;
        private Camera viewCamera;
        private Pose cameraPose;
        private float cameraFieldOfView;
        private bool previousAudioPause, initialized;
        private IDisposable worldPause;
        private IDisposable cameraSuspension;
        private ICutscenePlayback content;
        private ICutscenePreparationPlayback preparation;
        private ICutsceneInteractivePlayback interactive;
        private CutsceneDefinition definition;
        private CutscenePlaybackState clock;
        private bool returnPending, finishNormally, preparationReady, gameplayActive;
        private bool debugPreview;
        private double interactiveElapsed;
        private Action<bool> completion;
        private IDisposable interactivePause;
        private bool interactivePreviousAudioPause;
        private int inputGuardFrame, returnFrame;
        private long displayedMilliseconds = -1;
        private readonly char[] timecodeBuffer = new char[12];
        private GUIStyle timeStyle, buttonStyle;

        public CutsceneCatalog Catalog { get; } = new CutsceneCatalog();
        public bool IsPlaying => content != null;
        public bool IsDebugPreview => debugPreview;
        public bool IsInteractive => IsPlaying && gameplayActive;
        public bool IsInteractivePlayback => IsInteractive;
        public bool IsPlaybackPaused => clock != null && clock.IsPaused;
        public double ElapsedSeconds => (clock?.ElapsedSeconds ?? 0d) + interactiveElapsed;
        public string CurrentTimecode { get; private set; } = "00:00.000";
        internal bool AutomaticPlayback { get; set; } = true;

        /// <summary>A replay boundary may restore its previous ready-state camera before staging anew.</summary>
        internal void RefreshReturnCameraSnapshot()
        {
            if (worldPause == null || viewCamera == null) return;
            cameraPose = new Pose(viewCamera.transform.position, viewCamera.transform.rotation);
            cameraFieldOfView = viewCamera.fieldOfView;
        }

        public void Initialize(PlayerRuntime playerRuntime, PlayerCameraFollow cameraFollow,
            IntoxicationHudView intoxicationHud, PauseMenuController pauseMenu)
        {
            if (initialized) throw new InvalidOperationException("Cutscene playback is already initialized.");
            if (playerRuntime.GameObject == null || playerRuntime.Interactor == null || cameraFollow == null || pauseMenu == null)
                throw new ArgumentException("Cutscene playback requires the loaded level's player, camera and pause menu.");
            player = playerRuntime;
            follow = cameraFollow;
            viewCamera = follow.GetComponent<Camera>();
            if (viewCamera == null) throw new ArgumentException("The cutscene camera is missing.", nameof(cameraFollow));
            hud = intoxicationHud;
            menu = pauseMenu;
            initialized = true;
        }

        public bool TryPlay(string id) => TryPlayInternal(id, true, null);

        public bool TryPlayGameplay(string id, Action<bool> completed = null) =>
            TryPlayInternal(id, false, completed);

        private bool TryPlayInternal(string id, bool preview, Action<bool> completed)
        {
            if (!initialized || !isActiveAndEnabled || IsPlaying || returnPending ||
                SceneTransitionService.IsTransitioning || BarMinigameModalLock.IsAnyLocked ||
                GameTimeScaleRuntime.IsPaused || player.GameObject == null || follow == null || menu == null)
                return false;
            CutsceneDefinition next = Catalog.Find(id);
            if (next == null) return false;
            cameraPose = new Pose(viewCamera.transform.position, viewCamera.transform.rotation);
            cameraFieldOfView = viewCamera.fieldOfView;
            if (!modalLock.TryCaptureAndDisable(player.Interactor, follow, hud)) return false;

            definition = next;
            debugPreview = preview;
            completion = completed;
            cameraSuspension = follow.SuspendPresentation();
            previousAudioPause = AudioListener.pause;
            worldPause = GameTimeScaleRuntime.AcquirePause();
            AudioListener.pause = true;
            inputGuardFrame = Time.frameCount + 1;
            return PrepareContent();
        }

        private bool PrepareContent()
        {
            try
            {
                content = definition.Factory();
                if (content == null) throw new InvalidOperationException("The cutscene factory returned no playback.");
                content.Prepare();
                preparation = content as ICutscenePreparationPlayback;
                interactive = content as ICutsceneInteractivePlayback;
                preparationReady = preparation == null || preparation.IsReady;
                gameplayActive = false;
                interactiveElapsed = 0d;
                clock = new CutscenePlaybackState(content.DurationSeconds);
                content.Sample(0d);
                content.SetPaused(!GameInput.MovementFocused);
                contentPaused = !GameInput.MovementFocused;
                UpdateTimecode();
                return true;
            }
            catch (Exception exception)
            {
                GameLog.Warning("cutscene", "prepare_failed", GameLog.Field("message", exception.Message));
                Stop();
                return false;
            }
        }

        public bool TogglePause()
        {
            if (!IsPlaying || returnPending) return false;
            clock.TogglePause();
            ApplyInteractivePause();
            ApplyContentPause();
            return true;
        }

        public bool Restart()
        {
            if (!IsPlaying || returnPending || !debugPreview || menu.IsOpen) return false;
            // A live phase has released the original leases. Reacquire them
            // before restoring actors and preparing a new approach.
            if (worldPause == null)
            {
                ReleaseInteractivePause();
                if (!modalLock.TryCaptureAndDisable(player.Interactor, follow, hud)) return false;
                cameraSuspension = follow.SuspendPresentation();
                previousAudioPause = AudioListener.pause;
                worldPause = GameTimeScaleRuntime.AcquirePause();
                AudioListener.pause = true;
            }
            StopContent();
            // Keep the world, input and camera leases throughout the restart.
            inputGuardFrame = Time.frameCount + 1;
            if (PrepareContent()) return true;
            if (menu != null) menu.OpenCutscenes();
            return false;
        }

        internal void Advance(double seconds)
        {
            if (!IsPlaying || returnPending || IsPlaybackPaused || !GameInput.MovementFocused) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (gameplayActive && GameTimeScaleRuntime.IsPaused) return;
            try
            {
                if (!preparationReady)
                {
                    preparation.AdvancePreparation((float)seconds);
                    preparationReady = preparation.IsReady;
                    if (preparationReady) content.Sample(0d);
                    return;
                }
                if (gameplayActive)
                {
                    interactiveElapsed += seconds;
                    interactive.AdvanceInteractive((float)seconds);
                }
                else clock.Advance(seconds);
                content.Sample(ElapsedSeconds);
                UpdateTimecode();
                if (!gameplayActive && interactive != null && interactive.WantsGameplay)
                {
                    gameplayActive = true;
                    ReleasePresentationOwnership(restoreCamera: false);
                    ApplyInteractivePause();
                }
                // Keep the terminal sample visible before releasing presentation.
                if (interactive != null ? interactive.IsComplete : clock.IsComplete)
                {
                    if (interactive != null && interactive.KeepsGameplayOnCompletion)
                        interactive.CompleteIntoGameplay();
                    ScheduleFinish(true);
                }
            }
            catch (Exception exception)
            {
                GameLog.Warning("cutscene", "sample_failed", GameLog.Field("message", exception.Message));
                ReturnToList();
            }
        }

        public void ReturnToList() => ScheduleFinish(false);

        private void ScheduleFinish(bool completedNormally)
        {
            if (!IsPlaying || returnPending) return;
            returnPending = true;
            finishNormally = completedNormally;
            returnFrame = Time.frameCount;
        }

        public void Stop() => Stop(false);

        private void Stop(bool completedNormally)
        {
            Action<bool> callback = completion;
            completion = null;
            StopContent();
            ReleaseInteractivePause();
            ReleasePresentationOwnership(restoreCamera: true);
            definition = null;
            clock = null;
            interactiveElapsed = 0d;
            gameplayActive = preparationReady = false;
            returnPending = finishNormally = false;
            displayedMilliseconds = -1;
            CurrentTimecode = "00:00.000";
            try { callback?.Invoke(completedNormally); }
            catch (Exception exception)
            {
                GameLog.Warning("cutscene", "completion_failed", GameLog.Field("message", exception.Message));
            }
        }

        private void ReleasePresentationOwnership(bool restoreCamera)
        {
            if (worldPause != null)
            {
                if (restoreCamera && viewCamera != null)
                {
                    viewCamera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
                    viewCamera.fieldOfView = cameraFieldOfView;
                }
                cameraSuspension?.Dispose();
                cameraSuspension = null;
                AudioListener.pause = previousAudioPause;
                worldPause.Dispose();
                worldPause = null;
            }
            modalLock.Restore();
        }

        private void StopContent()
        {
            ICutscenePlayback previous = content;
            content = null;
            preparation = null;
            interactive = null;
            contentPaused = null;
            try { previous?.Stop(); }
            catch (Exception exception)
            {
                GameLog.Warning("cutscene", "cleanup_failed", GameLog.Field("message", exception.Message));
            }
        }

        private void Update()
        {
            if (!IsPlaying) return;
            if (SceneTransitionService.IsTransitioning || player.GameObject == null || follow == null || menu == null)
            {
                Stop();
                return;
            }
            if (returnPending)
            {
                if (Time.frameCount > returnFrame)
                {
                    bool reopenList = debugPreview &&
                        !(finishNormally && interactive != null && interactive.KeepsGameplayOnCompletion);
                    Stop(finishNormally);
                    if (reopenList) menu.OpenCutscenes();
                }
                return;
            }
            ApplyContentPause();
            if (Time.frameCount <= inputGuardFrame) return;
            if (gameplayActive && (menu.IsOpen || PauseMenuController.IsAnyPaused)) return;
            if (GameInput.MovementFocused)
            {
                if (!gameplayActive && (GameInput.WasPressed(GameInputAction.Cancel) || GameInput.WasPressed(GameInputAction.Pause))) ReturnToList();
                else if (debugPreview && GameInput.WasPressed(GameInputAction.CutscenePause)) TogglePause();
                else if (debugPreview && GameInput.WasPressed(GameInputAction.CutsceneRestart)) Restart();
                else if (AutomaticPlayback) Advance(gameplayActive ? Time.deltaTime : Time.unscaledDeltaTime);
            }
        }

        private void UpdateTimecode()
        {
            long milliseconds = (long)Math.Floor(ElapsedSeconds * 1000d + 0.000001d);
            if (milliseconds == displayedMilliseconds) return;
            displayedMilliseconds = milliseconds;
            long minutes = milliseconds / 60000;
            int minuteDigits = minutes >= 1000 ? 4 : minutes >= 100 ? 3 : 2;
            long value = minutes;
            for (int i = minuteDigits - 1; i >= 0; i--)
            {
                timecodeBuffer[i] = (char)('0' + value % 10);
                value /= 10;
            }
            timecodeBuffer[minuteDigits] = ':';
            timecodeBuffer[minuteDigits + 1] = (char)('0' + milliseconds / 10000 % 6);
            timecodeBuffer[minuteDigits + 2] = (char)('0' + milliseconds / 1000 % 10);
            timecodeBuffer[minuteDigits + 3] = '.';
            timecodeBuffer[minuteDigits + 4] = (char)('0' + milliseconds / 100 % 10);
            timecodeBuffer[minuteDigits + 5] = (char)('0' + milliseconds / 10 % 10);
            timecodeBuffer[minuteDigits + 6] = (char)('0' + milliseconds % 10);
            CurrentTimecode = new string(timecodeBuffer, 0, minuteDigits + 7);
        }

        private bool? contentPaused;
        private void ApplyContentPause()
        {
            bool paused = IsPlaybackPaused || !GameInput.MovementFocused ||
                (gameplayActive && GameTimeScaleRuntime.IsPaused);
            if (contentPaused == paused) return;
            contentPaused = paused;
            try { content?.SetPaused(paused); }
            catch (Exception exception)
            {
                GameLog.Warning("cutscene", "pause_failed", GameLog.Field("message", exception.Message));
                ReturnToList();
            }
        }

        private void ApplyInteractivePause()
        {
            if (!gameplayActive) return;
            if (IsPlaybackPaused && interactivePause == null)
            {
                interactivePreviousAudioPause = AudioListener.pause;
                interactivePause = GameTimeScaleRuntime.AcquirePause();
                AudioListener.pause = true;
            }
            else if (!IsPlaybackPaused && interactivePause != null)
                ReleaseInteractivePause();
        }

        private void ReleaseInteractivePause()
        {
            if (interactivePause == null) return;
            interactivePause.Dispose();
            interactivePause = null;
            // An ordinary pause can be layered over the debug pause.
            AudioListener.pause = PauseMenuController.IsAnyPaused || interactivePreviousAudioPause;
        }

        internal static Rect TimecodeRect => new Rect(510f, 332f, 120f, 20f);

        private void OnGUI()
        {
            if (!IsPlaying) return;
            if (timeStyle == null)
            {
                timeStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleRight, RetroUiTheme.Text);
                buttonStyle = RetroUiTheme.CreateButtonStyle(10, TextAnchor.MiddleCenter, RetroUiTheme.Text, false);
            }
            GUI.depth = -290;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(canvas);
            bool previousEnabled = GUI.enabled;
            try
            {
                RetroUiTheme.FillRect(TimecodeRect, RetroUiTheme.PanelInset);
                GUI.Label(TimecodeRect, CurrentTimecode, timeStyle);
                if (!debugPreview || menu.IsOpen) return;
                GUI.enabled = !returnPending && Time.frameCount > inputGuardFrame;
                if (GUI.Button(new Rect(270f, 333f, 72f, 18f), LocalizationService.Get(
                    IsPlaybackPaused ? "cutscene.resume" : "cutscene.pause"), buttonStyle)) TogglePause();
                if (GUI.Button(new Rect(346f, 333f, 76f, 18f), LocalizationService.Get("cutscene.restart"), buttonStyle)) Restart();
                if (GUI.Button(new Rect(426f, 333f, 76f, 18f), LocalizationService.Get("cutscene.back"), buttonStyle)) ReturnToList();
            }
            finally
            {
                GUI.enabled = previousEnabled;
                RetroUiTheme.EndCanvas(matrix);
            }
        }

        private void OnDisable() => Stop();
        private void OnDestroy()
        {
            Stop();
            Catalog.Clear();
        }
    }
}
