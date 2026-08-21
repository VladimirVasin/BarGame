using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Couples one explicit door dock/facing plan to an ordinary interactable.
    /// The trigger transform itself is intentionally not treated as facing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDoorActionTarget : MonoBehaviour
    {
        private PlayerDoorActionController activeController;
        private PlayerDoorActionPlan plan;

        public bool IsConfigured { get; private set; }
        public PlayerDoorActionPlan Plan => plan;

        public void Configure(PlayerDoorActionPlan configuredPlan)
        {
            configuredPlan.Validate(nameof(configuredPlan));
            CancelOwnedAction();
            plan = configuredPlan;
            IsConfigured = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (activeController != null &&
                !activeController.IsOwnedBy(this))
            {
                activeController = null;
            }

            if (!isActiveAndEnabled ||
                !IsConfigured ||
                interactor == null ||
                activeController != null)
            {
                return false;
            }

            PlayerDoorActionController controller =
                interactor.GetComponent<PlayerDoorActionController>();
            return controller != null && controller.CanBegin(this);
        }

        public bool TryBegin(
            PlayerInteractor interactor,
            Action completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            if (!CanInteract(interactor))
            {
                return false;
            }

            PlayerDoorActionController controller =
                interactor.GetComponent<PlayerDoorActionController>();
            activeController = controller;
            bool accepted = controller.TryBegin(
                this,
                plan,
                () =>
                {
                    if (activeController == controller)
                    {
                        activeController = null;
                    }

                    completed();
                });
            if (!accepted)
            {
                activeController = null;
            }

            return accepted;
        }

        private void OnDisable()
        {
            CancelOwnedAction();
        }

        private void OnDestroy()
        {
            CancelOwnedAction();
        }

        private void CancelOwnedAction()
        {
            if (activeController == null)
            {
                return;
            }

            PlayerDoorActionController controller = activeController;
            activeController = null;
            controller.Cancel(this);
        }
    }
}
