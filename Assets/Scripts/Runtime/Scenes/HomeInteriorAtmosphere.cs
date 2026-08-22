using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class HomeInteriorAtmosphere : MonoBehaviour
    {
        public const int MaximumPracticalLights = 2;
        public const int MaximumWindowLights = 1;
        public const int MaximumBathroomSpillLights = 1;
        public const int MaximumEntryDoorLights = 1;
        public const int MaximumRealtimeLights =
            MaximumPracticalLights +
            MaximumWindowLights +
            MaximumBathroomSpillLights +
            MaximumEntryDoorLights;
        public const int MaximumDustParticles = 12;
        public const float NightWindowLightIntensity = 5.25f;

        public static readonly Color NightWindowLightColor =
            new Color(0.43f, 0.58f, 0.84f);

        internal static readonly Vector3 MainEmitterPosition =
            new Vector3(-2.35f, 2.05f, 0.45f);
        internal static readonly Vector3 MainLightPosition =
            new Vector3(-2.35f, 2.00f, 0.45f);
        internal static readonly Vector3 BathroomEmitterPosition =
            new Vector3(3.15f, 2.16f, 3.755f);
        internal static readonly Vector3 BathroomLightPosition =
            new Vector3(3.15f, 2.04f, 3.38f);
        internal static readonly Vector3 WindowLightPosition =
            new Vector3(
                PlayerHomeBalconyGeometry.HomeFacadeX + 1.35f,
                2.75f,
                PlayerHomeBalconyGeometry.WindowCenterZ);
        internal static readonly Vector3 WindowLightTarget =
            new Vector3(
                0.65f,
                0.22f,
                PlayerHomeBalconyGeometry.WindowCenterZ);
        internal static readonly Vector3 WindowLightDirection =
            (WindowLightTarget - WindowLightPosition).normalized;
        internal static readonly Vector3 BathroomSpillLightPosition =
            new Vector3(2.15f, 2.05f, 0.82f);
        internal static readonly Vector3 BathroomSpillLightTarget =
            new Vector3(0f, 0.12f, -3.05f);
        internal static readonly Vector3 BathroomSpillLightDirection =
            (BathroomSpillLightTarget -
             BathroomSpillLightPosition).normalized;
        internal static readonly Vector3 EntryDoorLightPosition =
            new Vector3(0f, 2.45f, -3.70f);
        internal static readonly Vector3 EntryDoorLightTarget =
            new Vector3(0f, 0.55f, -2.75f);
        internal static readonly Vector3 EntryDoorLightDirection =
            (EntryDoorLightTarget - EntryDoorLightPosition).normalized;

        private readonly List<Light> practicalLights =
            new List<Light>(MaximumPracticalLights);
        private ReadOnlyCollection<Light> readOnlyPracticalLights;
        private VolumeProfile runtimeProfile;

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<Light> PracticalLights =>
            readOnlyPracticalLights ??
            (readOnlyPracticalLights = practicalLights.AsReadOnly());
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile => runtimeProfile;
        public ParticleSystem Dust { get; private set; }
        public Light WindowLight { get; private set; }
        public Light BathroomSpillLight { get; private set; }
        public Light EntryDoorLight { get; private set; }
        public HomeBathroomLightFlicker BathroomFlicker
        {
            get;
            private set;
        }

        public void Initialize()
        {
            Initialize(
                null,
                MainLightPosition,
                BathroomLightPosition);
        }

        public void Initialize(
            HomeBathroomLightFixture bathroomFixture)
        {
            Initialize(
                bathroomFixture,
                MainLightPosition,
                BathroomLightPosition);
        }

        public void Initialize(
            Vector3 mainLightPosition,
            Vector3 bathroomLightPosition)
        {
            Initialize(
                null,
                mainLightPosition,
                bathroomLightPosition);
        }

        private void Initialize(
            HomeBathroomLightFixture bathroomFixture,
            Vector3 mainLightPosition,
            Vector3 bathroomLightPosition)
        {
            if (IsInitialized ||
                practicalLights.Count > 0 ||
                WindowLight != null ||
                BathroomSpillLight != null ||
                EntryDoorLight != null ||
                PostProcessVolume != null)
            {
                throw new InvalidOperationException(
                    "The home atmosphere is already initialized.");
            }

            if (!IsFinite(mainLightPosition) ||
                !IsFinite(bathroomLightPosition))
            {
                throw new ArgumentException(
                    "Home practical-light positions must be finite.");
            }

            // The main lamp's range must reach the far walls of the
            // ~9x7 m flat, or its inverse-square falloff leaves the
            // corners pitch black.
            practicalLights.Add(
                CreatePracticalLight(
                    "Home Main Practical Light",
                    mainLightPosition,
                    new Color(0.95f, 0.52f, 0.22f),
                    3.50f,
                    9.0f));
            Light bathroomLight = CreatePracticalLight(
                "Home Bathroom Practical Light",
                bathroomLightPosition,
                new Color(0.52f, 0.68f, 0.72f),
                2.20f,
                4.0f);
            practicalLights.Add(bathroomLight);
            WindowLight = CreateWindowLight();
            BathroomSpillLight = CreateBathroomSpillLight();
            EntryDoorLight = CreateEntryDoorLight();
            BathroomFlicker =
                bathroomLight.gameObject.AddComponent<
                    HomeBathroomLightFlicker>();
            BathroomFlicker.Initialize(
                bathroomLight,
                BathroomSpillLight,
                bathroomFixture);
            CreatePostProcessVolume();
            CreateDust();
            IsInitialized = true;
        }

        private Light CreatePracticalLight(
            string lightName,
            Vector3 position,
            Color color,
            float intensity,
            float range)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = position;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.08f;
            return light;
        }

        private Light CreateWindowLight()
        {
            GameObject lightObject = new GameObject(
                "Home Window Night Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition =
                WindowLightPosition;
            lightObject.transform.localRotation =
                Quaternion.LookRotation(
                    WindowLightDirection,
                    Vector3.up);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = NightWindowLightColor;
            light.intensity = NightWindowLightIntensity;
            light.range = 10.0f;
            light.spotAngle = 58f;
            light.innerSpotAngle = 34f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.72f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.28f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.08f;
            light.cookie =
                HomeBalconyResources.WindowLightCookie;
            return light;
        }

        private Light CreateBathroomSpillLight()
        {
            GameObject lightObject = new GameObject(
                "Home Bathroom Spill Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition =
                BathroomSpillLightPosition;
            lightObject.transform.localRotation =
                Quaternion.LookRotation(
                    BathroomSpillLightDirection,
                    Vector3.up);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.52f, 0.68f, 0.72f);
            light.intensity = 10.0f;
            light.range = 6.6f;
            light.spotAngle = 52f;
            light.innerSpotAngle = 30f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.62f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.25f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            return light;
        }

        private Light CreateEntryDoorLight()
        {
            GameObject lightObject = new GameObject(
                "Home Entry Door Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition =
                EntryDoorLightPosition;
            lightObject.transform.localRotation =
                Quaternion.LookRotation(
                    EntryDoorLightDirection,
                    Vector3.up);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1.0f, 0.46f, 0.16f);
            light.intensity = 8.0f;
            light.range = 5.5f;
            light.spotAngle = 100f;
            light.innerSpotAngle = 72f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0f;
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Home Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume = volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            runtimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Runtime Home Interior Grade";
            runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = runtimeProfile;

            Bloom bloom = runtimeProfile.Add<Bloom>(true);
            bloom.intensity.Override(0.16f);
            bloom.threshold.Override(1.10f);
            bloom.scatter.Override(0.48f);

            ColorAdjustments color =
                runtimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.25f);
            color.contrast.Override(5f);
            color.saturation.Override(-16f);
            color.colorFilter.Override(
                new Color(0.93f, 0.90f, 0.82f, 1f));

            Vignette vignette = runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.52f);

            FilmGrain grain = runtimeProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.14f);
            grain.response.Override(0.74f);

            RuntimeSceneSetup.AddGaussianDepthOfField(
                runtimeProfile, 5f, 14f, 1.0f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(runtimeProfile);
        }

        private void CreateDust()
        {
            GameObject dustObject = new GameObject(
                "Home Sparse Dust");
            dustObject.transform.SetParent(transform, false);
            dustObject.transform.localPosition =
                new Vector3(0f, 1.9f, 0.35f);
            Dust = dustObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = Dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaximumDustParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(12f, 18f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.008f, 0.025f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.045f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 0.70f, 0.42f, 0.026f),
                new Color(0.48f, 0.60f, 0.61f, 0.014f));
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = Dust.emission;
            emission.rateOverTime = 0.45f;

            ParticleSystem.ShapeModule shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(8.8f, 2.4f, 6.8f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                Dust.velocityOverLifetime;
            velocity.enabled = false;
            velocity.x =
                new ParticleSystem.MinMaxCurve(-0.008f, 0.008f);
            velocity.y =
                new ParticleSystem.MinMaxCurve(0.006f, 0.018f);
            velocity.z =
                new ParticleSystem.MinMaxCurve(-0.004f, 0.004f);
            velocity.enabled = true;

            ParticleSystemRenderer renderer =
                Dust.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial =
                CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.08f;
            Dust.Play();
        }

        private void OnDestroy()
        {
            if (runtimeProfile == null)
            {
                return;
            }

            if (PostProcessVolume != null)
            {
                PostProcessVolume.profile = null;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeProfile);
            }
            else
            {
                Object.DestroyImmediate(runtimeProfile);
            }

            runtimeProfile = null;
            IsInitialized = false;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
