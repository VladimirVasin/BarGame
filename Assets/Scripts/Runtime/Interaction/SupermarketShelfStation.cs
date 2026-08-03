using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SupermarketShelfStation :
        MonoBehaviour,
        IInteractable
    {
        private SupermarketShelfShopController controller;
        private SupermarketShelfView shelf;

        public string PromptKey =>
            "interaction.browse_supermarket_shelf";
        public Vector3 InteractionPosition => transform.position;
        public SupermarketShelfView Shelf => shelf;

        public void Configure(
            SupermarketShelfShopController shopController,
            SupermarketShelfView shelfView)
        {
            controller = shopController;
            shelf = shelfView;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return interactor != null &&
                   controller != null &&
                   shelf != null &&
                   controller.IsShelfAvailable(shelf) &&
                   !controller.IsOpen &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                controller.Open(shelf, interactor);
            }
        }
    }
}
