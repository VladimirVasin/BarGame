using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Small passive cigarette presentation for the woman in the Mountain
    /// Road cafe. The ember and plume read the normalized time of her live
    /// default idle Playable; neither owns a clock, Light or AudioSource.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCigaretteEffect : MonoBehaviour
    {
        public const string CigaretteAnchorName = "SOCKET_Cigarette.R";
        public const string CigaretteRendererName = "ACC_CafeCigarette";
        public const string EmberRendererName = "ACC_CafeCigaretteEmber";

        public const float EmberRiseStartNormalized = 0.26f;
        public const float EmberPeakStartNormalized = 0.32f;
        public const float EmberPeakEndNormalized = 0.36f;
        public const float EmberFallEndNormalized = 0.50f;

        public const float PlumeRiseStartNormalized = 0.32f;
        public const float PlumePeakStartNormalized = 0.40f;
        public const float PlumePeakEndNormalized = 0.50f;
        public const float PlumeFallEndNormalized = 0.68f;
        public const float PlumePeakRate = 5.5f;
        public const int PlumeMaximumParticles = 12;

        public static readonly Color EmberRestColor =
            new Color(0.28f, 0.035f, 0.008f, 1f);
        public static readonly Color EmberDrawColor =
            new Color(1.45f, 0.34f, 0.055f, 1f);

        private const string PlumeObjectName = "Cafe Cigarette Plume";
        private const uint ParticleRandomSeed = 0x43414645u; // "CAFE"

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        private MountainRoadCafeCastPresentation presentation;
        private Transform cigaretteAnchor;
        private Renderer cigaretteRenderer;
        private Renderer emberRenderer;
        private ParticleSystem plume;
        private ParticleSystem.EmissionModule plumeEmission;
        private MaterialPropertyBlock properties;

        public bool IsInitialized { get; private set; }
        public Transform CigaretteAnchor => cigaretteAnchor;
        public Renderer CigaretteRenderer => cigaretteRenderer;
        public Renderer EmberRenderer => emberRenderer;
        public ParticleSystem Plume => plume;
        public float DefaultIdlePhase { get; private set; }
        public float EmberAmount { get; private set; }
        public float PlumeRate { get; private set; }

        public void Initialize(
            MountainRoadCafeCastPresentation configuredPresentation,
            MountainRoadCafeCastAssetRegistry registry)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The cafe cigarette effect is already initialized.");
            }

            presentation = configuredPresentation != null
                ? configuredPresentation
                : throw new ArgumentNullException(
                    nameof(configuredPresentation));
            if (!presentation.IsInitialized ||
                presentation.Role != MountainRoadCafeCastRole.PairWoman)
            {
                throw new InvalidOperationException(
                    "Only the initialized cafe woman may own the " +
                    "cigarette effect.");
            }

            if (registry == null ||
                registry != presentation.Registry ||
                registry.Role != MountainRoadCafeCastRole.PairWoman)
            {
                throw new InvalidOperationException(
                    "The cafe cigarette effect requires the woman's " +
                    "matching asset registry.");
            }

            cigaretteAnchor = registry.FindModelTransform(
                CigaretteAnchorName);
            cigaretteRenderer = FindRenderer(
                registry,
                CigaretteRendererName);
            emberRenderer = FindRenderer(registry, EmberRendererName);
            if (cigaretteAnchor == null ||
                cigaretteRenderer == null ||
                emberRenderer == null)
            {
                throw new InvalidOperationException(
                    "The cafe woman is missing her authored cigarette, " +
                    "ember or SOCKET_Cigarette.R anchor.");
            }

            properties = new MaterialPropertyBlock();
            emberRenderer.sharedMaterial =
                CityNightResources.EmissiveMaterial;
            emberRenderer.shadowCastingMode = ShadowCastingMode.Off;
            emberRenderer.receiveShadows = false;
            CreatePlume();
            IsInitialized = true;
            ApplyCurrentPresentationPhase();
        }

        /// <summary>
        /// Pure ember envelope. It peaks while the cigarette is at the lips
        /// in the authored 0.26-0.36 idle window.
        /// </summary>
        public static float EmberAmountAt(float normalizedPhase)
        {
            float phase = Mathf.Repeat(normalizedPhase, 1f);
            if (phase < EmberRiseStartNormalized ||
                phase >= EmberFallEndNormalized)
            {
                return 0f;
            }

            if (phase < EmberPeakStartNormalized)
            {
                return SmoothRamp(
                    EmberRiseStartNormalized,
                    EmberPeakStartNormalized,
                    phase);
            }

            if (phase <= EmberPeakEndNormalized)
            {
                return 1f;
            }

            return 1f - SmoothRamp(
                EmberPeakEndNormalized,
                EmberFallEndNormalized,
                phase);
        }

        /// <summary>
        /// Pure plume envelope, delayed behind the drag and fully decayed by
        /// the authored 0.68 idle key.
        /// </summary>
        public static float PlumeAmountAt(float normalizedPhase)
        {
            float phase = Mathf.Repeat(normalizedPhase, 1f);
            if (phase < PlumeRiseStartNormalized ||
                phase >= PlumeFallEndNormalized)
            {
                return 0f;
            }

            if (phase < PlumePeakStartNormalized)
            {
                return SmoothRamp(
                    PlumeRiseStartNormalized,
                    PlumePeakStartNormalized,
                    phase);
            }

            if (phase <= PlumePeakEndNormalized)
            {
                return 1f;
            }

            return 1f - SmoothRamp(
                PlumePeakEndNormalized,
                PlumeFallEndNormalized,
                phase);
        }

        public static float PlumeRateAt(float normalizedPhase)
        {
            return PlumePeakRate * PlumeAmountAt(normalizedPhase);
        }

        private void OnEnable()
        {
            if (!IsInitialized || plume == null)
            {
                return;
            }

            plume.Play(true);
            ApplyCurrentPresentationPhase();
        }

        private void LateUpdate()
        {
            if (IsInitialized && presentation != null)
            {
                ApplyCurrentPresentationPhase();
            }
        }

        private void OnDisable()
        {
            if (plume != null)
            {
                plume.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDestroy()
        {
            if (plume != null)
            {
                plume.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ApplyCurrentPresentationPhase()
        {
            float normalizedPhase = presentation.DefaultClipNormalizedTime;
            bool defaultIdleIsVisible =
                presentation.CurrentClipKind ==
                presentation.Registry.DefaultClipKind;
            Apply(normalizedPhase, defaultIdleIsVisible);
        }

        private void Apply(
            float normalizedPhase,
            bool defaultIdleIsVisible)
        {
            DefaultIdlePhase = Mathf.Repeat(normalizedPhase, 1f);
            EmberAmount = defaultIdleIsVisible
                ? EmberAmountAt(DefaultIdlePhase)
                : 0f;
            PlumeRate = defaultIdleIsVisible
                ? PlumeRateAt(DefaultIdlePhase)
                : 0f;

            emberRenderer.GetPropertyBlock(properties);
            Color ember = Color.Lerp(
                EmberRestColor,
                EmberDrawColor,
                EmberAmount);
            properties.SetColor(BaseColorId, ember);
            properties.SetColor(LegacyColorId, ember);
            emberRenderer.SetPropertyBlock(properties);
            properties.Clear();

            if (plume == null)
            {
                return;
            }

            plume.transform.SetPositionAndRotation(
                emberRenderer.bounds.center,
                Quaternion.LookRotation(
                    Vector3.up,
                    cigaretteAnchor.forward));
            plumeEmission.rateOverTime = PlumeRate;
        }

        private void CreatePlume()
        {
            var host = new GameObject(PlumeObjectName);
            host.layer = gameObject.layer;
            host.transform.SetParent(transform, false);
            plume = host.AddComponent<ParticleSystem>();
            plume.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            plume.useAutoRandomSeed = false;
            plume.randomSeed = ParticleRandomSeed;

            ParticleSystem.MainModule main = plume.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = PlumeMaximumParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.035f, 0.065f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.026f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.58f, 0.61f, 0.58f, 0.30f),
                new Color(0.42f, 0.46f, 0.44f, 0.18f));
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.Pause;

            plumeEmission = plume.emission;
            plumeEmission.enabled = true;
            plumeEmission.rateOverTime = 0f;
            plumeEmission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = plume.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 5f;
            shape.radius = 0.004f;
            shape.length = 0.008f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                plume.velocityOverLifetime;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.008f, 0.012f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.025f, 0.050f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.010f, 0.010f);
            velocity.enabled = true;

            ParticleSystem.NoiseModule noise = plume.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.018f, 0.038f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.018f, 0.038f);
            noise.frequency = 0.34f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.14f);

            ParticleSystem.ColorOverLifetimeModule color =
                plume.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateVisibilityGradient());

            ParticleSystem.SizeOverLifetimeModule size =
                plume.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.55f),
                    new Keyframe(0.35f, 1.15f),
                    new Keyframe(1f, 2.10f)));

            ParticleSystem.CollisionModule collision = plume.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = plume.lights;
            lights.enabled = false;
            ParticleSystem.TrailModule trails = plume.trails;
            trails.enabled = false;
            ParticleSystem.TriggerModule trigger = plume.trigger;
            trigger.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                plume.externalForces;
            externalForces.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters =
                plume.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.TextureSheetAnimationModule textureSheet =
                plume.textureSheetAnimation;
            textureSheet.enabled = false;

            var renderer = plume.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0.003f;
            renderer.maxParticleSize = 0.07f;
            renderer.enableGPUInstancing = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            var plumeProperties = new MaterialPropertyBlock();
            plumeProperties.SetColor(
                BaseColorId,
                new Color(0.68f, 0.71f, 0.68f, 1f));
            plumeProperties.SetFloat(EdgePowerId, 1.35f);
            plumeProperties.SetFloat(NoiseStrengthId, 0.52f);
            plumeProperties.SetFloat(SoftParticleDistanceId, 0.08f);
            renderer.SetPropertyBlock(plumeProperties);

            plume.Play(true);
        }

        private static Renderer FindRenderer(
            MountainRoadCafeCastAssetRegistry registry,
            string rendererName)
        {
            for (int index = 0;
                 index < registry.RendererBindings.Count;
                 index++)
            {
                Renderer renderer =
                    registry.RendererBindings[index]?.Renderer;
                if (renderer != null && string.Equals(
                        renderer.name,
                        rendererName,
                        StringComparison.Ordinal))
                {
                    return renderer;
                }
            }

            return null;
        }

        private static float SmoothRamp(
            float start,
            float end,
            float value)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(start, end, value));
        }

        private static Gradient CreateVisibilityGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(
                        new Color(0.72f, 0.75f, 0.72f),
                        0.62f),
                    new GradientColorKey(
                        new Color(0.55f, 0.59f, 0.57f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.72f, 0.12f),
                    new GradientAlphaKey(0.42f, 0.58f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
