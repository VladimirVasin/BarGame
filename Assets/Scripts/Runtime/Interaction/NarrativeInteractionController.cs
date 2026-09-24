using UnityEngine;

namespace BarPromenade
{
    public enum NarrativeInteractionPhase { Idle, Positioning, Framing, Reading, Exiting }

    /// <summary>One reusable owner of a silent inspection's rig, camera, pages and input.</summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class NarrativeInteractionController : MonoBehaviour
    {
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private readonly ContextualCameraDirector director = new ContextualCameraDirector();
        private PlayerInteractor listener;
        private PlayerAnimatedInteractionController animation;
        private PlayerCameraFollow follow;
        private InteractionPromptView view;
        private bool ownsAnimation, cameraStarted, exitingAnimation, confirmArmed, cursorCaptured;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private int pageFrame, animationFinishedFrame;
        public NarrativeInteraction Target { get; private set; }
        public NarrativeInteractionPhase Phase { get; private set; }
        public bool IsActive => Phase != NarrativeInteractionPhase.Idle;
        public int PageIndex { get; private set; }
        public NarrativePage CurrentPage => Target != null ? Target.Definition.Pages[PageIndex] : default;
        public ContextualCameraDirector CameraDirector => director;
        public string LastFailureReason { get; private set; } = string.Empty;

        public static NarrativeInteractionController For(PlayerInteractor interactor)
        {
            if (interactor == null) return null;
            var controller = interactor.GetComponent<NarrativeInteractionController>();
            return controller != null ? controller : interactor.gameObject.AddComponent<NarrativeInteractionController>();
        }

        public bool Begin(NarrativeInteraction target, PlayerInteractor interactor)
        {
            LastFailureReason = string.Empty;
            if (IsActive || Paused || !isActiveAndEnabled || target == null || interactor == null ||
                !target.CanInteract(interactor) || BarMinigameModalLock.IsAnyLocked)
            { LastFailureReason = "Inspection admission refused"; return false; }
            var registry = interactor.GetComponentInChildren<Player3DAssetRegistry>();
            animation = interactor.GetComponent<PlayerAnimatedInteractionController>();
            follow = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow>() : null;
            view = interactor.PromptView;
            if (registry == null || animation == null || !animation.IsInitialized || !animation.isActiveAndEnabled ||
                animation.IsActive || follow == null || view == null || !view.isActiveAndEnabled || view.HasHeldPage ||
                !PlayerDialogueActions.TryAttach(registry))
            { LastFailureReason = "Hero, camera, animation bank or lower UI unavailable"; return false; }
            if (!modal.TryCaptureAndDisable(interactor, follow, FindAnyObjectByType<IntoxicationHudView>()))
            { LastFailureReason = "Another modal owns input"; return false; }
            listener = interactor; Target = target; PageIndex = 0;
            LastFailureReason = string.Empty;
            Phase = NarrativeInteractionPhase.Positioning;
            ownsAnimation = cameraStarted = exitingAnimation = confirmArmed = false;
            animationFinishedFrame = -1;
            previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible; cursorCaptured = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
            try
            {
                NarrativeStagingPlan staging = target.Staging;
                ownsAnimation = animation.BeginPositioned(PlayerDialogueActions.CreateListeningDefinition(),
                    staging.Entry, staging.ActionHip, staging.Exit, staging.InitialVerticalTolerance);
                if (!ownsAnimation)
                {
                    LastFailureReason = "Positioned Begin rejected; height delta=" +
                        Mathf.Abs(interactor.transform.position.y - staging.Entry.RootPosition.y).ToString("F3") +
                        ", tolerance=" + staging.InitialVerticalTolerance.ToString("F3");
                    RestoreImmediate(); return false;
                }
                target.SetSession(this);
                return true;
            }
            catch { RestoreImmediate(); throw; }
        }

        public bool Confirm()
        {
            if (Phase != NarrativeInteractionPhase.Reading || Paused || Time.frameCount <= pageFrame ||
                !GameInput.CanRead(GameInputContext.Menu)) return false;
            if (PageIndex + 1 == Target.Definition.Pages.Count) Cancel();
            else { PageIndex++; ShowPage(); }
            return true;
        }

        public void Cancel()
        {
            if (!IsActive || Phase == NarrativeInteractionPhase.Exiting) return;
            if (view != null) view.ReleaseHeldPage(this);
            Phase = NarrativeInteractionPhase.Exiting;
            Cursor.visible = false;
            if (ownsAnimation && animation != null && animation.Phase == PlayerAnimatedInteractionPhase.Positioning)
                animation.CancelActiveInteraction();
            AdvanceExit();
        }

        private void Update()
        {
            if (!IsActive) return;
            if (Target == null || !Target.isActiveAndEnabled || Target.SubjectRoot == null ||
                !Target.SubjectRoot.gameObject.activeInHierarchy || listener == null || !listener.isActiveAndEnabled ||
                animation == null || !animation.isActiveAndEnabled || follow == null || !follow.isActiveAndEnabled ||
                view == null || !view.isActiveAndEnabled || SceneTransitionService.IsTransitioning)
            { RestoreImmediate(); return; }
            if (Paused) return;
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Menu)) Cancel();
            switch (Phase)
            {
                case NarrativeInteractionPhase.Positioning:
                    if (!animation.IsActive)
                    { LastFailureReason = "Positioned approach did not reach the authored standing pose"; RestoreImmediate(); return; }
                    if (animation.Phase == PlayerAnimatedInteractionPhase.Entering || animation.Phase == PlayerAnimatedInteractionPhase.Looping)
                    {
                        cameraStarted = director.BeginObject(follow, Target.SubjectRoot, Target.Staging.FocusBounds,
                            listener.transform, Target.Staging.CameraSideHint, Target.Staging.CameraMode, Target.Staging.CameraFront);
                        if (!cameraStarted)
                        { LastFailureReason = "Object framing rejected: " + director.LastRejectedShotReason; Cancel(); return; }
                        Phase = NarrativeInteractionPhase.Framing;
                    }
                    break;
                case NarrativeInteractionPhase.Framing:
                    if (director.IsSettled && animation.Phase == PlayerAnimatedInteractionPhase.Looping) ShowPage();
                    break;
                case NarrativeInteractionPhase.Reading:
                    if (!view.IsHeldBy(this)) { RestoreImmediate(); return; }
                    if (Time.frameCount > pageFrame)
                    {
                        if (!confirmArmed) confirmArmed = !GameInput.IsHeld(GameInputAction.Interact, GameInputContext.Menu);
                        if (confirmArmed && GameInput.WasPressed(GameInputAction.Interact, GameInputContext.Menu)) Confirm();
                    }
                    break;
                case NarrativeInteractionPhase.Exiting:
                    AdvanceExit();
                    break;
            }
            if (IsActive && Phase != NarrativeInteractionPhase.Exiting && !animation.IsActive) RestoreImmediate();
        }

        private void ShowPage()
        {
            NarrativePage page = CurrentPage;
            string heading = !string.IsNullOrWhiteSpace(page.AttributionKey) ? page.AttributionKey :
                page.Kind == NarrativePageKind.DocumentText ? "narrative.page.document" : "narrative.page.thought";
            string controls = PageIndex + 1 < Target.Definition.Pages.Count ? "narrative.controls.next" : "narrative.controls.close";
            if (!view.TryHoldPage(this, page.TextKey, heading, controls, Confirm)) { Cancel(); return; }
            Phase = NarrativeInteractionPhase.Reading; pageFrame = Time.frameCount;
            confirmArmed = false; Cursor.visible = true;
        }

        private void AdvanceExit()
        {
            if (cameraStarted && !director.IsReturning && !director.IsFinished) director.BeginExit();
            if (ownsAnimation && animation != null && animation.IsActive)
            {
                if (!exitingAnimation && animation.Phase == PlayerAnimatedInteractionPhase.Looping)
                    exitingAnimation = animation.RequestExitWithPoseTransition(1f, PlayerDialogueActions.ExitPlaybackSeconds);
                return;
            }
            if (animationFinishedFrame < 0) animationFinishedFrame = Time.frameCount;
        }

        private void LateUpdate()
        {
            if (!IsActive || Paused) return;
            if (cameraStarted)
            {
                director.Tick(Time.deltaTime);
                if (Phase != NarrativeInteractionPhase.Exiting &&
                    (director.IsFinished || director.IsSettled && !director.CurrentShotIsClear))
                { LastFailureReason = "Object shot lost: " + director.LastRejectedShotReason; Cancel(); return; }
            }
            if (Phase == NarrativeInteractionPhase.Exiting && animationFinishedFrame >= 0 &&
                Time.frameCount > animationFinishedFrame && (!cameraStarted || director.IsFinished)) RestoreImmediate();
        }

        public void RestoreImmediate()
        {
            if (!IsActive && !modal.IsLocked) return;
            Phase = NarrativeInteractionPhase.Idle;
            if (view != null) view.ReleaseHeldPage(this);
            if (ownsAnimation && animation != null) animation.CancelActiveInteraction();
            ownsAnimation = false;
            director.RestoreImmediate(); cameraStarted = false;
            if (cursorCaptured)
            { Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible; cursorCaptured = false; }
            modal.Restore();
            Target = null; listener = null; animation = null; follow = null; view = null;
        }

        private static bool Paused => PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
        private void OnDisable() => RestoreImmediate();
        private void OnDestroy() => RestoreImmediate();
    }
}
