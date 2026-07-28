using UnityEngine;

namespace BarPromenade
{
    public sealed class BarExit : MonoBehaviour, IInteractable
    {
        public string PromptKey => "interaction.exit_bar";
        public Vector3 InteractionPosition => transform.position;

        public bool CanInteract(PlayerInteractor interactor)
        {
            return !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            PlayerMotor motor = interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            if (SceneTransitionService.RequestDoorLoad(
                    SceneIds.City,
                    DoorTransitionDirection.ExitBar))
            {
                GameSessionState.PrepareCityReturn();
            }
            else
            {
                motor?.SetInputEnabled(true);
            }
        }
    }
}
