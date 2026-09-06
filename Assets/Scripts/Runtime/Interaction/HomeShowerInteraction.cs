using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public enum HomeShowerScenePhase
    {
        Idle = 0,
        CameraIn = 1,
        Approach = 2,
        Settle = 3,
        Wash = 4,
        WaterOff = 5,
        Straighten = 6,
        DripHold = 7,
        StepOut = 8,
        CameraOut = 9,
        Completed = 10
    }

    /// <summary>
    /// The shower, beat by beat: the moment E is pressed the camera flies
    /// into the hero's own eyes while he sets off for the stall; once the
    /// lens is inside his head his clothes come off, unseen; he walks into
    /// the stall, braces on the tile under the water, and washes; E shuts
    /// the tap; he straightens and stands still while the last drops fall;
    /// he walks out to the opening, dresses, and the camera goes home. Pure
    /// and EditMode-testable: fixed phases carry their overshoot into the
    /// next like the toilet's timeline, open phases wait for the scene to
    /// report the dock, the rendered neutral frame or the walk out.
    /// </summary>
    public sealed class HomeShowerSceneTimeline
    {
        public const float CameraInSeconds = 0.9f;
        public const float UndressGuardSeconds = 0.45f;
        public const float PoseRaiseSeconds = 0.7f;
        public const float WaterStartSeconds = 1.0f;
        public const float MinimumWashSeconds = 6f;
        public const float AutomaticWashSeconds = 12f;
        public const float WaterOffSeconds = 0.9f;
        public const float ValveReachSeconds = 0.45f;
        public const float WaterCutStartSeconds = 0.35f;
        public const float StraightenSeconds = 0.6f;
        public const float DripHoldSeconds = 3.0f;
        public const float CameraOutSeconds = 1.4f;
        public const float SteamLagSeconds = 1.5f;

        /// <summary>
        /// Where the eyes point: level-ish on the walks, hanging with the
        /// head under the water, and down at the tray while he stands for
        /// the drips (the nozzle is above and behind his own head, so the
        /// tray and his feet are what there is to look at).
        /// </summary>
        public const float WalkPitchDegrees = 6f;
        public const float WashPitchDegrees = 38f;
        public const float HoldPitchDegrees = 55f;

        private float phaseElapsed;
        private bool dockReached;
        private bool settleFrameRendered;
        private bool valveCuePending;
        private float stopStartWater = 1f;
        private float steam;
        private float dripClock;

        public HomeShowerScenePhase Phase { get; private set; } =
            HomeShowerScenePhase.Idle;
        public float PhaseElapsed => phaseElapsed;
        public bool ReachedMinimumWash { get; private set; }
        public bool DockReached => dockReached;
        public bool IsCompleted => Phase == HomeShowerScenePhase.Completed;
        public bool StopPromptVisible => Phase == HomeShowerScenePhase.Wash;

        /// <summary>Seconds since the tap started closing; the drip's own clock.</summary>
        public float DripClock => dripClock;

        /// <summary>Drops are falling: from the tap closing until the hold has run dry.</summary>
        public bool IsDripping =>
            Phase >= HomeShowerScenePhase.WaterOff &&
            Phase <= HomeShowerScenePhase.DripHold;

        /// <summary>The lens is inside his head: from the end of the fly-in until it starts back.</summary>
        public bool IsInsideHead =>
            Phase > HomeShowerScenePhase.CameraIn &&
            Phase < HomeShowerScenePhase.CameraOut;

        public float CameraBlend
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.CameraIn:
                        return Smooth(phaseElapsed / CameraInSeconds);
                    case HomeShowerScenePhase.Approach:
                    case HomeShowerScenePhase.Settle:
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                    case HomeShowerScenePhase.Straighten:
                    case HomeShowerScenePhase.DripHold:
                    case HomeShowerScenePhase.StepOut:
                        return 1f;
                    case HomeShowerScenePhase.CameraOut:
                        return 1f - Smooth(phaseElapsed / CameraOutSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>The shared breathing drift: on under the water, gone with the straighten; the walks move the lens themselves.</summary>
        public float DriftWeight
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                        return 1f;
                    case HomeShowerScenePhase.Straighten:
                        return 1f - Smooth(phaseElapsed / StraightenSeconds);
                    default:
                        return 0f;
                }
            }
        }

        public float PoseWeight
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                        return Smooth(phaseElapsed / PoseRaiseSeconds);
                    case HomeShowerScenePhase.WaterOff:
                        return 1f;
                    case HomeShowerScenePhase.Straighten:
                        return 1f - Smooth(phaseElapsed / StraightenSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>The base pitch of the first-person lens, degrees below the horizontal.</summary>
        public float ViewPitchDegrees
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                        return Mathf.Lerp(WalkPitchDegrees, WashPitchDegrees, PoseWeight);
                    case HomeShowerScenePhase.WaterOff:
                        return WashPitchDegrees;
                    case HomeShowerScenePhase.Straighten:
                        return Mathf.Lerp(
                            WashPitchDegrees,
                            HoldPitchDegrees,
                            Smooth(phaseElapsed / StraightenSeconds));
                    case HomeShowerScenePhase.DripHold:
                        return HoldPitchDegrees;
                    default:
                        return WalkPitchDegrees;
                }
            }
        }

        /// <summary>The right hand's journey from the tile to the tap.</summary>
        public float ValveReach
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOff:
                        return Smooth(phaseElapsed / ValveReachSeconds);
                    case HomeShowerScenePhase.Straighten:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>How far the cross handle has turned, once the hand is on it.</summary>
        public float ValveTurn
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOff:
                        return Smooth(
                            (phaseElapsed - ValveReachSeconds) /
                            (WaterOffSeconds - ValveReachSeconds));
                    case HomeShowerScenePhase.Straighten:
                    case HomeShowerScenePhase.DripHold:
                    case HomeShowerScenePhase.StepOut:
                    case HomeShowerScenePhase.CameraOut:
                    case HomeShowerScenePhase.Completed:
                        return 1f;
                    default:
                        return 0f;
                }
            }
        }

        public float WaterAmount
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                        return Mathf.Clamp01(phaseElapsed / WaterStartSeconds);
                    case HomeShowerScenePhase.WaterOff:
                        return stopStartWater * (1f - Smooth(
                            (phaseElapsed - WaterCutStartSeconds) /
                            (WaterOffSeconds - WaterCutStartSeconds)));
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>The steam lags the water by a slow exponential.</summary>
        public float SteamAmount => steam;

        public float SwayEnvelope
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                        return Smooth(phaseElapsed / PoseRaiseSeconds);
                    case HomeShowerScenePhase.WaterOff:
                        return 1f - Smooth(phaseElapsed / WaterOffSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>
        /// The patter of drops before he stands still, drops per second;
        /// the static hold runs its own schedule in
        /// <see cref="HomeShowerDripModel"/>.
        /// </summary>
        public float DripSteadyRate
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOff:
                        return HomeShowerDripModel.SteadyDropsPerSecond * Smooth(
                            (phaseElapsed - WaterCutStartSeconds) /
                            (WaterOffSeconds - WaterCutStartSeconds));
                    case HomeShowerScenePhase.Straighten:
                        return HomeShowerDripModel.SteadyDropsPerSecond;
                    default:
                        return 0f;
                }
            }
        }

        public void Begin()
        {
            Reset();
            Phase = HomeShowerScenePhase.CameraIn;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            float remaining = Mathf.Max(0f, deltaTime);
            int guard = 0;
            while (Phase != HomeShowerScenePhase.Idle && !IsCompleted && guard++ < 32)
            {
                float duration = Duration(Phase);
                if (float.IsPositiveInfinity(duration))
                {
                    // Open phases take the whole step and wait for an event.
                    Integrate(remaining);
                    remaining = 0f;
                    if (!TryLeaveOpenPhase())
                    {
                        break;
                    }

                    continue;
                }

                float step = Mathf.Min(remaining, Mathf.Max(0f, duration - phaseElapsed));
                Integrate(step);
                remaining -= step;
                if (Phase == HomeShowerScenePhase.Wash &&
                    phaseElapsed >= MinimumWashSeconds)
                {
                    ReachedMinimumWash = true;
                }

                if (phaseElapsed < duration)
                {
                    break;
                }

                // Carry the overshoot: hitches never lengthen the action.
                LeaveFixedPhase();
                if (remaining <= 0f)
                {
                    break;
                }
            }
        }

        /// <summary>The base has walked him onto the dock; remembered if the fly-in is still running.</summary>
        public void NotifyDockReached()
        {
            if (Phase == HomeShowerScenePhase.CameraIn ||
                Phase == HomeShowerScenePhase.Approach)
            {
                dockReached = true;
                if (Phase == HomeShowerScenePhase.Approach)
                {
                    SetPhase(HomeShowerScenePhase.Settle);
                }
            }
        }

        /// <summary>The neutral endpoint has been rendered once at the dock.</summary>
        public void NotifySettleFrameRendered()
        {
            if (Phase == HomeShowerScenePhase.Settle)
            {
                settleFrameRendered = true;
            }
        }

        /// <summary>He has walked out to the opening and turned to the room.</summary>
        public void NotifyWalkArrived()
        {
            if (Phase == HomeShowerScenePhase.StepOut)
            {
                SetPhase(HomeShowerScenePhase.CameraOut);
            }
        }

        /// <summary>
        /// E while washing: the tap closes from wherever the water is.
        /// Refused everywhere else, so the base keeps the input armed.
        /// </summary>
        public bool RequestFinish()
        {
            if (Phase != HomeShowerScenePhase.Wash)
            {
                return false;
            }

            BeginWaterOff();
            return true;
        }

        /// <summary>One-shot: the hand has closed on the tap.</summary>
        public bool ConsumeValveCue()
        {
            if (!valveCuePending)
            {
                return false;
            }

            valveCuePending = false;
            return true;
        }

        public void Reset()
        {
            Phase = HomeShowerScenePhase.Idle;
            phaseElapsed = 0f;
            dockReached = false;
            settleFrameRendered = false;
            valveCuePending = false;
            stopStartWater = 1f;
            steam = 0f;
            dripClock = 0f;
            ReachedMinimumWash = false;
        }

        private static float Duration(HomeShowerScenePhase phase)
        {
            switch (phase)
            {
                case HomeShowerScenePhase.CameraIn: return CameraInSeconds;
                case HomeShowerScenePhase.Wash: return AutomaticWashSeconds;
                case HomeShowerScenePhase.WaterOff: return WaterOffSeconds;
                case HomeShowerScenePhase.Straighten: return StraightenSeconds;
                case HomeShowerScenePhase.DripHold: return DripHoldSeconds;
                case HomeShowerScenePhase.CameraOut: return CameraOutSeconds;
                default: return float.PositiveInfinity;
            }
        }

        private bool TryLeaveOpenPhase()
        {
            switch (Phase)
            {
                case HomeShowerScenePhase.Approach:
                    if (dockReached)
                    {
                        SetPhase(HomeShowerScenePhase.Settle);
                        return true;
                    }

                    return false;
                case HomeShowerScenePhase.Settle:
                    if (settleFrameRendered)
                    {
                        SetPhase(HomeShowerScenePhase.Wash);
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        private void LeaveFixedPhase()
        {
            switch (Phase)
            {
                case HomeShowerScenePhase.CameraIn:
                    SetPhase(HomeShowerScenePhase.Approach);
                    break;
                case HomeShowerScenePhase.Wash:
                    ReachedMinimumWash = true;
                    BeginWaterOff();
                    break;
                case HomeShowerScenePhase.WaterOff:
                    SetPhase(HomeShowerScenePhase.Straighten);
                    break;
                case HomeShowerScenePhase.Straighten:
                    SetPhase(HomeShowerScenePhase.DripHold);
                    break;
                case HomeShowerScenePhase.DripHold:
                    SetPhase(HomeShowerScenePhase.StepOut);
                    break;
                case HomeShowerScenePhase.CameraOut:
                    SetPhase(HomeShowerScenePhase.Completed);
                    break;
            }
        }

        private void BeginWaterOff()
        {
            stopStartWater = WaterAmount;
            valveCuePending = true;
            SetPhase(HomeShowerScenePhase.WaterOff);
        }

        private void Integrate(float step)
        {
            if (step <= 0f)
            {
                return;
            }

            phaseElapsed += step;
            if (IsDripping)
            {
                dripClock += step;
            }

            float blend = 1f - Mathf.Exp(-step / SteamLagSeconds);
            steam += (WaterAmount - steam) * blend;
        }

        private void SetPhase(HomeShowerScenePhase phase)
        {
            Phase = phase;
            phaseElapsed = 0f;
        }

        private static float Smooth(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    /// <summary>
    /// Code-built water on the shared atmosphere material, no lights and
    /// no colliders: the stream falling from the nozzle plate onto the
    /// hero's back under gravity, slow steam over the stall, the drops a
    /// shut tap sheds and their splashes in the basin. Rates are written
    /// every frame from the timeline; drops and splashes are emitted one
    /// by one from the drip model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerWaterEffect : MonoBehaviour
    {
        public const float StreamRatePerSecond = 48f;
        public const float SteamRatePerSecond = 7f;
        public const int SplashParticlesPerLanding = 2;

        private ParticleSystem stream;
        private ParticleSystem steam;
        private ParticleSystem drips;
        private ParticleSystem splash;
        private bool isInitialized;

        public bool IsEmitting { get; private set; }
        public bool IsDripping { get; private set; }
        public int DropsEmitted { get; private set; }
        public int SplashesEmitted { get; private set; }

        /// <summary>Live particles in flight, for tests that must see water, not a flag.</summary>
        public int StreamParticleCount => stream != null ? stream.particleCount : 0;
        public int DripParticleCount => drips != null ? drips.particleCount : 0;
        public int SteamParticleCount => steam != null ? steam.particleCount : 0;

        public void Initialize(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            // The plate faces 35° forward and down; the water leaves it
            // nearly straight down with a little of that lean and falls
            // under gravity onto the nape and the basin.
            stream = CreateSystem(
                parent,
                "Shower Water Stream",
                HomeShowerFraming.DripOrigin,
                Quaternion.Euler(105f, 0f, 0f),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(0.55f, 0.62f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.9f, 1.2f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
                    main.gravityModifier = 1f;
                    main.maxParticles = 80;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.58f, 0.63f, 0.64f, 0.35f),
                        new Color(0.52f, 0.58f, 0.60f, 0.22f));
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 6f;
                    shape.radius = 0.045f;
                    ParticleSystemRenderer renderer = system
                        .GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode =
                        ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 3.5f;
                });
            steam = CreateSystem(
                parent,
                "Shower Steam",
                new Vector3(3.90f, 0.55f, 2.90f),
                Quaternion.identity,
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(2.0f, 2.8f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                    main.maxParticles = 20;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.50f, 0.55f, 0.55f, 0.10f),
                        new Color(0.45f, 0.52f, 0.52f, 0.05f));
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(0.9f, 0.3f, 0.9f);
                });
            drips = CreateSystem(
                parent,
                "Shower Drip",
                HomeShowerFraming.DripOrigin,
                Quaternion.Euler(90f, 0f, 0f),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(HomeShowerDripModel.FallSeconds);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.05f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.012f, 0.018f);
                    main.gravityModifier = 1f;
                    main.maxParticles = 12;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.60f, 0.65f, 0.66f, 0.45f));
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.enabled = false;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 0f;
                    shape.radius = 0.02f;
                    ParticleSystemRenderer renderer = system
                        .GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode =
                        ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 2.0f;
                });
            splash = CreateSystem(
                parent,
                "Shower Drip Splash",
                HomeShowerFraming.BasinLanding,
                Quaternion.Euler(-90f, 0f, 0f),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(0.18f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.008f, 0.012f);
                    main.gravityModifier = 1f;
                    main.maxParticles = 16;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.60f, 0.65f, 0.66f, 0.40f));
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.enabled = false;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 35f;
                    shape.radius = 0.01f;
                });
            isInitialized = true;
            StopAndClear();
        }

        /// <summary>The scene has begun: every system runs, emitting nothing yet.</summary>
        public void Begin()
        {
            if (!isInitialized)
            {
                return;
            }

            DropsEmitted = 0;
            SplashesEmitted = 0;
            EnsurePlaying(stream);
            EnsurePlaying(steam);
            EnsurePlaying(drips);
            EnsurePlaying(splash);
            SetWater(0f, 0f);
        }

        public void SetWater(float water, float steamAmount)
        {
            if (!isInitialized)
            {
                return;
            }

            float flow = Mathf.Clamp01(water);
            IsEmitting = flow > 0.05f;
            SetRate(stream, StreamRatePerSecond * flow);
            SetRate(steam, SteamRatePerSecond * Mathf.Clamp01(steamAmount));
        }

        public void SetDripping(bool dripping)
        {
            IsDripping = dripping;
        }

        public void EmitDrops(int count)
        {
            if (!isInitialized || count <= 0)
            {
                return;
            }

            EnsurePlaying(drips);
            drips.Emit(count);
            DropsEmitted += count;
        }

        public void EmitSplashes(int landings)
        {
            if (!isInitialized || landings <= 0)
            {
                return;
            }

            EnsurePlaying(splash);
            splash.Emit(landings * SplashParticlesPerLanding);
            SplashesEmitted += landings;
        }

        public void StopAndClear()
        {
            if (!isInitialized)
            {
                return;
            }

            IsEmitting = false;
            IsDripping = false;
            Clear(stream);
            Clear(steam);
            Clear(drips);
            Clear(splash);
        }

        private static void EnsurePlaying(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
            {
                system.Play();
            }
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
        }

        private static void Clear(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            system.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static ParticleSystem CreateSystem(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            Action<ParticleSystem> configure)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localRotation = localRotation;
            ParticleSystem system =
                holder.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            // Gravity pulls in the world, so the drops simulate there.
            main.simulationSpace =
                ParticleSystemSimulationSpace.World;
            configure(system);
            ParticleSystemRenderer renderer =
                system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial =
                CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }
    }

    /// <summary>
    /// The shower scene on the shared bathroom skeleton, seen from the
    /// hero's own eyes: E flies the camera into his head while the base
    /// walks him to the stall through the opening beside the gathered
    /// curtain; his clothes come off once the lens is inside; he braces
    /// on the tile and washes; E shuts the tap; he straightens, stands
    /// still through the last drops, walks out to the opening, dresses
    /// with the lens still inside, and the camera returns. A finished
    /// wash relieves stress.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class HomeShowerInteraction :
        HomeBathroomSceneInteraction
    {
        public const string TakePromptKey = "interaction.take_shower";
        public const string StopPromptKeyName =
            "interaction.stop_shower";
        public const int StressRelief = 12;
        public const string HotHandleName = "Home Bathroom Shower Mixer Handle Hot";
        public const float ValveTurnDegrees = 90f;

        /// <summary>
        /// The curtain never moves any more; it stays gathered at the
        /// rail's left end, tight enough to leave the opening the hero
        /// walks through.
        /// </summary>
        public const float GatheredCurtainScale = 0.40f;

        private readonly HomeShowerSceneTimeline timeline =
            new HomeShowerSceneTimeline();
        private readonly HomeShowerDripModel drips =
            new HomeShowerDripModel();

        private HomeShowerWaterEffect waterEffect;
        private HomeShowerWashPose washPose;
        private HomeShowerFirstPersonView view;
        private Player3DAssetRegistry registry;
        private Player3DBathingAppearance lease;
        private Transform hotHandle;
        private Quaternion hotHandleRest = Quaternion.identity;
        private bool previousHandoff;
        private bool ownsHandoff;
        private bool braceCaptured;
        private bool occlusionCaptured;
        private bool previousOcclusionEnabled;
        private bool stepOutCornerPassed;
        private bool holdBegun;
        private bool redressed;

        public HomeShowerSceneTimeline Timeline => timeline;
        public HomeShowerDripModel Drips => drips;
        public HomeShowerWaterEffect WaterEffect => waterEffect;
        public HomeShowerWashPose WashPose => washPose;
        public HomeShowerFirstPersonView View => view;
        public bool IsUndressed => lease != null;
        public bool HoldsOcclusionLease => occlusionCaptured;
        public bool HoldsHandoff => ownsHandoff;
        public float HotHandleTurn => timeline.ValveTurn;
        public override string PromptKey =>
            OwnsScene ? string.Empty : TakePromptKey;

        protected override string StopPromptKey => StopPromptKeyName;

        /// <summary>The eye at the moment of E: the fly-in's Bézier is captured against it; the live eye then leads it.</summary>
        protected override Vector3 CameraLocalPosition =>
            view != null && view.TryGetEyeLocal(Home.transform, out Vector3 eye, out _)
                ? eye
                : HomeShowerFraming.Stand + Vector3.up * 1.6f;

        protected override Vector3 CameraLocalLookAt =>
            view != null && view.TryGetEyeLocal(Home.transform, out Vector3 eye, out Vector3 forward)
                ? eye + forward
                : HomeShowerFraming.Stand + Vector3.up * 1.6f + Vector3.forward;

        protected override float CameraFieldOfView =>
            HomeShowerFirstPersonView.FieldOfView;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => timeline.DriftWeight;
        protected override bool CameraLeadsApproach => true;
        protected override bool SceneCompleted => timeline.IsCompleted;
        protected override bool StopPromptVisible => timeline.StopPromptVisible;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            InitializeScene(
                homeRoot,
                HomeShowerFraming.Dock,
                Quaternion.LookRotation(Vector3.forward, Vector3.up),
                HomeShowerFraming.Exit,
                Quaternion.LookRotation(Vector3.back, Vector3.up),
                HomeShowerFraming.Stand);
            Transform room = homeRoot.Room != null ? homeRoot.Room : homeRoot.transform;
            waterEffect = gameObject.AddComponent<HomeShowerWaterEffect>();
            waterEffect.Initialize(room);
            washPose = gameObject.AddComponent<HomeShowerWashPose>();
            view = gameObject.AddComponent<HomeShowerFirstPersonView>();
            hotHandle = room.Find(HotHandleName);
            if (hotHandle != null)
            {
                hotHandleRest = hotHandle.localRotation;
            }
        }

        /// <summary>
        /// A hero outside the stall walks in through the opening beside
        /// the curtain rather than through its side panels; one already
        /// inside goes straight to the dock.
        /// </summary>
        protected override bool TryGetApproachWaypoint(
            Vector3 heroPosition,
            out Vector3 waypoint,
            out float arrivalRadius)
        {
            Vector3 local = Home.transform.InverseTransformPoint(heroPosition);
            waypoint = HomeShowerFraming.Waypoint;
            arrivalRadius = HomeShowerFraming.WaypointArrivalRadius;
            return !HomeShowerFraming.IsInsideStall(local);
        }

        /// <summary>All fallible preparation precedes the modal capture.</summary>
        protected override bool PrepareScene()
        {
            if (Home.Player.GameObject == null ||
                !(Home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null)
            {
                return false;
            }

            registry = visual.Registry;
            if (washPose == null || view == null)
            {
                return false;
            }

            if (!washPose.IsInitialized || !washPose.HasBridges)
            {
                if (!washPose.Initialize(Home))
                {
                    return false;
                }
            }

            if (!view.IsPrepared && !view.Initialize(Home))
            {
                return false;
            }

            return hotHandle != null && !Player3DBathingAppearance.IsActive;
        }

        protected override void OnSceneCaptured()
        {
            timeline.Begin();
            drips.Reset();
            braceCaptured = false;
            stepOutCornerPassed = false;
            holdBegun = false;
            redressed = false;
            waterEffect?.Begin();
            // The occluder cutaways would dither the curtain and the
            // fixtures around the hero with the lens inside his head.
            if (Home.PlayerOcclusion != null)
            {
                previousOcclusionEnabled = Home.PlayerOcclusion.enabled;
                occlusionCaptured = true;
                Home.PlayerOcclusion.enabled = false;
                Home.PlayerOcclusion.ClearOcclusion();
            }

            view.Begin(timeline.ViewPitchDegrees);
        }

        protected override void OnApproachAdvance(float deltaTime)
        {
            Tick(deltaTime);
        }

        protected override void OnSceneBegin()
        {
            // The base settled him at the dock, facing the tile.
            timeline.NotifyDockReached();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            Tick(deltaTime);
        }

        private void Tick(float deltaTime)
        {
            timeline.Advance(deltaTime);
            ApplyPhaseEntries();
            if (timeline.Phase == HomeShowerScenePhase.StepOut &&
                !AdvanceStepOut(deltaTime))
            {
                return;
            }

            if (!OwnsScene)
            {
                return;
            }

            ApplyPhaseEntries();
            ApplyEffects(deltaTime);
        }

        /// <summary>
        /// One frame of the walk out through the stall's opening: the
        /// corner first, then the turn to the room. False when the scene
        /// was cancelled by a stall, so the caller stops touching it.
        /// </summary>
        private bool AdvanceStepOut(float deltaTime)
        {
            if (!stepOutCornerPassed)
            {
                HomeGuidedWalkStep corner = AdvanceGuidedWaypoint(
                    HomeShowerFraming.Waypoint,
                    HomeShowerFraming.WaypointArrivalRadius,
                    deltaTime);
                if (corner == HomeGuidedWalkStep.Stalled)
                {
                    CancelScene();
                    return false;
                }

                if (corner == HomeGuidedWalkStep.Arrived)
                {
                    stepOutCornerPassed = true;
                }

                return true;
            }

            HomeGuidedWalkStep step = AdvanceGuidedWalk(
                HomeShowerFraming.Exit,
                Quaternion.LookRotation(Vector3.back, Vector3.up),
                deltaTime);
            if (step == HomeGuidedWalkStep.Stalled)
            {
                CancelScene();
                return false;
            }

            if (step == HomeGuidedWalkStep.Arrived)
            {
                timeline.NotifyWalkArrived();
            }

            return true;
        }

        private void ApplyPhaseEntries()
        {
            HomeShowerScenePhase phase = timeline.Phase;
            if (timeline.IsInsideHead && lease == null && !redressed)
            {
                TryUndress();
            }

            if (phase == HomeShowerScenePhase.Settle && !braceCaptured)
            {
                // Lock first: the lock writes the Idle neutral synchronously,
                // and the capture must read that, not the last stride.
                previousHandoff = Home.Player.Visual.InteractionHandoffLocked;
                Home.Player.Visual.SetInteractionHandoffLocked(true);
                ownsHandoff = true;
                washPose?.Capture();
                braceCaptured = true;
            }

            if (phase >= HomeShowerScenePhase.Straighten && ownsHandoff)
            {
                // Released a phase early: the unlock lands after the next
                // presentation LateUpdate, before he stands for the drips.
                ReleaseHandoff();
            }

            if (phase >= HomeShowerScenePhase.DripHold && !holdBegun)
            {
                holdBegun = true;
                washPose?.End();
                braceCaptured = false;
                drips.BeginHold();
            }

            if (phase >= HomeShowerScenePhase.StepOut && lease != null)
            {
                // Dressed for the walk out, with the lens still in his head.
                Redress(true);
            }
        }

        private void TryUndress()
        {
            if (lease != null)
            {
                return;
            }

            bool inside = view != null && view.IsHeadHidden;
            if (!inside &&
                timeline.Phase == HomeShowerScenePhase.Approach &&
                timeline.PhaseElapsed < HomeShowerSceneTimeline.UndressGuardSeconds)
            {
                return;
            }

            if (!inside)
            {
                GameLog.Warning(
                    "home",
                    "shower_undress_in_view",
                    GameLog.Field("scene", gameObject.name));
            }

            lease = Player3DBathingAppearance.Apply(registry, true);
            washPose?.SetBridgesShown(true);
        }

        private void Redress(bool expectInsideHead)
        {
            if (lease == null)
            {
                return;
            }

            if (expectInsideHead && (view == null || !view.IsHeadHidden))
            {
                GameLog.Warning(
                    "home",
                    "shower_redress_in_view",
                    GameLog.Field("scene", gameObject.name));
            }

            lease.Restore();
            lease = null;
            redressed = true;
            washPose?.SetBridgesShown(false);
        }

        private void ApplyEffects(float deltaTime)
        {
            float water = timeline.WaterAmount;
            Home.Soundscape?.SetShowerWaterAmount(water);
            waterEffect?.SetWater(water, timeline.SteamAmount);
            int drops = timeline.IsDripping
                ? this.drips.Advance(deltaTime, timeline.DripSteadyRate)
                : 0;
            waterEffect?.EmitDrops(drops);
            waterEffect?.EmitSplashes(this.drips.ConsumeLandings());
            waterEffect?.SetDripping(
                timeline.IsDripping &&
                (this.drips.PendingLandings > 0 || !this.drips.IsDry));
            timeline.ConsumeValveCue();
            if (hotHandle != null)
            {
                hotHandle.localRotation = hotHandleRest *
                    Quaternion.Euler(0f, ValveTurnDegrees * timeline.ValveTurn, 0f);
            }
        }

        protected override void OnScenePresentation(float deltaTime)
        {
            if (washPose == null || view == null)
            {
                return;
            }

            HomeShowerScenePhase phase = timeline.Phase;
            switch (phase)
            {
                case HomeShowerScenePhase.Settle:
                    timeline.NotifySettleFrameRendered();
                    break;
                case HomeShowerScenePhase.Wash:
                case HomeShowerScenePhase.WaterOff:
                case HomeShowerScenePhase.Straighten:
                    washPose.ApplyBrace(
                        timeline.PoseWeight,
                        timeline.ValveReach,
                        timeline.SwayEnvelope,
                        SceneElapsed);
                    break;
            }

            washPose.FollowBridges();
            bool lookAllowed =
                phase >= HomeShowerScenePhase.Wash &&
                phase <= HomeShowerScenePhase.DripHold;
            view.Tick(deltaTime, timeline.CameraBlend, timeline.ViewPitchDegrees, lookAllowed);
        }

        protected override bool TryGetSceneCamera(out Vector3 position, out Quaternion rotation)
        {
            if (view == null || !view.IsActive)
            {
                position = default;
                rotation = default;
                return false;
            }

            view.EvaluateCamera(out position, out rotation);
            return true;
        }

        protected override bool OnRequestStop()
        {
            return timeline.RequestFinish();
        }

        protected override void OnSceneCommit()
        {
            // An interrupted wash that never reached the minimum ends
            // gracefully but relieves nothing.
            if (!timeline.ReachedMinimumWash)
            {
                return;
            }

            GameSessionState.CommitBathroomStressRelief(
                "shower",
                StressRelief);
        }

        /// <summary>
        /// Effects first, the rig last, every step on its own so one
        /// failure can never strand the modal lock behind it.
        /// </summary>
        protected override void OnSceneRestore()
        {
            RestoreStep("water", () =>
            {
                if (waterEffect != null) waterEffect.StopAndClear();
            });
            RestoreStep("sound", () => Home?.Soundscape?.SetShowerWaterAmount(0f));
            RestoreStep("handle", () =>
            {
                if (hotHandle != null) hotHandle.localRotation = hotHandleRest;
            });
            timeline.Reset();
            drips.Reset();
            RestoreStep("clothes", () => Redress(false));
            RestoreStep("pose", () =>
            {
                if (washPose != null)
                {
                    washPose.End();
                    washPose.SetBridgesShown(false);
                }
            });
            RestoreStep("view", () =>
            {
                if (view != null) view.End();
            });
            RestoreStep("handoff", ReleaseHandoff);
            RestoreStep("occlusion", () =>
            {
                if (occlusionCaptured)
                {
                    if (Home != null && Home.PlayerOcclusion != null)
                    {
                        Home.PlayerOcclusion.enabled = previousOcclusionEnabled;
                    }

                    occlusionCaptured = false;
                }
            });
            braceCaptured = false;
            holdBegun = false;
            redressed = false;
            stepOutCornerPassed = false;
        }

        private void ReleaseHandoff()
        {
            if (!ownsHandoff)
            {
                return;
            }

            ownsHandoff = false;
            if (Home != null && Home.Player.Visual != null)
            {
                Home.Player.Visual.SetInteractionHandoffLocked(previousHandoff);
            }
        }

        private void RestoreStep(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                GameLog.Warning(
                    "home",
                    "shower_restore_step_failed",
                    GameLog.Field("step", step),
                    GameLog.Field("error", exception.GetType().Name));
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (washPose != null)
            {
                washPose.Release();
            }
        }
    }
}
