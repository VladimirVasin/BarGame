using UnityEngine;

namespace BarPromenade
{
    public enum BarKaraokePhase { Closed, Approaching, Preparing, Singing, Exiting }

    /// <summary>One owner for the shared hero, camera, live voice, mouse and trace lifecycle.</summary>
    [DefaultExecutionOrder(320)]
    public sealed class BarKaraokeInteraction : MonoBehaviour, IInteractable
    {
        public const float PerformanceReleaseSeconds = .9f;
        private BarInteriorRoot bar;
        private PlayerCameraFollow follow;
        private IntoxicationHudView hud;
        private PlayerAnimatedInteractionController animation;
        private Player3DCharacterPresentation presentation;
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private readonly ContextualCameraDirector director = new ContextualCameraDirector();
        private KaraokeControlState control;
        private int openedFrame, lastMouseRevision, completedFrame = -1;
        private bool restoring, ownsAnimation, cursorLocked, inputWasAvailable;
        private Vector3 promptPosition;
        private float silenceSeconds;
        private bool resetShotWhenAvailable;
        private BarKaraokeCameraSequence shots;

        public BarKaraokePlan Plan { get; private set; }
        public KaraokeVoice Voice { get; private set; }
        public BarKaraokeView View { get; private set; }
        public BarKaraokePhase Phase { get; private set; }
        public bool OwnsInteraction => Phase != BarKaraokePhase.Closed;
        public string LastFailureReason { get; private set; } = string.Empty;
        public string PromptKey => "interaction.karaoke";
        public Vector3 InteractionPosition => promptPosition;
        public ContextualCameraDirector CameraDirector => director;
        public BarKaraokeAudience Audience { get; private set; }
        public bool IsPerforming { get; private set; }
        public BarKaraokeCameraSequence CameraSequence => shots;
        public int CurrentCameraShot => shots != null ? shots.CurrentShotIndex : 0;
        public int CameraCutCount => shots != null ? shots.CutCount : 0;
        public float CameraShotProgress => shots != null ? shots.Progress : 0f;
        public float EffectiveFrequencyHz => control != null ? control.EffectiveFrequencyHz : 220f;
        public float DriftSemitones => control != null ? control.DriftSemitones : 0f;

        public void InitializeAudience()
        {
            if (Audience != null) return;
            Audience = gameObject.AddComponent<BarKaraokeAudience>();
            Audience.Initialize(bar);
        }

