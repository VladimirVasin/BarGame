using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Drives a perspective chase camera directly behind the player's heading.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PlayerCameraFollow : MonoBehaviour
    {
        [Header("Exterior")]
        [SerializeField, Range(1f, 40f)] private float exteriorPitch = 14f;
        [SerializeField, Min(0.1f)] private float exteriorDistance = 5.5f;
        [SerializeField, Range(20f, 100f)] private float exteriorFieldOfView = 55f;
        [SerializeField] private float exteriorFocusHeight = 1.1f;

        [Header("Interior")]
        [SerializeField, Range(1f, 40f)] private float interiorPitch = 13f;
        [SerializeField, Min(0.1f)] private float interiorDistance = 3.8f;
        [SerializeField, Range(20f, 100f)] private float interiorFieldOfView = 60f;
        [SerializeField] private float interiorFocusHeight = 1.05f;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float yawSmoothTime = 0.08f;
        [SerializeField, Min(0f)] private float distanceRecoverySmoothTime = 0.18f;
        [SerializeField, Min(0f)] private float mouseYawSensitivity = 0.16f;
        [SerializeField, Min(0f)] private float gamepadYawSpeed = 150f;

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

        public bool OrbitInputEnabled { get; private set; } = true;

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
            Vector3 focusPoint = GetFocusPoint();
            Quaternion rotation = GetDesiredRotation();
            currentDistance = GetCollisionAdjustedDistance(focusPoint, rotation);
            ApplyPose(focusPoint, rotation);
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

            Quaternion desiredRotation = GetDesiredRotation();
            Vector3 focusPoint = GetFocusPoint();
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
            AlignTargetHeading(rotation);
            controlledCamera.transform.SetPositionAndRotation(
                focusPoint - rotation * Vector3.forward * currentDistance,
                rotation);
        }

        private void AlignTargetHeading(Quaternion rotation)
        {
            Vector3 heading = rotation * Vector3.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude > 0.001f)
            {
                followTarget.rotation = Quaternion.LookRotation(
                    heading,
                    Vector3.up);
            }
        }

        private Quaternion GetDesiredRotation()
        {
            float pitch = isInterior ? interiorPitch : exteriorPitch;
            return Quaternion.Euler(pitch, currentYaw, 0f);
        }

        private Vector3 GetFocusPoint()
        {
            float focusHeight = isInterior ? interiorFocusHeight : exteriorFocusHeight;
            return followTarget.position + Vector3.up * focusHeight;
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
