using UnityEngine;

namespace BarPromenade
{
    public sealed class BarEntrance : MonoBehaviour, IInteractable
    {
        public const string ClosedFeedbackKey = "interaction.bar_closed";

        public string BarId { get; private set; } = string.Empty;
        public BarActivityKind BarActivity { get; private set; } =
            BarActivityKind.None;

        /// <summary>The district whose identity this bar carries.</summary>
        public CityDistrictKind BarDistrict { get; private set; } =
            BarDistrictIdentityCatalog.FallbackDistrict;

        public Vector3 ReturnPosition { get; private set; }
        public string PromptKey => BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay)
            ? "interaction.enter_bar"
            : ClosedFeedbackKey;
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
            Configure(
                barId,
                barActivity,
                BarDistrictIdentityCatalog.FallbackDistrict,
                returnPosition);
        }

        public void Configure(
            string barId,
            BarActivityKind barActivity,
            CityDistrictKind barDistrict,
            Vector3 returnPosition)
        {
            BarId = barId ?? string.Empty;
            BarActivity = string.IsNullOrEmpty(BarId)
                ? BarActivityKind.None
                : barActivity;
            BarDistrict =
                BarDistrictIdentityCatalog.Normalize(barDistrict);
            ReturnPosition = returnPosition;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            PlayerDoorActionTarget doorAction =
                GetComponent<PlayerDoorActionTarget>();
            return !string.IsNullOrEmpty(BarId) &&
                   !SceneTransitionService.IsTransitioning &&
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

            if (RefuseWhenClosed(interactor)) return;

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
            // Closing may arrive while the hero is reaching for the handle.
            // Refuse before the transition or session context acquires anything.
            if (RefuseWhenClosed(interactor)) return;

            PlayerMotor motor = interactor == null
                ? null
                : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestDoorLoad(
                SceneIds.BarInterior,
                DoorTransitionDirection.EnterBar,
                out string operationId);
            if (accepted)
            {
                GameSessionState.EnterBar(
                    BarId,
                    BarActivity,
                    BarDistrict);
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

        private bool RefuseWhenClosed(PlayerInteractor interactor)
        {
            if (BarBusinessHours.IsOpen(GameSessionState.GameMinuteOfDay)) return false;
            interactor?.ShowFeedback(ClosedFeedbackKey, LockedDoorInteraction.ResponseDurationSeconds);
            LogResult(false, string.Empty, "bar_closed");
            return true;
        }

        private void LogResult(
            bool accepted,
            string operationId,
            string reason)
        {
            GameLog.Info(
                "interaction",
                "bar_enter_result",
                GameLog.Field("bar_id", BarId),
                GameLog.Field(
                    "activity",
                    BarActivity.ToString()),
                GameLog.Field("accepted", accepted),
                GameLog.Field("operation_id", operationId),
                GameLog.Field("reason", reason));
        }
    }
}
