using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Drives a freely orbiting perspective chase camera around the player.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PlayerCameraFollow : MonoBehaviour
    {
        [Header("Exterior")]
        [SerializeField, Range(1f, 40f)] private float exteriorPitch = 14f;
        [SerializeField, Min(0.1f)] private float exteriorDistance = 2.6f;
        [SerializeField, Range(20f, 100f)] private float exteriorFieldOfView = 53f;
        [SerializeField] private float exteriorFocusHeight = 1.4f;

        [Header("Interior")]
        [SerializeField, Range(1f, 40f)] private float interiorPitch = 13f;
        [SerializeField, Min(0.1f)] private float interiorDistance = 2.2f;
        [SerializeField, Range(20f, 100f)] private float interiorFieldOfView = 57f;
        [SerializeField] private float interiorFocusHeight = 1.3f;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float yawSmoothTime = 0.2f;
        [SerializeField, Min(0f)] private float pitchSmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float focusSmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float maximumFocusLag = 0.45f;
        [SerializeField, Min(0f)] private float teleportSnapDistance = 1.75f;
        [SerializeField, Min(0f)] private float distanceRecoverySmoothTime = 0.32f;
        [SerializeField, Range(-40f, 0f)] private float minimumOrbitPitch = -20f;
        [SerializeField, Range(0f, 75f)] private float maximumOrbitPitch = 55f;
        [SerializeField, Min(0f)] private float mouseYawSensitivity = 0.16f;
        [SerializeField, Min(0f)] private float mousePitchSensitivity = 0.14f;
        [SerializeField, Min(0f)] private float gamepadYawSpeed = 150f;
        [SerializeField, Min(0f)] private float gamepadPitchSpeed = 120f;
        [SerializeField, Min(0f)] private float keyboardYawSpeed = 150f;
        [SerializeField, Min(0f)] private float keyboardPitchSpeed = 120f;

        [Header("Cinematic Motion")]
        [SerializeField, Range(0f, 1f)] private float cinematicMotionAmount = 1f;
        [SerializeField, Min(0f)] private float cinematicBlendTime = 0.3f;
        [SerializeField, Min(0.1f)] private float fullMovementSpeed = 5.2f;
        [SerializeField, Min(0f)] private float movementSpeedSmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float walkCyclesPerSecond = 1.65f;
        [SerializeField, Min(0f)] private float idleVerticalAmplitude = 0.004f;
        [SerializeField, Min(0f)] private float idleLateralAmplitude = 0.002f;
        [SerializeField, Min(0f)] private float idlePitchAmplitude = 0.05f;
        [SerializeField, Min(0f)] private float idleRollAmplitude = 0.08f;
        [SerializeField, Min(0f)] private float walkVerticalAmplitude = 0.016f;
        [SerializeField, Min(0f)] private float walkLateralAmplitude = 0.006f;
        [SerializeField, Min(0f)] private float walkPitchAmplitude = 0.1f;
        [SerializeField, Min(0f)] private float walkRollAmplitude = 0.25f;

        [Header("Collision")]
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float collisionPadding = 0.12f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private const float FixedReactionScale = 0.25f;

        private readonly RaycastHit[] collisionHits = new RaycastHit[12];
        private Camera controlledCamera;
        private Transform followTarget;
        private bool isInterior;
        private bool fixedPoseActive;
        private Vector3 fixedBasePosition;
        private Quaternion fixedBaseRotation = Quaternion.identity;
        private float fixedBaseFieldOfView = 57f;
        private float targetYaw;
        private float currentYaw;
        private float yawVelocity;
        private float targetPitch;
        private float currentPitch;
        private float pitchVelocity;
        private float currentDistance;
        private float distanceVelocity;
        private Vector3 currentFocusPoint;
        private Vector3 focusVelocity;
        private Vector3 previousTargetPosition;
        private float cinematicTime;
        private float walkPhase;
        private float movementSpeedWeight;
        private float movementSpeedVelocity;
        private float cinematicMotionWeight;
        private float cinematicBlendVelocity;
        private float targetIntoxication;
        private float currentIntoxication;
        private float balanceLean;
        private float fallDirection;
        private float fallAmount;

        public bool OrbitInputEnabled { get; private set; } = true;
        public bool CinematicMotionEnabled { get; private set; } = true;
        public Vector3 CurrentFocusPoint => currentFocusPoint;
        public bool FixedPoseActive => fixedPoseActive;
        public int CollisionLayerMask => collisionMask.value;
        public Pose FixedBasePose =>
            new Pose(
                fixedBasePosition,
                fixedBaseRotation);
        public Vector3 FixedBasePosition => fixedBasePosition;
        public Quaternion FixedBaseRotation => fixedBaseRotation;
        public float FixedBaseFieldOfView => fixedBaseFieldOfView;
        public float FollowFieldOfView => isInterior
            ? interiorFieldOfView
            : exteriorFieldOfView;
        public float CurrentOrbitPitch => currentPitch;
        public float TargetOrbitPitch => targetPitch;
        public float MinimumOrbitPitch => Mathf.Min(
            minimumOrbitPitch,
            maximumOrbitPitch);
        public float MaximumOrbitPitch => Mathf.Max(
            minimumOrbitPitch,
            maximumOrbitPitch);

        public void Initialize(Camera camera, Transform target, bool interior)
        {
            controlledCamera = camera != null ? camera : GetComponent<Camera>();
            followTarget = target;
            isInterior = interior;
            collisionMask =
                collisionMask.value &
                CityPedestrianCollision.NonPedestrianMask &
                CityBusCollision.NonBusMask;
            fixedPoseActive = false;
            targetYaw = target != null ? target.eulerAngles.y : 0f;
            currentYaw = targetYaw;
            targetPitch = ClampOrbitPitch(
                isInterior ? interiorPitch : exteriorPitch);
            currentPitch = targetPitch;
            ConfigureCamera();
            Snap();
        }

        public void SetOrbitInputEnabled(bool enabled)
        {
            OrbitInputEnabled = enabled;
        }

        public void SetCinematicMotionEnabled(bool enabled)
        {
            CinematicMotionEnabled = enabled;
        }

        public void SetIntoxication(float normalized)
        {
            targetIntoxication = Mathf.Clamp01(normalized);
        }

        public void SetBalanceReaction(
            float signedLean,
            float signedFallDirection,
            float normalizedFallAmount)
        {
            balanceLean = Mathf.Clamp(signedLean, -1f, 1f);
            if (!Mathf.Approximately(signedFallDirection, 0f))
            {
                fallDirection = Mathf.Sign(signedFallDirection);
            }

            fallAmount = Mathf.Clamp01(normalizedFallAmount);
        }

        public void SetFixedPose(
            Vector3 position,
            Quaternion rotation,
            float fieldOfView)
        {
            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    "Fixed camera position must be finite.");
            }

            if (!IsValidRotation(rotation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rotation),
                    "Fixed camera rotation must be finite and non-zero.");
            }

            if (!IsFinite(fieldOfView) ||
                fieldOfView < 20f ||
                fieldOfView > 100f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fieldOfView),
                    "Fixed camera field of view must be between 20 and 100 degrees.");
            }

            fixedBasePosition = position;
            fixedBaseRotation = Normalize(rotation);
            fixedBaseFieldOfView = fieldOfView;
            fixedPoseActive = true;
            currentFocusPoint =
                fixedBasePosition +
                fixedBaseRotation * Vector3.forward;
            ApplyFixedPose(fixedBaseRotation);
            ConfigureCamera();
        }

        public void ClearFixedPose()
        {
            if (!fixedPoseActive)
            {
                return;
            }

            fixedPoseActive = false;
            Snap();
        }

        /// <summary>
        /// Resolves the ordinary chase-camera pose for a prospective target
        /// root without changing the current fixed/free camera state. Moving
        /// contextual interactions use this to blend back to gameplay before
        /// releasing their fixed-pose ownership.
        /// </summary>
        public Pose ResolveFollowPose(Vector3 targetRootPosition)
        {
            if (controlledCamera == null)
            {
                throw new InvalidOperationException(
                    "Initialize the player camera before resolving a " +
                    "follow pose.");
            }

            if (!IsFinite(targetRootPosition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetRootPosition),
                    "The prospective follow target must be finite.");
            }

            float focusHeight = isInterior
                ? interiorFocusHeight
                : exteriorFocusHeight;
            Vector3 focusPoint =
                targetRootPosition + Vector3.up * focusHeight;
            Quaternion rotation = GetDesiredRotation();
            float distance = GetCollisionAdjustedDistance(
                focusPoint,
                rotation);
            return new Pose(
                focusPoint - rotation * Vector3.forward * distance,
                rotation);
        }

        public void RotateYaw(float degrees)
        {
            if (fixedPoseActive)
            {
                return;
            }

            targetYaw = Mathf.Repeat(targetYaw + degrees, 360f);
        }

        public void RotatePitch(float degrees)
        {
            if (fixedPoseActive)
            {
                return;
            }

            targetPitch = ClampOrbitPitch(targetPitch + degrees);
        }

        /// <summary>
        /// Samples the same orbit controls used by the ordinary chase camera.
        /// A camera owner can consume both axes while a fixed pose is active;
        /// modal UI continues to suppress the sample through
        /// <see cref="OrbitInputEnabled"/>. A caller whose own controls
        /// live on the arrow keys (the park board cursor) opts out of
        /// the keyboard branch.
        /// </summary>
        public Vector2 SampleOrbitInputDegrees(
            float unscaledDeltaTime,
            bool includeKeyboard = true)
        {
            if (!OrbitInputEnabled)
            {
                return Vector2.zero;
            }

            Vector2 degrees = Vector2.zero;
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                degrees.x += delta.x * mouseYawSensitivity;
                degrees.y -= delta.y * mousePitchSensitivity;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                float deltaTime = Mathf.Max(0f, unscaledDeltaTime);
                degrees.x += stick.x * gamepadYawSpeed * deltaTime;
                degrees.y -= stick.y * gamepadPitchSpeed * deltaTime;
            }

            // The arrow keys are a normalized axis like the stick, so
            // they take the stick's per-second scaling, not the mouse's
            // per-pixel one. Up looks up, matching stick-up.
            Keyboard keyboard = Keyboard.current;
            if (includeKeyboard && keyboard != null)
            {
                float yawAxis =
                    (keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.leftArrowKey.isPressed ? 1f : 0f);
                float pitchAxis =
                    (keyboard.upArrowKey.isPressed ? 1f : 0f) -
                    (keyboard.downArrowKey.isPressed ? 1f : 0f);
                if (yawAxis != 0f || pitchAxis != 0f)
                {
                    float deltaTime = Mathf.Max(0f, unscaledDeltaTime);
                    degrees.x += yawAxis * keyboardYawSpeed * deltaTime;
                    degrees.y -=
                        pitchAxis * keyboardPitchSpeed * deltaTime;
                }
            }

            return degrees;
        }

        public void Snap()
        {
            if (controlledCamera == null)
            {
                return;
            }

            if (fixedPoseActive)
            {
                ApplyFixedPose(fixedBaseRotation);
                ConfigureCamera();
                return;
            }

            if (followTarget == null)
            {
                return;
            }

            currentYaw = targetYaw;
            yawVelocity = 0f;
            currentPitch = targetPitch;
            pitchVelocity = 0f;
            distanceVelocity = 0f;
            focusVelocity = Vector3.zero;
            movementSpeedWeight = 0f;
            movementSpeedVelocity = 0f;
            cinematicMotionWeight = 0f;
            cinematicBlendVelocity = 0f;
            currentFocusPoint = GetTargetFocusPoint();
            previousTargetPosition = followTarget.position;
            Quaternion rotation = GetDesiredRotation();
            currentDistance = GetCollisionAdjustedDistance(
                currentFocusPoint,
                rotation);
            ApplyPose(currentFocusPoint, rotation);
            ConfigureCamera();
        }

        private void LateUpdate()
        {
            if (controlledCamera == null || followTarget == null)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            if (fixedPoseActive)
            {
                UpdateFixedPose(deltaTime);
                return;
            }

            ReadOrbitInput(deltaTime);
            if (ShouldSnapForTeleport())
            {
                Snap();
                return;
            }

            UpdateMovementSpeed(deltaTime);
            UpdateFocusPoint(deltaTime);
            currentIntoxication = Mathf.MoveTowards(
                currentIntoxication,
                targetIntoxication,
                deltaTime / 0.7f);
            currentYaw = yawSmoothTime <= 0f
                ? targetYaw
                : Mathf.SmoothDampAngle(
                    currentYaw,
                    targetYaw,
                    ref yawVelocity,
                    yawSmoothTime,
                    Mathf.Infinity,
                    deltaTime);
            currentYaw = Mathf.Repeat(currentYaw, 360f);
            currentPitch = pitchSmoothTime <= 0f
                ? targetPitch
                : Mathf.SmoothDamp(
                    currentPitch,
                    targetPitch,
                    ref pitchVelocity,
                    pitchSmoothTime,
                    Mathf.Infinity,
                    deltaTime);
            currentPitch = ClampOrbitPitch(currentPitch);

            UpdateCinematicMotionWeight(deltaTime);
            EvaluateCinematicMotion(
                deltaTime,
                out Vector3 localFocusOffset,
                out float pitchOffset,
                out float rollOffset);
            Quaternion desiredRotation = GetDesiredRotation(
                pitchOffset,
                rollOffset);
            Vector3 focusPoint =
                currentFocusPoint + desiredRotation * localFocusOffset;
            float allowedDistance = GetCollisionAdjustedDistance(
                focusPoint,
                desiredRotation);
            if (allowedDistance <= currentDistance)
            {
                currentDistance = allowedDistance;
                distanceVelocity = 0f;
            }
            else
            {
                currentDistance = distanceRecoverySmoothTime <= 0f
                    ? allowedDistance
                    : Mathf.SmoothDamp(
                        currentDistance,
                        allowedDistance,
                        ref distanceVelocity,
                        distanceRecoverySmoothTime,
                        Mathf.Infinity,
                        deltaTime);
            }

            ApplyPose(focusPoint, desiredRotation);
            ConfigureCamera();
        }

        private void UpdateFixedPose(float deltaTime)
        {
            currentIntoxication = Mathf.MoveTowards(
                currentIntoxication,
                targetIntoxication,
                deltaTime / 0.7f);
            UpdateCinematicMotionWeight(deltaTime);
            cinematicTime += deltaTime;

            IntoxicationProfile intoxication =
                IntoxicationStageRules.Evaluate(
                    Mathf.RoundToInt(
                        currentIntoxication *
                        IntoxicationStageRules.MaximumLevel));
            float slowSway = Mathf.Sin(
                cinematicTime * 1.17f + 0.4f);
            float secondarySway = Mathf.Sin(
                cinematicTime * 0.73f + 2.1f);
            float reactionWeight =
                cinematicMotionWeight *
                FixedReactionScale;
            float pitchOffset =
                secondarySway *
                intoxication.CameraRollDegrees *
                0.22f *
                reactionWeight;
            float rollOffset =
                (slowSway *
                 intoxication.CameraRollDegrees +
                 balanceLean * 1.2f +
                 fallDirection * fallAmount * 1.8f) *
                reactionWeight;
            Quaternion reaction =
                Quaternion.Euler(
                    pitchOffset,
                    0f,
                    rollOffset);
            ApplyFixedPose(fixedBaseRotation * reaction);
            ConfigureCamera();
        }

        private void ApplyPose(Vector3 focusPoint, Quaternion rotation)
        {
            controlledCamera.transform.SetPositionAndRotation(
                focusPoint - rotation * Vector3.forward * currentDistance,
                rotation);
        }

        private void ApplyFixedPose(Quaternion rotation)
        {
            if (controlledCamera == null)
            {
                return;
            }

            controlledCamera.transform.SetPositionAndRotation(
                fixedBasePosition,
                rotation);
        }

        private Quaternion GetDesiredRotation(
            float pitchOffset = 0f,
            float rollOffset = 0f)
        {
            return Quaternion.Euler(
                currentPitch + pitchOffset,
                currentYaw,
                rollOffset);
        }

        private Vector3 GetTargetFocusPoint()
        {
            float focusHeight = isInterior ? interiorFocusHeight : exteriorFocusHeight;
            return followTarget.position + Vector3.up * focusHeight;
        }

        private bool ShouldSnapForTeleport()
        {
            if (teleportSnapDistance <= 0f)
            {
                previousTargetPosition = followTarget.position;
                return false;
            }

            Vector3 displacement =
                followTarget.position - previousTargetPosition;
            return displacement.sqrMagnitude >
                   teleportSnapDistance * teleportSnapDistance;
        }

        private void UpdateMovementSpeed(float deltaTime)
        {
            Vector3 displacement =
                followTarget.position - previousTargetPosition;
            displacement.y = 0f;
            previousTargetPosition = followTarget.position;
            float observedSpeed = deltaTime > 0.0001f
                ? displacement.magnitude / deltaTime
                : 0f;
            float targetWeight = fullMovementSpeed > 0f
                ? Mathf.Clamp01(observedSpeed / fullMovementSpeed)
                : 0f;
            movementSpeedWeight = movementSpeedSmoothTime <= 0f
                ? targetWeight
                : Mathf.SmoothDamp(
                    movementSpeedWeight,
                    targetWeight,
                    ref movementSpeedVelocity,
                    movementSpeedSmoothTime,
                    Mathf.Infinity,
                    deltaTime);
        }

        private void UpdateFocusPoint(float deltaTime)
        {
            Vector3 targetFocusPoint = GetTargetFocusPoint();
            currentFocusPoint = focusSmoothTime <= 0f
                ? targetFocusPoint
                : Vector3.SmoothDamp(
                    currentFocusPoint,
                    targetFocusPoint,
                    ref focusVelocity,
                    focusSmoothTime,
                    Mathf.Infinity,
                    deltaTime);

            Vector3 lag = targetFocusPoint - currentFocusPoint;
            if (maximumFocusLag > 0f &&
                lag.sqrMagnitude > maximumFocusLag * maximumFocusLag)
            {
                currentFocusPoint =
                    targetFocusPoint -
                    Vector3.ClampMagnitude(lag, maximumFocusLag);
            }
        }

        private void UpdateCinematicMotionWeight(float deltaTime)
        {
            float targetWeight = CinematicMotionEnabled
                ? cinematicMotionAmount
                : 0f;
            cinematicMotionWeight = cinematicBlendTime <= 0f
                ? targetWeight
                : Mathf.SmoothDamp(
                    cinematicMotionWeight,
                    targetWeight,
                    ref cinematicBlendVelocity,
                    cinematicBlendTime,
                    Mathf.Infinity,
                    deltaTime);
        }

        private void EvaluateCinematicMotion(
            float deltaTime,
            out Vector3 localFocusOffset,
            out float pitchOffset,
            out float rollOffset)
        {
            cinematicTime += deltaTime;
            walkPhase = Mathf.Repeat(
                walkPhase +
                deltaTime *
                walkCyclesPerSecond *
                Mathf.PI *
                2f *
                movementSpeedWeight,
                Mathf.PI * 2f);

            float idleVertical = Mathf.Sin(
                cinematicTime * Mathf.PI * 2f / 4.8f);
            float idleLateral = Mathf.Sin(
                cinematicTime * Mathf.PI * 2f / 7.1f + 1.2f);
            float idlePitch = Mathf.Sin(
                cinematicTime * Mathf.PI * 2f / 5.9f + 0.7f);
            float idleRoll = Mathf.Sin(
                cinematicTime * Mathf.PI * 2f / 6.7f + 2.1f);
            float walkVertical = -Mathf.Cos(walkPhase * 2f);
            float walkLateral = Mathf.Sin(walkPhase);
            float walkPitch = Mathf.Sin(walkPhase * 2f + 0.4f);
            float walkRoll = Mathf.Sin(walkPhase + 0.2f);
            float idleWeight = 1f - movementSpeedWeight * 0.35f;
            float weight = cinematicMotionWeight;

            localFocusOffset = new Vector3(
                (idleLateral * idleLateralAmplitude * idleWeight +
                 walkLateral *
                 walkLateralAmplitude *
                 movementSpeedWeight) *
                weight,
                (idleVertical * idleVerticalAmplitude * idleWeight +
                 walkVertical *
                 walkVerticalAmplitude *
                 movementSpeedWeight) *
                weight,
                0f);
            pitchOffset =
                (idlePitch * idlePitchAmplitude * idleWeight +
                 walkPitch *
                 walkPitchAmplitude *
                 movementSpeedWeight) *
                weight;
            rollOffset =
                (idleRoll * idleRollAmplitude * idleWeight +
                 walkRoll *
                 walkRollAmplitude *
                 movementSpeedWeight) *
                weight;

            IntoxicationProfile intoxication =
                IntoxicationStageRules.Evaluate(
                    Mathf.RoundToInt(
                        currentIntoxication *
                        IntoxicationStageRules.MaximumLevel));
            float intoxicationWeight =
                weight *
                currentIntoxication *
                currentIntoxication;
            float slowSway = Mathf.Sin(
                cinematicTime * 1.17f + 0.4f);
            float secondarySway = Mathf.Sin(
                cinematicTime * 0.73f + 2.1f);
            localFocusOffset.x +=
                slowSway *
                0.018f *
                intoxicationWeight;
            localFocusOffset.y +=
                secondarySway *
                0.008f *
                intoxicationWeight -
                fallAmount * 0.08f * weight;
            pitchOffset +=
                secondarySway *
                intoxication.CameraRollDegrees *
                0.22f *
                weight;
            rollOffset +=
                slowSway *
                intoxication.CameraRollDegrees *
                weight +
                balanceLean * 1.2f * weight +
                fallDirection * fallAmount * 1.8f * weight;
        }

        private float GetCollisionAdjustedDistance(
            Vector3 focusPoint,
            Quaternion rotation)
        {
            float idealDistance = isInterior ? interiorDistance : exteriorDistance;
            Vector3 direction = -(rotation * Vector3.forward);
            int hitCount = Physics.SphereCastNonAlloc(
                focusPoint,
                collisionRadius,
                direction,
                collisionHits,
                idealDistance,
                collisionMask,
                QueryTriggerInteraction.Ignore);
            float allowedDistance = idealDistance;

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = collisionHits[index];
                if (hit.collider == null || IsPlayerCollider(hit.collider.transform))
                {
                    continue;
                }

                allowedDistance = Mathf.Min(
                    allowedDistance,
                    Mathf.Max(0.01f, hit.distance - collisionPadding));
            }

            return allowedDistance;
        }

        private bool IsPlayerCollider(Transform candidate)
        {
            return followTarget != null &&
                   (candidate == followTarget || candidate.IsChildOf(followTarget));
        }

        private void ReadOrbitInput(float deltaTime)
        {
            Vector2 input = SampleOrbitInputDegrees(deltaTime);
            RotateYaw(input.x);
            RotatePitch(input.y);
        }

        private float ClampOrbitPitch(float pitch)
        {
            return Mathf.Clamp(
                pitch,
                MinimumOrbitPitch,
                MaximumOrbitPitch);
        }

        private void ConfigureCamera()
        {
            if (controlledCamera == null)
            {
                return;
            }

            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = fixedPoseActive
                ? fixedBaseFieldOfView
                : isInterior
                    ? interiorFieldOfView
                    : exteriorFieldOfView;
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

        private static bool IsValidRotation(
            Quaternion value)
        {
            if (!IsFinite(value))
            {
                return false;
            }

            float magnitudeSquared =
                QuaternionMagnitudeSquared(value);
            return IsFinite(magnitudeSquared) &&
                   magnitudeSquared > 0.000001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static float QuaternionMagnitudeSquared(
            Quaternion value)
        {
            return value.x * value.x +
                   value.y * value.y +
                   value.z * value.z +
                   value.w * value.w;
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float inverseMagnitude =
                1f /
                Mathf.Sqrt(
                    QuaternionMagnitudeSquared(value));
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }
    }
}
