using UnityEngine;

namespace BarPromenade
{
    public sealed class BarEntrance : MonoBehaviour, IInteractable
    {
        public string BarId { get; private set; } = string.Empty;
        public Vector3 ReturnPosition { get; private set; }
        public string PromptKey => "interaction.enter_bar";
        public Vector3 InteractionPosition => transform.position;

        public void Configure(string barId, Vector3 returnPosition)
        {
            BarId = barId ?? string.Empty;
            ReturnPosition = returnPosition;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return !string.IsNullOrEmpty(BarId) && !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            PlayerMotor motor = interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            GameSessionState.EnterBar(BarId);
            if (!SceneTransitionService.RequestLoad(SceneIds.BarInterior))
            {
                motor?.SetInputEnabled(true);
            }
        }
    }
}
