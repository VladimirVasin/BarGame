using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One first-person owner spans selection and the existing positioned hand operations.</summary>
    [DefaultExecutionOrder(450)]
    [DisallowMultipleComponent]
    public sealed class StairwellLabyrinthPuzzleModeController : MonoBehaviour
    {
        private const int ControlsPerStage = 4;
        private const float CameraBlendSeconds = .35f;
        public const float FieldOfView = 78f;
        private readonly StairwellLabyrinthPuzzleInteraction[] stageControls = new StairwellLabyrinthPuzzleInteraction[ControlsPerStage];
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private StairwellLabyrinthPuzzles puzzles;
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController animation;
        private Player3DAssetRegistry registry;
        private StairwellInteriorRoot host;
        private PlayerCameraFollow cameraFollow;
        private IDisposable hiddenPlayer;
        private WorldControlOutlineView outline;
        private Pose cameraStart, returnStart, previousFixedPose;
        private Vector3 eyeOffset, focus;
        private float cameraElapsed, previousLens;
        private int selectedIndex, openingFrame;
        private bool wasFixed, returning, firstPersonPresented, releasePresented, restoring;
        private Func<bool> operatePrompt;
        private string shownPromptKey;

        public bool IsActive { get; private set; }
        public bool IsSettled => IsActive && !returning && firstPersonPresented;
        public bool IsOperating => IsActive && SelectedControl != null && SelectedControl.OwnsActiveInteraction;
        public int Stage { get; private set; }
        public StairwellLabyrinthPuzzleInteraction SelectedControl => IsActive ? stageControls[selectedIndex] : null;
        public int RenderedOutlineCount => IsSettled && !GameTimeScaleRuntime.IsPaused && !puzzles.State.CanPassGate(Stage)
            ? outline?.RenderedPartCount ?? 0 : 0;
        public Vector3 FirstPersonPosition => registry != null && registry.Anchors.Mouth != null && registry.Anchors.Head != null
            ? registry.Anchors.Mouth.position + registry.Anchors.Head.rotation * eyeOffset : Vector3.zero;
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
            registry = runtime.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            cameraFollow = host != null ? host.CameraFollow : null;
            if (cameraFollow == null || host.FixedCamera == null || registry?.Anchors.Head == null || registry.Anchors.Mouth == null)
                throw new InvalidOperationException("A labyrinth puzzle mode requires the existing camera and registered eye anchors.");
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
                if (count == ControlsPerStage) { host.FixedCamera.ReleasePuzzleView(this); return false; }
                stageControls[count++] = candidate;
            }
            if (count != ControlsPerStage || !modal.TryCaptureAndDisable(player.Interactor, cameraFollow,
                    host.IntoxicationHud, BarMinigameModalLockOptions.FullscreenWithPause))
            {
                Array.Clear(stageControls, 0, stageControls.Length);
                host.FixedCamera.ReleasePuzzleView(this);
                return false;
            }
            try
            {
                Stage = control.Stage;
                selectedIndex = Array.IndexOf(stageControls, control);
                wasFixed = cameraFollow.FixedPoseActive;
                previousFixedPose = cameraFollow.FixedBasePose;
                previousLens = cameraFollow.Camera.fieldOfView;
                cameraStart = new Pose(cameraFollow.Camera.transform.position, cameraFollow.Camera.transform.rotation);
                eyeOffset = Quaternion.Inverse(registry.Anchors.Head.rotation) * host.transform.up * .068f;
                focus = Vector3.zero;
                foreach (var candidate in stageControls) focus += candidate.CameraFocusPosition;
                focus /= ControlsPerStage;
                cameraElapsed = 0f;
                returning = firstPersonPresented = releasePresented = false;
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
            selectedIndex = (selectedIndex + (delta < 0 ? ControlsPerStage - 1 : 1)) % ControlsPerStage;
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
            if (!IsActive || cameraFollow == null || registry == null) return;
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
            Vector3 eye = FirstPersonPosition;
            Vector3 direction = focus - eye;
            Quaternion rotation = direction.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(direction, host.transform.up) : cameraStart.rotation;
            cameraFollow.SetFixedPose(Vector3.Lerp(cameraStart.position, eye, amount),
                Quaternion.Slerp(cameraStart.rotation, rotation, amount), Mathf.Lerp(previousLens, FieldOfView, amount));
            if (amount >= .88f && hiddenPlayer == null) hiddenPlayer = player.PresentationVisibility.AcquireHidden(this);
            firstPersonPresented = cameraElapsed >= CameraBlendSeconds;
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
                IsActive = returning = firstPersonPresented = releasePresented = restoring = false;
                Array.Clear(stageControls, 0, stageControls.Length);
            }
        }
        private void OnDisable() => Restore();
        private void OnDestroy() { Restore(); outline?.Dispose(); outline = null; }
    }
}
