using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// Fixed exterior sources for the sparse lit-pane mask. Every selected
    /// window keeps its own light; camera movement never reassigns a source.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityWindowLighting : MonoBehaviour
    {
        public const int MaximumLights = 384;
        public const float MaximumRange = 5f;
        public const float MaximumFacadeRange = 1.75f;
        private const float BuildingRefreshSeconds = .35f;
        private const float BaseIntensity = 7f;
        private const float FacadeIntensity = .2f;
        private const float FacadeSourceOffset = .45f;
        private const float SourceInset = .2f;

        private readonly struct Source
        {
            public readonly CityBuildingAssetRegistry Registry;
            public readonly ulong PaneMask;
            public readonly Vector3 Center, Outward, Position;
            public readonly Quaternion Rotation;

            public Source(CityBuildingAssetRegistry registry, int slotId, Vector3 center, Vector3 outward)
            {
                Registry = registry;
                PaneMask = 1UL << slotId;
                Center = center;
                Outward = outward;
                // Direct room spill leaves along the window normal. The
                // separate compact facade wash supplies its local scattered
                // light without turning this into a downward projector.
                Position = center - outward * SourceInset;
                Rotation = Quaternion.LookRotation(outward, Vector3.up);
            }
        }

        private Source[] sources = Array.Empty<Source>();
        private Light[] lights = Array.Empty<Light>();
        private Light[] facadeLights = Array.Empty<Light>();
        private CityBuildingAssetRegistry[] buildings = Array.Empty<CityBuildingAssetRegistry>();
        private Camera viewCamera;
        private Func<bool> exteriorActive;
        private float nextBuildingRefresh, lastFixtureFactor = -1f;
        private bool initialized, renderingActive;

        public IReadOnlyList<Light> Lights => lights;
        public IReadOnlyList<Light> FacadeLights => facadeLights;
        public int CandidateCount => sources.Length;
        public uint StateGeneration { get; private set; }
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null && lights[i].enabled) count++;
                return count;
            }
        }

        public void Initialize(Transform sourceRoot, Camera camera, Func<bool> active)
        {
            if (initialized) throw new InvalidOperationException("Window lighting is already initialized.");
            if (sourceRoot == null) throw new ArgumentNullException(nameof(sourceRoot));
            viewCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            exteriorActive = active ?? throw new ArgumentNullException(nameof(active));
            var configuredSources = new List<Source>(MaximumLights);
            buildings = sourceRoot.GetComponentsInChildren<CityBuildingAssetRegistry>(true);
            foreach (CityBuildingAssetRegistry registry in buildings)
            {
                foreach (CityBuildingWindowSlot slot in registry.WindowSlots)
                {
                    if (((registry.GuaranteedWindowMask | registry.OptionalWindowMask | registry.LitWindowMask) & (1UL << slot.Uv2SlotId)) == 0) continue;
                    if (configuredSources.Count >= MaximumLights)
                        throw new InvalidOperationException("City window source budget exceeds 384.");
                    configuredSources.Add(new Source(registry, slot.Uv2SlotId,
                        registry.transform.TransformPoint(slot.LocalCenter),
                        registry.transform.TransformDirection(LocalOutward(slot.Side)).normalized));
                }
            }

            sources = configuredSources.ToArray();
            lights = new Light[sources.Length];
            facadeLights = new Light[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                Source source = sources[i];
                var lightObject = new GameObject("Window Spill " + i);
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.SetPositionAndRotation(source.Position, source.Rotation);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = CityNightAtmosphere.StreetLampColor;
                light.range = MaximumRange;
                light.spotAngle = 160f;
                light.innerSpotAngle = 120f;
                light.intensity = 0f;
                light.bounceIntensity = 0f;
                light.shadows = LightShadows.Hard;
                light.shadowBias = .015f;
                light.shadowNormalBias = .03f;
                light.shadowNearPlane = .05f;
                light.enabled = false;
                UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
                data.usePipelineSettings = false;
                data.renderingLayers = 1u;
                data.customShadowLayers = true;
                data.shadowRenderingLayers = 1u;
                // URP rejects the public tier setter outside Play mode.
                if (Application.isPlaying)
                    data.additionalLightsShadowResolutionTier =
                        UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                lights[i] = light;
                // The outward room source cannot light its coplanar wall.
                // A weak, compact inward wash represents light scattered by
                // the opening; it shares the pane state and adds no shadow map.
                var facadeObject = new GameObject("Window Facade Spill " + i);
                facadeObject.transform.SetParent(transform, false);
                facadeObject.transform.SetPositionAndRotation(source.Center + source.Outward * FacadeSourceOffset,
                    Quaternion.LookRotation(-source.Outward, Vector3.up));
                Light facade = facadeObject.AddComponent<Light>();
                facade.type = LightType.Spot;
                facade.color = light.color;
                facade.range = MaximumFacadeRange;
                facade.spotAngle = 160f;
                facade.innerSpotAngle = 110f;
                facade.intensity = 0f;
                facade.bounceIntensity = 0f;
                facade.shadows = LightShadows.None;
                facade.enabled = false;
                UniversalAdditionalLightData facadeData = facade.GetUniversalAdditionalLightData();
                facadeData.usePipelineSettings = false;
                facadeData.renderingLayers = 1u;
                facadeLights[i] = facade;
            }
            initialized = true;
            RefreshImmediate();
        }

        /// <summary>Refresh fixed-source visibility and the shared day/night factor.</summary>
        public void RefreshImmediate()
        {
            if (!IsEligible()) { DisableSources(); return; }
            ApplySources();
        }

        /// <summary>The index permanently identifies the same authored window.</summary>
        public bool TryGetSelectedWindow(int lightIndex, out Vector3 center, out Vector3 outward)
        {
            center = outward = Vector3.zero;
            if (!initialized || lightIndex < 0 || lightIndex >= sources.Length) return false;
            center = sources[lightIndex].Center;
            outward = sources[lightIndex].Outward;
            return true;
        }

        private void LateUpdate()
        {
            if (!IsEligible())
            {
                if (renderingActive) DisableSources();
                return;
            }
            if (!renderingActive) { ApplySources(); return; }
            if (GameTimeScaleRuntime.IsPaused) return;
            float factor = GameTimeDayNightRules.FixtureFactor(CityWindowAppearance.NightFactor);
            if (factor != lastFixtureFactor || Time.unscaledTime >= nextBuildingRefresh) ApplySources();
        }

        private bool IsEligible() => initialized && isActiveAndEnabled && viewCamera != null &&
            viewCamera.isActiveAndEnabled && viewCamera.cameraType == CameraType.Game &&
            !viewCamera.TryGetComponent(out ScenePresentationExclusion _) && exteriorActive();

        private void ApplySources()
        {
            double absoluteMinutes = CityBuildingWindowSlotAppearance.AbsoluteMinutes;
            bool stateChanged = false;
            for (int i = 0; i < buildings.Length; i++)
                if (buildings[i] != null)
                {
                    ulong before = buildings[i].LitWindowMask;
                    CityBuildingWindowSlotAppearance.RefreshTimedMask(buildings[i], absoluteMinutes);
                    stateChanged |= before != buildings[i].LitWindowMask;
                }
            if (stateChanged) unchecked { StateGeneration++; }
            float factor = GameTimeDayNightRules.FixtureFactor(CityWindowAppearance.NightFactor);
            float intensity = BaseIntensity * factor;
            bool intensityChanged = factor != lastFixtureFactor;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                if (intensityChanged)
                {
                    light.intensity = intensity;
                    facadeLights[i].intensity = FacadeIntensity * factor;
                }
                CityBuildingAssetRegistry building = sources[i].Registry;
                bool enabledSource = building != null && building.gameObject.activeInHierarchy &&
                    (building.LitWindowMask & sources[i].PaneMask) != 0;
                if (light.enabled != enabledSource) light.enabled = enabledSource;
                if (facadeLights[i].enabled != enabledSource) facadeLights[i].enabled = enabledSource;
            }
            lastFixtureFactor = factor;
            nextBuildingRefresh = Time.unscaledTime + BuildingRefreshSeconds;
            renderingActive = true;
        }

        private void DisableSources()
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].enabled) lights[i].enabled = false;
                if (facadeLights[i] != null && facadeLights[i].enabled) facadeLights[i].enabled = false;
            }
            renderingActive = false;
            nextBuildingRefresh = 0f;
        }

        private static Vector3 LocalOutward(string side)
        {
            switch (side)
            {
                case "Front": return Vector3.forward;
                case "Rear": return Vector3.back;
                case "Left": return Vector3.left;
                case "Right": return Vector3.right;
                default: throw new InvalidOperationException("Unknown window facade side: " + side);
            }
        }

        private void OnDisable() => DisableSources();
        private void OnDestroy()
        {
            DisableSources();
            for (int i = 0; i < lights.Length; i++)
            {
                DestroySource(lights[i]);
                DestroySource(facadeLights[i]);
            }
            lights = Array.Empty<Light>();
            facadeLights = Array.Empty<Light>();
            sources = Array.Empty<Source>();
            buildings = Array.Empty<CityBuildingAssetRegistry>();
            exteriorActive = null;
            viewCamera = null;
        }

        private static void DestroySource(Light light)
        {
            if (light == null) return;
            if (Application.isPlaying) Destroy(light.gameObject);
            else DestroyImmediate(light.gameObject);
        }
    }
}
