using UnityEngine;

namespace BarPromenade
{
    public sealed class HomeEntrance : MonoBehaviour, IInteractable
    {
        public Vector3 ReturnPosition { get; private set; }
        public string PromptKey => "interaction.enter_building";
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
            Vector3 playerPosition = interactor == null
                ? Vector3.zero
                : interactor.transform.position;
            GameLog.Info(
                "interaction",
                "home_enter_requested",
                GameLog.Field("player_x", playerPosition.x),
                GameLog.Field("player_z", playerPosition.z),
                GameLog.Field("return_x", ReturnPosition.x),
                GameLog.Field("return_z", ReturnPosition.z));
            if (!CanInteract(interactor))
            {
                GameLog.Info(
                    "interaction",
                    "home_enter_result",
                    GameLog.Field("accepted", false),
                    GameLog.Field("reason", "transition_busy"),
                    GameLog.Field("operation_id", string.Empty));
                return;
            }

            PlayerDoorActionTarget doorAction =
                GetComponent<PlayerDoorActionTarget>();
            if (!doorAction.TryBegin(
                    interactor,
                    () => CompleteDoorAction(interactor)))
            {
                LogResult(false, string.Empty, "door_action_rejected");
            }
        }

        private void CompleteDoorAction(PlayerInteractor interactor)
        {
            PlayerMotor motor = interactor == null
                ? null
                : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.StairwellInterior,
                DoorTransitionDirection.EnterBuilding,
                out string operationId);
            if (accepted)
            {
                GameSessionState.EnterHome();
                GameSessionState.PrepareStairwellArrival(
                    StairwellArrivalKind.StreetDoor);
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            LogResult(
                accepted,
                operationId,
                accepted ? "accepted" : "transition_rejected");
        }

        private static void LogResult(
            bool accepted,
            string operationId,
            string reason)
        {
            GameLog.Info(
                "interaction",
                "home_enter_result",
                GameLog.Field("accepted", accepted),
                GameLog.Field("operation_id", operationId),
                GameLog.Field("reason", reason));
        }
    }
}
