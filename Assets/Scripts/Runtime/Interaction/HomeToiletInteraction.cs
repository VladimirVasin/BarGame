using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeToiletScenePhase
    {
        Idle, Entering, Urinating, Shaking, Exiting, Completed
    }

    /// <summary>Exact action time, independent of frame rate and presentation.</summary>
    public sealed class HomeToiletSceneTimeline
    {
        public const float EnterSeconds = 1.5f;
        public const float UrinatingSeconds = 6f;
        public const float ShakingSeconds = 2f;
        public const float ExitSeconds = 1.3f;
        public const float FlowFadeFraction = 0.2f;

        private float phaseElapsed;
        private float exitStartBlend = 1f;
        private bool flushConsumed;
        public HomeToiletScenePhase Phase { get; private set; }
        public float PhaseElapsed => phaseElapsed;
        public float TotalUrinatingSeconds { get; private set; }
        public float TotalShakingSeconds { get; private set; }
        public bool WasCancelled { get; private set; }
        public bool IsCompleted => Phase == HomeToiletScenePhase.Completed;
        public bool CanCommit => IsCompleted && !WasCancelled &&
            TotalUrinatingSeconds >= UrinatingSeconds && TotalShakingSeconds >= ShakingSeconds;
        public float RemainingAmount => 1f - Mathf.Clamp01(TotalUrinatingSeconds / UrinatingSeconds);
        public float UrineFlow => EvaluateUrineFlow(TotalUrinatingSeconds);
        public bool GaugeVisible => Phase == HomeToiletScenePhase.Urinating || Phase == HomeToiletScenePhase.Shaking;
        public float CameraBlend => Phase == HomeToiletScenePhase.Entering
            ? Smooth(phaseElapsed / EnterSeconds)
            : Phase == HomeToiletScenePhase.Urinating || Phase == HomeToiletScenePhase.Shaking
                ? 1f : Phase == HomeToiletScenePhase.Exiting
                    ? exitStartBlend * (1f - Smooth(phaseElapsed / ExitSeconds)) : 0f;

        public void Begin()
        {
            Reset();
            Phase = HomeToiletScenePhase.Entering;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            float remaining = Mathf.Max(0f, deltaTime);
            // Carry overshoot through every phase: hitches never lengthen the action.
            while (remaining > 0f && Phase != HomeToiletScenePhase.Idle && !IsCompleted)
            {
                float duration = Duration(Phase);
                float step = Mathf.Min(remaining, Mathf.Max(0f, duration - phaseElapsed));
                phaseElapsed += step;
                remaining -= step;
                if (Phase == HomeToiletScenePhase.Urinating)
                    TotalUrinatingSeconds = Mathf.Min(UrinatingSeconds, TotalUrinatingSeconds + step);
                if (Phase == HomeToiletScenePhase.Shaking)
                    TotalShakingSeconds = Mathf.Min(ShakingSeconds, TotalShakingSeconds + step);
                if (phaseElapsed >= duration)
                {
                    if (Phase == HomeToiletScenePhase.Urinating) TotalUrinatingSeconds = UrinatingSeconds;
                    if (Phase == HomeToiletScenePhase.Shaking) TotalShakingSeconds = ShakingSeconds;
                    Phase++;
                    phaseElapsed = 0f;
                }
            }
        }

        public bool RequestFinish()
        {
            if (Phase == HomeToiletScenePhase.Idle || Phase >= HomeToiletScenePhase.Exiting) return false;
            exitStartBlend = CameraBlend;
            WasCancelled = true;
            flushConsumed = true;
            Phase = HomeToiletScenePhase.Exiting;
            phaseElapsed = 0f;
            return true;
        }

        public bool ConsumeFlushCue()
        {
            if (flushConsumed || WasCancelled || Phase < HomeToiletScenePhase.Exiting) return false;
            flushConsumed = true;
            return true;
        }

        public void Reset()
        {
            Phase = HomeToiletScenePhase.Idle;
            phaseElapsed = TotalUrinatingSeconds = TotalShakingSeconds = 0f;
            exitStartBlend = 1f;
            WasCancelled = flushConsumed = false;
        }
        private static float Duration(HomeToiletScenePhase phase) =>
            phase == HomeToiletScenePhase.Entering ? EnterSeconds :
            phase == HomeToiletScenePhase.Urinating ? UrinatingSeconds :
            phase == HomeToiletScenePhase.Shaking ? ShakingSeconds : ExitSeconds;
        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

        public static float EvaluateUrineFlow(float urinatingElapsed)
        {
            float fadeSeconds = UrinatingSeconds * FlowFadeFraction;
            return 1f - Smooth((urinatingElapsed - (UrinatingSeconds - fadeSeconds)) / fadeSeconds);
        }

        public static float AverageUrineFlow(float fromSeconds, float toSeconds)
        {
            if (toSeconds <= fromSeconds) return EvaluateUrineFlow(toSeconds);
            return Mathf.Clamp01((IntegratedUrineFlow(toSeconds) - IntegratedUrineFlow(fromSeconds)) /
                (toSeconds - fromSeconds));
        }

        private static float IntegratedUrineFlow(float seconds)
        {
            float fadeSeconds = UrinatingSeconds * FlowFadeFraction;
            float fadeStart = UrinatingSeconds - fadeSeconds;
            float time = Mathf.Clamp(seconds, 0f, UrinatingSeconds);
            float u = Mathf.Clamp01((time - fadeStart) / fadeSeconds);
            // Exact integral of 1 - smoothstep: even a frame crossing the
            // phase boundary retains its final, fading portion of emission.
            return Mathf.Min(time, fadeStart) + fadeSeconds * (u - u * u * u + 0.5f * u * u * u * u);
        }
    }

    /// <summary>First-person toilet action on the shared bathroom lifecycle.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class HomeToiletInteraction : HomeBathroomSceneInteraction
    {
        public const string UsePromptKey = "interaction.use_toilet";
        public const string StopPromptKeyName = "interaction.stop_toilet";
        public const int StressRelief = 6;
        public const float FlushHandlePressDepth = 0.03f;
        private readonly HomeToiletSceneTimeline timeline = new HomeToiletSceneTimeline();
        private HomeToiletFirstPersonView firstPerson;
        private HomeUrineEffect urine;
        private HomeToiletLid lid;
        private Transform flushHandle;
        private Vector3 flushHandleRest;
        private float flushPress;
        private float pendingUrineSeconds;
        private float pendingShakeSeconds;
        private bool previousHandoff;
        private bool ownsHandoff;

        public HomeToiletSceneTimeline Timeline => timeline;
        public HomeToiletFirstPersonView FirstPerson => firstPerson;
        public HomeUrineEffect Urine => urine;
        public HomeToiletLid Lid => lid;
        public bool GaugeVisible => OwnsScene && timeline.GaugeVisible;
        public override string PromptKey => OwnsScene ? string.Empty : UsePromptKey;
        protected override string StopPromptKey => StopPromptKeyName;
        protected override Vector3 CameraLocalPosition => new Vector3(3.40f, 1.66f, 1.40f);
        protected override Vector3 CameraLocalLookAt => new Vector3(4.05f, 0.60f, 1.40f);
        protected override float CameraFieldOfView => HomeToiletFirstPersonView.FieldOfView;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => 0f;
        protected override bool SceneCompleted => timeline.IsCompleted;
        protected override bool StopPromptVisible => timeline.GaugeVisible;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null) throw new ArgumentNullException(nameof(homeRoot));
            Vector3 dock = new Vector3(3.32f, 0f, 1.40f);
            InitializeScene(homeRoot, dock, Quaternion.LookRotation(Vector3.right),
                new Vector3(3.10f, 0f, 1.40f), Quaternion.LookRotation(Vector3.left), dock);
            lid = homeRoot.Room.GetComponentInChildren<HomeToiletLid>(true);
            firstPerson = gameObject.AddComponent<HomeToiletFirstPersonView>();
            firstPerson.Initialize(homeRoot);
            // Flying liquid and residue outlive this modal action.
            var effectObject = new GameObject("Home Urine");
            effectObject.transform.SetParent(homeRoot.transform, false);
            urine = effectObject.AddComponent<HomeUrineEffect>();
            urine.Initialize(homeRoot.transform);
            gameObject.AddComponent<HomeToiletGaugeView>().Bind(this);
            flushHandle = homeRoot.Room.Find("Home Bathroom Toilet Flush");
            if (flushHandle != null) flushHandleRest = flushHandle.localPosition;
        }

        protected override bool PrepareScene() => lid != null && firstPerson.Prepare();
        protected override void OnSceneCaptured() => lid.Open();
        protected override void OnSceneBegin()
        {
            timeline.Begin();
            pendingUrineSeconds = pendingShakeSeconds = 0f;
            previousHandoff = Home.Player.Visual.InteractionHandoffLocked;
            Home.Player.Visual.SetInteractionHandoffLocked(true);
            ownsHandoff = true;
            firstPerson.Begin();
            urine.BeginEmission();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            float previousUrine = timeline.TotalUrinatingSeconds;
            float previousShake = timeline.TotalShakingSeconds;
            timeline.Advance(deltaTime);
            pendingUrineSeconds += timeline.TotalUrinatingSeconds - previousUrine;
            pendingShakeSeconds += timeline.TotalShakingSeconds - previousShake;
            if (timeline.ConsumeFlushCue())
            {
                Home.Audio?.TryPlay(RetroSfxId.ToiletFlush,
                    Home.transform.TransformPoint(new Vector3(4.49f, 0.9f, 1.40f)));
                flushPress = 1f;
            }
            flushPress = Mathf.MoveTowards(flushPress, 0f, deltaTime * 1.25f);
            if (flushHandle != null)
                flushHandle.localPosition = flushHandleRest + Vector3.down * (FlushHandlePressDepth * flushPress);
        }

        protected override void OnScenePresentation(float deltaTime)
        {
            firstPerson.Tick(deltaTime, timeline.CameraBlend,
                timeline.Phase == HomeToiletScenePhase.Shaking ? timeline.PhaseElapsed : -1f,
                timeline.Phase == HomeToiletScenePhase.Urinating);
            if (pendingUrineSeconds > 0f)
            {
                float flow = HomeToiletSceneTimeline.AverageUrineFlow(
                    timeline.TotalUrinatingSeconds - pendingUrineSeconds, timeline.TotalUrinatingSeconds);
                urine.EmitStep(firstPerson.OutletPosition, firstPerson.OutletDirection, pendingUrineSeconds, flow, false);
            }
            if (pendingShakeSeconds > 0f)
                urine.EmitStep(firstPerson.OutletPosition, firstPerson.OutletDirection, pendingShakeSeconds, 0.7f, true);
            pendingUrineSeconds = pendingShakeSeconds = 0f;
            if (timeline.Phase >= HomeToiletScenePhase.Exiting) urine.StopEmission();
            if (timeline.IsCompleted)
            {
                // This is the terminal rendered endpoint. Release the pose
                // now so the subsequent guided walk-out has its normal gait.
                firstPerson.End();
                ReleaseHandoff();
            }
        }

        protected override bool TryGetSceneCamera(out Vector3 position, out Quaternion rotation)
        {
            firstPerson.EvaluateCamera(out position, out rotation);
            return firstPerson.IsActive;
        }
        protected override bool OnRequestStop() => timeline.RequestFinish();
        protected override void OnSceneCommit()
        {
            if (timeline.CanCommit) GameSessionState.CommitBathroomStressRelief("toilet", StressRelief);
        }
        protected override void OnSceneRestore()
        {
            urine?.StopEmission();
            firstPerson?.End();
            ReleaseHandoff();
            lid?.Close();
            timeline.Reset();
            pendingUrineSeconds = pendingShakeSeconds = flushPress = 0f;
            if (flushHandle != null) flushHandle.localPosition = flushHandleRest;
        }

        private void ReleaseHandoff()
        {
            if (ownsHandoff)
            {
                Home.Player.Visual.SetInteractionHandoffLocked(previousHandoff);
                ownsHandoff = false;
            }
        }
    }
}
