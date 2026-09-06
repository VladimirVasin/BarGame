using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BarPromenade
{
    /// <summary>
    /// Common game actions and their current bindings. Device reads stay here;
    /// callers name an action and the context that currently owns it.
    /// </summary>
    public static class GameInput
    {
        public static bool CanRead(GameInputContext context)
        {
            bool modalLocked = BarMinigameModalLock.IsAnyLocked;
            return GameInputPolicy.Allows(
                context,
                SceneTransitionService.IsTransitioning,
                PauseMenuController.IsAnyPaused,
                GameTimeScaleRuntime.IsPaused,
                modalLocked,
                BarMinigameModalLock.BlocksMotorInput);
        }

        public static bool WasPressed(
            GameInputAction action,
            GameInputContext context = GameInputContext.Menu)
        {
            return CanRead(context) && ReadAction(action, false);
        }

        public static bool IsHeld(
            GameInputAction action,
            GameInputContext context = GameInputContext.Contextual)
        {
            return CanRead(context) && ReadAction(action, true);
        }

        public static Vector2 ReadMovement()
        {
            if (!CanRead(GameInputContext.Movement))
            {
                return Vector2.zero;
            }

            Keyboard keyboard = Keyboard.current;
            Vector2 movement = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) -
                (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) -
                (keyboard.sKey.isPressed ? 1f : 0f));
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > movement.sqrMagnitude)
                {
                    movement = stick;
                }
            }

            // Tank yaw and forward are independent, including W+A.
            return new Vector2(
                Mathf.Clamp(movement.x, -1f, 1f),
                Mathf.Clamp(movement.y, -1f, 1f));
        }

        public static int ReadMenuSelectionDelta(GameInputContext context)
        {
            if (!CanRead(context))
            {
                return 0;
            }

            if (KeyboardUp() || KeyboardLeft()) return -1;
            if (KeyboardDown() || KeyboardRight()) return 1;
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null) return 0;
            if (Pressed(gamepad.dpad.up) || Pressed(gamepad.dpad.left) ||
                Pressed(gamepad.leftStick.up) || Pressed(gamepad.leftStick.left))
                return -1;
            return Pressed(gamepad.dpad.down) || Pressed(gamepad.dpad.right) ||
                   Pressed(gamepad.leftStick.down) || Pressed(gamepad.leftStick.right)
                ? 1 : 0;
        }

        public static int ReadGridSelectionDelta(int columns)
        {
            if (!CanRead(GameInputContext.Menu)) return 0;
            if (KeyboardLeft()) return -1;
            if (KeyboardRight()) return 1;
            if (KeyboardUp()) return -columns;
            if (KeyboardDown()) return columns;
            Gamepad gamepad = Gamepad.current;
            if (gamepad == null) return 0;
            if (Pressed(gamepad.dpad.left)) return -1;
            if (Pressed(gamepad.dpad.right)) return 1;
            if (Pressed(gamepad.dpad.up)) return -columns;
            return Pressed(gamepad.dpad.down) ? columns : 0;
        }

        public static int ReadCounterSelectionDelta()
        {
            if (!CanRead(GameInputContext.Gameplay)) return 0;
            Keyboard keyboard = Keyboard.current;
            if (Pressed(keyboard?.wKey)) return -1;
            if (Pressed(keyboard?.sKey)) return 1;
            Gamepad gamepad = Gamepad.current;
            if (Pressed(gamepad?.dpad.up)) return -1;
            return Pressed(gamepad?.dpad.down) ? 1 : 0;
        }

        private static bool ReadAction(GameInputAction action, bool held)
        {
            Keyboard keyboard = Keyboard.current;
            Gamepad gamepad = Gamepad.current;
            switch (action)
            {
                case GameInputAction.Interact:
                    return Read(keyboard?.eKey, held) ||
                           Read(keyboard?.enterKey, held) ||
                           Read(gamepad?.buttonSouth, held);
                case GameInputAction.Confirm:
                    return ReadAction(GameInputAction.Submit, held) ||
                           Read(keyboard?.spaceKey, held);
                case GameInputAction.Submit:
                    return ReadAction(GameInputAction.Interact, held) ||
                           Read(keyboard?.numpadEnterKey, held);
                case GameInputAction.Cancel:
                    return Read(keyboard?.escapeKey, held) ||
                           Read(gamepad?.buttonEast, held);
                case GameInputAction.GamepadCancel:
                    return Read(gamepad?.buttonEast, held);
                case GameInputAction.Pause:
                    return Read(keyboard?.escapeKey, held) ||
                           Read(gamepad?.startButton, held);
                case GameInputAction.Inventory:
                    return Read(keyboard?.iKey, held) ||
                           Read(gamepad?.buttonNorth, held);
                case GameInputAction.Journal:
                    return Read(keyboard?.jKey, held) ||
                           Read(gamepad?.rightShoulder, held);
                case GameInputAction.UseItem:
                    return Read(keyboard?.uKey, held) ||
                           Read(gamepad?.buttonWest, held);
                case GameInputAction.CounterConfirm:
                    return Read(keyboard?.spaceKey, held) ||
                           Read(gamepad?.buttonWest, held);
                case GameInputAction.SkipRide:
                    return Read(keyboard?.f10Key, held);
                case GameInputAction.Sprint:
                    return Read(keyboard?.leftShiftKey, held) ||
                           Read(keyboard?.rightShiftKey, held) ||
                           Read(gamepad?.leftStickButton, held);
                default:
                    return false;
            }
        }

        private static bool KeyboardLeft() =>
            Pressed(Keyboard.current?.leftArrowKey) || Pressed(Keyboard.current?.aKey);
        private static bool KeyboardRight() =>
            Pressed(Keyboard.current?.rightArrowKey) || Pressed(Keyboard.current?.dKey);
        private static bool KeyboardUp() =>
            Pressed(Keyboard.current?.upArrowKey) || Pressed(Keyboard.current?.wKey);
        private static bool KeyboardDown() =>
            Pressed(Keyboard.current?.downArrowKey) || Pressed(Keyboard.current?.sKey);
        private static bool Pressed(ButtonControl button) => Read(button, false);
        private static bool Read(ButtonControl button, bool held) =>
            button != null && (held ? button.isPressed : button.wasPressedThisFrame);
    }
}
