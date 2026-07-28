using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Maintains a small, scene-local field of drifting fog around the player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityFogField : MonoBehaviour
    {
        public const int MaximumParticles = 36;

        private const string ParticleObjectName = "City Fog Particles";
        private const float InitialFillSeconds = 12f;
        private const float FollowHeight = 0.35f;
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        [SerializeField] private ParticleSystem particles;
        [SerializeField] private ParticleSystemRenderer fogRenderer;

        private Transform player;
        private Transform particleTransform;

        public bool IsInitialized { get; private set; }
        public Transform Player => player;
        public ParticleSystem Particles => particles;
        public ParticleSystemRenderer FogRenderer => fogRenderer;

        public void Initialize(
            Transform playerTransform,
            Material fogMaterial,
            int seed)
        {
            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            if (fogMaterial == null)
            {
                throw new ArgumentNullException(nameof(fogMaterial));
            }

            EnsureParticleSystem();
            PositionEmitter();
            ConfigureParticleSystem(fogMaterial, seed);
            IsInitialized = true;
        }

        private void LateUpdate()
        {
            if (!IsInitialized || player == null || particleTransform == null)
            {
                return;
            }

            PositionEmitter();
        }

        private void EnsureParticleSystem()
        {
            if (particles == null)
            {
                GameObject particleObject = new GameObject(ParticleObjectName);
                particleTransform = particleObject.transform;
                particleTransform.SetParent(transform, false);
                particles = particleObject.AddComponent<ParticleSystem>();
            }
            else
            {
                particleTransform = particles.transform;
            }

            if (fogRenderer == null)
            {
                fogRenderer = particles.GetComponent<ParticleSystemRenderer>();
            }

            if (fogRenderer == null)
            {
                fogRenderer =
                    particles.gameObject.AddComponent<ParticleSystemRenderer>();
            }
        }

        private void PositionEmitter()
        {
            Vector3 position = player.position;
            position.y += FollowHeight;
            particleTransform.position = position;
            particleTransform.rotation = Quaternion.identity;
            particleTransform.localScale = Vector3.one;
        }

        private void ConfigureParticleSystem(Material fogMaterial, int seed)
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = CreateRandomSeed(seed);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 18f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles = MaximumParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(7.0f, 12.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 2.8f;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, 1.4f, 0f);
            shape.scale = new Vector3(30f, 4.5f, 30f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.008f, 0.014f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.025f, 0.025f);

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.008f, 0.025f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            noise.frequency = 0.08f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.02f);

            ParticleSystem.ColorOverLifetimeModule color =
                particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateVisibilityGradient());

            DisableUnusedModules();
            ConfigureRenderer(fogMaterial);

            particles.Simulate(InitialFillSeconds, true, true, true);
            particles.Play(true);
        }

        private void DisableUnusedModules()
        {
            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = particles.lights;
            lights.enabled = false;
            ParticleSystem.TriggerModule trigger = particles.trigger;
            trigger.enabled = false;
            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                particles.externalForces;
            externalForces.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters =
                particles.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.TextureSheetAnimationModule textureSheet =
                particles.textureSheetAnimation;
            textureSheet.enabled = false;
        }

        private void ConfigureRenderer(Material fogMaterial)
        {
            fogRenderer.sharedMaterial = fogMaterial;
            fogRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            fogRenderer.alignment = ParticleSystemRenderSpace.View;
            fogRenderer.sortMode = ParticleSystemSortMode.Distance;
            fogRenderer.minParticleSize = 0.015f;
            fogRenderer.maxParticleSize = 0.18f;
            fogRenderer.enableGPUInstancing = true;
            fogRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fogRenderer.receiveShadows = false;
            fogRenderer.lightProbeUsage = LightProbeUsage.Off;
            fogRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            fogRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            fogRenderer.allowOcclusionWhenDynamic = true;

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(EdgePowerId, 1.45f);
            properties.SetFloat(NoiseStrengthId, 0.58f);
            properties.SetFloat(SoftParticleDistanceId, 1.10f);
            fogRenderer.SetPropertyBlock(properties);
        }

        private static Gradient CreateVisibilityGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(
                        new Color(0.82f, 0.88f, 0.84f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.080f, 0.18f),
                    new GradientAlphaKey(0.120f, 0.62f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static uint CreateRandomSeed(int seed)
        {
            uint value = unchecked((uint)seed) ^ 0x464F4721u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value == 0u ? 0xA341316Cu : value;
        }
    }
}
