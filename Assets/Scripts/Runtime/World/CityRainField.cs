using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public enum CityPrecipitationKind
    {
        Rain = 0,
        Snow = 1
    }

    /// <summary>
    /// Everything that differs between the two things the sky drops.
    ///
    /// The field around them is identical — same follow, same box, same
    /// sheltered donut, same continuous intensity, same shared atmosphere
    /// material — so the kind is a table rather than a second component.
    /// Rain's numbers are the ones the constants on
    /// <see cref="CityRainField"/> still name, because the city is the
    /// default and its contracts are asserted against those constants.
    /// </summary>
    public readonly struct CityPrecipitationProfile
    {
        private CityPrecipitationProfile(
            int maximumParticles,
            float maximumEmissionRate,
            float emissionExponent,
            float lifetimeSeconds,
            float prewarmSeconds,
            Vector2 fallSpeedRange,
            Vector2 quietSizeRange,
            Vector2 heavySizeRange,
            Color tint,
            Vector2 alphaRange,
            bool stretched,
            Vector2 velocityScaleRange,
            Vector2 driftScaleRange,
            float driftJitter,
            float spinDegreesPerSecond,
            Vector3 turbulence,
            float edgePower,
            float shaderNoiseStrength,
            float softParticleDistance)
        {
            MaximumParticles = maximumParticles;
            MaximumEmissionRate = maximumEmissionRate;
            EmissionExponent = emissionExponent;
            LifetimeSeconds = lifetimeSeconds;
            PrewarmSeconds = prewarmSeconds;
            FallSpeedRange = fallSpeedRange;
            QuietSizeRange = quietSizeRange;
            HeavySizeRange = heavySizeRange;
            Tint = tint;
            AlphaRange = alphaRange;
            Stretched = stretched;
            VelocityScaleRange = velocityScaleRange;
            DriftScaleRange = driftScaleRange;
            DriftJitter = driftJitter;
            SpinDegreesPerSecond = spinDegreesPerSecond;
            Turbulence = turbulence;
            EdgePower = edgePower;
            ShaderNoiseStrength = shaderNoiseStrength;
            SoftParticleDistance = softParticleDistance;
        }

        public int MaximumParticles { get; }
        public float MaximumEmissionRate { get; }
        public float EmissionExponent { get; }
        public float LifetimeSeconds { get; }
        public float PrewarmSeconds { get; }
        public Vector2 FallSpeedRange { get; }
        public Vector2 QuietSizeRange { get; }
        public Vector2 HeavySizeRange { get; }
        public Color Tint { get; }
        public Vector2 AlphaRange { get; }
        public bool Stretched { get; }
        public Vector2 VelocityScaleRange { get; }
        public Vector2 DriftScaleRange { get; }
        public float DriftJitter { get; }
        public float SpinDegreesPerSecond { get; }

        /// <summary>Strength, frequency and scroll of the swirl; zero
        /// strength leaves the noise module off entirely.</summary>
        public Vector3 Turbulence { get; }

        public float EdgePower { get; }
        public float ShaderNoiseStrength { get; }
        public float SoftParticleDistance { get; }

        public static CityPrecipitationProfile Rain { get; } =
            new CityPrecipitationProfile(
                CityRainField.MaximumParticles,
                CityRainField.MaximumEmissionRate,
                1.35f,
                1.1f,
                2.5f,
                new Vector2(-16.5f, -12.5f),
                new Vector2(0.013f, 0.018f),
                new Vector2(0.018f, 0.028f),
                new Color(0.78f, 0.84f, 0.90f, 1f),
                new Vector2(0.10f, 0.16f),
                true,
                new Vector2(0.018f, 0.030f),
                new Vector2(
                    CityRainField.DriftScaleMin,
                    CityRainField.DriftScaleMax),
                0.2f,
                0f,
                Vector3.zero,
                1.15f,
                0f,
                0.45f);

        /// <summary>
        /// The mountain road, where the same schedule falls frozen.
        ///
        /// Almost every number is a consequence of one of them: a flake
        /// settles at about a metre a second, so it needs ten times the
        /// lifetime to cross the same twelve metres, so it needs the same
        /// factor more particles alive to look like weather, so the rate has
        /// to come down to keep the count under the cap. It is a billboard
        /// rather than a streak because a flake has no velocity smear, which
        /// then forces a real size — the streak's two centimetres were only
        /// ever legible because stretching drew them longer.
        /// </summary>
        public static CityPrecipitationProfile Snow { get; } =
            new CityPrecipitationProfile(
                760,
                58f,
                1.15f,
                12f,
                12f,
                new Vector2(-1.45f, -0.95f),
                new Vector2(0.030f, 0.052f),
                new Vector2(0.048f, 0.075f),
                new Color(0.90f, 0.93f, 0.97f, 1f),
                new Vector2(0.32f, 0.55f),
                false,
                new Vector2(0f, 0f),
                new Vector2(0.85f, 1.55f),
                0.28f,
                35f,
                new Vector3(0.35f, 0.22f, 0.12f),
                1.05f,
                0.25f,
                0.45f);

        public static CityPrecipitationProfile For(
            CityPrecipitationKind kind)
        {
            return kind == CityPrecipitationKind.Snow ? Snow : Rain;
        }
    }

    /// <summary>
    /// Maintains a scene-local field of falling precipitation around a follow
    /// target. Intensity is continuous so the deterministic weather schedule
    /// can fade between clear, light and heavy without pops.
    ///
    /// It carries whatever the sky is dropping: the city gets rain, the
    /// mountain road gets the same schedule as snow. The name is the city's
    /// because the city is the default and its contracts are written against
    /// these constants; the kind is chosen at initialization.
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
        /// bus, so streaks never spawn through the cabin roof. It hugs the
        /// body: the 8.25 x 2.38 m bus has a 4.3 m half-diagonal, and the
        /// rest is margin for wind drift during the fall. The old 10 m core
        /// pushed every streak past the fog's teeth and the ride read as
        /// dry — the passenger judges the weather by what stands right
        /// outside the glass.
        /// </summary>
        public const float ShelterHoleRadius = 6.5f;

        /// <summary>
        /// Streak drift spans this range of the wind velocity, so rain
        /// and cloth agree on one wind without every drop matching it
        /// exactly.
        /// </summary>
        public const float DriftScaleMin = 0.55f;

        public const float DriftScaleMax = 1.05f;

        private const string ParticleObjectName = "City Rain Particles";
        private const float MinimumVisibleIntensity = 0.005f;
        private const float DriftChangeThreshold = 0.05f;
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        [SerializeField] private ParticleSystem particles;
        [SerializeField] private ParticleSystemRenderer rainRenderer;

        private Transform followTarget;
        private CityPrecipitationProfile profile =
            CityPrecipitationProfile.Rain;
        private float appliedIntensity = -1f;
        private bool appliedSheltered;
        private Vector2 appliedWindDrift;

        public bool IsInitialized { get; private set; }
        public CityPrecipitationKind Kind { get; private set; }
        public CityPrecipitationProfile Profile => profile;
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
            float initialIntensity = 0f,
            CityPrecipitationKind kind = CityPrecipitationKind.Rain)
        {
            followTarget = target != null
                ? target
                : throw new ArgumentNullException(nameof(target));
            if (rainMaterial == null)
            {
                throw new ArgumentNullException(nameof(rainMaterial));
            }

            Kind = kind;
            profile = CityPrecipitationProfile.For(kind);
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
            main.maxParticles = profile.MaximumParticles;
            main.startLifetime = profile.LifetimeSeconds;
            main.startSpeed = 0f;
            main.startRotation = profile.SpinDegreesPerSecond > 0f
                ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f)
                : new ParticleSystem.MinMaxCurve(0f);
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
            velocity.y = new ParticleSystem.MinMaxCurve(
                profile.FallSpeedRange.x,
                profile.FallSpeedRange.y);
            appliedWindDrift = Vector2.zero;
            ApplyWindDrift(appliedWindDrift);

            DisableUnusedModules();
            ConfigureSpin();
            ConfigureRenderer(rainMaterial);
            ApplyIntensity(initialIntensity);
            appliedIntensity = initialIntensity;

            // Long enough to fill the whole column. A flake takes ten
            // seconds to fall the field's twelve metres, so a rain-sized
            // prewarm would leave the sky empty for the first third of the
            // climb - which is exactly the tunnel mouth the player arrives
            // through.
            particles.Simulate(profile.PrewarmSeconds, true, true, true);
            particles.Play(true);
        }

        private void ApplyWindDrift(Vector2 wind)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.x = CreateDriftCurve(wind.x);
            velocity.z = CreateDriftCurve(wind.y);
        }

        private ParticleSystem.MinMaxCurve CreateDriftCurve(
            float windComponent)
        {
            float near = windComponent * profile.DriftScaleRange.x;
            float far = windComponent * profile.DriftScaleRange.y;
            return new ParticleSystem.MinMaxCurve(
                Mathf.Min(near, far) - profile.DriftJitter,
                Mathf.Max(near, far) + profile.DriftJitter);
        }

        /// <summary>
        /// Tumble. Without it a field of soft discs reads as drifting dust
        /// rather than as snow; a streak has an axis already and takes none.
        /// </summary>
        private void ConfigureSpin()
        {
            ParticleSystem.RotationOverLifetimeModule rotation =
                particles.rotationOverLifetime;
            if (profile.SpinDegreesPerSecond <= 0f)
            {
                rotation.enabled = false;
                return;
            }

            float radians = profile.SpinDegreesPerSecond * Mathf.Deg2Rad;
            rotation.enabled = true;
            rotation.separateAxes = false;
            rotation.z = new ParticleSystem.MinMaxCurve(
                -radians,
                radians);
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
                profile.MaximumEmissionRate *
                Mathf.Pow(intensity, profile.EmissionExponent);

            ParticleSystem.MainModule main = particles.main;
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(
                    profile.QuietSizeRange.x,
                    profile.HeavySizeRange.x,
                    intensity),
                Mathf.Lerp(
                    profile.QuietSizeRange.y,
                    profile.HeavySizeRange.y,
                    intensity));
            main.startColor = new Color(
                profile.Tint.r,
                profile.Tint.g,
                profile.Tint.b,
                Mathf.Lerp(
                    profile.AlphaRange.x,
                    profile.AlphaRange.y,
                    intensity));
            rainRenderer.velocityScale = Mathf.Lerp(
                profile.VelocityScaleRange.x,
                profile.VelocityScaleRange.y,
                intensity);
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
            // The swirl a flake takes on the way down. Rain falls too fast
            // and lives too briefly for turbulence to show, so it stays off
            // there and this module is the profile's to decide.
            ParticleSystem.NoiseModule noise = particles.noise;
            if (profile.Turbulence.x > 0f)
            {
                noise.enabled = true;
                noise.separateAxes = false;
                noise.quality = ParticleSystemNoiseQuality.Medium;
                noise.octaveCount = 1;
                noise.damping = true;
                noise.strength =
                    new ParticleSystem.MinMaxCurve(profile.Turbulence.x);
                noise.frequency = profile.Turbulence.y;
                noise.scrollSpeed =
                    new ParticleSystem.MinMaxCurve(profile.Turbulence.z);
            }
            else
            {
                noise.enabled = false;
            }

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
            rainRenderer.renderMode = profile.Stretched
                ? ParticleSystemRenderMode.Stretch
                : ParticleSystemRenderMode.Billboard;
            rainRenderer.alignment = ParticleSystemRenderSpace.View;
            rainRenderer.lengthScale = 0f;
            rainRenderer.velocityScale = profile.VelocityScaleRange.x;
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
            properties.SetFloat(EdgePowerId, profile.EdgePower);
            properties.SetFloat(
                NoiseStrengthId,
                profile.ShaderNoiseStrength);
            properties.SetFloat(
                SoftParticleDistanceId,
                profile.SoftParticleDistance);
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
