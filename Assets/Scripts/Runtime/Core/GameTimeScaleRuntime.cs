using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One persistent writer of Unity's tempo and physics timestep.</summary>
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    public sealed class GameTimeScaleRuntime : MonoBehaviour
    {
        // A small drink must take time to reach the ear too. The visual ramp's
        // fixed points/second reaches +8/+20 almost immediately.
        private const float AudioResponseSeconds = 0.7f;

        private static GameTimeScaleRuntime instance;
        private GameTimeScaleState state;
        private float lastAppliedScale;
        private float presentationLevel;
        private float audioLevel;
        private IntoxicationPerceptionProfile audioPerception;

        public static bool IsPaused =>
            Time.timeScale <= 0f || (instance != null && instance.state.IsPaused);
        public static float PerceptionIntensity =>
            instance != null ? instance.state.PerceptionIntensity : 0f;
        public static IntoxicationPerceptionProfile AudioPerception => instance != null
            ? instance.audioPerception
            : IntoxicationPerceptionRules.Evaluate(GameSessionState.IntoxicationLevel);
        public static float PerceptionPlaybackRate => AudioPerception.PlaybackRate;
        public static float SmoothedAudioLevel => instance != null
            ? instance.audioLevel : GameSessionState.IntoxicationLevel;
        public static float SmoothedPresentationLevel =>
            instance != null ? instance.presentationLevel :
                GameSessionState.IntoxicationLevel;
        public static float CalendarDeltaTime =>
            IsPaused ? 0f : instance != null
                ? instance.state.CalendarDelta(Time.unscaledDeltaTime)
                : Time.unscaledDeltaTime;
        public static int DebugTimeMultiplier =>
            instance != null ? instance.state.DebugTimeMultiplier : 1;
        public static bool DebugSpeedSelectionEnabled =>
            instance == null || instance.state.DebugSpeedSelectionEnabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        public static GameTimeScaleRuntime EnsureInstalled()
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<GameTimeScaleRuntime>();
            }

            if (instance == null)
            {
                new GameObject("[Bar Promenade] World Tempo")
                    .AddComponent<GameTimeScaleRuntime>();
            }

            return instance;
        }

        public static void SetIntoxicationLevel(float level, bool synchronizeAudio = true)
        {
            GameTimeScaleRuntime runtime = EnsureInstalled();
            runtime.AdoptExternalBaseline();
            runtime.presentationLevel = Mathf.Clamp(level, 0f, 100f);
            if (synchronizeAudio) runtime.SetAudioLevel(runtime.presentationLevel);
            runtime.state.SetIntoxicationLevel(level);
            runtime.Apply();
        }

        public static IDisposable AcquirePause()
        {
            GameTimeScaleRuntime runtime = EnsureInstalled();
            runtime.AdoptExternalBaseline();
            long lease = runtime.state.AcquirePause();
            runtime.Apply();
            return new PauseLease(runtime, lease);
        }

        public static void SetDebugSpeedSelectionEnabled(bool enabled)
        {
            GameTimeScaleRuntime runtime = EnsureInstalled();
            runtime.AdoptExternalBaseline();
            runtime.state.SetDebugSpeedSelectionEnabled(enabled);
            runtime.Apply();
        }

        public static bool ToggleDebugTimeMultiplier(int multiplier)
        {
            GameTimeScaleRuntime runtime = EnsureInstalled();
            runtime.AdoptExternalBaseline();
            if (!runtime.state.ToggleDebugTimeMultiplier(multiplier)) return false;
            runtime.Apply();
            return true;
        }

        public static void ResetSession()
        {
            // Pure session tests need no runtime GameObject. An installed
            // owner resets immediately, including any old scene's leases.
            if (instance != null)
            {
                instance.presentationLevel = 0f;
                instance.SetAudioLevel(0f);
                instance.state.ResetSession();
                instance.Apply();
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            state = new GameTimeScaleState(Time.timeScale, Time.fixedDeltaTime);
            presentationLevel = GameSessionState.IntoxicationLevel;
            SetAudioLevel(presentationLevel);
            state.SetIntoxicationLevel(presentationLevel);
            DontDestroyOnLoad(gameObject);
            Apply();
            gameObject.AddComponent<DebugTimeControls>();
        }

        private void Update()
        {
            AdoptExternalBaseline();
            float deltaTime = state.RealGameplayDelta(Time.unscaledDeltaTime);
            presentationLevel = Mathf.MoveTowards(
                presentationLevel,
                GameSessionState.IntoxicationLevel,
                deltaTime * 100f / 0.7f);
            if (deltaTime > 0f && audioLevel != GameSessionState.IntoxicationLevel)
            {
                float target = GameSessionState.IntoxicationLevel;
                float next = Mathf.Lerp(audioLevel, target,
                    1f - Mathf.Exp(-deltaTime / AudioResponseSeconds));
                SetAudioLevel(Mathf.Abs(next - target) <= 0.001f ? target : next);
            }
            state.SetIntoxicationLevel(presentationLevel);
            Apply();
        }

        private void SetAudioLevel(float level)
        {
            audioLevel = level;
            audioPerception = IntoxicationPerceptionRules.Evaluate(level);
        }

        private void AdoptExternalBaseline()
        {
            // Retain explicit debug/test baseline overrides. Pause owners
            // have priority until their last lease has been released.
            if (!state.IsPaused && Time.timeScale != lastAppliedScale)
            {
                state.SetBaseTimeScale(
                    Time.timeScale / (state.IntoxicationTimeScale * state.DebugTimeMultiplier));
            }
            else if (state.BaseTimeScale == 0f && Time.timeScale > 0f)
            {
                state.SetBaseTimeScale(
                    Time.timeScale / (state.IntoxicationTimeScale * state.DebugTimeMultiplier));
            }
        }

        private void Apply()
        {
            lastAppliedScale = state.EffectiveTimeScale;
            Time.timeScale = lastAppliedScale;
            Time.fixedDeltaTime = state.FixedDeltaTime;
        }

        private void ReleasePause(long lease)
        {
            if (state.ReleasePause(lease))
            {
                Apply();
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                state.ResetSession();
                Apply();
                instance = null;
            }
        }

        private sealed class PauseLease : IDisposable
        {
            private GameTimeScaleRuntime owner;
            private readonly long lease;

            public PauseLease(GameTimeScaleRuntime owner, long lease)
            {
                this.owner = owner;
                this.lease = lease;
            }

            public void Dispose()
            {
                if (owner != null)
                {
                    owner.ReleasePause(lease);
                    owner = null;
                }
            }
        }
    }
}
