using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct DialogueParticipant
    {
        public readonly UnityEngine.Object Owner;
        public readonly Transform Root, Head;
        public readonly string Voice;
        public DialogueParticipant(UnityEngine.Object owner, Transform root, Transform head, string voice)
        { Owner = owner; Root = root; Head = head; Voice = voice; }
        public bool IsPresent => Owner != null && Root != null && Head != null &&
            Root.gameObject.activeInHierarchy && Head.gameObject.activeInHierarchy;
    }

    public readonly struct DialogueStagingPlan
    {
        public readonly PlayerAnimatedInteractionPose Entry, Exit;
        public readonly Vector3 ActionHip;
        public DialogueStagingPlan(PlayerAnimatedInteractionPose entry, Vector3 actionHip,
            PlayerAnimatedInteractionPose exit)
        { Entry = entry; ActionHip = actionHip; Exit = exit; }
    }

    public enum DialoguePhase { Idle, Positioning, Framing, PreparingLine, Speaking, FinishingGesture, Choosing, Exiting }

    /// <summary>One owner for a positioned conversation, shared speech, camera, rig and input.</summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class DialogueSessionController : MonoBehaviour
    {
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private readonly DialogueCameraDirector director = new DialogueCameraDirector();
        private DialogueCursor cursor;
        private DialogueParticipant npc, hero;
        private PlayerInteractor listener;
        private PlayerAnimatedInteractionController animation;
        private PlayerCameraFollow follow;
        private NpcSpeechBubbleView bubbles;
        private ISpeechFaceActor heroFace, npcFace;
        private Action<bool> setNpcSpeaking;
        private Action finished;
        private bool cameraStarted, confirmArmed, cursorCaptured, preparingTalk, exitingAnimation;
        private CursorLockMode savedCursorLock;
        private bool savedCursorVisible;
        private float clock;
        private int choiceFrame, animationFinishedFrame;
        private GUIStyle optionStyle, selectedStyle;
        public DialoguePhase Phase { get; private set; }
        public bool IsActive => Phase != DialoguePhase.Idle;
        public bool IsChoosing => Phase == DialoguePhase.Choosing;
        public int SelectedChoice { get; private set; }
        public DialogueNode CurrentNode => cursor?.Current;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public DialogueCameraDirector CameraDirector => director;
        public DialogueStagingPlan Staging { get; private set; }
        public Transform HeroHead => hero.Head;
        public float SpeechClock => clock;
        public Rect LastChoicePanelRect { get; private set; }

        public bool Begin(DialogueGraph graph, DialogueStagingPlan staging, DialogueParticipant participant,
            PlayerInteractor interactor, Action<bool> speaking, Action completed)
        {
            if (IsActive || !isActiveAndEnabled || graph == null || !participant.IsPresent ||
                interactor == null || !interactor.isActiveAndEnabled || !interactor.InputEnabled ||
                BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning) return false;
            var registry = interactor.GetComponentInChildren<Player3DAssetRegistry>();
            animation = interactor.GetComponent<PlayerAnimatedInteractionController>();
            follow = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow>() : null;
            if (registry == null || registry.Anchors.Head == null || animation == null ||
                !animation.IsInitialized || !animation.isActiveAndEnabled || animation.IsActive || follow == null ||
                !PlayerDialogueActions.TryAttach(registry)) return false;
            if (!modal.TryCaptureAndDisable(interactor, follow,
                FindFirstObjectByType<IntoxicationHudView>())) return false;
            listener = interactor; npc = participant;
            heroFace = interactor.GetComponentInChildren<Player3DCharacterPresentation>();
            npcFace = participant.Owner as ISpeechFaceActor;
            hero = new DialogueParticipant(interactor, interactor.transform, registry.Anchors.Head,
                NpcVoiceCatalog.HeroMutterDesignId);
            setNpcSpeaking = speaking; finished = completed;
            Staging = staging; cursor = new DialogueCursor(graph); clock = 0f;
            Phase = DialoguePhase.Positioning; cameraStarted = preparingTalk = exitingAnimation = false;
            animationFinishedFrame = -1;
            savedCursorLock = Cursor.lockState; savedCursorVisible = Cursor.visible; cursorCaptured = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = false;
            try
            {
                if (bubbles == null)
                {
                    var host = new GameObject("Dialogue speech");
                    host.transform.SetParent(transform, false);
                    bubbles = host.AddComponent<NpcSpeechBubbleView>();
                    bubbles.UseManualClock = true;
                }
                bubbles.Initialize(follow.Camera, interactor.transform);
                if (!bubbles.DeclareSpeaker(npc.Owner, npc.Head, npc.Voice, NpcEarshotProfile.Conversation) ||
                    !bubbles.DeclareSpeaker(hero.Owner, hero.Head, hero.Voice, NpcEarshotProfile.Conversation) ||
                    !animation.BeginPositioned(PlayerDialogueActions.CreateListeningDefinition(),
                        staging.Entry, staging.ActionHip, staging.Exit))
                { RestoreImmediate(); return false; }
                setNpcSpeaking?.Invoke(false);
                return true;
            }
            catch { RestoreImmediate(); throw; }
        }

        public bool SelectChoice(int index)
        {
            if (!IsChoosing || index < 0 || index >= cursor.Current.Choices.Count) return false;
            SelectedChoice = index; return true;
        }

        public bool Confirm()
        {
            if (!IsChoosing || Time.frameCount <= choiceFrame || !GameInput.CanRead(GameInputContext.Menu)) return false;
            if (!cursor.Select(SelectedChoice)) return false;
            PrepareNode();
            return true;
        }

        public void Cancel()
        {
            if (!IsActive || Phase == DialoguePhase.Exiting) return;
            bubbles?.DismissAll(); setNpcSpeaking?.Invoke(false);
            ReleaseFaces();
            Cursor.visible = false;
            Phase = DialoguePhase.Exiting;
            if (animation != null && animation.Phase == PlayerAnimatedInteractionPhase.Positioning)
            { animation.CancelActiveInteraction(); }
            AdvanceExit();
        }

        private void Update()
        {
            if (!IsActive) return;
            if (!npc.IsPresent || !hero.IsPresent || listener == null || !listener.isActiveAndEnabled ||
                animation == null || !animation.isActiveAndEnabled || follow == null ||
                SceneTransitionService.IsTransitioning || Vector3.Distance(hero.Root.position, npc.Root.position) > 3.5f)
            { RestoreImmediate(); return; }
            bool paused = PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning;
            bubbles.RenderEnabled = !paused;
            if (paused) return;
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Menu)) Cancel();
            clock += Time.deltaTime;
            bubbles.AdvanceTo(clock);
            switch (Phase)
            {
                case DialoguePhase.Positioning:
                    if (!animation.IsActive) { RestoreImmediate(); return; }
                    if (animation.Phase == PlayerAnimatedInteractionPhase.Entering ||
                        animation.Phase == PlayerAnimatedInteractionPhase.Looping)
                    {
                        cameraStarted = director.Begin(follow, npc.Root, npc.Head, hero.Root, hero.Head);
                        if (!cameraStarted) { Cancel(); return; }
                        Phase = DialoguePhase.Framing;
                    }
                    break;
                case DialoguePhase.Framing:
                    if (director.IsSettled && animation.Phase == PlayerAnimatedInteractionPhase.Looping) PrepareNode();
                    break;
                case DialoguePhase.PreparingLine:
                    if (preparingTalk && animation.NestedLoopActionPhase != PlayerAnimatedInteractionPhase.Looping) break;
                    TrySpeak();
                    break;
                case DialoguePhase.Speaking:
                    DialogueParticipant speaker = cursor.Current.Speaker == DialogueSpeaker.Hero ? hero : npc;
                    if (bubbles.IsShowing(speaker.Owner)) break;
                    setNpcSpeaking?.Invoke(false);
                    if (preparingTalk)
                    {
                        animation.RequestNestedLoopActionExit();
                        Phase = DialoguePhase.FinishingGesture;
                    }
                    else { cursor.CompleteLine(); PrepareNode(); }
                    break;
                case DialoguePhase.FinishingGesture:
                    if (!animation.IsNestedLoopActionActive)
                    { preparingTalk = false; cursor.CompleteLine(); PrepareNode(); }
                    break;
                case DialoguePhase.Choosing:
                    ReadChoices();
                    break;
                case DialoguePhase.Exiting:
                    AdvanceExit();
                    break;
            }
            if (IsActive && Phase != DialoguePhase.Exiting && !animation.IsActive) RestoreImmediate();
        }

        private void PrepareNode()
        {
            DialogueNode node = cursor.Current;
            preparingTalk = false;
            if (node.Kind == DialogueNodeKind.End) { Cancel(); return; }
            director.Focus(node.Speaker == DialogueSpeaker.Hero);
            if (node.Kind == DialogueNodeKind.Choice)
            {
                Cursor.visible = true;
                Phase = DialoguePhase.Choosing; SelectedChoice = 0;
                choiceFrame = Time.frameCount; confirmArmed = false;
                return;
            }
            Phase = DialoguePhase.PreparingLine;
            Cursor.visible = false;
            if (node.Speaker == DialogueSpeaker.Hero)
            {
                preparingTalk = animation.BeginNestedLoopAction(PlayerDialogueActions.CreateTalkingDefinition(), holdLoop: true);
                if (!preparingTalk) Cancel();
            }
        }

        private void TrySpeak()
        {
            DialogueNode node = cursor.Current;
            DialogueParticipant speaker = node.Speaker == DialogueSpeaker.Hero ? hero : npc;
            string text = LocalizationService.Get(node.TextKey);
            if (!bubbles.ShowAt(speaker.Owner, text, clock,
                SpeechDelivery.ResolveSpokenDuration(text, SpeechDelivery.ReadingTailSeconds))) return;
            setNpcSpeaking?.Invoke(node.Speaker == DialogueSpeaker.Npc);
            Phase = DialoguePhase.Speaking;
        }

        private void ReadChoices()
        {
            if (Time.frameCount <= choiceFrame || !GameInput.CanRead(GameInputContext.Menu)) return;
            if (!confirmArmed) confirmArmed = !GameInput.IsHeld(GameInputAction.Confirm, GameInputContext.Menu);
            int delta = GameInput.ReadMenuSelectionDelta(GameInputContext.Menu);
            if (delta != 0)
            {
                int count = cursor.Current.Choices.Count;
                SelectedChoice = (SelectedChoice + (delta > 0 ? 1 : count - 1)) % count;
            }
            if (confirmArmed && GameInput.WasPressed(GameInputAction.Confirm, GameInputContext.Menu)) Confirm();
        }

        private void AdvanceExit()
        {
            // Return the camera while the body settles, not after another full idle cycle.
            if (cameraStarted && !director.IsReturning && !director.IsFinished) director.BeginExit();
            if (animation.IsActive)
            {
                if (!exitingAnimation && animation.Phase == PlayerAnimatedInteractionPhase.Looping)
                    exitingAnimation = animation.RequestExitWithPoseTransition(1f, PlayerDialogueActions.ExitPlaybackSeconds);
                return;
            }
            if (animationFinishedFrame < 0)
            {
                animationFinishedFrame = Time.frameCount;
            }
        }

        private void LateUpdate()
        {
            if (!IsActive || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || !GameSessionState.IsGameTimeRunning) return;
            if (Phase != DialoguePhase.Positioning && Phase != DialoguePhase.Exiting)
            {
                if (!PresentFace(heroFace, hero.Owner, SpeechFaceProfile.Hero) ||
                    !PresentFace(npcFace, npc.Owner, SpeechFaceProfile.Foreman))
                { Cancel(); return; }
            }
            if (cameraStarted)
            {
                director.Tick(Time.deltaTime);
                if (Phase != DialoguePhase.Exiting && director.IsSettled && !director.CurrentShotIsClear)
                { RestoreImmediate(); return; }
            }
            if (Phase == DialoguePhase.Exiting && animationFinishedFrame >= 0 &&
                Time.frameCount > animationFinishedFrame && (!cameraStarted || director.IsFinished)) RestoreImmediate();
        }

        public void RestoreImmediate()
        {
            if (!IsActive && !modal.IsLocked) return;
            Action callback = finished; finished = null;
            Phase = DialoguePhase.Idle;
            ReleaseFaces(); heroFace = npcFace = null;
            bubbles?.DismissAll();
            bubbles?.WithdrawSpeaker(npc.Owner); bubbles?.WithdrawSpeaker(hero.Owner);
            setNpcSpeaking?.Invoke(false); setNpcSpeaking = null;
            animation?.CancelActiveInteraction();
            director.RestoreImmediate(); cameraStarted = false;
            if (cursorCaptured)
            { Cursor.lockState = savedCursorLock; Cursor.visible = savedCursorVisible; cursorCaptured = false; }
            modal.Restore();
            listener = null; hero = npc = default; cursor = null;
            callback?.Invoke();
        }

        private bool PresentFace(ISpeechFaceActor actor, UnityEngine.Object owner, SpeechFaceProfile profile)
        {
            if (actor == null) return true;
            SpeechFacePose pose = bubbles.TryGetSpeechFaceSample(owner, out SpeechFaceSample sample)
                ? SpeechFaceAnimation.Resolve(sample, profile, clock)
                : SpeechFaceAnimation.ResolveListening(profile, clock);
            return actor.TrySetSpeechFace(this, pose);
        }

        private void ReleaseFaces()
        {
            if (heroFace is UnityEngine.Object heroObject && heroObject != null) heroFace.ReleaseSpeechFace(this);
            if (npcFace is UnityEngine.Object npcObject && npcObject != null) npcFace.ReleaseSpeechFace(this);
        }

        private void OnGUI()
        {
            if (!IsChoosing || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            if (optionStyle == null)
            {
                // A text-only hit target avoids inherited editor skin's scaled button textures.
                optionStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.Text);
                selectedStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.SelectionText);
            }
            int previousDepth = GUI.depth; bool enabled = GUI.enabled;
            GUI.depth = -90;
            GUI.enabled = enabled && Time.frameCount > choiceFrame && GameInput.CanRead(GameInputContext.Menu);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(RetroUiTheme.CalculateCanvas(Screen.width, Screen.height));
            try
            {
                int count = cursor.Current.Choices.Count, visible = Mathf.Min(4, count);
                int first = Mathf.Clamp(SelectedChoice - visible + 1, 0, count - visible);
                float height = visible * 28f + 16f;
                Rect panel = new Rect((RetroUiTheme.LogicalWidth - 280f) * .5f,
                    RetroUiTheme.LogicalHeight - height - 18f, 280f, height);
                LastChoicePanelRect = panel;
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Panel, RetroUiTheme.BorderMuted, false, 0f, 1f);
                for (int row = 0; row < visible; row++)
                {
                    int index = first + row;
                    Rect rect = new Rect(panel.x + 10f, panel.y + 8f + row * 28f, 260f, 26f);
                    if (GUI.enabled && Event.current.type == EventType.MouseMove && rect.Contains(Event.current.mousePosition))
                        SelectedChoice = index;
                    bool selected = index == SelectedChoice;
                    RetroUiTheme.DrawSelection(rect, selected);
                    GUI.Label(new Rect(rect.x + 7f, rect.y, rect.width - 14f, rect.height),
                        LocalizationService.Get(cursor.Current.Choices[index].TextKey), selected ? selectedStyle : optionStyle);
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    { SelectChoice(index); Confirm(); break; }
                }
            }
            finally { RetroUiTheme.EndCanvas(matrix); GUI.depth = previousDepth; GUI.enabled = enabled; }
        }
        private void OnDisable() => RestoreImmediate();
        private void OnDestroy() => RestoreImmediate();
    }
}
