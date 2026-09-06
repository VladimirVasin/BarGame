namespace BarPromenade
{
    /// <summary>
    /// Shared controls for a physical counter menu. The ordinary
    /// E/Enter/South interaction aliases remain with PlayerInteractor and
    /// the seated station, exactly as in the mountain cafe.
    /// </summary>
    public static class CounterMenuInput
    {
        public static int ReadSelectionDelta()
        {
            return GameInput.ReadCounterSelectionDelta();
        }

        public static bool WasConfirmPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.CounterConfirm, GameInputContext.Gameplay);
        }

        public static bool WasCancelPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Cancel, GameInputContext.Gameplay);
        }

        public static bool IsBlockedByOtherUi()
        {
            return PauseMenuController.IsAnyPaused ||
                   InventoryController.IsAnyOpen ||
                   JournalController.IsAnyOpen ||
                   InventoryTargetInteractionController.IsAnyOpen ||
                   BarMinigameModalLock.IsAnyLocked ||
                   SceneTransitionService.IsTransitioning;
        }
    }
}
