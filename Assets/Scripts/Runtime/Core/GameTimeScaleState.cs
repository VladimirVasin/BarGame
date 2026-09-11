using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Combines a baseline tempo, debug speed, intoxication and pause owners.
    /// A released or obsolete lease can never unpause another owner.
    /// </summary>
    public sealed class GameTimeScaleState
    {
        private readonly HashSet<long> pauseLeases = new HashSet<long>();
        private long nextLease;

        public GameTimeScaleState(float baseTimeScale, float baseFixedDeltaTime)
        {
            SetBaseTimeScale(baseTimeScale);
            if (float.IsNaN(baseFixedDeltaTime) ||
                float.IsInfinity(baseFixedDeltaTime) || baseFixedDeltaTime <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(baseFixedDeltaTime));
            }

            BaseFixedDeltaTime = baseFixedDeltaTime;
        }

        public float BaseTimeScale { get; private set; }
        public float BaseFixedDeltaTime { get; }
        public float PerceptionIntensity { get; private set; }
        public float IntoxicationTimeScale { get; private set; } = 1f;
        public int DebugTimeMultiplier { get; private set; } = 1;
        public bool DebugSpeedSelectionEnabled { get; private set; } = true;
        public bool IsPaused => pauseLeases.Count > 0 || BaseTimeScale <= 0f;
        public float EffectiveTimeScale =>
            IsPaused ? 0f : BaseTimeScale * IntoxicationTimeScale * DebugTimeMultiplier;
        // Debug fast-forward runs more physics steps, never larger collision steps.
        public float FixedDeltaTime => BaseFixedDeltaTime *
            (BaseTimeScale > 0f ? BaseTimeScale : 1f) * IntoxicationTimeScale;

        public void SetDebugSpeedSelectionEnabled(bool enabled)
        {
            DebugSpeedSelectionEnabled = enabled;
            if (!enabled) DebugTimeMultiplier = 1;
        }

        public bool ToggleDebugTimeMultiplier(int multiplier)
        {
            if (!DebugSpeedSelectionEnabled ||
                (multiplier != 3 && multiplier != 5 && multiplier != 10))
            {
                return false;
            }

            DebugTimeMultiplier = DebugTimeMultiplier == multiplier ? 1 : multiplier;
            return true;
        }

        public void SetBaseTimeScale(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            BaseTimeScale = value;
        }

        public void SetIntoxicationLevel(float level)
        {
            IntoxicationPerceptionProfile profile =
                IntoxicationPerceptionRules.Evaluate(level);
            PerceptionIntensity = profile.Intensity;
            IntoxicationTimeScale = profile.WorldTimeScale;
        }

        public long AcquirePause()
        {
            long lease = ++nextLease;
            pauseLeases.Add(lease);
            return lease;
        }

        public bool ReleasePause(long lease)
        {
            return pauseLeases.Remove(lease);
        }

        public float RealGameplayDelta(float unscaledDeltaTime)
        {
            return IsPaused || float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) || unscaledDeltaTime <= 0f
                ? 0f
                : unscaledDeltaTime;
        }

        public void ResetSession()
        {
            pauseLeases.Clear();
            BaseTimeScale = 1f;
            DebugTimeMultiplier = 1;
            DebugSpeedSelectionEnabled = true;
            SetIntoxicationLevel(0f);
        }

        public float CalendarDelta(float unscaledDeltaTime) =>
            RealGameplayDelta(unscaledDeltaTime) * DebugTimeMultiplier;
    }
}
