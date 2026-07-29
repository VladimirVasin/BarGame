using UnityEngine;

namespace BarPromenade
{
    public sealed class HomeExit : MonoBehaviour, IInteractable
    {
        public string PromptKey => "interaction.exit_home";
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

            PlayerMotor motor =
                interactor == null
                    ? null
                    : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.City,
                DoorTransitionDirection.ExitHome,
                out string operationId);
            if (accepted)
            {
                GameSessionState.PrepareHomeReturn();
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "home_exit_result",
                GameLog.Field("accepted", accepted),
                GameLog.Field("operation_id", operationId),
                GameLog.Field(
                    "reason",
                    accepted
                        ? "accepted"
                        : "transition_rejected"));
        }
    }
}
