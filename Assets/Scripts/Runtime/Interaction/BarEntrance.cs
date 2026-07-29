using UnityEngine;

namespace BarPromenade
{
    public sealed class BarEntrance : MonoBehaviour, IInteractable
    {
        public string BarId { get; private set; } = string.Empty;
        public BarActivityKind BarActivity { get; private set; } =
            BarActivityKind.None;
        public Vector3 ReturnPosition { get; private set; }
        public string PromptKey => "interaction.enter_bar";
        public Vector3 InteractionPosition => transform.position;

        public void Configure(string barId, Vector3 returnPosition)
        {
            Configure(
                barId,
                BarActivityKind.Cocktail,
                returnPosition);
        }

        public void Configure(
            string barId,
            BarActivityKind barActivity,
            Vector3 returnPosition)
        {
            BarId = barId ?? string.Empty;
            BarActivity = string.IsNullOrEmpty(BarId)
                ? BarActivityKind.None
                : barActivity;
            ReturnPosition = returnPosition;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return !string.IsNullOrEmpty(BarId) && !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            Vector3 playerPosition = interactor == null
                ? Vector3.zero
                : interactor.transform.position;
            GameLog.Info(
                "interaction",
                "bar_enter_requested",
                GameLog.Field("bar_id", BarId),
                GameLog.Field(
                    "activity",
                    BarActivity.ToString()),
                GameLog.Field("player_x", playerPosition.x),
                GameLog.Field("player_z", playerPosition.z),
                GameLog.Field("return_x", ReturnPosition.x),
                GameLog.Field("return_z", ReturnPosition.z));
            if (!CanInteract(interactor))
            {
                GameLog.Info(
                    "interaction",
                    "bar_enter_result",
                    GameLog.Field("bar_id", BarId),
                    GameLog.Field("accepted", false),
                    GameLog.Field(
                        "reason",
                        string.IsNullOrEmpty(BarId)
                            ? "missing_bar_id"
                            : "transition_busy"),
                    GameLog.Field("operation_id", string.Empty));
                return;
            }

            PlayerMotor motor = interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                    SceneIds.BarInterior,
                    DoorTransitionDirection.EnterBar,
                    out string operationId);
            if (accepted)
            {
                GameSessionState.EnterBar(BarId, BarActivity);
            }
            else
            {
                motor?.SetInputEnabled(true);
            }

            GameLog.Info(
                "interaction",
                "bar_enter_result",
                GameLog.Field("bar_id", BarId),
                GameLog.Field(
                    "activity",
                    BarActivity.ToString()),
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
