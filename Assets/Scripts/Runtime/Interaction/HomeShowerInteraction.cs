using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public enum HomeShowerScenePhase
    {
        Idle = 0,
        CurtainClosing = 1,
        WaterStart = 2,
        Hold = 3,
        WaterStop = 4,
        CurtainOpening = 5,
        Completed = 6
    }

    /// <summary>
    /// The shower vignette: curtain draws shut over the hero, water
    /// and steam run under the flickering tube, curtain opens again.
    /// Pure and EditMode-testable.
    /// </summary>
    public sealed class HomeShowerSceneTimeline
    {
        public const float CurtainCloseSeconds = 1.6f;
        public const float WaterStartSeconds = 1.2f;
        public const float MinimumHoldSeconds = 6.0f;
        public const float AutomaticHoldSeconds = 10.0f;
        public const float WaterStopSeconds = 1.0f;
        public const float CurtainOpenSeconds = 1.4f;
        public const float CameraArrivalSeconds = 2.5f;
        public const float GatheredCurtainScale = 0.55f;

        private float phaseElapsed;
        private float cameraElapsed;
        private float openStartScale = 1f;
        private float openStartBlend = 1f;
        private float stopStartWater = 1f;

        public HomeShowerScenePhase Phase { get; private set; } =
            HomeShowerScenePhase.Idle;
        public float PhaseElapsed => phaseElapsed;
        public bool ReachedMinimumHold { get; private set; }

        /// <summary>Curtain group scale.x between gathered and drawn.</summary>
        public float CurtainScale
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.CurtainClosing:
                        return Mathf.Lerp(
                            GatheredCurtainScale,
                            1f,
                            SmoothStep01(
                                phaseElapsed / CurtainCloseSeconds));
                    case HomeShowerScenePhase.WaterStart:
                    case HomeShowerScenePhase.Hold:
                    case HomeShowerScenePhase.WaterStop:
                        return 1f;
                    case HomeShowerScenePhase.CurtainOpening:
                        // Starts from wherever the curtain actually
                        // was, so an abort mid-close never snaps it
                        // fully drawn first.
                        return Mathf.Lerp(
                            openStartScale,
                            GatheredCurtainScale,
                            SmoothStep01(
                                phaseElapsed / CurtainOpenSeconds));
                    default:
                        return GatheredCurtainScale;
                }
            }
        }

        public float WaterAmount
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterStart:
                        return Mathf.Clamp01(
                            phaseElapsed / WaterStartSeconds);
                    case HomeShowerScenePhase.Hold:
                        return 1f;
                    case HomeShowerScenePhase.WaterStop:
                        return stopStartWater * (1f - Mathf.Clamp01(
                            phaseElapsed / WaterStopSeconds));
                    default:
                        return 0f;
                }
            }
        }

        public float CameraBlend
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterStart:
                    case HomeShowerScenePhase.Hold:
                    case HomeShowerScenePhase.WaterStop:
                        return SmoothStep01(
                            cameraElapsed / CameraArrivalSeconds);
                    case HomeShowerScenePhase.CurtainOpening:
                        return openStartBlend * (1f - SmoothStep01(
                            phaseElapsed / CurtainOpenSeconds));
                    default:
                        return 0f;
                }
            }
        }

        public bool IsCompleted =>
            Phase == HomeShowerScenePhase.Completed;

        public void Begin()
        {
            Phase = HomeShowerScenePhase.CurtainClosing;
            phaseElapsed = 0f;
            cameraElapsed = 0f;
            openStartScale = 1f;
            openStartBlend = 1f;
            stopStartWater = 1f;
            ReachedMinimumHold = false;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime));
            }

            if (Phase == HomeShowerScenePhase.Idle ||
                Phase == HomeShowerScenePhase.Completed)
            {
                return;
            }

            float safeDelta = Mathf.Max(0f, deltaTime);
            phaseElapsed += safeDelta;
            if (Phase == HomeShowerScenePhase.WaterStart ||
                Phase == HomeShowerScenePhase.Hold ||
                Phase == HomeShowerScenePhase.WaterStop)
            {
                cameraElapsed += safeDelta;
            }

            switch (Phase)
            {
                case HomeShowerScenePhase.CurtainClosing:
                    if (phaseElapsed >= CurtainCloseSeconds)
                    {
                        SetPhase(HomeShowerScenePhase.WaterStart);
                    }

                    break;
                case HomeShowerScenePhase.WaterStart:
                    if (phaseElapsed >= WaterStartSeconds)
                    {
                        SetPhase(HomeShowerScenePhase.Hold);
                    }

                    break;
                case HomeShowerScenePhase.Hold:
                    if (phaseElapsed >= MinimumHoldSeconds)
                    {
                        ReachedMinimumHold = true;
                    }

                    if (phaseElapsed >= AutomaticHoldSeconds)
                    {
                        SetPhase(HomeShowerScenePhase.WaterStop);
                    }

                    break;
                case HomeShowerScenePhase.WaterStop:
                    if (phaseElapsed >= WaterStopSeconds)
                    {
                        BeginCurtainOpening();
                    }

                    break;
                case HomeShowerScenePhase.CurtainOpening:
                    if (phaseElapsed >= CurtainOpenSeconds)
                    {
                        SetPhase(HomeShowerScenePhase.Completed);
                    }

                    break;
            }
        }

        /// <summary>
        /// Interrupts the wash from any pre-wind-down phase: the
        /// curtain reverses from wherever it is, water fades from its
        /// current amount and the camera walks home from its actual
        /// blend. The minimum hold gates only the stress reward.
        /// </summary>
        public bool RequestFinish()
        {
            switch (Phase)
            {
                case HomeShowerScenePhase.CurtainClosing:
                    BeginCurtainOpening();
                    return true;
                case HomeShowerScenePhase.WaterStart:
                case HomeShowerScenePhase.Hold:
                    stopStartWater = WaterAmount;
                    SetPhase(HomeShowerScenePhase.WaterStop);
                    return true;
                default:
                    return false;
            }
        }

        public void Reset()
        {
            Phase = HomeShowerScenePhase.Idle;
            phaseElapsed = 0f;
            cameraElapsed = 0f;
            openStartScale = 1f;
            openStartBlend = 1f;
            stopStartWater = 1f;
            ReachedMinimumHold = false;
        }

        private void BeginCurtainOpening()
        {
            openStartScale = CurtainScale;
            openStartBlend = CameraBlend;
            SetPhase(HomeShowerScenePhase.CurtainOpening);
        }

        private void SetPhase(HomeShowerScenePhase phase)
        {
            Phase = phase;
            phaseElapsed = 0f;
        }

        private static float SmoothStep01(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    /// <summary>
    /// Code-built water: a narrow stream cone from the shower head
    /// and slow steam over the stall, both on the shared atmosphere
    /// material, no lights and no colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerWaterEffect : MonoBehaviour
    {
        private ParticleSystem stream;
        private ParticleSystem steam;
        private bool isInitialized;

        public bool IsEmitting { get; private set; }

        public void Initialize(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            stream = CreateSystem(
                parent,
                "Shower Water Stream",
                new Vector3(3.72f, 1.98f, 3.20f),
                Quaternion.Euler(145f, 0f, 0f),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(0.55f, 0.75f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(2.3f, 2.9f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
                    main.maxParticles = 60;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.58f, 0.63f, 0.64f, 0.35f),
                        new Color(0.52f, 0.58f, 0.60f, 0.22f));
                    ParticleSystem.EmissionModule emission =
                        system.emission;
                    emission.rateOverTime = 48f;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 9f;
                    shape.radius = 0.05f;
                    ParticleSystemRenderer renderer = system
                        .GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode =
                        ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 4.5f;
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
                    ParticleSystem.EmissionModule emission =
                        system.emission;
                    emission.rateOverTime = 7f;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(0.9f, 0.3f, 0.9f);
                });
            isInitialized = true;
            SetEmitting(false);
        }

        public void SetEmitting(bool emitting)
        {
            if (!isInitialized)
            {
                return;
            }

            IsEmitting = emitting;
            Toggle(stream, emitting);
            Toggle(steam, emitting);
        }

        public void StopAndClear()
        {
            if (!isInitialized)
            {
                return;
            }

            IsEmitting = false;
            Clear(stream);
            Clear(steam);
        }

        private static void Toggle(
            ParticleSystem system,
            bool emitting)
        {
            if (system == null)
            {
                return;
            }

            if (emitting && !system.isEmitting)
            {
                system.Play();
            }
            else if (!emitting && system.isEmitting)
            {
                system.Stop(
                    false,
                    ParticleSystemStopBehavior.StopEmitting);
            }
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
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;
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
    /// The shower scene: walk into the tray, the curtain draws shut
    /// over the hero, water and steam run for a while, and a finished
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

        private static readonly Vector3 CameraPosition =
            new Vector3(2.30f, 2.05f, 1.45f);
        private static readonly Vector3 CameraLookAt =
            new Vector3(3.85f, 1.30f, 2.95f);

        private readonly HomeShowerSceneTimeline timeline =
            new HomeShowerSceneTimeline();

        private Transform curtainGroup;
        private HomeShowerWaterEffect waterEffect;

        public HomeShowerSceneTimeline Timeline => timeline;
        public HomeShowerWaterEffect WaterEffect => waterEffect;
        public override string PromptKey =>
            OwnsScene ? string.Empty : TakePromptKey;

        protected override string StopPromptKey => StopPromptKeyName;
        protected override Vector3 CameraLocalPosition =>
            CameraPosition;
        protected override Vector3 CameraLocalLookAt => CameraLookAt;
        protected override float CameraFieldOfView => 54f;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => 1f;
        protected override bool SceneCompleted =>
            timeline.IsCompleted;
        protected override bool StopPromptVisible =>
            timeline.Phase == HomeShowerScenePhase.Hold;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            InitializeScene(
                homeRoot,
                new Vector3(3.925f, 0f, 2.925f),
                Quaternion.LookRotation(
                    Vector3.forward,
                    Vector3.up),
                new Vector3(3.30f, 0f, 2.60f),
                Quaternion.LookRotation(Vector3.left, Vector3.up),
                new Vector3(3.55f, 0f, 2.60f));
            curtainGroup = homeRoot.Room != null
                ? homeRoot.Room.Find("Home Bathroom Shower Curtain")
                : null;
            waterEffect = gameObject
                .AddComponent<HomeShowerWaterEffect>();
            waterEffect.Initialize(
                homeRoot.Room != null
                    ? homeRoot.Room
                    : homeRoot.transform);
        }

        protected override void OnSceneBegin()
        {
            timeline.Begin();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            timeline.Advance(deltaTime);
            ApplyCurtain(timeline.CurtainScale);
            float water = timeline.WaterAmount;
            Home.Soundscape?.SetShowerWaterAmount(water);
            waterEffect?.SetEmitting(water > 0.05f);
        }

        protected override bool OnRequestStop()
        {
            return timeline.RequestFinish();
        }

        protected override void OnSceneCommit()
        {
            // An interrupted wash that never reached the minimum
            // hold ends gracefully but relieves nothing.
            if (!timeline.ReachedMinimumHold)
            {
                return;
            }

            GameSessionState.CommitBathroomStressRelief(
                "shower",
                StressRelief);
        }

        protected override void OnSceneRestore()
        {
            timeline.Reset();
            ApplyCurtain(
                HomeShowerSceneTimeline.GatheredCurtainScale);
            Home?.Soundscape?.SetShowerWaterAmount(0f);
            waterEffect?.StopAndClear();
        }

        private void ApplyCurtain(float scale)
        {
            if (curtainGroup == null)
            {
                return;
            }

            Vector3 localScale = curtainGroup.localScale;
            localScale.x = scale;
            curtainGroup.localScale = localScale;
        }
    }
}
