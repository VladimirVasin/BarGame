using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The shower from the hero's own eyes. The lens sits inside the head —
    /// the measured mouth anchor plus the toilet's eye offset — so whatever
    /// pose the scene writes each frame (the walk in, the brace under the
    /// water, the sway, the reach to the tap, the walk out) carries the
    /// camera with it. The head geometry comes off while the lens is inside
    /// it and goes back the moment the lens leaves. The base pitch is the
    /// scene's (level on the way in, hanging under the water, level again
    /// for the way out); the mouse or the right stick looks around inside
    /// a clamped cone that never turns the body, so looking down shows him
    /// what he is washing. Nothing here is a second camera: the scene base
    /// blends the pinned bathroom shot into this pose and back out of it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerFirstPersonView : MonoBehaviour
    {
        public const float FieldOfView = 78f;
        public const float EyeHeightAboveMouth = 0.068f;

        /// <summary>The blend at which the lens counts as inside the head.</summary>
        public const float HeadHideBlend = 0.90f;
        public const float MaximumLookYawDegrees = 75f;
        public const float MinimumLookPitchDegrees = -45f;
        public const float MaximumLookPitchDegrees = 55f;

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
            lookYaw = 0f;
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
            if (lookAllowed && !PauseMenuController.IsAnyPaused)
            {
                ReadLookInput(Mathf.Max(0f, deltaTime), !wasLookAllowed);
            }

            wasLookAllowed = lookAllowed;
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

        /// <summary>A look turn in degrees, clamped to the cone; the same path serves mouse, stick and tests.</summary>
        public void ApplyLookDelta(float yawDegrees, float pitchDegrees)
        {
            if (!IsActive || !IsFinite(yawDegrees) || !IsFinite(pitchDegrees))
            {
                return;
            }

            lookYaw = Mathf.Clamp(
                lookYaw + yawDegrees,
                -MaximumLookYawDegrees,
                MaximumLookYawDegrees);
            lookPitch = Mathf.Clamp(
                lookPitch + pitchDegrees,
                MinimumLookPitchDegrees,
                MaximumLookPitchDegrees);
        }

        public void End()
        {
            RestoreHead();
            RestoreCursor();
            IsActive = false;
            wasLookAllowed = false;
            lookYaw = 0f;
            lookPitch = 0f;
        }

        private void UpdateCameraPose()
        {
            cameraPosition = registry.Anchors.Mouth.position +
                Vector3.up * EyeHeightAboveMouth;
            cameraRotation = actor.rotation *
                Quaternion.Euler(basePitch + lookPitch, lookYaw, 0f);
        }

        private void ReadLookInput(float deltaTime, bool discardMouseDelta)
        {
            float yaw = 0f;
            float pitch = 0f;
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
