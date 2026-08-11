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
        private TransformPose frontDoorBase;
        private TransformPose rearDoorBase;
        private TransformPose frontLeftWheelBase;
        private TransformPose frontRightWheelBase;
        private TransformPose rearLeftWheelBase;
        private TransformPose rearRightWheelBase;
        private TransformPose frontLeftSteeringBase;
        private TransformPose frontRightSteeringBase;
        private float wheelRotationDegrees;
        private float brakeFactor;

        public bool IsInitialized { get; private set; }
        public CityBusAssetRegistry Registry => registry;
        public float DoorOpenness { get; private set; }
        public float SteeringAngle { get; private set; }
        public float NightFactor { get; private set; }
        public float BrakeFactor => brakeFactor;

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
            CaptureBasePoses();
            IsInitialized = true;
            ResetForPool();
        }

        public void SetMotion(
            float signedDistance,
            float steeringAngleDegrees,
            bool braking)
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
            ApplyDoorPose(frontDoorBase, DoorOpenness);
            ApplyDoorPose(rearDoorBase, DoorOpenness);
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
            RestorePose(frontDoorBase);
            RestorePose(rearDoorBase);
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
            frontDoorBase = new TransformPose(registry.FrontDoor);
            rearDoorBase = new TransformPose(registry.RearDoor);
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

        private static void ApplyDoorPose(
            TransformPose pose,
            float openness)
        {
            if (pose.Target == null)
            {
                return;
            }

            pose.Target.localPosition = pose.LocalPosition;
            pose.Target.localRotation = pose.LocalRotation *
                Quaternion.AngleAxis(
                    MaximumDoorAngle * openness,
                    Vector3.up);
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
