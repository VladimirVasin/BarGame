using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Pure presentation rules for the village's low, wind-driven snow.
    /// The main precipitation field owns snowfall through the full camera
    /// volume; this layer owns the metre above the sampled ground, where a
    /// gale becomes legible as sheeting spindrift even in a still frame.
    /// </summary>
    public static class AlpineVillageStormFieldRules
    {
        public const int MaximumParticles = 680;
        public const float FieldExtent = 24f;
        public const float MinimumEmissionRate = 60f;
        public const float MaximumEmissionRate = 230f;
        public const float MaximumTransportSpeed = 7.4f;
        public const float ShelterEmissionFactor = 0.18f;
        public const float MinimumLifetime = 1.1f;
        public const float MaximumLifetime = 1.9f;
        public const float MinimumGroundLift = 0.08f;
        public const float MaximumGroundLift = 0.35f;

        /// <summary>
        /// The most enclosing-ridge rise a strip may be born on. Spindrift
        /// belongs to the bowl floor; with the toe now `18 m` from the last
        /// houses the `24 m` field regularly straddles the `58°` wall, and a
        /// strip emitted on the wall's own slope reads as snow running up a
        /// cliff rather than along the ground.
        /// </summary>
        public const float SpindriftRiseLimit = 2f;

        /// <summary>
        /// Where on the RAW shared gust rhythm the haze starts to close and
        /// where it is fully closed. `0.86` is the rhythm's mean plus its
        /// primary swell; `0.66` sits above the mean so the quick secondary
        /// ripple alone cannot hold the wave up between swells, which is
        /// what keeps the top house coming back every cycle.
        /// </summary>
        public const float StormWaveGustFloor = 0.66f;

        public const float StormWaveGustCrest = 0.86f;

        /// <summary>
        /// One-pole smoothing of the wave, asymmetric: a gust closes the
        /// lane quickly and the haze thins back at half that pace. Real
        /// seconds; the game clock advances on the same `Time.deltaTime`,
        /// so a pause freezes both the rhythm and the wave.
        /// </summary>
        public const float StormWaveAttackSeconds = 0.5f;

        public const float StormWaveReleaseSeconds = 1f;

        public static Vector3 EvaluateTransport(in WindSample wind)
        {
            return wind.HorizontalDirection *
                   (Mathf.Clamp01(wind.Strength01) *
                    MaximumTransportSpeed);
        }

        public static float EvaluateEmissionRate(
            float windStrength01,
            bool sheltered,
            bool riding)
        {
            if (riding)
            {
                return 0f;
            }

            float pulse = EvaluateGalePulse(windStrength01);
            float rate = Mathf.Lerp(
                MinimumEmissionRate,
                MaximumEmissionRate,
                pulse);
            return sheltered ? rate * ShelterEmissionFactor : rate;
        }

        public static float EvaluateGalePulse(float windStrength01)
        {
            float normalized = Mathf.InverseLerp(
                AlpineVillageWeatherRules.WindFloor,
                AlpineVillageWeatherRules.WindCeiling,
                windStrength01);
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        /// <summary>
        /// Where the haze wants to be for one value of the raw gust rhythm
        /// (<see cref="GameWeatherRules.EvaluateGust"/>): `0` at or under
        /// the floor, `1` at or over the crest. Not the shaped gale pulse
        /// above - that one is pinned at `1` for a whole thunderstorm slot
        /// at the lane head and could never reopen the street.
        /// </summary>
        public static float EvaluateStormWaveTarget(float gust)
        {
            float normalized = Mathf.InverseLerp(
                StormWaveGustFloor,
                StormWaveGustCrest,
                gust);
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        /// <summary>
        /// Moves the wave toward its target over one frame with the attack
        /// or release constant, whichever way it is going. A non-positive
        /// step returns the wave unchanged, so a frozen clock holds the
        /// haze where it is instead of letting it settle on a stale target.
        /// </summary>
        public static float AdvanceStormWave(
            float current,
            float target,
            float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return current;
            }

            float timeConstant = target > current
                ? StormWaveAttackSeconds
                : StormWaveReleaseSeconds;
            float blend = 1f - Mathf.Exp(-deltaSeconds / timeConstant);
            return Mathf.Clamp01(current + (target - current) * blend);
        }
    }

    /// <summary>
    /// A deterministic, terrain-hugging spindrift field local to the Alpine
    /// Village. Particles are emitted manually so every strip begins just
    /// above the actual sampled slope; a box emitter would put half of this
    /// low layer underground on one side of the uphill lane and floating on
    /// the other.
    ///
    /// It reads <see cref="CityWeatherController.CurrentWind"/> after the
    /// controller has applied the shared schedule. It never writes cloth or
    /// the main snow field, so there is still exactly one owner of each.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(80)]
    public sealed class AlpineVillageStormField : MonoBehaviour
    {
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        private Transform followTarget;
        private AlpineVillagePlan plan;
        private CityWeatherController weather;
        private Func<bool> isSheltered;
        private MountainRoadWindSoundPlayer windSound;
        private uint randomState;
        private float emissionCarry;
        private bool appliedSheltered;
        private ParticleSystem.Particle[] liveParticles;

        public bool IsInitialized { get; private set; }
        public ParticleSystem Particles { get; private set; }
        public ParticleSystemRenderer ParticleRenderer { get; private set; }
        public Vector3 AppliedTransport { get; private set; }
        public float AppliedEmissionRate { get; private set; }

        public void Initialize(
            Transform target,
            AlpineVillagePlan villagePlan,
            CityWeatherController weatherController,
            Material snowMaterial,
            int seed,
            Func<bool> shelterProvider,
            MountainRoadWindSoundPlayer soundPlayer)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Alpine Village storm field is already initialized.");
            }

            followTarget = target != null
                ? target
                : throw new ArgumentNullException(nameof(target));
            plan = villagePlan ??
                   throw new ArgumentNullException(nameof(villagePlan));
            weather = weatherController != null
                ? weatherController
                : throw new ArgumentNullException(nameof(weatherController));
            if (snowMaterial == null)
            {
                throw new ArgumentNullException(nameof(snowMaterial));
            }

            isSheltered = shelterProvider;
            windSound = soundPlayer;
            randomState = CreateRandomSeed(seed);
            ConfigureParticles(snowMaterial);
            bool sheltered = isSheltered != null && isSheltered();
            ApplyWeather(sheltered);
            Prewarm();
            Particles.Play(true);
            IsInitialized = true;
        }

        private void LateUpdate()
        {
            if (!IsInitialized ||
                followTarget == null ||
                Particles == null)
            {
                return;
            }

            bool sheltered = isSheltered != null && isSheltered();
            ApplyWeather(sheltered);
            CullShelteredParticles();
            float delta = Time.unscaledDeltaTime;
            if (delta <= 0f || AppliedEmissionRate <= 0f)
            {
                return;
            }

            emissionCarry += AppliedEmissionRate * delta;
            int count = Mathf.Min(64, Mathf.FloorToInt(emissionCarry));
            emissionCarry -= count;
            Emit(count);
        }

        private void ApplyWeather(bool sheltered)
        {
            appliedSheltered = sheltered;
            WindSample wind = weather.CurrentWind;
            AppliedTransport =
                AlpineVillageStormFieldRules.EvaluateTransport(wind);
            AppliedEmissionRate =
                AlpineVillageStormFieldRules.EvaluateEmissionRate(
                    wind.Strength01,
                    sheltered,
                    GameSessionState.IsRidingAVehicle);
            float audibleGale = Mathf.Lerp(
                0.78f,
                1f,
                AlpineVillageStormFieldRules.EvaluateGalePulse(
                    wind.Strength01));
            windSound?.SetNormalizedStrength(audibleGale);
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
            main.duration = 4f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            main.maxParticles =
                AlpineVillageStormFieldRules.MaximumParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                AlpineVillageStormFieldRules.MinimumLifetime,
                AlpineVillageStormFieldRules.MaximumLifetime);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.82f, 0.88f, 0.94f, 0.18f),
                new Color(0.94f, 0.96f, 0.98f, 0.34f));
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
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.32f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.48f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.32f);
            noise.frequency = 0.48f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.85f);

            ParticleSystem.ColorOverLifetimeModule color =
                Particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.82f, 0.72f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(fade);

            DisableUnusedModules();
            ConfigureRenderer(material);
        }

        private void Emit(int count)
        {
            if (count <= 0 || followTarget == null)
            {
                return;
            }

            Vector3 center = followTarget.position;
            Vector3 cross = Vector3.Cross(
                Vector3.up,
                AppliedTransport.sqrMagnitude > 0.001f
                    ? AppliedTransport.normalized
                    : Vector3.forward);
            for (int index = 0; index < count; index++)
            {
                float x = (Next01() - 0.5f) *
                          AlpineVillageStormFieldRules.FieldExtent;
                float z = (Next01() - 0.5f) *
                          AlpineVillageStormFieldRules.FieldExtent;
                var point = new Vector2(center.x + x, center.z + z);
                if (!plan.TerrainMeshBounds.Contains(point))
                {
                    continue;
                }

                if (appliedSheltered && IsInsideStationShelter(point))
                {
                    continue;
                }

                // Strips belong to the bowl floor, not to the wall.
                if (AlpineVillageTerrainSampler.SampleRidgeRise(plan, point) >
                    AlpineVillageStormFieldRules.SpindriftRiseLimit)
                {
                    continue;
                }

                float ground = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    point);
                float lift = Mathf.Lerp(
                    AlpineVillageStormFieldRules.MinimumGroundLift,
                    AlpineVillageStormFieldRules.MaximumGroundLift,
                    Next01());
                float sideJitter = Mathf.Lerp(-0.42f, 0.42f, Next01());
                float upward = Mathf.Lerp(0.08f, 0.48f, Next01());
                float terrainVertical = EvaluateTerrainVerticalVelocity(
                    point,
                    ground);
                var emit = new ParticleSystem.EmitParams
                {
                    position = new Vector3(point.x, ground + lift, point.y),
                    velocity = AppliedTransport +
                               cross * sideJitter +
                               Vector3.up * (terrainVertical + upward),
                    startLifetime = Mathf.Lerp(
                        AlpineVillageStormFieldRules.MinimumLifetime,
                        AlpineVillageStormFieldRules.MaximumLifetime,
                        Next01()),
                    startSize = Mathf.Lerp(0.018f, 0.045f, Next01()),
                    startColor = Color.Lerp(
                        new Color(0.82f, 0.88f, 0.94f, 0.18f),
                        new Color(0.94f, 0.96f, 0.98f, 0.34f),
                        Next01()),
                    rotation = Next01() * Mathf.PI * 2f
                };
                Particles.Emit(emit, 1);
            }
        }

        private float EvaluateTerrainVerticalVelocity(
            Vector2 point,
            float ground)
        {
            if (AppliedTransport.sqrMagnitude < 0.001f)
            {
                return 0f;
            }

            Vector3 direction = AppliedTransport.normalized;
            var ahead = point + new Vector2(direction.x, direction.z);
            if (!plan.TerrainMeshBounds.Contains(ahead))
            {
                return 0f;
            }

            float aheadGround = AlpineVillageTerrainSampler.SampleHeight(
                plan,
                ahead);
            return Mathf.Clamp(
                (aheadGround - ground) * AppliedTransport.magnitude,
                -1.5f,
                1.5f);
        }

        private void Prewarm()
        {
            if (AppliedEmissionRate <= 0f)
            {
                return;
            }

            const float step = 0.1f;
            float carry = 0f;
            for (int index = 0; index < 18; index++)
            {
                carry += AppliedEmissionRate * step;
                int count = Mathf.FloorToInt(carry);
                carry -= count;
                Emit(count);
                Particles.Simulate(step, true, false, false);
            }

            emissionCarry = carry;
        }

        /// <summary>
        /// Ground spindrift is manually emitted in world space, so it cannot
        /// use the main field's donut shape. Reject new strips under the
        /// station canopy and remove any that the wind carries into it; snow
        /// immediately outside stays visible. During the cabin ride the whole
        /// local field is dry and follows no exposed ground at all.
        /// </summary>
        private void CullShelteredParticles()
        {
            if (GameSessionState.IsRidingAVehicle)
            {
                Particles.Clear(true);
                emissionCarry = 0f;
                return;
            }

            if (!appliedSheltered || Particles.particleCount == 0)
            {
                return;
            }

            int capacity = Mathf.Max(
                Particles.main.maxParticles,
                Particles.particleCount);
            if (liveParticles == null || liveParticles.Length < capacity)
            {
                liveParticles = new ParticleSystem.Particle[capacity];
            }

            int count = Particles.GetParticles(liveParticles);
            int kept = 0;
            for (int index = 0; index < count; index++)
            {
                ParticleSystem.Particle particle = liveParticles[index];
                var point = new Vector2(
                    particle.position.x,
                    particle.position.z);
                if (IsInsideStationShelter(point))
                {
                    continue;
                }

                liveParticles[kept++] = particle;
            }

            if (kept != count)
            {
                Particles.SetParticles(liveParticles, kept);
            }
        }

        private bool IsInsideStationShelter(Vector2 point)
        {
            Vector3 sample = new Vector3(point.x, 0f, point.y);
            return plan.Station.PadArea.ContainsXZ(sample, -0.35f);
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
            ParticleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            ParticleRenderer.alignment = ParticleSystemRenderSpace.View;
            ParticleRenderer.lengthScale = 0.20f;
            ParticleRenderer.velocityScale = 0.085f;
            ParticleRenderer.cameraVelocityScale = 0f;
            ParticleRenderer.sortMode = ParticleSystemSortMode.None;
            ParticleRenderer.minParticleSize = 0f;
            ParticleRenderer.maxParticleSize = 0.32f;
            ParticleRenderer.enableGPUInstancing = true;
            ParticleRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ParticleRenderer.receiveShadows = false;
            ParticleRenderer.lightProbeUsage = LightProbeUsage.Off;
            ParticleRenderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            ParticleRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var properties = new MaterialPropertyBlock();
            properties.SetFloat(EdgePowerId, 1.12f);
            properties.SetFloat(NoiseStrengthId, 0.32f);
            properties.SetFloat(SoftParticleDistanceId, 0.55f);
            ParticleRenderer.SetPropertyBlock(properties);
        }

        private float Next01()
        {
            uint value = randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            randomState = value;
            return (value & 0x00FFFFFFu) / 16777215f;
        }

        private static uint CreateRandomSeed(int seed)
        {
            uint value = unchecked((uint)seed) ^ 0x424C495Au;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value == 0u ? 0x424C495Au : value;
        }

        private void OnDisable()
        {
            windSound?.SetNormalizedStrength(0f);
        }
    }
}
