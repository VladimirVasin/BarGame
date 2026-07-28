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
            if (!CanInteract(interactor))
            {
                return;
            }

            PlayerMotor motor = interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            if (SceneTransitionService.RequestDoorLoad(
                    SceneIds.BarInterior,
                    DoorTransitionDirection.EnterBar))
            {
                GameSessionState.EnterBar(BarId, BarActivity);
            }
            else
            {
                motor?.SetInputEnabled(true);
            }
        }
    }
}
