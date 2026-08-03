using UnityEngine;

namespace BarPromenade
{
    public sealed class SupermarketEntrance :
        MonoBehaviour,
        IInteractable
    {
        public Vector3 ReturnPosition { get; private set; }
        public string PromptKey => "interaction.enter_supermarket";
        public Vector3 InteractionPosition => transform.position;

        public void Configure(Vector3 returnPosition)
        {
            ReturnPosition = returnPosition;
        }

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

            PlayerMotor motor = interactor == null
                ? null
                : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.SupermarketInterior,
                DoorTransitionDirection.EnterBuilding,
                out string operationId);
            if (accepted)
            {
                GameSessionState.EnterSupermarket();
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "supermarket_enter_result",
                GameLog.Field("accepted", accepted),
                GameLog.Field("operation_id", operationId),
                GameLog.Field("return_x", ReturnPosition.x),
                GameLog.Field("return_z", ReturnPosition.z));
        }
    }
}
