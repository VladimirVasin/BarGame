using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One persistent writer of Unity's tempo and physics timestep.</summary>
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    public sealed class GameTimeScaleRuntime : MonoBehaviour
    {
        private static GameTimeScaleRuntime instance;
        private GameTimeScaleState state;
        private float lastAppliedScale;
        private float presentationLevel;

        public static bool IsPaused =>
            Time.timeScale <= 0f || (instance != null && instance.state.IsPaused);
        public static float PerceptionIntensity =>
            instance != null ? instance.state.PerceptionIntensity : 0f;
        public static float SmoothedPresentationLevel =>
            instance != null ? instance.presentationLevel :
                GameSessionState.IntoxicationLevel;
        public static float CalendarDeltaTime =>
            IsPaused ? 0f : Time.unscaledDeltaTime;

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

        public static void SetIntoxicationLevel(float level)
        {
            GameTimeScaleRuntime runtime = EnsureInstalled();
            runtime.AdoptExternalBaseline();
            runtime.presentationLevel = Mathf.Clamp(level, 0f, 100f);
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

        public static void ResetSession()
        {
            // Pure session tests need no runtime GameObject. An installed
            // owner resets immediately, including any old scene's leases.
            if (instance != null)
            {
                instance.presentationLevel = 0f;
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
            state.SetIntoxicationLevel(presentationLevel);
            DontDestroyOnLoad(gameObject);
            Apply();
        }

        private void Update()
        {
            AdoptExternalBaseline();
            presentationLevel = Mathf.MoveTowards(
                presentationLevel,
                GameSessionState.IntoxicationLevel,
                state.RealGameplayDelta(Time.unscaledDeltaTime) * 100f / 0.7f);
            state.SetIntoxicationLevel(presentationLevel);
            Apply();
        }

        private void AdoptExternalBaseline()
        {
            // Retain explicit debug/test baseline overrides. Pause owners
            // have priority until their last lease has been released.
            if (!state.IsPaused && Time.timeScale != lastAppliedScale)
            {
                state.SetBaseTimeScale(
                    Time.timeScale / state.IntoxicationTimeScale);
            }
            else if (state.BaseTimeScale == 0f && Time.timeScale > 0f)
            {
                state.SetBaseTimeScale(
                    Time.timeScale / state.IntoxicationTimeScale);
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
