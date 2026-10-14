using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>Scene-local darkness with a faint diffuse floor, never an invisible direct light.</summary>
    public static class WarehouseArenaLighting
    {
        public const float NearDistance = 3f;
        public const float FarDistance = 12f;
        public const string OriginPropertyName = "_WarehouseAmbientOrigin";
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
            // the one moving source is installed later with the original hero rig.
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
