using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Small passive cigarette presentation for the woman in the Mountain
    /// Road cafe. The ember and mouth exhale read the normalized time of her
    /// live default idle Playable; neither owns a clock, Light or AudioSource.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCigaretteEffect : MonoBehaviour
    {
        public const string CigaretteAnchorName = "SOCKET_Cigarette.R";
        public const string MouthAnchorName = "SOCKET_Mouth";
        public const string CigaretteRendererName = "ACC_CafeCigarette";
        public const string EmberRendererName = "ACC_CafeCigaretteEmber";
        public const float MouthForwardOffset = 0.018f;

        public const float EmberRiseStartNormalized = 0.26f;
        public const float EmberPeakStartNormalized = 0.32f;
        public const float EmberPeakEndNormalized = 0.36f;
        public const float EmberFallEndNormalized = 0.50f;

        public const float AuthoredExhaleNormalized = 0.58f;
        public const float PlumeRiseStartNormalized = 0.50f;
        public const float PlumePeakStartNormalized = 0.55f;
        public const float PlumePeakEndNormalized = 0.61f;
        public const float PlumeFallEndNormalized = 0.68f;
        public const float PlumePeakRate = 8f;
        public const int PlumeMaximumParticles = 24;

        public static readonly Color EmberRestColor =
            new Color(0.28f, 0.035f, 0.008f, 1f);
        public static readonly Color EmberDrawColor =
            new Color(1.45f, 0.34f, 0.055f, 1f);

        private const string PlumeObjectName = "Cafe Mouth Exhale Plume";
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
        private Transform mouthAnchor;
        private Renderer cigaretteRenderer;
        private Renderer emberRenderer;
        private ParticleSystem plume;
        private ParticleSystem.EmissionModule plumeEmission;
        private MaterialPropertyBlock properties;

        public bool IsInitialized { get; private set; }
        public Transform CigaretteAnchor => cigaretteAnchor;
        public Transform MouthAnchor => mouthAnchor;
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
            mouthAnchor = registry.FindModelTransform(MouthAnchorName);
            cigaretteRenderer = FindRenderer(
                registry,
                CigaretteRendererName);
            emberRenderer = FindRenderer(registry, EmberRendererName);
            if (cigaretteAnchor == null ||
                mouthAnchor == null ||
                cigaretteRenderer == null ||
                emberRenderer == null)
            {
                throw new InvalidOperationException(
                    "The cafe woman is missing her authored cigarette, " +
                    "ember, SOCKET_Cigarette.R or SOCKET_Mouth anchor.");
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
        /// Pure mouth-exhale envelope. It begins only after the cigarette
        /// leaves the lips, peaks around <see cref="AuthoredExhaleNormalized"/>
        /// and stops emitting by the 0.68 conversational-safe idle key.
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

            FollowMouth();
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
            FollowMouth();
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
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.75f, 2.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.19f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.040f, 0.070f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.74f, 0.79f, 0.73f, 0.50f),
                new Color(0.56f, 0.63f, 0.59f, 0.34f));
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            plumeEmission = plume.emission;
            plumeEmission.enabled = true;
            plumeEmission.rateOverTime = 0f;
            plumeEmission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = plume.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.008f;
            shape.length = 0.020f;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                plume.velocityOverLifetime;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.012f, 0.018f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.018f, 0.018f);
            velocity.enabled = true;

            ParticleSystem.NoiseModule noise = plume.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            noise.frequency = 0.34f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.18f);

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
                    new Keyframe(0f, 0.65f),
                    new Keyframe(0.18f, 1.15f),
                    new Keyframe(0.66f, 2.30f),
                    new Keyframe(1f, 2.90f)));

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
            renderer.minParticleSize = 0.006f;
            renderer.maxParticleSize = 0.14f;
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
                new Color(0.86f, 0.91f, 0.85f, 1f));
            plumeProperties.SetFloat(EdgePowerId, 1.35f);
            plumeProperties.SetFloat(NoiseStrengthId, 0.52f);
            plumeProperties.SetFloat(SoftParticleDistanceId, 0.16f);
            renderer.SetPropertyBlock(plumeProperties);

            FollowMouth();
            plume.Play(true);
        }

        private void FollowMouth()
        {
            if (mouthAnchor == null || plume == null)
            {
                return;
            }

            // NpcHumanV2 shares the production Hero V2 mouth socket: its
            // local up axis points out through the lips.
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

            plume.transform.SetPositionAndRotation(
                mouthAnchor.position + outward * MouthForwardOffset,
                Quaternion.LookRotation(outward, worldUp));
            plume.transform.localScale = Vector3.one;
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
                    new GradientAlphaKey(1f, 0.06f),
                    new GradientAlphaKey(0.82f, 0.38f),
                    new GradientAlphaKey(0.48f, 0.78f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
