using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeSleepPhase { Idle, LyingDown, FaceHold, Cut, Waking }

    /// <summary>Owns the ordinary bed's finite camera sequence; the shared action owns the rig.</summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class HomeSleepController : MonoBehaviour
    {
        public const float PushSeconds = 3f;
        public const float FaceHoldSeconds = 3f;
        public const float CutSeconds = .44f;
        private const float CutCommitSeconds = .29f;
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private readonly ContextualCameraDirector cameraDirector = new ContextualCameraDirector();
        private HomeInteriorRoot home;
        private Transform head;
        private float elapsed;
        private int cutFrame;
        private bool cameraAcquired;
        private bool committed;

        public HomeSleepPhase Phase { get; private set; }
        public bool IsActive => Phase != HomeSleepPhase.Idle;
        public float PhaseElapsedSeconds => elapsed;
        public bool IsScreenDark => Phase == HomeSleepPhase.Cut &&
            (elapsed < .10f || elapsed >= .16f && elapsed < .23f || elapsed >= CutCommitSeconds);

        public void Initialize(HomeInteriorRoot root)
        {
            home = root != null ? root : throw new ArgumentNullException(nameof(root));
            var hero = home.Player.Visual as Player3DCharacterPresentation;
            head = hero != null ? hero.Registry.Anchors.Head : null;
            if (head == null) throw new InvalidOperationException("Sleep requires the production hero's head anchor.");
            home.Bed.BindSleepPresentation(this);
        }

        public bool TryBegin()
        {
            if (!isActiveAndEnabled || IsActive || home == null || head == null || home.Bed == null ||
                SceneTransitionService.IsTransitioning ||
                !modal.TryCaptureAndDisable(home.Player.Interactor, home.CameraFollow,
                    home.IntoxicationHud, BarMinigameModalLockOptions.FullscreenWithPause)) return false;
            Phase = HomeSleepPhase.LyingDown;
            elapsed = 0f;
            committed = cameraAcquired = false;
            try
            {
                if (home.Bed.BeginPositionedSleep()) return true;
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
                SceneTransitionService.IsTransitioning || !home.Bed.OwnsActiveInteraction)
            { Restore(true); return; }
            if (PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            float dt = Time.deltaTime;
            switch (Phase)
            {
                case HomeSleepPhase.LyingDown:
                    if (home.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Positioning) return;
                    if (!cameraAcquired && home.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Entering &&
                        home.AnimatedInteraction.PhaseProgress < .4f) return;
                    if (!UpdateFaceCamera(dt)) { Restore(true); return; }
                    if (cameraDirector.IsSettled && home.AnimatedInteraction.Phase == PlayerAnimatedInteractionPhase.Looping)
                    { Phase = HomeSleepPhase.FaceHold; elapsed = 0f; }
                    break;
                case HomeSleepPhase.FaceHold:
                    if (!UpdateFaceCamera(dt)) { Restore(true); return; }
                    elapsed += dt;
                    if (elapsed >= FaceHoldSeconds)
                    { Phase = HomeSleepPhase.Cut; elapsed = 0f; cutFrame = Time.frameCount; }
                    break;
                case HomeSleepPhase.Cut:
                    // At least one actually rendered dark frame precedes the atomic handoff,
                    // even if a hitch crosses the complete nominal cut duration.
                    if (Time.frameCount <= cutFrame) return;
                    elapsed += dt;
                    if (!committed && elapsed >= CutCommitSeconds)
                    {
                        if (!home.ApartmentDays.CommitSleepAppearance(home.Bed)) { Restore(true); return; }
                        committed = true;
                        cameraDirector.RestoreImmediate();
                        cameraAcquired = false;
                        home.FixedCamera.RestoreGameplayCamera();
                        if (!home.Bed.RequestAbruptWake()) { Restore(true); return; }
                        // Keep a committed dark frame before revealing the waking room.
                        elapsed = CutCommitSeconds;
                        return;
                    }
                    if (committed && elapsed >= CutSeconds)
                    { Phase = HomeSleepPhase.Waking; elapsed = 0f; }
                    break;
                case HomeSleepPhase.Waking:
                    elapsed += dt;
                    break;
            }
        }

        private bool UpdateFaceCamera(float dt)
        {
            Vector3 focus = head.position + Vector3.up * .065f;
            Vector3 footward = home.Room.TransformDirection(home.BedInteractionPlan.HeadToFootAxis).normalized;
            Vector3 position = focus + Vector3.up * .53f + Vector3.right * .23f + footward * .04f;
            Pose pose = new Pose(position, Quaternion.LookRotation(focus - position, -footward));
            if (!cameraAcquired)
            {
                cameraAcquired = cameraDirector.BeginAuthored(home.CameraFollow, head,
                    home.Player.GameObject.transform, pose, 38f, focus, PushSeconds, .01f, true, .035f);
                if (!cameraAcquired) return false;
            }
            if (!cameraDirector.TrySetAuthoredShot(pose, 38f, focus, sightSubject: head, nearClipPlane: .035f)) return false;
            cameraDirector.Tick(dt);
            return true;
        }

        private void OnGUI()
        {
            if (!IsScreenDark || Event.current.type != EventType.Repaint) return;
            int depth = GUI.depth;
            Color color = GUI.color;
            // Keep the translucent pause card above the covered handoff.
            GUI.depth = PauseMenuController.IsAnyPaused ? -290 : int.MinValue;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = color;
            GUI.depth = depth;
        }

        private void Restore(bool cancelBed)
        {
            if (!IsActive) return;
            Phase = HomeSleepPhase.Idle;
            elapsed = 0f;
            if (cancelBed && home != null && home.Bed != null && home.Bed.OwnsActiveInteraction)
                home.AnimatedInteraction.CancelActiveInteraction();
            if (cameraAcquired) cameraDirector.RestoreImmediate();
            cameraAcquired = false;
            modal.Restore();
            if (home != null && home.FixedCamera != null) home.FixedCamera.RestoreGameplayCamera();
        }

        private void OnDisable() => Restore(true);
        private void OnDestroy() => Restore(true);
    }
}
