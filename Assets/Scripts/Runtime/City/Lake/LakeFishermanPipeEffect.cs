using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The pipe clenched in the fisherman's teeth: a live ember on the
    /// bowl, the small warm light it throws onto the beard and the hood,
    /// and the thin plume that leaves it.
    ///
    /// All three are driven by one number — the breath phase of the very
    /// clip that is moving his chest — rather than by a timer of their
    /// own. That is the whole design. A second free-running timer would
    /// be simpler and would look wrong within a second of watching him:
    /// smoke that thickens while the ribs are closing does not read as
    /// smoking, it reads as a particle system parented to a man.
    ///
    /// So the ember comes up as the chest opens, peaks at the top of the
    /// draw, and falls back as he lets it go, and the plume is emitted
    /// on the same curve one beat behind it.
    ///
    /// Built at runtime by <see cref="LakeFishermanFactory"/>, never
    /// authored into the staged prefab: that prefab is validated to
    /// carry no light and no audio, which is exactly the guard that
    /// keeps effects like this one from being smuggled into the art.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class LakeFishermanPipeEffect : MonoBehaviour
    {
        /// <summary>The ember's colour at rest. Deliberately deep — a
        /// pipe between draws is dull red under ash, not orange.</summary>
        public static readonly Color EmberRestColor =
            new Color(0.330f, 0.075f, 0.018f, 1f);

        /// <summary>And at the top of the draw, when the air pulled
        /// through the bowl fans it. Red and green saturate while blue
        /// stays low, the same reasoning the pier hand lamp records:
        /// lift blue past its own ceiling and a warm ember becomes a
        /// white chip.</summary>
        public static readonly Color EmberDrawColor =
            new Color(1.700f, 0.560f, 0.115f, 1f);

        public static readonly Color EmberLightColor =
            new Color(1f, 0.470f, 0.180f, 1f);

        /// <summary>Reach of the ember light. It is an ember, so it is
        /// allowed to touch his face and nothing else: at this range it
        /// never reaches the boards, the water or the hand lamp's
        /// pool.</summary>
        public const float LightRange = 0.62f;
        public const float LightRestIntensity = 0.55f;
        public const float LightDrawIntensity = 2.30f;

        /// <summary>
        /// How far behind the chest the smoke is. The draw pulls air
        /// through the bowl and the plume that results leaves it a
        /// moment later, so the emission curve is the breath curve
        /// delayed by this fraction of one breath.
        /// </summary>
        public const float PlumeBreathLag = 0.18f;

        public const float PlumeRestRate = 2.2f;
        public const float PlumeDrawRate = 11.0f;
        public const int PlumeMaximumParticles = 24;

        private const string ParticleObjectName = "Pipe Plume";
        private const string LightObjectName = "Pipe Ember Light";
        private const uint ParticleRandomSeed = 0x50495045u; // "PIPE"

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

        private LakeFishermanPresentation presentation;
        private Transform emberAnchor;
        private Renderer emberRenderer;
        private Light emberLight;
        private ParticleSystem plume;
        private ParticleSystem.EmissionModule plumeEmission;
        private MaterialPropertyBlock properties;

        public bool IsInitialized { get; private set; }

        /// <summary>Last chest opening the effect was driven with, for
        /// tests and for anyone reading the inspector.</summary>
        public float BreathAmount { get; private set; }

        /// <summary>Last plume rate, in particles per second.</summary>
        public float PlumeRate { get; private set; }

        public Light EmberLight => emberLight;
        public ParticleSystem Plume => plume;

        public void Initialize(
            LakeFishermanPresentation configuredPresentation,
            Transform configuredEmberAnchor,
            Renderer configuredEmberRenderer)
        {
            presentation = configuredPresentation != null
                ? configuredPresentation
                : throw new ArgumentNullException(
                    nameof(configuredPresentation));
            emberAnchor = configuredEmberAnchor != null
                ? configuredEmberAnchor
                : throw new ArgumentNullException(
                    nameof(configuredEmberAnchor));
            emberRenderer = configuredEmberRenderer;

            properties = new MaterialPropertyBlock();
            if (emberRenderer != null)
            {
                // The shared pedestrian material is validated
                // non-emissive, which is right for cloth and wrong for
                // a coal. Swapping this one instance's shared material
                // for the City's emissive one is what makes the ember
                // read as burning by day as well as at night, and it
                // creates no per-instance material.
                emberRenderer.sharedMaterial =
                    CityNightResources.EmissiveMaterial;
                emberRenderer.shadowCastingMode = ShadowCastingMode.Off;
            }

            CreateLight();
            CreatePlume();
            Apply(0f);
            IsInitialized = true;
        }

        /// <summary>
        /// Pure: the plume's emission rate for a breath phase. Split out
        /// so the lag can be proved without a scene.
        /// </summary>
        public static float PlumeRateAt(float breathPhase)
        {
            float lagged = LakeFishermanPresentation.BreathAmountAt(
                breathPhase - PlumeBreathLag);
            return Mathf.Lerp(PlumeRestRate, PlumeDrawRate, lagged);
        }

        private void LateUpdate()
        {
            if (!IsInitialized || presentation == null)
            {
                return;
            }

            Apply(presentation.BreathPhase);
        }

        private void Apply(float breathPhase)
        {
            BreathAmount = LakeFishermanPresentation.BreathAmountAt(
                breathPhase);
            PlumeRate = PlumeRateAt(breathPhase);

            if (emberRenderer != null)
            {
                Color ember = Color.Lerp(
                    EmberRestColor,
                    EmberDrawColor,
                    BreathAmount);
                emberRenderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, ember);
                properties.SetColor(LegacyColorId, ember);
                emberRenderer.SetPropertyBlock(properties);
                properties.Clear();
            }

            if (emberLight != null)
            {
                emberLight.transform.position = emberAnchor.position;
                emberLight.intensity = Mathf.Lerp(
                    LightRestIntensity,
                    LightDrawIntensity,
                    BreathAmount);
            }

            if (plume != null)
            {
                plume.transform.SetPositionAndRotation(
                    emberAnchor.position,
                    Quaternion.LookRotation(Vector3.up, emberAnchor.forward));
                plumeEmission.rateOverTime = PlumeRate;
            }
        }

        private void CreateLight()
        {
            var host = new GameObject(LightObjectName);
            host.transform.SetParent(transform, false);
            host.transform.position = emberAnchor.position;
            emberLight = host.AddComponent<Light>();
            emberLight.type = LightType.Point;
            emberLight.color = EmberLightColor;
            emberLight.intensity = LightRestIntensity;
            emberLight.range = LightRange;
            emberLight.shadows = LightShadows.None;
            emberLight.renderMode = LightRenderMode.ForcePixel;
            emberLight.lightmapBakeType = LightmapBakeType.Realtime;

            // Deliberately not on the night registry, for the reason
            // the pier hand lamp records: that registry dims a fixture
            // under a day sky, which is right for something switched
            // off and wrong for something burning. He smokes at noon.
        }

        private void CreatePlume()
        {
            var host = new GameObject(ParticleObjectName);
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
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(1.65f, 2.40f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.085f, 0.135f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.022f, 0.038f);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.66f, 0.68f, 0.63f, 0.40f),
                new Color(0.48f, 0.52f, 0.50f, 0.28f));
            main.gravityModifier = 0f;
            // Pause, not PauseAndCatchup: he is off-screen most of the
            // time, and catching up would fire a lap's worth of smoke
            // out of the bowl the moment the player turns round.
            main.cullingMode = ParticleSystemCullingMode.Pause;

            plumeEmission = plume.emission;
            plumeEmission.enabled = true;
            plumeEmission.rateOverTime = PlumeRestRate;
            plumeEmission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = plume.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.010f;
            shape.length = 0.014f;

            // Still air over still water: the plume climbs and drifts,
            // it never streams. The lake is the one precinct whose whole
            // water contract is that nothing is flowing.
            ParticleSystem.VelocityOverLifetimeModule velocity =
                plume.velocityOverLifetime;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.010f, 0.014f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.030f, 0.070f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.014f, 0.014f);
            velocity.enabled = true;

            ParticleSystem.NoiseModule noise = plume.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.030f, 0.062f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.014f, 0.036f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.030f, 0.062f);
            noise.frequency = 0.30f;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.16f);

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
                    new Keyframe(0.24f, 1.20f),
                    new Keyframe(1f, 2.60f)));

            ParticleSystem.CollisionModule collision = plume.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = plume.lights;
            lights.enabled = false;
            ParticleSystem.TriggerModule trigger = plume.trigger;
            trigger.enabled = false;
            ParticleSystem.TrailModule trails = plume.trails;
            trails.enabled = false;
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
            renderer.minParticleSize = 0.004f;
            renderer.maxParticleSize = 0.10f;
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
                new Color(0.78f, 0.80f, 0.76f, 1f));
            plumeProperties.SetFloat(EdgePowerId, 1.45f);
            plumeProperties.SetFloat(NoiseStrengthId, 0.58f);
            plumeProperties.SetFloat(SoftParticleDistanceId, 0.12f);
            renderer.SetPropertyBlock(plumeProperties);

            plume.Play(true);
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

        private static Gradient CreateVisibilityGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(
                        new Color(0.72f, 0.75f, 0.72f),
                        0.6f),
                    new GradientColorKey(
                        new Color(0.55f, 0.60f, 0.58f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.10f),
                    new GradientAlphaKey(0.70f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
