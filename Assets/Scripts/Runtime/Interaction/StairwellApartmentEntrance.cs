using UnityEngine;

namespace BarPromenade
{
    public sealed class StairwellApartmentEntrance :
        MonoBehaviour,
        IInteractable
    {
        public string PromptKey => "interaction.enter_apartment";
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
                SceneIds.HomeInterior,
                DoorTransitionDirection.EnterApartment,
                out string operationId);
            if (!accepted)
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "apartment_enter_result",
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