        public void Initialize(BarInteriorRoot owner, PlayerCameraFollow cameraFollow, IntoxicationHudView status)
        {
            bar = owner; follow = cameraFollow; hud = status;
            animation = owner.Player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            presentation = owner.Player.Visual as Player3DCharacterPresentation;
            promptPosition = owner.Room.TransformPoint(new Vector3(3.4f, .8f, -2.15f));
            transform.position = promptPosition;
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.3f, 1.6f, 1.4f);
            Voice = gameObject.AddComponent<KaraokeVoice>();
            View = gameObject.AddComponent<BarKaraokeView>();
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (bar == null || !isActiveAndEnabled || !bar.IsBusinessOpen || OwnsInteraction ||
                interactor != bar.Player.Interactor || !interactor.InputEnabled || animation == null ||
                !animation.isActiveAndEnabled || animation.IsActive || presentation == null ||
                BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning ||
                !GameInput.CanRead(GameInputContext.Contextual) ||
                bar.ArrivalPresentation != null && bar.ArrivalPresentation.IsPlaying) return false;
            Vector3 point = bar.Room.InverseTransformPoint(interactor.transform.position);
            // Admit only the open hall side of the existing podium, never through its back wall.
            return point.x >= 1.8f && point.x <= 4.65f && point.z >= -3.2f && point.z <= -.9f &&
                Mathf.Abs(point.y - BarInteriorLayoutPlanner.StageStandingPosition.y) <= .3f;
        }

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) BeginInteraction(); }

        public bool BeginInteraction()
        {
            if (bar == null || !CanInteract(bar.Player.Interactor)) return false;
            LastFailureReason = string.Empty;
            if (!PlayerKaraokeActions.TryAttach(presentation.Registry))
            { LastFailureReason = "The production karaoke action bank is missing or invalid."; return false; }
            Plan = new BarKaraokePlan(bar.Room);
            Voice.Initialize(presentation.Registry.Anchors.Head);
            if (!Voice.IsInitialized) { LastFailureReason = "The live hero voice is unavailable."; return false; }
            if (!modal.TryCaptureAndDisable(bar.Player.Interactor, follow, hud,
                    BarMinigameModalLockOptions.FullscreenWithPause)) return false;
            try
            {
                control = new KaraokeControlState();
                shots = new BarKaraokeCameraSequence(bar, Plan, director);
                silenceSeconds = 0f; resetShotWhenAvailable = false;
                inputWasAvailable = false;
                openedFrame = Time.frameCount; completedFrame = -1;
                Phase = BarKaraokePhase.Approaching;
                SetCursorLocked(true);
                lastMouseRevision = GameCursor.MouseLookRevision;
                ownsAnimation = animation.BeginPositioned(PlayerKaraokeActions.CreateDefinition(),
                    Plan.EntryPose, Plan.ActionPelvis, Plan.ExitPose, .3f,
                    Plan.ApproachWaypoints, Plan.ApproachWaypoints.Length);
                if (!ownsAnimation)
                { LastFailureReason = "The grounded karaoke approach was rejected."; Cleanup(); return false; }
                bar.RefreshMusicPlayback();
                return true;
            }
            catch { Cleanup(); throw; }
        }

        public void RequestExit()
        {
            if (!OwnsInteraction || Phase == BarKaraokePhase.Exiting) return;
            bool remoteShot = shots != null && (shots.CurrentKind == BarKaraokeShotKind.WashroomFace ||
                shots.CurrentKind == BarKaraokeShotKind.WashroomBowl);
            control?.Suspend();
            SetPerforming(false);
            if (remoteShot) shots.ReturnToStand();
            Voice.SetControl(EffectiveFrequencyHz, false);
            View.Close();
            presentation.ReleaseSpeechFace(this);
            if (animation.Phase == PlayerAnimatedInteractionPhase.Positioning)
            { Cleanup(); return; }
            Phase = BarKaraokePhase.Exiting;
            animation.RequestExitWithPoseTransition(1f, .15f);
            director.BeginExit();
        }

        private void Update()
        {
            if (!OwnsInteraction) return;
            if (bar == null || SceneTransitionService.IsTransitioning ||
                !animation.IsActive && Phase != BarKaraokePhase.Exiting)
            { Cleanup(); return; }
            if (!bar.IsBusinessOpen) RequestExit();
            if (!OwnsInteraction) return;
            SetCursorLocked(!PauseMenuController.IsAnyPaused);
            bool available = GameCursor.ApplicationFocused && GameInput.CanRead(GameInputContext.Contextual);
            bool inputResumed = available && !inputWasAvailable;
            inputWasAvailable = available;
            Voice.SetPaused(!available);
            if (!available) control.Suspend();
            if (available && Time.frameCount > openedFrame &&
                GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Contextual)) RequestExit();
            if (!OwnsInteraction) return;
            if (Phase == BarKaraokePhase.Approaching &&
                animation.Phase != PlayerAnimatedInteractionPhase.Positioning)
            {
                if (!director.BeginAuthored(follow, transform, bar.Player.GameObject.transform,
                        Plan.CameraPose, BarKaraokePlan.FieldOfView, Plan.CameraFocus,
                        BarKaraokePlan.CameraSeconds, BarKaraokePlan.CameraSeconds, true))
                { LastFailureReason = "The karaoke camera is obstructed or already owned."; RequestExit(); return; }
                Phase = BarKaraokePhase.Preparing;
            }
            if (Phase == BarKaraokePhase.Preparing && director.IsSettled &&
                animation.Phase == PlayerAnimatedInteractionPhase.Looping)
            {
                Phase = BarKaraokePhase.Singing;
                control.Suspend();
                View.Open();
            }
            if (Phase == BarKaraokePhase.Singing)
            {
                bool mouseChanged = lastMouseRevision != GameCursor.MouseLookRevision;
                lastMouseRevision = GameCursor.MouseLookRevision;
                float delta = mouseChanged || inputResumed ? 0f : GameInput.ReadContextualMouseDelta().y /
                    Mathf.Max(200f, Screen.height * .65f);
                control.Advance(delta, GameInput.ContextualMouseHeld, available);
                control.AdvanceDrift(Time.unscaledDeltaTime, GameTimeScaleRuntime.SmoothedAudioLevel, available);
                Voice.SetControl(control.EffectiveFrequencyHz, control.IsSinging);
                AdvancePerformance(Time.unscaledDeltaTime, available);
                if (available) View.Sample(Time.unscaledDeltaTime,
                    Voice.Envelope > .015f ? Voice.SmoothedFrequencyHz : control.EffectiveFrequencyHz, Voice.Envelope);
            }
        }

        private void AdvancePerformance(float seconds, bool available)
        {
            if (!available)
            {
                SetPerforming(false);
                resetShotWhenAvailable = true;
                return;
            }
            if (resetShotWhenAvailable)
            {
                shots.ReturnToStand();
                resetShotWhenAvailable = false;
            }
            if (control.IsSinging)
            {
                if (!IsPerforming) SetPerforming(true);
                silenceSeconds = 0f;
            }
            else if (IsPerforming)
            {
                silenceSeconds += seconds;
                if (silenceSeconds >= PerformanceReleaseSeconds)
                {
                    SetPerforming(false);
                    shots.ReturnToStand();
                }
            }
            if (IsPerforming) shots.Advance(seconds);
        }

        private void SetPerforming(bool value)
        {
            if (IsPerforming == value) return;
            IsPerforming = value;
            if (value) shots?.BeginPerformance();
            else { silenceSeconds = 0f; shots?.Suspend(); }
            Audience?.SetPerforming(value);
            bar?.Soundscape?.SetKaraokeSilenced(value);
        }

        private void LateUpdate()
        {
            if (!OwnsInteraction) return;
            director.Tick(Time.deltaTime);
            if (Phase == BarKaraokePhase.Singing)
            {
                bool sounding = GameCursor.ApplicationFocused && !GameTimeScaleRuntime.IsPaused && Voice.Envelope > .025f;
                presentation.TrySetSpeechFace(this, new SpeechFacePose(
                    sounding ? SpeechMouthPose.Open : SpeechMouthPose.Closed, SpeechFaceExpression.Rest));
            }
            if (Phase == BarKaraokePhase.Exiting && !animation.IsActive && director.IsFinished)
            {
                if (completedFrame < 0) completedFrame = Time.frameCount;
                else if (Time.frameCount > completedFrame) Cleanup();
            }
        }

        public void CancelInteraction() => Cleanup();
        private void Cleanup()
        {
            if (restoring) return;
            restoring = true;
            Phase = BarKaraokePhase.Closed;
            SetPerforming(false);
            Voice?.Stop(); View?.Close(); control?.Suspend();
            if (presentation != null) presentation.ReleaseSpeechFace(this);
            if (ownsAnimation && animation != null) animation.CancelActiveInteraction();
            ownsAnimation = false;
            shots?.Dispose(); shots = null;
            director.RestoreImmediate();
            GameCursor.Release(this);
            cursorLocked = false;
            modal.Restore();
            if (bar != null) bar.RefreshMusicPlayback();
            restoring = false;
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!OwnsInteraction) return;
            control?.Suspend(); inputWasAvailable = false; Voice?.SetPaused(!focused);
            if (!focused) { SetPerforming(false); resetShotWhenAvailable = true; }
        }
        private void SetCursorLocked(bool value)
        {
            if (cursorLocked == value) return;
            cursorLocked = value;
            if (value) GameCursor.Capture(this, CursorLockMode.Locked, false);
            else GameCursor.Release(this);
        }
        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();
    }
}
