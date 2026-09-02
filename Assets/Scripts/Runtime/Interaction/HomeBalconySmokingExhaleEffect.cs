using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Owns the bounded world-space plume emitted from the production
    /// character's mouth once per smoking loop.
    /// </summary>
    [DefaultExecutionOrder(280)]
    [DisallowMultipleComponent]
    public sealed class HomeBalconySmokingExhaleEffect : MonoBehaviour
    {
        public const int ExhaleStartLoopFrame = 16;
        public const int BurstParticleCount = 16;
        public const int MaximumParticles = 32;
        public const float MouthForwardOffset = 0.018f;

        private const string ParticleObjectName =
            "Home Smoking Exhale Particles";
        private const uint ParticleRandomSeed = 0x534D4F4Bu;
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        [SerializeField] private ParticleSystem particles;
        [SerializeField]
        private ParticleSystemRenderer smokeRenderer;

        private Transform mouthAnchor;
        private Transform particleTransform;

        public bool IsInitialized { get; private set; }
        public bool IsEmissionCycleActive { get; private set; }
        public bool IsManualBurstMode { get; private set; }
        public int ManualBurstCount { get; private set; }
        public Transform MouthAnchor => mouthAnchor;
        public ParticleSystem Particles => particles;
        public ParticleSystemRenderer SmokeRenderer => smokeRenderer;
        public float BurstTimeSeconds { get; private set; }
        public float LoopDurationSeconds { get; private set; }

        public void Initialize(
            Transform mouth,
            PlayerAnimatedInteractionDefinition definition)
        {
            IsInitialized = false;
            IsManualBurstMode = false;
            ManualBurstCount = 0;
            mouthAnchor = mouth != null
                ? mouth
                : throw new ArgumentNullException(nameof(mouth));
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (definition.LoopFrameCount <= ExhaleStartLoopFrame)
            {
                throw new ArgumentException(
                    "The smoking loop must contain its authored exhale " +
                    "frame.",
                    nameof(definition));
            }

            StopAndClear();
            EnsureParticleSystem();
            BurstTimeSeconds = CalculateLoopFrameStartSeconds(
                definition,
                ExhaleStartLoopFrame);
            LoopDurationSeconds =
                checked((float)definition.LoopDurationSeconds);
            ConfigureParticleSystem();
            FollowMouth();
            IsInitialized = true;
        }

        public bool BeginLooping()
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                particles == null)
            {
                return false;
            }

            if (IsManualBurstMode)
            {
                ConfigureParticleSystem();
                IsManualBurstMode = false;
            }

            StopAndClear();
            FollowMouth();
            particles.Play(true);
            IsEmissionCycleActive = true;
            return true;
        }

        /// <summary>
        /// Removes the self-timed looping burst so an external manual
        /// animation graph can trigger the same plume exactly when its
        /// authored exhale frame is crossed.
        /// </summary>
        public bool EnableManualBurstMode()
        {
            if (!IsInitialized || particles == null)
            {
                return false;
            }

            StopAndClear();
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            IsManualBurstMode = true;
            return true;
        }

        /// <summary>
        /// Emits one bounded plume without introducing an autonomous clock.
        /// Calls made while uninitialised, disabled or in looping mode are
        /// rejected rather than queued for a later duplicate emission.
        /// </summary>
        public bool EmitManualBurst()
        {
            if (!IsInitialized ||
                !IsManualBurstMode ||
                !isActiveAndEnabled ||
                particles == null)
            {
                return false;
            }

            FollowMouth();
            if (!particles.isPlaying)
            {
                particles.Play(true);
            }

            particles.Emit(BurstParticleCount);
            ManualBurstCount++;
            return true;
        }

        public void StopEmitting()
        {
            IsEmissionCycleActive = false;
            if (particles == null)
            {
                return;
            }

            particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting);
        }

        public void StopAndClear()
        {
            IsEmissionCycleActive = false;
            if (particles == null)
            {
                return;
            }

            particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void Update()
        {
            if (IsInitialized)
            {
                FollowMouth();
            }
        }

        private void LateUpdate()
        {
            if (IsInitialized)
            {
                FollowMouth();
            }
        }

        private void OnDisable()
        {
            StopAndClear();
        }

        private void OnDestroy()
        {
            StopAndClear();
        }

        private void EnsureParticleSystem()
        {
            if (particles == null)
            {
                GameObject particleObject =
                    new GameObject(ParticleObjectName);
                particleTransform = particleObject.transform;
                particleTransform.SetParent(transform, false);
                particles = particleObject.AddComponent<ParticleSystem>();
            }
            else
            {
                particleTransform = particles.transform;
            }

            if (smokeRenderer == null)
            {
                smokeRenderer =
                    particles.GetComponent<ParticleSystemRenderer>();
            }

            if (smokeRenderer == null)
            {
                smokeRenderer = particles.gameObject.AddComponent<
                    ParticleSystemRenderer>();
            }
        }

        private void ConfigureParticleSystem()
        {
            particles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = ParticleRandomSeed;

            ParticleSystem.MainModule main = particles.main;
            main.duration = LoopDurationSeconds;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.useUnscaledTime = false;
            main.maxParticles = MaximumParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(2.05f, 2.75f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.17f, 0.25f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.055f, 0.090f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.74f, 0.79f, 0.73f, 0.58f),
                new Color(0.56f, 0.63f, 0.59f, 0.42f));
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(
                new[]
                {
                    new ParticleSystem.Burst(
                        BurstTimeSeconds,
                        (short)BurstParticleCount)
                });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.008f;
            shape.length = 0.020f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x =
                new ParticleSystem.MinMaxCurve(-0.012f, 0.018f);
            velocity.y =
                new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
            velocity.z =
                new ParticleSystem.MinMaxCurve(-0.018f, 0.018f);
            velocity.enabled = true;

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX =
                new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            noise.strengthY =
                new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            noise.strengthZ =
                new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            noise.frequency = 0.34f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.18f);

            ParticleSystem.ColorOverLifetimeModule color =
                particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateVisibilityGradient());

            ParticleSystem.SizeOverLifetimeModule size =
                particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.65f),
                    new Keyframe(0.18f, 1.15f),
                    new Keyframe(0.66f, 2.30f),
                    new Keyframe(1f, 2.90f)));

            DisableUnusedModules();
            ConfigureRenderer();
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

        private void ConfigureRenderer()
        {
            smokeRenderer.sharedMaterial =
                CityNightResources.AtmosphereMaterial;
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            smokeRenderer.alignment = ParticleSystemRenderSpace.View;
            smokeRenderer.sortMode = ParticleSystemSortMode.Distance;
            smokeRenderer.minParticleSize = 0.006f;
            smokeRenderer.maxParticleSize = 0.14f;
            smokeRenderer.enableGPUInstancing = true;
            smokeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            smokeRenderer.receiveShadows = false;
            smokeRenderer.lightProbeUsage = LightProbeUsage.Off;
            smokeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            smokeRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            smokeRenderer.allowOcclusionWhenDynamic = true;

            var properties = new MaterialPropertyBlock();
            properties.SetColor(
                BaseColorId,
                new Color(0.86f, 0.91f, 0.85f, 1f));
            properties.SetFloat(EdgePowerId, 1.35f);
            properties.SetFloat(NoiseStrengthId, 0.52f);
            properties.SetFloat(SoftParticleDistanceId, 0.16f);
            smokeRenderer.SetPropertyBlock(properties);
        }

        private void FollowMouth()
        {
            if (mouthAnchor == null || particleTransform == null)
            {
                return;
            }

            Vector3 outward = mouthAnchor.up;
            if (outward.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            outward.Normalize();
            Vector3 worldUp = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(outward, worldUp)) > 0.98f)
            {
                worldUp = mouthAnchor.forward;
            }

            particleTransform.SetPositionAndRotation(
                mouthAnchor.position + outward * MouthForwardOffset,
                Quaternion.LookRotation(outward, worldUp));
            particleTransform.localScale = Vector3.one;
        }

        private static float CalculateLoopFrameStartSeconds(
            PlayerAnimatedInteractionDefinition definition,
            int localFrame)
        {
            double seconds = 0d;
            for (int frame = 0; frame < localFrame; frame++)
            {
                seconds += definition.GetLoopFrameDurationSeconds(frame);
            }

            return checked((float)seconds);
        }

        private static Gradient CreateVisibilityGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(
                        new Color(0.78f, 0.84f, 0.79f),
                        0.55f),
                    new GradientColorKey(
                        new Color(0.60f, 0.67f, 0.63f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.06f),
                    new GradientAlphaKey(0.82f, 0.38f),
                    new GradientAlphaKey(0.48f, 0.78f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
