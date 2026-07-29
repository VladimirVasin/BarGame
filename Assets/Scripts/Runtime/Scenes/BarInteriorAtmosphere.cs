using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public readonly struct BarPracticalLightSpec
    {
        public BarPracticalLightSpec(
            string name,
            Vector3 position,
            Color color,
            LightType type,
            float intensity,
            float range,
            float spotAngle = 90f)
            : this(
                name,
                position,
                Vector3.down,
                color,
                type,
                intensity,
                range,
                spotAngle)
        {
        }

        public BarPracticalLightSpec(
            string name,
            Vector3 position,
            Vector3 direction,
            Color color,
            LightType type,
            float intensity,
            float range,
            float spotAngle = 90f)
        {
            if (type != LightType.Point && type != LightType.Spot)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(type),
                    "Bar practical lights must be point or spot lights.");
            }

            Name = string.IsNullOrWhiteSpace(name)
                ? "Bar Practical Light"
                : name;
            Position = position;
            Direction = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.down;
            Color = color;
            Type = type;
            Intensity = Mathf.Max(0f, intensity);
            Range = Mathf.Max(0.1f, range);
            SpotAngle = Mathf.Clamp(spotAngle, 1f, 179f);
        }

        public string Name { get; }
        public Vector3 Position { get; }
        public Vector3 Direction { get; }
        public Color Color { get; }
        public LightType Type { get; }
        public float Intensity { get; }
        public float Range { get; }
        public float SpotAngle { get; }
    }

    [DisallowMultipleComponent]
    public sealed class BarInteriorAtmosphere : MonoBehaviour
    {
        public const int MaximumPracticalLights = 6;
        public const int MaximumDustParticles = 24;

        private readonly List<Light> practicalLights =
            new List<Light>(MaximumPracticalLights);
        private ReadOnlyCollection<Light> readOnlyPracticalLights;
        private VolumeProfile runtimeProfile;

        public IReadOnlyList<Light> PracticalLights =>
            readOnlyPracticalLights ??
            (readOnlyPracticalLights = practicalLights.AsReadOnly());
        public Volume PostProcessVolume { get; private set; }
        public ParticleSystem Dust { get; private set; }

        public void Initialize(
            IReadOnlyList<BarPracticalLightSpec> lightSpecs)
        {
            if (lightSpecs == null)
            {
                throw new ArgumentNullException(nameof(lightSpecs));
            }

            if (lightSpecs.Count > MaximumPracticalLights)
            {
                throw new ArgumentException(
                    $"A bar may use at most {MaximumPracticalLights} " +
                    "realtime practical lights.",
                    nameof(lightSpecs));
            }

            if (practicalLights.Count > 0 || PostProcessVolume != null)
            {
                throw new InvalidOperationException(
                    "The bar atmosphere is already initialized.");
            }

            for (int index = 0; index < lightSpecs.Count; index++)
            {
                practicalLights.Add(CreateLight(lightSpecs[index]));
            }

            CreatePostProcessVolume();
            CreateDust();
        }

        private Light CreateLight(BarPracticalLightSpec spec)
        {
            GameObject lightObject = new GameObject(spec.Name);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = spec.Position;

            Light light = lightObject.AddComponent<Light>();
            light.type = spec.Type;
            light.color = spec.Color;
            light.intensity = spec.Intensity;
            light.range = spec.Range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.12f;
            if (spec.Type == LightType.Spot)
            {
                light.spotAngle = spec.SpotAngle;
                light.innerSpotAngle = spec.SpotAngle * 0.55f;
                Vector3 rotationUp =
                    Mathf.Abs(Vector3.Dot(
                        spec.Direction,
                        Vector3.up)) > 0.98f
                        ? Vector3.forward
                        : Vector3.up;
                light.transform.localRotation = Quaternion.LookRotation(
                    spec.Direction,
                    rotationUp);
            }

            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Bar Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume = volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Runtime Bar Interior Grade";
            runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = runtimeProfile;

            Bloom bloom = runtimeProfile.Add<Bloom>(true);
            bloom.intensity.Override(0.22f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.56f);

            ColorAdjustments color =
                runtimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(-0.05f);
            color.contrast.Override(9f);
            color.saturation.Override(-4f);
            color.colorFilter.Override(
                new Color(1f, 0.94f, 0.88f, 1f));

            Vignette vignette = runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.17f);
            vignette.smoothness.Override(0.45f);

            FilmGrain grain = runtimeProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            grain.response.Override(0.72f);
        }

        private void CreateDust()
        {
            GameObject dustObject = new GameObject(
                "Bar Local Dust");
            dustObject.transform.SetParent(transform, false);
            dustObject.transform.localPosition =
                new Vector3(0f, 2.45f, 1.6f);
            Dust = dustObject.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = Dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaximumDustParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.045f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.72f, 0.42f, 0.028f),
                new Color(0.40f, 0.68f, 0.78f, 0.018f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = Dust.emission;
            emission.rateOverTime = 1.35f;

            ParticleSystem.ShapeModule shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(16f, 2.6f, 10f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                Dust.velocityOverLifetime;
            velocity.enabled = true;
            velocity.y = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

            ParticleSystemRenderer renderer =
                Dust.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.12f;
            Dust.Play();
        }

        private void OnDestroy()
        {
            if (runtimeProfile == null)
            {
                return;
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
        }
    }
}
