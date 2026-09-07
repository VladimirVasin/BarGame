using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The shower from the hero's own eyes. The lens sits inside the head —
    /// the measured mouth anchor plus the toilet's eye offset — so whatever
    /// wash pose the scene writes carries the camera with it. The interaction
    /// detaches that lens before straightening and keeps it parked while the
    /// hero walks out. Head visibility follows the actual lens separation.
    /// During washing, arrow keys, the mouse or the right stick look around inside
    /// a clamped cone that never turns the body, so looking down shows him
    /// what he is washing. Nothing here is a second camera: the scene base
    /// blends the pinned bathroom shot into this pose and back out of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerFirstPersonView : MonoBehaviour
    {
        public const float FieldOfView = 78f;
        public const float EyeHeightAboveMouth = 0.068f;
        public const float InitialLookYawDegrees = 7f;

        /// <summary>The blend at which the lens counts as inside the head.</summary>
        public const float HeadHideBlend = 0.90f;
        public const float MaximumLookYawDegrees = 75f;
        public const float MinimumViewPitchDegrees = -75f;
        // The bent head sits ahead of the chest. Looking back down at that
        // skin needs travel past vertical, with room for a visible stroke.
        public const float MaximumViewPitchDegrees = 115f;
        public const float KeyboardDegreesPerSecond = 90f;

        // Wash-relative limits are also used by the body picking/framing
        // helpers. The live view clamps the absolute pitch at every base pose.
        public const float MinimumLookPitchDegrees =
            MinimumViewPitchDegrees - HomeShowerSceneTimeline.WashPitchDegrees;
        public const float MaximumLookPitchDegrees =
            MaximumViewPitchDegrees - HomeShowerSceneTimeline.WashPitchDegrees;

        private const float MouseYawSensitivity = 0.16f;
        private const float MousePitchSensitivity = 0.14f;
        private const float StickDegreesPerSecond = 105f;

        private HomeInteriorRoot home;
        private Player3DAssetRegistry registry;
        private Transform actor;
        private Player3DHeadVisibility hiddenHead;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool cursorCaptured;
        private bool wasLookAllowed;
        private float lookYaw;
        private float lookPitch;
        private float basePitch;
        private Vector3 cameraPosition;
        private Quaternion cameraRotation;

        public bool IsPrepared => registry != null && actor != null;
        public bool IsActive { get; private set; }
        public bool IsHeadHidden => hiddenHead != null;
        public bool IsPointerMode { get; private set; }
        public int HiddenHeadRendererCount => hiddenHead?.HiddenRendererCount ?? 0;
        public float LookYawDegrees => lookYaw;
        public float LookPitchDegrees => lookPitch;
        public float BasePitchDegrees => basePitch;

        /// <summary>Binds the view to the production rig; false when the hero is not the 3D hero.</summary>
        public bool Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            End();
            home = homeRoot;
            registry = null;
            actor = null;
            if (home.Player.GameObject == null ||
                !(home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null ||
                visual.Registry.Anchors.Mouth == null)
            {
                return false;
            }

            registry = visual.Registry;
            actor = home.Player.GameObject.transform;
            return true;
        }

        /// <summary>The eye, in the room's frame, for the camera path captured at E.</summary>
        public bool TryGetEyeLocal(Transform room, out Vector3 position, out Vector3 forward)
        {
            if (!IsPrepared || room == null)
            {
                position = default;
                forward = default;
                return false;
            }

            position = room.InverseTransformPoint(
                registry.Anchors.Mouth.position + Vector3.up * EyeHeightAboveMouth);
            forward = room.InverseTransformDirection(actor.forward);
            return true;
        }

        public void Begin(float basePitchDegrees)
        {
            if (IsActive)
            {
                return;
            }

            if (!IsPrepared)
            {
                throw new InvalidOperationException(
                    "The shower view requires the production hero rig.");
            }

            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            lookYaw = InitialLookYawDegrees;
            lookPitch = 0f;
            wasLookAllowed = false;
            basePitch = basePitchDegrees;
            IsActive = true;
            UpdateCameraPose();
        }

        /// <summary>
        /// Once per presentation frame, after the scene has written the
        /// pose: read the look input, follow the base pitch, and take the
        /// head off or put it back as the blend crosses the threshold.
        /// </summary>
        public void Tick(
            float deltaTime,
            float cameraBlend,
            float basePitchDegrees,
            bool lookAllowed)
        {
            if (!IsActive || !IsPrepared)
            {
                return;
            }

            basePitch = basePitchDegrees;
            lookPitch = ClampLookPitch(lookPitch);
            bool acceptsLook = lookAllowed && !PauseMenuController.IsAnyPaused &&
                IsFinite(deltaTime) && deltaTime > 0f;
            if (acceptsLook)
            {
                ReadLookInput(deltaTime, !wasLookAllowed);
            }

            wasLookAllowed = acceptsLook;
            UpdateCameraPose();
            if (cameraBlend >= HeadHideBlend && hiddenHead == null)
            {
                hiddenHead = Player3DHeadVisibility.Hide(registry);
            }
            else if (cameraBlend < HeadHideBlend && hiddenHead != null)
            {
                RestoreHead();
            }
        }

        public void EvaluateCamera(out Vector3 position, out Quaternion rotation)
        {
            position = cameraPosition;
            rotation = cameraRotation;
        }

        /// <summary>A look turn in degrees, clamped to the cone; the same path serves keys, mouse, stick and tests.</summary>
        public void ApplyLookDelta(float yawDegrees, float pitchDegrees)
        {
            if (!IsActive || PauseMenuController.IsAnyPaused ||
                !IsFinite(yawDegrees) || !IsFinite(pitchDegrees))
            {
                return;
            }

            lookYaw = Mathf.Clamp(
                lookYaw + yawDegrees,
                -MaximumLookYawDegrees,
                MaximumLookYawDegrees);
            lookPitch = ClampLookPitch(lookPitch + pitchDegrees);
        }

        public void End()
        {
            RestoreHead();
            RestoreCursor();
            IsActive = false;
            IsPointerMode = false;
            wasLookAllowed = false;
            lookYaw = 0f;
            lookPitch = 0f;
        }

        public void SetPointerMode(bool enabled)
        {
            if (!IsActive || IsPointerMode == enabled) return;
            IsPointerMode = enabled;
            Cursor.lockState = enabled ? CursorLockMode.Confined : CursorLockMode.Locked;
            Cursor.visible = enabled;
            wasLookAllowed = false;
        }

        private void UpdateCameraPose()
        {
            cameraPosition = registry.Anchors.Mouth.position +
                Vector3.up * EyeHeightAboveMouth;
            cameraRotation = actor.rotation *
                Quaternion.Euler(basePitch + lookPitch, lookYaw, 0f);
        }

        private float ClampLookPitch(float value) => Mathf.Clamp(value,
            MinimumViewPitchDegrees - basePitch,
            MaximumViewPitchDegrees - basePitch);

        private void ReadLookInput(float deltaTime, bool discardMouseDelta)
        {
            float yaw = 0f;
            float pitch = 0f;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                yaw += ((keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.leftArrowKey.isPressed ? 1f : 0f)) * KeyboardDegreesPerSecond * deltaTime;
                pitch += ((keyboard.downArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.upArrowKey.isPressed ? 1f : 0f)) * KeyboardDegreesPerSecond * deltaTime;
            }

            // The soap pointer owns mouse/stick motion until its look mode
            // is held. Arrow look stays available alongside that pointer.
            if (!IsPointerMode)
            {
                Mouse mouse = Mouse.current;
                if (mouse != null && !discardMouseDelta)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * MouseYawSensitivity;
                    pitch -= delta.y * MousePitchSensitivity;
                }

                Gamepad gamepad = Gamepad.current;
                if (gamepad != null)
                {
                    Vector2 stick = gamepad.rightStick.ReadValue();
                    yaw += stick.x * StickDegreesPerSecond * deltaTime;
                    pitch -= stick.y * StickDegreesPerSecond * deltaTime;
                }
            }

            ApplyLookDelta(yaw, pitch);
        }

        private void RestoreHead()
        {
            hiddenHead?.Restore();
            hiddenHead = null;
        }

        private void RestoreCursor()
        {
            if (!cursorCaptured)
            {
                return;
            }

            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorCaptured = false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDisable()
        {
            End();
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
