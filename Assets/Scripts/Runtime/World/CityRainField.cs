using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Maintains a scene-local field of falling rain streaks around a follow
    /// target. Intensity is continuous so the deterministic weather schedule
    /// can fade between clear, light and heavy rain without pops.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityRainField : MonoBehaviour
    {
        public const int MaximumParticles = 420;
        public const float MaximumEmissionRate = 320f;
        public const float FieldExtent = 26f;
        public const float FieldHeight = 12f;

        /// <summary>
        /// Radius of the rain-free core used while the hero rides inside the
        /// bus, so streaks never spawn through the cabin roof.
        /// </summary>
        public const float ShelterHoleRadius = 10f;

        /// <summary>
        /// Streak drift spans this range of the wind velocity, so rain
        /// and cloth agree on one wind without every drop matching it
        /// exactly.
        /// </summary>
        public const float DriftScaleMin = 0.55f;

        public const float DriftScaleMax = 1.05f;

        private const string ParticleObjectName = "City Rain Particles";
        private const float InitialFillSeconds = 2.5f;
        private const float MinimumVisibleIntensity = 0.005f;
        private const float DriftChangeThreshold = 0.05f;
        private const float DriftJitter = 0.2f;
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        [SerializeField] private ParticleSystem particles;
        [SerializeField] private ParticleSystemRenderer rainRenderer;

        private Transform followTarget;
        private float appliedIntensity = -1f;
        private bool appliedSheltered;
        private Vector2 appliedWindDrift;

        public bool IsInitialized { get; private set; }
        public Transform FollowTarget => followTarget;
        public ParticleSystem Particles => particles;
        public ParticleSystemRenderer RainRenderer => rainRenderer;
        public float Intensity =>
            appliedIntensity < 0f ? 0f : appliedIntensity;
        public bool IsSheltered => appliedSheltered;
        public Vector2 AppliedWindDrift => appliedWindDrift;

        public void Initialize(
            Transform target,
            Material rainMaterial,
            int seed,
            float initialIntensity = 0f)
        {
            followTarget = target != null
                ? target
                : throw new ArgumentNullException(nameof(target));
            if (rainMaterial == null)
            {
                throw new ArgumentNullException(nameof(rainMaterial));
            }

            EnsureParticleSystem();
            PositionEmitter();
            ConfigureParticleSystem(
                rainMaterial,
                seed,
                Mathf.Clamp01(initialIntensity));
            IsInitialized = true;
        }

        public void SetIntensity(float intensity)
        {
            if (!IsInitialized)
            {
                return;
            }

            float clamped = Mathf.Clamp01(intensity);
            if (clamped.Equals(appliedIntensity))
            {
                return;
            }

            appliedIntensity = clamped;
            ApplyIntensity(clamped);
        }

        /// <summary>
        /// Aligns the streak drift with the horizontal wind velocity
        /// (meters per second, XZ).
        /// </summary>
        public void SetWindDrift(Vector2 horizontalMetersPerSecond)
        {
            if (!IsInitialized)
            {
                return;
            }

            if ((horizontalMetersPerSecond - appliedWindDrift)
                .magnitude < DriftChangeThreshold)
            {
                return;
            }

            appliedWindDrift = horizontalMetersPerSecond;
            ApplyWindDrift(horizontalMetersPerSecond);
        }

        public void SetSheltered(bool sheltered)
        {
            if (!IsInitialized || sheltered == appliedSheltered)
            {
                return;
            }

            appliedSheltered = sheltered;
            ApplyShape(sheltered);
        }

        private void LateUpdate()
        {
            if (!IsInitialized ||
                followTarget == null ||
                particles == null)
            {
                return;
            }

            PositionEmitter();
        }

        private void EnsureParticleSystem()
        {
            if (particles == null)
            {
                GameObject particleObject =
                    new GameObject(ParticleObjectName);
                particleObject.transform.SetParent(transform, false);
                particles = particleObject.AddComponent<ParticleSystem>();
            }

            if (rainRenderer == null)
            {
                rainRenderer =
                    particles.GetComponent<ParticleSystemRenderer>();
            }

            if (rainRenderer == null)
            {
                rainRenderer = particles.gameObject
                    .AddComponent<ParticleSystemRenderer>();
            }
        }

        private void PositionEmitter()
        {
            particles.transform.position = followTarget.position;
            particles.transform.rotation = Quaternion.identity;
            particles.transform.localScale = Vector3.one;
        }

        private void ConfigureParticleSystem(
            Material rainMaterial,
            int seed,
            float initialIntensity)
        {
            particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = CreateRandomSeed(seed);

            ParticleSystem.MainModule main = particles.main;
            main.duration = 10f;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles = MaximumParticles;
            main.startLifetime = 1.1f;
            main.startSpeed = 0f;
            main.startRotation = 0f;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            ApplyShape(false);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(-16.5f, -12.5f);
            appliedWindDrift = Vector2.zero;
            ApplyWindDrift(appliedWindDrift);

            DisableUnusedModules();
            ConfigureRenderer(rainMaterial);
            ApplyIntensity(initialIntensity);
            appliedIntensity = initialIntensity;

            particles.Simulate(InitialFillSeconds, true, true, true);
            particles.Play(true);
        }

        private void ApplyWindDrift(Vector2 wind)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.x = CreateDriftCurve(wind.x);
            velocity.z = CreateDriftCurve(wind.y);
        }

        private static ParticleSystem.MinMaxCurve CreateDriftCurve(
            float windComponent)
        {
            float near = windComponent * DriftScaleMin;
            float far = windComponent * DriftScaleMax;
            return new ParticleSystem.MinMaxCurve(
                Mathf.Min(near, far) - DriftJitter,
                Mathf.Max(near, far) + DriftJitter);
        }

        private void ApplyShape(bool sheltered)
        {
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            if (sheltered)
            {
                shape.shapeType = ParticleSystemShapeType.Donut;
                shape.radius = FieldExtent * 0.5f;
                shape.donutRadius =
                    FieldExtent * 0.5f - ShelterHoleRadius;
                shape.position = new Vector3(0f, FieldHeight, 0f);
                shape.rotation = new Vector3(90f, 0f, 0f);
                shape.scale = Vector3.one;
                return;
            }

            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = new Vector3(0f, FieldHeight, 0f);
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(FieldExtent, 0.5f, FieldExtent);
        }

        private void ApplyIntensity(float intensity)
        {
            ParticleSystem.EmissionModule emission = particles.emission;
            if (intensity <= MinimumVisibleIntensity)
            {
                emission.rateOverTime = 0f;
                return;
            }

            emission.rateOverTime =
                MaximumEmissionRate * Mathf.Pow(intensity, 1.35f);

            ParticleSystem.MainModule main = particles.main;
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(0.013f, 0.018f, intensity),
                Mathf.Lerp(0.018f, 0.028f, intensity));
            main.startColor = new Color(
                0.78f,
                0.84f,
                0.90f,
                Mathf.Lerp(0.10f, 0.16f, intensity));
            rainRenderer.velocityScale =
                Mathf.Lerp(0.018f, 0.030f, intensity);
        }

        private void DisableUnusedModules()
        {
            ParticleSystem.CollisionModule collision =
                particles.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = particles.lights;
            lights.enabled = false;
            ParticleSystem.TriggerModule trigger = particles.trigger;
            trigger.enabled = false;
            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = false;
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = false;
            ParticleSystem.ColorOverLifetimeModule color =
                particles.colorOverLifetime;
            color.enabled = false;
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

        private void ConfigureRenderer(Material rainMaterial)
        {
            rainRenderer.sharedMaterial = rainMaterial;
            rainRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            rainRenderer.lengthScale = 0f;
            rainRenderer.velocityScale = 0.024f;
            rainRenderer.cameraVelocityScale = 0f;
            rainRenderer.sortMode = ParticleSystemSortMode.None;
            rainRenderer.minParticleSize = 0f;
            rainRenderer.maxParticleSize = 0.5f;
            rainRenderer.enableGPUInstancing = true;
            rainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            rainRenderer.receiveShadows = false;
            rainRenderer.lightProbeUsage = LightProbeUsage.Off;
            rainRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            rainRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            rainRenderer.allowOcclusionWhenDynamic = true;

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(EdgePowerId, 1.15f);
            properties.SetFloat(NoiseStrengthId, 0f);
            properties.SetFloat(SoftParticleDistanceId, 0.45f);
            rainRenderer.SetPropertyBlock(properties);
        }

        private static uint CreateRandomSeed(int seed)
        {
            uint value = unchecked((uint)seed) ^ 0x5241494Eu;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value == 0u ? 0x52414942u : value;
        }
    }
}
