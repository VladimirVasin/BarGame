using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityNightAtmosphere : MonoBehaviour
    {
        public const int MaximumRealtimeLights = 2048;
        public const float MinimumTunnelFlickerMultiplier = 0.05f;

        private const float AvailabilityRefreshInterval = 0.35f;
        internal const float PracticalLeaseDistance = 20f;
        internal const string IndustrialTaskLightPrefix = "IndustrialTaskLight_";
        internal const string FixedLightPrefix = "Fixed City Light ";
        private const int MaximumIndustrialTaskLights = 512;

        // The mast grew from 2.92 m to 4.70 m of source height; the
        // inverse-square law wants (4.70/2.92)^2 = 2.6x the luminous
        // power for the same pavement below.
        private const float StreetLightIntensity = 31f;
        private const float StreetLightRange = 16.5f;
        private const float HillsideLightRange = 22f;
        private const float BarLightIntensity = 8f;
        private const float VisibleFactorThreshold = 0.0001f;

        // The river's waterside lanterns. Their lens hangs about a
        // metre over the surface it lights where the street mast
        // stands 4.7 m over its pavement, so the street's 31 would
        // blow the granite out; the wide cone is what lets one low
        // fixture graze the wall face and lay a pool on the water.
        private const float QuayLampIntensity = 6f;
        private const float QuayLampRange = 10f;
        private const float QuayLampSpotAngle = 130f;
        private const float QuayLampInnerSpotAngle = 70f;
        private const float QuayLampHaloInnerSize = 0.70f;
        private const float QuayLampHaloOuterSize = 1.90f;

        private static readonly Color StreetLightColor =
            new Color(1f, 0.72f, 0.42f);
        private static readonly Color BarLightColor =
            new Color(1f, 0.76f, 0.48f);
        private static readonly Color StreetHaloInner =
            new Color(4.0f, 2.0f, 0.55f, 0.18f);
        private static readonly Color StreetHaloOuter =
            new Color(2.2f, 1.25f, 0.42f, 0.045f);
        private static readonly Color BarHaloInner =
            new Color(4.2f, 2.3f, 0.78f, 0.20f);
        private static readonly Color BarHaloOuter =
            new Color(2.4f, 1.45f, 0.58f, 0.055f);

        internal static Color StreetLampColor => StreetLightColor;

        private Transform player;
        private Transform[] busStopPoles = Array.Empty<Transform>();
        private CityBusStopLighting[] busStopFixtures = Array.Empty<CityBusStopLighting>();
        private Camera stopShadowCamera;
        private Func<bool> stopShadowActive;
        private readonly Plane[] stopShadowFrustum = new Plane[6];
        private readonly int[] desiredShadowStops = { -1, -1 };
        private readonly float[] shadowStopDistances = new float[CityBusStopLighting.MaximumShadowedStops];
        private float nextStopShadowRefresh;
        private Vector3 lastStopShadowCamera;
        private static readonly Color PlateLampColor = new Color(1f, .89f, .73f);
        private Transform[] lampAnchors = Array.Empty<Transform>();
        private CityFringePracticalAnchor[] practicalAnchors =
            Array.Empty<CityFringePracticalAnchor>();
        private Light[] streetLightPool = Array.Empty<Light>();
        private Light[] barLights = Array.Empty<Light>();
        private CityLightHalo[] streetLightHalos =
            Array.Empty<CityLightHalo>();
        private CityLightHalo[] barLightHalos =
            Array.Empty<CityLightHalo>();
        private int[] selectedAnchorIndices = Array.Empty<int>();
        private float[] pooledLightBaseIntensities = Array.Empty<float>();
        // Static fixtures keep their Light for their complete exterior lifetime.
        // Only fringe practicals need an additional halo: masts, quay and task
        // fixtures already own fixed halos independent of the actual Light.
        private bool[] pooledHaloVisible = Array.Empty<bool>();
        private int activePracticalIndex = -1;
        private int quayAnchorStartIndex = int.MaxValue;
        private int industrialAnchorStartIndex = int.MaxValue;
        private CityIndustrialPurpose[] industrialPurposes = Array.Empty<CityIndustrialPurpose>();
        private bool[] industrialUseShadows = Array.Empty<bool>();
        private float[] industrialIntensityScales = Array.Empty<float>();
        private float[] industrialRangeLimits = Array.Empty<float>();
        private float[] industrialSpotAngles = Array.Empty<float>();
        private float[] industrialInnerSpotAngles = Array.Empty<float>();
        private bool[] industrialStreetOnly = Array.Empty<bool>();
        private UniversalAdditionalLightData[] pooledShadowData = Array.Empty<UniversalAdditionalLightData>();
        private CityLightHalo[] industrialFixtureHalos = Array.Empty<CityLightHalo>();
        private CityCoveredPassageLight[] coveredPassageLights = Array.Empty<CityCoveredPassageLight>();
        private CityGlassReturnWorkLight[] glassReturnWorkLights = Array.Empty<CityGlassReturnWorkLight>();
        private bool[] industrialIsCoveredPassage = Array.Empty<bool>();
        private readonly HashSet<Light> residentialEntranceLights = new HashSet<Light>();
        private readonly Dictionary<Light, (CityBusStopLighting fixture, int source)> busStopLightOwners =
            new Dictionary<Light, (CityBusStopLighting, int)>();
        public int IndustrialTaskLightCount => industrialPurposes.Length;
        private float nextAvailabilityRefresh;
        private int practicalLightStart, plateLightStart, stopLightStart;
        private bool fixedBindingsReady;
        private float nightFactor = 1f;
        private float tunnelPracticalFlickerMultiplier = 1f;
        private bool hasAppliedNightFactor;

        public IReadOnlyList<Transform> LampAnchors => lampAnchors;
        public IReadOnlyList<Light> StreetLightPool => streetLightPool;
        public IReadOnlyList<Light> BarLights => barLights;
        public IReadOnlyList<CityBusStopLighting> BusStopFixtures => busStopFixtures;
        public float NightFactor => nightFactor;
        public float TunnelPracticalFlickerMultiplier =>
            tunnelPracticalFlickerMultiplier;
        // Retained for diagnostics compatibility: fixed fixtures never reassign.
        public int ReassignmentCount => 0;
        public int RealtimeLightCount =>
            streetLightPool.Length + barLights.Length;
        internal bool IsPracticalSlotLeased { get; private set; }
        internal CityFringeYardKind? ActivePracticalKind =>
            IsPracticalSlotLeased &&
            activePracticalIndex >= 0 &&
            activePracticalIndex < practicalAnchors.Length
                ? practicalAnchors[activePracticalIndex].Kind
                : null;
        internal Light ActivePracticalLight =>
            IsPracticalSlotLeased && activePracticalIndex >= 0
                ? streetLightPool[practicalLightStart + activePracticalIndex]
                : null;
        public int FixedFixtureCount => RealtimeLightCount;
        public int FixedAnchorLightCount => lampAnchors.Length;

        public Light GetLightForAnchor(int anchorIndex) =>
            anchorIndex >= 0 && anchorIndex < lampAnchors.Length
                ? streetLightPool[anchorIndex] : null;

        internal bool TryGetBusStopSource(Light light, out CityBusStopLighting fixture, out int source)
        {
            if (busStopLightOwners.TryGetValue(light, out var binding))
            { fixture = binding.fixture; source = binding.source; return true; }
            fixture = null; source = -1; return false;
        }

        public Light GetPracticalLight(CityFringeYardKind kind)
        {
            for (int i = 0; i < practicalAnchors.Length; i++)
                if (practicalAnchors[i].Kind == kind) return streetLightPool[practicalLightStart + i];
            return null;
        }
        internal int AssignedStreetLightCount { get; private set; }

        public void Initialize(
            Transform playerTransform,
            IReadOnlyList<Transform> streetLampAnchors,
            IReadOnlyList<Vector3> barLightPositions,
            IReadOnlyList<CityFringePracticalAnchor>
                fringePracticalAnchors = null,
            IReadOnlyList<Transform> quayLampAnchors = null, Transform busStops = null)
        {
            if (fixedBindingsReady)
                throw new InvalidOperationException("City fixed lighting is already initialized.");
            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            if (streetLampAnchors == null)
            {
                throw new ArgumentNullException(nameof(streetLampAnchors));
            }

            if (barLightPositions == null)
            {
                throw new ArgumentNullException(nameof(barLightPositions));
            }

            // Stable index ranges preserve every fixture's physical source,
            // authored aim and special shadow profile at any observer distance.
            Transform[] taskAnchors = FindIndustrialTaskLights(transform.parent);
            industrialAnchorStartIndex = streetLampAnchors.Count + (quayLampAnchors?.Count ?? 0);
            lampAnchors = CopyAnchors(streetLampAnchors, quayLampAnchors, taskAnchors);
            quayAnchorStartIndex = streetLampAnchors.Count;
            industrialPurposes = new CityIndustrialPurpose[taskAnchors.Length];
            industrialUseShadows = new bool[taskAnchors.Length];
            industrialIntensityScales = new float[taskAnchors.Length];
            industrialRangeLimits = new float[taskAnchors.Length];
            industrialSpotAngles = new float[taskAnchors.Length];
            industrialInnerSpotAngles = new float[taskAnchors.Length];
            industrialStreetOnly = new bool[taskAnchors.Length];
            industrialFixtureHalos = new CityLightHalo[taskAnchors.Length];
            coveredPassageLights = new CityCoveredPassageLight[taskAnchors.Length];
            glassReturnWorkLights = new CityGlassReturnWorkLight[taskAnchors.Length];
            industrialIsCoveredPassage = new bool[taskAnchors.Length];
            for (int index = 0; index < taskAnchors.Length; index++)
            {
                string purposeName = taskAnchors[index].name.Substring(IndustrialTaskLightPrefix.Length);
                industrialIntensityScales[index] = 1f;
                industrialRangeLimits[index] = float.PositiveInfinity;
                switch (purposeName)
                {
                    case "Substation": industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse; break;
                    case "MunicipalWorkshop": industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop; break;
                    case "PortWorkshop": industrialPurposes[index] = CityIndustrialPurpose.MetalWorkshop; break;
                    case "PhoneBooth":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .7f;
                        industrialRangeLimits[index] = 6.5f;
                        industrialSpotAngles[index] = 130f;
                        industrialInnerSpotAngles[index] = 85f; break;
                    case "RidgeKiosk":
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = 6f;
                        industrialRangeLimits[index] = 14f;
                        industrialSpotAngles[index] = 120f;
                        industrialInnerSpotAngles[index] = 50f;
                        industrialStreetOnly[index] = true;
                        taskAnchors[index].GetComponentInParent<RidgeKioskStreetLightReceivers>()
                            .Initialize(transform.parent, taskAnchors[index].position, industrialRangeLimits[index]);
                        break;
                    case RidgeKioskCourtWorld.LampPurpose:
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialIntensityScales[index] = .15f;
                        industrialRangeLimits[index] = 3.4f;
                        industrialSpotAngles[index] = 164f;
                        industrialInnerSpotAngles[index] = 108f;
                        break;
                    case "PigeonLoftAviary":
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .12f;
                        industrialRangeLimits[index] = 3f;
                        industrialSpotAngles[index] = 145f;
                        industrialInnerSpotAngles[index] = 95f; break;
                    case "PigeonLoft":
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .32f;
                        industrialRangeLimits[index] = 6f;
                        industrialSpotAngles[index] = 145f;
                        industrialInnerSpotAngles[index] = 100f; break;
                    case "RidgeKioskInterior":
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .03f;
                        industrialRangeLimits[index] = 4.2f;
                        industrialSpotAngles[index] = 165f;
                        industrialInnerSpotAngles[index] = 150f; break;
                    case "HomeEntrance":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .28f;
                        industrialRangeLimits[index] = 4.5f; break;
                    case "ResidentialEntrance":
                    case "CourtyardEntrance":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .5f;
                        industrialRangeLimits[index] = 5.5f;
                        industrialSpotAngles[index] = 155f;
                        industrialInnerSpotAngles[index] = 95f; break;
                    case "PawnshopEntrance":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = 2f;
                        industrialRangeLimits[index] = 12f;
                        industrialSpotAngles[index] = 150f;
                        industrialInnerSpotAngles[index] = 100f; break;
                    case "PawnshopSignLeft":
                    case "PawnshopSignRight":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = 1.2f;
                        industrialRangeLimits[index] = 8f;
                        industrialSpotAngles[index] = 145f;
                        industrialInnerSpotAngles[index] = 125f; break;
                    case "PawnshopFacadeLeft":
                    case "PawnshopFacadeRight":
                    case "PawnshopFacadeRear":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = 4f;
                        industrialRangeLimits[index] = 15f;
                        industrialSpotAngles[index] = 150f;
                        industrialInnerSpotAngles[index] = 105f; break;
                    case "RidgeFishSign":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialIntensityScales[index] = 6f;
                        industrialRangeLimits[index] = 10f;
                        industrialSpotAngles[index] = 108f;
                        industrialInnerSpotAngles[index] = 80f; break;
                    case "RidgeBench":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = 2.5f;
                        industrialRangeLimits[index] = 11f;
                        industrialSpotAngles[index] = 145f;
                        industrialInnerSpotAngles[index] = 110f; break;
                    case "CoveredPassage":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .60f;
                        industrialRangeLimits[index] = 7f;
                        industrialSpotAngles[index] = 150f;
                        industrialInnerSpotAngles[index] = 105f;
                        industrialIsCoveredPassage[index] = true;
                        coveredPassageLights[index] = taskAnchors[index].GetComponent<CityCoveredPassageLight>() ??
                            throw new InvalidOperationException("A covered passage source needs its fixed fixture marker.");
                        break;
                    case "GlassReturnFloodlight":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = 16f;
                        industrialRangeLimits[index] = 19f;
                        industrialSpotAngles[index] = 140f;
                        industrialInnerSpotAngles[index] = 105f;
                        break;
                    case "GlassReturnWindow":
                        glassReturnWorkLights[index] = taskAnchors[index].GetComponent<CityGlassReturnWorkLight>() ??
                            throw new InvalidOperationException("Glass return work light requires its physical switch circuit.");
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialIntensityScales[index] = .52f;
                        industrialRangeLimits[index] = 4.2f;
                        industrialSpotAngles[index] = 96f;
                        industrialInnerSpotAngles[index] = 66f;
                        break;
                    case "KioskSign":
                    case "GlassReturnSignLeft":
                    case "GlassReturnSignRight":
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialIntensityScales[index] = .035f;
                        industrialRangeLimits[index] = 1.6f;
                        industrialSpotAngles[index] = 155f;
                        industrialInnerSpotAngles[index] = 125f;
                        break;
                    case "LaundryWork":
                        industrialPurposes[index] = CityIndustrialPurpose.RepairWorkshop;
                        industrialIntensityScales[index] = .75f;
                        industrialUseShadows[index] = true; break;
                    case "LaundryMachines":
                        industrialPurposes[index] = CityIndustrialPurpose.ColdStorage;
                        industrialIntensityScales[index] = .625f;
                        industrialUseShadows[index] = true; break;
                    case "LaundryCourt":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialIntensityScales[index] = .60f;
                        industrialUseShadows[index] = true; break;
                    case "LaundryEntrance":
                        industrialPurposes[index] = CityIndustrialPurpose.CompressorHouse;
                        industrialUseShadows[index] = true;
                        industrialIntensityScales[index] = .01f;
                        industrialRangeLimits[index] = 4f; break;
                    default:
                        if (!Enum.TryParse(purposeName, out industrialPurposes[index]) || industrialPurposes[index] == CityIndustrialPurpose.None)
                            throw new InvalidOperationException("Unknown functional industrial task light: " + taskAnchors[index].name);
                        break;
                }
                PracticalLightProfile profile = GetIndustrialProfile(industrialPurposes[index]);
                // Two lobes describe one kiosk diffuser. The sign projectors
                // are hooded toward their board, so only their real lenses glow.
                if (purposeName == "RidgeKioskInterior" || purposeName == "GlassReturnWindow" || purposeName == "PawnshopSignLeft" ||
                    purposeName == "PawnshopSignRight" || purposeName == "GlassReturnSignLeft" ||
                    purposeName == "GlassReturnSignRight" || purposeName == "KioskSign" ||
                    purposeName == RidgeKioskCourtWorld.LampPurpose) continue;
                // Imported anchors inherit the FBX unit factor. Keep particle
                // sizes in world metres and move their soft depth fade clear
                // of the fixture housing and the adjacent opaque facade.
                Transform haloOwner = new GameObject("Industrial Fixed Halo " + index).transform;
                haloOwner.SetParent(transform, true);
                haloOwner.SetPositionAndRotation(taskAnchors[index].position +
                    taskAnchors[index].forward * (industrialUseShadows[index] ? 0f : .30f),
                    taskAnchors[index].rotation);
                industrialFixtureHalos[index] = CityLightHalo.CreateAlwaysBurning(haloOwner, Vector3.zero,
                    profile.HaloInnerSize * (industrialUseShadows[index] ? .5f : 1f),
                    profile.HaloOuterSize * (industrialUseShadows[index] ? .5f : 1f),
                    profile.HaloInnerColor * Mathf.Min(industrialIntensityScales[index], 2f),
                    profile.HaloOuterColor * Mathf.Min(industrialIntensityScales[index], 2f));
                if (coveredPassageLights[index] != null) coveredPassageLights[index].BindHalo(industrialFixtureHalos[index]);
            }
            practicalAnchors = CopyPracticalAnchors(
                fringePracticalAnchors);
            if (busStops != null)
            {
                busStopPoles = new Transform[busStops.childCount];
                for (int i = 0; i < busStopPoles.Length; i++) busStopPoles[i] = busStops.GetChild(i);
                busStopFixtures = busStops.GetComponentsInChildren<CityBusStopLighting>(true);
            }
            practicalLightStart = lampAnchors.Length;
            plateLightStart = checked(practicalLightStart + practicalAnchors.Length);
            stopLightStart = checked(plateLightStart + busStopPoles.Length * 2);
            int streetLightCount = checked(stopLightStart + busStopFixtures.Length * CityBusStopLighting.LightCount);
            int barLightCount = barLightPositions.Count;
            if (checked(streetLightCount + barLightCount) > MaximumRealtimeLights)
                throw new InvalidOperationException("City fixed-light catalog exceeds 2048 fixtures.");
            barLights = new Light[barLightCount];
            barLightHalos = new CityLightHalo[barLightCount];
            for (int index = 0; index < barLightCount; index++)
            {
                barLights[index] = CreateLight(
                    $"Bar Entrance Light {index + 1}",
                    barLightPositions[index],
                    BarLightColor,
                    LightType.Point,
                    BarLightIntensity,
                    7.5f,
                    1.05f,
                    2.85f,
                    BarHaloInner,
                    BarHaloOuter,
                    out barLightHalos[index]);
            }

            streetLightPool = new Light[streetLightCount];
            streetLightHalos = new CityLightHalo[streetLightCount];
            selectedAnchorIndices = new int[streetLightCount];
            pooledLightBaseIntensities = new float[streetLightCount];
            pooledHaloVisible = new bool[streetLightCount];
            pooledShadowData = new UniversalAdditionalLightData[streetLightCount];
            for (int index = 0; index < streetLightCount; index++)
            {
                selectedAnchorIndices[index] = -1;
                bool practical = index >= practicalLightStart && index < plateLightStart;
                pooledLightBaseIntensities[index] = StreetLightIntensity;
                string name = FixedLightPrefix + (index + 1);
                if (index >= industrialAnchorStartIndex && index < lampAnchors.Length &&
                    lampAnchors[index].name == IndustrialTaskLightPrefix + RidgeKioskCourtWorld.LampPurpose)
                    name += " " + RidgeKioskCourtWorld.LampPurpose;
                streetLightPool[index] = CreateLight(
                    name, transform.position,
                    StreetLightColor, LightType.Spot, StreetLightIntensity, StreetLightRange,
                    1.15f, 3.10f, StreetHaloInner, StreetHaloOuter,
                    out streetLightHalos[index], practical);
            }
            BindFixedFixtures();

            SetNightFactor(nightFactor);
        }

        private void Update()
        {
            RefreshStreetLights(false);
            RefreshCoveredPassagePower();
        }

        private void LateUpdate()
        {
            if (busStopFixtures.Length == 0) return;
            bool active = stopShadowCamera != null && stopShadowCamera.isActiveAndEnabled &&
                (stopShadowActive == null || stopShadowActive());
            RefreshStopShadows(active ? stopShadowCamera : null, Time.unscaledDeltaTime);
        }

        public void ConfigureStopShadowCamera(Camera camera, Func<bool> active)
        {
            stopShadowCamera = camera;
            stopShadowActive = active;
        }

        /// <summary>Only shadow detail follows the view. Fixed source power and range never do.</summary>
        public void RefreshStopShadows(Camera camera, float deltaTime, bool immediate = false)
        {
            if (!isActiveAndEnabled || camera == null)
            {
                foreach (var stop in busStopFixtures) if (stop != null) stop.ApplyShadowStrength(0f);
                desiredShadowStops[0] = desiredShadowStops[1] = -1;
                return;
            }
            if (immediate || Time.unscaledTime >= nextStopShadowRefresh ||
                (lastStopShadowCamera - camera.transform.position).sqrMagnitude > 4f)
            {
                nextStopShadowRefresh = Time.unscaledTime + .2f;
                lastStopShadowCamera = camera.transform.position;
                GeometryUtility.CalculateFrustumPlanes(camera, stopShadowFrustum);
                for (int slot = 0; slot < desiredShadowStops.Length; slot++)
                { desiredShadowStops[slot] = -1; shadowStopDistances[slot] = float.PositiveInfinity; }
                float reach = Mathf.Min(45f, camera.farClipPlane);
                for (int i = 0; i < busStopFixtures.Length; i++)
                {
                    var stop = busStopFixtures[i];
                    if (stop == null || !stop.isActiveAndEnabled ||
                        !GeometryUtility.TestPlanesAABB(stopShadowFrustum, stop.WorldBounds)) continue;
                    float distance = (stop.transform.position - camera.transform.position).sqrMagnitude;
                    if (distance >= reach * reach) continue;
                    if (stop.ShadowStrength > .0001f) distance *= .8f;
                    int at = 0;
                    while (at < desiredShadowStops.Length && distance >= shadowStopDistances[at]) at++;
                    if (at == desiredShadowStops.Length) continue;
                    for (int move = desiredShadowStops.Length - 1; move > at; move--)
                    { desiredShadowStops[move] = desiredShadowStops[move - 1]; shadowStopDistances[move] = shadowStopDistances[move - 1]; }
                    desiredShadowStops[at] = i; shadowStopDistances[at] = distance;
                }
            }
            float step = immediate ? 1f : Mathf.Clamp(deltaTime, 0f, .1f) * 2.5f;
            int activeCount = 0;
            // Release old shadow maps before admitting a new platform, keeping
            // the cap valid even during a camera cut or a transfer handover.
            for (int i = 0; i < busStopFixtures.Length; i++)
            {
                var stop = busStopFixtures[i];
                if (stop == null) continue;
                if (i != desiredShadowStops[0] && i != desiredShadowStops[1])
                    stop.ApplyShadowStrength(Mathf.MoveTowards(stop.ShadowStrength, 0f, step));
                if (stop.ShadowStrength > .0001f) activeCount++;
            }
            foreach (int index in desiredShadowStops)
            {
                if (index < 0) continue;
                var stop = busStopFixtures[index];
                float distance = Vector3.Distance(stop.transform.position, camera.transform.position);
                float target = stop.isActiveAndEnabled ? Mathf.Clamp01((45f - distance) / 15f) : 0f;
                if (stop.ShadowStrength <= .0001f && target > 0f)
                { if (activeCount >= CityBusStopLighting.MaximumShadowedStops) continue; activeCount++; }
                stop.ApplyShadowStrength(Mathf.MoveTowards(stop.ShadowStrength, target, step));
            }
        }

        public void RefreshImmediate()
        {
            RefreshStreetLights(true);
            RefreshCoveredPassagePower();
        }

        public void SetNightFactor(
            float factor,
            bool force = false)
        {
            float clampedFactor = Mathf.Clamp01(factor);
            if (!force &&
                hasAppliedNightFactor &&
                clampedFactor.Equals(nightFactor))
            {
                return;
            }

            bool wasVisible =
                hasAppliedNightFactor &&
                nightFactor > VisibleFactorThreshold;
            bool firstApplication = !hasAppliedNightFactor;
            nightFactor = clampedFactor;
            hasAppliedNightFactor = true;
            bool visible = nightFactor > VisibleFactorThreshold;

            // Static sources retain the §20 daylight floor at any distance.
            // Their identities and illuminated surfaces never follow the hero.
            float fixtureFactor =
                GameTimeDayNightRules.FixtureFactor(nightFactor);
            foreach (var source in glassReturnWorkLights)
                if (source != null) source.SetFixtureFactor(fixtureFactor);
            for (int index = 0; index < industrialFixtureHalos.Length; index++)
                if (industrialFixtureHalos[index] != null && !industrialIsCoveredPassage[index])
                    industrialFixtureHalos[index].SetIntensityFactor(fixtureFactor);
            for (int index = 0; index < barLights.Length; index++)
            {
                barLights[index].intensity =
                    BarLightIntensity * fixtureFactor;
                barLights[index].enabled = isActiveAndEnabled;
                barLightHalos[index].SetVisible(isActiveAndEnabled);
                barLightHalos[index].SetIntensityFactor(fixtureFactor);
            }

            for (int index = 0; index < streetLightPool.Length; index++)
            {
                float intensityFactor = GetPooledIntensityFactor(index);
                streetLightPool[index].intensity =
                    pooledLightBaseIntensities[index] * intensityFactor;
                if (intensityFactor <= VisibleFactorThreshold)
                {
                    streetLightPool[index].enabled = false;
                }
                else if (IsActivePracticalPoolIndex(index))
                {
                    streetLightPool[index].enabled = isActiveAndEnabled;
                }

                streetLightHalos[index]?.SetIntensityFactor(
                    intensityFactor);
                streetLightHalos[index]?.SetVisible(
                    intensityFactor > VisibleFactorThreshold &&
                    streetLightPool[index].enabled &&
                    pooledHaloVisible[index]);
            }

            if (isActiveAndEnabled &&
                (force || firstApplication || wasVisible != visible))
            {
                RefreshStreetLights(true);
            }
            RefreshCoveredPassagePower();
        }

        private void RefreshCoveredPassagePower()
        {
            if (!isActiveAndEnabled) { RestoreCoveredPassagePower(); return; }
            double absoluteSeconds = CityCoveredPassageFlickerEvaluator.AbsoluteSeconds;
            float fixtureFactor = GameTimeDayNightRules.FixtureFactor(nightFactor);
            for (int index = 0; index < coveredPassageLights.Length; index++)
            {
                if (!industrialIsCoveredPassage[index]) continue;
                var source = coveredPassageLights[index];
                if (source != null && source.gameObject.activeInHierarchy) source.ApplyAt(absoluteSeconds, fixtureFactor);
                else if ((source == null || !source.gameObject.activeInHierarchy) && industrialFixtureHalos[index] != null)
                    industrialFixtureHalos[index].SetVisible(false);
            }
            // A contact fault stays at frame cadence on its fixed source,
            // without multiplying an already attenuated power value.
            for (int slot = 0; slot < streetLightPool.Length; slot++)
            {
                if (IsActivePracticalPoolIndex(slot)) continue;
                int task = selectedAnchorIndices[slot] - industrialAnchorStartIndex;
                if (task < 0 || task >= industrialIsCoveredPassage.Length || !industrialIsCoveredPassage[task]) continue;
                float factor = GetPooledIntensityFactor(slot);
                streetLightPool[slot].intensity = pooledLightBaseIntensities[slot] * factor;
                streetLightPool[slot].enabled = IsAnchorAvailable(selectedAnchorIndices[slot]) && factor > VisibleFactorThreshold;
            }
        }

        private void OnEnable()
        {
            if (!fixedBindingsReady) return;
            foreach (CityLightHalo halo in industrialFixtureHalos) if (halo != null) halo.SetVisible(true);
            RefreshStreetLights(true);
            SetNightFactor(nightFactor, true);
        }

        private void OnDisable()
        {
            foreach (var stop in busStopFixtures) if (stop != null) stop.ApplyShadowStrength(0f);
            RestoreCoveredPassagePower();
            foreach (Light light in streetLightPool) if (light != null) light.enabled = false;
            foreach (Light light in barLights) if (light != null) light.enabled = false;
            foreach (CityLightHalo halo in streetLightHalos) if (halo != null) halo.SetVisible(false);
            foreach (CityLightHalo halo in barLightHalos) if (halo != null) halo.SetVisible(false);
            foreach (CityLightHalo halo in industrialFixtureHalos) if (halo != null) halo.SetVisible(false);
        }

        private void OnDestroy()
        {
            RestoreCoveredPassagePower();
            residentialEntranceLights.Clear();
            busStopLightOwners.Clear(); stopShadowCamera = null; stopShadowActive = null;
            foreach (Light light in streetLightPool) DestroyOwnedLight(light);
            foreach (Light light in barLights) DestroyOwnedLight(light);
            foreach (CityLightHalo halo in industrialFixtureHalos)
                if (halo != null) DestroyOwnedObject(halo.transform.parent.gameObject);
        }

        private static void DestroyOwnedLight(Light light)
        {
            if (light == null) return;
            DestroyOwnedObject(light.gameObject);
        }

        private static void DestroyOwnedObject(GameObject target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void RestoreCoveredPassagePower()
        {
            float fixtureFactor = GameTimeDayNightRules.FixtureFactor(nightFactor);
            foreach (var source in coveredPassageLights) if (source != null) source.RestoreSteady(fixtureFactor);
            for (int slot = 0; slot < streetLightPool.Length; slot++)
            {
                if (IsActivePracticalPoolIndex(slot)) continue;
                int task = selectedAnchorIndices[slot] - industrialAnchorStartIndex;
                if (task >= 0 && task < industrialIsCoveredPassage.Length && industrialIsCoveredPassage[task] && streetLightPool[slot] != null)
                    streetLightPool[slot].intensity = pooledLightBaseIntensities[slot] * fixtureFactor;
            }
        }

        /// <summary>
        /// Applies the faulty tunnel fixture's visible power loss to the
        /// already-owned practical slot. Invalid input recovers to steady
        /// power and finite input is clamped, so a presentation controller
        /// cannot create a negative or over-budget light state.
        /// </summary>
        public void SetTunnelPracticalFlickerMultiplier(float multiplier)
        {
            float safeMultiplier =
                float.IsNaN(multiplier) || float.IsInfinity(multiplier)
                    ? 1f
                    : Mathf.Clamp(
                        multiplier,
                        MinimumTunnelFlickerMultiplier,
                        1f);
            if (Mathf.Approximately(
                    tunnelPracticalFlickerMultiplier,
                    safeMultiplier))
            {
                return;
            }

            tunnelPracticalFlickerMultiplier = safeMultiplier;
            ApplyActivePracticalIntensity();
        }

        private void BindFixedFixtures()
        {
            for (int index = 0; index < lampAnchors.Length; index++)
            {
                selectedAnchorIndices[index] = index;
                bool industrial = index >= industrialAnchorStartIndex;
                bool quay = !industrial && index >= quayAnchorStartIndex;
                if (industrial)
                {
                    int task = index - industrialAnchorStartIndex;
                    ApplyIndustrialProfile(index, industrialPurposes[task], industrialUseShadows[task],
                        industrialIntensityScales[task], industrialRangeLimits[task],
                        industrialSpotAngles[task], industrialInnerSpotAngles[task], industrialStreetOnly[task]);
                    string anchorName = lampAnchors[index].name;
                    if (glassReturnWorkLights[task] != null)
                        glassReturnWorkLights[task].BindLight(streetLightPool[index], pooledLightBaseIntensities[index], this);
                    if (anchorName == IndustrialTaskLightPrefix + "GlassReturnFloodlight")
                    {
                        // PCF softens the edge; the opaque kiosk must still
                        // stop the full beam when its interior circuit is off.
                        streetLightPool[index].shadowStrength = 1f;
                        streetLightPool[index].cookie = CityGlassReturnWorld.FloodlightCookie(
                            lampAnchors[index], streetLightPool[index].spotAngle);
                    }
                    if (anchorName == CityResidentialEntranceLighting.LightAnchorName ||
                        anchorName == "IndustrialTaskLight_CourtyardEntrance")
                    {
                        // The frosted bulb illuminates the wall beside/above
                        // the door and the underside of the balcony as well as
                        // the apron. Its real housing and building cast shadows.
                        streetLightPool[index].type = LightType.Point;
                        streetLightPool[index].shadowStrength = 1f;
                        residentialEntranceLights.Add(streetLightPool[index]);
                    }
                }
                else if (quay) ApplyQuayLampProfile(index);
                else ApplyStreetProfile(index, lampAnchors[index].forward.y < -.05f);
                Transform anchor = lampAnchors[index];
                streetLightPool[index].transform.SetPositionAndRotation(anchor.position,
                    industrial || quay ? anchor.rotation : CreateStreetLightRotation(anchor));
            }
            for (int index = 0; index < practicalAnchors.Length; index++)
            {
                int slot = practicalLightStart + index;
                pooledHaloVisible[slot] = true;
                AssignPracticalLight(slot, practicalAnchors[index]);
            }
            for (int index = 0; index < busStopPoles.Length; index++)
                for (int face = 0; face < 2; face++)
                    AssignPlateLight(plateLightStart + index * 2 + face,
                        busStopPoles[index], face == 0 ? 1 : -1, 1f);
            for (int index = 0; index < busStopFixtures.Length; index++)
                for (int source = 0; source < CityBusStopLighting.LightCount; source++)
                {
                    int slot = stopLightStart + index * CityBusStopLighting.LightCount + source;
                    CityBusStopLighting fixture = busStopFixtures[index];
                    fixture.BindLight(source, streetLightPool[slot]);
                    busStopLightOwners.Add(streetLightPool[slot], (fixture, source));
                    pooledLightBaseIntensities[slot] = fixture.NightIntensity(source);
                }
            fixedBindingsReady = true;
        }

        private void RefreshStreetLights(bool force)
        {
            if (!fixedBindingsReady) return;
            float now = Time.unscaledTime;
            if (!force && now < nextAvailabilityRefresh) return;
            nextAvailabilityRefresh = now + AvailabilityRefreshInterval;
            // Compatibility diagnostics may describe the nearest practical;
            // they never control a fixture's binding, position or power.
            activePracticalIndex = player != null ? FindNearestPractical(player.position, true) : -1;
            IsPracticalSlotLeased = activePracticalIndex >= 0;
            AssignedStreetLightCount = 0;
            for (int index = 0; index < streetLightPool.Length; index++)
            {
                Light light = streetLightPool[index];
                float factor = GetPooledIntensityFactor(index);
                bool available = index < lampAnchors.Length ?
                    lampAnchors[index] != null && IsAnchorAvailable(index) :
                    index < plateLightStart ? practicalAnchors[index - practicalLightStart].Anchor != null :
                    index < stopLightStart ? busStopPoles[(index - plateLightStart) / 2] != null :
                    busStopFixtures[(index - stopLightStart) / CityBusStopLighting.LightCount] != null &&
                    busStopFixtures[(index - stopLightStart) / CityBusStopLighting.LightCount].isActiveAndEnabled;
                light.intensity = pooledLightBaseIntensities[index] * factor;
                light.enabled = isActiveAndEnabled && available && factor > VisibleFactorThreshold;
                CityLightHalo halo = streetLightHalos[index];
                if (halo != null)
                {
                    halo.SetIntensityFactor(factor);
                    halo.SetVisible(light.enabled && pooledHaloVisible[index]);
                }
                if (light.enabled && !IsActivePracticalPoolIndex(index)) AssignedStreetLightCount++;
            }
        }
        private void AssignPlateLight(int slot, Transform pole, int side, float strength)
        {
            // Each board keeps two tiny grazing beams, one per face. Their
            // short radius affects the plate, without borrowing a street Light.
            Vector3 face = pole.TransformPoint(new Vector3(0f, 2.18f, .101f * side));
            Vector3 source = face + pole.up * .31f + pole.forward * (.22f * side);
            ApplyPooledLightProfile(slot, PlateLampColor, .16f * strength, .95f, 124f, 82f,
                .1f, .2f, Color.clear, Color.clear);
            var light = streetLightPool[slot];
            light.transform.SetPositionAndRotation(source, Quaternion.LookRotation(face - source, pole.up));
            light.enabled = true;
            pooledHaloVisible[slot] = false;
            streetLightHalos[slot]?.SetVisible(false);
        }
        private int FindNearestPractical(
            Vector3 playerPosition,
            bool includeNightOnly)
        {
            int nearestIndex = -1;
            float nearestDistance =
                PracticalLeaseDistance * PracticalLeaseDistance;
            for (int index = 0; index < practicalAnchors.Length; index++)
            {
                if (!includeNightOnly &&
                    practicalAnchors[index].Kind !=
                    CityFringeYardKind.SouthTunnelForecourt)
                {
                    continue;
                }

                Transform anchor = practicalAnchors[index].Anchor;
                if (anchor == null)
                {
                    continue;
                }

                float distance =
                    (anchor.position - playerPosition).sqrMagnitude;
                if (distance > nearestDistance ||
                    (nearestIndex >= 0 &&
                     distance >= nearestDistance))
                {
                    continue;
                }

                nearestIndex = index;
                nearestDistance = distance;
            }

            return nearestIndex;
        }

        private void AssignPracticalLight(
            int poolIndex,
            CityFringePracticalAnchor practical)
        {
            PracticalLightProfile profile = GetPracticalProfile(
                practical.Kind);
            Light light = streetLightPool[poolIndex];
            CityLightHalo halo = streetLightHalos[poolIndex];
            ApplyPooledLightProfile(
                poolIndex,
                profile.Color,
                profile.Intensity,
                profile.Range,
                profile.SpotAngle,
                profile.InnerSpotAngle,
                profile.HaloInnerSize,
                profile.HaloOuterSize,
                profile.HaloInnerColor,
                profile.HaloOuterColor);
            light.transform.SetPositionAndRotation(
                practical.Anchor.position,
                practical.Anchor.rotation);
            float intensityFactor = GetPracticalIntensityFactor(
                practical.Kind);
            light.intensity =
                pooledLightBaseIntensities[poolIndex] * intensityFactor;
            halo.SetIntensityFactor(intensityFactor);
            bool visible = intensityFactor > VisibleFactorThreshold;
            light.enabled = visible;
            halo.SetVisible(visible);
        }

        private void ApplyIndustrialProfile(int poolIndex, CityIndustrialPurpose purpose, bool useShadows,
            float intensityScale, float rangeLimit, float spotAngle, float innerSpotAngle, bool streetOnly)
        {
            PracticalLightProfile profile = GetIndustrialProfile(purpose);
            // An authored kiosk beam owns its full range. The legacy workshop
            // profile's seven-metre ceiling must not truncate the street spill.
            float range = spotAngle > 0f ? rangeLimit : Mathf.Min(profile.Range, rangeLimit);
            ApplyPooledLightProfile(poolIndex, profile.Color, profile.Intensity * intensityScale,
                range,
                spotAngle > 0f ? spotAngle : useShadows ? 165f : profile.SpotAngle,
                spotAngle > 0f ? innerSpotAngle : useShadows ? 20f : profile.InnerSpotAngle,
                profile.HaloInnerSize, profile.HaloOuterSize,
                profile.HaloInnerColor * intensityScale, profile.HaloOuterColor * intensityScale);
            if (!useShadows) return;
            Light light = streetLightPool[poolIndex];
            light.shadows = LightShadows.Soft;
            light.shadowStrength = spotAngle > 0f ? 1f : .85f;
            light.shadowBias = .015f;
            light.shadowNormalBias = .06f;
            light.shadowNearPlane = .05f;
            UniversalAdditionalLightData data = pooledShadowData[poolIndex];
            if (data == null)
                pooledShadowData[poolIndex] = data = light.GetUniversalAdditionalLightData();
            data.usePipelineSettings = false;
            if (streetOnly)
            {
                data.renderingLayers = RidgeKioskStreetLightReceivers.StreetLayer;
                // People and the opaque shell still shadow the pavement.
                data.customShadowLayers = true;
                data.shadowRenderingLayers = 1u;
            }
            // The pinned PC asset's Low tier is 256. URP rejects its public
            // tier setter outside Play mode; edit-time composition retains
            // valid geometry and shadows with the pipeline's default tier.
            if (Application.isPlaying)
                data.additionalLightsShadowResolutionTier =
                    UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
        }

        internal static Color GetIndustrialTaskLightColor(CityIndustrialPurpose purpose) =>
            GetIndustrialProfile(purpose).Color;

        internal bool IsResidentialEntranceLight(Light light) =>
            light != null && residentialEntranceLights.Contains(light);

        // Native frosted laundry lenses emit a broad downward hemisphere.
        // Their fixed sources illuminate surfaces without a separate air beam.
        internal bool IsLaundryDiffuser(Light light)
        {
            for (int i = 0; i < streetLightPool.Length; i++)
            {
                if (streetLightPool[i] != light) continue;
                if (IsActivePracticalPoolIndex(i)) return false;
                int task = selectedAnchorIndices[i] - industrialAnchorStartIndex;
                return task >= 0 && task < industrialUseShadows.Length && industrialUseShadows[task];
            }
            return false;
        }

        private static PracticalLightProfile GetIndustrialProfile(CityIndustrialPurpose purpose)
        {
            bool neutral = purpose == CityIndustrialPurpose.ColdStorage ||
                purpose == CityIndustrialPurpose.MetalWorkshop || purpose == CityIndustrialPurpose.CompressorHouse;
            Color color = neutral ? new Color(.85f, .93f, .88f) : new Color(1f, .77f, .48f);
            float intensity, range, angle, inner;
            switch (purpose)
            {
                case CityIndustrialPurpose.DryWarehouse: intensity = 24f; range = 12f; angle = 105f; inner = 60f; break;
                case CityIndustrialPurpose.ColdStorage: intensity = 16f; range = 8f; angle = 58f; inner = 32f; break;
                case CityIndustrialPurpose.RepairWorkshop: intensity = 12f; range = 7f; angle = 78f; inner = 44f; break;
                case CityIndustrialPurpose.MetalWorkshop: intensity = 20f; range = 10f; angle = 88f; inner = 45f; break;
                case CityIndustrialPurpose.PackagingWorkshop: intensity = 14f; range = 8f; angle = 92f; inner = 48f; break;
                case CityIndustrialPurpose.BoilerHouse: intensity = 23f; range = 11f; angle = 72f; inner = 38f; break;
                case CityIndustrialPurpose.CompressorHouse: intensity = 15f; range = 8f; angle = 64f; inner = 36f; break;
                case CityIndustrialPurpose.RepairGarage: intensity = 22f; range = 11f; angle = 110f; inner = 62f; break;
                default: throw new ArgumentOutOfRangeException(nameof(purpose));
            }
            return new PracticalLightProfile(color, intensity, range, angle, inner, .42f, 1.15f,
                new Color(color.r * 3.2f, color.g * 2.8f, color.b * 2.4f, .12f),
                new Color(color.r * 1.8f, color.g * 1.5f, color.b * 1.3f, .028f));
        }

        private static Transform[] FindIndustrialTaskLights(Transform sourceRoot)
        {
            if (sourceRoot == null) return Array.Empty<Transform>();
            var result = new List<Transform>();
            // One construction-time scan; fixed bindings retain these anchors.
            foreach (Transform candidate in sourceRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!candidate.name.StartsWith(IndustrialTaskLightPrefix, StringComparison.Ordinal)) continue;
                if (result.Count == MaximumIndustrialTaskLights)
                    throw new InvalidOperationException("Industrial task-light anchor catalog exceeds its fixed budget.");
                result.Add(candidate);
            }
            return result.ToArray();
        }

        private void ApplyStreetProfile(int poolIndex, bool aimedAtSurface = false)
        {
            ApplyPooledLightProfile(
                poolIndex,
                StreetLightColor,
                StreetLightIntensity,
                aimedAtSurface ? HillsideLightRange : StreetLightRange,
                aimedAtSurface ? 165f : 105f,
                aimedAtSurface ? 95f : 55f,
                1.15f,
                3.10f,
                StreetHaloInner,
                StreetHaloOuter);
        }

        private void ApplyQuayLampProfile(int poolIndex)
        {
            ApplyPooledLightProfile(
                poolIndex,
                StreetLightColor,
                QuayLampIntensity,
                QuayLampRange,
                QuayLampSpotAngle,
                QuayLampInnerSpotAngle,
                QuayLampHaloInnerSize,
                QuayLampHaloOuterSize,
                StreetHaloInner,
                StreetHaloOuter);
        }

        private void ApplyPooledLightProfile(
            int poolIndex,
            Color color,
            float intensity,
            float range,
            float spotAngle,
            float innerSpotAngle,
            float innerHaloSize,
            float outerHaloSize,
            Color innerHaloColor,
            Color outerHaloColor)
        {
            Light light = streetLightPool[poolIndex];
            light.type = LightType.Spot;
            light.color = color;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
            light.shadows = LightShadows.None;
            if (pooledShadowData[poolIndex] != null)
            {
                var data = pooledShadowData[poolIndex];
                data.usePipelineSettings = true;
                data.customShadowLayers = false;
                data.renderingLayers = 1u;
                data.shadowRenderingLayers = 1u;
            }
            pooledLightBaseIntensities[poolIndex] = intensity;
            light.intensity =
                intensity * GetPooledIntensityFactor(poolIndex);
            streetLightHalos[poolIndex]?.SetAppearance(
                innerHaloSize,
                outerHaloSize,
                innerHaloColor,
                outerHaloColor);
        }

        private void ApplyActivePracticalIntensity()
        {
            // Every fringe source exists at any distance. The tunnel fault
            // always addresses its own source, never a nearest leased slot.
            for (int index = 0; index < practicalAnchors.Length; index++)
            {
                if (practicalAnchors[index].Kind != CityFringeYardKind.SouthTunnelForecourt) continue;
                int slot = practicalLightStart + index;
                float factor = GetPracticalIntensityFactor(practicalAnchors[index].Kind);
                Light light = streetLightPool[slot];
                light.intensity = pooledLightBaseIntensities[slot] * factor;
                light.enabled = isActiveAndEnabled && practicalAnchors[index].Anchor != null && factor > VisibleFactorThreshold;
                CityLightHalo halo = streetLightHalos[slot];
                if (halo != null)
                {
                    halo.SetIntensityFactor(factor);
                    halo.SetVisible(light.enabled);
                }
            }
        }

        private float GetPooledIntensityFactor(int poolIndex)
        {
            if (!IsActivePracticalPoolIndex(poolIndex))
            {
                float fixtureFactor = GameTimeDayNightRules.FixtureFactor(nightFactor);
                int task = selectedAnchorIndices[poolIndex] - industrialAnchorStartIndex;
                if (task >= 0 && task < glassReturnWorkLights.Length && glassReturnWorkLights[task] != null)
                    return fixtureFactor * glassReturnWorkLights[task].PowerMultiplier;
                if (task >= 0 && task < industrialIsCoveredPassage.Length && industrialIsCoveredPassage[task])
                {
                    var source = coveredPassageLights[task];
                    if (source == null || !source.gameObject.activeInHierarchy) return 0f;
                    return fixtureFactor * (source.isActiveAndEnabled ? source.PowerMultiplier : 1f);
                }
                return fixtureFactor;
            }

            return GetPracticalIntensityFactor(
                practicalAnchors[poolIndex - practicalLightStart].Kind);
        }

        private bool IsAnchorAvailable(int anchorIndex)
        {
            int task = anchorIndex - industrialAnchorStartIndex;
            if (task < 0 || task >= industrialIsCoveredPassage.Length || !industrialIsCoveredPassage[task]) return true;
            var source = coveredPassageLights[task];
            return source != null && source.gameObject.activeInHierarchy;
        }

        private float GetPracticalIntensityFactor(
            CityFringeYardKind kind)
        {
            // The §20 floor for every yard practical. The faulty tunnel
            // fixture used to carry its own private day floor of 0.22; the
            // law's two-thirds subsumes it, and what stays the fixture's
            // own is the FLICKER - a fault is character, not a schedule.
            float poweredFactor =
                GameTimeDayNightRules.FixtureFactor(nightFactor);
            if (kind != CityFringeYardKind.SouthTunnelForecourt)
            {
                return poweredFactor;
            }

            return poweredFactor * tunnelPracticalFlickerMultiplier;
        }

        private bool IsActivePracticalPoolIndex(int poolIndex) =>
            poolIndex >= practicalLightStart && poolIndex < plateLightStart;

        private static PracticalLightProfile GetPracticalProfile(
            CityFringeYardKind kind)
        {
            switch (kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
                case CityFringeYardKind.EastUtilityEdge:
                    return new PracticalLightProfile(
                        new Color(1f, 0.63f, 0.32f),
                        18f,
                        8f,
                        65f,
                        38f,
                        0.82f,
                        2.10f,
                        new Color(3.6f, 1.65f, 0.35f, 0.16f),
                        new Color(1.9f, 1.0f, 0.30f, 0.04f));
                case CityFringeYardKind.WestIndustrialBelt:
                    return new PracticalLightProfile(
                        new Color(0.72f, 1f, 0.80f),
                        21f,
                        9f,
                        72f,
                        42f,
                        0.90f,
                        2.30f,
                        new Color(1.55f, 3.65f, 1.95f, 0.17f),
                        new Color(0.72f, 1.95f, 1.02f, 0.045f));
                case CityFringeYardKind.SouthTunnelForecourt:
                    return new PracticalLightProfile(
                        new Color(1f, 0.56f, 0.28f),
                        150f,
                        16f,
                        72f,
                        40f,
                        1.15f,
                        3.10f,
                        new Color(4.0f, 1.55f, 0.34f, 0.18f),
                        new Color(2.15f, 0.92f, 0.30f, 0.05f));
                case CityFringeYardKind.SouthFloodWorks:
                    return new PracticalLightProfile(
                        new Color(0.66f, 1f, 0.72f),
                        20f,
                        9f,
                        70f,
                        40f,
                        0.88f,
                        2.25f,
                        new Color(1.6f, 3.6f, 1.8f, 0.16f),
                        new Color(0.72f, 1.9f, 0.92f, 0.04f));
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "The fringe kind has no practical-light profile.");
            }
        }

        private Light CreateLight(
            string lightName,
            Vector3 position,
            Color color,
            LightType lightType,
            float intensity,
            float range,
            float innerHaloSize,
            float outerHaloSize,
            Color innerHaloColor,
            Color outerHaloColor,
            out CityLightHalo halo,
            bool createHalo = true)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, true);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = lightType;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.15f;

            if (lightType == LightType.Spot)
            {
                light.spotAngle = 105f;
                light.innerSpotAngle = 55f;
            }

            if (!createHalo) { halo = null; return light; }
            GameObject haloObject = new GameObject("Fog Light Halo");
            haloObject.transform.SetParent(lightObject.transform, false);
            halo = haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                innerHaloSize,
                outerHaloSize,
                innerHaloColor,
                outerHaloColor);
            return light;
        }

        private static Quaternion CreateStreetLightRotation(Transform anchor)
        {
            // Hillside poles retain their physical lens and an authored beam
            // towards the actual stair/deck datum. Ordinary streets keep the
            // original downward profile.
            if (anchor.forward.y < -.05f) return anchor.rotation;
            Vector3 roadDirection = Vector3.ProjectOnPlane(
                anchor.forward,
                Vector3.up).normalized;
            if (roadDirection.sqrMagnitude < 0.5f)
            {
                roadDirection = Vector3.forward;
            }

            Vector3 lightDirection =
                (Vector3.down * 0.92f + roadDirection * 0.38f).normalized;
            return Quaternion.LookRotation(lightDirection, Vector3.up);
        }

        private static Transform[] CopyAnchors(
            IReadOnlyList<Transform> anchors,
            IReadOnlyList<Transform> quayAnchors,
            IReadOnlyList<Transform> industrialAnchors)
        {
            int quayCount = quayAnchors?.Count ?? 0;
            var result = new Transform[anchors.Count + quayCount + industrialAnchors.Count];
            for (int index = 0; index < anchors.Count; index++)
            {
                result[index] = anchors[index] != null
                    ? anchors[index]
                    : throw new ArgumentException(
                        "Street lamp anchors cannot contain null.",
                        nameof(anchors));
            }

            for (int index = 0; index < quayCount; index++)
            {
                result[anchors.Count + index] = quayAnchors[index] != null
                    ? quayAnchors[index]
                    : throw new ArgumentException(
                        "Quay lamp anchors cannot contain null.",
                        nameof(quayAnchors));
            }

            for (int index = 0; index < industrialAnchors.Count; index++)
                result[anchors.Count + quayCount + index] = industrialAnchors[index];
            return result;
        }

        private static CityFringePracticalAnchor[] CopyPracticalAnchors(
            IReadOnlyList<CityFringePracticalAnchor> anchors)
        {
            if (anchors == null || anchors.Count == 0)
            {
                return Array.Empty<CityFringePracticalAnchor>();
            }

            var result = new List<CityFringePracticalAnchor>(
                anchors.Count);
            for (int index = 0; index < anchors.Count; index++)
            {
                CityFringePracticalAnchor candidate = anchors[index];
                if (!IsSupportedPracticalKind(candidate.Kind))
                {
                    continue;
                }

                if (candidate.Anchor == null)
                {
                    throw new ArgumentException(
                        "Supported fringe practicals cannot contain a " +
                        "null anchor.",
                        nameof(anchors));
                }

                result.Add(candidate);
            }

            return result.ToArray();
        }

        private static bool IsSupportedPracticalKind(
            CityFringeYardKind kind)
        {
            return kind == CityFringeYardKind.WestStoneTerraces ||
                   kind == CityFringeYardKind.WestIndustrialBelt ||
                   kind == CityFringeYardKind.SouthTunnelForecourt ||
                   kind == CityFringeYardKind.SouthFloodWorks ||
                   kind == CityFringeYardKind.EastUtilityEdge;
        }

        private readonly struct PracticalLightProfile
        {
            public PracticalLightProfile(
                Color color,
                float intensity,
                float range,
                float spotAngle,
                float innerSpotAngle,
                float haloInnerSize,
                float haloOuterSize,
                Color haloInnerColor,
                Color haloOuterColor)
            {
                Color = color;
                Intensity = intensity;
                Range = range;
                SpotAngle = spotAngle;
                InnerSpotAngle = innerSpotAngle;
                HaloInnerSize = haloInnerSize;
                HaloOuterSize = haloOuterSize;
                HaloInnerColor = haloInnerColor;
                HaloOuterColor = haloOuterColor;
            }

            public Color Color { get; }
            public float Intensity { get; }
            public float Range { get; }
            public float SpotAngle { get; }
            public float InnerSpotAngle { get; }
            public float HaloInnerSize { get; }
            public float HaloOuterSize { get; }
            public Color HaloInnerColor { get; }
            public Color HaloOuterColor { get; }
        }
    }
}
