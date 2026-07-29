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
            Vector3 playerPosition = interactor == null
                ? Vector3.zero
                : interactor.transform.position;
            GameLog.Info(
                "interaction",
                "bar_exit_requested",
                GameLog.Field(
                    "bar_id",
                    GameSessionState.ActiveBarId),
                GameLog.Field(
                    "activity",
                    GameSessionState.ActiveBarActivity.ToString()),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "drinks_consumed",
                    GameSessionState.DrinksConsumed),
                GameLog.Field("player_x", playerPosition.x),
                GameLog.Field("player_z", playerPosition.z));
            if (!CanInteract(interactor))
            {
                GameLog.Info(
                    "interaction",
                    "bar_exit_result",
                    GameLog.Field("accepted", false),
                    GameLog.Field(
                        "reason",
                        "transition_busy"),
                    GameLog.Field("operation_id", string.Empty));
                return;
            }

            GetComponentInParent<BarArrivalPresentation>()?.Skip();
            PlayerMotor motor = interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                    SceneIds.City,
                    DoorTransitionDirection.ExitBar,
                    out string operationId);
            if (accepted)
            {
                GameSessionState.PrepareCityReturn();
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "bar_exit_result",
                GameLog.Field(
                    "bar_id",
                    GameSessionState.ActiveBarId),
                GameLog.Field("accepted", accepted),
                GameLog.Field(
                    "operation_id",
                    operationId),
                GameLog.Field(
                    "reason",
                    accepted
                        ? "accepted"
                        : "transition_rejected"));
        }
    }
}
