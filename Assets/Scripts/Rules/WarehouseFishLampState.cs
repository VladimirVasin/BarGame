using System;

namespace BarPromenade
{
    public enum WarehouseFishLampPhase { Waiting = 0, Heating = 1, Burst = 2 }

    /// <summary>One retained failure of the warehouse pendant, independent of its presentation.</summary>
    public sealed class WarehouseFishLampState
    {
        public const float HeatingSeconds = 2f;
        private double elapsed;

        public WarehouseFishLampPhase Phase { get; private set; }
        public float HeatingElapsed => (float)elapsed;
        public float Heat01 => (float)(elapsed / HeatingSeconds);

        public bool TryBegin(bool keyReceived, bool isDescending)
        {
            if (Phase != WarehouseFishLampPhase.Waiting || !keyReceived || !isDescending) return false;
            Phase = WarehouseFishLampPhase.Heating;
            return true;
        }

        /// <summary>The runtime advances only active, unpaused time; the terminal failure cannot replay.</summary>
        public bool Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Phase != WarehouseFishLampPhase.Heating || seconds == 0f) return false;
            elapsed = Math.Min(HeatingSeconds, elapsed + seconds);
            if (elapsed >= HeatingSeconds) Phase = WarehouseFishLampPhase.Burst;
            return true;
        }

        public void Reset()
        {
            Phase = WarehouseFishLampPhase.Waiting;
            elapsed = 0d;
        }
    }
}
