using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One frontal inspection owner spans selection and the existing positioned hand operations.</summary>
    [DefaultExecutionOrder(450)]
    [DisallowMultipleComponent]
    public sealed class StairwellLabyrinthPuzzleModeController : MonoBehaviour
    {
        private const int MaximumStageControls = 4;
        private const float CameraBlendSeconds = .35f;
        public const float FieldOfView = 56f;
        private readonly StairwellLabyrinthPuzzleInteraction[] stageControls = new StairwellLabyrinthPuzzleInteraction[MaximumStageControls];
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private StairwellLabyrinthPuzzles puzzles;
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController animation;
        private StairwellInteriorRoot host;
        private PlayerCameraFollow cameraFollow;
        private IDisposable hiddenPlayer;
        private WorldControlOutlineView outline;
        private Pose cameraStart, returnStart, previousFixedPose;
        private float cameraElapsed, previousLens;
        private int selectedIndex, openingFrame, controlCount;
        private bool wasFixed, returning, inspectionPresented, releasePresented, restoring;
        private Func<bool> operatePrompt;
        private string shownPromptKey;

        public bool IsActive { get; private set; }
        public bool IsSettled => IsActive && !returning && inspectionPresented;
        public bool IsOperating => IsActive && SelectedControl != null && SelectedControl.OwnsActiveInteraction;
        public int Stage { get; private set; }
        public StairwellLabyrinthPuzzleInteraction SelectedControl => IsActive ? stageControls[selectedIndex] : null;
        public int RenderedOutlineCount => IsSettled && !GameTimeScaleRuntime.IsPaused && !puzzles.State.CanPassGate(Stage)
            ? outline?.RenderedPartCount ?? 0 : 0;
        public Pose InspectionPose => Stage > 0 && puzzles != null && cameraFollow != null
            ? puzzles.GetStageViewPose(Stage, cameraFollow.Camera.aspect) : default;
        public Bounds InspectionBounds => Stage > 0 && puzzles != null ? puzzles.GetStageViewBounds(Stage) : default;
        public Vector3 InspectionForward => InspectionPose.rotation * Vector3.forward;
        // Historical capture callers use the inspection position as their settled-camera seam.
        public Vector3 FirstPersonPosition => InspectionPose.position;
        public bool IsPlayerHidden => hiddenPlayer != null;
        public bool IsHeadHidden => IsPlayerHidden;

        internal void Initialize(StairwellLabyrinthPuzzles owner, PlayerRuntime runtime, PlayerAnimatedInteractionController shared)
        {
            if (puzzles != null || owner == null || runtime.GameObject == null || shared == null)
                throw new ArgumentException("Initialize a labyrinth puzzle mode once with its production hero.");
            puzzles = owner;
            player = runtime;
            animation = shared;
            host = runtime.GameObject.GetComponentInParent<StairwellInteriorRoot>();
            cameraFollow = host != null ? host.CameraFollow : null;
            if (cameraFollow == null || host.FixedCamera == null)
                throw new InvalidOperationException("A labyrinth puzzle mode requires the existing gameplay camera.");
            outline = new WorldControlOutlineView();
            operatePrompt = OperateSelected;
        }

        public bool Begin(StairwellLabyrinthPuzzleInteraction control)
        {
            if (!isActiveAndEnabled || IsActive || puzzles == null || control == null ||
                !control.CanInteract(player.Interactor) || !host.FixedCamera.TryAcquirePuzzleView(this)) return false;
            int count = 0;
            foreach (var candidate in puzzles.Controls)
            {
                if (candidate.Stage != control.Stage) continue;
                if (count == MaximumStageControls) { host.FixedCamera.ReleasePuzzleView(this); return false; }
                stageControls[count++] = candidate;
            }
            if (count != (control.Stage == 2 ? 3 : 4) || !modal.TryCaptureAndDisable(player.Interactor, cameraFollow,
                    host.IntoxicationHud, BarMinigameModalLockOptions.FullscreenWithPause))
            {
                Array.Clear(stageControls, 0, stageControls.Length);
                host.FixedCamera.ReleasePuzzleView(this);
                return false;
            }
            try
            {
                Stage = control.Stage;
                controlCount = count;
                selectedIndex = Array.IndexOf(stageControls, control);
                wasFixed = cameraFollow.FixedPoseActive;
                previousFixedPose = cameraFollow.FixedBasePose;
                previousLens = cameraFollow.Camera.fieldOfView;
                cameraStart = new Pose(cameraFollow.Camera.transform.position, cameraFollow.Camera.transform.rotation);
                cameraElapsed = 0f;
                returning = inspectionPresented = releasePresented = false;
                openingFrame = Time.frameCount;
                IsActive = true;
                GameCursor.Capture(this, CursorLockMode.Locked, false);
                outline.Select(puzzles.GetControlRenderers(control));
                return true;
            }
            catch { Restore(); throw; }
        }

        internal bool Owns(StairwellLabyrinthPuzzleInteraction control) => IsSettled && !IsOperating &&
            control == SelectedControl && GameCursor.ApplicationFocused && !GameTimeScaleRuntime.IsPaused &&
            !SceneTransitionService.IsTransitioning;

        public bool Select(int delta)
        {
            if (!IsSettled || IsOperating || delta == 0 || !GameCursor.ApplicationFocused ||
                !GameInput.CanRead(GameInputContext.Contextual) || puzzles.State.CanPassGate(Stage)) return false;
            selectedIndex = (selectedIndex + (delta < 0 ? controlCount - 1 : 1)) % controlCount;
            outline.Select(puzzles.GetControlRenderers(SelectedControl));
            RefreshPrompt();
            return true;
        }

        public bool OperateSelected() => IsSettled && !IsOperating &&
            SelectedControl != null && SelectedControl.BeginFromPuzzleMode(this);

        public bool RequestExit()
        {
            if (!IsActive || returning) return false;
            SelectedControl?.Cancel();
            returnStart = new Pose(cameraFollow.Camera.transform.position, cameraFollow.Camera.transform.rotation);
            cameraElapsed = 0f;
            returning = true;
            outline.Clear();
            ReleasePrompt();
            return true;
        }

        public bool Cancel()
        {
            if (!IsActive) return false;
            Restore();
            return true;
        }

        private void Update()
        {
            if (!IsActive) return;
            if (puzzles == null || !puzzles.isActiveAndEnabled || player.GameObject == null || animation == null ||
                !animation.isActiveAndEnabled || cameraFollow == null || SceneTransitionService.IsTransitioning)
            { Restore(); return; }
            if (releasePresented) { Restore(); return; }
            if (GameTimeScaleRuntime.IsPaused || !GameCursor.ApplicationFocused) return;
            cameraElapsed = Mathf.Min(CameraBlendSeconds, cameraElapsed + Mathf.Max(0f, Time.deltaTime));
            if (DebugFreeCamera.IsInspecting(player.GameObject)) { ReleasePrompt(); return; }
            RefreshPrompt();
            if (Time.frameCount <= openingFrame || returning) return;
            if (GameInput.WasPressed(GameInputAction.PuzzleExit, GameInputContext.Contextual)) { RequestExit(); return; }
            if (!IsSettled || IsOperating) return;
            if (GameInput.WasPressed(GameInputAction.PuzzlePreviousControl, GameInputContext.Contextual)) Select(-1);
            else if (GameInput.WasPressed(GameInputAction.PuzzleNextControl, GameInputContext.Contextual)) Select(1);
            if (GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Contextual)) OperateSelected();
        }

        private void LateUpdate()
        {
            if (!IsActive || cameraFollow == null) return;
            if (DebugFreeCamera.IsInspecting(player.GameObject)) { RestorePlayer(); outline.Hide(); return; }
            float amount = Mathf.SmoothStep(0f, 1f, cameraElapsed / CameraBlendSeconds);
            if (returning)
            {
                Pose target = wasFixed ? previousFixedPose : cameraFollow.ResolveReleasePose();
                cameraFollow.SetFixedPose(Vector3.Lerp(returnStart.position, target.position, amount),
                    Quaternion.Slerp(returnStart.rotation, target.rotation, amount), Mathf.Lerp(FieldOfView, previousLens, amount));
                if (amount > .1f) RestorePlayer();
                releasePresented = cameraElapsed >= CameraBlendSeconds;
                return;
            }
            Pose inspection = InspectionPose;
            cameraFollow.SetFixedPose(Vector3.Lerp(cameraStart.position, inspection.position, amount),
                Quaternion.Slerp(cameraStart.rotation, inspection.rotation, amount), Mathf.Lerp(previousLens, FieldOfView, amount));
            if (amount >= .88f && hiddenPlayer == null) hiddenPlayer = player.PresentationVisibility.AcquireHidden(this);
            inspectionPresented = cameraElapsed >= CameraBlendSeconds;
            if (IsSettled && !puzzles.State.CanPassGate(Stage) && !GameTimeScaleRuntime.IsPaused)
                outline.Present(cameraFollow.Camera);
            else outline.Hide();
        }

        private void RefreshPrompt()
        {
            if (!IsSettled || host == null || host.InteractionPrompt == null) return;
            bool solved = puzzles.State.CanPassGate(Stage);
            string key = solved ? "ui.labyrinth_exit" : SelectedControl.ActionPromptKey;
            if (key == shownPromptKey && host.InteractionPrompt.IsHeldBy(this)) return;
            if (!host.InteractionPrompt.TryHoldPage(this, key, null, solved ? null : "ui.labyrinth_controls",
                solved ? null : operatePrompt, compact: true, requiresRunningGameTime: false)) { Restore(); return; }
            shownPromptKey = key;
        }

        private void ReleasePrompt()
        {
            if (host != null && host.InteractionPrompt != null) host.InteractionPrompt.ReleaseHeldPage(this);
            shownPromptKey = null;
        }

        private void RestorePlayer() { hiddenPlayer?.Dispose(); hiddenPlayer = null; }
        private void Restore()
        {
            if (restoring || !IsActive && !modal.IsLocked) return;
            restoring = true;
            try
            {
                SelectedControl?.Cancel();
                RestorePlayer();
                outline?.Clear();
                ReleasePrompt();
                if (cameraFollow != null)
                {
                    if (wasFixed) cameraFollow.SetFixedPose(previousFixedPose.position, previousFixedPose.rotation, previousLens);
                    else if (releasePresented && !DebugFreeCamera.IsInspecting(player.GameObject)) cameraFollow.ReleaseFixedPoseToOrbit();
                    else cameraFollow.ClearFixedPose();
                }
            }
            finally
            {
                if (host != null && host.FixedCamera != null) host.FixedCamera.ReleasePuzzleView(this);
                modal.Restore();
                GameCursor.Release(this);
                IsActive = returning = inspectionPresented = releasePresented = restoring = false;
                Array.Clear(stageControls, 0, stageControls.Length);
            }
        }
        private void OnDisable() => Restore();
        private void OnDestroy() { Restore(); outline?.Dispose(); outline = null; }
    }
}
