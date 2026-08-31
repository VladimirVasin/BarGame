using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Presentation-only tuning for the whiteout that closes the untouched
    /// snow beside the village routes. These rules never alter collision,
    /// movement, damage or the global fog owned by <see cref="AlpineVillageRoot"/>.
    /// </summary>
    public static class AlpineVillagePeripheralStormFieldRules
    {
        private static readonly Vector2[] FootprintDirections =
        {
            new Vector2(1f, 0f),
            new Vector2(0.7071068f, 0.7071068f),
            new Vector2(0f, 1f),
            new Vector2(-0.7071068f, 0.7071068f),
            new Vector2(-1f, 0f),
            new Vector2(-0.7071068f, -0.7071068f),
            new Vector2(0f, -1f),
            new Vector2(0.7071068f, -0.7071068f)
        };

        public const int MaximumParticles = 520;
        public const float SpawnBoundsOutset = 8f;
        public const float MinimumEmissionRate = 34f;
        public const float MaximumEmissionRate = 62f;
        public const float MinimumLifetime = 8f;
        public const float MaximumLifetime = 13f;
        public const float MinimumSize = 3.4f;
        public const float MaximumSize = 8.8f;
        public const float MinimumGroundLift = 1.0f;
        public const float MaximumGroundLift = 6.2f;
        public const float MinimumVisibleStrength = 0.045f;
        public const float OffRouteLocalSpawnChance = 0.58f;
        public const float OffRouteLocalSpawnRadius = 16f;

        /// <summary>
        /// Billboards are screen-facing discs, not points. Reserve slightly
        /// more than their half-size around every calm route and aperture so
        /// a centre outside the mask cannot paint across its protected edge.
        /// </summary>
        public const float FootprintRadiusFactor = 0.56f;

        public static float EvaluateEmissionRate(
            float windStrength01,
            float playerStormStrength01,
            bool riding)
        {
            if (riding)
            {
                return 0f;
            }

            float gale = AlpineVillageStormFieldRules.EvaluateGalePulse(
                windStrength01);
            float rate = Mathf.Lerp(
                MinimumEmissionRate,
                MaximumEmissionRate,
                gale);
            return rate * Mathf.Lerp(
                1f,
                1.28f,
                Mathf.Clamp01(playerStormStrength01));
        }

        public static Vector3 EvaluateTransport(in WindSample wind)
        {
            float speed = Mathf.Lerp(
                0.42f,
                1.35f,
                Mathf.Clamp01(wind.Strength01));
            return wind.HorizontalDirection * speed;
        }

        public static float EvaluateOpacity(
            float spatialStrength01,
            float stormWave01,
            float playerStormStrength01,
            float variation01)
        {
            float strength = Mathf.Clamp01(spatialStrength01);
            if (strength < MinimumVisibleStrength)
            {
                return 0f;
            }

            float baseAlpha = Mathf.Lerp(0.055f, 0.245f, strength);
            float gust = Mathf.Lerp(0.90f, 1.42f, Mathf.Clamp01(stormWave01));
            float localPressure = Mathf.Lerp(
                1f,
                1.16f,
                Mathf.Clamp01(playerStormStrength01));
            float variation = Mathf.Lerp(0.82f, 1.12f, variation01);
            return Mathf.Clamp(baseAlpha * gust * localPressure * variation,
                0f,
                0.42f);
        }

        public static float EvaluateSize(
            float spatialStrength01,
            float variation01)
        {
            float strength = Mathf.Sqrt(Mathf.Clamp01(spatialStrength01));
            float size = Mathf.Lerp(MinimumSize, MaximumSize, strength);
            return size * Mathf.Lerp(0.84f, 1.12f, variation01);
        }

        public static float EvaluateFootprintTrailExposure(
            float distanceOutsideTrodden,
            float particleSize)
        {
            float radius = Mathf.Max(0f, particleSize) *
                           FootprintRadiusFactor;
            return AlpineVillagePeripheralStormRules.EvaluateTrailExposure(
                distanceOutsideTrodden - radius);
        }

        public static float EvaluateFootprintRearClosure(
            float metresBehindRearWall,
            float particleSize)
        {
            float radius = Mathf.Max(0f, particleSize) *
                           FootprintRadiusFactor;
            return AlpineVillagePeripheralStormRules.EvaluateRearClosure(
                metresBehindRearWall - radius);
        }

        public static float EvaluateFootprintApertureProtection(
            AlpineVillagePeripheralStormPlan spatialPlan,
            Vector2 point,
            float particleSize)
        {
            if (spatialPlan == null)
            {
                throw new ArgumentNullException(nameof(spatialPlan));
            }

            float radius = Mathf.Max(0f, particleSize) *
                           FootprintRadiusFactor;
            float protection = spatialPlan
                .EvaluateLandmarkApertureProtection(point);
            for (int index = 0;
                 index < FootprintDirections.Length;
                 index++)
            {
                protection = Mathf.Max(
                    protection,
                    spatialPlan.EvaluateLandmarkApertureProtection(
                        point + FootprintDirections[index] * radius));
            }

            return protection;
        }
    }

    /// <summary>
    /// World-anchored soft snow sheets for the village flanks and the space
    /// behind the mother's house. The deterministic spatial plan leaves the
    /// station-to-house landmark aperture and every trodden route open. When
    /// the hero walks into untouched snow, the same field gathers locally,
    /// but it remains visual feedback only: traversal is still unrestricted.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(90)]
    public sealed class AlpineVillagePeripheralStormField : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        private Transform player;
        private AlpineVillagePlan village;
        private CityWeatherController weather;
        private Rect spawnBounds;
        private uint randomState;
        private float emissionCarry;
        private float stormWave;
        private Color hazeColor = Color.white;
        private ParticleSystem.Particle[] liveParticles;
        private MaterialPropertyBlock rendererProperties;

        public bool IsInitialized { get; private set; }
        public ParticleSystem Particles { get; private set; }
        public ParticleSystemRenderer ParticleRenderer { get; private set; }
        public AlpineVillagePeripheralStormPlan SpatialPlan
        {
            get;
            private set;
        }

        public AlpineVillagePeripheralStormSample PlayerSample
        {
            get;
            private set;
        }

        public Vector3 AppliedTransport { get; private set; }
        public float AppliedEmissionRate { get; private set; }
        public float AppliedStormWave => stormWave;

        public void Initialize(
            Transform playerTransform,
            AlpineVillagePlan villagePlan,
            CityWeatherController weatherController,
            Material atmosphereMaterial,
            int seed)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Alpine Village peripheral storm is already initialized.");
            }

            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            village = villagePlan ??
                      throw new ArgumentNullException(nameof(villagePlan));
            weather = weatherController != null
                ? weatherController
                : throw new ArgumentNullException(nameof(weatherController));
            if (atmosphereMaterial == null)
            {
                throw new ArgumentNullException(nameof(atmosphereMaterial));
            }

            SpatialPlan = AlpineVillagePeripheralStormPlan.Create(village);
            spawnBounds = Expand(
                village.TerrainBounds,
                AlpineVillagePeripheralStormFieldRules.SpawnBoundsOutset);
            randomState = CreateRandomSeed(seed);
            PlayerSample = SpatialPlan.Evaluate(player.position);
            ConfigureParticles(atmosphereMaterial);
            ApplyWeather();
            Prewarm();
            RefreshLiveParticles();
            Particles.Play(true);
            IsInitialized = true;
        }

        /// <summary>
        /// Receives the already-applied global haze and the existing gust
        /// wave. It does not write RenderSettings, the camera or weather.
        /// </summary>
        public void SetVisibility(Color appliedHazeColor, float stormWave01)
        {
            hazeColor = appliedHazeColor;
            hazeColor.a = 1f;
            stormWave = Mathf.Clamp01(stormWave01);
            ApplyRendererProperties();
        }

        private void LateUpdate()
        {
            if (!IsInitialized || player == null || Particles == null)
            {
                return;
            }

            PlayerSample = SpatialPlan.Evaluate(player.position);
            ApplyWeather();
            RefreshLiveParticles();

            float delta = Time.unscaledDeltaTime;
            if (delta <= 0f || AppliedEmissionRate <= 0f)
            {
                return;
            }

            emissionCarry += AppliedEmissionRate * delta;
            int count = Mathf.Min(48, Mathf.FloorToInt(emissionCarry));
            emissionCarry -= count;
            Emit(count);
        }

        private void ApplyWeather()
        {
            WindSample wind = weather.CurrentWind;
            AppliedTransport = AlpineVillagePeripheralStormFieldRules
                .EvaluateTransport(wind);
            AppliedEmissionRate = AlpineVillagePeripheralStormFieldRules
                .EvaluateEmissionRate(
                    wind.Strength01,
                    PlayerSample.StormStrength01,
                    GameSessionState.IsRidingAVehicle);
        }

        private void ConfigureParticles(Material material)
        {
            Particles = gameObject.AddComponent<ParticleSystem>();
            ParticleRenderer = GetComponent<ParticleSystemRenderer>();
            Particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            Particles.useAutoRandomSeed = false;
            Particles.randomSeed = randomState;

            ParticleSystem.MainModule main = Particles.main;
            main.duration = 16f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles =
                AlpineVillagePeripheralStormFieldRules.MaximumParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                AlpineVillagePeripheralStormFieldRules.MinimumLifetime,
                AlpineVillagePeripheralStormFieldRules.MaximumLifetime);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(
                AlpineVillagePeripheralStormFieldRules.MinimumSize,
                AlpineVillagePeripheralStormFieldRules.MaximumSize);
            main.startColor = Color.white;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = Particles.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = Particles.shape;
            shape.enabled = false;
            ParticleSystem.VelocityOverLifetimeModule velocity =
                Particles.velocityOverLifetime;
            velocity.enabled = false;

            ParticleSystem.NoiseModule noise = Particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.octaveCount = 1;
            noise.damping = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.18f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.11f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.18f);
            noise.frequency = 0.11f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.16f);

            ParticleSystem.ColorOverLifetimeModule color =
                Particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateLifetimeGradient());

            DisableUnusedModules();
            ConfigureRenderer(material);
        }

        private void Emit(int requestedCount)
        {
            int capacity = Particles.main.maxParticles -
                           Particles.particleCount;
            int target = Mathf.Min(
                requestedCount,
                Mathf.Max(0, capacity));
            int emitted = 0;
            int attempts = 0;
            int maximumAttempts = target * 6;
            while (emitted < target && attempts < maximumAttempts)
            {
                attempts++;
                Vector2 point = PickCandidatePoint();
                if (!spawnBounds.Contains(point) || IsProtectedStructure(point))
                {
                    continue;
                }

                AlpineVillagePeripheralStormSample sample =
                    SpatialPlan.Evaluate(point);
                uint particleSeed = NextRandom();
                float variation = Hash01(particleSeed);
                float nominalSize = AlpineVillagePeripheralStormFieldRules
                    .EvaluateSize(sample.StormStrength01, variation);
                float strength = EvaluateFootprintStrength(
                    point,
                    sample,
                    nominalSize);
                if (strength <
                    AlpineVillagePeripheralStormFieldRules
                        .MinimumVisibleStrength ||
                    Next01() > Mathf.Lerp(0.32f, 1f, strength))
                {
                    continue;
                }

                float ground = AlpineVillageTerrainSampler.SampleHeight(
                    village,
                    point);
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        point.x,
                        ground + Mathf.Lerp(
                            AlpineVillagePeripheralStormFieldRules
                                .MinimumGroundLift,
                            AlpineVillagePeripheralStormFieldRules
                                .MaximumGroundLift,
                            Next01()),
                        point.y),
                    velocity = AppliedTransport +
                               Vector3.up * Mathf.Lerp(-0.035f, 0.06f, Next01()),
                    startLifetime = Mathf.Lerp(
                        AlpineVillagePeripheralStormFieldRules.MinimumLifetime,
                        AlpineVillagePeripheralStormFieldRules.MaximumLifetime,
                        Next01()),
                    startSize = AlpineVillagePeripheralStormFieldRules
                        .EvaluateSize(strength, variation),
                    startColor = EvaluateColor(strength, variation),
                    rotation = Next01() * Mathf.PI * 2f,
                    randomSeed = particleSeed
                };
                Particles.Emit(emit, 1);
                emitted++;
            }
        }

        private Vector2 PickCandidatePoint()
        {
            float localChance =
                AlpineVillagePeripheralStormFieldRules
                    .OffRouteLocalSpawnChance *
                PlayerSample.StormStrength01;
            if (Next01() < localChance)
            {
                float angle = Next01() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(Next01()) *
                    AlpineVillagePeripheralStormFieldRules
                        .OffRouteLocalSpawnRadius;
                Vector3 origin = player.position;
                return new Vector2(
                    origin.x + Mathf.Cos(angle) * radius,
                    origin.z + Mathf.Sin(angle) * radius);
            }

            return new Vector2(
                Mathf.Lerp(spawnBounds.xMin, spawnBounds.xMax, Next01()),
                Mathf.Lerp(spawnBounds.yMin, spawnBounds.yMax, Next01()));
        }

        private void RefreshLiveParticles()
        {
            if (GameSessionState.IsRidingAVehicle)
            {
                Particles.Clear(true);
                emissionCarry = 0f;
                return;
            }

            int count = Particles.particleCount;
            if (count <= 0)
            {
                return;
            }

            EnsureParticleBuffer(count);
            count = Particles.GetParticles(liveParticles);
            int kept = 0;
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = liveParticles[index];
                Vector3 position = particle.position;
                var point = new Vector2(position.x, position.z);
                if (!spawnBounds.Contains(point) || IsProtectedStructure(point))
                {
                    continue;
                }

                AlpineVillagePeripheralStormSample sample =
                    SpatialPlan.Evaluate(point);
                float variation = Hash01(particle.randomSeed);
                float nominalSize = AlpineVillagePeripheralStormFieldRules
                    .EvaluateSize(sample.StormStrength01, variation);
                float strength = EvaluateFootprintStrength(
                    point,
                    sample,
                    nominalSize);
                if (strength <
                    AlpineVillagePeripheralStormFieldRules
                        .MinimumVisibleStrength)
                {
                    continue;
                }

                float ground = AlpineVillageTerrainSampler.SampleHeight(
                    village,
                    point);
                position.y = Mathf.Clamp(
                    position.y,
                    ground + 0.55f,
                    ground +
                    AlpineVillagePeripheralStormFieldRules.MaximumGroundLift +
                    1f);
                particle.position = position;
                particle.velocity = AppliedTransport;
                particle.startSize = AlpineVillagePeripheralStormFieldRules
                    .EvaluateSize(strength, variation);
                particle.startColor = EvaluateColor(strength, variation);
                liveParticles[kept++] = particle;
            }

            Particles.SetParticles(liveParticles, kept);
        }

        private float EvaluateFootprintStrength(
            Vector2 point,
            AlpineVillagePeripheralStormSample center,
            float particleSize)
        {
            float trailExposure = AlpineVillagePeripheralStormFieldRules
                .EvaluateFootprintTrailExposure(
                    center.DistanceOutsideTrodden,
                    particleSize);
            float apertureProtection =
                AlpineVillagePeripheralStormFieldRules
                    .EvaluateFootprintApertureProtection(
                        SpatialPlan,
                        point,
                        particleSize);

            float behindRearWall = Vector2.Dot(
                point - SpatialPlan.RearWallCenter,
                SpatialPlan.RearDirection);
            float rearClosure = AlpineVillagePeripheralStormFieldRules
                .EvaluateFootprintRearClosure(
                    behindRearWall,
                    particleSize);
            return AlpineVillagePeripheralStormRules.ComposeStrength(
                trailExposure,
                apertureProtection,
                rearClosure);
        }

        private Color32 EvaluateColor(float strength, float variation)
        {
            Color color = Color.Lerp(
                hazeColor,
                Color.white,
                Mathf.Lerp(0.14f, 0.36f, strength));
            color.a = AlpineVillagePeripheralStormFieldRules.EvaluateOpacity(
                strength,
                stormWave,
                PlayerSample.StormStrength01,
                variation);
            return color;
        }

        private bool IsProtectedStructure(Vector2 point)
        {
            Vector3 sample = new Vector3(point.x, 0f, point.y);
            if (village.Station.PadArea.ContainsXZ(sample, -0.8f))
            {
                return true;
            }

            for (int index = 0; index < village.Plots.Count; index++)
            {
                Rect footprint = Expand(village.Plots[index].BoundsXZ, 0.45f);
                if (footprint.Contains(point))
                {
                    return true;
                }
            }

            return false;
        }

        private void Prewarm()
        {
            if (AppliedEmissionRate <= 0f)
            {
                return;
            }

            const float step = 0.12f;
            float carry = 0f;
            for (int index = 0; index < 92; index++)
            {
                carry += AppliedEmissionRate * step;
                int count = Mathf.FloorToInt(carry);
                carry -= count;
                Emit(count);
                Particles.Simulate(step, true, false, false);
            }

            emissionCarry = carry;
        }

        private void EnsureParticleBuffer(int count)
        {
            int capacity = Mathf.Max(Particles.main.maxParticles, count);
            if (liveParticles == null || liveParticles.Length < capacity)
            {
                liveParticles = new ParticleSystem.Particle[capacity];
            }
        }

        private void DisableUnusedModules()
        {
            ParticleSystem.CollisionModule collision = Particles.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = Particles.lights;
            lights.enabled = false;
            ParticleSystem.TrailModule trails = Particles.trails;
            trails.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                Particles.externalForces;
            externalForces.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters =
                Particles.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.TextureSheetAnimationModule textureSheet =
                Particles.textureSheetAnimation;
            textureSheet.enabled = false;
            ParticleSystem.TriggerModule trigger = Particles.trigger;
            trigger.enabled = false;
        }

        private void ConfigureRenderer(Material material)
        {
            ParticleRenderer.sharedMaterial = material;
            ParticleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            ParticleRenderer.alignment = ParticleSystemRenderSpace.View;
            ParticleRenderer.sortMode = ParticleSystemSortMode.Distance;
            ParticleRenderer.minParticleSize = 0.01f;
            ParticleRenderer.maxParticleSize = 0.34f;
            // This field mutates particle colour/size/position every frame.
            // Keep it on the regular billboard path; Unity 6's D3D12
            // instanced particle path is not stable under that workload.
            ParticleRenderer.enableGPUInstancing = false;
            ParticleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ParticleRenderer.receiveShadows = false;
            ParticleRenderer.lightProbeUsage = LightProbeUsage.Off;
            ParticleRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            ParticleRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            ParticleRenderer.allowOcclusionWhenDynamic = true;
            ApplyRendererProperties();
        }

        private void ApplyRendererProperties()
        {
            if (ParticleRenderer == null)
            {
                return;
            }

            if (rendererProperties == null)
            {
                rendererProperties = new MaterialPropertyBlock();
            }

            // Per-particle colour already carries the current haze. Keep the
            // shared material multiplier neutral or the warm village haze is
            // applied twice and the side sheets turn into brown smoke.
            rendererProperties.SetColor(BaseColorId, Color.white);
            rendererProperties.SetFloat(EdgePowerId, 1.18f);
            rendererProperties.SetFloat(NoiseStrengthId, 0.72f);
            rendererProperties.SetFloat(SoftParticleDistanceId, 1.35f);
            ParticleRenderer.SetPropertyBlock(rendererProperties);
        }

        private static Gradient CreateLifetimeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.88f, 0.76f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private float Next01()
        {
            uint value = NextRandom();
            return (value & 0x00FFFFFFu) / 16777215f;
        }

        private uint NextRandom()
        {
            uint value = randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            randomState = value;
            return value;
        }

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return (value & 0x00FFFFFFu) / 16777215f;
        }

        private static uint CreateRandomSeed(int seed)
        {
            uint value = unchecked((uint)seed) ^ 0x50455249u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value == 0u ? 0x50455249u : value;
        }

        private static Rect Expand(Rect source, float amount)
        {
            return Rect.MinMaxRect(
                source.xMin - amount,
                source.yMin - amount,
                source.xMax + amount,
                source.yMax + amount);
        }
    }
}
