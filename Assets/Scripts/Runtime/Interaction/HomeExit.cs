using UnityEngine;

namespace BarPromenade
{
    public sealed class HomeExit : MonoBehaviour, IInteractable
    {
        public string PromptKey => "interaction.exit_apartment";
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
                SceneIds.StairwellInterior,
                DoorTransitionDirection.ExitApartment,
                out string operationId);
            if (accepted)
            {
                GameSessionState.PrepareStairwellArrival(
                    StairwellArrivalKind.ApartmentDoor);
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
