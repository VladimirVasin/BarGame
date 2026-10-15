using System;

namespace BarPromenade
{
    /// <summary>The retained lock and drive progress; releasing the valve never lowers the gate.</summary>
    public sealed class WarehouseGateState
    {
        public const float TurnSeconds = 6f;
        public const int Turns = 3;
        private double elapsed;

        public bool Unlocked { get; private set; }
        public float Progress => (float)(elapsed / TurnSeconds);
        public bool IsOpen => elapsed >= TurnSeconds;

        public bool TryUnlock(bool hasKey)
        {
            if (Unlocked) return true;
            if (!hasKey) return false;
            Unlocked = true;
            return true;
        }

        /// <summary>Only held input turns an unlocked drive; the terminal position remains latched.</summary>
        public bool Advance(float seconds, bool held)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Unlocked || !held || seconds == 0f || IsOpen) return false;
            elapsed = Math.Min(TurnSeconds, elapsed + seconds);
            return true;
        }

        public void Reset()
        {
            Unlocked = false;
            elapsed = 0d;
        }
    }
}
