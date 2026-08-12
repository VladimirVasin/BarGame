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
        public const float MaximumSteeringWheelAngle = 100f;
        public const float MaximumSuspensionHeave = 0.045f;
        public const float MaximumSuspensionPitch = 0.8f;
        public const float MaximumSuspensionRoll = 1f;
        public const float MaximumWiperSweepDegrees = 40f;

        private const float MinimumWiperIntensity = 0.02f;
        private const float MinimumWiperHertz = 0.35f;
        private const float MaximumWiperHertz = 1.15f;
        private const float WiperParkDegreesPerSecond = 110f;

        private const float SuspensionWaveLength = 2.8f;
        private const float SuspensionResponse = 7f;
        private const float AccelerationPitchScale = 0.12f;
        private const float SteeringRollScale = 0.78f;
        private const float SteeringWheelRatio = 3.55f;
        private const float HeadlightBaseIntensity = 14f;
        private const float HeadlightRange = 22f;
        private const float HeadlightSpotAngle = 48f;
        private const float HeadlightInnerSpotAngle = 28f;
        private const float CabinLightBaseIntensity = 7.5f;
        private const float CabinLightRange = 3.6f;
        // World height of the pendant bulb centres authored by
        // tools/build-city-bus-3d-model.py (bulbs span 2.56-2.66 m), so the
        // runtime light visibly originates inside the visible lamps.
        private const float CabinLampHeight = 2.61f;
        private const float CabinLightSpotAngle = 110f;
        private const float CabinLightInnerSpotAngle = 68f;
        private const float VisibleLightFactorThreshold = 0.0001f;
        private const string SuspensionVisualName = "Suspension Visual";

        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");
        private static readonly Color HeadlightEmission =
            new Color(4.2f, 3.55f, 2.35f);
        private static readonly Color TailLightEmission =
            new Color(3.5f, 0.10f, 0.035f);
        private static readonly Color CabinLightEmission =
            new Color(3.1f, 2.0f, 1.05f);
        private static readonly Color HeadlightColor =
            new Color(1f, 0.85f, 0.58f);
        private static readonly Color CabinLightColor =
            new Color(1f, 0.65f, 0.34f);

        private MaterialPropertyBlock lightProperties;
        private CityBusAssetRegistry registry;
        private Light[] headlightLights = Array.Empty<Light>();
        private Light[] cabinLights = Array.Empty<Light>();
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
        private Vector3 frontLeftSteeringAxisLocal = Vector3.up;
        private Vector3 frontRightSteeringAxisLocal = Vector3.up;
        private TransformPose steeringWheelBase;
        private TransformPose doorButtonBase;
        private CityBusDriverPresentation driverPresentation;
        private Transform driverFocusTarget;
        private CityBusDriverDoorSample driverDoorSample;
        private TransformPose leftWiperBase;
        private TransformPose rightWiperBase;
        private Vector3 leftWiperAxisLocal = Vector3.forward;
        private Vector3 rightWiperAxisLocal = Vector3.forward;
        private float wiperPhase;
        private bool wipersRunning;
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
        public float SteeringWheelAngle { get; private set; }
        public float DoorButtonPressFactor { get; private set; }
        public CityBusDoorPhase DoorPhase => driverDoorSample.DoorPhase;
        public CityBusDriverDoorSample DriverDoorSample => driverDoorSample;
        public CityBusDriverPresentation DriverPresentation =>
            driverPresentation;
        public float NightFactor { get; private set; }
        public float RainIntensity { get; private set; }
        public float WiperAngleDegrees { get; private set; }
        public float BrakeFactor => brakeFactor;
        public Transform SuspensionVisual => suspensionVisual;
        public float SuspensionHeave => suspensionHeave;
        public float SuspensionPitch => suspensionPitch;
        public float SuspensionRoll => suspensionRoll;
        public IReadOnlyList<Light> HeadlightLights => headlightLights;
        public IReadOnlyList<Light> CabinLights => cabinLights;

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
            CreateRuntimeLights();
            CaptureDoorHingeAxis();
            CaptureBasePoses();
            IsInitialized = true;
            ResetForPool();
        }

        public void AttachDriver(CityBusDriverAssetRegistry driverRegistry)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the city bus presentation before attaching " +
                    "its driver.");
            }

            if (driverPresentation != null)
            {
                throw new InvalidOperationException(
                    "The city bus presentation already owns a driver.");
            }

            if (driverRegistry == null)
            {
                throw new ArgumentNullException(nameof(driverRegistry));
            }

            driverPresentation = driverRegistry.GetComponent<
                CityBusDriverPresentation>();
            if (driverPresentation == null)
            {
                driverPresentation = driverRegistry.gameObject.AddComponent<
                    CityBusDriverPresentation>();
            }

            driverPresentation.Initialize(
                driverRegistry,
                registry,
                transform);
            driverPresentation.SetPlayerFocusTarget(driverFocusTarget);
            driverPresentation.ResetForPool();
        }

        public void SetDriverFocusTarget(Transform playerRoot)
        {
            driverFocusTarget = playerRoot;
            if (driverPresentation != null)
            {
                driverPresentation.SetPlayerFocusTarget(playerRoot);
            }
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
            ApplyAxisPose(
                frontLeftSteeringBase,
                SteeringAngle,
                frontLeftSteeringAxisLocal);
            ApplyAxisPose(
                frontRightSteeringBase,
                SteeringAngle,
                frontRightSteeringAxisLocal);
            SteeringWheelAngle = Mathf.Clamp(
                SteeringAngle * SteeringWheelRatio,
                -MaximumSteeringWheelAngle,
                MaximumSteeringWheelAngle);
            ApplyAxisPose(
                steeringWheelBase,
                SteeringWheelAngle,
                registry.SteeringWheelAxisLocal);
            AdvanceSuspension(
                signedDistance,
                speedMetersPerSecond,
                longitudinalAcceleration,
                SteeringAngle,
                deltaTime);
            SetBrakeFactor(braking ? 1f : 0f);
            ApplyDriverControls(deltaTime);
        }

        public void SetDriverDoorSample(
            CityBusDriverDoorSample sample)
        {
            if (!IsInitialized)
            {
                return;
            }

            driverDoorSample = sample;
            SetDoors(sample.DoorOpenness);
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

        /// <summary>
        /// Advances the windshield wipers for one frame: rain intensity sets
        /// the sweep rate, and a dry frame parks the blades back at rest
        /// instead of freezing them mid-sweep.
        /// </summary>
        public void AdvanceWipers(float rainIntensity, float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            RainIntensity = IsFinite(rainIntensity)
                ? Mathf.Clamp01(rainIntensity)
                : 0f;
            float safeDelta = IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
            if (RainIntensity >= MinimumWiperIntensity)
            {
                if (!wipersRunning)
                {
                    // Re-enter the sweep at the parked arm's own phase so a
                    // rain restart cannot teleport the blades.
                    wipersRunning = true;
                    wiperPhase = Mathf.Repeat(
                        Mathf.Asin(
                            Mathf.Clamp(
                                WiperAngleDegrees /
                                MaximumWiperSweepDegrees,
                                -1f,
                                1f)) /
                        (Mathf.PI * 2f),
                        1f);
                }

                float sweepHertz = Mathf.Lerp(
                    MinimumWiperHertz,
                    MaximumWiperHertz,
                    RainIntensity);
                wiperPhase = Mathf.Repeat(
                    wiperPhase + safeDelta * sweepHertz,
                    1f);
                WiperAngleDegrees =
                    Mathf.Sin(wiperPhase * Mathf.PI * 2f) *
                    MaximumWiperSweepDegrees;
            }
            else
            {
                wipersRunning = false;
                WiperAngleDegrees = Mathf.MoveTowards(
                    WiperAngleDegrees,
                    0f,
                    WiperParkDegreesPerSecond * safeDelta);
            }

            ApplyAxisPose(
                leftWiperBase,
                WiperAngleDegrees,
                leftWiperAxisLocal);
            ApplyAxisPose(
                rightWiperBase,
                -WiperAngleDegrees,
                rightWiperAxisLocal);
        }

        public void ResetForPool()
        {
            if (!IsInitialized)
            {
                return;
            }

            wheelRotationDegrees = 0f;
            SteeringAngle = 0f;
            SteeringWheelAngle = 0f;
            DoorOpenness = 0f;
            DoorButtonPressFactor = 0f;
            driverDoorSample = default;
            NightFactor = 0f;
            RainIntensity = 0f;
            WiperAngleDegrees = 0f;
            wiperPhase = 0f;
            wipersRunning = false;
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
            RestorePose(steeringWheelBase);
            RestorePose(doorButtonBase);
            RestorePose(leftWiperBase);
            RestorePose(rightWiperBase);
            if (driverPresentation != null)
            {
                driverPresentation.ResetForPool();
            }
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
            frontLeftSteeringAxisLocal = ResolveVerticalAxisLocal(
                registry.FrontLeftSteeringPivot,
                registry.Body);
            frontRightSteeringAxisLocal = ResolveVerticalAxisLocal(
                registry.FrontRightSteeringPivot,
                registry.Body);
            steeringWheelBase = new TransformPose(
                registry.SteeringWheelPivot);
            doorButtonBase = new TransformPose(
                registry.DoorButtonPivot);
            leftWiperBase = new TransformPose(
                registry.LeftWiperPivot);
            rightWiperBase = new TransformPose(
                registry.RightWiperPivot);
            leftWiperAxisLocal = ResolveForwardAxisLocal(
                registry.LeftWiperPivot,
                registry.Body);
            rightWiperAxisLocal = ResolveForwardAxisLocal(
                registry.RightWiperPivot,
                registry.Body);
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

        private void CreateRuntimeLights()
        {
            Bounds bounds = registry.LocalBounds;
            float headlightZ = bounds.max.z + 0.06f;
            Vector3 headlightDirection =
                new Vector3(0f, -0.12f, 1f).normalized;
            headlightLights = new[]
            {
                CreateSpotLight(
                    "Bus Headlight Left",
                    new Vector3(-0.72f, 0.96f, headlightZ),
                    headlightDirection,
                    HeadlightColor,
                    HeadlightRange,
                    HeadlightSpotAngle,
                    HeadlightInnerSpotAngle),
                CreateSpotLight(
                    "Bus Headlight Right",
                    new Vector3(0.72f, 0.96f, headlightZ),
                    headlightDirection,
                    HeadlightColor,
                    HeadlightRange,
                    HeadlightSpotAngle,
                    HeadlightInnerSpotAngle)
            };

            float cabinY = CabinLampHeight;
            cabinLights = new[]
            {
                CreateSpotLight(
                    "Bus Cabin Light Front",
                    new Vector3(0f, cabinY, 1.45f),
                    Vector3.down,
                    CabinLightColor,
                    CabinLightRange,
                    CabinLightSpotAngle,
                    CabinLightInnerSpotAngle),
                CreateSpotLight(
                    "Bus Cabin Light Rear",
                    new Vector3(0f, cabinY, -1.45f),
                    Vector3.down,
                    CabinLightColor,
                    CabinLightRange,
                    CabinLightSpotAngle,
                    CabinLightInnerSpotAngle)
            };
        }

        private Light CreateSpotLight(
            string objectName,
            Vector3 presentationLocalPosition,
            Vector3 presentationLocalDirection,
            Color color,
            float range,
            float spotAngle,
            float innerSpotAngle)
        {
            GameObject lightObject = new GameObject(objectName);
            lightObject.layer = gameObject.layer;
            Transform lightTransform = lightObject.transform;
            lightTransform.SetParent(suspensionVisual, false);
            Vector3 localUp = Mathf.Abs(Vector3.Dot(
                presentationLocalDirection,
                Vector3.up)) > 0.98f
                    ? Vector3.forward
                    : Vector3.up;
            lightTransform.SetPositionAndRotation(
                transform.TransformPoint(presentationLocalPosition),
                transform.rotation * Quaternion.LookRotation(
                    presentationLocalDirection,
                    localUp));

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = 0f;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            light.enabled = false;
            return light;
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
            SetRuntimeLightFactor(
                headlightLights,
                HeadlightBaseIntensity);
            SetRuntimeLightFactor(
                cabinLights,
                CabinLightBaseIntensity);
        }

        private void SetRuntimeLightFactor(
            IReadOnlyList<Light> lights,
            float baseIntensity)
        {
            bool visible = NightFactor > VisibleLightFactorThreshold;
            for (int index = 0; index < lights.Count; index++)
            {
                Light target = lights[index];
                if (target == null)
                {
                    continue;
                }

                target.intensity = baseIntensity * NightFactor;
                target.enabled = visible;
            }
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

        private void ApplyDriverControls(float deltaTime)
        {
            DoorButtonPressFactor = IsFinite(
                    driverDoorSample.ButtonPress01)
                ? Mathf.Clamp01(driverDoorSample.ButtonPress01)
                : 0f;
            if (doorButtonBase.Target != null)
            {
                doorButtonBase.Target.localPosition =
                    doorButtonBase.LocalPosition +
                    registry.DoorButtonTravelLocal *
                    DoorButtonPressFactor;
                doorButtonBase.Target.localRotation =
                    doorButtonBase.LocalRotation;
            }

            if (driverPresentation != null)
            {
                driverPresentation.ApplyPose(
                    driverDoorSample.RightHandButtonBlend,
                    driverDoorSample.DoorLook01,
                    deltaTime);
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

        /// <summary>
        /// Resolves which of a pivot's own axes points along the vehicle
        /// vertical, measured once from its base pose.
        /// <para>
        /// The imported wheel pivots do not carry the vehicle's own basis: the
        /// bus up direction reads as `(0, 0, -1)` in their local space, so
        /// steering around `Vector3.up` turned the front wheels about the
        /// longitudinal axis and they leaned instead of turning. Rolling has
        /// always used the lateral axis, which happens to survive the same
        /// mapping, which is why only the steering looked wrong. Deriving the
        /// axis from the model keeps this correct through any re-export
        /// instead of trusting a hard-coded one.
        /// </para>
        /// </summary>
        private static Vector3 ResolveVerticalAxisLocal(
            Transform pivot,
            Transform reference)
        {
            if (pivot == null || reference == null)
            {
                return Vector3.up;
            }

            Vector3 axis = pivot.InverseTransformDirection(reference.up);
            return axis.sqrMagnitude > 0.0001f
                ? axis.normalized
                : Vector3.up;
        }

        /// <summary>
        /// Resolves which of a pivot's own axes points along the vehicle
        /// longitudinal direction — the windshield normal the wipers sweep
        /// around. Derived from the model for the same reason as the wheel
        /// vertical axis: imported pivots do not carry the vehicle basis.
        /// </summary>
        private static Vector3 ResolveForwardAxisLocal(
            Transform pivot,
            Transform reference)
        {
            if (pivot == null || reference == null)
            {
                return Vector3.forward;
            }

            Vector3 axis = pivot.InverseTransformDirection(
                reference.forward);
            return axis.sqrMagnitude > 0.0001f
                ? axis.normalized
                : Vector3.forward;
        }

        private static void ApplyAxisPose(
            TransformPose pose,
            float angle,
            Vector3 localAxis)
        {
            if (pose.Target == null)
            {
                return;
            }

            Vector3 axis = localAxis.sqrMagnitude > 0.0001f
                ? localAxis.normalized
                : Vector3.up;
            pose.Target.localPosition = pose.LocalPosition;
            pose.Target.localRotation = pose.LocalRotation *
                Quaternion.AngleAxis(angle, axis);
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
