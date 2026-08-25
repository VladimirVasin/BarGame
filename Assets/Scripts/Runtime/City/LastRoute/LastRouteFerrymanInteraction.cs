using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The Ferryman answers the way the cat does: a two-choice menu -
    /// "Поговорить" or "Взаимодействовать" - and the second one asks a
    /// question before it does anything.
    ///
    /// It is the cat's flow with the cat's one requirement removed. The cat
    /// wanted a tin; what the Ferryman asks for is an answer, and an answer
    /// is not something the player carries, so the definition declares no
    /// inventory requirement and the shared controller skips every
    /// inventory step for it. Nothing here can take, refund or complain
    /// about an item.
    ///
    /// Saying yes is not reversible and is not meant to be. He gets off the
    /// bonnet and into the car, and from then on <see cref="CanInteract"/>
    /// is false: there is nobody on the bonnet to talk to any more.
    /// </summary>
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanInteraction :
        MonoBehaviour,
        IInteractable,
        IInventoryTargetInteractionHandler
    {
        public const string DefaultPromptKey = "interaction.ferryman";

        /// <summary>"Уехать из города?" - the whole interaction.</summary>
        public const string LeaveConfirmationPromptKey =
            "lastroute.ferryman.confirm.leave";

        /// <summary>A shade longer than the watchman's three seconds, as
        /// with the fisherman: the lines are short but meant to sit.
        /// </summary>
        public const float ResponseDurationSeconds = 3.4f;

        private Vector3 standPosition;
        private uint quipState;
        private int lastLineIndex = -1;
        private bool isInitialized;
        private bool ownsExecution;
        private LastRouteFerrymanPresentation presentation;
        private InventoryTargetInteractionController targetInteraction;
        private InventoryTargetInteractionDefinition interactionDefinition;

        public string PromptKey => DefaultPromptKey;
        public Vector3 InteractionPosition => standPosition;
        public bool IsInitialized => isInitialized;
        public bool OwnsExecution => ownsExecution;

        /// <summary>The last line index served, for tests and logs; -1
        /// before he has said anything.</summary>
        public int LastLineIndex => lastLineIndex;

        public InventoryTargetInteractionDefinition Definition =>
            interactionDefinition;

        public void Initialize(
            Vector3 configuredStandPosition,
            int citySeed,
            LastRouteFerrymanPresentation ferrymanPresentation,
            InventoryTargetInteractionController interactionController)
        {
            if (ferrymanPresentation == null)
            {
                throw new ArgumentNullException(
                    nameof(ferrymanPresentation));
            }

            if (interactionController == null ||
                !interactionController.IsInitialized)
            {
                throw new ArgumentException(
                    "The Ferryman needs an initialized target interaction " +
                    "controller.",
                    nameof(interactionController));
            }

            standPosition = configuredStandPosition;
            quipState = LastRouteFerrymanQuips.CreateState(citySeed);
            lastLineIndex = -1;
            presentation = ferrymanPresentation;
            targetInteraction = interactionController;
            interactionDefinition = BuildDefinition(
                LastRouteFerrymanQuips.LineKeys[0]);
            isInitialized = true;
        }

        private static InventoryTargetInteractionDefinition BuildDefinition(
            string talkResponseKey)
        {
            return InventoryTargetInteractionDefinition.WithoutRequirement(
                talkResponseKey,
                LeaveConfirmationPromptKey,
                ResponseDurationSeconds);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   targetInteraction != null &&
                   !targetInteraction.IsOpen &&
                   presentation != null &&
                   presentation.IsWaiting &&
                   !ownsExecution &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            TryOpen(interactor);
        }

        public bool TryOpen(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return false;
            }

            // The line is drawn when the menu opens, because the shared
            // controller captures the whole definition at that moment. A
            // player who opens the menu and backs out therefore burns one
            // draw - invisible against a pool of twelve that never repeats
            // itself twice running, and the alternative is a talk option
            // that shows the same line every time.
            lastLineIndex = LastRouteFerrymanQuips.NextIndex(
                ref quipState,
                lastLineIndex);
            interactionDefinition = BuildDefinition(
                LastRouteFerrymanQuips.LineKeys[lastLineIndex]);
            return targetInteraction.Open(
                interactor,
                interactionDefinition,
                this);
        }

        public bool TryPrepareInventoryInteraction()
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   !ownsExecution &&
                   presentation != null &&
                   presentation.IsWaiting &&
                   targetInteraction != null &&
                   targetInteraction.IsExecuting;
        }

        public void BeginInventoryInteraction()
        {
            if (!isInitialized ||
                presentation == null ||
                !presentation.TryBeginBoarding())
            {
                throw new InvalidOperationException(
                    "The Ferryman could not get off the bonnet.");
            }

            ownsExecution = true;
        }

        public void CancelInventoryInteractionPreparation()
        {
            // Idempotent by construction: nothing is reserved before
            // BeginInventoryInteraction, and after it he is already moving
            // and does not come back.
            ownsExecution = false;
        }

        private void Update()
        {
            if (!ownsExecution ||
                presentation == null ||
                !presentation.HasLeftTheBonnet)
            {
                return;
            }

            // His boots are on the ground. Nothing is said about it - him
            // getting off that bonnet IS the answer - so the menu closes.
            //
            // It closes HERE, and not seven seconds later when he finally
            // shuts his door, because the rest of it is a walk round a car
            // and the player should be watching it rather than holding a
            // dialogue open through it. The beat that had to be owned was
            // the answer, and the answer is over the moment he moves.
            ownsExecution = false;
            targetInteraction.CompleteExecution();
        }

        private void OnDisable()
        {
            if (targetInteraction == null ||
                !targetInteraction.CloseForHandler(this))
            {
                CancelInventoryInteractionPreparation();
            }
        }

        private void OnDestroy()
        {
            if (targetInteraction == null ||
                !targetInteraction.CloseForHandler(this))
            {
                CancelInventoryInteractionPreparation();
            }

            isInitialized = false;
        }
    }
}
