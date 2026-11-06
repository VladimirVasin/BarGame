using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One sparse contact dip, with independently seeded interval and phase. No catch-up events.</summary>
    public static class CityCoveredPassageFlickerEvaluator
    {
        public const float MinimumPower = .12f;
        public const float FaultDuration = .72f;
        public const float MinimumInterval = 19f, MaximumInterval = 37f;

        public static double AbsoluteSeconds =>
            (GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes) /
            GameTimeState.GameMinutesPerRealSecond;

        public static double IntervalSeconds(int seed) => MinimumInterval +
            (CityCoveredPassagePlan.Mix(unchecked((uint)seed)) & 0xffffu) / 65535d * (MaximumInterval - MinimumInterval);

        public static float Evaluate(double absoluteSeconds, int seed)
        {
            if (double.IsNaN(absoluteSeconds) || double.IsInfinity(absoluteSeconds) || absoluteSeconds < 0d) return 1f;
            double interval = IntervalSeconds(seed);
            uint phaseSeed = CityCoveredPassagePlan.Mix(unchecked((uint)seed) ^ 0xb5297a4du);
            double phase = (phaseSeed & 0xffffu) / 65535d * interval;
            double local = (absoluteSeconds + phase) % interval;
            if (local >= FaultDuration) return 1f;
            if (local < .14d) return Mathf.Lerp(1f, MinimumPower, Mathf.SmoothStep(0f, 1f, (float)(local / .14d)));
            if (local < .30d) return MinimumPower;
            return Mathf.Lerp(MinimumPower, 1f, Mathf.SmoothStep(0f, 1f, (float)((local - .30d) / (FaultDuration - .30d))));
        }
    }
}
