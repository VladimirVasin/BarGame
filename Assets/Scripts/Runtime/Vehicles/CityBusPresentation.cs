using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityBusPresentation : MonoBehaviour
    {
        public const float MaximumDoorAngle = 72f;
        public const float MaximumSteeringAngle = 28f;
        public const float MaximumSuspensionHeave = 0.045f;
        public const float MaximumSuspensionPitch = 0.8f;
        public const float MaximumSuspensionRoll = 1f;

        private const float SuspensionWaveLength = 2.8f;
        private const float SuspensionResponse = 7f;
        private const float AccelerationPitchScale = 0.12f;
        private const float SteeringRollScale = 0.78f;
        private const string SuspensionVisualName = "Suspension Visual";

        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");
        private static readonly Color HeadlightEmission =
            new Color(4.2f, 3.55f, 2.35f);
        private static readonly Color TailLightEmission =
            new Color(3.5f, 0.10f, 0.035f);
        private static readonly Color CabinLightEmission =
            new Color(1.85f, 1.20f, 0.62f);

        private MaterialPropertyBlock lightProperties;
        private CityBusAssetRegistry registry;
        private Transform suspensionVisual;
        private TransformPose suspensionVisualBase;
        private Vector3 suspensionPositionInPresentation;
        private Quaternion suspensionRotationInPresentation;
        private TransformPose frontDoorForwardLeafBase;
        private TransformPose frontDoorRearwardLeafBase;
        private TransformPose rearDoorForwardLeafBase;
        private TransformPose rearDoorRearwardLeafBase;
        private TransformPose frontLeftWheelBase;
        private TransformPose frontRightWheelBase;
        private TransformPose rearLeftWheelBase;
        private TransformPose rearRightWheelBase;
        private TransformPose frontLeftSteeringBase;
        private TransformPose frontRightSteeringBase;
        private float wheelRotationDegrees;
        private float brakeFactor;
        private float suspensionPhase;
        private float suspensionHeave;
        private float suspensionPitch;
        private float suspensionRoll;
        private Vector3 doorHingeAxisLocal = Vector3.up;

        public bool IsInitialized { get; private set; }
        public CityBusAssetRegistry Registry => registry;
        public float DoorOpenness { get; private set; }
        public float SteeringAngle { get; private set; }
        public float NightFactor { get; private set; }
        public float BrakeFactor => brakeFactor;
        public Transform SuspensionVisual => suspensionVisual;
        public float SuspensionHeave => suspensionHeave;
        public float SuspensionPitch => suspensionPitch;
        public float SuspensionRoll => suspensionRoll;

        public void Initialize(CityBusAssetRegistry assetRegistry)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus presentation is already initialized.");
            }

            registry = assetRegistry != null
                ? assetRegistry
                : throw new ArgumentNullException(nameof(assetRegistry));
            lightProperties = new MaterialPropertyBlock();
            CreateSuspensionHierarchy();
            CaptureDoorHingeAxis();
            CaptureBasePoses();
            IsInitialized = true;
            ResetForPool();
        }

        public void SetMotion(
            float signedDistance,
            float speedMetersPerSecond,
            float longitudinalAcceleration,
            float steeringAngleDegrees,
            bool braking,
            float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float radius = registry.Dimensions.WheelRadius;
            if (IsFinite(signedDistance) &&
                IsFinite(radius) &&
                radius > 0.0001f)
            {
                wheelRotationDegrees = Mathf.Repeat(
                    wheelRotationDegrees -
                    (signedDistance / radius) * Mathf.Rad2Deg,
                    360f);
            }

            SteeringAngle = IsFinite(steeringAngleDegrees)
                ? Mathf.Clamp(
                    steeringAngleDegrees,
                    -MaximumSteeringAngle,
                    MaximumSteeringAngle)
                : 0f;
            ApplyWheelPose(frontLeftWheelBase, wheelRotationDegrees);
            ApplyWheelPose(frontRightWheelBase, wheelRotationDegrees);
            ApplyWheelPose(rearLeftWheelBase, wheelRotationDegrees);
            ApplyWheelPose(rearRightWheelBase, wheelRotationDegrees);
            ApplySteeringPose(frontLeftSteeringBase, SteeringAngle);
            ApplySteeringPose(frontRightSteeringBase, SteeringAngle);
            AdvanceSuspension(
                signedDistance,
                speedMetersPerSecond,
                longitudinalAcceleration,
                SteeringAngle,
                deltaTime);
            SetBrakeFactor(braking ? 1f : 0f);
        }

        public void SetDoors(float openness01)
        {
            if (!IsInitialized)
            {
                return;
            }

            DoorOpenness = IsFinite(openness01)
                ? Mathf.Clamp01(openness01)
                : 0f;
            ApplyDoorLeafPose(
                frontDoorForwardLeafBase,
                DoorOpenness);
            ApplyDoorLeafPose(
                frontDoorRearwardLeafBase,
                -DoorOpenness);
            ApplyDoorLeafPose(
                rearDoorForwardLeafBase,
                DoorOpenness);
            ApplyDoorLeafPose(
                rearDoorRearwardLeafBase,
                -DoorOpenness);
        }

        public void SetNightFactor(float factor)
        {
            if (!IsInitialized)
            {
                return;
            }

            NightFactor = IsFinite(factor)
                ? Mathf.Clamp01(factor)
                : 0f;
            RefreshLights();
        }

        public void ResetForPool()
        {
            if (!IsInitialized)
            {
                return;
            }

            wheelRotationDegrees = 0f;
            SteeringAngle = 0f;
            DoorOpenness = 0f;
            NightFactor = 0f;
            brakeFactor = 0f;
            suspensionPhase = 0f;
            suspensionHeave = 0f;
            suspensionPitch = 0f;
            suspensionRoll = 0f;
            RestorePose(suspensionVisualBase);
            RestorePose(frontDoorForwardLeafBase);
            RestorePose(frontDoorRearwardLeafBase);
            RestorePose(rearDoorForwardLeafBase);
            RestorePose(rearDoorRearwardLeafBase);
            RestorePose(frontLeftWheelBase);
            RestorePose(frontRightWheelBase);
            RestorePose(rearLeftWheelBase);
            RestorePose(rearRightWheelBase);
            RestorePose(frontLeftSteeringBase);
            RestorePose(frontRightSteeringBase);
            RefreshLights();
        }

        private void OnDisable()
        {
            if (IsInitialized)
            {
                ResetForPool();
            }
        }

        private void CaptureBasePoses()
        {
            suspensionVisualBase = new TransformPose(suspensionVisual);
            if (suspensionVisual != null)
            {
                suspensionPositionInPresentation =
                    transform.InverseTransformPoint(
                        suspensionVisual.position);
                suspensionRotationInPresentation =
                    Quaternion.Inverse(transform.rotation) *
                    suspensionVisual.rotation;
            }
            frontDoorForwardLeafBase = new TransformPose(
                registry.FrontDoorForwardLeaf);
            frontDoorRearwardLeafBase = new TransformPose(
                registry.FrontDoorRearwardLeaf);
            rearDoorForwardLeafBase = new TransformPose(
                registry.RearDoorForwardLeaf);
            rearDoorRearwardLeafBase = new TransformPose(
                registry.RearDoorRearwardLeaf);
            frontLeftWheelBase = new TransformPose(
                registry.FrontLeftWheel);
            frontRightWheelBase = new TransformPose(
                registry.FrontRightWheel);
            rearLeftWheelBase = new TransformPose(
                registry.RearLeftWheel);
            rearRightWheelBase = new TransformPose(
                registry.RearRightWheel);
            frontLeftSteeringBase = new TransformPose(
                registry.FrontLeftSteeringPivot);
            frontRightSteeringBase = new TransformPose(
                registry.FrontRightSteeringPivot);
        }

        private void CreateSuspensionHierarchy()
        {
            Transform body = registry.Body;
            if (body == null || body.parent == null)
            {
                return;
            }

            Transform bodyParent = body.parent;
            GameObject suspensionObject = new GameObject(
                SuspensionVisualName);
            suspensionObject.layer = gameObject.layer;
            suspensionVisual = suspensionObject.transform;
            suspensionVisual.SetParent(bodyParent, false);
            suspensionVisual.localPosition = body.localPosition;
            suspensionVisual.localRotation = body.localRotation;
            suspensionVisual.localScale = body.localScale;

            var detachedRoots = new HashSet<Transform>();
            DetachWheelAssembly(
                registry.FrontLeftSteeringPivot,
                body,
                bodyParent,
                detachedRoots);
            DetachWheelAssembly(
                registry.FrontRightSteeringPivot,
                body,
                bodyParent,
                detachedRoots);
            DetachWheelAssembly(
                registry.RearLeftWheel,
                body,
                bodyParent,
                detachedRoots);
            DetachWheelAssembly(
                registry.RearRightWheel,
                body,
                bodyParent,
                detachedRoots);
            body.SetParent(suspensionVisual, true);
        }

        private void CaptureDoorHingeAxis()
        {
            if (suspensionVisual == null)
            {
                doorHingeAxisLocal = Vector3.up;
                return;
            }

            doorHingeAxisLocal = suspensionVisual
                .InverseTransformDirection(transform.up)
                .normalized;
        }

        private static void DetachWheelAssembly(
            Transform target,
            Transform body,
            Transform destination,
            ISet<Transform> detachedRoots)
        {
            if (target == null)
            {
                return;
            }

            Transform assemblyRoot = target;
            while (assemblyRoot.parent != null &&
                   assemblyRoot.parent != body)
            {
                assemblyRoot = assemblyRoot.parent;
            }

            if (assemblyRoot.parent == body &&
                detachedRoots.Add(assemblyRoot))
            {
                assemblyRoot.SetParent(destination, true);
            }
        }

        private void AdvanceSuspension(
            float signedDistance,
            float speedMetersPerSecond,
            float longitudinalAcceleration,
            float steeringAngleDegrees,
            float deltaTime)
        {
            if (suspensionVisual == null)
            {
                return;
            }

            float safeDistance = IsFinite(signedDistance)
                ? Mathf.Abs(signedDistance)
                : 0f;
            float safeSpeed = IsFinite(speedMetersPerSecond)
                ? Mathf.Max(0f, speedMetersPerSecond)
                : 0f;
            float safeAcceleration = IsFinite(longitudinalAcceleration)
                ? longitudinalAcceleration
                : 0f;
            float safeDeltaTime = IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
            suspensionPhase = Mathf.Repeat(
                suspensionPhase +
                ((safeDistance / SuspensionWaveLength) * Mathf.PI * 2f),
                Mathf.PI * 2f);

            float motionFactor = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(safeSpeed / CityBusActor.CruiseSpeed));
            float primaryWave = Mathf.Sin(suspensionPhase);
            float secondaryWave = Mathf.Sin(
                (suspensionPhase * 2f) + 0.85f);
            float targetHeave = MaximumSuspensionHeave * motionFactor *
                ((primaryWave * 0.72f) + (secondaryWave * 0.28f));
            float roadPitch = MaximumSuspensionPitch * 0.24f *
                motionFactor * Mathf.Sin(suspensionPhase + 1.35f);
            float accelerationPitch = Mathf.Clamp(
                -safeAcceleration * AccelerationPitchScale,
                -MaximumSuspensionPitch * 0.78f,
                MaximumSuspensionPitch * 0.78f);
            float targetPitch = Mathf.Clamp(
                roadPitch + accelerationPitch,
                -MaximumSuspensionPitch,
                MaximumSuspensionPitch);
            float steeringRoll = -Mathf.Clamp(
                steeringAngleDegrees / MaximumSteeringAngle,
                -1f,
                1f) * SteeringRollScale;
            float roadRoll = MaximumSuspensionRoll * 0.20f *
                motionFactor * Mathf.Sin(
                    (suspensionPhase * 2f) + 2.15f);
            float targetRoll = Mathf.Clamp(
                steeringRoll + roadRoll,
                -MaximumSuspensionRoll,
                MaximumSuspensionRoll);
            float response = safeDeltaTime > 0f
                ? 1f - Mathf.Exp(-SuspensionResponse * safeDeltaTime)
                : 0f;
            suspensionHeave = Mathf.Lerp(
                suspensionHeave,
                targetHeave,
                response);
            suspensionPitch = Mathf.Lerp(
                suspensionPitch,
                targetPitch,
                response);
            suspensionRoll = Mathf.Lerp(
                suspensionRoll,
                targetRoll,
                response);
            ApplySuspensionPose();
        }

        private void ApplySuspensionPose()
        {
            Vector3 neutralWorldPosition = transform.TransformPoint(
                suspensionPositionInPresentation);
            Quaternion worldRotation =
                transform.rotation *
                Quaternion.Euler(
                    suspensionPitch,
                    0f,
                    suspensionRoll) *
                suspensionRotationInPresentation;
            suspensionVisual.SetPositionAndRotation(
                neutralWorldPosition +
                (transform.up * suspensionHeave),
                worldRotation);
            suspensionVisual.localScale = suspensionVisualBase.LocalScale;
        }

        private void SetBrakeFactor(float factor)
        {
            float next = Mathf.Clamp01(factor);
            if (Mathf.Approximately(next, brakeFactor))
            {
                return;
            }

            brakeFactor = next;
            RefreshLights();
        }

        private void RefreshLights()
        {
            SetEmission(
                registry.Headlights,
                HeadlightEmission * NightFactor);
            SetEmission(
                registry.TailLights,
                TailLightEmission * Mathf.Max(
                    NightFactor * 0.55f,
                    brakeFactor));
            SetEmission(
                registry.CabinLights,
                CabinLightEmission * NightFactor);
        }

        private void SetEmission(
            IReadOnlyList<Renderer> renderers,
            Color color)
        {
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer target = renderers[index];
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(lightProperties);
                lightProperties.SetColor(EmissionColorId, color);
                target.SetPropertyBlock(lightProperties);
                lightProperties.Clear();
            }
        }

        private void ApplyDoorLeafPose(
            TransformPose pose,
            float signedOpenness)
        {
            if (pose.Target == null)
            {
                return;
            }

            pose.Target.localPosition = pose.LocalPosition;
            pose.Target.localRotation = pose.LocalRotation;
            Vector3 hingeAxis = ResolveDoorHingeAxis();
            pose.Target.rotation =
                Quaternion.AngleAxis(
                    MaximumDoorAngle * signedOpenness,
                    hingeAxis) *
                pose.Target.rotation;
        }

        private Vector3 ResolveDoorHingeAxis()
        {
            if (suspensionVisual == null)
            {
                return transform.up;
            }

            Vector3 hingeAxis = suspensionVisual.TransformDirection(
                doorHingeAxisLocal);
            return hingeAxis.sqrMagnitude > 0.0001f
                ? hingeAxis.normalized
                : transform.up;
        }

        private static void ApplyWheelPose(
            TransformPose pose,
            float rotationDegrees)
        {
            if (pose.Target == null)
            {
                return;
            }

            pose.Target.localPosition = pose.LocalPosition;
            pose.Target.localRotation = pose.LocalRotation *
                Quaternion.AngleAxis(rotationDegrees, Vector3.right);
        }

        private static void ApplySteeringPose(
            TransformPose pose,
            float steeringAngle)
        {
            if (pose.Target == null)
            {
                return;
            }

            pose.Target.localPosition = pose.LocalPosition;
            pose.Target.localRotation = pose.LocalRotation *
                Quaternion.AngleAxis(steeringAngle, Vector3.up);
        }

        private static void RestorePose(TransformPose pose)
        {
            if (pose.Target == null)
            {
                return;
            }

            pose.Target.localPosition = pose.LocalPosition;
            pose.Target.localRotation = pose.LocalRotation;
            pose.Target.localScale = pose.LocalScale;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct TransformPose
        {
            public TransformPose(Transform target)
            {
                Target = target;
                LocalPosition = target != null
                    ? target.localPosition
                    : Vector3.zero;
                LocalRotation = target != null
                    ? target.localRotation
                    : Quaternion.identity;
                LocalScale = target != null
                    ? target.localScale
                    : Vector3.one;
            }

            public Transform Target { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }
    }
}
