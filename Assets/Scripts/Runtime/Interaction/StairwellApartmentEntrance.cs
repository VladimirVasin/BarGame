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
