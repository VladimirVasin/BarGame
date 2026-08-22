using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Gives the reconstructed exterior the real City visibility profile
    /// without applying that profile to the apartment's interior shots.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [DisallowMultipleComponent]
    public sealed class HomeBalconyExteriorAtmosphere : MonoBehaviour
    {
        public const float FogAnchorDepth = 25.5f;

        private Camera targetCamera;
        private HomeFixedCameraController fixedCamera;
        private VisibilitySnapshot homeVisibility;
        private LightingSnapshot homeLighting;
        private VolumeProfile runtimeCityProfile;
        private DayNightVisualSample exteriorLightingSample;
        private bool hasExteriorLightingSample;
        private long lastThunderStrikeId = long.MinValue;

        public bool IsInitialized { get; private set; }
        public bool IsBalconyVisibilityActive { get; private set; }
        public Transform ExteriorRoot { get; private set; }
        public Transform FogAnchor { get; private set; }
        public CityFogField FogField { get; private set; }
        public CityRainField RainField { get; private set; }
        public CityRainSoundPlayer RainSound { get; private set; }
        public CityLightningFlashLight LightningFlash
        {
            get;
            private set;
        }
        public CityThunderSoundPlayer ThunderSound
        {
            get;
            private set;
        }
        public CityNightAtmosphere ExteriorLighting { get; private set; }
        public CityPedestrianDirector Pedestrians { get; private set; }
        public Volume CityPostProcessVolume { get; private set; }
        public VolumeProfile RuntimeCityProfile => runtimeCityProfile;

        public void Initialize(
            Camera camera,
            HomeFixedCameraController cameraController,
            Transform exteriorRoot,
            CityNightWorldResult exteriorNight,
            HomeExteriorContextPlan exteriorContext,
            Transform lightingFocus,
            HomeBalconyLayoutPlan balcony,
            int citySeed)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home balcony exterior atmosphere is already initialized.");
            }

            targetCamera = camera != null
                ? camera
                : throw new ArgumentNullException(nameof(camera));
            fixedCamera = cameraController != null
                ? cameraController
                : throw new ArgumentNullException(nameof(cameraController));
            ExteriorRoot = exteriorRoot != null
                ? exteriorRoot
                : throw new ArgumentNullException(nameof(exteriorRoot));
            if (exteriorNight == null)
            {
                throw new ArgumentNullException(nameof(exteriorNight));
            }
            if (exteriorContext == null)
            {
                throw new ArgumentNullException(nameof(exteriorContext));
            }
            if (lightingFocus == null)
            {
                throw new ArgumentNullException(nameof(lightingFocus));
            }
            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            homeVisibility = VisibilitySnapshot.Capture(targetCamera);
            homeLighting = LightingSnapshot.Capture();
            BuildExteriorFog(balcony, citySeed);
            BuildExteriorLighting(
                exteriorNight,
                exteriorContext,
                lightingFocus);
            BuildCityPostProcessVolume();
            IsInitialized = true;
            RefreshVisibility(true);
        }

        public void RefreshImmediate()
        {
            if (IsInitialized)
            {
                RefreshVisibility(true);
            }
        }

        public void BindPedestrians(
            CityPedestrianDirector pedestrianDirector)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the Home balcony exterior atmosphere first.");
            }

            if (Pedestrians != null)
            {
                throw new InvalidOperationException(
                    "The Home balcony pedestrians are already bound.");
            }

            Pedestrians = pedestrianDirector != null
                ? pedestrianDirector
                : throw new ArgumentNullException(
                    nameof(pedestrianDirector));
            SetPedestriansActive(IsBalconyVisibilityActive);
        }

        public void ApplyExteriorLighting(
            DayNightVisualSample sample,
            bool updateEnvironment = false)
        {
            exteriorLightingSample = sample;
            hasExteriorLightingSample = true;
            if (!IsInitialized || !IsBalconyVisibilityActive)
            {
                return;
            }

            RuntimeSceneSetup.ApplyCityExteriorLighting(
                sample,
                updateEnvironment);
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                RefreshVisibility(true);
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            RefreshVisibility(false);
            WeatherVisualSample weather =
                GameWeatherRules.EvaluateCurrent();
            RainField.SetIntensity(weather.RainIntensity);
            Vector3 windVelocity = GameWeatherRules
                .EvaluateCurrentWind()
                .Velocity(GameWeatherRules.WindSpeedAtFullStrength);
            RainField.SetWindDrift(
                new Vector2(windVelocity.x, windVelocity.z));
            RainSound.SetIntensity(
                IsBalconyVisibilityActive
                    ? weather.RainIntensity
                    : 0f);
            ApplyLightning();
        }

        private void ApplyLightning()
        {
            bool timeFlowing =
                GameSessionState.IsGameTimeRunning &&
                Time.timeScale > 0f;
            LightningSample sample =
                IsBalconyVisibilityActive && timeFlowing
                    ? GameWeatherRules.EvaluateCurrentLightning()
                    : LightningSample.None;
            LightningFlash.Apply(sample);
            if (!sample.IsFlashing ||
                sample.StrikeId == lastThunderStrikeId)
            {
                return;
            }

            lastThunderStrikeId = sample.StrikeId;
            ThunderSound.PlayStrike(sample.DistanceFactor);
        }

        private void OnDisable()
        {
            SetPedestriansActive(false);
            RestoreHomeVisibility();
        }

        private void OnDestroy()
        {
            SetPedestriansActive(false);
            RestoreHomeVisibility();
            DestroyCityPostProcessProfile();
            IsInitialized = false;
        }

        private void BuildExteriorFog(
            HomeBalconyLayoutPlan balcony,
            int citySeed)
        {
            GameObject anchorObject = new GameObject(
                "Home Exterior Fog Anchor");
            FogAnchor = anchorObject.transform;
            FogAnchor.SetParent(ExteriorRoot, false);
            FogAnchor.localPosition = new Vector3(
                HomeExteriorViewBuilder.ExteriorMinimumX +
                FogAnchorDepth,
                balcony.StreetGroundY,
                PlayerHomeBalconyGeometry.BalconyCenterZ);

            GameObject fogObject = new GameObject(
                "Home Exterior Fog Field");
            fogObject.transform.SetParent(ExteriorRoot, false);
            FogField = fogObject.AddComponent<CityFogField>();
            FogField.Initialize(
                FogAnchor,
                CityNightResources.AtmosphereMaterial,
                citySeed);

            GameObject rainObject = new GameObject(
                "Home Exterior Rain Field");
            rainObject.transform.SetParent(ExteriorRoot, false);
            RainField = rainObject.AddComponent<CityRainField>();
            RainField.Initialize(
                FogAnchor,
                CityNightResources.AtmosphereMaterial,
                citySeed,
                GameWeatherRules.EvaluateCurrent().RainIntensity);

            GameObject rainSoundObject = new GameObject(
                "Home Exterior Rain Sound");
            rainSoundObject.transform.SetParent(ExteriorRoot, false);
            RainSound =
                rainSoundObject.AddComponent<CityRainSoundPlayer>();
            GameObject lightningObject = new GameObject(
                "Home Exterior Lightning Flash");
            lightningObject.transform.SetParent(ExteriorRoot, false);
            LightningFlash = lightningObject
                .AddComponent<CityLightningFlashLight>();
            GameObject thunderObject = new GameObject(
                "Home Exterior Thunder Sound");
            thunderObject.transform.SetParent(ExteriorRoot, false);
            ThunderSound = thunderObject
                .AddComponent<CityThunderSoundPlayer>();
        }

        private void BuildExteriorLighting(
            CityNightWorldResult exteriorNight,
            HomeExteriorContextPlan context,
            Transform lightingFocus)
        {
            var barLightPositions = new List<Vector3>();
            for (int index = 0;
                 index < context.NearbyLots.Count;
                 index++)
            {
                BuildingLot lot =
                    context.NearbyLots[index];
                if (!lot.IsBar ||
                    !lot.HasRoadFrontage)
                {
                    continue;
                }

                Vector3 frontage = new Vector3(
                    lot.FrontageDirection.x,
                    0f,
                    lot.FrontageDirection.y);
                Vector3 cityPosition =
                    lot.DoorPosition + frontage * 0.72f;
                cityPosition.y = 2.45f;
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        cityPosition);
                if (localPosition.x <=
                    HomeExteriorViewBuilder.ExteriorMinimumX)
                {
                    continue;
                }

                barLightPositions.Add(
                    ExteriorRoot.TransformPoint(localPosition));
            }

            exteriorNight.InitializeAtmosphere(
                lightingFocus,
                barLightPositions);
            ExteriorLighting = exteriorNight.Atmosphere;
            SetExteriorLightingEnabled(false);
        }

        private void RefreshVisibility(bool force)
        {
            bool useCityVisibility =
                fixedCamera != null &&
                fixedCamera.isActiveAndEnabled &&
                fixedCamera.IsInitialized &&
                fixedCamera.ActiveShotKind ==
                HomeCameraShotKind.Balcony;
            if (!force &&
                useCityVisibility ==
                IsBalconyVisibilityActive)
            {
                return;
            }

            if (useCityVisibility)
            {
                RuntimeSceneSetup.ApplyCityExteriorVisibility(
                    targetCamera);
                SetExteriorLightingEnabled(true);
                if (hasExteriorLightingSample)
                {
                    RuntimeSceneSetup.ApplyCityExteriorLighting(
                        exteriorLightingSample);
                }
                else
                {
                    RuntimeSceneSetup.ApplyCityExteriorLighting();
                }
                FogField.FogRenderer.enabled = true;
                RainField.RainRenderer.enabled = true;
                CityPostProcessVolume.weight = 1f;
                IsBalconyVisibilityActive = true;
                SetPedestriansActive(true);
                return;
            }

            SetPedestriansActive(false);
            FogField.FogRenderer.enabled = false;
            RainField.RainRenderer.enabled = false;
            SetExteriorLightingEnabled(false);
            CityPostProcessVolume.weight = 0f;
            homeVisibility.Restore(targetCamera);
            homeLighting.Restore();
            IsBalconyVisibilityActive = false;
        }

        private void SetPedestriansActive(bool active)
        {
            if (Pedestrians != null &&
                Pedestrians.enabled != active)
            {
                Pedestrians.enabled = active;
            }
        }

        private void RestoreHomeVisibility()
        {
            if (!IsInitialized ||
                !IsBalconyVisibilityActive)
            {
                return;
            }

            homeVisibility.Restore(targetCamera);
            homeLighting.Restore();
            SetExteriorLightingEnabled(false);
            if (CityPostProcessVolume != null)
            {
                CityPostProcessVolume.weight = 0f;
            }
            if (FogField != null &&
                FogField.FogRenderer != null)
            {
                FogField.FogRenderer.enabled = false;
            }
            if (RainField != null &&
                RainField.RainRenderer != null)
            {
                RainField.RainRenderer.enabled = false;
            }
            if (RainSound != null)
            {
                RainSound.SetIntensity(0f);
            }
            if (LightningFlash != null)
            {
                LightningFlash.Apply(LightningSample.None);
            }
            IsBalconyVisibilityActive = false;
        }

        private void SetExteriorLightingEnabled(
            bool lightingEnabled)
        {
            if (ExteriorLighting == null)
            {
                return;
            }

            ExteriorLighting.enabled = lightingEnabled;
            SetLightObjectsEnabled(
                ExteriorLighting.BarLights,
                lightingEnabled);
            SetLightObjectsEnabled(
                ExteriorLighting.StreetLightPool,
                lightingEnabled);
            if (lightingEnabled)
            {
                ExteriorLighting.RefreshImmediate();
            }
        }

        private static void SetLightObjectsEnabled(
            IReadOnlyList<Light> lights,
            bool lightingEnabled)
        {
            for (int index = 0;
                 index < lights.Count;
                 index++)
            {
                Light light = lights[index];
                if (light != null)
                {
                    light.gameObject.SetActive(lightingEnabled);
                }
            }
        }

        private void BuildCityPostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Home Balcony City Grade");
            volumeObject.transform.SetParent(transform, false);
            CityPostProcessVolume =
                volumeObject.AddComponent<Volume>();
            CityPostProcessVolume.isGlobal = true;
            CityPostProcessVolume.priority = 5f;
            CityPostProcessVolume.weight = 0f;
            runtimeCityProfile =
                RuntimeSceneSetup
                    .CreateCityNoirRuntimeProfile();
            CityPostProcessVolume.profile = runtimeCityProfile;
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(runtimeCityProfile);
        }

        private void DestroyCityPostProcessProfile()
        {
            if (runtimeCityProfile == null)
            {
                return;
            }

            if (CityPostProcessVolume != null)
            {
                CityPostProcessVolume.profile = null;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeCityProfile);
            }
            else
            {
                Object.DestroyImmediate(runtimeCityProfile);
            }

            runtimeCityProfile = null;
        }

        private readonly struct VisibilitySnapshot
        {
            private VisibilitySnapshot(
                bool fog,
                Color fogColor,
                FogMode fogMode,
                float fogDensity,
                Color backgroundColor,
                float farClipPlane)
            {
                Fog = fog;
                FogColor = fogColor;
                FogMode = fogMode;
                FogDensity = fogDensity;
                BackgroundColor = backgroundColor;
                FarClipPlane = farClipPlane;
            }

            private bool Fog { get; }
            private Color FogColor { get; }
            private FogMode FogMode { get; }
            private float FogDensity { get; }
            private Color BackgroundColor { get; }
            private float FarClipPlane { get; }

            public static VisibilitySnapshot Capture(Camera camera)
            {
                return new VisibilitySnapshot(
                    RenderSettings.fog,
                    RenderSettings.fogColor,
                    RenderSettings.fogMode,
                    RenderSettings.fogDensity,
                    camera.backgroundColor,
                    camera.farClipPlane);
            }

            public void Restore(Camera camera)
            {
                RenderSettings.fog = Fog;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogMode = FogMode;
                RenderSettings.fogDensity = FogDensity;
                if (camera == null)
                {
                    return;
                }

                camera.backgroundColor = BackgroundColor;
                camera.farClipPlane = FarClipPlane;
            }
        }

        private readonly struct LightingSnapshot
        {
            private LightingSnapshot(
                Light sun,
                Quaternion sunRotation,
                Color sunColor,
                float sunIntensity,
                LightShadows sunShadows,
                float sunShadowStrength,
                AmbientMode ambientMode,
                Color ambientLight,
                float reflectionIntensity)
            {
                Sun = sun;
                SunRotation = sunRotation;
                SunColor = sunColor;
                SunIntensity = sunIntensity;
                SunShadows = sunShadows;
                SunShadowStrength = sunShadowStrength;
                AmbientMode = ambientMode;
                AmbientLight = ambientLight;
                ReflectionIntensity = reflectionIntensity;
            }

            private Light Sun { get; }
            private Quaternion SunRotation { get; }
            private Color SunColor { get; }
            private float SunIntensity { get; }
            private LightShadows SunShadows { get; }
            private float SunShadowStrength { get; }
            private AmbientMode AmbientMode { get; }
            private Color AmbientLight { get; }
            private float ReflectionIntensity { get; }

            public static LightingSnapshot Capture()
            {
                Light sun = RenderSettings.sun;
                return new LightingSnapshot(
                    sun,
                    sun != null
                        ? sun.transform.rotation
                        : Quaternion.identity,
                    sun != null
                        ? sun.color
                        : Color.black,
                    sun != null
                        ? sun.intensity
                        : 0f,
                    sun != null
                        ? sun.shadows
                        : LightShadows.None,
                    sun != null
                        ? sun.shadowStrength
                        : 0f,
                    RenderSettings.ambientMode,
                    RenderSettings.ambientLight,
                    RenderSettings.reflectionIntensity);
            }

            public void Restore()
            {
                RenderSettings.sun = Sun;
                if (Sun != null)
                {
                    Sun.transform.rotation = SunRotation;
                    Sun.color = SunColor;
                    Sun.intensity = SunIntensity;
                    Sun.shadows = SunShadows;
                    Sun.shadowStrength = SunShadowStrength;
                }

                RenderSettings.ambientMode = AmbientMode;
                RenderSettings.ambientLight = AmbientLight;
                RenderSettings.reflectionIntensity =
                    ReflectionIntensity;
                DynamicGI.UpdateEnvironment();
            }
        }
    }
}
