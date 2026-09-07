using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Bounded condensation driven by the cold pose's exhale envelope. The
    /// owning presentation decides eligibility; this component only renders it.
    /// </summary>
    [DefaultExecutionOrder(280)]
    [DisallowMultipleComponent]
    public sealed class PlayerColdBreathEffect : MonoBehaviour
    {
        public const int MaximumParticles = 40;
        public const float MouthForwardOffset = 0.025f;

        private const float PeakParticlesPerSecond = 26f;
        private const float WindSpeedAtFullStrength = 0.55f;
        private const uint ParticleRandomSeed = 0x434F4C44u;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EdgePowerId = Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        private Transform mouthAnchor;
        private Transform particleTransform;
        private ParticleSystem particles;
        private ParticleSystemRenderer breathRenderer;
        private Func<WindSample> sampleWind;
        private float emissionRemainder;

        public bool IsInitialized { get; private set; }
        public bool IsEmissionEnabled { get; private set; }
        public float ExhaleEnvelope01 { get; private set; }
        public Transform MouthAnchor => mouthAnchor;
        public ParticleSystem Particles => particles;
        public ParticleSystemRenderer BreathRenderer => breathRenderer;

        public void Initialize(Transform mouth, Func<WindSample> wind)
        {
            if (mouth == null)
            {
                throw new ArgumentNullException(nameof(mouth));
            }

            if (wind == null)
            {
                throw new ArgumentNullException(nameof(wind));
            }

            StopAndClear();
            mouthAnchor = mouth;
            sampleWind = wind;
            EnsureParticleSystem();
            ConfigureParticleSystem();
            FollowMouth();
            IsInitialized = true;
        }

        public void SetBreath(float exhaleEnvelope, bool enabled)
        {
            bool canEmit = enabled && IsInitialized && isActiveAndEnabled &&
                           mouthAnchor != null;
            if (!canEmit)
            {
                if (IsEmissionEnabled)
                {
                    StopAndClear();
                }

                return;
            }

            IsEmissionEnabled = true;
            ExhaleEnvelope01 = float.IsNaN(exhaleEnvelope)
                ? 0f
                : Mathf.Clamp01(exhaleEnvelope);
        }

        public void StopAndClear()
        {
            IsEmissionEnabled = false;
            ExhaleEnvelope01 = 0f;
            emissionRemainder = 0f;
            if (particles != null)
            {
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void LateUpdate()
        {
            if (!IsEmissionEnabled)
            {
                return;
            }

            if (mouthAnchor == null || !mouthAnchor.gameObject.activeInHierarchy)
            {
                StopAndClear();
                return;
            }

            FollowMouth();
            if (Time.deltaTime <= 0f)
            {
                return;
            }

            UpdateWind();
            if (ExhaleEnvelope01 <= 0f)
            {
                emissionRemainder = 0f;
                return;
            }

            // Manual emission occurs after the final pose, at its actual mouth
            // position. World-space particles already exhaled stay in the air.
            emissionRemainder += PeakParticlesPerSecond *
                                 ExhaleEnvelope01 * Time.deltaTime;
            int count = Mathf.Min(
                MaximumParticles,
                Mathf.FloorToInt(emissionRemainder));
            if (count <= 0)
            {
                return;
            }

            emissionRemainder -= Mathf.Floor(emissionRemainder);
            if (!particles.isPlaying)
            {
                particles.Play(true);
            }

            particles.Emit(count);
        }

        private void OnDisable()
        {
            StopAndClear();
        }

        private void OnDestroy()
        {
            StopAndClear();
            sampleWind = null;
        }

        private void EnsureParticleSystem()
        {
            if (particles != null)
            {
                return;
            }

            var particleObject = new GameObject("Player Cold Breath Particles");
            particleTransform = particleObject.transform;
            particleTransform.SetParent(transform, false);
            particles = particleObject.AddComponent<ParticleSystem>();
            breathRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
        }

        private void ConfigureParticleSystem()
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = ParticleRandomSeed;

            ParticleSystem.MainModule main = particles.main;
            main.duration = PlayerColdPresentationModel.BreathCycleSeconds;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.useUnscaledTime = false;
            main.maxParticles = MaximumParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.28f, 0.40f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.09f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.84f, 0.88f, 0.88f, 0.48f),
                new Color(0.95f, 0.96f, 0.94f, 0.64f));
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 13f;
            shape.radius = 0.008f;
            shape.length = 0.018f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            UpdateWind();

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            noise.frequency = 0.45f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = 0.2f;

            var visibility = new Gradient();
            visibility.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.86f, 0.89f, 0.90f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.1f),
                    new GradientAlphaKey(0.75f, 0.35f),
                    new GradientAlphaKey(0.3f, 0.7f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(visibility);

            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.55f),
                    new Keyframe(0.2f, 1.2f),
                    new Keyframe(0.65f, 2.4f),
                    new Keyframe(1f, 3.1f)));

            breathRenderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            breathRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            breathRenderer.alignment = ParticleSystemRenderSpace.View;
            breathRenderer.sortMode = ParticleSystemSortMode.Distance;
            breathRenderer.minParticleSize = 0.003f;
            breathRenderer.maxParticleSize = 0.09f;
            breathRenderer.enableGPUInstancing = true;
            breathRenderer.shadowCastingMode = ShadowCastingMode.Off;
            breathRenderer.receiveShadows = false;
            breathRenderer.lightProbeUsage = LightProbeUsage.Off;
            breathRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            breathRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, new Color(0.94f, 0.97f, 0.98f, 1f));
            properties.SetFloat(EdgePowerId, 1.25f);
            properties.SetFloat(NoiseStrengthId, 0.38f);
            properties.SetFloat(SoftParticleDistanceId, 0.12f);
            breathRenderer.SetPropertyBlock(properties);
        }

        private void UpdateWind()
        {
            Vector3 drift = sampleWind().Velocity(WindSpeedAtFullStrength);
            ParticleSystem.VelocityOverLifetimeModule velocity =
                particles.velocityOverLifetime;
            velocity.x = drift.x;
            velocity.y = 0.065f;
            velocity.z = drift.z;
        }

        private void FollowMouth()
        {
            Vector3 outward = mouthAnchor.up.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(outward, Vector3.up)) > 0.98f
                ? mouthAnchor.forward
                : Vector3.up;
            particleTransform.SetPositionAndRotation(
                mouthAnchor.position + outward * MouthForwardOffset,
                Quaternion.LookRotation(outward, up));
            particleTransform.localScale = Vector3.one;
        }
    }
}
