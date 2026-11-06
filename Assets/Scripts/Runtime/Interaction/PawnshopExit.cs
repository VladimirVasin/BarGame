using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class PawnshopExit : MonoBehaviour, IInteractable
    {
        private InteriorHingedDoorInteraction door;
        private PlayerInteractor pending;
        public string PromptKey => "pawnshop.exit.prompt";
        public Vector3 InteractionPosition => transform.position;

        public void Initialize(InteriorHingedDoorInteraction entranceDoor)
        {
            if (door != null) door.InteractionFinished -= Finished;
            door = entranceDoor;
            if (door != null) door.InteractionFinished += Finished;
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            isActiveAndEnabled && !SceneTransitionService.IsTransitioning && door != null && door.CanInteract(interactor);

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            pending = interactor;
            if (!door.Begin()) pending = null;
        }

        private void Finished(bool completed)
        {
            var interactor = pending; pending = null;
            if (!completed || interactor == null) return;
            var motor = interactor.GetComponent<PlayerMotor>();
            motor?.SetInputEnabled(false);
            if (SceneTransitionService.RequestDoorLoad(SceneIds.City, DoorTransitionDirection.ExitPawnshop))
                GameSessionState.PreparePawnshopReturn();
            else
            {
                motor?.SetInputEnabled(true);
                door.TrySetOpenFraction(0f);
            }
        }

        private void OnDisable() { pending = null; door?.Cancel(); }
        private void OnDestroy() { if (door != null) door.InteractionFinished -= Finished; }
    }
}
