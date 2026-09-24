using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A replenishable source; the inventory owns the one-log limit.</summary>
    [DisallowMultipleComponent]
    public sealed class WoodpileInteraction : MonoBehaviour, IInteractable,
        IInventoryTargetInteractionHandler
    {
        public const string InteractionPromptKey = "interaction.woodpile";
        public const string ConfirmationPromptKey = "woodpile.take.confirm";
        public const string AlreadyCarryingFeedbackKey = "woodpile.take.already";

        private InventoryTargetInteractionController controller;
        private PlayerInteractor activeInteractor;
        private bool prepared;
        private readonly RaycastHit[] sightHits = new RaycastHit[16];

        public string PromptKey => InteractionPromptKey;
        public Vector3 InteractionPosition => transform.position;

        public void Initialize(
            InventoryTargetInteractionController confirmationController)
        {
            if (confirmationController == null)
                throw new ArgumentNullException(nameof(confirmationController));
            Close();
            controller = confirmationController;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled && controller != null &&
                   controller.IsInitialized && interactor != null &&
                   interactor.InputEnabled && !interactor.InteractKeyClaimed &&
                   !SceneTransitionService.IsTransitioning &&
                   !CounterMenuInput.IsBlockedByOtherUi() && IsInReach(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            if (GameSessionState.HasInventoryItem(InventoryItemId.FirewoodLog))
            {
                ShowAlreadyCarrying(interactor);
                return;
            }

            activeInteractor = interactor;
            if (!controller.Open(interactor,
                    InventoryTargetInteractionDefinition.ConfirmationOnly(
                        ConfirmationPromptKey), this))
                activeInteractor = null;
        }

        public bool TryPrepareInventoryInteraction()
        {
            prepared = isActiveAndEnabled && activeInteractor != null &&
                       !SceneTransitionService.IsTransitioning &&
                       IsInReach(activeInteractor);
            return prepared;
        }

        public void BeginInventoryInteraction()
        {
            if (!prepared) return;
            prepared = false;
            PlayerInteractor interactor = activeInteractor;
            activeInteractor = null;
            // Recheck capacity at the transaction, even if the menu opened empty.
            bool added = GameSessionState.TryAddInventoryItem(
                InventoryItemId.FirewoodLog);
            controller.CompleteExecution();
            if (!added) ShowAlreadyCarrying(interactor);
        }

        public void CancelInventoryInteractionPreparation()
        {
            prepared = false;
            activeInteractor = null;
        }

        private bool IsInReach(PlayerInteractor interactor)
        {
            Vector3 chest = interactor.transform.position + Vector3.up * .8f;
            Vector3 delta = InteractionPosition - chest;
            float distance = delta.magnitude;
            if (distance > PlayerInteractor.InteractionRadius) return false;
            if (distance <= .001f) return true;
            int count = Physics.RaycastNonAlloc(chest, delta / distance, sightHits,
                distance, ~0, QueryTriggerInteraction.Ignore);
            if (count == sightHits.Length) return false;
            for (int index = 0; index < count; index++)
            {
                Collider hit = sightHits[index].collider;
                // Inspect every hit: a hand in front of the chest must not hide
                // a wall farther along the same segment.
                if (hit != null && !hit.transform.IsChildOf(interactor.transform))
                    return false;
            }
            return true;
        }

        private static void ShowAlreadyCarrying(PlayerInteractor interactor)
        {
            interactor?.ShowFeedback(AlreadyCarryingFeedbackKey,
                InventoryTargetInteractionDefinition.DefaultFeedbackDurationSeconds);
        }

        private void Close()
        {
            if (controller != null) controller.CloseForHandler(this);
            CancelInventoryInteractionPreparation();
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
