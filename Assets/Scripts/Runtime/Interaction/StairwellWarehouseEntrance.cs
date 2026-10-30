using UnityEngine;

namespace BarPromenade
{
    /// <summary>The final stair door uses the shared rig action and keeps the active game session.</summary>
    public sealed class StairwellWarehouseEntrance : MonoBehaviour, IInteractable
    {
        private PlayerDoorActionTarget door;
        public string PromptKey => "interaction.open_village_door";
        public Vector3 InteractionPosition => transform.position;

        public void Initialize(PlayerDoorActionTarget target) => door = target;

        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled &&
            GameSessionState.StairwellLabyrinthPuzzles.AllSolved &&
            !SceneTransitionService.IsTransitioning && door != null && door.CanInteract(interactor);

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            door.TryBegin(interactor, () =>
            {
                if (!GameSessionState.StairwellLabyrinthPuzzles.AllSolved) return;
                PlayerMotor motor = interactor == null ? null : interactor.GetComponent<PlayerMotor>();
                motor?.SetInputEnabled(false);
                if (!WarehouseStoryEntryService.TryEnter()) motor?.SetInputEnabled(true);
            });
        }
    }
}
