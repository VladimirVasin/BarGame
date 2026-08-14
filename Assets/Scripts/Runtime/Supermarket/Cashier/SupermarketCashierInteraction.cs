using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The placeholder talk stub in front of the register — the phone
    /// booth/dumpster contract. A future pass replaces
    /// <see cref="Interact"/> with a real conversation without moving
    /// the trigger; for now the clerk just stares, unblinking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketCashierInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey = "interaction.talk_cashier";
        public const string PlaceholderKey =
            "supermarket.cashier.placeholder";
        public const float ResponseDurationSeconds = 2.5f;

        private Vector3 standPosition;
        private bool isInitialized;

        public string PromptKey => TalkPromptKey;
        public Vector3 InteractionPosition => standPosition;

        public void Initialize(Vector3 configuredStandPosition)
        {
            standPosition = configuredStandPosition;
            isInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            interactor.ShowFeedback(
                PlaceholderKey,
                ResponseDurationSeconds);
        }
    }
}
