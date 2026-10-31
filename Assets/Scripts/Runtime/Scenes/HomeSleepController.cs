using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public enum HomeSleepPhase { Idle, LyingDown, FaceHold, Nightmare, Cut, Startle, Waking, Recovering, Returning, AwaitingStandUp, OpeningSleep }

    /// <summary>Owns the ordinary bed's finite camera sequence; the shared action owns the rig.</summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class HomeSleepController : MonoBehaviour
    {
        public const float PushSeconds = 2.5f;
        public const float FaceHoldSeconds = 3f;
        public const float NightmareSeconds = 1.2f;
        public const float CutSeconds = 2.1f;
        public const float MontageLeadSeconds = .1f;
        public const float MontageBeatSeconds = .3f;
        public const int MontageBeatCount = 6;
        public const float StartleSeconds = .65f;
        public const float CameraReturnSeconds = 6.8f;
        public const float FaceCameraDistanceScale = 1.45f;
        private const float CutCommitSeconds = 1.92f;
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private readonly ContextualCameraDirector cameraDirector = new ContextualCameraDirector();
        private readonly HomeSleepAudioCue.Sequence bedAudio = new HomeSleepAudioCue.Sequence();
        private HomeInteriorRoot home;
        private Transform head;
        private Player3DCharacterPresentation hero;
        private float elapsed;
        private float wakeElapsed;
        private float seatedIdleElapsed, idleExitElapsed;
        private int cutFrame;
        private bool cameraAcquired;
        private bool committed;
        private bool ownsFace;
        private PlayerFacialExpression appliedFace;
        private int montageStartLevel, montageBeat;
        private bool montageBeatRevealed;
        private bool wakeCompleted;
        private bool cameraReturned;
        private Func<IInteractable, bool> standUpFilter;
        private Vector3 trackedFace, faceVelocity, wakeStartOffset, wakeStartUp;
        private bool faceTrackingInitialized;
        private bool normalOpening;
        private HomeOpeningCameraSequence openingShots;

        public HomeSleepPhase Phase { get; private set; }
        public bool IsActive => Phase != HomeSleepPhase.Idle;
        public HomeSleepAudioCue.Sequence BedAudio => bedAudio;
        public float PhaseElapsedSeconds => elapsed;
        public bool IsCameraReturning => cameraAcquired && cameraDirector.IsReturning;
        public bool WaitingForStandUp => Phase == HomeSleepPhase.AwaitingStandUp;
        public bool WaitingForWake => Phase == HomeSleepPhase.OpeningSleep && cameraAcquired;
        public bool IsNormalOpening => normalOpening && IsActive;
        public int OpeningShotIndex => openingShots != null ? openingShots.CurrentShotIndex : -1;
        public int OpeningShotCount => HomeOpeningCameraSequence.ShotCount;
        private PlayerFacialExpression AwakeExpression => normalOpening
            ? PlayerFacialExpression.Neutral : PlayerFacialExpression.Tense;
        public bool IsScreenDark
        {
            get
            {
                if (Phase != HomeSleepPhase.Cut) return false;
                float montage = elapsed - MontageLeadSeconds;
                if (montage < 0f || montage >= MontageBeatCount * MontageBeatSeconds) return true;
                float beat = montage % MontageBeatSeconds;
                return beat < .055f || beat >= .28f;
            }
        }

        public void Initialize(HomeInteriorRoot root)
        {
            if (home != null && home.AnimatedInteraction != null)
                home.AnimatedInteraction.InteractionCompleted -= HandleBedCompleted;
            home = root != null ? root : throw new ArgumentNullException(nameof(root));
            hero = home.Player.Visual as Player3DCharacterPresentation;
            head = hero != null ? hero.Registry.Anchors.Head : null;
            if (head == null) throw new InvalidOperationException("Sleep requires the production hero's head anchor.");
            home.Bed.BindSleepPresentation(this);
            standUpFilter = candidate => ReferenceEquals(candidate, home.Bed);
            home.AnimatedInteraction.InteractionCompleted += HandleBedCompleted;
        }

        public bool TryBegin() => Begin(false);
        public bool TryBeginOpening() => Begin(true);

        private bool Begin(bool opening)
        {
            if (!isActiveAndEnabled || IsActive || home == null || head == null || home.Bed == null ||
                SceneTransitionService.IsTransitioning ||
                !modal.TryCaptureAndDisable(home.Player.Interactor, home.CameraFollow,
                    home.IntoxicationHud, BarMinigameModalLockOptions.FullscreenWithPause)) return false;
            normalOpening = opening;
            Phase = opening ? HomeSleepPhase.OpeningSleep : HomeSleepPhase.LyingDown;
            elapsed = 0f;
            wakeElapsed = 0f;
            seatedIdleElapsed = idleExitElapsed = 0f;
            bedAudio.Reset();
            faceTrackingInitialized = false;
            faceVelocity = Vector3.zero;
            committed = cameraAcquired = false;
            ownsFace = false;
            wakeCompleted = false;
            cameraReturned = false;
            montageStartLevel = Mathf.Max(GameSessionState.HeroCorruptionLevel,
                hero.CorruptionAppearance.AppliedLevel);
            montageBeat = -1;
            montageBeatRevealed = false;
            try
            {
                if (opening ? home.Bed.BeginSleeping() : home.Bed.BeginPositionedSleep())
                {
                    if (!opening) return true;
                    openingShots = new HomeOpeningCameraSequence(home, cameraDirector, head);
                    cameraAcquired = openingShots.Begin();
                    if (cameraAcquired && SetFace(PlayerFacialExpression.ClosedBlink))
                    {
                        home.Player.Interactor.SetInteractionFilter(this, standUpFilter);
                        home.Player.Interactor.SetInputEnabled(true);
                        return true;
                    }
                    Restore(true);
                    return false;
                }
                Restore(false);
                return false;
            }
            catch
            {
                Restore(true);
                throw;
            }
        }

        private void LateUpdate()
        {
            if (!IsActive) return;
            if (home == null || head == null || home.Bed == null || !home.Bed.isActiveAndEnabled ||
                SceneTransitionService.IsTransitioning ||
                (!home.Bed.OwnsActiveInteraction && !wakeCompleted))
            { Restore(true); return; }
            bool paused = PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused;
            HomeSleepAudioCue.SetPaused(home.Audio, this, paused);
            if (paused) return;
            float dt = Time.deltaTime;
            if (wakeCompleted)
            {
                Restore(false);
                return;
            }
            bedAudio.Tick(home, this, Phase, normalOpening, wakeElapsed);
            switch (Phase)
            {
                case HomeSleepPhase.OpeningSleep:
                    elapsed += dt;
                    if (!openingShots.Advance(dt)) Restore(true);
                    break;
                case HomeSleepPhase.LyingDown:
                    if (home.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Positioning) return;
                    if (!cameraAcquired && home.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Entering &&
                        home.AnimatedInteraction.PhaseProgress < .2f) return;
                    if (!UpdateFaceCamera(dt)) { Restore(true); return; }
                    if (cameraDirector.IsSettled && home.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping)
                    {
                        if (!SetFace(PlayerFacialExpression.ClosedBlink)) { Restore(true); return; }
                        Phase = HomeSleepPhase.FaceHold; elapsed = 0f;
                    }
                    break;
                case HomeSleepPhase.FaceHold:
                    if (!UpdateFaceCamera(dt)) { Restore(true); return; }
                    elapsed += dt;
                    if (elapsed >= FaceHoldSeconds)
                    { Phase = HomeSleepPhase.Nightmare; elapsed = 0f; }
                    break;
                case HomeSleepPhase.Nightmare:
                    elapsed += dt;
                    float nightmare = Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / NightmareSeconds));
                    if (!SetBody(nightmare, 0f, elapsed) || !UpdateFaceCamera(dt)) { Restore(true); return; }
                    if (elapsed >= NightmareSeconds)
                    {
                        Phase = HomeSleepPhase.Cut; elapsed = 0f; cutFrame = Time.frameCount;
                        HomeSleepAudioCue.Trigger(home.Audio, this);
                    }
                    break;
                case HomeSleepPhase.Cut:
                    // At least one actually rendered dark frame precedes the atomic handoff,
                    // even if a hitch crosses the complete nominal cut duration.
                    if (Time.frameCount <= cutFrame) return;
                    elapsed += dt;
                    float nextBeat = MontageLeadSeconds + (montageBeat + 1) * MontageBeatSeconds;
                    float reveal = MontageLeadSeconds + montageBeat * MontageBeatSeconds + .08f;
                    if (montageBeat >= 0 && !montageBeatRevealed && elapsed >= reveal)
                    {
                        // The frame after a hitch must reveal this form before
                        // the next covered stage can replace it.
                        elapsed = reveal;
                        montageBeatRevealed = true;
                    }
                    else if (montageBeat < MontageBeatCount - 1 && elapsed >= nextBeat)
                    {
                        // Even a hitch presents every authored stage under a
                        // covered boundary, rather than skipping to the last.
                        montageBeat++;
                        montageBeatRevealed = false;
                        elapsed = nextBeat;
                        int level = montageStartLevel + Mathf.CeilToInt(
                            (HeroCorruptionRules.LastLevel - montageStartLevel) *
                            ((montageBeat + 1f) / MontageBeatCount));
                        if (hero.CorruptionAppearance.AppliedLevel != level)
                        {
                            hero.CorruptionAppearance.ApplyLevel(level);
                            hero.TrySetContextualFacialExpression(this, appliedFace);
                        }
                    }
                    if (!SetBody(.3f * (1f - Mathf.Clamp01(elapsed / CutCommitSeconds)), 0f,
                            NightmareSeconds + elapsed) || !UpdateFaceCamera(dt))
                    { Restore(true); return; }
                    if (!committed && elapsed >= CutCommitSeconds)
                    {
                        if (!home.ApartmentDays.CommitSleepAppearance(home.Bed)) { Restore(true); return; }
                        committed = true;
                        // Keep a committed dark frame before revealing the waking room.
                        elapsed = CutCommitSeconds;
                        return;
                    }
                    if (committed && elapsed >= CutSeconds)
                    {
                        Phase = HomeSleepPhase.Startle; elapsed = 0f;
                        if (!SetFace(PlayerFacialExpression.Horror) || !SetBody(0f, 0f, 0f))
                        { Restore(true); return; }
                    }
                    break;
                case HomeSleepPhase.Startle:
                    elapsed += dt;
                    float startle = Mathf.Sin(Mathf.PI * Mathf.Clamp01(elapsed / .24f));
                    if (!SetBody(0f, 0f, elapsed, startle) || !UpdateFaceCamera(dt))
                    { Restore(true); return; }
                    if (elapsed >= StartleSeconds)
                    {
                        if (!home.Bed.RequestStartledWake()) { Restore(true); return; }
                        SetFace(AwakeExpression);
                        // The lens belongs to the room, not to the animated
                        // skull: a flinch must not roll or whip the camera.
                        wakeStartOffset = cameraDirector.CurrentShotPose.position - trackedFace;
                        wakeStartUp = cameraDirector.CurrentShotPose.up;
                        Phase = HomeSleepPhase.Waking; elapsed = wakeElapsed = 0f;
                    }
                    break;
                case HomeSleepPhase.Waking:
                case HomeSleepPhase.Recovering:
                    wakeElapsed += dt;
                    if (home.Bed.WakeRecoveryActive)
                    {
                        Phase = HomeSleepPhase.Recovering;
                        float recovery = home.Bed.WakeRecoveryProgress;
                        elapsed = recovery * HomeBedInteraction.WakeRecoveryHoldSeconds;
                        float envelope = Mathf.SmoothStep(0f, 1f, recovery / .18f) *
                            (1f - Mathf.SmoothStep(0f, 1f, (recovery - .78f) / .22f));
                        if (!SetBody(0f, envelope, wakeElapsed)) { Restore(true); return; }
                        SetFace(recovery > .30f && recovery < .43f
                            ? PlayerFacialExpression.ClosedBlink : AwakeExpression);
                    }
                    else
                    {
                        Phase = HomeSleepPhase.Waking;
                        elapsed = wakeElapsed;
                        if (cameraReturned)
                        {
                            seatedIdleElapsed += dt;
                            idleExitElapsed += dt;
                            if (!SetBody(0f, 0f, seatedIdleElapsed,
                                seatedIdle: 1f - Mathf.SmoothStep(0f, 1f, idleExitElapsed / .35f)))
                            { Restore(true); return; }
                        }
                        else if (!SetBody(0f, 0f, wakeElapsed)) { Restore(true); return; }
                    }
                    if (!cameraReturned)
                    {
                        bool framed = normalOpening
                            ? openingShots.AdvanceWake(dt, home.AnimatedInteraction.ClipProgress)
                            : UpdateFaceCamera(dt, waking: true);
                        if (!framed) { Restore(true); return; }
                        if (home.Bed.WakeRecoveryActive && home.Bed.WakeRecoveryProgress >= 1f)
                        {
                            // Keep the authored seat held throughout the full
                            // ordinary-duration camera return, then await E.
                            HomeCameraShot destination = home.FixedCamera.ActiveShot;
                            if (!cameraDirector.BeginExitToFixedShot(
                                    new Pose(destination.Position, destination.Rotation),
                                    destination.FieldOfView, destination.Focus))
                            { Restore(true); return; }
                            Phase = HomeSleepPhase.Returning;
                            elapsed = 0f;
                        }
                    }
                    break;
                case HomeSleepPhase.Returning:
                    elapsed += dt;
                    if (!UpdateSeatedIdle(dt)) { Restore(true); return; }
                    cameraDirector.Tick(dt);
                    if (cameraDirector.IsFinished)
                    {
                        cameraAcquired = false;
                        cameraReturned = true;
                        home.FixedCamera.RestoreGameplayCamera();
                        home.Player.Interactor.SetInteractionFilter(this, standUpFilter);
                        home.Player.Interactor.SetInputEnabled(true);
                        Phase = HomeSleepPhase.AwaitingStandUp;
                        elapsed = 0f;
                    }
                    break;
                case HomeSleepPhase.AwaitingStandUp:
                    if (!UpdateSeatedIdle(dt)) { Restore(true); return; }
                    break;
            }
        }

        public bool TryStandUp()
        {
            if (!WaitingForStandUp || !GameInput.CanRead(GameInputContext.Contextual) ||
                !home.Bed.RequestStandUpFromRecovery()) return false;
            home.Player.Interactor.SetInputEnabled(false);
            idleExitElapsed = 0f;
            SetFace(AwakeExpression);
            Phase = HomeSleepPhase.Waking;
            return true;
        }

        public bool TryWakeOpening()
        {
            if (!WaitingForWake || !GameInput.CanRead(GameInputContext.Contextual) ||
                GameSessionState.IsGameTimeRunning || !home.Bed.RequestSeatedWake(false)) return false;
            GameSessionState.TryStartPreparedGameTime();
            home.Player.Interactor.SetInputEnabled(false);
            SetFace(PlayerFacialExpression.Neutral);
            Phase = HomeSleepPhase.Waking;
            elapsed = wakeElapsed = 0f;
            return true;
        }

        public bool ApplyOpeningShot(int index, float progress) => WaitingForWake && openingShots.Apply(index, progress);
        public void CancelOpening() { if (normalOpening && IsActive) Restore(true); }

        private void HandleBedCompleted()
        {
            if (Phase == HomeSleepPhase.Waking || Phase == HomeSleepPhase.Recovering)
                wakeCompleted = true;
        }

        private bool SetFace(PlayerFacialExpression expression)
        {
            if (ownsFace && appliedFace == expression) return true;
            if (hero == null || !hero.TrySetContextualFacialExpression(this, expression)) return false;
            ownsFace = true;
            appliedFace = expression;
            return true;
        }

        private bool SetBody(float nightmare, float recovery, float seconds, float startle = 0f,
            float seatedIdle = 0f) => hero != null && hero.TrySetSleepBodyPresentation(
                this, nightmare, recovery, seconds, startle, seatedIdle);

        private bool UpdateSeatedIdle(float dt)
        {
            seatedIdleElapsed += dt;
            float blink = seatedIdleElapsed % 4.7f;
            return SetBody(0f, 0f, seatedIdleElapsed,
                    seatedIdle: Mathf.SmoothStep(0f, 1f, seatedIdleElapsed / .6f)) &&
                SetFace(blink >= 3.2f && blink < 3.34f
                    ? PlayerFacialExpression.ClosedBlink : AwakeExpression);
        }

        private bool UpdateFaceCamera(float dt, bool waking = false)
        {
            Vector3 footward = home.Room.TransformDirection(home.BedInteractionPlan.HeadToFootAxis).normalized;
            Vector3 roomSide = home.Room.right;
            float reframe = waking ? SmoothCameraProgress(wakeElapsed / 1.2f) : 0f;
            Vector3 target = head.position + Vector3.up * .065f;
            Transform mouth = hero.Registry.Anchors.Mouth;
            if (waking && mouth != null)
                target = Vector3.Lerp(target, mouth.position + Vector3.up * .065f, reframe);
            if (!faceTrackingInitialized)
            {
                trackedFace = target;
                faceTrackingInitialized = true;
            }
            trackedFace = Vector3.SmoothDamp(trackedFace, target, ref faceVelocity,
                waking ? .045f : .12f, Mathf.Infinity, dt);
            Vector3 focus = trackedFace;
            Vector3 position = focus + (Vector3.up * .53f + roomSide * .23f + footward * .04f) *
                FaceCameraDistanceScale;
            Pose pose = new Pose(position, Quaternion.LookRotation(focus - position, -footward));
            float fov = 38f;
            if (waking)
            {
                Vector3 offset = Vector3.Lerp(wakeStartOffset,
                    roomSide + Vector3.up * .13f + footward * .13f, reframe);
                position = focus + offset;
                pose = new Pose(position, Quaternion.LookRotation(focus - position,
                    Vector3.Slerp(wakeStartUp, Vector3.up, reframe)));
                fov = Mathf.Lerp(38f, 42f, reframe);
            }
            else if (Phase == HomeSleepPhase.LyingDown)
            {
                // First reveal the supported body, then approach the face as
                // the shoulders settle. Both moves fit the existing BedEnter.
                Vector3 bed = home.Room.TransformPoint(home.BedInteractionPlan.ActionHipPosition);
                Vector3 mediumFocus = bed + Vector3.up * .24f;
                Vector3 mediumPosition = bed + roomSide * 2.35f + Vector3.up * 1.15f - footward * .25f;
                float close = SmoothCameraProgress((home.AnimatedInteraction.ClipProgress - .70f) / .30f);
                focus = Vector3.Lerp(mediumFocus, focus, close);
                position = Vector3.Lerp(mediumPosition, position, close);
                pose = new Pose(position, Quaternion.LookRotation(focus - position,
                    Vector3.Slerp(Vector3.up, -footward, close)));
                fov = Mathf.Lerp(50f, fov, close);
            }
            if (!cameraAcquired)
            {
                cameraAcquired = cameraDirector.BeginAuthored(home.CameraFollow, head,
                    home.Player.GameObject.transform, pose, fov, focus, PushSeconds, CameraReturnSeconds, true, .035f);
                if (!cameraAcquired) return false;
            }
            if (!cameraDirector.TrySetAuthoredShot(pose, fov, focus,
                    sightSubject: head, nearClipPlane: .035f)) return false;
            cameraDirector.Tick(dt);
            return true;
        }

        private static float SmoothCameraProgress(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private void OnGUI()
        {
            if (Phase != HomeSleepPhase.Cut || Event.current.type != EventType.Repaint) return;
            int depth = GUI.depth;
            Color color = GUI.color;
            // Keep the translucent pause card above the covered handoff.
            GUI.depth = PauseMenuController.IsAnyPaused ? -290 : int.MinValue;
            GUI.color = IsScreenDark ? Color.black : new Color(.75f, .82f, .78f, .16f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.depth = depth;
        }

        private void Restore(bool cancelBed)
        {
            if (home != null) HomeSleepAudioCue.Stop(home.Audio, this);
            bedAudio.Reset();
            if (!IsActive) return;
            if (normalOpening && !GameSessionState.IsGameTimeRunning &&
                !SceneTransitionService.IsTransitioning && SceneManager.GetActiveScene().name == SceneIds.HomeInterior)
                GameSessionState.TryStartPreparedGameTime();
            Phase = HomeSleepPhase.Idle;
            wakeCompleted = false;
            elapsed = 0f;
            hero?.ReleaseSleepBodyPresentation(this);
            hero?.ReleaseContextualFacialExpression(this);
            ownsFace = false;
            if (!committed && hero != null && hero.CorruptionAppearance != null)
                hero.CorruptionAppearance.ApplyLevel(GameSessionState.HeroCorruptionLevel);
            if (cancelBed && home != null && home.Bed != null && home.Bed.OwnsActiveInteraction)
                home.AnimatedInteraction.CancelActiveInteraction();
            if (home != null) home.Player.Interactor.SetInteractionFilter(this, null);
            if (cameraAcquired) cameraDirector.RestoreImmediate();
            cameraAcquired = false;
            openingShots = null;
            modal.Restore();
            if (home != null && home.FixedCamera != null) home.FixedCamera.RestoreGameplayCamera();
        }

        private void OnDisable() => Restore(true);
        private void OnDestroy()
        {
            if (home != null && home.AnimatedInteraction != null)
                home.AnimatedInteraction.InteractionCompleted -= HandleBedCompleted;
            Restore(true);
        }
    }
}
