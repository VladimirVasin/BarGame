using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        private const float MinimumGlassRainIntensity = 0.02f;
        private const string GlassRainShaderName =
            "Bar Promenade/City Bus Glass Rain";

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
        private static readonly int RainIntensityId =
            Shader.PropertyToID("_RainIntensity");
        private static readonly int WiperAId =
            Shader.PropertyToID("_WiperA");
        private static readonly int WiperBId =
            Shader.PropertyToID("_WiperB");
        private static readonly int WiperMaskId =
            Shader.PropertyToID("_WiperMask");
        private static readonly int BusForwardId =
            Shader.PropertyToID("_BusForwardWS");
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
        private readonly List<Renderer> glassRainOverlays =
            new List<Renderer>();
        private readonly List<MaterialPropertyBlock>
            glassRainOverlayProperties =
                new List<MaterialPropertyBlock>();
        private Material glassRainMaterial;
        private float appliedGlassRainIntensity = -1f;
        private Vector3 leftWiperTipLocal;
        private Vector3 rightWiperTipLocal;
        private float wiperBladeReach;
        private float previousLeftWiperAngle;
        private float previousRightWiperAngle;
        private float leftWiperSweepSign = 1f;
        private float rightWiperSweepSign = -1f;
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
        public IReadOnlyList<Renderer> GlassRainOverlays =>
            glassRainOverlays;
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
            CreateGlassRainOverlays();
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
            // The column axis binding points at the windshield, and a
            // positive Unity rotation reads counterclockwise to the
            // viewer that axis points away from — so the rim rolled LEFT
            // under the driver's hands on every right turn. The driver
            // watches from the axis tail: negate it so a positive (right)
            // steer rolls the rim clockwise for him, like a real wheel.
            ApplyAxisPose(
                steeringWheelBase,
                SteeringWheelAngle,
                -registry.SteeringWheelAxisLocal);
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

            float next = IsFinite(factor)
                ? Mathf.Clamp01(factor)
                : 0f;
            if (Mathf.Approximately(next, NightFactor))
            {
                return;
            }

            NightFactor = next;
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
            ApplyGlassRain();
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
            previousLeftWiperAngle = 0f;
            previousRightWiperAngle = 0f;
            leftWiperSweepSign = 1f;
            rightWiperSweepSign = -1f;
            ApplyGlassRain();
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
            // The reference must be the VEHICLE ROOT, never the imported
            // Body node: Body's own up reads (0, 0, -1) in root space —
            // the very import rotation this resolution exists to absorb —
            // so resolving against Body.up handed the pivots the
            // longitudinal axis and the front wheels leaned into corners
            // instead of turning.
            frontLeftSteeringAxisLocal = ResolveVerticalAxisLocal(
                registry.FrontLeftSteeringPivot,
                transform);
            frontRightSteeringAxisLocal = ResolveVerticalAxisLocal(
                registry.FrontRightSteeringPivot,
                transform);
            steeringWheelBase = new TransformPose(
                registry.SteeringWheelPivot);
            doorButtonBase = new TransformPose(
                registry.DoorButtonPivot);
            leftWiperBase = new TransformPose(
                registry.LeftWiperPivot);
            rightWiperBase = new TransformPose(
                registry.RightWiperPivot);
            // The reference must be the VEHICLE ROOT, like the wheel
            // pivots: the imported Body's own forward reads (0, -1, 0)
            // in root space, so resolving against it swung the blades
            // around the vehicle vertical — door-style — instead of
            // arcing them across the windshield around its normal.
            leftWiperAxisLocal = ResolveForwardAxisLocal(
                registry.LeftWiperPivot,
                transform);
            rightWiperAxisLocal = ResolveForwardAxisLocal(
                registry.RightWiperPivot,
                transform);
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

        /// <summary>
        /// Clones every glass pane into a droplet overlay: the same mesh,
        /// pulled a hair toward the camera by the shader's depth offset,
        /// carrying the procedural running-drop layer. Cloning keeps the
        /// panes' own translucent look untouched and needs no prefab
        /// rebuild — the drops simply stop rendering while the glass is
        /// dry.
        /// </summary>
        private void CreateGlassRainOverlays()
        {
            glassRainOverlays.Clear();
            glassRainOverlayProperties.Clear();
            Shader shader = Shader.Find(GlassRainShaderName);
            if (shader == null)
            {
                return;
            }

            glassRainMaterial = new Material(shader)
            {
                name = "City Bus Glass Rain (Runtime)"
            };
            IReadOnlyList<CityBusRendererBinding> bindings =
                registry.RendererBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                CityBusRendererBinding binding = bindings[index];
                if (binding == null ||
                    binding.MaterialSlot != CityBusMaterialSlot.Glass ||
                    binding.Renderer == null)
                {
                    continue;
                }

                MeshFilter sourceFilter =
                    binding.Renderer.GetComponent<MeshFilter>();
                if (sourceFilter == null ||
                    sourceFilter.sharedMesh == null)
                {
                    continue;
                }

                GameObject overlayObject = new GameObject(
                    binding.Renderer.name + " Rain Drops");
                overlayObject.layer =
                    binding.Renderer.gameObject.layer;
                Transform overlayTransform = overlayObject.transform;
                overlayTransform.SetParent(
                    binding.Renderer.transform,
                    false);
                overlayObject.AddComponent<MeshFilter>().sharedMesh =
                    sourceFilter.sharedMesh;
                MeshRenderer overlay =
                    overlayObject.AddComponent<MeshRenderer>();
                overlay.sharedMaterial = glassRainMaterial;
                overlay.shadowCastingMode = ShadowCastingMode.Off;
                overlay.receiveShadows = false;
                overlay.lightProbeUsage = LightProbeUsage.Off;
                overlay.reflectionProbeUsage =
                    ReflectionProbeUsage.Off;
                overlay.motionVectorGenerationMode =
                    MotionVectorGenerationMode.Object;
                overlay.enabled = false;
                var properties = new MaterialPropertyBlock();
                properties.SetFloat(RainIntensityId, 0f);
                overlay.SetPropertyBlock(properties);
                glassRainOverlays.Add(overlay);
                glassRainOverlayProperties.Add(properties);
            }

            MeasureWiperBlades();
        }

        /// <summary>
        /// Measures each parked wiper's blade tip in its pivot's local
        /// space, so the wipe mask can follow the VISIBLE blade every
        /// frame instead of re-deriving rest angles from the imported
        /// axes — the sweep mask and the drawn arm can never disagree.
        /// </summary>
        private void MeasureWiperBlades()
        {
            wiperBladeReach = 0f;
            leftWiperTipLocal = MeasureWiperTipLocal(
                registry.LeftWiperPivot,
                ref wiperBladeReach);
            rightWiperTipLocal = MeasureWiperTipLocal(
                registry.RightWiperPivot,
                ref wiperBladeReach);
        }

        private static Vector3 MeasureWiperTipLocal(
            Transform pivot,
            ref float reach)
        {
            if (pivot == null)
            {
                return Vector3.zero;
            }

            Vector3 tipWorld = pivot.position;
            float best = 0f;
            Renderer[] renderers =
                pivot.GetComponentsInChildren<Renderer>();
            for (int index = 0; index < renderers.Length; index++)
            {
                Bounds bounds = renderers[index].bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0
                            ? bounds.min.x
                            : bounds.max.x,
                        (corner & 2) == 0
                            ? bounds.min.y
                            : bounds.max.y,
                        (corner & 4) == 0
                            ? bounds.min.z
                            : bounds.max.z);
                    float distance = Vector3.Distance(
                        point,
                        pivot.position);
                    if (distance > best)
                    {
                        best = distance;
                        tipWorld = point;
                    }
                }
            }

            reach = Mathf.Max(reach, best);
            return pivot.InverseTransformPoint(tipWorld);
        }

        /// <summary>
        /// Pushes the droplet state for this frame: intensity plus the
        /// wipe mask that trails the visible blades, so the drops
        /// vanish exactly where a blade has just squeegeed and regrow
        /// toward its return stroke. Dry glass turns the overlays off
        /// entirely.
        /// </summary>
        private void ApplyGlassRain()
        {
            if (glassRainOverlayProperties.Count == 0)
            {
                return;
            }

            float intensity =
                RainIntensity < MinimumGlassRainIntensity
                    ? 0f
                    : RainIntensity;
            if (intensity <= 0f)
            {
                if (Mathf.Approximately(appliedGlassRainIntensity, 0f))
                {
                    return;
                }

                appliedGlassRainIntensity = 0f;
                for (int index = 0;
                     index < glassRainOverlays.Count;
                     index++)
                {
                    if (glassRainOverlays[index] != null)
                    {
                        glassRainOverlays[index].enabled = false;
                    }
                }

                return;
            }

            appliedGlassRainIntensity = intensity;
            Vector3 busForward = transform.forward;
            Vector3 acrossAxis = Vector3.Cross(
                busForward,
                Vector3.down);
            acrossAxis = acrossAxis.sqrMagnitude > 0.0001f
                ? acrossAxis.normalized
                : Vector3.right;
            bool wiping = wipersRunning &&
                wiperBladeReach > 0.01f &&
                leftWiperBase.Target != null &&
                rightWiperBase.Target != null;
            float leftAngle = 0f;
            float rightAngle = 0f;
            if (wiping)
            {
                leftAngle = MeasureBladeAngle(
                    leftWiperBase.Target,
                    leftWiperTipLocal,
                    acrossAxis);
                rightAngle = MeasureBladeAngle(
                    rightWiperBase.Target,
                    rightWiperTipLocal,
                    acrossAxis);
                UpdateSweepSign(
                    leftAngle,
                    ref previousLeftWiperAngle,
                    ref leftWiperSweepSign);
                UpdateSweepSign(
                    rightAngle,
                    ref previousRightWiperAngle,
                    ref rightWiperSweepSign);
            }

            var mask = wiping
                ? new Vector4(
                    0.12f,
                    wiperBladeReach * 1.06f,
                    1.6f,
                    1f)
                : Vector4.zero;
            for (int index = 0;
                 index < glassRainOverlays.Count;
                 index++)
            {
                Renderer overlay = glassRainOverlays[index];
                if (overlay == null)
                {
                    continue;
                }

                overlay.enabled = true;
                MaterialPropertyBlock properties =
                    glassRainOverlayProperties[index];
                properties.SetFloat(RainIntensityId, intensity);
                properties.SetVector(WiperMaskId, mask);
                if (wiping)
                {
                    Vector3 origin = overlay.transform.position;
                    properties.SetVector(WiperAId, WiperState(
                        leftWiperBase.Target,
                        origin,
                        acrossAxis,
                        leftAngle,
                        leftWiperSweepSign));
                    properties.SetVector(WiperBId, WiperState(
                        rightWiperBase.Target,
                        origin,
                        acrossAxis,
                        rightAngle,
                        rightWiperSweepSign));
                    properties.SetVector(
                        BusForwardId,
                        busForward);
                }

                overlay.SetPropertyBlock(properties);
            }
        }

        private static Vector4 WiperState(
            Transform pivot,
            Vector3 paneOrigin,
            Vector3 acrossAxis,
            float bladeAngle,
            float sweepSign)
        {
            Vector3 relative = pivot.position - paneOrigin;
            return new Vector4(
                Vector3.Dot(relative, acrossAxis),
                relative.y,
                bladeAngle,
                sweepSign);
        }

        private static float MeasureBladeAngle(
            Transform pivot,
            Vector3 tipLocal,
            Vector3 acrossAxis)
        {
            Vector3 offset =
                pivot.TransformPoint(tipLocal) - pivot.position;
            return Mathf.Atan2(
                offset.y,
                Vector3.Dot(offset, acrossAxis));
        }

        private static void UpdateSweepSign(
            float angle,
            ref float previousAngle,
            ref float sweepSign)
        {
            float delta = Mathf.DeltaAngle(
                previousAngle * Mathf.Rad2Deg,
                angle * Mathf.Rad2Deg);
            if (Mathf.Abs(delta) > 0.01f)
            {
                sweepSign = Mathf.Sign(delta);
            }

            previousAngle = angle;
        }

        private void OnDestroy()
        {
            if (glassRainMaterial == null)
            {
                return;
            }

            // Edit-mode teardown (DestroyImmediate cascades in tests)
            // must not route through the deferred Destroy.
            if (Application.isPlaying)
            {
                Destroy(glassRainMaterial);
            }
            else
            {
                DestroyImmediate(glassRainMaterial);
            }

            glassRainMaterial = null;
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
