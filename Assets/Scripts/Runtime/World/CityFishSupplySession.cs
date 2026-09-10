using UnityEngine;

namespace BarPromenade
{
    /// <summary>Only scalar simulation state survives City unloading. No scene references.</summary>
    public static class CityFishSupplySession
    {
        private static bool initialized;
        private static double lastSessionSeconds;
        public static double WorkingSeconds { get; private set; }
        private static double SessionSeconds => (GameSessionState.GameDayIndex * 1440d +
            GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond;

        public static double Advance(bool blocked)
            => Advance(blocked ? 0f : 1f);

        public static double Advance(float rate)
        {
            double now = SessionSeconds;
            if (initialized && now >= lastSessionSeconds)
                WorkingSeconds += (now - lastSessionSeconds) * Mathf.Clamp01(rate);
            lastSessionSeconds = now;
            initialized = true;
            return WorkingSeconds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewGame()
        {
            initialized = false;
            lastSessionSeconds = WorkingSeconds = 0;
        }
    }
}
