using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityBusDriverPresentation : MonoBehaviour
    {
        public const float MaximumHeadYaw = 68f;
        public const float MaximumHeadPitch = 12f;
        public const float MaximumGripError = 0.02f;
        public const float PlayerFocusFullDistance = 2.25f;
        public const float PlayerFocusZeroDistance = 2.75f;
        public const float MaximumPlayerFocusStretch = 0.10f;
        public const float MaximumNeckStretchRatio = 1.35f;
        public const float BlinkCycleDuration = 3.60f;
        public const float BlinkStartTime = 2.70f;
        public const float BlinkCloseDuration = 0.06f;
        public const float BlinkHoldDuration = 0.07f;
        public const float BlinkOpenDuration = 0.09f;

        private const float DriverSeatLift = 0.015f;
        private const float DriverSeatBackOffset = 0.20f;
        private const float FootForwardOffset = 0.35f;
        private const float FootSideOffset = 0.14f;
        private const float AnkleDropFromSeat = 0.34f;
        private const float KneeForwardOffset = 0.30f;
        private const float KneeSideOffset = 0.18f;
        private const float KneeDropFromSeat = 0.10f;
        private const float ReachArcHeight = 0.055f;
        private const float BreathingDegrees = 0.55f;
        private const float BreathingRadiansPerSecond = 1.25f;
        private const float PlayerFocusFallbackEyeHeight = 1.58f;
        private const float PlayerFocusHeadLift = 0.12f;
        private const float PlayerFocusSideInset = 0.15f;
        private const float PlayerFocusEnterSpeed = 3.5f;
        private const float PlayerFocusExitSpeed = 2f;
        private const float EyesClosedThreshold = 0.78f;
        private const int IkIterations = 5;

        private readonly List<BonePose> baseBonePoses =
            new List<BonePose>(24);

        private CityBusDriverAssetRegistry registry;
        private CityBusAssetRegistry busRegistry;
        private Transform presentationRoot;
        private Transform playerFocusRoot;
        private Transform playerFocusHead;
        private Renderer[] blinkRenderers = Array.Empty<Renderer>();
        private HandAttachment leftHandAttachment;
        private HandAttachment rightHandAttachment;
        private Quaternion leftFootRotationInDriver;
        private Quaternion rightFootRotationInDriver;
        private float breathingPhase;
        private float blinkPhase;

        public bool IsInitialized { get; private set; }
        public float RightHandButtonBlend { get; private set; }
        public float DoorLookWeight { get; private set; }
        public float LeftGripDistance { get; private set; }
        public float RightGripDistance { get; private set; }
        public float RightHandButtonDistance { get; private set; }
        public float DoorLookAlignment { get; private set; }
        public float PlayerFocusWeight { get; private set; }
        public bool IsPlayerNearFrontDoor { get; private set; }
        public Vector3 PlayerFocusPoint { get; private set; }
        public float FocusStretchDistance { get; private set; }
        public float NeckStretchRatio { get; private set; } = 1f;
        public float BlinkClosure { get; private set; }
        public bool EyesClosed { get; private set; }
        public CityBusDriverAssetRegistry Registry => registry;

        public void Initialize(
            CityBusDriverAssetRegistry driverRegistry,
            CityBusAssetRegistry configuredBusRegistry,
            Transform configuredPresentationRoot)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus driver presentation is already initialized.");
            }

            registry = driverRegistry != null
                ? driverRegistry
                : throw new ArgumentNullException(nameof(driverRegistry));
            busRegistry = configuredBusRegistry != null
                ? configuredBusRegistry
                : throw new ArgumentNullException(
                    nameof(configuredBusRegistry));
            presentationRoot = configuredPresentationRoot != null
                ? configuredPresentationRoot
                : throw new ArgumentNullException(
                    nameof(configuredPresentationRoot));
            ValidateBindings();
            CaptureBlinkRenderers();

            if (registry.Animator != null)
            {
                registry.Animator.applyRootMotion = false;
                registry.Animator.runtimeAnimatorController = null;
                registry.Animator.enabled = false;
            }

            Transform driverRoot = registry.transform;
            driverRoot.SetParent(busRegistry.Body, true);
            driverRoot.rotation = presentationRoot.rotation;
            Vector3 seatTarget = busRegistry.DriverSeatAnchor.position +
                presentationRoot.up * DriverSeatLift -
                presentationRoot.forward * DriverSeatBackOffset;
            driverRoot.position += seatTarget - registry.Pelvis.position;

            CaptureBaseBonePoses();
            leftHandAttachment = new HandAttachment(
                registry.LeftHand,
                registry.LeftGripSocket);
            rightHandAttachment = new HandAttachment(
                registry.RightHand,
                registry.RightGripSocket);
            leftFootRotationInDriver = Quaternion.Inverse(
                    driverRoot.rotation) *
                registry.LeftFoot.rotation;
            rightFootRotationInDriver = Quaternion.Inverse(
                    driverRoot.rotation) *
                registry.RightFoot.rotation;

            IsInitialized = true;
            ResetForPool();
        }

        public void SetPlayerFocusTarget(Transform playerRoot)
        {
            playerFocusRoot = playerRoot;
            Player3DAssetRegistry playerRegistry = playerRoot != null
                ? playerRoot.GetComponentInChildren<
                    Player3DAssetRegistry>(true)
                : null;
            playerFocusHead = playerRegistry != null
                ? playerRegistry.Anchors.Head
                : null;
            IsPlayerNearFrontDoor = false;
            PlayerFocusWeight = 0f;
            FocusStretchDistance = 0f;
            NeckStretchRatio = 1f;
        }

        public void ApplyPose(
            float rightHandButtonBlend,
            float doorLookWeight,
            float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            RightHandButtonBlend = Sanitize01(rightHandButtonBlend);
            DoorLookWeight = Sanitize01(doorLookWeight);
            float safeDeltaTime = IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
            breathingPhase = Mathf.Repeat(
                breathingPhase +
                safeDeltaTime * BreathingRadiansPerSecond,
                Mathf.PI * 2f);
            AdvanceBlink(safeDeltaTime);
            UpdatePlayerFocus(safeDeltaTime);

            RestoreBaseBonePoses();
            ApplySeatedPose();
            ApplyBreathing();
            ApplyLookAt(
                busRegistry.DriverDoorLookAnchor.position,
                DoorLookWeight);
            if (PlayerFocusWeight > 0f)
            {
                Vector3 headBeforeFocus = registry.Head.position;
                float neckLengthBeforeFocus = Vector3.Distance(
                    registry.Neck.position,
                    registry.Head.position);
                ApplyPlayerFocusStretch(
                    PlayerFocusPoint,
                    PlayerFocusWeight * PlayerFocusWeight);
                ApplyLookAt(PlayerFocusPoint, PlayerFocusWeight);
                FocusStretchDistance = Vector3.Distance(
                    headBeforeFocus,
                    registry.Head.position);
                NeckStretchRatio = neckLengthBeforeFocus > 0.0001f
                    ? Vector3.Distance(
                        registry.Neck.position,
                        registry.Head.position) / neckLengthBeforeFocus
                    : 1f;
            }
            else
            {
                FocusStretchDistance = 0f;
                NeckStretchRatio = 1f;
            }
            ApplySteeringHands(RightHandButtonBlend);
            RefreshContactDiagnostics();
        }

        public void ResetForPool()
        {
            if (!IsInitialized)
            {
                return;
            }

            breathingPhase = 0f;
            blinkPhase = 0f;
            RightHandButtonBlend = 0f;
            DoorLookWeight = 0f;
            PlayerFocusWeight = 0f;
            IsPlayerNearFrontDoor = false;
            FocusStretchDistance = 0f;
            NeckStretchRatio = 1f;
            RestoreBaseBonePoses();
            ApplySeatedPose();
            ApplyLookAt(
                busRegistry.DriverDoorLookAnchor.position,
                0f);
            ApplySteeringHands(0f);
            SetBlinkClosure(0f);
            RefreshContactDiagnostics();
        }

        private void CaptureBlinkRenderers()
        {
            var targets = new List<Renderer>(4);
            IReadOnlyList<CityBusDriverRendererBinding> bindings =
                registry.RendererBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                CityBusDriverRendererBinding binding = bindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                if (string.Equals(
                        binding.Role,
                        "long_horizontal_eye",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        binding.Role,
                        "visible_eye_pupil",
                        StringComparison.Ordinal))
                {
                    targets.Add(binding.Renderer);
                }
            }

            if (targets.Count != 4)
            {
                throw new InvalidOperationException(
                    "The city bus driver blink requires exactly two eye " +
                    "whites and two pupils.");
            }

            blinkRenderers = targets.ToArray();
        }

        private void ValidateBindings()
        {
            Transform[] requiredDriverBindings =
            {
                registry.Pelvis,
                registry.Spine,
                registry.Chest,
                registry.Neck,
                registry.Head,
                registry.FaceEyeLeft,
                registry.FaceEyeRight,
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                registry.LeftGripSocket,
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                registry.RightGripSocket,
                registry.LeftThigh,
                registry.LeftShin,
                registry.LeftFoot,
                registry.RightThigh,
                registry.RightShin,
                registry.RightFoot
            };
            for (int index = 0;
                 index < requiredDriverBindings.Length;
                 index++)
            {
                if (requiredDriverBindings[index] == null)
                {
                    throw new InvalidOperationException(
                        "The city bus driver rig is missing a required " +
                        "bone or grip binding.");
                }
            }

            Transform[] requiredBusBindings =
            {
                busRegistry.Body,
                busRegistry.DriverSeatAnchor,
                busRegistry.LeftSteeringGrip,
                busRegistry.RightSteeringGrip,
                busRegistry.DoorButtonPressAnchor,
                busRegistry.DriverDoorLookAnchor
            };
            for (int index = 0;
                 index < requiredBusBindings.Length;
                 index++)
            {
                if (requiredBusBindings[index] == null)
                {
                    throw new InvalidOperationException(
                        "The city bus is missing a driver seat, steering " +
                        "grip, door button or door-look binding.");
                }
            }
        }

        private void CaptureBaseBonePoses()
        {
            baseBonePoses.Clear();
            AddBasePose(registry.Pelvis);
            AddBasePose(registry.Spine);
            AddBasePose(registry.Chest);
            AddBasePose(registry.Neck);
            AddBasePose(registry.Head);
            AddBasePose(registry.FaceEyeLeft);
            AddBasePose(registry.FaceEyeRight);
            AddBasePose(registry.LeftUpperArm);
            AddBasePose(registry.LeftForearm);
            AddBasePose(registry.LeftHand);
            AddBasePose(registry.RightUpperArm);
            AddBasePose(registry.RightForearm);
            AddBasePose(registry.RightHand);
            AddBasePose(registry.LeftThigh);
            AddBasePose(registry.LeftShin);
            AddBasePose(registry.LeftFoot);
            AddBasePose(registry.RightThigh);
            AddBasePose(registry.RightShin);
            AddBasePose(registry.RightFoot);
        }

        private void AddBasePose(Transform target)
        {
            baseBonePoses.Add(new BonePose(target));
        }

        private void RestoreBaseBonePoses()
        {
            for (int index = 0; index < baseBonePoses.Count; index++)
            {
                baseBonePoses[index].Restore();
            }
        }

        private void ApplySeatedPose()
        {
            Transform driverRoot = registry.transform;
            Vector3 up = driverRoot.up;
            Vector3 forward = driverRoot.forward;
            Vector3 right = driverRoot.right;
            Vector3 seat = registry.Pelvis.position;

            ApplyLegPose(
                registry.LeftThigh,
                registry.LeftShin,
                registry.LeftFoot,
                seat +
                right * FootSideOffset +
                forward * FootForwardOffset -
                up * AnkleDropFromSeat,
                seat +
                right * KneeSideOffset +
                forward * KneeForwardOffset -
                up * KneeDropFromSeat,
                driverRoot.rotation * leftFootRotationInDriver);
            ApplyLegPose(
                registry.RightThigh,
                registry.RightShin,
                registry.RightFoot,
                seat -
                right * FootSideOffset +
                forward * (FootForwardOffset + 0.025f) -
                up * AnkleDropFromSeat,
                seat -
                right * KneeSideOffset +
                forward * KneeForwardOffset -
                up * (KneeDropFromSeat + 0.015f),
                driverRoot.rotation * rightFootRotationInDriver);

            registry.Spine.rotation = Quaternion.AngleAxis(
                    5f,
                    right) *
                registry.Spine.rotation;
            registry.Chest.rotation = Quaternion.AngleAxis(
                    3f,
                    right) *
                registry.Chest.rotation;
        }

        private static void ApplyLegPose(
            Transform thigh,
            Transform shin,
            Transform foot,
            Vector3 ankleTarget,
            Vector3 kneeHint,
            Quaternion footRotation)
        {
            SolveTwoBone(
                thigh,
                shin,
                foot,
                ankleTarget,
                footRotation,
                kneeHint);
        }

        private void ApplyBreathing()
        {
            float amount = Mathf.Sin(breathingPhase) * BreathingDegrees;
            registry.Chest.rotation = Quaternion.AngleAxis(
                    amount,
                    registry.transform.right) *
                registry.Chest.rotation;
        }

        private void ApplyLookAt(Vector3 targetPosition, float weight)
        {
            Transform driverRoot = registry.transform;
            Vector3 origin = registry.Head.position;
            Vector3 toTarget = targetPosition - origin;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                DoorLookAlignment = 0f;
                return;
            }

            Vector3 up = driverRoot.up;
            Vector3 forward = ResolvePlanarFaceForward(up);
            Vector3 planarTarget = Vector3.ProjectOnPlane(toTarget, up);
            float yaw = planarTarget.sqrMagnitude > 0.0001f
                ? Mathf.Clamp(
                    Vector3.SignedAngle(forward, planarTarget, up),
                    -MaximumHeadYaw,
                    MaximumHeadYaw) * weight
                : 0f;
            Vector3 normalizedTarget = toTarget.normalized;
            float pitch = Mathf.Clamp(
                    Vector3.SignedAngle(
                        planarTarget.normalized,
                        normalizedTarget,
                        driverRoot.right),
                    -MaximumHeadPitch,
                    MaximumHeadPitch) * weight;

            Quaternion neckTurn =
                Quaternion.AngleAxis(yaw * 0.42f, up) *
                Quaternion.AngleAxis(-pitch * 0.35f, driverRoot.right);
            Quaternion headTurn =
                Quaternion.AngleAxis(yaw * 0.58f, up) *
                Quaternion.AngleAxis(-pitch * 0.65f, driverRoot.right);
            registry.Neck.rotation = neckTurn * registry.Neck.rotation;
            registry.Head.rotation = headTurn * registry.Head.rotation;

            float eyeYaw = Mathf.Clamp(yaw * 0.10f, -7f, 7f);
            Quaternion eyeTurn = Quaternion.AngleAxis(eyeYaw, up);
            registry.FaceEyeLeft.rotation =
                eyeTurn * registry.FaceEyeLeft.rotation;
            registry.FaceEyeRight.rotation =
                eyeTurn * registry.FaceEyeRight.rotation;

            Vector3 resolvedLook = ResolvePlanarFaceForward(up);
            Vector3 resolvedTarget = Vector3.ProjectOnPlane(
                targetPosition -
                registry.Head.position,
                up);
            DoorLookAlignment = Vector3.Dot(
                resolvedLook.normalized,
                resolvedTarget.normalized);
        }

        private Vector3 ResolvePlanarFaceForward(Vector3 up)
        {
            Vector3 eyeCenter =
                (registry.FaceEyeLeft.position +
                 registry.FaceEyeRight.position) * 0.5f;
            Vector3 faceForward = Vector3.ProjectOnPlane(
                eyeCenter - registry.Head.position,
                up);
            if (faceForward.sqrMagnitude > 0.0001f)
            {
                return faceForward.normalized;
            }

            Vector3 fallback = Vector3.ProjectOnPlane(
                registry.transform.forward,
                up);
            return fallback.sqrMagnitude > 0.0001f
                ? fallback.normalized
                : Vector3.forward;
        }

        private void UpdatePlayerFocus(float deltaTime)
        {
            float targetWeight = 0f;
            if (playerFocusRoot != null &&
                busRegistry.FrontDoorEntryAnchor != null)
            {
                PlayerFocusPoint = ResolvePlayerFocusPoint();
                Vector3 up = presentationRoot.up;
                Vector3 playerFromDoor = Vector3.ProjectOnPlane(
                    playerFocusRoot.position -
                    busRegistry.FrontDoorEntryAnchor.position,
                    up);
                float distance = playerFromDoor.magnitude;
                Vector3 outward = Vector3.ProjectOnPlane(
                    busRegistry.FrontDoorEntryAnchor.position -
                    busRegistry.DriverSeatAnchor.position,
                    up);
                if (outward.sqrMagnitude < 0.0001f)
                {
                    outward = presentationRoot.right;
                }

                float outsideDepth = Vector3.Dot(
                    playerFromDoor,
                    outward.normalized);
                if (outsideDepth >= -PlayerFocusSideInset)
                {
                    targetWeight = 1f - SmoothStep01(
                        Mathf.InverseLerp(
                            PlayerFocusFullDistance,
                            PlayerFocusZeroDistance,
                            distance));
                }
            }
            else
            {
                PlayerFocusPoint =
                    busRegistry.DriverDoorLookAnchor.position;
            }

            IsPlayerNearFrontDoor = targetWeight > 0.0001f;
            float response = targetWeight > PlayerFocusWeight
                ? PlayerFocusEnterSpeed
                : PlayerFocusExitSpeed;
            PlayerFocusWeight = Mathf.MoveTowards(
                PlayerFocusWeight,
                targetWeight,
                response * deltaTime);
        }

        private Vector3 ResolvePlayerFocusPoint()
        {
            if (playerFocusHead != null)
            {
                return playerFocusHead.position +
                    playerFocusRoot.up * PlayerFocusHeadLift;
            }

            return playerFocusRoot.position +
                playerFocusRoot.up * PlayerFocusFallbackEyeHeight;
        }

        private void ApplyPlayerFocusStretch(
            Vector3 targetPosition,
            float weight)
        {
            Vector3 up = presentationRoot.up;
            Vector3 focusDirection = Vector3.ProjectOnPlane(
                targetPosition - registry.Head.position,
                up);
            Vector3 neckSegment =
                registry.Head.position - registry.Neck.position;
            float neckLength = neckSegment.magnitude;
            if (focusDirection.sqrMagnitude < 0.0001f ||
                neckLength < 0.0001f)
            {
                return;
            }

            Vector3 desiredSegment = neckSegment +
                focusDirection.normalized *
                (MaximumPlayerFocusStretch * Mathf.Clamp01(weight));
            float desiredLength = Mathf.Clamp(
                desiredSegment.magnitude,
                neckLength,
                neckLength * MaximumNeckStretchRatio);
            desiredSegment = desiredSegment.normalized * desiredLength;

            registry.Neck.rotation = Quaternion.FromToRotation(
                    neckSegment,
                    desiredSegment) *
                registry.Neck.rotation;
            Vector3 headInNeck = registry.Head.localPosition;
            Vector3 neckScale = registry.Neck.localScale;
            float scaleRatio = desiredLength / neckLength;
            if (Mathf.Abs(headInNeck.x) >= Mathf.Abs(headInNeck.y) &&
                Mathf.Abs(headInNeck.x) >= Mathf.Abs(headInNeck.z))
            {
                neckScale.x *= scaleRatio;
            }
            else if (Mathf.Abs(headInNeck.y) >= Mathf.Abs(headInNeck.z))
            {
                neckScale.y *= scaleRatio;
            }
            else
            {
                neckScale.z *= scaleRatio;
            }

            registry.Neck.localScale = neckScale;
            registry.Head.position =
                registry.Neck.position + desiredSegment;
        }

        private void AdvanceBlink(float deltaTime)
        {
            blinkPhase = Mathf.Repeat(
                blinkPhase + deltaTime,
                BlinkCycleDuration);
            float closeEnd = BlinkStartTime + BlinkCloseDuration;
            float holdEnd = closeEnd + BlinkHoldDuration;
            float openEnd = holdEnd + BlinkOpenDuration;
            float closure;
            if (blinkPhase < BlinkStartTime || blinkPhase >= openEnd)
            {
                closure = 0f;
            }
            else if (blinkPhase < closeEnd)
            {
                closure = SmoothStep01(
                    (blinkPhase - BlinkStartTime) /
                    BlinkCloseDuration);
            }
            else if (blinkPhase < holdEnd)
            {
                closure = 1f;
            }
            else
            {
                closure = 1f - SmoothStep01(
                    (blinkPhase - holdEnd) /
                    BlinkOpenDuration);
            }

            SetBlinkClosure(closure);
        }

        private void SetBlinkClosure(float closure)
        {
            BlinkClosure = Mathf.Clamp01(closure);
            EyesClosed = BlinkClosure >= EyesClosedThreshold;
            for (int index = 0; index < blinkRenderers.Length; index++)
            {
                Renderer target = blinkRenderers[index];
                if (target != null)
                {
                    target.forceRenderingOff = EyesClosed;
                }
            }
        }

        private static float SmoothStep01(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }

        private void ApplySteeringHands(float buttonBlend)
        {
            TargetPose leftGrip = new TargetPose(
                busRegistry.LeftSteeringGrip.position,
                busRegistry.LeftSteeringGrip.rotation);
            TargetPose rightGrip = new TargetPose(
                busRegistry.RightSteeringGrip.position,
                busRegistry.RightSteeringGrip.rotation);
            TargetPose button = new TargetPose(
                busRegistry.DoorButtonPressAnchor.position,
                busRegistry.DoorButtonPressAnchor.rotation);
            float arc = Mathf.Sin(buttonBlend * Mathf.PI) * ReachArcHeight;
            TargetPose rightTarget = new TargetPose(
                Vector3.Lerp(rightGrip.Position, button.Position, buttonBlend) +
                registry.transform.up * arc,
                Quaternion.Slerp(
                    rightGrip.Rotation,
                    button.Rotation,
                    buttonBlend));

            ApplyArmPose(
                registry.LeftUpperArm,
                registry.LeftForearm,
                registry.LeftHand,
                leftHandAttachment,
                leftGrip,
                registry.transform.right);
            ApplyArmPose(
                registry.RightUpperArm,
                registry.RightForearm,
                registry.RightHand,
                rightHandAttachment,
                rightTarget,
                -registry.transform.right);
        }

        private void ApplyArmPose(
            Transform upperArm,
            Transform forearm,
            Transform hand,
            HandAttachment attachment,
            TargetPose socketTarget,
            Vector3 elbowSide)
        {
            Quaternion handRotation =
                socketTarget.Rotation *
                Quaternion.Inverse(attachment.SocketRotationInHand);
            Vector3 handPosition =
                socketTarget.Position -
                handRotation * attachment.SocketPositionInHand;
            Vector3 elbowHint =
                upperArm.position +
                elbowSide * 0.32f +
                registry.transform.forward * 0.08f -
                registry.transform.up * 0.08f;
            SolveTwoBone(
                upperArm,
                forearm,
                hand,
                handPosition,
                handRotation,
                elbowHint);
        }

        private void RefreshContactDiagnostics()
        {
            LeftGripDistance = Vector3.Distance(
                registry.LeftGripSocket.position,
                busRegistry.LeftSteeringGrip.position);
            RightGripDistance = Vector3.Distance(
                registry.RightGripSocket.position,
                busRegistry.RightSteeringGrip.position);
            RightHandButtonDistance = Vector3.Distance(
                registry.RightGripSocket.position,
                busRegistry.DoorButtonPressAnchor.position);
        }

        private static void SolveTwoBone(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Vector3 hintPosition)
        {
            if (upper == null || lower == null || tip == null)
            {
                return;
            }

            for (int iteration = 0;
                 iteration < IkIterations;
                 iteration++)
            {
                RotateJointToward(lower, tip.position, targetPosition);
                RotateJointToward(upper, tip.position, targetPosition);
            }

            ApplyBendHint(upper, lower, targetPosition, hintPosition);
            for (int iteration = 0; iteration < 2; iteration++)
            {
                RotateJointToward(lower, tip.position, targetPosition);
                RotateJointToward(upper, tip.position, targetPosition);
            }

            tip.rotation = targetRotation;
        }

        private static void RotateJointToward(
            Transform joint,
            Vector3 tipPosition,
            Vector3 targetPosition)
        {
            Vector3 current = tipPosition - joint.position;
            Vector3 target = targetPosition - joint.position;
            if (current.sqrMagnitude < 0.000001f ||
                target.sqrMagnitude < 0.000001f)
            {
                return;
            }

            joint.rotation = Quaternion.FromToRotation(current, target) *
                joint.rotation;
        }

        private static void ApplyBendHint(
            Transform upper,
            Transform lower,
            Vector3 targetPosition,
            Vector3 hintPosition)
        {
            Vector3 axis = targetPosition - upper.position;
            if (axis.sqrMagnitude < 0.000001f)
            {
                return;
            }

            axis.Normalize();
            Vector3 current = Vector3.ProjectOnPlane(
                lower.position - upper.position,
                axis);
            Vector3 desired = Vector3.ProjectOnPlane(
                hintPosition - upper.position,
                axis);
            if (current.sqrMagnitude < 0.000001f ||
                desired.sqrMagnitude < 0.000001f)
            {
                return;
            }

            float angle = Vector3.SignedAngle(current, desired, axis);
            upper.rotation = Quaternion.AngleAxis(angle, axis) *
                upper.rotation;
        }

        private static float Sanitize01(float value)
        {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct TargetPose
        {
            public TargetPose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        private readonly struct HandAttachment
        {
            public HandAttachment(Transform hand, Transform socket)
            {
                SocketPositionInHand = Quaternion.Inverse(hand.rotation) *
                    (socket.position - hand.position);
                SocketRotationInHand = Quaternion.Inverse(hand.rotation) *
                    socket.rotation;
            }

            public Vector3 SocketPositionInHand { get; }
            public Quaternion SocketRotationInHand { get; }
        }

        private readonly struct BonePose
        {
            private readonly Transform target;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;

            public BonePose(Transform configuredTarget)
            {
                target = configuredTarget;
                localPosition = configuredTarget.localPosition;
                localRotation = configuredTarget.localRotation;
                localScale = configuredTarget.localScale;
            }

            public void Restore()
            {
                target.localPosition = localPosition;
                target.localRotation = localRotation;
                target.localScale = localScale;
            }
        }
    }
}
