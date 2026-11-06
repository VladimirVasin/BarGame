using UnityEngine;

namespace BarPromenade
{
    public sealed class PawnshopEntrance : MonoBehaviour, IInteractable
    {
        private PlayerDoorActionTarget door;
        public string PromptKey => "interaction.enter_pawnshop";
        public Vector3 InteractionPosition => transform.position;
        public Vector3 ReturnPosition { get; private set; }

        public void Configure(CityPawnshopPlan plan)
        {
            ReturnPosition = plan.ReturnWorld;
            door = GetComponent<PlayerDoorActionTarget>();
        }

        public bool CanInteract(PlayerInteractor interactor) => !SceneTransitionService.IsTransitioning &&
            door != null && door.CanInteract(interactor);

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            door.TryBegin(interactor, () => Complete(interactor));
        }

        private static void Complete(PlayerInteractor interactor)
        {
            PlayerMotor motor = interactor == null ? null : interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            if (SceneTransitionService.RequestDoorLoad(SceneIds.PawnshopInterior,
                DoorTransitionDirection.EnterPawnshop, out _)) GameSessionState.EnterPawnshop();
            else motor?.SetInputEnabled(true);
        }
    }
}
