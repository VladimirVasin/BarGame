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
    public sealed class StairwellInteriorAtmosphere : MonoBehaviour
    {
        public const int MaximumPracticalLights = 3;
        public const int MaximumDustParticles = 14;

        internal static readonly Vector3 GroundEmitterPosition =
            new Vector3(-1.45f, 2.70f, -3.40f);
        internal static readonly Vector3 MiddleEmitterPosition =
            new Vector3(0f, 5.25f, 1.48f);
        internal static readonly Vector3 ApartmentEmitterPosition =
            new Vector3(2.48f, 5.78f, -3.20f);

        private readonly List<Light> practicalLights =
            new List<Light>(MaximumPracticalLights);
        private ReadOnlyCollection<Light> readOnlyPracticalLights;
        private VolumeProfile runtimeProfile;

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<Light> PracticalLights =>
            readOnlyPracticalLights ??
            (readOnlyPracticalLights =
                practicalLights.AsReadOnly());
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile => runtimeProfile;
        public ParticleSystem Dust { get; private set; }

        public void Initialize()
        {
            if (IsInitialized ||
                practicalLights.Count > 0 ||
                PostProcessVolume != null)
            {
                throw new InvalidOperationException(
                    "The stairwell atmosphere is already initialized.");
            }

            practicalLights.Add(
                CreatePracticalLight(
                    "Ground Entrance Light",
                    GroundEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.50f, 0.70f, 0.55f),
                    4.80f,
                    6.2f,
                    0.2f,
                    false));
            practicalLights.Add(
                CreatePracticalLight(
                    "Middle Flickering Light",
                    MiddleEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.46f, 0.67f, 0.59f),
                    7.40f,
                    8.4f,
                    1.7f,
                    true));
            practicalLights.Add(
                CreatePracticalLight(
                    "Apartment Landing Light",
                    ApartmentEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.82f, 0.58f, 0.29f),
                    4.60f,
                    6.0f,
                    3.1f,
                    false));
            CreatePostProcessVolume();
            CreateDust();
            IsInitialized = true;
        }

        private Light CreatePracticalLight(
            string lightName,
            Vector3 position,
            Color color,
            float intensity,
            float range,
            float flickerPhase,
            bool strongerFlicker)
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
            light.bounceIntensity = 0.06f;
            StairwellLightFlicker flicker =
                lightObject.AddComponent<StairwellLightFlicker>();
            flicker.Initialize(
                intensity,
                flickerPhase,
                strongerFlicker);
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject =
                new GameObject("Stairwell Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume =
                volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            runtimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name =
                "Runtime Stairwell Interior Grade";
            runtimeProfile.hideFlags =
                HideFlags.HideAndDontSave;
            PostProcessVolume.profile = runtimeProfile;

            Bloom bloom = runtimeProfile.Add<Bloom>(true);
            bloom.intensity.Override(0.18f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.48f);

            ColorAdjustments color =
                runtimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.12f);
            color.contrast.Override(12f);
            color.saturation.Override(-28f);
            color.colorFilter.Override(
                new Color(0.72f, 0.82f, 0.71f, 1f));

            Vignette vignette =
                runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.31f);
            vignette.smoothness.Override(0.56f);

            FilmGrain grain =
                runtimeProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(0.19f);
            grain.response.Override(0.78f);
        }

        private void CreateDust()
        {
            GameObject dustObject =
                new GameObject("Stairwell Damp Dust");
            dustObject.transform.SetParent(transform, false);
            dustObject.transform.localPosition =
                new Vector3(0f, 3.0f, -0.4f);
            Dust = dustObject.AddComponent<ParticleSystem>();
            Dust.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            Dust.useAutoRandomSeed = false;
            Dust.randomSeed = 0x53544149u;

            ParticleSystem.MainModule main = Dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaximumDustParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(13f, 21f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.006f, 0.021f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.035f, 0.105f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.46f, 0.61f, 0.52f, 0.022f),
                new Color(0.72f, 0.58f, 0.38f, 0.012f));
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = Dust.emission;
            emission.rateOverTime = 0.42f;

            ParticleSystem.ShapeModule shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7.4f, 5.2f, 7.8f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                Dust.velocityOverLifetime;
            velocity.enabled = false;
            velocity.x =
                new ParticleSystem.MinMaxCurve(-0.006f, 0.006f);
            velocity.y =
                new ParticleSystem.MinMaxCurve(0.004f, 0.014f);
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
            renderer.maxParticleSize = 0.07f;
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
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class StairwellLightFlicker : MonoBehaviour
    {
        private Light controlledLight;
        private float baseIntensity;
        private float phase;
        private float flickerStrength;

        public float CurrentFactor { get; private set; } = 1f;

        public void Initialize(
            float intensity,
            float phaseOffset,
            bool strongerFlicker)
        {
            controlledLight = GetComponent<Light>();
            baseIntensity = Mathf.Max(0f, intensity);
            phase = phaseOffset;
            flickerStrength = strongerFlicker ? 0.18f : 0.07f;
            Apply(1f);
        }

        private void Update()
        {
            if (controlledLight == null)
            {
                return;
            }

            float time = Time.unscaledTime + phase;
            float wobble =
                Mathf.Sin(time * 7.3f) * 0.45f +
                Mathf.Sin(time * 17.9f + 0.8f) * 0.32f +
                Mathf.Sin(time * 31.1f + 2.1f) * 0.23f;
            float dipSignal =
                Mathf.Sin(time * 2.3f + phase * 1.7f);
            float dip = dipSignal > 0.94f
                ? (dipSignal - 0.94f) / 0.06f
                : 0f;
            float factor =
                1f +
                wobble * flickerStrength -
                dip * flickerStrength * 0.65f;
            Apply(Mathf.Clamp(factor, 0.72f, 1.12f));
        }

        private void Apply(float factor)
        {
            CurrentFactor = factor;
            if (controlledLight != null)
            {
                controlledLight.intensity =
                    baseIntensity * CurrentFactor;
            }
        }
    }
}
