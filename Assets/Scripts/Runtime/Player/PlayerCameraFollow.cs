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
        [SerializeField, Min(0.1f)] private float exteriorDistance = 4.6f;
        [SerializeField, Range(20f, 100f)] private float exteriorFieldOfView = 53f;
        [SerializeField] private float exteriorFocusHeight = 1.1f;

        [Header("Interior")]
        [SerializeField, Range(1f, 40f)] private float interiorPitch = 13f;
        [SerializeField, Min(0.1f)] private float interiorDistance = 3.3f;
        [SerializeField, Range(20f, 100f)] private float interiorFieldOfView = 57f;
        [SerializeField] private float interiorFocusHeight = 1.05f;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float yawSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float focusSmoothTime = 0.1f;
        [SerializeField, Min(0f)] private float maximumFocusLag = 0.35f;
        [SerializeField, Min(0f)] private float teleportSnapDistance = 1.75f;
        [SerializeField, Min(0f)] private float distanceRecoverySmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float mouseYawSensitivity = 0.16f;
        [SerializeField, Min(0f)] private float gamepadYawSpeed = 150f;

        [Header("Cinematic Motion")]
        [SerializeField, Range(0f, 1f)] private float cinematicMotionAmount = 1f;
        [SerializeField, Min(0f)] private float cinematicBlendTime = 0.2f;
        [SerializeField, Min(0.1f)] private float fullMovementSpeed = 5.2f;
        [SerializeField, Min(0f)] private float movementSpeedSmoothTime = 0.1f;
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

        private readonly RaycastHit[] collisionHits = new RaycastHit[12];
        private Camera controlledCamera;
        private Transform followTarget;
        private bool isInterior;
        private float targetYaw;
        private float currentYaw;
        private float yawVelocity;
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

        public bool OrbitInputEnabled { get; private set; } = true;
        public bool CinematicMotionEnabled { get; private set; } = true;
        public Vector3 CurrentFocusPoint => currentFocusPoint;

        public void Initialize(Camera camera, Transform target, bool interior)
        {
            controlledCamera = camera != null ? camera : GetComponent<Camera>();
            followTarget = target;
            isInterior = interior;
            targetYaw = target != null ? target.eulerAngles.y : 0f;
            currentYaw = targetYaw;
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

        public void RotateYaw(float degrees)
        {
            targetYaw = Mathf.Repeat(targetYaw + degrees, 360f);
        }

        public void Snap()
        {
            if (controlledCamera == null || followTarget == null)
            {
                return;
            }

            currentYaw = targetYaw;
            yawVelocity = 0f;
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
            ReadYawInput(deltaTime);
            if (ShouldSnapForTeleport())
            {
                Snap();
                return;
            }

            UpdateMovementSpeed(deltaTime);
            UpdateFocusPoint(deltaTime);
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

        private void ApplyPose(Vector3 focusPoint, Quaternion rotation)
        {
            controlledCamera.transform.SetPositionAndRotation(
                focusPoint - rotation * Vector3.forward * currentDistance,
                rotation);
        }

        private Quaternion GetDesiredRotation(
            float pitchOffset = 0f,
            float rollOffset = 0f)
        {
            float pitch = isInterior ? interiorPitch : exteriorPitch;
            return Quaternion.Euler(
                pitch + pitchOffset,
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

        private void ReadYawInput(float deltaTime)
        {
            if (!OrbitInputEnabled)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                RotateYaw(mouse.delta.ReadValue().x * mouseYawSensitivity);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                RotateYaw(
                    gamepad.rightStick.ReadValue().x *
                    gamepadYawSpeed *
                    deltaTime);
            }
        }

        private void ConfigureCamera()
        {
            if (controlledCamera == null)
            {
                return;
            }

            controlledCamera.orthographic = false;
            controlledCamera.fieldOfView = isInterior
                ? interiorFieldOfView
                : exteriorFieldOfView;
        }
    }
}
