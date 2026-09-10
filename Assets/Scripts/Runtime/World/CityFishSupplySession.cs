using UnityEngine;

namespace BarPromenade
{
    /// <summary>Only scalar simulation state survives City unloading. No scene references.</summary>
    public static class CityFishSupplySession
    {
        private static double lastSessionSeconds;
        public static bool HasStarted { get; private set; }
        public static double WorkingSeconds { get; private set; }
        private static double SessionSeconds => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        public static bool TryStart(bool heroAtDocks)
        {
            if (HasStarted || !heroAtDocks || !GameSessionState.IsGameTimeRunning || GameTimeScaleRuntime.IsPaused)
                return false;
            // Time spent elsewhere before the first visit is not port time.
            // This session latch survives unloading City; returning never restarts it.
            lastSessionSeconds = SessionSeconds;
            HasStarted = true;
            return true;
        }

        public static double Advance(bool blocked)
            => Advance(blocked ? 0f : 1f);

        public static double Advance(float rate)
        {
            if (!HasStarted) return 0d;
            double now = SessionSeconds;
            if (now >= lastSessionSeconds)
                WorkingSeconds += (now - lastSessionSeconds) * Mathf.Clamp01(rate);
            lastSessionSeconds = now;
            return WorkingSeconds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewGame()
        {
            HasStarted = false;
            lastSessionSeconds = WorkingSeconds = 0;
        }
    }
}
