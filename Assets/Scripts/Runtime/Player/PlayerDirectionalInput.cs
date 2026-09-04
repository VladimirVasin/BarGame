using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The one place WASD and the left stick are read. The motor reads
    /// them as tank controls (x yaws, y walks); a body on the floor has
    /// no meaningful forward of its own, so the fall reads the same keys
    /// relative to the camera instead.
    /// </summary>
    public static class PlayerDirectionalInput
    {
        /// <summary>WASD (never the arrows: those orbit the camera) or the left stick, each axis in <c>-1..1</c>.</summary>
        public static Vector2 ReadRaw()
        {
            Vector2 movement = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                movement.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                movement.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null &&
                gamepad.leftStick.ReadValue().sqrMagnitude > movement.sqrMagnitude)
            {
                movement = gamepad.leftStick.ReadValue();
            }

            // The axes are independent channels for the motor: a combined
            // W+A must keep full forward speed, so no vector clamp here.
            movement.x = Mathf.Clamp(movement.x, -1f, 1f);
            movement.y = Mathf.Clamp(movement.y, -1f, 1f);
            return movement;
        }

        /// <summary>
        /// The raw pair as a planar world direction relative to the
        /// camera's facing (x to the camera's right, y away from it),
        /// falling back to the root's facing when there is no camera;
        /// never longer than one.
        /// </summary>
        public static Vector3 ToWorldPlanar(
            Vector2 raw,
            Transform cameraOrNull,
            Transform fallbackRoot)
        {
            Transform frame = cameraOrNull != null ? cameraOrNull : fallbackRoot;
            Vector3 forward = frame != null ? frame.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                // A camera looking straight down: its up is the way its
                // top of screen points, which is the way "forward" reads.
                forward = frame != null ? frame.up : Vector3.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.forward;
                }
            }

            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 world = right * raw.x + forward * raw.y;
            return world.sqrMagnitude > 1f ? world.normalized : world;
        }

        /// <summary>A planar world direction in a root's frame: x along its right, y along its forward.</summary>
        public static Vector2 ToBodyLocal(Vector3 worldPlanar, Transform root)
        {
            if (root == null)
            {
                return new Vector2(worldPlanar.x, worldPlanar.z);
            }

            Vector3 forward = root.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return new Vector2(
                Vector3.Dot(worldPlanar, right),
                Vector3.Dot(worldPlanar, forward));
        }
    }
}
