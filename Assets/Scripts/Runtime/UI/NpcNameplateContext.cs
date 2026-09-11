using UnityEngine;

namespace BarPromenade
{
    /// <summary>Explicit camera/listener ownership prevents previews or another scene from showing labels.</summary>
    [DisallowMultipleComponent]
    public sealed class NpcNameplateContext : MonoBehaviour
    {
        public PlayerInteractor Listener { get; private set; }

        public bool IsSuppressed => !isActiveAndEnabled || Listener == null ||
            !Listener.isActiveAndEnabled || !Listener.InputEnabled ||
            PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused ||
            SceneTransitionService.IsTransitioning || InventoryController.IsAnyOpen ||
            JournalController.IsAnyOpen || BarMinigameModalLock.IsAnyLocked;

        public void Initialize(PlayerInteractor listener) => Listener = listener;
    }
}
