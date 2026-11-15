using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct DialogueParticipant
    {
        public readonly UnityEngine.Object Owner;
        public readonly Transform Root, Head;
        public readonly NpcSpeaker Speech;
        public readonly ISpeechFaceActor Face;
        public readonly SpeechFaceProfile FaceProfile;
        public DialogueParticipant(UnityEngine.Object owner, Transform root, Transform head, string voice,
            SpeechFaceProfile faceProfile = SpeechFaceProfile.Foreman)
            : this(new NpcSpeaker(owner, head, voice, NpcEarshotProfile.Conversation), root, owner as ISpeechFaceActor,
                faceProfile) { }

        internal DialogueParticipant(in NpcSpeaker speech, Transform root, ISpeechFaceActor face, SpeechFaceProfile profile)
        {
            Owner = speech.Owner;
            Root = root;
            Head = speech.Anchor;
            Speech = speech;
            Face = face;
            FaceProfile = profile;
        }
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

    public enum DialoguePhase { Idle, Positioning, Framing, PreparingLine, Speaking, FinishingGesture, Choosing, Exiting, Presenting }

    internal interface IDialogueIntroduction
    {
        // True after the action has returned to the dialogue's grounded pose.
        bool Present(float seconds, out bool failed);
        void Release();
    }

    /// <summary>One owner for a positioned conversation, shared speech, camera, rig and input.</summary>
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class DialogueSessionController : MonoBehaviour
    {
        private readonly BarMinigameModalLock modal = new BarMinigameModalLock();
        private readonly DialogueCameraDirector director = new DialogueCameraDirector();
        private DialogueCursor cursor;
        private DialogueParticipant npc, secondNpc, hero;
        private PlayerInteractor listener;
        private PlayerAnimatedInteractionController animation;
        private PlayerCameraFollow follow;
        private NpcSpeechBubbleView bubbles;
        private Action<bool> setNpcSpeaking;
        private Action<DialogueSpeaker> focusChanged;
        private Action<DialogueNode> lineCompleted;
        private Action finished;
        private Transform table;
        private IDialogueIntroduction introduction;
        private string currentText;
        private bool cameraStarted, confirmArmed, preparingTalk, exitingAnimation;
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
        internal string LastEndReason { get; private set; }

        public bool Begin(DialogueGraph graph, DialogueStagingPlan staging, DialogueParticipant participant,
            PlayerInteractor interactor, Action<bool> speaking, Action completed) =>
            Begin(graph, staging, participant, default, interactor, speaking, completed, null, null, null, null, null);

        internal bool BeginGroup(DialogueGraph graph, DialogueStagingPlan staging,
            DialogueParticipant first, DialogueParticipant second, PlayerInteractor interactor, Transform groupTable,
            string entryId, Action<DialogueSpeaker> focus, Action<DialogueNode> onLineCompleted,
            Action completed, IDialogueIntroduction prelude = null) =>
            Begin(graph, staging, first, second, interactor, null, completed, groupTable, entryId, focus,
                onLineCompleted, prelude);

        private bool Begin(DialogueGraph graph, DialogueStagingPlan staging, DialogueParticipant participant,
            DialogueParticipant other, PlayerInteractor interactor, Action<bool> speaking, Action completed,
            Transform groupTable, string entryId, Action<DialogueSpeaker> focus,
            Action<DialogueNode> onLineCompleted, IDialogueIntroduction prelude)
        {
            if (IsActive || !isActiveAndEnabled || graph == null || !participant.IsPresent ||
                graph.UsesSecondNpc && (!groupTable || !other.IsPresent || participant.Owner == other.Owner) ||
                interactor == null || !interactor.isActiveAndEnabled || !interactor.InputEnabled ||
                BarMinigameModalLock.IsAnyLocked || SceneTransitionService.IsTransitioning) return false;
            var start = new DialogueCursor(graph, entryId);
            var registry = interactor.GetComponentInChildren<Player3DAssetRegistry>();
            animation = interactor.GetComponent<PlayerAnimatedInteractionController>();
            follow = Camera.main != null ? Camera.main.GetComponent<PlayerCameraFollow>() : null;
            if (registry == null || registry.Anchors.Head == null || animation == null ||
                !animation.IsInitialized || !animation.isActiveAndEnabled || animation.IsActive || follow == null ||
                !PlayerDialogueActions.TryAttach(registry))
            {
                LastEndReason = "dialogue rig or camera unavailable";
                return false;
            }
            if (!modal.TryCaptureAndDisable(interactor, follow,
                FindAnyObjectByType<IntoxicationHudView>())) return false;
            listener = interactor; npc = participant; secondNpc = other; table = groupTable;
            hero = new DialogueParticipant(new NpcSpeaker(interactor, registry.Anchors.Head,
                NpcVoiceCatalog.HeroMutterDesignId, NpcEarshotProfile.Conversation), interactor.transform,
                registry.GetComponent<Player3DCharacterPresentation>() ??
                interactor.GetComponentInChildren<Player3DCharacterPresentation>(), SpeechFaceProfile.Hero);
            setNpcSpeaking = speaking; finished = completed;
            focusChanged = focus; lineCompleted = onLineCompleted; introduction = prelude;
            currentText = null;
            LastEndReason = null;
            Staging = staging; cursor = start; clock = 0f;
            Phase = DialoguePhase.Positioning; cameraStarted = preparingTalk = exitingAnimation = false;
            animationFinishedFrame = -1;
            GameCursor.Capture(this, CursorLockMode.None, false);
            try
            {
                if (bubbles == null)
                {
                    var host = new GameObject("Dialogue speech");
                    host.transform.SetParent(transform, false);
                    bubbles = host.AddComponent<NpcSpeechBubbleView>();
                    bubbles.UseManualClock = true;
                    bubbles.UseSubtitles = true;
                }
                bubbles.Initialize(follow.Camera, interactor.transform);
                if (!bubbles.DeclareSpeaker(npc.Speech) || !bubbles.DeclareSpeaker(hero.Speech) ||
                    table && !bubbles.DeclareSpeaker(secondNpc.Speech) ||
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
            bubbles.HeldSubtitle = null;
            bubbles.DismissAll(); setNpcSpeaking?.Invoke(false);
            introduction?.Release();
            introduction = null;
            ReleaseFaces();
            GameCursor.Capture(this, CursorLockMode.None, false);
            Phase = DialoguePhase.Exiting;
            if (animation != null && animation.Phase == PlayerAnimatedInteractionPhase.Positioning)
            { animation.CancelActiveInteraction(); }
            AdvanceExit();
        }

        private void Update()
        {
            if (!IsActive) return;
            if (!npc.IsPresent || !hero.IsPresent ||
                !ReferenceEquals(table, null) && (!table || !secondNpc.IsPresent) ||
                listener == null || !listener.isActiveAndEnabled ||
                animation == null || !animation.isActiveAndEnabled || follow == null ||
                SceneTransitionService.IsTransitioning || Vector3.Distance(hero.Root.position, npc.Root.position) > 3.5f ||
                table && Vector3.Distance(hero.Root.position, secondNpc.Root.position) > 3.5f)
            { LastEndReason = "participant or owner unavailable"; RestoreImmediate(); return; }
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
                        cameraStarted = table
                            ? director.BeginGroup(follow, table, npc, secondNpc, hero, introduction != null)
                            : director.Begin(follow, npc.Root, npc.Head, hero.Root, hero.Head);
                        if (!cameraStarted) { LastEndReason = "dialogue establishing shot unavailable"; Cancel(); return; }
                        Phase = DialoguePhase.Framing;
                    }
                    break;
                case DialoguePhase.Framing:
                    if (!director.IsSettled || animation.Phase != PlayerAnimatedInteractionPhase.Looping) break;
                    if (introduction != null) Phase = DialoguePhase.Presenting;
                    else PrepareNode();
                    break;
                case DialoguePhase.PreparingLine:
                    if (preparingTalk && animation.NestedLoopActionPhase != PlayerAnimatedInteractionPhase.Looping) break;
                    TrySpeak();
                    break;
                case DialoguePhase.Speaking:
                    DialogueParticipant speaker = Participant(cursor.Current.Speaker);
                    if (bubbles.IsShowing(speaker.Owner)) break;
                    setNpcSpeaking?.Invoke(false);
                    if (preparingTalk)
                    {
                        animation.RequestNestedLoopActionExit();
                        Phase = DialoguePhase.FinishingGesture;
                    }
                    else CompleteLine();
                    break;
                case DialoguePhase.FinishingGesture:
                    if (!animation.IsNestedLoopActionActive)
                    { preparingTalk = false; CompleteLine(); }
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
            if (node.Kind == DialogueNodeKind.Choice)
            {
                if (!table) director.Focus(true);
                bubbles.HeldSubtitle = currentText;
                bubbles.SubtitleBottom = ChoicePanel(node.Choices.Count).yMin - 6f;
                GameCursor.Capture(this, CursorLockMode.None, true);
                Phase = DialoguePhase.Choosing; SelectedChoice = 0;
                choiceFrame = Time.frameCount; confirmArmed = false;
                return;
            }
            bubbles.HeldSubtitle = null;
            bubbles.SubtitleBottom = RetroUiTheme.LogicalHeight - 18f;
            currentText = LocalizationService.Get(node.TextKey);
            if (table)
            {
                if (!director.FocusGroup(node.Speaker))
                {
                    LastEndReason = "dialogue shot unavailable for " + node.Speaker;
                    Cancel();
                    return;
                }
                focusChanged?.Invoke(node.Speaker);
            }
            else director.Focus(node.Speaker == DialogueSpeaker.Hero);
            Phase = DialoguePhase.PreparingLine;
            GameCursor.Capture(this, CursorLockMode.None, false);
            if (node.Speaker == DialogueSpeaker.Hero)
            {
                preparingTalk = animation.BeginNestedLoopAction(PlayerDialogueActions.CreateTalkingDefinition(), holdLoop: true);
                if (!preparingTalk) Cancel();
            }
        }

        private void TrySpeak()
        {
            DialogueNode node = cursor.Current;
            DialogueParticipant speaker = Participant(node.Speaker);
            if (!bubbles.ShowAt(speaker.Owner, currentText, clock,
                SpeechDelivery.ResolveSpokenDuration(currentText, SpeechDelivery.ReadingTailSeconds)))
            {
                LastEndReason = "dialogue speech unavailable";
                Cancel();
                return;
            }
            setNpcSpeaking?.Invoke(node.Speaker != DialogueSpeaker.Hero);
            Phase = DialoguePhase.Speaking;
        }

        private DialogueParticipant Participant(DialogueSpeaker speaker) => speaker switch
        {
            DialogueSpeaker.Hero => hero,
            DialogueSpeaker.SecondNpc => secondNpc,
            _ => npc
        };

        private void CompleteLine()
        {
            cursor.CompleteLine();
            lineCompleted?.Invoke(cursor.Current);
            PrepareNode();
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
                if (!PresentFace(hero) || !PresentFace(npc) || table && !PresentFace(secondNpc))
                { LastEndReason = "participant face unavailable"; Cancel(); return; }
            }
            if (cameraStarted)
            {
                director.Tick(Time.deltaTime);
                if (Phase != DialoguePhase.Exiting &&
                    (director.IsFinished || director.IsSettled && !director.CurrentShotIsClear))
                { LastEndReason = "dialogue sightline lost"; RestoreImmediate(); return; }
            }
            if (Phase == DialoguePhase.Presenting)
            {
                bool ready = introduction.Present(Time.deltaTime, out bool failed);
                if (failed)
                {
                    LastEndReason = "dialogue introduction could not reach contact";
                    Cancel();
                }
                else if (ready)
                {
                    introduction.Release();
                    introduction = null;
                    PrepareNode();
                }
            }
            if (Phase == DialoguePhase.Exiting && animationFinishedFrame >= 0 &&
                Time.frameCount > animationFinishedFrame && (!cameraStarted || director.IsFinished)) RestoreImmediate();
        }

        public void RestoreImmediate()
        {
            if (!IsActive && !modal.IsLocked) return;
            Action callback = finished; finished = null;
            Phase = DialoguePhase.Idle;
            introduction?.Release();
            introduction = null;
            ReleaseFaces();
            bubbles?.DismissAll();
            bubbles?.WithdrawSpeaker(npc.Owner); bubbles?.WithdrawSpeaker(hero.Owner);
            if (bubbles != null)
            {
                if (table) bubbles.WithdrawSpeaker(secondNpc.Owner);
                bubbles.HeldSubtitle = null;
            }
            setNpcSpeaking?.Invoke(false); setNpcSpeaking = null;
            animation?.CancelActiveInteraction();
            director.RestoreImmediate(); cameraStarted = false;
            GameCursor.Release(this);
            modal.Restore();
            listener = null; hero = npc = secondNpc = default; cursor = null;
            table = null; currentText = null; focusChanged = null; lineCompleted = null;
            callback?.Invoke();
        }

        private bool PresentFace(in DialogueParticipant participant)
        {
            if (participant.Face == null) return true;
            SpeechFacePose pose = bubbles.TryGetSpeechFaceSample(participant.Owner, out SpeechFaceSample sample)
                ? SpeechFaceAnimation.Resolve(sample, participant.FaceProfile, clock)
                : SpeechFaceAnimation.ResolveListening(participant.FaceProfile, clock);
            return participant.Face.TrySetSpeechFace(this, pose);
        }

        private void ReleaseFaces()
        {
            ReleaseFace(hero);
            ReleaseFace(npc);
            ReleaseFace(secondNpc);
        }

        private void ReleaseFace(in DialogueParticipant participant)
        {
            if (participant.Face is UnityEngine.Object actor && !actor) return;
            participant.Face?.ReleaseSpeechFace(this);
        }

        private static Rect ChoicePanel(int count)
        {
            float height = Mathf.Min(4, count) * 28f + 16f;
            return new Rect((RetroUiTheme.LogicalWidth - 328f) * .5f,
                RetroUiTheme.LogicalHeight - height - 18f, 328f, height);
        }

        private void OnGUI()
        {
            using var input = RetroUiInput.Begin();
            if (!IsChoosing || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused) return;
            if (optionStyle == null)
            {
                // A text-only hit target avoids inherited editor skin's scaled button textures.
                optionStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.Text);
                selectedStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.SelectionText);
                optionStyle.wordWrap = selectedStyle.wordWrap = true;
            }
            int previousDepth = GUI.depth; bool enabled = GUI.enabled;
            GUI.depth = -90;
            GUI.enabled = enabled && Time.frameCount > choiceFrame && GameInput.CanRead(GameInputContext.Menu);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(RetroUiTheme.CalculateCanvas(Screen.width, Screen.height));
            try
            {
                int count = cursor.Current.Choices.Count, visible = Mathf.Min(4, count);
                int first = Mathf.Clamp(SelectedChoice - visible + 1, 0, count - visible);
                Rect panel = ChoicePanel(count);
                LastChoicePanelRect = panel;
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Panel, RetroUiTheme.BorderMuted,
                    false, 0f, 1f, RetroUiTheme.DialoguePanelOpacity);
                for (int row = 0; row < visible; row++)
                {
                    int index = first + row;
                    Rect rect = new Rect(panel.x + 10f, panel.y + 8f + row * 28f, panel.width - 20f, 26f);
                    if (GUI.enabled && RetroUiInput.PointerMoved && rect.Contains(RetroUiInput.MousePosition))
                        SelectedChoice = index;
                    bool selected = index == SelectedChoice;
                    RetroUiTheme.DrawSelection(rect, selected, RetroUiTheme.DialoguePanelOpacity);
                    GUI.Label(new Rect(rect.x + 7f, rect.y, rect.width - 14f, rect.height),
                        LocalizationService.Get(cursor.Current.Choices[index].TextKey), selected ? selectedStyle : optionStyle);
                    if (RetroUiInput.Button(rect, GUIContent.none, GUIStyle.none))
                    { SelectChoice(index); Confirm(); break; }
                }
            }
            finally { RetroUiTheme.EndCanvas(matrix); GUI.depth = previousDepth; GUI.enabled = enabled; }
        }
        private void OnDisable() => RestoreImmediate();
        private void OnDestroy() => RestoreImmediate();
    }
}
