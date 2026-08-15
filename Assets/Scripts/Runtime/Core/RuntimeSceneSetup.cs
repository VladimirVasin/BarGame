using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class RuntimeSceneSetup
    {
        public static readonly Color CityFogColor =
            new Color(0.330f, 0.380f, 0.355f);
        public static readonly Color HomeBackgroundColor =
            new Color(0.105f, 0.080f, 0.070f);
        public static readonly Color CityAmbientColor =
            new Color(0.260f, 0.295f, 0.280f);
        public static readonly Color MoonlightColor =
            new Color(0.72f, 0.79f, 0.77f);
        public static readonly Quaternion CityMoonlightRotation =
            Quaternion.Euler(48f, -34f, 0f);

        public const float CityFogDensity = 0.070f;
        public const float CityFarClipPlane = 48f;
        public const float DoorTransitionFarClipPlane = 18f;
        public const float DefaultFarClipPlane = 220f;
        public const float GameplayNearClipPlane = 0.06f;
        public const float CityShadowStrength = 0.38f;
        public const float CityMoonlightIntensity = 0.72f;
        public const float CityNightReflectionIntensity = 0.50f;
        public const float PlayerMeshShadowBias = 0.04f;
        public const float PlayerMeshShadowNormalBias = 0.25f;
        public const float PlayerMeshShadowNearPlane = 0.10f;

        public static Camera EnsureCityNight()
        {
            Camera camera = EnsureCamera(CityFogColor);
            SetPostProcessing(camera, true);
            ApplyCityExteriorVisibility(camera);
            ApplyCityExteriorLighting();
            return camera;
        }

        public static Camera EnsureDoorTransition()
        {
            Camera camera = EnsureCamera(Color.black);
            SetPostProcessing(camera, false);
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
            SetPostProcessing(camera, true);
            ConfigureDirectionalLighting(
                new Color(0.92f, 0.82f, 0.72f),
                0.72f,
                new Color(0.11f, 0.055f, 0.045f),
                0.72f);

            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.65f;
            DynamicGI.UpdateEnvironment();
            return camera;
        }

        public static Camera EnsureSupermarketInterior()
        {
            Camera camera = EnsureCamera(
                new Color(0.055f, 0.070f, 0.060f));
            SetPostProcessing(camera, true);
            // The fluorescent practicals of the atmosphere carry the
            // hall; the directional is the fill. The ceiling shadows
            // the whole floor from it, so only (1 - shadowStrength)
            // survives indoors — a softer strength and a stronger key
            // turn it into a usable readability floor, the same rule
            // the Home interior settled on.
            ConfigureDirectionalLighting(
                new Color(0.70f, 0.82f, 0.72f),
                0.72f,
                new Color(0.21f, 0.25f, 0.225f),
                0.45f);

            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.38f;
            DynamicGI.UpdateEnvironment();
            return camera;
        }

        public static Camera EnsureHomeInterior()
        {
            Camera camera = EnsureCamera(HomeBackgroundColor);
            SetPostProcessing(camera, true);
            // The ceiling shadows the whole flat from the directional,
            // so only (1 - shadowStrength) of it survives indoors; a
            // softer strength turns the sun into usable interior fill.
            ConfigureDirectionalLighting(
                new Color(0.88f, 0.82f, 0.72f),
                0.85f,
                new Color(0.22f, 0.20f, 0.18f),
                0.45f);

            ApplyHomeInteriorVisibility(camera);
            RenderSettings.reflectionIntensity = 0.55f;
            DynamicGI.UpdateEnvironment();
            return camera;
        }

        public static void ApplyCityExteriorVisibility(Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            camera.backgroundColor = CityFogColor;
            camera.farClipPlane = CityFarClipPlane;
            RenderSettings.fog = true;
            RenderSettings.fogColor = CityFogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = CityFogDensity;
        }

        public static void ApplyHomeInteriorVisibility(Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            camera.backgroundColor = HomeBackgroundColor;
            camera.farClipPlane = DefaultFarClipPlane;
            RenderSettings.fog = false;
        }

        public static void ApplyCityExteriorLighting()
        {
            ApplyCityExteriorLighting(
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes),
                true);
        }

        public static void ApplyCityExteriorLighting(
            DayNightVisualSample sample,
            bool updateEnvironment = true)
        {
            Light directional = ConfigureDirectionalLighting(
                sample.DirectionalLightColor,
                sample.DirectionalLightIntensity,
                sample.AmbientLightColor,
                sample.ShadowStrength);
            directional.transform.rotation =
                sample.DirectionalLightRotation;
            RenderSettings.reflectionIntensity =
                sample.ReflectionIntensity;
            if (updateEnvironment)
            {
                DynamicGI.UpdateEnvironment();
            }
        }

        public static VolumeProfile CreateCityNoirRuntimeProfile()
        {
            VolumeProfile profile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Runtime City Noir Grade";
            profile.hideFlags = HideFlags.HideAndDontSave;

            Tonemapping tonemapping =
                profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.60f);
            bloom.intensity.Override(0.62f);
            bloom.scatter.Override(0.48f);
            bloom.clamp.Override(10f);
            bloom.highQualityFiltering.Override(false);

            ColorAdjustments color =
                profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.62f);
            color.contrast.Override(-10f);
            color.saturation.Override(-24f);
            color.colorFilter.Override(
                new Color(0.94f, 1.00f, 0.97f, 1f));

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.10f);
            vignette.smoothness.Override(0.48f);
            vignette.rounded.Override(false);

            FilmGrain grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.015f);
            grain.response.Override(0.80f);
            return profile;
        }

        public static Camera EnsureStairwellInterior()
        {
            Camera camera = EnsureCamera(
                new Color(0.035f, 0.052f, 0.044f));
            SetPostProcessing(camera, true);
            ConfigureDirectionalLighting(
                new Color(0.50f, 0.62f, 0.55f),
                0.34f,
                new Color(0.040f, 0.055f, 0.046f),
                0.72f);

            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.30f;
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
            // The continuous 3D player and camera-local arm subsets can enter
            // authored close shots. Keep the near plane tight enough for those
            // meshes without sacrificing meaningful depth precision at the
            // bounded gameplay far planes.
            camera.nearClipPlane = GameplayNearClipPlane;
            camera.farClipPlane = DefaultFarClipPlane;
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderShadows = true;
            cameraData.requiresDepthTexture = true;
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

        private static Light ConfigureDirectionalLighting(
            Color color,
            float intensity,
            Color ambientColor,
            float shadowStrength)
        {
            Light directional = RenderSettings.sun;
            if (directional == null ||
                directional.type != LightType.Directional)
            {
                Light[] lights = Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Exclude);
                directional = null;
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional)
                    {
                        directional = lights[i];
                        break;
                    }
                }
            }

            if (directional == null)
            {
                GameObject lightObject = new GameObject("Directional Light");
                directional = lightObject.AddComponent<Light>();
                directional.type = LightType.Directional;
            }

            directional.transform.rotation = CityMoonlightRotation;
            directional.color = color;
            directional.intensity = intensity;
            directional.enabled = true;
            directional.cullingMask = ~0;
            directional.shadows = LightShadows.Hard;
            directional.shadowStrength = shadowStrength;
            directional.shadowBias = PlayerMeshShadowBias;
            directional.shadowNormalBias = PlayerMeshShadowNormalBias;
            directional.shadowNearPlane = PlayerMeshShadowNearPlane;
            RenderSettings.sun = directional;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
            return directional;
        }

        private static void SetPostProcessing(
            Camera camera,
            bool enabled)
        {
            UniversalAdditionalCameraData cameraData =
                camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = enabled;
        }
    }
}
