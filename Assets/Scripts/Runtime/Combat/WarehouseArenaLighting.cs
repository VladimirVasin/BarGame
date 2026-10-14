using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>Scene-local darkness, dim practical pools and the fan's authored overhead source.</summary>
    public static class WarehouseArenaLighting
    {
        public const float NearDistance = 3f;
        public const float FarDistance = 12f;
        public const string OriginPropertyName = "_WarehouseAmbientOrigin";
        public const string FanLightName = "Warehouse Fan Overhead Light";
        private static readonly int OriginId = Shader.PropertyToID(OriginPropertyName);
        private static readonly int FalloffId = Shader.PropertyToID("_WarehouseAmbientFalloff");
        private static Transform visibilityOwner;
        private static Vector3 lastOrigin;
        // Unity's ambient colour is gamma-space. This remains faint after the
        // dark painted-metal albedo and the final PS1 composite are applied.
        public static readonly Color AmbientColor = new Color(.36f, .40f, .375f);

        public static Camera EnsureCamera()
        {
            Camera camera = RuntimeSceneSetup.EnsureCamera(Color.black);
            camera.farClipPlane = 48f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            // The warehouse is Single-loaded. Any authored scene light is off;
            // Warehouse practicals, the fan source and the hero's light are installed later.
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (light.gameObject.scene == camera.gameObject.scene) light.enabled = false;
            ApplyAmbient();
            return camera;
        }

        public static void ApplyAmbient()
        {
            RenderSettings.sun = null;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            // Skybox mode also supports an explicitly authored ambient probe.
            // Flat mode regenerates native probe data from the gamma-space colour.
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientLight = AmbientColor;
            RenderSettings.reflectionIntensity = 0f;
            // Supply one uniform ambient probe; no sky/environment capture or
            // recurring updates are needed for this sealed, scene-local room.
            var diffuse = new SphericalHarmonicsL2();
            diffuse.AddAmbientLight(AmbientColor);
            RenderSettings.ambientProbe = diffuse;
        }

        public static Light CreatePractical(WarehouseArenaLamp lamp, Transform sourceAnchor, Transform parent)
        {
            var host = new GameObject("Warehouse Work Lamp");
            host.transform.SetParent(parent, false);
            // The real fixture lens owns the source, including its imported FBX unit factor.
            host.transform.position = sourceAnchor.position;
            host.transform.localRotation = Quaternion.LookRotation(lamp.Direction,
                Mathf.Abs(Vector3.Dot(lamp.Direction, Vector3.up)) > .99f ? Vector3.forward : Vector3.up);
            Light light = host.AddComponent<Light>();
            // Short directed pools prevent a room's light leaking through its neighbours.
            light.type = LightType.Spot;
            light.color = lamp.Color;
            light.intensity = lamp.Intensity;
            light.range = lamp.Range;
            light.spotAngle = lamp.OuterAngle;
            light.innerSpotAngle = lamp.InnerAngle;
            light.shadows = LightShadows.Hard;
            light.shadowBias = .035f;
            light.shadowNormalBias = .12f;
            UniversalAdditionalLightData data = host.AddComponent<UniversalAdditionalLightData>();
            data.usePipelineSettings = false;
            data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            return light;
        }

        public static Light CreateFanOverhead(Transform sourceAnchor, Transform parent)
        {
            var host = new GameObject(FanLightName);
            host.transform.SetParent(parent, false);
            // Read the imported anchor in world space: its FBX root owns the
            // centimetre unit factor. The light itself uses the metre wrapper.
            host.transform.position = sourceAnchor.position;
            host.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            Light light = host.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(.92f, .86f, .65f);
            light.intensity = 250f;
            light.range = 30f;
            light.spotAngle = 125f;
            light.innerSpotAngle = 108f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 1f;
            light.shadowBias = .012f;
            light.shadowNormalBias = .025f;
            light.shadowNearPlane = .05f;
            UniversalAdditionalLightData data = host.AddComponent<UniversalAdditionalLightData>();
            data.usePipelineSettings = false;
            data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh;
            return light;
        }

        public static void BindHeroVisibility(Transform hero)
        {
            visibilityOwner = hero;
            Shader.SetGlobalVector(FalloffId, new Vector4(NearDistance, 1f / (FarDistance - NearDistance), FarDistance, 0f));
            WriteHeroOrigin(hero.position);
        }

        public static void UpdateHeroVisibility(Transform hero)
        {
            if (visibilityOwner != hero || hero.position.Equals(lastOrigin)) return;
            WriteHeroOrigin(hero.position);
        }

        public static void ClearHeroVisibility(Transform hero)
        {
            // An old scene cannot clear a newer hero's rendering ownership.
            if (visibilityOwner != hero) return;
            ResetHeroVisibility();
        }

        private static void WriteHeroOrigin(Vector3 position)
        {
            lastOrigin = position;
            Shader.SetGlobalVector(OriginId, new Vector4(position.x, position.y, position.z, 1f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetHeroVisibility()
        {
            visibilityOwner = null;
            lastOrigin = default;
            Shader.SetGlobalVector(OriginId, Vector4.zero);
        }
    }
}
