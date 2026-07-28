using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed class PlayerMotor : MonoBehaviour
    {
        private const float MoveSpeed = 2.6f;
        private const float Acceleration = 6.5f;
        private const float Deceleration = 11f;
        private const float Gravity = 24f;
        private const float FootstepStride = 1.35f;
        private const float FootstepMinimumSpeedSquared = 0.36f;
        private const float FacingThresholdSquared = 0.0004f;

        private CharacterController controller;
        private Camera movementCamera;
        private IWalkableArea walkableArea;
        private PlayerSpriteRig spriteRig;
        private float verticalSpeed;
        private float speedMultiplier = 1f;
        private float footstepDistance;

        public bool InputEnabled { get; private set; } = true;
        public float SpeedMultiplier => speedMultiplier;
        public Vector3 PlanarVelocity { get; private set; }

        public void Initialize(
            Camera cameraToUse,
            IWalkableArea area,
            PlayerSpriteRig visual)
        {
            controller = GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.minMoveDistance = 0f;
            }

            movementCamera = cameraToUse;
            walkableArea = area;
            spriteRig = visual;
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled)
            {
                StopPlanarMotion();
            }
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            speedMultiplier = Mathf.Clamp(multiplier, 0f, 2f);
        }

        public void Teleport(Vector3 position)
        {
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }

            transform.position = position;
            verticalSpeed = 0f;
            StopPlanarMotion();

            if (controller != null)
            {
                controller.enabled = wasEnabled;
            }
        }

        private void Update()
        {
            if (controller == null)
            {
                return;
            }

            bool isTransitioning =
                SceneTransitionService.IsTransitioning;
            if (isTransitioning)
            {
                StopPlanarMotion();
            }

            Vector2 input = InputEnabled && !isTransitioning
                ? ReadMovement()
                : Vector2.zero;
            Vector3 desiredDirection = CameraRelativeDirection(input);
            Vector3 desiredPlanarVelocity =
                desiredDirection * MoveSpeed * speedMultiplier;
            float velocityChangeRate = GetVelocityChangeRate(
                PlanarVelocity,
                desiredPlanarVelocity);
            Vector3 inertialPlanarVelocity = Vector3.MoveTowards(
                PlanarVelocity,
                desiredPlanarVelocity,
                velocityChangeRate * Time.deltaTime);
            Vector3 current = transform.position;
            Vector3 desired =
                current + (inertialPlanarVelocity * Time.deltaTime);
            Vector3 constrained = walkableArea == null
                ? desired
                : walkableArea.Constrain(current, desired, controller.radius);

            if (controller.isGrounded && verticalSpeed < 0f)
            {
                verticalSpeed = -2f;
            }
            else
            {
                verticalSpeed -= Gravity * Time.deltaTime;
            }

            Vector3 before = transform.position;
            Vector3 planarDelta = constrained - current;
            planarDelta.y = 0f;
            controller.Move(planarDelta + (Vector3.up * verticalSpeed * Time.deltaTime));

            float inverseDelta = Time.deltaTime > 0.0001f ? 1f / Time.deltaTime : 0f;
            Vector3 planarVelocity = transform.position - before;
            planarVelocity.y = 0f;
            PlanarVelocity = planarVelocity * inverseDelta;
            FaceMovementDirection(PlanarVelocity);
            spriteRig?.SetMotion(PlanarVelocity);
            UpdateFootsteps(planarVelocity);
        }

        private static float GetVelocityChangeRate(
            Vector3 currentVelocity,
            Vector3 desiredVelocity)
        {
            if (desiredVelocity.sqrMagnitude <=
                FacingThresholdSquared)
            {
                return Deceleration;
            }

            if (currentVelocity.sqrMagnitude >
                    FacingThresholdSquared &&
                Vector3.Dot(currentVelocity, desiredVelocity) <= 0f)
            {
                return Deceleration;
            }

            return desiredVelocity.sqrMagnitude <
                   currentVelocity.sqrMagnitude
                ? Deceleration
                : Acceleration;
        }

        private void StopPlanarMotion()
        {
            PlanarVelocity = Vector3.zero;
            footstepDistance = 0f;
            spriteRig?.SetMotion(Vector3.zero);
        }

        private void UpdateFootsteps(Vector3 planarDisplacement)
        {
            if (!InputEnabled ||
                SceneTransitionService.IsTransitioning ||
                PlanarVelocity.sqrMagnitude <
                FootstepMinimumSpeedSquared)
            {
                footstepDistance = Mathf.Min(
                    footstepDistance,
                    FootstepStride * 0.35f);
                return;
            }

            footstepDistance += planarDisplacement.magnitude;
            if (footstepDistance < FootstepStride)
            {
                return;
            }

            footstepDistance %= FootstepStride;
            RetroAudio.PlayAt(
                RetroSfxId.Footstep,
                transform.position);
        }

        private void FaceMovementDirection(Vector3 planarVelocity)
        {
            planarVelocity.y = 0f;
            if (planarVelocity.sqrMagnitude <= FacingThresholdSquared)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(
                planarVelocity.normalized,
                Vector3.up);
        }

        private Vector3 CameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.001f)
            {
                return Vector3.zero;
            }

            Camera cameraToUse = movementCamera != null ? movementCamera : Camera.main;
            Vector3 forward = cameraToUse == null
                ? Vector3.forward
                : cameraToUse.transform.forward;
            Vector3 right = cameraToUse == null
                ? Vector3.right
                : cameraToUse.transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
            return Vector3.ClampMagnitude(
                (right * input.x) + (forward * input.y),
                1f);
        }

        private static Vector2 ReadMovement()
        {
            Vector2 movement = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                movement.x =
                    (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                movement.y =
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.leftStick.ReadValue().sqrMagnitude > movement.sqrMagnitude)
            {
                movement = gamepad.leftStick.ReadValue();
            }

            return Vector2.ClampMagnitude(movement, 1f);
        }
    }
}
