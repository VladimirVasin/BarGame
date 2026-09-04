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

        [Header("Drunk Dolly Zoom")]
        [SerializeField, Range(60f, 120f)] private float dollyZoomWideFieldOfView = 100f;
        [SerializeField, Range(20f, 50f)] private float dollyZoomNarrowFieldOfView = 34f;
        [SerializeField, Min(0f)] private float dollyZoomBlendTime = 0.35f;
        [SerializeField, Min(0f)] private float dollyZoomReleaseBlendTime = 0.45f;
        [SerializeField, Min(0f)] private float dollyZoomClearanceRecoveryTime = 0.32f;

        private const float FixedReactionScale = 0.25f;
        private const int DollyZoomSeedSalt = 0x5A17;
        private const float DollyZoomNarrowRoomFactor = 1.35f;
        private const float DollyZoomReleaseToleranceDegrees = 1.5f;
        private const float MinimumDollyDistance = 0.02f;

        private readonly RaycastHit[] collisionHits = new RaycastHit[24];
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
        private IntoxicationDollyZoomModel dollyZoom;
        private float dollyWeight;
        private float releaseExponent;
        private float releaseWeight;
        private float releaseVelocity;
        private float lastFreeDollyFieldOfView;
        private float dollyClearance;
        private float dollySmoothedClearance;
        private float dollyClearanceVelocity;
        private float dollyDistance;
        private float dollyFieldOfView;
        private float dollyAppliedExponent;

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

        /// <summary>
        /// The drunk dolly zoom's applied reach this frame, -1..1: positive
        /// is the wide lens with the camera pushed in, negative the narrow
        /// lens with the camera pulled back. Zero whenever a fixed pose
        /// owns the camera.
        /// </summary>
        public float DollyZoomExponent => dollyAppliedExponent;
        public IntoxicationDollyZoomPhase DollyZoomPhase =>
            dollyZoom != null
                ? dollyZoom.Phase
                : IntoxicationDollyZoomPhase.Rest;
        public float DollyZoomFieldOfView => dollyFieldOfView;
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
            EnsureDollyZoom();
            dollyZoom.Reset(IntoxicationDollyZoomModel.InitialRestSeconds);
            dollyWeight = GetDollyZoomTargetWeight();
            releaseExponent = 0f;
            releaseWeight = 0f;
            releaseVelocity = 0f;
            dollyAppliedExponent = 0f;
            float idealDistance = isInterior
                ? interiorDistance
                : exteriorDistance;
            dollyClearance = idealDistance * GetDollyNarrowScale();
            dollySmoothedClearance = dollyClearance;
            dollyClearanceVelocity = 0f;
            dollyDistance = idealDistance;
            dollyFieldOfView = FollowFieldOfView;
            lastFreeDollyFieldOfView = dollyFieldOfView;
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

        /// <summary>
        /// Test seam: restarts the drunk dolly zoom's random stream from
        /// rest. Call it right after <see cref="Initialize"/>; mid-cycle
        /// it cuts the lens back to its base.
        /// </summary>
        public void ReseedDollyZoom(int seed)
        {
            dollyZoom = new IntoxicationDollyZoomModel(seed);
            dollyAppliedExponent = 0f;
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

            if (!fixedPoseActive)
            {
                // Handing the camera to an owner: remember the lens the
                // drunk dolly zoom was on so the release can tell a pose
                // that returns to it from an authored shot, and put the
                // breath back to rest — it is silent for as long as the
                // owner holds the camera.
                lastFreeDollyFieldOfView = dollyFieldOfView;
                EnsureDollyZoom();
                dollyZoom.Reset(
                    IntoxicationDollyZoomModel.InitialRestSeconds);
                releaseExponent = 0f;
                releaseWeight = 0f;
                releaseVelocity = 0f;
                dollyAppliedExponent = 0f;
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

            // An owner that blended back to the very lens it took from the
            // drunk camera (the bar shop returns to the live pose it
            // captured) is absorbed: the dolly layer starts on that lens
            // and eases it to base. An authored shot lens that merely
            // differs from base cuts exactly as it always did.
            releaseVelocity = 0f;
            if (Mathf.Abs(
                    fixedBaseFieldOfView - lastFreeDollyFieldOfView) <=
                DollyZoomReleaseToleranceDegrees &&
                Mathf.Abs(fixedBaseFieldOfView - FollowFieldOfView) > 0.01f)
            {
                releaseExponent = GetDollyExponentForFieldOfView(
                    fixedBaseFieldOfView);
                releaseWeight = 1f;
            }
            else
            {
                releaseExponent = 0f;
                releaseWeight = 0f;
            }

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

        /// <summary>
        /// The target was moved on purpose by less than a teleport's
        /// worth (the root brought back under a lying body): forget the
        /// jump so it is not read as a burst of walking speed, and let
        /// the focus smooth over to it as it would to a step.
        /// </summary>
        public void AbsorbTargetShift()
        {
            if (followTarget != null)
            {
                previousTargetPosition = followTarget.position;
            }
        }

        /// <summary>
        /// Height above a focus-override point the camera looks at: a
        /// lying body's pelvis is on the floor, and a shot at the floor
        /// itself tips the horizon.
        /// </summary>
        public const float FocusOverrideHeight = 0.35f;

        /// <summary>Where the focus is being pulled to, and how much.</summary>
        public Vector3 FocusOverridePoint { get; private set; }
        public float FocusOverrideWeight { get; private set; }

        /// <summary>
        /// Pulls the focus from the root toward <paramref name="worldPoint"/>
        /// (plus <see cref="FocusOverrideHeight"/>) by <paramref name="weight"/>:
        /// the camera follows the body where it lies rather than the
        /// capsule left standing where it fell. The ordinary damping and
        /// lag clamp still apply, so the pull is a pan, not a cut.
        /// </summary>
        public void SetFocusOverride(Vector3 worldPoint, float weight)
        {
            FocusOverridePoint = worldPoint;
            FocusOverrideWeight = Mathf.Clamp01(weight);
        }

        public void ClearFocusOverride()
        {
            FocusOverrideWeight = 0f;
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
            dollySmoothedClearance = dollyClearance;
            dollyClearanceVelocity = 0f;
            ResolveDollyZoom();
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

            AdvanceDollyZoom(deltaTime);
            ResolveDollyZoom();
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
                focusPoint - rotation * Vector3.forward * dollyDistance,
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
            Vector3 rootFocus = followTarget.position + Vector3.up * focusHeight;
            if (FocusOverrideWeight <= 0f)
            {
                return rootFocus;
            }

            return Vector3.Lerp(
                rootFocus,
                FocusOverridePoint + Vector3.up * FocusOverrideHeight,
                FocusOverrideWeight);
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
            // The sweep reaches as far as a full pull-out would, so the
            // one cast that shortens the ordinary arm also measures the
            // room the drunk dolly zoom has behind the camera.
            float reach = idealDistance * GetDollyNarrowScale();
            Vector3 direction = -(rotation * Vector3.forward);
            int hitCount = Physics.SphereCastNonAlloc(
                focusPoint,
                collisionRadius,
                direction,
                collisionHits,
                reach,
                collisionMask,
                QueryTriggerInteraction.Ignore);
            float allowedDistance = idealDistance;
            float clearance = reach;

            for (int index = 0; index < hitCount; index++)
            {
                RaycastHit hit = collisionHits[index];
                if (hit.collider == null || IsPlayerCollider(hit.collider.transform))
                {
                    continue;
                }

                float limit = Mathf.Max(
                    0.01f,
                    hit.distance - collisionPadding);
                allowedDistance = Mathf.Min(allowedDistance, limit);
                clearance = Mathf.Min(clearance, limit);
            }

            dollyClearance = clearance;
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
                : dollyFieldOfView;
        }

        private void EnsureDollyZoom()
        {
            if (dollyZoom == null)
            {
                dollyZoom = new IntoxicationDollyZoomModel(
                    GameSessionState.CitySeed ^ DollyZoomSeedSalt);
            }
        }

        private float GetDollyZoomTargetWeight()
        {
            return CinematicMotionEnabled &&
                   GraphicsEffectsSettings.IntoxicationLensFxEnabled
                ? 1f
                : 0f;
        }

        private static float HalfAngleTangent(float fieldOfViewDegrees)
        {
            return Mathf.Tan(fieldOfViewDegrees * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>How far past the ideal arm a full pull-out reaches.</summary>
        private float GetDollyNarrowScale()
        {
            return HalfAngleTangent(FollowFieldOfView) /
                   HalfAngleTangent(dollyZoomNarrowFieldOfView);
        }

        /// <summary>
        /// The signed reach at which the dolly layer would put the lens
        /// on <paramref name="fieldOfView"/>; the inverse of the mapping
        /// in <see cref="ResolveDollyZoom"/>, clamped to the full swing.
        /// </summary>
        private float GetDollyExponentForFieldOfView(float fieldOfView)
        {
            float baseTangent = HalfAngleTangent(FollowFieldOfView);
            float tangent = HalfAngleTangent(
                Mathf.Clamp(fieldOfView, 1f, 179f));
            if (tangent > baseTangent)
            {
                float wideTangent =
                    HalfAngleTangent(dollyZoomWideFieldOfView);
                return wideTangent <= baseTangent
                    ? 0f
                    : Mathf.Clamp01(
                        Mathf.Log(tangent / baseTangent) /
                        Mathf.Log(wideTangent / baseTangent));
            }

            float narrowTangent =
                HalfAngleTangent(dollyZoomNarrowFieldOfView);
            return narrowTangent >= baseTangent
                ? 0f
                : -Mathf.Clamp01(
                    Mathf.Log(baseTangent / tangent) /
                    Mathf.Log(baseTangent / narrowTangent));
        }

        private void AdvanceDollyZoom(float deltaTime)
        {
            EnsureDollyZoom();
            IntoxicationProfile intoxication =
                IntoxicationStageRules.Evaluate(
                    Mathf.RoundToInt(
                        currentIntoxication *
                        IntoxicationStageRules.MaximumLevel));
            float pace = Mathf.InverseLerp(
                IntoxicationStageRules.BalanceThreshold /
                (float)IntoxicationStageRules.MaximumLevel,
                1f,
                currentIntoxication);
            bool narrowAllowed =
                dollyClearance >=
                currentDistance * DollyZoomNarrowRoomFactor;
            dollyZoom.Advance(
                deltaTime,
                intoxication.DollyZoomStrength,
                pace,
                narrowAllowed);

            float targetWeight = GetDollyZoomTargetWeight();
            dollyWeight = dollyZoomBlendTime <= 0f
                ? targetWeight
                : Mathf.MoveTowards(
                    dollyWeight,
                    targetWeight,
                    deltaTime / dollyZoomBlendTime);

            if (releaseWeight > 0f)
            {
                releaseWeight = dollyZoomReleaseBlendTime <= 0f
                    ? 0f
                    : Mathf.SmoothDamp(
                        releaseWeight,
                        0f,
                        ref releaseVelocity,
                        dollyZoomReleaseBlendTime,
                        Mathf.Infinity,
                        deltaTime);
                if (releaseWeight < 0.002f)
                {
                    releaseWeight = 0f;
                    releaseVelocity = 0f;
                }
            }

            // The room behind the camera closes at once and opens again
            // with the ordinary arm's damping, so a pillar leaving the
            // sweep does not fling a pulled-back camera.
            if (dollyClearance <= dollySmoothedClearance)
            {
                dollySmoothedClearance = dollyClearance;
                dollyClearanceVelocity = 0f;
            }
            else
            {
                dollySmoothedClearance =
                    dollyZoomClearanceRecoveryTime <= 0f
                        ? dollyClearance
                        : Mathf.SmoothDamp(
                            dollySmoothedClearance,
                            dollyClearance,
                            ref dollyClearanceVelocity,
                            dollyZoomClearanceRecoveryTime,
                            Mathf.Infinity,
                            deltaTime);
            }
        }

        /// <summary>
        /// Maps the breath onto the arm and the lens together. The hero's
        /// apparent size is distance × tan(fov/2), so the arm is scaled by
        /// the inverse of the lens change; the reference is the
        /// collision-resolved ordinary arm, so whatever framing a wall
        /// forced is what the zoom preserves. A pull-out stops at the
        /// room behind the camera and the lens follows the arm it got.
        /// Ticks nothing: two calls in one frame agree.
        /// </summary>
        private void ResolveDollyZoom()
        {
            float baseFieldOfView = FollowFieldOfView;
            float exponent = Mathf.Clamp(
                (dollyZoom != null ? dollyZoom.Exponent : 0f) *
                dollyWeight +
                releaseExponent * releaseWeight,
                -1f,
                1f);
            dollyAppliedExponent = exponent;
            if (exponent == 0f)
            {
                // At rest the layer is bit-exactly the ordinary camera:
                // the arm is the arm, not the arm through a tangent and
                // back, so a sober pose matches ResolveFollowPose to the
                // last ulp.
                dollyDistance = currentDistance;
                dollyFieldOfView = baseFieldOfView;
                return;
            }

            float baseTangent = HalfAngleTangent(baseFieldOfView);
            float targetTangent = exponent >= 0f
                ? baseTangent *
                  Mathf.Pow(
                      HalfAngleTangent(dollyZoomWideFieldOfView) /
                      baseTangent,
                      exponent)
                : baseTangent *
                  Mathf.Pow(
                      HalfAngleTangent(dollyZoomNarrowFieldOfView) /
                      baseTangent,
                      -exponent);
            float idealDistance =
                currentDistance * baseTangent / targetTangent;
            float room = Mathf.Min(
                dollyClearance,
                Mathf.Max(dollySmoothedClearance, currentDistance));
            dollyDistance = Mathf.Max(
                MinimumDollyDistance,
                Mathf.Min(idealDistance, room));
            dollyFieldOfView = Mathf.Clamp(
                2f *
                Mathf.Atan(currentDistance * baseTangent / dollyDistance) *
                Mathf.Rad2Deg,
                dollyZoomNarrowFieldOfView,
                dollyZoomWideFieldOfView);
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
