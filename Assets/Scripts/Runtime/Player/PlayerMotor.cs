using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed class PlayerMotor : MonoBehaviour
    {
        private const float MoveSpeed = 2.6f;
        private const float RunSpeed = 4.2f;
        private const float BackwardMoveSpeed = 1.4f;
        private const float TurnSpeedDegreesPerSecond = 150f;
        private const float InteractionTurnSpeedDegrees = 540f;
        private const float Acceleration = 6.5f;
        private const float Deceleration = 11f;
        private const float Gravity = 24f;
        private const float FootstepStride = 1.35f;
        private const float RunFootstepStride = 1.58f;
        private const float FootstepMinimumSpeedSquared = 0.36f;
        private const float FacingThresholdSquared = 0.0004f;
        private const float InteractionStallTimeoutSeconds = 1.5f;
        private const float InteractionProgressDistance = 0.0001f;
        private const float InteractionProgressDegrees = 0.05f;

        public const float InteractionPositionTolerance = 0.015f;
        public const float InteractionVerticalTolerance = 0.02f;
        public const float InteractionRotationToleranceDegrees = 0.5f;

        private CharacterController controller;
        private IWalkableArea walkableArea;
        private IPlayerMotionPresentation presentation;
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
            IWalkableArea area,
            IPlayerMotionPresentation visual)
        {
            controller = GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.minMoveDistance = 0f;
            }

            walkableArea = area;
            presentation = visual;
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
                WalkPlanarStep(targetPosition, deltaTime);
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

        /// <summary>
        /// Walks the rig towards an intermediate approach corner with the
        /// same constrained, visible gait as the pose move, completing as
        /// soon as the planar distance drops inside the arrival radius.
        /// Facing and height stay free: corners are passed through, not
        /// posed at, so the walk flows on into the next leg.
        /// </summary>
        public bool MoveTowardsApproachWaypoint(
            Vector3 targetPosition,
            float arrivalRadius,
            float deltaTime)
        {
            ValidateInteractionPose(
                targetPosition,
                Quaternion.identity,
                deltaTime);
            if (float.IsNaN(arrivalRadius) ||
                float.IsInfinity(arrivalRadius) ||
                arrivalRadius <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(arrivalRadius),
                    arrivalRadius,
                    "The arrival radius must be positive.");
            }

            if (!interactionPoseMoveActive)
            {
                interactionPoseStallSeconds = 0f;
                InteractionPoseMoveStalled = false;
                lastInteractionPosePosition = transform.position;
                lastInteractionPoseRotation = transform.rotation;
            }

            interactionPoseMoveActive = true;
            if (PlanarDistance(transform.position, targetPosition) <=
                arrivalRadius)
            {
                interactionPoseStallSeconds = 0f;
                return true;
            }

            WalkPlanarStep(targetPosition, deltaTime);
            return false;
        }

        private void WalkPlanarStep(
            Vector3 targetPosition,
            float deltaTime)
        {
            Vector3 current = transform.position;
            Vector3 toTarget = targetPosition - current;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (deltaTime <= 0f || distance <= 0.000001f)
            {
                StopPlanarMotion();
                RecordInteractionPoseProgress(deltaTime);
                return;
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
            // Scripted approaches always face along their travel, so the
            // presentation sees them as plain forward walking.
            presentation?.SetMotion(new PlayerMotionSample(
                PlanarVelocity,
                PlanarVelocity.magnitude,
                0f));
            UpdateFootsteps(
                displacement,
                allowWhenInputDisabled: true);
            RecordInteractionPoseProgress(deltaTime);
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
                presentation?.SetMotion(PlayerMotionSample.Stationary);
                return;
            }

            // Tank controls: A/D yaw the hero on the spot, W walks along
            // the hero's own forward axis and S backs up along it at a
            // reduced pace. The camera no longer steers locomotion.
            Vector2 input = InputEnabled && !isTransitioning
                ? ReadMovement()
                : Vector2.zero;
            bool sprintRequested = InputEnabled &&
                                   !isTransitioning &&
                                   IsSprintRequested();
            float turnInput = input.x;
            float yawDelta =
                turnInput * TurnSpeedDegreesPerSecond *
                speedMultiplier * Time.deltaTime;
            transform.Rotate(
                0f,
                yawDelta,
                0f);

            float desiredSpeed = input.y >= 0f
                ? input.y * (sprintRequested ? RunSpeed : MoveSpeed)
                : input.y * BackwardMoveSpeed;
            Vector3 desiredPlanarVelocity =
                transform.forward * (desiredSpeed * speedMultiplier);
            // Tank steering rotates the already-earned forward momentum with
            // the actor. Otherwise changing the velocity direction would
            // consume the same bounded acceleration that raises its speed;
            // at the canonical yaw rate a running arc could never reach the
            // run cap and would visibly skid out of its Run gait.
            Vector3 steeredPlanarVelocity =
                Quaternion.AngleAxis(yawDelta, Vector3.up) *
                PlanarVelocity;
            float velocityChangeRate = GetVelocityChangeRate(
                steeredPlanarVelocity,
                desiredPlanarVelocity);
            Vector3 inertialPlanarVelocity = Vector3.MoveTowards(
                steeredPlanarVelocity,
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
            float signedForwardSpeed =
                Vector3.Dot(PlanarVelocity, transform.forward);
            float runBlend = CalculateRunBlend(signedForwardSpeed);
            presentation?.SetMotion(new PlayerMotionSample(
                PlanarVelocity,
                signedForwardSpeed,
                turnInput,
                runBlend));
            UpdateFootsteps(planarVelocity, runBlend: runBlend);
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

        private float CalculateRunBlend(float signedForwardSpeed)
        {
            if (signedForwardSpeed <= 0f)
            {
                return 0f;
            }

            float walkCap = MoveSpeed * speedMultiplier;
            float runCap = RunSpeed * speedMultiplier;
            if (runCap <= walkCap + Mathf.Epsilon)
            {
                return 0f;
            }

            return Mathf.InverseLerp(
                walkCap,
                runCap,
                signedForwardSpeed);
        }

        private void StopPlanarMotion()
        {
            PlanarVelocity = Vector3.zero;
            footstepDistance = 0f;
            presentation?.SetMotion(PlayerMotionSample.Stationary);
        }

        private void OnDisable()
        {
            verticalSpeed = 0f;
            ResetInteractionPoseMove();
            StopPlanarMotion();
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
            bool allowWhenInputDisabled = false,
            float runBlend = 0f)
        {
            float stride = Mathf.Lerp(
                FootstepStride,
                RunFootstepStride,
                Mathf.Clamp01(runBlend));
            if ((!InputEnabled && !allowWhenInputDisabled) ||
                SceneTransitionService.IsTransitioning ||
                PlanarVelocity.sqrMagnitude <
                FootstepMinimumSpeedSquared)
            {
                footstepDistance = Mathf.Min(
                    footstepDistance,
                    stride * 0.35f);
                return;
            }

            footstepDistance += planarDisplacement.magnitude;
            if (footstepDistance < stride)
            {
                return;
            }

            footstepDistance %= stride;
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

        private static Vector2 ReadMovement()
        {
            Vector2 movement = Vector2.zero;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // WASD only: the arrow keys belong to the camera
                // orbit (PlayerCameraFollow), not to walking.
                movement.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                movement.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null && gamepad.leftStick.ReadValue().sqrMagnitude > movement.sqrMagnitude)
            {
                movement = gamepad.leftStick.ReadValue();
            }

            // The axes are independent channels now: X is yaw, Y is
            // travel. A combined W+A must keep full forward speed, so
            // no vector clamp across the pair.
            movement.x = Mathf.Clamp(movement.x, -1f, 1f);
            movement.y = Mathf.Clamp(movement.y, -1f, 1f);
            return movement;
        }

        private static bool IsSprintRequested()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.leftShiftKey.isPressed ||
                 keyboard.rightShiftKey.isPressed))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.leftStickButton.isPressed;
        }
    }
}
