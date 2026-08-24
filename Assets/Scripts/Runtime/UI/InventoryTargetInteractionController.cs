using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DefaultExecutionOrder(-825)]
    [DisallowMultipleComponent]
    public sealed class InventoryTargetInteractionController :
        MonoBehaviour
    {
        public const string TalkChoiceLocalizationKey =
            "interaction.choice.talk";
        public const string InteractChoiceLocalizationKey =
            "interaction.choice.interact";
        public const string YesLocalizationKey = "common.yes";
        public const string NoLocalizationKey = "common.no";

        private static InventoryTargetInteractionController
            activeController;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly InventoryTargetInteractionModel model =
            new InventoryTargetInteractionModel();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private PlayerInteractor sourceInteractor;
        private IInventoryTargetInteractionHandler handler;
        private InventoryTargetInteractionDefinition definition;
        private GUIStyle optionStyle;
        private GUIStyle selectedOptionStyle;
        private GUIStyle messageStyle;
        private int inputUnlockFrame;
        private bool handlerOwnsWork;

        /// <summary>
        /// The requirement has left the inventory and the interaction has
        /// not yet reached the point where spending it means anything.
        /// Until it does, every way out of here has to put it back.
        /// </summary>
        private bool requirementTaken;

        public static bool IsAnyOpen =>
            activeController != null && activeController.IsOpen;
        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public bool IsExecuting =>
            IsOpen &&
            model.State == InventoryTargetInteractionState.Executing;
        public InventoryTargetInteractionState State => model.State;
        public InventoryTargetInteractionChoice SelectedChoice =>
            model.SelectedChoice;
        public bool ConfirmationYesSelected =>
            model.ConfirmationYesSelected;
        public InventoryTargetInteractionDefinition Definition =>
            definition;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeController = null;
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud)
        {
            if (playerRuntime.GameObject == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "The target interaction requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The target interaction is already initialized.");
            }

            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            IsInitialized = true;
        }

        public bool Open(
            PlayerInteractor interactor,
            InventoryTargetInteractionDefinition interactionDefinition,
            IInventoryTargetInteractionHandler interactionHandler)
        {
            if (!interactionDefinition.IsValid)
            {
                throw new ArgumentException(
                    "The target interaction definition must be valid.",
                    nameof(interactionDefinition));
            }

            if (interactionHandler == null)
            {
                throw new ArgumentNullException(
                    nameof(interactionHandler));
            }

            if (!CanOpen(interactor) ||
                !modalLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                return false;
            }

            sourceInteractor = interactor;
            definition = interactionDefinition;
            handler = interactionHandler;
            handlerOwnsWork = false;
            requirementTaken = false;
            model.Open();
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            activeController = this;
            return true;
        }

        public bool MoveSelection(int delta)
        {
            return IsOpen && model.MoveSelection(delta);
        }

        public bool SelectChoice(
            InventoryTargetInteractionChoice choice)
        {
            return IsOpen && model.SelectChoice(choice);
        }

        public bool SelectConfirmation(bool yes)
        {
            return IsOpen && model.SelectConfirmation(yes);
        }

        public bool Confirm()
        {
            if (!IsOpen)
            {
                return false;
            }

            InventoryTargetInteractionState previousState = model.State;
            InventoryTargetInteractionAction action = model.Confirm(
                HasRequiredItem());
            bool handled = action !=
                               InventoryTargetInteractionAction.None ||
                           model.State != previousState;
            HandleAction(action);
            return handled;
        }

        public bool Cancel()
        {
            if (!IsOpen)
            {
                return false;
            }

            InventoryTargetInteractionState previousState = model.State;
            InventoryTargetInteractionAction action = model.Cancel();
            bool handled = action !=
                               InventoryTargetInteractionAction.None ||
                           model.State != previousState;
            HandleAction(action);
            return handled;
        }

        public bool CompleteExecution()
        {
            if (!IsOpen || !model.CompleteExecution())
            {
                return false;
            }

            handlerOwnsWork = false;
            // An interaction that ran to its end spent what it took.
            requirementTaken = false;
            return CloseInternal();
        }

        /// <summary>
        /// Marks the requirement as spent for good, from the moment the
        /// handler's work becomes irreversible — the cat has its tin open
        /// in front of it and no abort can un-feed it. Before this the
        /// item is only borrowed and comes back if the interaction is
        /// abandoned; after it, it is gone.
        ///
        /// False when nothing was taken to commit.
        /// </summary>
        public bool CommitRequirement()
        {
            if (!requirementTaken)
            {
                return false;
            }

            requirementTaken = false;
            return true;
        }

        public bool AbortExecution()
        {
            if (!IsOpen ||
                model.State !=
                    InventoryTargetInteractionState.Executing)
            {
                return false;
            }

            return CloseInternal();
        }

        /// <summary>
        /// Lets a target that is being disabled or destroyed release only the
        /// modal session it owns. Active work is cancelled before player input
        /// is restored; another target's session is never affected.
        /// </summary>
        public bool CloseForHandler(
            IInventoryTargetInteractionHandler interactionHandler)
        {
            if (interactionHandler == null ||
                !ReferenceEquals(handler, interactionHandler))
            {
                return false;
            }

            return CloseInternal();
        }

        private bool CanOpen(PlayerInteractor interactor)
        {
            return IsInitialized &&
                   isActiveAndEnabled &&
                   !IsOpen &&
                   activeController == null &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning &&
                   interactor != null &&
                   interactor == player.Interactor &&
                   interactor.InputEnabled &&
                   player.Motor != null &&
                   player.Motor.InputEnabled;
        }

        private bool HasRequiredItem()
        {
            if (!definition.HasRequirement)
            {
                return true;
            }

            InventoryItemRequirement requirement =
                definition.Requirement;
            return GameSessionState.HasInventoryItem(
                requirement.ItemId,
                requirement.Count);
        }

        private void HandleAction(
            InventoryTargetInteractionAction action)
        {
            switch (action)
            {
                case InventoryTargetInteractionAction.None:
                    return;
                case InventoryTargetInteractionAction.ShowTalkFeedback:
                    CloseWithFeedback(definition.TalkResponseKey);
                    return;
                case InventoryTargetInteractionAction
                    .ShowMissingRequirementFeedback:
                    CloseWithFeedback(
                        definition.MissingRequirementResponseKey);
                    return;
                case InventoryTargetInteractionAction.BeginExecution:
                    TryBeginExecution();
                    return;
                case InventoryTargetInteractionAction.Close:
                    CloseInternal();
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported target interaction action '{action}'.");
            }
        }

        private void TryBeginExecution()
        {
            IInventoryTargetInteractionHandler currentHandler = handler;
            if (currentHandler == null)
            {
                AbortExecution();
                return;
            }

            // Preparation may acquire more than one presentation resource.
            // Treat the handler as owning cleanup before the first call so a
            // false result or exception can unwind partial work safely.
            handlerOwnsWork = true;
            bool prepared;
            try
            {
                prepared = currentHandler
                    .TryPrepareInventoryInteraction();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                AbortExecution();
                return;
            }

            if (!prepared)
            {
                AbortExecution();
                return;
            }

            if (!definition.HasRequirement)
            {
                // Nothing to spend, so nothing to fail on: straight to the
                // act the player just agreed to. `requirementTaken` stays
                // false, which is also what keeps the refund path a no-op
                // if this is abandoned half way.
                try
                {
                    currentHandler.BeginInventoryInteraction();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    AbortExecution();
                }

                return;
            }

            InventoryItemRequirement requirement =
                definition.Requirement;
            if (!GameSessionState.TryRemoveInventoryItem(
                    requirement.ItemId,
                    requirement.Count))
            {
                string feedbackKey =
                    definition.MissingRequirementResponseKey;
                float feedbackDuration =
                    definition.FeedbackDurationSeconds;
                PlayerInteractor feedbackTarget = sourceInteractor;
                AbortExecution();
                feedbackTarget?.ShowFeedback(
                    feedbackKey,
                    feedbackDuration);
                return;
            }

            requirementTaken = true;
            try
            {
                currentHandler.BeginInventoryInteraction();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                AbortExecution();
            }
        }

        private void CloseWithFeedback(string feedbackKey)
        {
            PlayerInteractor feedbackTarget = sourceInteractor;
            float feedbackDuration =
                definition.FeedbackDurationSeconds;
            CloseInternal();
            feedbackTarget?.ShowFeedback(
                feedbackKey,
                feedbackDuration);
        }

        private bool CloseInternal()
        {
            bool hadState = IsOpen || modalLock.IsLocked;
            RefundUncommittedRequirement();
            bool cleanupHandler =
                model.State ==
                    InventoryTargetInteractionState.Executing &&
                handlerOwnsWork;
            IInventoryTargetInteractionHandler currentHandler = handler;
            handlerOwnsWork = false;
            if (cleanupHandler)
            {
                CancelPreparation(currentHandler);
            }

            model.Close();
            modalLock.Restore();
            IsOpen = false;
            sourceInteractor = null;
            handler = null;
            definition = default;
            if (activeController == this)
            {
                activeController = null;
            }

            return hadState;
        }

        /// <summary>
        /// Gives back what an abandoned interaction already took. The
        /// requirement leaves the inventory before the handler starts its
        /// work, so a close that never reached
        /// <see cref="CommitRequirement"/> has to put it back. Without
        /// this the item is simply gone, and anything waiting on the hero
        /// still owning it — the cat's tin, and the quest reservation that
        /// follows it — is stranded with no way to finish.
        /// </summary>
        private void RefundUncommittedRequirement()
        {
            if (!requirementTaken)
            {
                return;
            }

            requirementTaken = false;
            InventoryItemRequirement requirement =
                definition.Requirement;
            if (!GameSessionState.TryAddInventoryItem(
                    requirement.ItemId,
                    requirement.Count))
            {
                Debug.LogError(
                    $"Could not refund {requirement.Count}x " +
                    $"{requirement.ItemId} after an abandoned target " +
                    "interaction.",
                    this);
            }
        }

        private static void CancelPreparation(
            IInventoryTargetInteractionHandler currentHandler)
        {
            try
            {
                currentHandler
                    ?.CancelInventoryInteractionPreparation();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                CloseInternal();
                return;
            }

            if (model.State ==
                    InventoryTargetInteractionState.Executing ||
                Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasCancelPressed())
            {
                Cancel();
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                MoveSelection(selectionDelta);
            }

            if (WasConfirmPressed())
            {
                Confirm();
            }
        }

        private void OnGUI()
        {
            if (!IsOpen ||
                model.State == InventoryTargetInteractionState.Executing)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -90;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                if (model.State ==
                    InventoryTargetInteractionState.Confirmation)
                {
                    DrawConfirmation();
                }
                else
                {
                    DrawChoice();
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawChoice()
        {
            const float panelWidth = 250f;
            const float panelHeight = 82f;
            Rect panel = new Rect(
                (RetroUiTheme.LogicalWidth - panelWidth) * 0.5f,
                (RetroUiTheme.LogicalHeight - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);

            Rect talkRect = new Rect(
                panel.x + 15f,
                panel.y + 12f,
                panel.width - 30f,
                25f);
            Rect interactRect = new Rect(
                talkRect.x,
                talkRect.yMax + 8f,
                talkRect.width,
                talkRect.height);
            DrawOption(
                talkRect,
                TalkChoiceLocalizationKey,
                model.SelectedChoice ==
                    InventoryTargetInteractionChoice.Talk,
                () => ChooseAndConfirm(
                    InventoryTargetInteractionChoice.Talk));
            DrawOption(
                interactRect,
                InteractChoiceLocalizationKey,
                model.SelectedChoice ==
                    InventoryTargetInteractionChoice.Interact,
                () => ChooseAndConfirm(
                    InventoryTargetInteractionChoice.Interact));
        }

        private void DrawConfirmation()
        {
            const float panelWidth = 300f;
            const float panelHeight = 105f;
            Rect panel = new Rect(
                (RetroUiTheme.LogicalWidth - panelWidth) * 0.5f,
                (RetroUiTheme.LogicalHeight - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);
            GUI.Label(
                new Rect(
                    panel.x + 15f,
                    panel.y + 11f,
                    panel.width - 30f,
                    39f),
                LocalizationService.Get(
                    definition.ConfirmationPromptKey),
                messageStyle);

            const float gap = 10f;
            float buttonWidth = (panel.width - 40f - gap) * 0.5f;
            Rect noRect = new Rect(
                panel.x + 20f,
                panel.yMax - 37f,
                buttonWidth,
                25f);
            Rect yesRect = new Rect(
                noRect.xMax + gap,
                noRect.y,
                buttonWidth,
                noRect.height);
            DrawOption(
                noRect,
                NoLocalizationKey,
                !model.ConfirmationYesSelected,
                () => ChooseConfirmationAndConfirm(false));
            DrawOption(
                yesRect,
                YesLocalizationKey,
                model.ConfirmationYesSelected,
                () => ChooseConfirmationAndConfirm(true));
        }

        private void DrawOption(
            Rect rect,
            string localizationKey,
            bool selected,
            Action clicked)
        {
            RetroUiTheme.DrawPanel(
                rect,
                selected
                    ? RetroUiTheme.PanelRaised
                    : RetroUiTheme.PanelInset,
                selected
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.BorderMuted,
                selected,
                2f,
                1f);
            if (GUI.Button(
                    rect,
                    LocalizationService.Get(localizationKey),
                    selected ? selectedOptionStyle : optionStyle))
            {
                clicked();
            }
        }

        private void ChooseAndConfirm(
            InventoryTargetInteractionChoice choice)
        {
            SelectChoice(choice);
            Confirm();
        }

        private void ChooseConfirmationAndConfirm(bool yes)
        {
            SelectConfirmation(yes);
            Confirm();
        }

        private void EnsureStyles()
        {
            if (optionStyle != null &&
                selectedOptionStyle != null &&
                messageStyle != null)
            {
                return;
            }

            optionStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            selectedOptionStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            messageStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true,
                true);
        }

        private void OnDisable()
        {
            CloseInternal();
        }

        private void OnDestroy()
        {
            CloseInternal();
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.upArrowKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame ||
                    keyboard.wKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.rightArrowKey.wasPressedThisFrame ||
                    keyboard.downArrowKey.wasPressedThisFrame ||
                    keyboard.dKey.wasPressedThisFrame ||
                    keyboard.sKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.left.wasPressedThisFrame ||
                    gamepad.dpad.up.wasPressedThisFrame)
                {
                    return -1;
                }

                if (gamepad.dpad.right.wasPressedThisFrame ||
                    gamepad.dpad.down.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            return 0;
        }
    }
}
