using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarCounterStation : MonoBehaviour, IInteractable
    {
        private BarDrinkShopController controller;

        public string PromptKey => "interaction.buy_drink";
        public Vector3 InteractionPosition => transform.position;

        public void Configure(BarDrinkShopController shopController)
        {
            controller = shopController;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return interactor != null &&
                   controller != null &&
                   !controller.IsOpen &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                controller.Open(interactor);
            }
        }
    }
}
