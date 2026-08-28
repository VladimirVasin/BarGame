using System;
using BarPromenade.Rendering;
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
        public static readonly Color MountainRoadFogColor =
            new Color(0.265f, 0.315f, 0.300f);

        /// <summary>
        /// The one warm haze in the game. Everywhere else the fog is the same
        /// grey-green; up here it is pale and slightly amber, which is most of
        /// why the village reads as a different temperature rather than a
        /// different architecture.
        /// </summary>
        public static readonly Color AlpineVillageFogColor =
            new Color(0.575f, 0.545f, 0.495f);

        /// <summary>
        /// What the village looks like once it has gone out - the ordinary
        /// mountain dusk it ends the prologue as. Held here beside the warm
        /// one so the two are read together and never drift apart.
        /// </summary>
        public static readonly Color AlpineVillageDimFogColor =
            new Color(0.295f, 0.310f, 0.315f);
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
        public const float MountainRoadFogDensity = 0.026f;
        public const float MountainRoadFarClipPlane = 120f;

        /// <summary>
        /// Chosen against one shot, not by feel: the mother's house stands
        /// `82 m` up the lane from the station platform, and at this density
        /// roughly a quarter of it survives the haze - a warm shape you walk
        /// into rather than a building you can read. Any denser and the whole
        /// composition disappears; any thinner and the ridge stops hiding the
        /// edge of the world, which is what keeps the alpine postcard out.
        /// </summary>
        public const float AlpineVillageFogDensity = 0.0145f;

        public const float AlpineVillageFarClipPlane = 140f;
        public const float DoorTransitionFarClipPlane = 18f;
        public const float AreaLoadingFarClipPlane = 1f;
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
            BindAuthoredVolumeDepthOfField();
            return camera;
        }

        /// <summary>
        /// Attaches the depth-of-field settings binder to every
        /// authored global volume in the scene, working on the
        /// volume's runtime profile clone so the shared asset is
        /// never dirtied. The override is ensured on the clone, so a
        /// stale authored profile still gets the city defaults.
        /// </summary>
        public static void BindAuthoredVolumeDepthOfField()
        {
            Volume[] volumes = Object.FindObjectsByType<Volume>();
            for (int index = 0; index < volumes.Length; index++)
            {
                Volume volume = volumes[index];
                if (!volume.isGlobal ||
                    volume.sharedProfile == null ||
                    volume.GetComponent<DepthOfFieldSettingsBinder>()
                        != null)
                {
                    continue;
                }

                AddGaussianDepthOfField(
                    volume.profile, 8f, 28f, 1.5f);
                volume.gameObject
                    .AddComponent<DepthOfFieldSettingsBinder>()
                    .Initialize(volume.profile);
            }
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

        public static Camera EnsureAreaLoading()
        {
            Camera camera = EnsureCamera(Color.black);
            SetPostProcessing(camera, false);
            camera.cullingMask = 0;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = AreaLoadingFarClipPlane;

            RenderSettings.fog = false;
            RenderSettings.sun = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;
            return camera;
        }

        public static Camera EnsureMountainRoad()
        {
            Camera camera = EnsureCamera(MountainRoadFogColor);
            camera.cullingMask = ~0;
            SetPostProcessing(camera, true);
            ApplyMountainRoadVisibility(camera);
            ApplyMountainRoadLighting(
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes));
            BindAuthoredVolumeDepthOfField();
            return camera;
        }

        public static Camera EnsureAlpineVillage()
        {
            Camera camera = EnsureCamera(AlpineVillageFogColor);
            camera.cullingMask = ~0;
            SetPostProcessing(camera, true);
            ApplyAlpineVillageVisibility(camera, 0f);
            ApplyAlpineVillageLighting(
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes),
                0f);
            BindAuthoredVolumeDepthOfField();
            return camera;
        }

        public static Camera EnsureBarInterior()
        {
            Camera camera = EnsureCamera(new Color(0.09f, 0.045f, 0.035f));
            SetPostProcessing(camera, true);
            // The Home readability rule, tuned darker: the bar stays
            // the moodiest interior, but the ambient floor keeps the
            // hall and its guests legible between the lamp pools and
            // a softer shadow lets the directional act as fill.
            ConfigureDirectionalLighting(
                new Color(0.92f, 0.82f, 0.72f),
                0.95f,
                new Color(0.28f, 0.20f, 0.17f),
                0.42f);

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

        public static Camera EnsureChurchInterior()
        {
            Camera camera = EnsureCamera(
                new Color(0.075f, 0.068f, 0.060f));
            camera.farClipPlane = 70f;
            SetPostProcessing(camera, true);
            // The night end of the church's own schedule.
            // ChurchInteriorDayNightController owns the sun, the
            // ambient and both light layers from its first frame; this
            // is only the state the scene opens on.
            // Nearly opaque shadows, and this is the number the whole
            // feature turns on. At 0.48 half the sun survived every
            // wall in the building, so a window could not be the way
            // light gets in - it was already everywhere. Soft, because
            // an aperture resolves to a few dozen shadow texels at
            // nave distance and a hard edge on that crawls.
            ConfigureDirectionalLighting(
                ChurchInteriorDayNightController.NightSunColor,
                ChurchInteriorDayNightController.NightSunIntensity,
                ChurchInteriorDayNightController.NightAmbientColor,
                ChurchInteriorDayNightController.SunShadowStrength,
                ChurchInteriorSunRules.BakedInteriorSun,
                LightShadows.Soft);

            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.42f;
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

        public static void ApplyMountainRoadVisibility(Camera camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            camera.backgroundColor = MountainRoadFogColor;
            camera.farClipPlane = MountainRoadFarClipPlane;
            RenderSettings.fog = true;
            RenderSettings.fogColor = MountainRoadFogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = MountainRoadFogDensity;
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

        public static void ApplyMountainRoadLighting(
            DayNightVisualSample sample,
            bool updateEnvironment = true)
        {
            Color coldDirectional = sample.DirectionalLightColor *
                new Color(0.82f, 0.91f, 0.90f);
            Color coldAmbient = sample.AmbientLightColor *
                new Color(0.72f, 0.86f, 0.82f);
            Light directional = ConfigureDirectionalLighting(
                coldDirectional,
                sample.DirectionalLightIntensity * 0.90f,
                coldAmbient,
                Mathf.Lerp(
                    sample.ShadowStrength,
                    0.52f,
                    0.35f));
            directional.transform.rotation =
                sample.DirectionalLightRotation;
            RenderSettings.reflectionIntensity =
                sample.ReflectionIntensity * 0.68f;
            if (updateEnvironment)
            {
                DynamicGI.UpdateEnvironment();
            }
        }

        /// <summary>
        /// The village's haze, as a function of how far it has gone out.
        ///
        /// Fog is applied here and not somewhere else on purpose. Distant
        /// geometry blends TO the fog colour, so a warm sun over a cold haze
        /// is grey soup with a hole in it - the two have to move on one
        /// weight, which means they have to be written by one call.
        /// </summary>
        public static void ApplyAlpineVillageVisibility(
            Camera camera,
            float warmthGrade)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            float dim = Mathf.Clamp01(warmthGrade);
            Color fog = Color.Lerp(
                AlpineVillageFogColor,
                AlpineVillageDimFogColor,
                dim);
            camera.backgroundColor = fog;
            camera.farClipPlane = AlpineVillageFarClipPlane;
            RenderSettings.fog = true;
            RenderSettings.fogColor = fog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            // It closes in a little as the place goes out. Not enough to be a
            // weather event - «снежная буря» is forbidden - just enough that
            // the top of the lane stops being visible from the bottom.
            RenderSettings.fogDensity = Mathf.Lerp(
                AlpineVillageFogDensity,
                AlpineVillageFogDensity * 1.55f,
                dim);
        }

        /// <summary>
        /// The village grade, and the reason <paramref name="warmthGrade"/> is
        /// a PARAMETER rather than something written over this afterwards.
        ///
        /// The area's atmosphere re-applies this every game minute. Anything
        /// another component writes on top of it is wiped inside a second, so
        /// the only place a dimming pass can live is here, inside the call
        /// that keeps happening. The mountain road learned this the expensive
        /// way with its ride blackout.
        ///
        /// `0` is the village as §12 describes it - warm, and warm is the
        /// baseline, not an effect. `1` is an ordinary mountain village at
        /// dusk. Nothing drives it above zero yet; the prologue will.
        /// </summary>
        public static void ApplyAlpineVillageLighting(
            DayNightVisualSample sample,
            float warmthGrade,
            bool updateEnvironment = true)
        {
            float dim = Mathf.Clamp01(warmthGrade);

            // Warm key, lifted ambient, soft shadows: the opposite of the
            // mountain road's cold treatment, which the dim end lerps to.
            Color warmDirectional = sample.DirectionalLightColor *
                new Color(1.06f, 0.97f, 0.84f);
            Color coldDirectional = sample.DirectionalLightColor *
                new Color(0.84f, 0.92f, 0.94f);
            Color warmAmbient = sample.AmbientLightColor *
                new Color(1.10f, 0.99f, 0.86f);
            Color coldAmbient = sample.AmbientLightColor *
                new Color(0.74f, 0.86f, 0.88f);

            Light directional = ConfigureDirectionalLighting(
                Color.Lerp(warmDirectional, coldDirectional, dim),
                sample.DirectionalLightIntensity *
                Mathf.Lerp(1.06f, 0.86f, dim),
                Color.Lerp(warmAmbient, coldAmbient, dim) *
                Mathf.Lerp(1.22f, 0.88f, dim),
                Mathf.Lerp(
                    Mathf.Lerp(sample.ShadowStrength, 0.44f, 0.45f),
                    Mathf.Lerp(sample.ShadowStrength, 0.55f, 0.30f),
                    dim));
            directional.transform.rotation =
                sample.DirectionalLightRotation;
            RenderSettings.reflectionIntensity =
                sample.ReflectionIntensity * Mathf.Lerp(0.86f, 0.64f, dim);
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

            AddGaussianDepthOfField(profile, 8f, 28f, 1.5f);
            return profile;
        }

        public static DepthOfField AddGaussianDepthOfField(
            VolumeProfile profile,
            float gaussianStart,
            float gaussianEnd,
            float gaussianMaxRadius)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (!profile.TryGet(out DepthOfField depthOfField))
            {
                depthOfField = profile.Add<DepthOfField>(true);
            }

            depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
            depthOfField.gaussianStart.Override(gaussianStart);
            depthOfField.gaussianEnd.Override(gaussianEnd);
            depthOfField.gaussianMaxRadius.Override(gaussianMaxRadius);
            depthOfField.highQualitySampling.Override(false);
            depthOfField.active =
                GraphicsEffectsSettings.DepthOfFieldEnabled;
            return depthOfField;
        }

        public static Camera EnsureStairwellInterior()
        {
            Camera camera = EnsureCamera(
                new Color(0.035f, 0.052f, 0.044f));
            SetPostProcessing(camera, true);
            ConfigureDirectionalLighting(
                new Color(0.50f, 0.62f, 0.55f),
                0.34f,
                new Color(0.085f, 0.105f, 0.090f),
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

        /// <summary>
        /// Finds, or makes, the one directional light a scene is lit
        /// by.
        ///
        /// <paramref name="rotation"/> exists because an interior does
        /// not share the City's compass: the church's model stands at
        /// identity in its own scene, so the world sun has to be
        /// carried into that frame before it means anything. Left null
        /// the moon's world pose is used, which is what every exterior
        /// wants.
        ///
        /// <paramref name="shadows"/> because a room whose only light
        /// comes through five windows lives or dies on the quality of
        /// the shadow that shapes them.
        /// </summary>
        private static Light ConfigureDirectionalLighting(
            Color color,
            float intensity,
            Color ambientColor,
            float shadowStrength,
            Quaternion? rotation = null,
            LightShadows shadows = LightShadows.Hard)
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

            directional.transform.rotation =
                rotation ?? CityMoonlightRotation;
            directional.color = color;
            directional.intensity = intensity;
            directional.enabled = true;
            directional.cullingMask = ~0;
            directional.shadows = shadows;
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
