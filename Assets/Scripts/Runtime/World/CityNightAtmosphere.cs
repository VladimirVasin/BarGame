using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityNightAtmosphere : MonoBehaviour
    {
        public const int MaximumRealtimeLights = 12;
        public const float MinimumTunnelFlickerMultiplier = 0.05f;

        private const float ReassignmentInterval = 0.35f;
        private const float ReassignmentDistance = 1.25f;
        internal const float PracticalLeaseDistance = 20f;

        // The mast grew from 2.92 m to 4.70 m of source height; the
        // inverse-square law wants (4.70/2.92)^2 = 2.6x the luminous
        // power for the same pavement below, and the pool reaches
        // correspondingly further.
        private const float StreetLightIntensity = 31f;
        private const float StreetLightRange = 16.5f;
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

        private Transform player;
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
        private float[] selectedAnchorDistances = Array.Empty<float>();
        private float[] pooledLightBaseIntensities = Array.Empty<float>();
        // Whether a pool slot may show its travelling halo. Only the
        // leased practical does: every fixed lamp carries its own
        // fixture halo now, and a pooled spot arriving at a mast or
        // a quay lantern must not double the blob. The quay wall halo
        // is independently always lit; most fixtures remain night-gated.
        private bool[] pooledHaloVisible = Array.Empty<bool>();
        private int activePracticalIndex = -1;
        private int quayAnchorStartIndex = int.MaxValue;
        private float nextReassignmentTime;
        private Vector3 lastAssignmentPosition;
        private float nightFactor = 1f;
        private float tunnelPracticalFlickerMultiplier = 1f;
        private bool hasAppliedNightFactor;

        public IReadOnlyList<Transform> LampAnchors => lampAnchors;
        public IReadOnlyList<Light> StreetLightPool => streetLightPool;
        public IReadOnlyList<Light> BarLights => barLights;
        public float NightFactor => nightFactor;
        public float TunnelPracticalFlickerMultiplier =>
            tunnelPracticalFlickerMultiplier;
        public int ReassignmentCount { get; private set; }
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
            IsPracticalSlotLeased && streetLightPool.Length > 0
                ? streetLightPool[streetLightPool.Length - 1]
                : null;
        internal int AssignedStreetLightCount { get; private set; }

        public void Initialize(
            Transform playerTransform,
            IReadOnlyList<Transform> streetLampAnchors,
            IReadOnlyList<Vector3> barLightPositions,
            IReadOnlyList<CityFringePracticalAnchor>
                fringePracticalAnchors = null,
            IReadOnlyList<Transform> quayLampAnchors = null)
        {
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

            // The quay lanterns ride in the same nearest-first pool as
            // the street masts, distinguished by index alone: anything
            // at or past the boundary takes the low waterside profile
            // and its anchor's own authored aim.
            lampAnchors = CopyAnchors(streetLampAnchors, quayLampAnchors);
            quayAnchorStartIndex = streetLampAnchors.Count;
            practicalAnchors = CopyPracticalAnchors(
                fringePracticalAnchors);
            int barLightCount = Mathf.Min(
                barLightPositions.Count,
                MaximumRealtimeLights);
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

            int streetLightCount = Mathf.Min(
                lampAnchors.Length,
                MaximumRealtimeLights - barLightCount);
            streetLightPool = new Light[streetLightCount];
            streetLightHalos = new CityLightHalo[streetLightCount];
            selectedAnchorIndices = new int[streetLightCount];
            selectedAnchorDistances = new float[streetLightCount];
            pooledLightBaseIntensities = new float[streetLightCount];
            pooledHaloVisible = new bool[streetLightCount];
            for (int index = 0; index < streetLightCount; index++)
            {
                pooledLightBaseIntensities[index] = StreetLightIntensity;
                streetLightPool[index] = CreateLight(
                    $"Pooled Street Light {index + 1}",
                    transform.position,
                    StreetLightColor,
                    LightType.Spot,
                    StreetLightIntensity,
                    StreetLightRange,
                    1.15f,
                    3.10f,
                    StreetHaloInner,
                    StreetHaloOuter,
                    out streetLightHalos[index]);
            }

            SetNightFactor(nightFactor);
        }

        private void Update()
        {
            RefreshStreetLights(false);
        }

        public void RefreshImmediate()
        {
            RefreshStreetLights(true);
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

            // Every realtime fixture below rides the §20 floor rather than
            // the raw factor: the law says a fixture gives at least two
            // thirds of its night strength at noon and its fog halo is
            // never taken away. The raw factor keeps driving what really
            // belongs to the hour - the refresh cadence, the pool scan.
            float fixtureFactor =
                GameTimeDayNightRules.FixtureFactor(nightFactor);
            for (int index = 0; index < barLights.Length; index++)
            {
                barLights[index].intensity =
                    BarLightIntensity * fixtureFactor;
                barLights[index].enabled = true;
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
                    streetLightPool[index].enabled = true;
                }

                streetLightHalos[index].SetIntensityFactor(
                    intensityFactor);
                streetLightHalos[index].SetVisible(
                    intensityFactor > VisibleFactorThreshold &&
                    streetLightPool[index].enabled &&
                    pooledHaloVisible[index]);
            }

            if (isActiveAndEnabled &&
                (force || firstApplication || wasVisible != visible))
            {
                RefreshStreetLights(true);
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

        private void RefreshStreetLights(bool force)
        {
            if (player == null ||
                streetLightPool.Length == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            Vector3 playerPosition = player.position;
            float movementSquared =
                (playerPosition - lastAssignmentPosition).sqrMagnitude;
            if (!force &&
                now < nextReassignmentTime &&
                movementSquared <
                ReassignmentDistance * ReassignmentDistance)
            {
                return;
            }

            for (int index = 0; index < selectedAnchorIndices.Length; index++)
            {
                selectedAnchorIndices[index] = -1;
                selectedAnchorDistances[index] = float.PositiveInfinity;
            }

            // The pool leases by the FIXTURE factor, not the sky's. The
            // old gate skipped the anchor scan at a zero night factor -
            // a fair optimisation while lamps died at dawn, and repealed
            // with them: under §20 the nearest lamps spill realtime light
            // at noon exactly as they do at midnight, two thirds as hard.
            bool nightVisible =
                GameTimeDayNightRules.FixtureFactor(nightFactor) >
                VisibleFactorThreshold;
            activePracticalIndex = FindNearestPractical(
                playerPosition,
                nightVisible);
            IsPracticalSlotLeased =
                activePracticalIndex >= 0 &&
                streetLightPool.Length > 0;
            int streetSlotCount = streetLightPool.Length -
                                  (IsPracticalSlotLeased ? 1 : 0);

            if (nightVisible)
            {
                for (int anchorIndex = 0;
                     anchorIndex < lampAnchors.Length;
                     anchorIndex++)
                {
                    Transform anchor = lampAnchors[anchorIndex];
                    float distance = anchor != null
                        ? (anchor.position - playerPosition).sqrMagnitude
                        : float.PositiveInfinity;
                    InsertNearest(
                        anchorIndex,
                        distance,
                        streetSlotCount);
                }
            }

            AssignedStreetLightCount = 0;
            for (int index = 0; index < streetLightPool.Length; index++)
            {
                if (IsPracticalSlotLeased &&
                    index == streetLightPool.Length - 1)
                {
                    pooledHaloVisible[index] = true;
                    AssignPracticalLight(
                        index,
                        practicalAnchors[activePracticalIndex]);
                    continue;
                }

                int anchorIndex = selectedAnchorIndices[index];
                bool isQuay = anchorIndex >= quayAnchorStartIndex;
                if (isQuay)
                {
                    ApplyQuayLampProfile(index);
                }
                else
                {
                    ApplyStreetProfile(index);
                }

                Light light = streetLightPool[index];
                bool hasAnchor =
                    anchorIndex >= 0 &&
                    anchorIndex < lampAnchors.Length &&
                    lampAnchors[anchorIndex] != null;
                bool visible =
                    hasAnchor &&
                    nightVisible;
                light.enabled = visible;
                // The fixture's own always-on halo carries the blur;
                // the pooled spot brings light alone.
                pooledHaloVisible[index] = false;
                streetLightHalos[index].SetVisible(false);
                if (hasAnchor)
                {
                    AssignedStreetLightCount++;
                    Transform anchor = lampAnchors[anchorIndex];
                    // A quay lantern's anchor is authored aim, the
                    // practical's convention; a street anchor only
                    // knows its road and the tilt is derived.
                    light.transform.SetPositionAndRotation(
                        anchor.position,
                        isQuay
                            ? anchor.rotation
                            : CreateStreetLightRotation(anchor));
                }
            }

            lastAssignmentPosition = playerPosition;
            nextReassignmentTime = now + ReassignmentInterval;
            ReassignmentCount++;
        }

        private void InsertNearest(
            int anchorIndex,
            float distance,
            int slotCount)
        {
            for (int slot = 0; slot < slotCount; slot++)
            {
                if (distance >= selectedAnchorDistances[slot])
                {
                    continue;
                }

                for (int move = slotCount - 1;
                     move > slot;
                     move--)
                {
                    selectedAnchorDistances[move] =
                        selectedAnchorDistances[move - 1];
                    selectedAnchorIndices[move] =
                        selectedAnchorIndices[move - 1];
                }

                selectedAnchorDistances[slot] = distance;
                selectedAnchorIndices[slot] = anchorIndex;
                return;
            }
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

        private void ApplyStreetProfile(int poolIndex)
        {
            ApplyPooledLightProfile(
                poolIndex,
                StreetLightColor,
                StreetLightIntensity,
                StreetLightRange,
                105f,
                55f,
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
            pooledLightBaseIntensities[poolIndex] = intensity;
            light.intensity =
                intensity * GetPooledIntensityFactor(poolIndex);
            streetLightHalos[poolIndex].SetAppearance(
                innerHaloSize,
                outerHaloSize,
                innerHaloColor,
                outerHaloColor);
        }

        private void ApplyActivePracticalIntensity()
        {
            if (!IsPracticalSlotLeased ||
                activePracticalIndex < 0 ||
                activePracticalIndex >= practicalAnchors.Length ||
                streetLightPool.Length == 0)
            {
                return;
            }

            CityFringePracticalAnchor practical =
                practicalAnchors[activePracticalIndex];
            if (practical.Kind !=
                CityFringeYardKind.SouthTunnelForecourt)
            {
                return;
            }

            int poolIndex = streetLightPool.Length - 1;
            float intensityFactor = GetPracticalIntensityFactor(
                practical.Kind);
            Light light = streetLightPool[poolIndex];
            CityLightHalo halo = streetLightHalos[poolIndex];
            light.intensity =
                pooledLightBaseIntensities[poolIndex] * intensityFactor;
            light.enabled = intensityFactor > VisibleFactorThreshold;
            halo.SetIntensityFactor(intensityFactor);
            halo.SetVisible(
                light.enabled && pooledHaloVisible[poolIndex]);
        }

        private float GetPooledIntensityFactor(int poolIndex)
        {
            if (!IsActivePracticalPoolIndex(poolIndex))
            {
                return GameTimeDayNightRules.FixtureFactor(nightFactor);
            }

            return GetPracticalIntensityFactor(
                practicalAnchors[activePracticalIndex].Kind);
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

        private bool IsActivePracticalPoolIndex(int poolIndex)
        {
            return IsPracticalSlotLeased &&
                   activePracticalIndex >= 0 &&
                   activePracticalIndex < practicalAnchors.Length &&
                   streetLightPool.Length > 0 &&
                   poolIndex == streetLightPool.Length - 1;
        }

        private static PracticalLightProfile GetPracticalProfile(
            CityFringeYardKind kind)
        {
            switch (kind)
            {
                case CityFringeYardKind.WestStoneTerraces:
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
            out CityLightHalo halo)
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
            IReadOnlyList<Transform> quayAnchors)
        {
            int quayCount = quayAnchors?.Count ?? 0;
            var result = new Transform[anchors.Count + quayCount];
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
                   kind == CityFringeYardKind.SouthFloodWorks;
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
