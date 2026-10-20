using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>One scene-owned inspection camera. The hero stays in the world and time keeps running.</summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class DebugFreeCamera : MonoBehaviour
    {
        private const float MoveSpeed = 4f;
        private const float FastMultiplier = 4f;
        private const float MouseSensitivity = .16f;
        private static DebugFreeCamera activeController;
        private static int inputResumeFrame = -1;
        private static bool awaitingControlRelease;
        private PlayerRuntime player;
        private Camera viewCamera;
        private PlayerCameraFollow follow;
        private IDisposable cameraSuspension;
        private Pose flyingPose;
        private float originalFieldOfView;
        private float yaw;
        private float pitch;
        private int mouseResumeFrame;

        public bool IsInitialized { get; private set; }
        public bool IsActive { get; private set; }
        public static bool IsAnyActive => activeController != null && activeController.IsActive;
        public static bool IsInspecting(GameObject subject) => IsAnyActive && activeController.player.GameObject == subject;

        internal static bool TryGetCapturedInput(GameObject subject, out bool motorInput, out bool interactorInput)
        {
            motorInput = interactorInput = false;
            if (!IsInspecting(subject)) return false;
            // Flight reserves device input without changing these flags.
            // A terminal actor therefore captures the current action's
            // baseline, including an action that finished during inspection.
            motorInput = activeController.player.Motor != null && activeController.player.Motor.InputEnabled;
            interactorInput = activeController.player.Interactor != null && activeController.player.Interactor.InputEnabled;
            return true;
        }

        /// <summary>Flight controls must be released before they can become hero movement or an E action.</summary>
        public static bool BlocksWorldInput
        {
            get
            {
                if (IsAnyActive || Time.frameCount <= inputResumeFrame) return true;
                if (awaitingControlRelease && !FlightControlsHeld()) awaitingControlRelease = false;
                return awaitingControlRelease;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeController = null;
            inputResumeFrame = -1;
            awaitingControlRelease = false;
        }

        public void Initialize(PlayerRuntime runtime, Camera camera)
        {
            if (runtime.GameObject == null || runtime.Motor == null || runtime.Interactor == null || camera == null)
                throw new ArgumentException("The debug camera requires an initialized player and scene camera.");
            Exit();
            player = runtime;
            viewCamera = camera;
            IsInitialized = true;
        }

        public bool TryEnter()
        {
            if (!IsInitialized || !isActiveAndEnabled || IsActive || IsAnyActive ||
                player.GameObject == null || viewCamera == null || !viewCamera.isActiveAndEnabled ||
                !GameCursor.ApplicationFocused || SceneTransitionService.IsTransitioning ||
                GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) return false;

            // PlayerFactory runs before each scene wires its camera follow.
            if (follow == null) follow = viewCamera.GetComponent<PlayerCameraFollow>();
            if (follow == null) return false;

            flyingPose = new Pose(viewCamera.transform.position, viewCamera.transform.rotation);
            originalFieldOfView = viewCamera.fieldOfView;
            cameraSuspension = follow.SuspendPresentation();
            Vector3 euler = flyingPose.rotation.eulerAngles;
            yaw = euler.y;
            pitch = Mathf.DeltaAngle(0f, euler.x);
            mouseResumeFrame = Time.frameCount + 1;
            IsActive = true;
            activeController = this;
            // Contextual phases may acquire new priority-10 cursor leases
            // while their world action continues underneath this view.
            GameCursor.Capture(this, CursorLockMode.Locked, false, 20);
            return true;
        }

        public bool Exit()
        {
            if (!IsActive) return false;
            IsActive = false;
            if (activeController == this) activeController = null;
            inputResumeFrame = Time.frameCount + 1;
            awaitingControlRelease = true;
            GameCursor.Release(this);
            cameraSuspension?.Dispose();
            cameraSuspension = null;
            // The action may have finished, moved to another shot or died
            // during flight. Its current state owns the return, never an
            // obsolete camera or input snapshot from entry.
            if (follow != null && !follow.PresentationSuspended) follow.Snap();
            return true;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (IsActive)
            {
                if (viewCamera == null || player.GameObject == null || follow == null ||
                    SceneTransitionService.IsTransitioning ||
                    GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused ||
                    (keyboard != null && (keyboard.f4Key.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)))
                    Exit();
            }
            else if (keyboard != null && keyboard.f4Key.wasPressedThisFrame)
            {
                TryEnter();
            }
        }

        private void LateUpdate()
        {
            if (!IsActive || viewCamera == null) return;
            if (!GameCursor.ApplicationFocused)
            {
                mouseResumeFrame = Time.frameCount + 1;
                return;
            }
            Mouse mouse = Mouse.current;
            Vector2 delta = mouse != null && Time.frameCount > mouseResumeFrame
                ? mouse.delta.ReadValue() : Vector2.zero;
            if (delta.sqrMagnitude > 0f)
            {
                yaw += delta.x * MouseSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * MouseSensitivity, -89f, 89f);
                flyingPose.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Vector3 local = new Vector3(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f), 0f,
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                Vector3 movement = flyingPose.rotation * local + Vector3.up *
                    ((keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f));
                float speed = MoveSpeed * (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                    ? FastMultiplier : 1f);
                flyingPose.position += Vector3.ClampMagnitude(movement, 1f) *
                    speed * Mathf.Min(Time.unscaledDeltaTime, .1f);
            }
            viewCamera.transform.SetPositionAndRotation(flyingPose.position, flyingPose.rotation);
            viewCamera.fieldOfView = originalFieldOfView;
        }

        private static bool FlightControlsHeld()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            return (keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed ||
                keyboard.sKey.isPressed || keyboard.dKey.isPressed || keyboard.qKey.isPressed || keyboard.eKey.isPressed)) ||
                (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed));
        }

        private void OnDisable() => Exit();
        private void OnDestroy() => Exit();
    }
}
