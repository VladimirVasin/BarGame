using UnityEngine;

namespace BarPromenade
{
    public sealed class SupermarketExit :
        MonoBehaviour,
        IInteractable
    {
        private InteriorHingedDoorInteraction door;
        private PlayerInteractor pendingInteractor;

        public void Initialize(InteriorHingedDoorInteraction configuredDoor)
        {
            if (door != null) door.InteractionFinished -= DoorFinished;
            door = configuredDoor;
            if (door != null) door.InteractionFinished += DoorFinished;
        }

        private void DoorFinished(bool completed)
        {
            PlayerInteractor interactor = pendingInteractor;
            pendingInteractor = null;
            if (completed && interactor != null && !CompleteDoorAction(interactor))
                door.TrySetOpenFraction(0f);
        }

        private void OnDisable()
        {
            pendingInteractor = null;
            door?.Cancel();
        }
        private void OnDestroy()
        {
            if (door != null) door.InteractionFinished -= DoorFinished;
        }

        public string PromptKey => "interaction.exit_supermarket";
        public Vector3 InteractionPosition => transform.position;

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (door != null) return !SceneTransitionService.IsTransitioning && door.CanInteract(interactor);
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

            if (door != null)
            {
                pendingInteractor = interactor;
                if (!door.Begin()) pendingInteractor = null;
                return;
            }
            PlayerDoorActionTarget doorAction =
                GetComponent<PlayerDoorActionTarget>();
            doorAction.TryBegin(
                interactor,
                () => CompleteDoorAction(interactor));
        }

        private static bool CompleteDoorAction(
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
            return accepted;
        }
    }
}
