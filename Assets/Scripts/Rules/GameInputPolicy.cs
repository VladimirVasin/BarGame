namespace BarPromenade
{
    public enum GameInputContext
    {
        Gameplay,
        Movement,
        Contextual,
        Menu,
        PauseMenu
    }

    public enum GameInputAction
    {
        Interact,
        Confirm,
        Submit,
        Cancel,
        GamepadCancel,
        Pause,
        Inventory,
        Journal,
        UseItem,
        CounterConfirm,
        SkipRide,
        Sprint
    }

    /// <summary>Input ownership rules with no device or scene dependencies.</summary>
    public static class GameInputPolicy
    {
        public static bool Allows(
            GameInputContext context,
            bool transitioning,
            bool pauseMenuOpen,
            bool timePaused,
            bool modalLocked,
            bool modalBlocksMovement)
        {
            if (transitioning)
            {
                return false;
            }

            switch (context)
            {
                case GameInputContext.PauseMenu:
                    return pauseMenuOpen || !modalLocked;
                case GameInputContext.Menu:
                    // Inventory freezes time while its own menu still reads.
                    return !pauseMenuOpen;
                case GameInputContext.Movement:
                    // The balance lock leaves directional recovery available.
                    return !timePaused && !pauseMenuOpen && !modalBlocksMovement;
                case GameInputContext.Contextual:
                    // The current interaction may itself own a modal lock.
                    return !timePaused && !pauseMenuOpen;
                case GameInputContext.Gameplay:
                    return !timePaused && !pauseMenuOpen && !modalLocked;
                default:
                    return false;
            }
        }
    }
}
