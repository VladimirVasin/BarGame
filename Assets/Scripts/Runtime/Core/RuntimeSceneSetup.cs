using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class RuntimeSceneSetup
    {
        private static readonly Color CityFogColor =
            new Color(0.330f, 0.380f, 0.355f);
        private static readonly Color CityAmbientColor =
            new Color(0.260f, 0.295f, 0.280f);
        private static readonly Color MoonlightColor =
            new Color(0.72f, 0.79f, 0.77f);

        public const float CityFogDensity = 0.070f;
        public const float CityFarClipPlane = 48f;
        public const float DoorTransitionFarClipPlane = 18f;
        public const float DefaultFarClipPlane = 220f;
        public const float CityShadowStrength = 0.38f;

        public static Camera EnsureCityNight()
        {
            Camera camera = EnsureCamera(CityFogColor);
            camera.farClipPlane = CityFarClipPlane;
            ConfigureDirectionalLighting(
                MoonlightColor,
                0.72f,
                CityAmbientColor,
                CityShadowStrength);

            RenderSettings.fog = true;
            RenderSettings.fogColor = CityFogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = CityFogDensity;
            RenderSettings.reflectionIntensity = 0.50f;
            DynamicGI.UpdateEnvironment();
            return camera;
        }

        public static Camera EnsureDoorTransition()
        {
            Camera camera = EnsureCamera(Color.black);
            camera.farClipPlane = DoorTransitionFarClipPlane;

            RenderSettings.fog = false;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.010f, 0.009f);
            RenderSettings.reflectionIntensity = 0f;
            DynamicGI.UpdateEnvironment();
            return camera;
        }

        public static Camera EnsureBarInterior()
        {
            Camera camera = EnsureCamera(new Color(0.09f, 0.045f, 0.035f));
            ConfigureDirectionalLighting(
                new Color(1f, 0.92f, 0.82f),
                1.25f,
                new Color(0.32f, 0.18f, 0.14f),
                1f);

            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 1f;
            DynamicGI.UpdateEnvironment();
            return camera;
        }

        public static Camera EnsureCamera(Color backgroundColor)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = Object.FindAnyObjectByType<Camera>();
            }

            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = backgroundColor;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = DefaultFarClipPlane;
            return camera;
        }

        public static void EnsureLighting(Color ambientColor)
        {
            ConfigureDirectionalLighting(
                new Color(1f, 0.92f, 0.82f),
                1.25f,
                ambientColor,
                1f);
        }

        private static void ConfigureDirectionalLighting(
            Color color,
            float intensity,
            Color ambientColor,
            float shadowStrength)
        {
            Light[] lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Exclude);
            Light directional = null;
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    directional = lights[i];
                    break;
                }
            }

            if (directional == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                directional = lightObject.AddComponent<Light>();
                directional.type = LightType.Directional;
            }

            directional.transform.rotation = Quaternion.Euler(48f, -34f, 0f);
            directional.color = color;
            directional.intensity = intensity;
            directional.shadows = LightShadows.Hard;
            directional.shadowStrength = shadowStrength;
            RenderSettings.sun = directional;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
        }
    }
}
