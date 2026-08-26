using UnityEngine;

namespace BarPromenade
{
    public sealed class ChurchEntrance : MonoBehaviour, IInteractable
    {
        public Vector3 ReturnPosition { get; private set; }
        public string PromptKey => "interaction.enter_church";
        public Vector3 InteractionPosition => transform.position;

        public void Configure(Vector3 returnPosition)
        {
            ReturnPosition = returnPosition;
        }

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

        private static void CompleteDoorAction(PlayerInteractor interactor)
        {
            PlayerMotor motor = interactor == null
                ? null
                : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.ChurchInterior,
                DoorTransitionDirection.EnterChurch,
                out string operationId);
            if (accepted)
            {
                GameSessionState.EnterChurch();
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "church_enter_result",
                GameLog.Field("accepted", accepted),
                GameLog.Field("operation_id", operationId));
        }
    }
}
