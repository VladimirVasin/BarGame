using UnityEngine.InputSystem;

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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.sKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return 0;
            }

            if (gamepad.dpad.up.wasPressedThisFrame)
            {
                return -1;
            }

            return gamepad.dpad.down.wasPressedThisFrame ? 1 : 0;
        }

        public static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonWest.wasPressedThisFrame;
        }

        public static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
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
