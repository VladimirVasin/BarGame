using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed class PlayerMotor : MonoBehaviour
    {
        private const float MoveSpeed = 2.6f;
        private const float InteractionTurnSpeedDegrees = 540f;
        private const float Acceleration = 6.5f;
        private const float Deceleration = 11f;
        private const float Gravity = 24f;
        private const float FootstepStride = 1.35f;
        private const float FootstepMinimumSpeedSquared = 0.36f;
        private const float FacingThresholdSquared = 0.0004f;
        private const float InteractionStallTimeoutSeconds = 1.5f;
        private const float InteractionProgressDistance = 0.0001f;
        private const float InteractionProgressDegrees = 0.05f;

        public const float InteractionPositionTolerance = 0.015f;
        public const float InteractionVerticalTolerance = 0.02f;
        public const float InteractionRotationToleranceDegrees = 0.5f;

        private CharacterController controller;
        private Camera movementCamera;
        private IWalkableArea walkableArea;
        private PlayerSpriteRig spriteRig;
        private float verticalSpeed;
        private float speedMultiplier = 1f;
        private float footstepDistance;
        private bool interactionPoseMoveActive;
        private float interactionPoseStallSeconds;
        private Vector3 lastInteractionPosePosition;
        private Quaternion lastInteractionPoseRotation;

        public bool InputEnabled { get; private set; } = true;
        public bool IsGrounded =>
            controller != null && controller.isGrounded;
        public float SpeedMultiplier => speedMultiplier;
        public Vector3 PlanarVelocity { get; private set; }
        public bool InteractionPoseMoveActive =>
            interactionPoseMoveActive;
        public bool InteractionPoseMoveStalled { get; private set; }

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
            if (enabled)
            {
                CancelInteractionPoseMove();
            }

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
            ResetInteractionPoseMove();
            StopPlanarMotion();

            if (controller != null)
            {
                controller.enabled = wasEnabled;
            }
        }

        public void CancelInteractionPoseMove()
        {
            ResetInteractionPoseMove();
            StopPlanarMotion();
        }

        /// <summary>
        /// Moves the ordinary player rig to an authored interaction pose while
        /// player input is locked. The regular CharacterController, walkable
        /// constraint, facing, gait and footsteps remain in use, so this is a
        /// visible short approach rather than a hidden teleport.
        /// </summary>
        public bool MoveTowardsInteractionPose(
            Vector3 targetPosition,
            Quaternion targetRotation,
            float deltaTime)
        {
            ValidateInteractionPose(
                targetPosition,
                targetRotation,
                deltaTime);
            if (!interactionPoseMoveActive)
            {
                interactionPoseStallSeconds = 0f;
                InteractionPoseMoveStalled = false;
                lastInteractionPosePosition = transform.position;
                lastInteractionPoseRotation = transform.rotation;
            }

            interactionPoseMoveActive = true;
            targetRotation = NormalizeRotation(targetRotation);

            Vector3 current = transform.position;
            Vector3 toTarget = targetPosition - current;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance > 0.000001f)
            {
                if (deltaTime <= 0f)
                {
                    StopPlanarMotion();
                    RecordInteractionPoseProgress(deltaTime);
                    return false;
                }

                float step = Mathf.Min(
                    distance,
                    MoveSpeed * deltaTime);
                Vector3 desired =
                    current + (toTarget / distance) * step;
                desired.y = current.y;
                Vector3 constrained = walkableArea == null
                    ? desired
                    : walkableArea.Constrain(
                        current,
                        desired,
                        controller != null ? controller.radius : 0f);
                Vector3 planarDelta = constrained - current;
                planarDelta.y = 0f;
                Vector3 before = transform.position;
                if (controller != null && controller.enabled)
                {
                    controller.Move(planarDelta);
                }
                else
                {
                    transform.position += planarDelta;
                }

                Vector3 displacement = transform.position - before;
                displacement.y = 0f;
                PlanarVelocity = deltaTime > 0.0001f
                    ? displacement / deltaTime
                    : Vector3.zero;
                FaceMovementDirection(PlanarVelocity);
                spriteRig?.SetMotion(PlanarVelocity);
                UpdateFootsteps(
                    displacement,
                    allowWhenInputDisabled: true);
                RecordInteractionPoseProgress(deltaTime);
                return false;
            }

            StopPlanarMotion();
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                InteractionTurnSpeedDegrees * deltaTime);
            if (Quaternion.Angle(
                    transform.rotation,
                    targetRotation) <=
                InteractionRotationToleranceDegrees)
            {
                transform.rotation = targetRotation;
            }

            bool completed =
                PlanarDistance(
                    transform.position,
                    targetPosition) <=
                InteractionPositionTolerance &&
                Mathf.Abs(
                    transform.position.y - targetPosition.y) <=
                InteractionVerticalTolerance &&
                Quaternion.Angle(
                    transform.rotation,
                    targetRotation) <=
                InteractionRotationToleranceDegrees;
            if (completed)
            {
                interactionPoseMoveActive = false;
                interactionPoseStallSeconds = 0f;
                InteractionPoseMoveStalled = false;
            }
            else
            {
                RecordInteractionPoseProgress(deltaTime);
            }

            return completed;
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
                ResetInteractionPoseMove();
                StopPlanarMotion();
            }

            else if (interactionPoseMoveActive)
            {
                UpdateVerticalMotion();
                PlanarVelocity = Vector3.zero;
                spriteRig?.SetMotion(Vector3.zero);
                return;
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

            UpdateVerticalSpeed();

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

        private void UpdateVerticalMotion()
        {
            UpdateVerticalSpeed();
            controller.Move(Vector3.up * verticalSpeed * Time.deltaTime);
        }

        private void UpdateVerticalSpeed()
        {
            if (controller.isGrounded && verticalSpeed < 0f)
            {
                verticalSpeed = -2f;
            }
            else
            {
                verticalSpeed -= Gravity * Time.deltaTime;
            }
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

        private void RecordInteractionPoseProgress(float deltaTime)
        {
            bool progressed =
                PlanarDistance(
                    transform.position,
                    lastInteractionPosePosition) >=
                InteractionProgressDistance ||
                Quaternion.Angle(
                    transform.rotation,
                    lastInteractionPoseRotation) >=
                InteractionProgressDegrees;
            interactionPoseStallSeconds = progressed
                ? 0f
                : interactionPoseStallSeconds + deltaTime;
            lastInteractionPosePosition = transform.position;
            lastInteractionPoseRotation = transform.rotation;
            if (interactionPoseStallSeconds <
                InteractionStallTimeoutSeconds)
            {
                return;
            }

            InteractionPoseMoveStalled = true;
            interactionPoseMoveActive = false;
            StopPlanarMotion();
        }

        private void ResetInteractionPoseMove()
        {
            interactionPoseMoveActive = false;
            interactionPoseStallSeconds = 0f;
            InteractionPoseMoveStalled = false;
        }

        private void UpdateFootsteps(
            Vector3 planarDisplacement,
            bool allowWhenInputDisabled = false)
        {
            if ((!InputEnabled && !allowWhenInputDisabled) ||
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

        private static void ValidateInteractionPose(
            Vector3 targetPosition,
            Quaternion targetRotation,
            float deltaTime)
        {
            if (!IsFinite(targetPosition))
            {
                throw new System.ArgumentException(
                    "The interaction target position must be finite.",
                    nameof(targetPosition));
            }

            if (!IsFinite(targetRotation) ||
                QuaternionMagnitudeSquared(targetRotation) <= 0.000001f)
            {
                throw new System.ArgumentException(
                    "The interaction target rotation must be finite and " +
                    "non-zero.",
                    nameof(targetRotation));
            }

            if (!IsFinite(deltaTime) || deltaTime < 0f)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(deltaTime),
                    deltaTime,
                    "Delta time must be finite and non-negative.");
            }
        }

        private static Quaternion NormalizeRotation(Quaternion value)
        {
            float inverseMagnitude =
                1f / Mathf.Sqrt(QuaternionMagnitudeSquared(value));
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }

        private static float QuaternionMagnitudeSquared(Quaternion value)
        {
            return value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w;
        }

        private static float PlanarDistance(
            Vector3 first,
            Vector3 second)
        {
            first.y = 0f;
            second.y = 0f;
            return Vector3.Distance(first, second);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
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
