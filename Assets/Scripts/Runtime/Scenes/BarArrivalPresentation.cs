using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class BarArrivalPresentation : MonoBehaviour
    {
        private const float StickSkipThreshold = 0.18f;
        private Camera controlledCamera;
        private PlayerCameraFollow cameraFollow;
        private Vector3 establishingPosition;
        private Quaternion establishingRotation;
        private float establishingFieldOfView;
        private Vector3 pathControlPosition;
        private Vector3 followPosition;
        private Quaternion followRotation;
        private float followFieldOfView;
        private float duration;
        private float elapsedTime;
        private bool followWasEnabled;
        private bool orbitInputWasEnabled;

        public bool IsPlaying { get; private set; }
        public bool WasSkipped { get; private set; }
        public float Duration => duration;
        public float ElapsedTime => elapsedTime;
        public Camera ControlledCamera => controlledCamera;
        public PlayerCameraFollow CameraFollow => cameraFollow;

        public void Initialize(
            PlayerCameraFollow follow,
            Vector3 shotPosition,
            Vector3 lookAtPosition,
            float shotFieldOfView = 60f,
            float shotDuration = BarArrivalTimeline.DefaultDuration)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                throw new InvalidOperationException(
                    "A main camera is required for the bar arrival shot.");
            }

            Initialize(
                mainCamera,
                follow,
                shotPosition,
                lookAtPosition,
                shotFieldOfView,
                shotDuration);
        }

        public void Initialize(
            Camera camera,
            PlayerCameraFollow follow,
            Vector3 shotPosition,
            Vector3 lookAtPosition,
            float shotFieldOfView = 60f,
            float shotDuration = BarArrivalTimeline.DefaultDuration)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            if (follow == null)
            {
                throw new ArgumentNullException(nameof(follow));
            }

            RestoreCapturedState();

            controlledCamera = camera;
            cameraFollow = follow;
            followPosition = camera.transform.position;
            followRotation = camera.transform.rotation;
            followFieldOfView = camera.fieldOfView;
            followWasEnabled = follow.enabled;
            orbitInputWasEnabled = follow.OrbitInputEnabled;

            establishingPosition = shotPosition;
            Vector3 lookDirection = lookAtPosition - shotPosition;
            establishingRotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(
                    lookDirection.normalized,
                    Vector3.up)
                : followRotation;
            establishingFieldOfView = Mathf.Clamp(
                shotFieldOfView,
                20f,
                100f);
            pathControlPosition = Vector3.Lerp(
                establishingPosition,
                followPosition,
                0.55f);
            pathControlPosition.x = followPosition.x;
            pathControlPosition.y =
                Mathf.Max(
                    establishingPosition.y,
                    followPosition.y) +
                0.15f;
            duration = BarArrivalTimeline.ClampDuration(
                shotDuration);
            elapsedTime = 0f;
            WasSkipped = false;
            IsPlaying = true;

            follow.SetOrbitInputEnabled(false);
            follow.enabled = false;
            ApplyFrame(BarArrivalTimeline.Evaluate(0f, duration));
        }

        public void AdvancePresentation(float unscaledDeltaTime)
        {
            if (!IsPlaying)
            {
                return;
            }

            if (!float.IsNaN(unscaledDeltaTime) &&
                unscaledDeltaTime > 0f)
            {
                elapsedTime += unscaledDeltaTime;
            }

            BarArrivalFrame frame = BarArrivalTimeline.Evaluate(
                elapsedTime,
                duration);
            ApplyFrame(frame);
            if (frame.IsComplete)
            {
                RestoreCapturedState();
            }
        }

        public bool Skip()
        {
            if (!IsPlaying)
            {
                return false;
            }

            WasSkipped = true;
            RestoreCapturedState();
            return true;
        }

        private void Update()
        {
            if (!IsPlaying)
            {
                return;
            }

            if (HasSkipInput())
            {
                Skip();
                return;
            }

            AdvancePresentation(Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            RestoreCapturedState();
        }

        private void OnDestroy()
        {
            RestoreCapturedState();
        }

        private void ApplyFrame(BarArrivalFrame frame)
        {
            if (controlledCamera == null)
            {
                RestoreCapturedState();
                return;
            }

            float blend = frame.CameraBlend;
            float inverseBlend = 1f - blend;
            controlledCamera.transform.SetPositionAndRotation(
                inverseBlend *
                inverseBlend *
                establishingPosition +
                2f *
                inverseBlend *
                blend *
                pathControlPosition +
                blend *
                blend *
                followPosition,
                Quaternion.Slerp(
                    establishingRotation,
                    followRotation,
                    blend));
            controlledCamera.fieldOfView = Mathf.Lerp(
                establishingFieldOfView,
                followFieldOfView,
                blend);
        }

        private void RestoreCapturedState()
        {
            if (!IsPlaying)
            {
                return;
            }

            IsPlaying = false;

            if (controlledCamera != null)
            {
                controlledCamera.transform.SetPositionAndRotation(
                    followPosition,
                    followRotation);
                controlledCamera.fieldOfView = followFieldOfView;
            }

            if (cameraFollow != null)
            {
                cameraFollow.SetOrbitInputEnabled(
                    orbitInputWasEnabled);
                cameraFollow.enabled = followWasEnabled;
                if (followWasEnabled)
                {
                    cameraFollow.Snap();
                }
            }
        }

        private static bool HasSkipInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.wKey.isPressed ||
                 keyboard.aKey.isPressed ||
                 keyboard.sKey.isPressed ||
                 keyboard.dKey.isPressed ||
                 keyboard.upArrowKey.isPressed ||
                 keyboard.downArrowKey.isPressed ||
                 keyboard.leftArrowKey.isPressed ||
                 keyboard.rightArrowKey.isPressed ||
                 keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame ||
                 keyboard.f9Key.wasPressedThisFrame))
            {
                return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null &&
                (mouse.leftButton.wasPressedThisFrame ||
                 mouse.rightButton.isPressed))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return false;
            }

            float thresholdSquared =
                StickSkipThreshold * StickSkipThreshold;
            return
                gamepad.leftStick.ReadValue().sqrMagnitude >
                thresholdSquared ||
                gamepad.rightStick.ReadValue().sqrMagnitude >
                thresholdSquared ||
                gamepad.dpad.ReadValue().sqrMagnitude >
                thresholdSquared ||
                gamepad.buttonSouth.wasPressedThisFrame ||
                gamepad.startButton.wasPressedThisFrame;
        }
    }
}
