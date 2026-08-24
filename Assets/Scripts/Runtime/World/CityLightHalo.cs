using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Draws a two-layer, depth-tested fog halo around one light source.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class CityLightHalo : MonoBehaviour
    {
        public const int ParticleCount = 2;

        private const float ParticleLifetime = 100000f;
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        [SerializeField] private ParticleSystem particles;
        [SerializeField] private ParticleSystemRenderer haloRenderer;

        private readonly ParticleSystem.Particle[] haloParticles =
            new ParticleSystem.Particle[ParticleCount];
        private float innerSize;
        private float outerSize;
        private Color innerColor;
        private Color outerColor;
        private float intensityFactor = 1f;

        public ParticleSystem Particles => particles;
        public ParticleSystemRenderer HaloRenderer => haloRenderer;
        public bool IsVisible =>
            haloRenderer != null && haloRenderer.enabled;
        public float IntensityFactor => intensityFactor;

        /// <summary>
        /// Builds a fog halo with no Light of its own and hands it to
        /// <see cref="CityNightGlowRegistry"/> so it follows the night
        /// factor: dead by day, full at night. This is how ordinary
        /// night-gated fixed lamps stay visible at a distance — the emissive
        /// lens is a couple of pixels the fog swallows by twenty
        /// metres, where the halo billboard is the blurred ball of
        /// light a lamp actually is in fog. The pooled realtime lights
        /// carry light only; the blurred presence is the fixture's own.
        /// Always-burning fixtures initialize a halo directly and therefore
        /// deliberately stay outside this night registry.
        /// </summary>
        public static CityLightHalo CreateNightRegistered(
            Transform parent,
            Vector3 localPosition,
            float innerSize,
            float outerSize,
            Color innerColor,
            Color outerColor)
        {
            var haloObject = new GameObject("Fog Light Halo");
            haloObject.transform.SetParent(parent, false);
            haloObject.transform.localPosition = localPosition;
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                innerSize,
                outerSize,
                innerColor,
                outerColor);
            CityNightGlowRegistry.RegisterHalo(halo);
            return halo;
        }

        public void Initialize(
            Material sharedMaterial,
            float innerSize,
            float outerSize,
            Color innerColor,
            Color outerColor)
        {
            if (sharedMaterial == null)
            {
                throw new ArgumentNullException(nameof(sharedMaterial));
            }

            if (innerSize <= 0f || outerSize <= innerSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outerSize),
                    "The outer halo must be larger than the inner halo.");
            }

            EnsureComponents();
            ConfigureParticleSystem();
            ConfigureRenderer(sharedMaterial);
            SetAppearance(
                innerSize,
                outerSize,
                innerColor,
                outerColor);
            SetVisible(intensityFactor > 0.0001f);
        }

        public void SetAppearance(
            float innerSize,
            float outerSize,
            Color innerColor,
            Color outerColor)
        {
            if (innerSize <= 0f || outerSize <= innerSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outerSize),
                    "The outer halo must be larger than the inner halo.");
            }

            bool appearanceChanged =
                !Mathf.Approximately(this.innerSize, innerSize) ||
                !Mathf.Approximately(this.outerSize, outerSize) ||
                !this.innerColor.Equals(innerColor) ||
                !this.outerColor.Equals(outerColor);
            this.innerSize = innerSize;
            this.outerSize = outerSize;
            this.innerColor = innerColor;
            this.outerColor = outerColor;
            if (appearanceChanged &&
                particles != null &&
                haloRenderer != null)
            {
                ApplyIntensity();
            }
        }

        public void SetIntensityFactor(float factor)
        {
            float clampedFactor = Mathf.Clamp01(factor);
            bool intensityChanged =
                !Mathf.Approximately(
                    intensityFactor,
                    clampedFactor);
            intensityFactor = clampedFactor;
            if (particles == null || haloRenderer == null)
            {
                return;
            }

            if (intensityChanged)
            {
                ApplyIntensity();
            }

            haloRenderer.enabled = intensityFactor > 0.0001f;
        }

        public void SetVisible(bool visible)
        {
            if (haloRenderer != null)
            {
                haloRenderer.enabled = visible;
            }
        }

        private void EnsureComponents()
        {
            particles = particles != null
                ? particles
                : GetComponent<ParticleSystem>();
            haloRenderer = haloRenderer != null
                ? haloRenderer
                : GetComponent<ParticleSystemRenderer>();
        }

        private void ConfigureParticleSystem()
        {
            particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = 0x48414C4Fu;

            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = ParticleCount;
            main.startLifetime = ParticleLifetime;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;
            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = particles.lights;
            lights.enabled = false;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = false;
            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = false;
            ParticleSystem.TriggerModule trigger = particles.trigger;
            trigger.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters =
                particles.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.TextureSheetAnimationModule textureSheet =
                particles.textureSheetAnimation;
            textureSheet.enabled = false;
        }

        private void ConfigureRenderer(Material sharedMaterial)
        {
            haloRenderer.sharedMaterial = sharedMaterial;
            haloRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            haloRenderer.alignment = ParticleSystemRenderSpace.View;
            haloRenderer.sortMode = ParticleSystemSortMode.Distance;
            haloRenderer.minParticleSize = 0f;
            haloRenderer.maxParticleSize = 0.2f;
            haloRenderer.enableGPUInstancing = true;
            haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            haloRenderer.receiveShadows = false;
            haloRenderer.lightProbeUsage = LightProbeUsage.Off;
            haloRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            haloRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(EdgePowerId, 1.8f);
            properties.SetFloat(NoiseStrengthId, 0.06f);
            properties.SetFloat(SoftParticleDistanceId, 0.42f);
            haloRenderer.SetPropertyBlock(properties);
        }

        private void SetHaloParticles(
            float innerSize,
            float outerSize,
            Color innerColor,
            Color outerColor)
        {
            haloParticles[0] = CreateParticle(
                outerSize,
                outerColor,
                1u);
            haloParticles[1] = CreateParticle(
                innerSize,
                innerColor,
                2u);

            particles.Play(false);
            particles.SetParticles(haloParticles, ParticleCount);
        }

        private void ApplyIntensity()
        {
            SetHaloParticles(
                innerSize,
                outerSize,
                ScaleColor(innerColor, intensityFactor),
                ScaleColor(outerColor, intensityFactor));
        }

        private static Color ScaleColor(Color color, float factor)
        {
            return new Color(
                color.r * factor,
                color.g * factor,
                color.b * factor,
                color.a * factor);
        }

        private static ParticleSystem.Particle CreateParticle(
            float size,
            Color color,
            uint randomSeed)
        {
            return new ParticleSystem.Particle
            {
                position = Vector3.zero,
                startSize = size,
                startColor = color,
                startLifetime = ParticleLifetime,
                remainingLifetime = ParticleLifetime,
                randomSeed = randomSeed
            };
        }
    }
}
