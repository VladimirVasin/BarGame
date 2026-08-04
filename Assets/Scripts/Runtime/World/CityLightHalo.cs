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
            this.innerSize = innerSize;
            this.outerSize = outerSize;
            this.innerColor = innerColor;
            this.outerColor = outerColor;
            ApplyIntensity();
            SetVisible(intensityFactor > 0.0001f);
        }

        public void SetIntensityFactor(float factor)
        {
            intensityFactor = Mathf.Clamp01(factor);
            if (particles == null || haloRenderer == null)
            {
                return;
            }

            ApplyIntensity();
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
