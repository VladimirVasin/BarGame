using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarCounterStation : MonoBehaviour, IInteractable
    {
        private CocktailMinigameController minigame;

        public string PromptKey => "interaction.order_drinks";
        public Vector3 InteractionPosition => transform.position;

        public void Configure(CocktailMinigameController controller)
        {
            minigame = controller;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return minigame != null &&
                   !minigame.IsOpen &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                minigame.Open(interactor);
            }
        }
    }
}
