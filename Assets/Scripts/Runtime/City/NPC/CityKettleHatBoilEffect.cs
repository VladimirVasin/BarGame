using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The kettle on the Kettle Hat walker's head, on the boil: the lid
    /// trembles and jumps on the vent, and a thin grey plume leaves the
    /// spout. It is on whatever the walker is doing - standing, walking,
    /// sitting on a bench, riding the bus, seen from the balcony - because
    /// a kettle boils because there is a fire under it, not because its
    /// owner reached a particular clip.
    ///
    /// The motion is a pure <see cref="KettleBoilModel"/> fed the SAME
    /// delta the presentation advances its graph with, through the
    /// presentation's <c>Advanced</c> event. That is what keeps the boil
    /// the walker's own: a distant walker's body runs up to 2.75x fast,
    /// and a lid on its own clock would visibly drift out of step with
    /// the head carrying it. Lid and steam share one phase for the same
    /// reason the fisherman's ember and plume do.
    ///
    /// The lid is moved by rotating the editor-built pivot the lid and
    /// knob are skinned to about the lid's own measured centre, in the
    /// head bone's frame. Every metric lift goes through
    /// InverseTransformVector: under the 100x FBX root a metre is 0.01
    /// of the head's units, and a constant written into a bone-child
    /// localPosition is the mistake this project has already made once.
    ///
    /// The steam is a code-built ParticleSystem on the shared atmosphere
    /// material, never a Light and never authored into the prefab; the
    /// prefab is validated to carry neither. It is created lazily, only
    /// in play mode, so the EditMode tests that build the pool stay
    /// inert and never touch the atmosphere material. It always
    /// simulates - a pooled walker is off-screen most of its life and
    /// paused steam would trail behind him the moment he re-entered the
    /// frame - and it switches to local simulation while he is seated
    /// or aboard the bus, with a lower, shorter rise, so the plume rides
    /// the cabin instead of streaming out of its roof.
    ///
    /// Attached by <see cref="CityPedestrianFactory"/> to a live object
    /// that is deactivated a moment later, so every Unity callback is
    /// gated on <see cref="IsInitialized"/>. Pool release and rebind are
    /// the everyday path: the steam is stopped and cleared on every
    /// disable and enable, and the first play after an enable waits for
    /// the first spout follow in LateUpdate so nothing puffs at the
    /// pool root.
    /// </summary>
    [DefaultExecutionOrder(350)]
    [DisallowMultipleComponent]
    public sealed class CityKettleHatBoilEffect : MonoBehaviour
    {
        public const string SteamObjectName = "Kettle Steam";
        public const int SteamMaximumParticles = 40;

        /// <summary>Particles thrown in one go when a vent fires, on the
        /// street and inside the cabin.</summary>
        public const int StreetBurstCount = KettleBoilModel.VentBurstCount;
        public const int CabinBurstCount = 4;

        /// <summary>Rise of the steam, on the street and in the cabin. The
        /// cabin figures keep the plume under the roof: about 0.36 m of
        /// climb over a lifetime, against roughly 0.7 m from the spout to
        /// the ceiling.</summary>
        public const float StreetRiseMinimum = 0.18f;
        public const float StreetRiseMaximum = 0.30f;
        public const float CabinRiseMinimum = 0.08f;
        public const float CabinRiseMaximum = 0.14f;
        public const float StreetLifetimeMinimum = 1.3f;
        public const float StreetLifetimeMaximum = 1.9f;
        public const float CabinLifetimeMinimum = 0.9f;
        public const float CabinLifetimeMaximum = 1.2f;
        public const float StreetSpeedMinimum = 0.16f;
        public const float StreetSpeedMaximum = 0.26f;
        public const float CabinSpeedMinimum = 0.10f;
        public const float CabinSpeedMaximum = 0.16f;

        /// <summary>How far the plume's launch axis leans from the spout
        /// line toward straight up: half way.</summary>
        public const float SpoutToVerticalBlend = 0.5f;

        private const float SteamSizeMinimum = 0.07f;
        private const float SteamSizeMaximum = 0.10f;
        private const float SteamConeAngle = 9f;
        private const float SteamConeRadius = 0.012f;
        private const float SteamConeLength = 0.02f;
        private const float SteamDriftMaximum = 0.02f;
        private const float SteamNoiseFrequency = 0.35f;
        private const float SteamNoiseScrollSpeed = 0.2f;
        private const float SteamMinimumParticleSize = 0.004f;
        private const float SteamMaximumParticleSize = 0.12f;
        private const float SteamEdgePower = 1.6f;
        private const float SteamNoiseStrength = 0.5f;
        private const float SteamSoftParticleDistance = 0.14f;
        private const uint SteamSeedSalt = 0x4B45544Cu; // "KETL"
        private const uint SteamSeedMixer = 0x9E3779B1u;
        private const float DegenerateAxisSquared = 0.000001f;
        private const float ParallelToUpDot = 0.98f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int EdgePowerId =
            Shader.PropertyToID("_EdgePower");
        private static readonly int NoiseStrengthId =
            Shader.PropertyToID("_NoiseStrength");
        private static readonly int SoftParticleDistanceId =
            Shader.PropertyToID("_SoftParticleDistance");

        private CityPedestrianPresentation presentation;
        private CityKettleHatRigAnchors anchors;
        private CityPedestrianActor actor;
        private Transform head;
        private Transform lidPivot;
        private Transform spoutAnchor;
        private KettleBoilModel model;
        private uint seed;
        private ParticleSystem steam;
        private Transform steamTransform;
        private ParticleSystem.EmissionModule steamEmission;
        private bool pendingBurst;
        private bool playPending;
        private bool inCabin;

        public bool IsInitialized { get; private set; }
        public KettleBoilModel Model => model;
        public ParticleSystem Steam => steam;
        public Transform LidPivot => lidPivot;
        public Transform SpoutAnchor => spoutAnchor;

        /// <summary>Last lid lift written, in metres, for tests.</summary>
        public float LastLidLift { get; private set; }

        /// <summary>Last lid tilt written, in degrees, for tests.</summary>
        public Vector2 LastLidTilt { get; private set; }

        /// <summary>True while the steam simulates in local space because
        /// the walker is seated or aboard the bus.</summary>
        public bool IsInCabin => inCabin;

        public void Initialize(
            CityPedestrianPresentation configuredPresentation,
            CityKettleHatRigAnchors configuredAnchors,
            uint configuredSeed)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The kettle boil effect is already initialized.");
            }

            presentation = configuredPresentation != null
                ? configuredPresentation
                : throw new ArgumentNullException(
                    nameof(configuredPresentation));
            anchors = configuredAnchors != null
                ? configuredAnchors
                : throw new ArgumentNullException(
                    nameof(configuredAnchors));
            if (anchors.LidPivot == null || anchors.SpoutAnchor == null)
            {
                throw new InvalidOperationException(
                    "The kettle rig anchors must carry a lid pivot and a " +
                    "spout anchor.");
            }

            lidPivot = anchors.LidPivot;
            spoutAnchor = anchors.SpoutAnchor;
            head = lidPivot.parent;
            if (head == null)
            {
                throw new InvalidOperationException(
                    "The kettle lid pivot must hang off the head bone.");
            }

            seed = configuredSeed;
            model = new KettleBoilModel(seed);
            presentation.Advanced += OnAdvanced;
            IsInitialized = true;
            if (isActiveAndEnabled)
            {
                HandleEnabled();
            }
        }

        private void OnEnable()
        {
            if (!IsInitialized)
            {
                return;
            }

            HandleEnabled();
        }

        private void OnDisable()
        {
            if (!IsInitialized)
            {
                return;
            }

            StopAndClear();
            pendingBurst = false;
            playPending = false;
            anchors.ResetLid();
            ApplySpace(false);
        }

        private void OnDestroy()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (presentation != null)
            {
                presentation.Advanced -= OnAdvanced;
            }

            StopAndClear();
            pendingBurst = false;
            IsInitialized = false;
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            FollowSpout();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            FollowSpout();
            if (steam == null)
            {
                return;
            }

            bool cabin = IsCabinBound();
            if (cabin != inCabin)
            {
                ApplySpace(cabin);
                if (!playPending)
                {
                    steam.Play(true);
                }
            }

            steamEmission.rateOverTime = model.SteamRate;
            if (playPending)
            {
                steam.Play(true);
                playPending = false;
            }

            if (pendingBurst)
            {
                steam.Emit(cabin ? CabinBurstCount : StreetBurstCount);
                pendingBurst = false;
            }
        }

        private void HandleEnabled()
        {
            // The presentation moves under the actor at bind, after this
            // component was created in the pool, so the actor is looked up
            // here rather than once.
            actor = GetComponentInParent<CityPedestrianActor>();
            if (steam == null && Application.isPlaying)
            {
                CreateSteam();
            }

            StopAndClear();
            ApplySpace(false);
            pendingBurst = false;
            playPending = steam != null;
        }

        private void OnAdvanced(float deltaTime)
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                presentation == null ||
                presentation.Registry == null)
            {
                return;
            }

            model.Advance(deltaTime);
            WriteLid();
            if (model.VentJustFired)
            {
                pendingBurst = true;
            }
        }

        /// <summary>
        /// Rotates the pivot about the lid's measured centre `c` and lifts
        /// it along the kettle axis. A point `p` of the lid, head-local,
        /// lands at `R * (p - c) + c`, which is a local rotation of `R`
        /// with a local translation of `c - R * c`.
        /// </summary>
        private void WriteLid()
        {
            if (lidPivot == null || head == null)
            {
                return;
            }

            Vector2 tilt = model.LidTilt;
            float lift = model.LidLift;
            Quaternion rotation =
                Quaternion.AngleAxis(tilt.x, anchors.LidTiltAxisALocal) *
                Quaternion.AngleAxis(tilt.y, anchors.LidTiltAxisBLocal);
            Vector3 centre = anchors.LidCentreLocal;
            Vector3 liftWorld =
                head.TransformDirection(anchors.KettleAxisLocal).normalized *
                lift;
            lidPivot.localRotation = rotation;
            lidPivot.localPosition =
                centre - (rotation * centre) +
                head.InverseTransformVector(liftWorld);
            LastLidLift = lift;
            LastLidTilt = tilt;
        }

        private bool IsCabinBound()
        {
            return presentation != null &&
                   (presentation.IsSeated ||
                    (actor != null && actor.IsAttachedToVehicle));
        }

        private void FollowSpout()
        {
            if (steamTransform == null || spoutAnchor == null)
            {
                return;
            }

            Vector3 along = spoutAnchor.forward;
            if (along.sqrMagnitude <= DegenerateAxisSquared)
            {
                along = Vector3.up;
            }

            along.Normalize();
            Vector3 launch = Vector3.Slerp(
                along,
                Vector3.up,
                SpoutToVerticalBlend);
            if (launch.sqrMagnitude <= DegenerateAxisSquared)
            {
                launch = Vector3.up;
            }

            launch.Normalize();
            Vector3 reference = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(launch, reference)) > ParallelToUpDot)
            {
                reference = spoutAnchor.right;
            }

            steamTransform.SetPositionAndRotation(
                spoutAnchor.position,
                Quaternion.LookRotation(launch, reference));
            steamTransform.localScale = Vector3.one;
        }

        /// <summary>
        /// Street or cabin. Always stop-and-clear first: particles born in
        /// one simulation space and carried into the other jump.
        /// </summary>
        private void ApplySpace(bool cabin)
        {
            inCabin = cabin;
            if (steam == null)
            {
                return;
            }

            StopAndClear();
            ParticleSystem.MainModule main = steam.main;
            main.simulationSpace = cabin
                ? ParticleSystemSimulationSpace.Local
                : ParticleSystemSimulationSpace.World;
            main.startLifetime = cabin
                ? new ParticleSystem.MinMaxCurve(
                    CabinLifetimeMinimum,
                    CabinLifetimeMaximum)
                : new ParticleSystem.MinMaxCurve(
                    StreetLifetimeMinimum,
                    StreetLifetimeMaximum);
            main.startSpeed = cabin
                ? new ParticleSystem.MinMaxCurve(
                    CabinSpeedMinimum,
                    CabinSpeedMaximum)
                : new ParticleSystem.MinMaxCurve(
                    StreetSpeedMinimum,
                    StreetSpeedMaximum);
            ParticleSystem.VelocityOverLifetimeModule velocity =
                steam.velocityOverLifetime;
            velocity.y = cabin
                ? new ParticleSystem.MinMaxCurve(
                    CabinRiseMinimum,
                    CabinRiseMaximum)
                : new ParticleSystem.MinMaxCurve(
                    StreetRiseMinimum,
                    StreetRiseMaximum);
        }

        private void StopAndClear()
        {
            if (steam != null)
            {
                steam.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void CreateSteam()
        {
            // A child of the presentation ROOT at scale one, never of a
            // bone: a particle system under the 100x bone hierarchy would
            // simulate a hundred times too large.
            var host = new GameObject(SteamObjectName);
            host.transform.SetParent(presentation.transform, false);
            steamTransform = host.transform;
            steam = host.AddComponent<ParticleSystem>();
            steam.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            steam.useAutoRandomSeed = false;
            steam.randomSeed = SteamSeedSalt ^ (seed * SteamSeedMixer);

            ParticleSystem.MainModule main = steam.main;
            main.loop = true;
            main.prewarm = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = SteamMaximumParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                StreetLifetimeMinimum,
                StreetLifetimeMaximum);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                StreetSpeedMinimum,
                StreetSpeedMaximum);
            main.startSize = new ParticleSystem.MinMaxCurve(
                SteamSizeMinimum,
                SteamSizeMaximum);
            main.startRotation = new ParticleSystem.MinMaxCurve(
                0f,
                Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.80f, 0.82f, 0.80f, 0.30f),
                new Color(0.62f, 0.66f, 0.64f, 0.20f));
            main.gravityModifier = 0f;
            // AlwaysSimulate, unlike the fisherman's Pause: he is pooled
            // and moving, so a paused plume would be left standing in
            // the street where he was when the player looked away, and
            // a batch run without a renderer would never simulate at all.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            steamEmission = steam.emission;
            steamEmission.enabled = true;
            steamEmission.rateOverTime = KettleBoilModel.RestSteamRate;
            steamEmission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = steam.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = SteamConeAngle;
            shape.radius = SteamConeRadius;
            shape.length = SteamConeLength;

            ParticleSystem.VelocityOverLifetimeModule velocity =
                steam.velocityOverLifetime;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(
                -SteamDriftMaximum,
                SteamDriftMaximum);
            velocity.y = new ParticleSystem.MinMaxCurve(
                StreetRiseMinimum,
                StreetRiseMaximum);
            velocity.z = new ParticleSystem.MinMaxCurve(
                -SteamDriftMaximum,
                SteamDriftMaximum);
            velocity.enabled = true;

            ParticleSystem.NoiseModule noise = steam.noise;
            noise.enabled = true;
            noise.separateAxes = true;
            noise.strengthX = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            noise.strengthY = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
            noise.strengthZ = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            noise.frequency = SteamNoiseFrequency;
            noise.damping = true;
            noise.octaveCount = 1;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.scrollSpeed =
                new ParticleSystem.MinMaxCurve(SteamNoiseScrollSpeed);

            ParticleSystem.ColorOverLifetimeModule color =
                steam.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateVisibilityGradient());

            ParticleSystem.SizeOverLifetimeModule size =
                steam.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.6f),
                    new Keyframe(0.3f, 1.3f),
                    new Keyframe(1f, 2.5f)));

            DisableUnusedModules();
            ConfigureRenderer();
        }

        private void DisableUnusedModules()
        {
            ParticleSystem.CollisionModule collision = steam.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = steam.lights;
            lights.enabled = false;
            ParticleSystem.TriggerModule trigger = steam.trigger;
            trigger.enabled = false;
            ParticleSystem.TrailModule trails = steam.trails;
            trails.enabled = false;
            ParticleSystem.ExternalForcesModule externalForces =
                steam.externalForces;
            externalForces.enabled = false;
            ParticleSystem.SubEmittersModule subEmitters =
                steam.subEmitters;
            subEmitters.enabled = false;
            ParticleSystem.TextureSheetAnimationModule textureSheet =
                steam.textureSheetAnimation;
            textureSheet.enabled = false;
            ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity =
                steam.limitVelocityOverLifetime;
            limitVelocity.enabled = false;
            ParticleSystem.InheritVelocityModule inheritVelocity =
                steam.inheritVelocity;
            inheritVelocity.enabled = false;
            ParticleSystem.ForceOverLifetimeModule forceOverLifetime =
                steam.forceOverLifetime;
            forceOverLifetime.enabled = false;
            ParticleSystem.RotationOverLifetimeModule rotationOverLifetime =
                steam.rotationOverLifetime;
            rotationOverLifetime.enabled = false;
        }

        private void ConfigureRenderer()
        {
            var renderer = steam.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = SteamMinimumParticleSize;
            renderer.maxParticleSize = SteamMaximumParticleSize;
            renderer.enableGPUInstancing = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;

            // Grey on purpose, a shade under the enamel: the white kettle
            // is the one bright detail on this walker and the steam must
            // not out-shine it.
            var properties = new MaterialPropertyBlock();
            properties.SetColor(
                BaseColorId,
                new Color(0.82f, 0.84f, 0.82f, 1f));
            properties.SetFloat(EdgePowerId, SteamEdgePower);
            properties.SetFloat(NoiseStrengthId, SteamNoiseStrength);
            properties.SetFloat(
                SoftParticleDistanceId,
                SteamSoftParticleDistance);
            renderer.SetPropertyBlock(properties);
        }

        private static Gradient CreateVisibilityGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(
                        new Color(0.74f, 0.77f, 0.75f),
                        0.6f),
                    new GradientColorKey(
                        new Color(0.58f, 0.62f, 0.60f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.55f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
