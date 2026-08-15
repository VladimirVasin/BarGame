using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeToiletScenePhase
    {
        Idle = 0,
        CameraAway = 1,
        Privacy = 2,
        CameraReturn = 3,
        Completed = 4
    }

    /// <summary>
    /// The tactful privacy cut: the camera walks away to the ajar
    /// door, the cistern hisses, the flush lands off-frame and the
    /// camera comes back. Pure and EditMode-testable.
    /// </summary>
    public sealed class HomeToiletSceneTimeline
    {
        public const float CameraAwaySeconds = 2.2f;
        public const float CameraHoldSeconds = 0.35f;
        public const float PrivacySeconds = 5.0f;
        public const float MinimumPrivacySeconds = 2.5f;
        public const float CameraReturnSeconds = 1.8f;
        public const float FlushTimeSeconds = 3.6f;
        public const float WaterFadeInSeconds = 0.8f;
        public const float WaterFadeOutSeconds = 1.0f;
        public const float PrivacyWaterAmount = 0.35f;

        private float phaseElapsed;
        private bool flushConsumed;

        public HomeToiletScenePhase Phase { get; private set; } =
            HomeToiletScenePhase.Idle;
        public float PhaseElapsed => phaseElapsed;

        public float CameraBlend
        {
            get
            {
                switch (Phase)
                {
                    case HomeToiletScenePhase.CameraAway:
                        return SmoothStep01(
                            Mathf.InverseLerp(
                                CameraHoldSeconds,
                                CameraAwaySeconds,
                                phaseElapsed));
                    case HomeToiletScenePhase.Privacy:
                        return 1f;
                    case HomeToiletScenePhase.CameraReturn:
                        return 1f - SmoothStep01(
                            phaseElapsed / CameraReturnSeconds);
                    default:
                        return 0f;
                }
            }
        }

        public float WaterAmount
        {
            get
            {
                if (Phase != HomeToiletScenePhase.Privacy)
                {
                    return 0f;
                }

                float fadeIn = Mathf.Clamp01(
                    phaseElapsed / WaterFadeInSeconds);
                float fadeOut = Mathf.Clamp01(
                    (PrivacySeconds - phaseElapsed) /
                    WaterFadeOutSeconds);
                return PrivacyWaterAmount *
                    Mathf.Min(fadeIn, fadeOut);
            }
        }

        public bool IsCompleted =>
            Phase == HomeToiletScenePhase.Completed;

        public void Begin()
        {
            Phase = HomeToiletScenePhase.CameraAway;
            phaseElapsed = 0f;
            flushConsumed = false;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime));
            }

            if (Phase == HomeToiletScenePhase.Idle ||
                Phase == HomeToiletScenePhase.Completed)
            {
                return;
            }

            phaseElapsed += Mathf.Max(0f, deltaTime);
            switch (Phase)
            {
                case HomeToiletScenePhase.CameraAway:
                    if (phaseElapsed >= CameraAwaySeconds)
                    {
                        SetPhase(HomeToiletScenePhase.Privacy);
                    }

                    break;
                case HomeToiletScenePhase.Privacy:
                    if (phaseElapsed >= PrivacySeconds)
                    {
                        SetPhase(HomeToiletScenePhase.CameraReturn);
                    }

                    break;
                case HomeToiletScenePhase.CameraReturn:
                    if (phaseElapsed >= CameraReturnSeconds)
                    {
                        SetPhase(HomeToiletScenePhase.Completed);
                    }

                    break;
            }
        }

        /// <summary>
        /// A stop request shortens the privacy hold once the minimum
        /// has passed; the flush still lands before the camera turns.
        /// </summary>
        public bool RequestFinish()
        {
            if (Phase != HomeToiletScenePhase.Privacy ||
                phaseElapsed < MinimumPrivacySeconds)
            {
                return false;
            }

            SetPhase(HomeToiletScenePhase.CameraReturn);
            return true;
        }

        /// <summary>One-shot: true exactly once, at the flush beat.</summary>
        public bool ConsumeFlushCue()
        {
            bool due =
                !flushConsumed &&
                ((Phase == HomeToiletScenePhase.Privacy &&
                  phaseElapsed >= FlushTimeSeconds) ||
                 Phase == HomeToiletScenePhase.CameraReturn ||
                 Phase == HomeToiletScenePhase.Completed);
            if (due)
            {
                flushConsumed = true;
            }

            return due;
        }

        public void Reset()
        {
            Phase = HomeToiletScenePhase.Idle;
            phaseElapsed = 0f;
            flushConsumed = false;
        }

        private void SetPhase(HomeToiletScenePhase phase)
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
    /// The toilet scene: the hero walks to the bowl, the camera
    /// retreats to the ajar-door frame while the cistern works, and a
    /// completed visit relieves a little stress.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class HomeToiletInteraction :
        HomeBathroomSceneInteraction
    {
        public const string UsePromptKey = "interaction.use_toilet";
        public const int StressRelief = 6;
        public const float FlushHandlePressDepth = 0.03f;

        private static readonly Vector3 CameraPosition =
            new Vector3(2.55f, 1.90f, 2.05f);
        private static readonly Vector3 CameraLookAt =
            new Vector3(2.60f, 1.10f, 0.40f);

        private readonly HomeToiletSceneTimeline timeline =
            new HomeToiletSceneTimeline();

        private Transform flushHandle;
        private Vector3 flushHandleRestPosition;
        private float flushHandlePress;

        public HomeToiletSceneTimeline Timeline => timeline;
        public override string PromptKey =>
            OwnsScene ? string.Empty : UsePromptKey;

        protected override string StopPromptKey => string.Empty;
        protected override Vector3 CameraLocalPosition =>
            CameraPosition;
        protected override Vector3 CameraLocalLookAt => CameraLookAt;
        protected override float CameraFieldOfView => 60f;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => 0.6f;
        protected override bool SceneCompleted =>
            timeline.IsCompleted;
        protected override bool StopPromptVisible => false;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            Vector3 dock = new Vector3(3.32f, 0f, 1.40f);
            Quaternion facing = Quaternion.LookRotation(
                Vector3.right,
                Vector3.up);
            InitializeScene(
                homeRoot,
                dock,
                facing,
                new Vector3(3.10f, 0f, 1.40f),
                Quaternion.LookRotation(Vector3.left, Vector3.up),
                dock);
            flushHandle = homeRoot.Room != null
                ? homeRoot.Room.Find("Home Bathroom Toilet Flush")
                : null;
            if (flushHandle != null)
            {
                flushHandleRestPosition =
                    flushHandle.localPosition;
            }
        }

        protected override void OnSceneBegin()
        {
            timeline.Begin();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            timeline.Advance(deltaTime);
            Home.Soundscape?.SetShowerWaterAmount(
                timeline.WaterAmount);
            if (timeline.ConsumeFlushCue())
            {
                Home.Audio?.TryPlay(
                    RetroSfxId.ToiletFlush,
                    Home.transform.TransformPoint(
                        new Vector3(4.49f, 0.9f, 1.40f)));
                flushHandlePress = 1f;
            }

            if (flushHandle != null)
            {
                flushHandlePress = Mathf.MoveTowards(
                    flushHandlePress,
                    0f,
                    deltaTime * 2.5f);
                flushHandle.localPosition =
                    flushHandleRestPosition +
                    Vector3.down *
                    (FlushHandlePressDepth * flushHandlePress);
            }
        }

        protected override void OnRequestStop()
        {
            timeline.RequestFinish();
        }

        protected override void OnSceneCommit()
        {
            GameSessionState.CommitBathroomStressRelief(
                "toilet",
                StressRelief);
        }

        protected override void OnSceneRestore()
        {
            timeline.Reset();
            Home?.Soundscape?.SetShowerWaterAmount(0f);
            if (flushHandle != null)
            {
                flushHandle.localPosition = flushHandleRestPosition;
            }

            flushHandlePress = 0f;
        }
    }
}
