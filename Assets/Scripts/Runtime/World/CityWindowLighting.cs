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
        public const int MaximumLights = 192;
        public const float MaximumRange = 5f;
        private const float BuildingRefreshSeconds = .35f;
        private const float BaseIntensity = 7f;
        private const float SourceInset = .2f;

        private readonly struct Source
        {
            public readonly Transform Building;
            public readonly Vector3 Center, Outward, Position;
            public readonly Quaternion Rotation;

            public Source(Transform building, Vector3 center, Vector3 outward)
            {
                Building = building;
                Center = center;
                Outward = outward;
                // Light leaves the opening from just inside the room. The
                // authored reveal and sill shape its exterior spill, instead
                // of an exterior downward projector painting the facade.
                Position = center - outward * SourceInset;
                Rotation = Quaternion.LookRotation(outward, Vector3.up);
            }
        }

        private Source[] sources = Array.Empty<Source>();
        private Light[] lights = Array.Empty<Light>();
        private Camera viewCamera;
        private Func<bool> exteriorActive;
        private float nextBuildingRefresh, lastFixtureFactor = -1f;
        private bool initialized, renderingActive;

        public IReadOnlyList<Light> Lights => lights;
        public int CandidateCount => sources.Length;
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
            foreach (CityBuildingAssetRegistry registry in sourceRoot.GetComponentsInChildren<CityBuildingAssetRegistry>(true))
            {
                foreach (CityBuildingWindowSlot slot in registry.WindowSlots)
                {
                    if ((registry.LitWindowMask & (1UL << slot.Uv2SlotId)) == 0) continue;
                    if (configuredSources.Count >= MaximumLights)
                        throw new InvalidOperationException("City window source budget exceeds 192.");
                    configuredSources.Add(new Source(registry.transform,
                        registry.transform.TransformPoint(slot.LocalCenter),
                        registry.transform.TransformDirection(LocalOutward(slot.Side)).normalized));
                }
            }

            sources = configuredSources.ToArray();
            lights = new Light[sources.Length];
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
            float factor = GameTimeDayNightRules.FixtureFactor(CityWindowAppearance.NightFactor);
            float intensity = BaseIntensity * factor;
            bool intensityChanged = factor != lastFixtureFactor;
            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];
                if (light == null) continue;
                if (intensityChanged) light.intensity = intensity;
                Transform building = sources[i].Building;
                bool enabledSource = building != null && building.gameObject.activeInHierarchy;
                if (light.enabled != enabledSource) light.enabled = enabledSource;
            }
            lastFixtureFactor = factor;
            nextBuildingRefresh = Time.unscaledTime + BuildingRefreshSeconds;
            renderingActive = true;
        }

        private void DisableSources()
        {
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null && lights[i].enabled) lights[i].enabled = false;
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
                if (lights[i] == null) continue;
                if (Application.isPlaying) Destroy(lights[i].gameObject);
                else DestroyImmediate(lights[i].gameObject);
            }
            lights = Array.Empty<Light>();
            sources = Array.Empty<Source>();
            exteriorActive = null;
            viewCamera = null;
        }
    }
}
