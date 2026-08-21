using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityNightAtmosphere : MonoBehaviour
    {
        public const int MaximumRealtimeLights = 12;

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
        private int activePracticalIndex = -1;
        private float nextReassignmentTime;
        private Vector3 lastAssignmentPosition;
        private float nightFactor = 1f;
        private bool hasAppliedNightFactor;

        public IReadOnlyList<Transform> LampAnchors => lampAnchors;
        public IReadOnlyList<Light> StreetLightPool => streetLightPool;
        public IReadOnlyList<Light> BarLights => barLights;
        public float NightFactor => nightFactor;
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
                fringePracticalAnchors = null)
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

            lampAnchors = CopyAnchors(streetLampAnchors);
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
            for (int index = 0; index < barLights.Length; index++)
            {
                barLights[index].intensity =
                    BarLightIntensity * nightFactor;
                barLights[index].enabled = visible;
                barLightHalos[index].SetIntensityFactor(nightFactor);
            }

            for (int index = 0; index < streetLightPool.Length; index++)
            {
                streetLightPool[index].intensity =
                    pooledLightBaseIntensities[index] * nightFactor;
                if (!visible)
                {
                    streetLightPool[index].enabled = false;
                }

                streetLightHalos[index].SetIntensityFactor(nightFactor);
                streetLightHalos[index].SetVisible(
                    visible && streetLightPool[index].enabled);
            }

            if (visible &&
                isActiveAndEnabled &&
                (force || firstApplication || !wasVisible))
            {
                RefreshStreetLights(true);
            }
        }

        private void RefreshStreetLights(bool force)
        {
            if (nightFactor <= VisibleFactorThreshold ||
                player == null ||
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

            activePracticalIndex = FindNearestPractical(
                playerPosition);
            IsPracticalSlotLeased =
                activePracticalIndex >= 0 &&
                streetLightPool.Length > 0;
            int streetSlotCount = streetLightPool.Length -
                                  (IsPracticalSlotLeased ? 1 : 0);

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

            AssignedStreetLightCount = 0;
            for (int index = 0; index < streetLightPool.Length; index++)
            {
                if (IsPracticalSlotLeased &&
                    index == streetLightPool.Length - 1)
                {
                    AssignPracticalLight(
                        index,
                        practicalAnchors[activePracticalIndex]);
                    continue;
                }

                ApplyStreetProfile(index);
                int anchorIndex = selectedAnchorIndices[index];
                Light light = streetLightPool[index];
                bool hasAnchor =
                    anchorIndex >= 0 &&
                    anchorIndex < lampAnchors.Length &&
                    lampAnchors[anchorIndex] != null;
                bool visible =
                    hasAnchor &&
                    nightFactor > VisibleFactorThreshold;
                light.enabled = visible;
                streetLightHalos[index].SetVisible(visible);
                if (hasAnchor)
                {
                    AssignedStreetLightCount++;
                    Transform anchor = lampAnchors[anchorIndex];
                    light.transform.SetPositionAndRotation(
                        anchor.position,
                        CreateStreetLightRotation(anchor));
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

        private int FindNearestPractical(Vector3 playerPosition)
        {
            int nearestIndex = -1;
            float nearestDistance =
                PracticalLeaseDistance * PracticalLeaseDistance;
            for (int index = 0; index < practicalAnchors.Length; index++)
            {
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
            bool visible = nightFactor > VisibleFactorThreshold;
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
            light.intensity = intensity * nightFactor;
            streetLightHalos[poolIndex].SetAppearance(
                innerHaloSize,
                outerHaloSize,
                innerHaloColor,
                outerHaloColor);
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
                        24f,
                        10f,
                        80f,
                        48f,
                        1.0f,
                        2.55f,
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
            IReadOnlyList<Transform> anchors)
        {
            var result = new Transform[anchors.Count];
            for (int index = 0; index < anchors.Count; index++)
            {
                result[index] = anchors[index] != null
                    ? anchors[index]
                    : throw new ArgumentException(
                        "Street lamp anchors cannot contain null.",
                        nameof(anchors));
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
