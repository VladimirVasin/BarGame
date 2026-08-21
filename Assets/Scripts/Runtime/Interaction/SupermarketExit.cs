using UnityEngine;

namespace BarPromenade
{
    public sealed class SupermarketExit :
        MonoBehaviour,
        IInteractable
    {
        public string PromptKey => "interaction.exit_supermarket";
        public Vector3 InteractionPosition => transform.position;

        public bool CanInteract(PlayerInteractor interactor)
        {
            PlayerDoorActionTarget doorAction =
                GetComponent<PlayerDoorActionTarget>();
            return !SceneTransitionService.IsTransitioning &&
                   doorAction != null &&
                   doorAction.CanInteract(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            PlayerDoorActionTarget doorAction =
                GetComponent<PlayerDoorActionTarget>();
            doorAction.TryBegin(
                interactor,
                () => CompleteDoorAction(interactor));
        }

        private static void CompleteDoorAction(
            PlayerInteractor interactor)
        {
            PlayerMotor motor = interactor == null
                ? null
                : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.City,
                DoorTransitionDirection.ExitBuilding,
                out string operationId);
            if (accepted)
            {
                GameSessionState.PrepareSupermarketReturn();
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "supermarket_exit_result",
                GameLog.Field("accepted", accepted),
                GameLog.Field("operation_id", operationId));
        }
    }
}
