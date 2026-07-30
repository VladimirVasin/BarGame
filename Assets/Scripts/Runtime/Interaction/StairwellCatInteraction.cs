using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class StairwellCatInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string DefaultPromptKey = "interaction.cat";
        public const string ResponsePromptKey =
            "stairwell.cat.placeholder";
        public const float ResponseDurationSeconds = 2.5f;

        private Vector3 interactionPosition;
        private float responseStartedAt;
        private float responseExpiresAt;
        private bool isInitialized;
        private bool responseActive;

        public string PromptKey =>
            GetPromptKeyAt(Time.unscaledTime);
        public Vector3 InteractionPosition =>
            interactionPosition;
        public bool IsInitialized => isInitialized;

        public void Initialize(
            Vector3 authoredWorldInteractionPosition)
        {
            interactionPosition =
                authoredWorldInteractionPosition;
            isInitialized = true;
            responseActive = false;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   isInitialized &&
                   isActiveAndEnabled &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            InteractAt(interactor, Time.unscaledTime);
        }

        public bool InteractAt(
            PlayerInteractor interactor,
            float unscaledTime)
        {
            if (!CanInteract(interactor))
            {
                return false;
            }

            responseStartedAt = unscaledTime;
            responseExpiresAt =
                unscaledTime + ResponseDurationSeconds;
            responseActive = true;
            return true;
        }

        public string GetPromptKeyAt(float unscaledTime)
        {
            return responseActive &&
                   unscaledTime >= responseStartedAt &&
                   unscaledTime < responseExpiresAt
                ? ResponsePromptKey
                : DefaultPromptKey;
        }
    }
}
