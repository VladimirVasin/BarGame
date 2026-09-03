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
        private const float DriftGroundBias = 0.001f;

        public const float InteractionPositionTolerance = 0.015f;
        public const float InteractionVerticalTolerance = 0.02f;
        public const float InteractionRotationToleranceDegrees = 0.5f;

        private CharacterController controller;
        private IWalkableArea walkableArea;
        private IPlayerFootstepSurface footstepSurface;
        private IPlayerMotionPresentation presentation;
        private float verticalSpeed;
        private float speedMultiplier = 1f;
        private float footstepDistance;
        // The balance model's contribution: a root velocity carried
        // through the same constraint and controller as the player's own
        // motion, but never folded into next frame's momentum, so a wall
        // that stops the drift cannot fling him the other way.
        private Vector3 balanceDrift;
        private float balanceYawScale = 1f;
        private Vector3 momentumVelocity;
        private PlayerMotorContactSample lastContact;
        private bool sideHitThisMove;
        private Vector3 sideHitNormal;
        private Vector3 sideHitPoint;
        private bool interactionPoseMoveActive;
        private float interactionPoseStallSeconds;
        private Vector3 lastInteractionPosePosition;
        private Quaternion lastInteractionPoseRotation;

        public bool InputEnabled { get; private set; } = true;

        /// <summary>
        /// Grounded as of the player's own move this frame. The balance
        /// drift is a second, purely planar move whose collision flags
        /// would otherwise report "nothing below" and stall everything
        /// that asks — the model, the fall gate, the vertical speed.
        /// </summary>
        public bool IsGrounded =>
            controller != null &&
            (controller.isGrounded || groundedAfterMainMove);

        private bool groundedAfterMainMove;
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

        /// <summary>
        /// Hands the footstep to whatever the hero is standing ON, if the
        /// area has an opinion. A surface that answers owns the step - sound
        /// and effect both - and the default is played only when nothing
        /// does, so an area cannot accidentally double it.
        /// </summary>
        public void SetFootstepSurface(IPlayerFootstepSurface surface)
        {
            footstepSurface = surface;
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            speedMultiplier = Mathf.Clamp(multiplier, 0f, 2f);
        }

        /// <summary>The yaw axis the player is holding this frame.</summary>
        public float CurrentTurnInput { get; private set; }

        /// <summary>What the capsule met during its last move.</summary>
        public PlayerMotorContactSample LastContact => lastContact;

        /// <summary>
        /// The balance model's root velocity for the coming move, world
        /// planar metres per second. Applied through the walkable
        /// constraint and the controller like any other motion, and
        /// cleared once used — the balance controller sets it every frame.
        /// </summary>
        public void SetBalanceDrift(Vector3 worldPlanarVelocity)
        {
            worldPlanarVelocity.y = 0f;
            balanceDrift = IsFinite(worldPlanarVelocity)
                ? worldPlanarVelocity
                : Vector3.zero;
        }

        /// <summary>
        /// Scales the tank yaw while the hero is fighting for balance, so
        /// leaning into a fall with A/D recovers him instead of spinning
        /// him into the wall he is trying to catch.
        /// </summary>
        public void SetBalanceYawScale(float scale)
        {
            balanceYawScale = float.IsNaN(scale)
                ? 1f
                : Mathf.Clamp(scale, 0.2f, 1f);
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
            momentumVelocity = PlanarVelocity;
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
            CurrentTurnInput = turnInput;
            float yawDelta =
                turnInput * TurnSpeedDegreesPerSecond *
                speedMultiplier * balanceYawScale * Time.deltaTime;
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
            // run cap and would visibly skid out of its Run gait. The
            // momentum is the player's own achieved motion; the balance
            // drift moves the capsule in a second, separate move below and
            // is never re-integrated here.
            Vector3 steeredPlanarVelocity =
                Quaternion.AngleAxis(yawDelta, Vector3.up) *
                momentumVelocity;
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
            Vector3 constraintPush = desired - constrained;
            constraintPush.y = 0f;

            UpdateVerticalSpeed();

            sideHitThisMove = false;
            Vector3 before = transform.position;
            Vector3 planarDelta = constrained - current;
            planarDelta.y = 0f;
            controller.Move(planarDelta + (Vector3.up * verticalSpeed * Time.deltaTime));
            CollisionFlags flags = controller.collisionFlags;
            groundedAfterMainMove = controller.isGrounded;

            float inverseDelta = Time.deltaTime > 0.0001f ? 1f / Time.deltaTime : 0f;
            Vector3 momentumDisplacement = transform.position - before;
            momentumDisplacement.y = 0f;
            momentumVelocity = momentumDisplacement * inverseDelta;

            Vector3 driftDisplacement = Vector3.zero;
            Vector3 drift = balanceDrift;
            balanceDrift = Vector3.zero;
            if (drift.sqrMagnitude > 0.000001f && Time.deltaTime > 0f)
            {
                Vector3 driftStart = transform.position;
                Vector3 driftDesired = driftStart + drift * Time.deltaTime;
                Vector3 driftConstrained = walkableArea == null
                    ? driftDesired
                    : walkableArea.Constrain(
                        driftStart,
                        driftDesired,
                        controller.radius);
                Vector3 driftPush = driftDesired - driftConstrained;
                driftPush.y = 0f;
                constraintPush += driftPush;
                Vector3 driftDelta = driftConstrained - driftStart;
                driftDelta.y = 0f;
                // A hair of downward push keeps the capsule in contact
                // through the second move, so the controller keeps
                // reporting the ground under him.
                controller.Move(driftDelta + Vector3.down * DriftGroundBias);
                flags |= controller.collisionFlags;
                groundedAfterMainMove |= controller.isGrounded;
                driftDisplacement = transform.position - driftStart;
                driftDisplacement.y = 0f;
            }

            lastContact = PlayerMotorContactSample.From(
                flags,
                sideHitThisMove,
                sideHitNormal,
                sideHitPoint,
                constraintPush);

            Vector3 planarVelocity = momentumDisplacement + driftDisplacement;
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
            if (IsGrounded && verticalSpeed < 0f)
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
            momentumVelocity = Vector3.zero;
            balanceDrift = Vector3.zero;
            lastContact = default;
            footstepDistance = 0f;
            presentation?.SetMotion(PlayerMotionSample.Stationary);
        }

        private void OnDisable()
        {
            verticalSpeed = 0f;
            ResetInteractionPoseMove();
            StopPlanarMotion();
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Walls only: floors and ceilings are the controller's own
            // business. The flattest side normal of the move wins so a
            // graze along a kerb face does not masquerade as the wall he
            // is leaning on.
            if (hit == null ||
                hit.collider == null ||
                hit.collider.isTrigger ||
                Mathf.Abs(hit.normal.y) >= 0.5f)
            {
                return;
            }

            if (!sideHitThisMove ||
                Mathf.Abs(hit.normal.y) < Mathf.Abs(sideHitNormal.y))
            {
                sideHitThisMove = true;
                sideHitNormal = hit.normal;
                sideHitPoint = hit.point;
            }
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
            Vector3 at = transform.position;
            if (footstepSurface != null &&
                footstepSurface.TryPlayFootstep(at, runBlend))
            {
                return;
            }

            RetroAudio.PlayAt(RetroSfxId.Footstep, at);
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

    /// <summary>
    /// What the capsule met during one move: a wall it touched sideways, or
    /// a walkable-area boundary that refused part of the motion (the
    /// interiors have no wall colliders, only a rectangle). Either is a
    /// wall to the balance model.
    /// </summary>
    public readonly struct PlayerMotorContactSample
    {
        public PlayerMotorContactSample(
            bool hasSideCollision,
            bool hasAreaRefusal,
            Vector3 normal,
            Vector3 point,
            Vector3 constraintPush,
            CollisionFlags flags)
        {
            HasSideCollision = hasSideCollision;
            HasAreaRefusal = hasAreaRefusal;
            Normal = normal;
            Point = point;
            ConstraintPush = constraintPush;
            Flags = flags;
        }

        public static PlayerMotorContactSample From(
            CollisionFlags flags,
            bool sideHit,
            Vector3 sideNormal,
            Vector3 sidePoint,
            Vector3 constraintPush)
        {
            bool hasSideCollision =
                sideHit || (flags & CollisionFlags.Sides) != 0;
            bool hasAreaRefusal = constraintPush.sqrMagnitude > 0.000004f;
            Vector3 normal = Vector3.zero;
            if (sideHit)
            {
                normal = sideNormal;
                normal.y = 0f;
            }
            else if (hasAreaRefusal)
            {
                normal = -constraintPush;
            }

            if (normal.sqrMagnitude > 0.000001f)
            {
                normal.Normalize();
            }

            return new PlayerMotorContactSample(
                hasSideCollision,
                hasAreaRefusal,
                normal,
                sideHit ? sidePoint : Vector3.zero,
                constraintPush,
                flags);
        }

        /// <summary>The controller reported a sideways collision.</summary>
        public bool HasSideCollision { get; }

        /// <summary>The walkable area clamped the desired motion.</summary>
        public bool HasAreaRefusal { get; }

        /// <summary>Planar normal pointing away from the wall, or zero.</summary>
        public Vector3 Normal { get; }

        /// <summary>World contact point of a physical side hit, or zero.</summary>
        public Vector3 Point { get; }

        /// <summary>Metres of motion the area refused this frame.</summary>
        public Vector3 ConstraintPush { get; }
        public CollisionFlags Flags { get; }

        public bool HasWall =>
            (HasSideCollision || HasAreaRefusal) &&
            Normal.sqrMagnitude > 0.5f;
    }
}
