using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarActivityStation : MonoBehaviour, IInteractable
    {
        public const string DefaultPromptKey =
            "interaction.order_drinks";

        [SerializeField] private string promptKey =
            DefaultPromptKey;

        private IBarMinigame minigame;

        public string PromptKey => string.IsNullOrWhiteSpace(promptKey)
            ? DefaultPromptKey
            : promptKey;
        public Vector3 InteractionPosition => transform.position;
        public IBarMinigame Minigame => minigame;

        public void Configure(
            IBarMinigame controller,
            string localizedPromptKey = DefaultPromptKey)
        {
            minigame = controller;
            promptKey = string.IsNullOrWhiteSpace(localizedPromptKey)
                ? DefaultPromptKey
                : localizedPromptKey;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return interactor != null &&
                   minigame != null &&
                   !minigame.IsOpen &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                GetComponentInParent<BarArrivalPresentation>()?.Skip();
                minigame.Open(interactor);
            }
        }
    }
}
