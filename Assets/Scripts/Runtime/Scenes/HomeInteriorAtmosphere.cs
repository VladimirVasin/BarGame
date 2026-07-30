using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        public const int MaximumRealtimeLights =
            MaximumPracticalLights + MaximumWindowLights;
        public const int MaximumDustParticles = 12;

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

        public void Initialize()
        {
            Initialize(
                MainLightPosition,
                BathroomLightPosition);
        }

        public void Initialize(
            Vector3 mainLightPosition,
            Vector3 bathroomLightPosition)
        {
            if (IsInitialized ||
                practicalLights.Count > 0 ||
                WindowLight != null ||
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

            practicalLights.Add(
                CreatePracticalLight(
                    "Home Main Practical Light",
                    mainLightPosition,
                    new Color(0.95f, 0.52f, 0.22f),
                    3.50f,
                    6.0f));
            practicalLights.Add(
                CreatePracticalLight(
                    "Home Bathroom Practical Light",
                    bathroomLightPosition,
                    new Color(0.52f, 0.68f, 0.72f),
                    2.20f,
                    4.0f));
            WindowLight = CreateWindowLight();
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
            light.color =
                new Color(0.43f, 0.58f, 0.84f);
            light.intensity = 5.25f;
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
            color.postExposure.Override(-0.08f);
            color.contrast.Override(7f);
            color.saturation.Override(-16f);
            color.colorFilter.Override(
                new Color(0.88f, 0.84f, 0.72f, 1f));

            Vignette vignette = runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.52f);

            FilmGrain grain = runtimeProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.14f);
            grain.response.Override(0.74f);
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
