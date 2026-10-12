using System;

namespace BarPromenade
{
    /// <summary>Shared unpaused firearm clocks; each weapon owns its ammunition mechanism.</summary>
    public class FirearmSettings
    {
        public FirearmSettings(int magazineCapacity, float fireCooldownSeconds, float raiseSeconds, float reloadSeconds,
            int startingReserveRounds = 0, int reserveCapacity = 0)
        {
            if (magazineCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(magazineCapacity));
            if (reserveCapacity < 0) throw new ArgumentOutOfRangeException(nameof(reserveCapacity));
            if (startingReserveRounds < 0 || startingReserveRounds > reserveCapacity)
                throw new ArgumentOutOfRangeException(nameof(startingReserveRounds));
            MagazineCapacity = magazineCapacity;
            StartingReserveRounds = startingReserveRounds;
            ReserveCapacity = reserveCapacity;
            FireCooldownSeconds = Positive(fireCooldownSeconds, nameof(fireCooldownSeconds));
            RaiseSeconds = Positive(raiseSeconds, nameof(raiseSeconds));
            ReloadSeconds = Positive(reloadSeconds, nameof(reloadSeconds));
        }

        public int MagazineCapacity { get; }
        public int StartingReserveRounds { get; }
        public int ReserveCapacity { get; }
        public float FireCooldownSeconds { get; }
        public float RaiseSeconds { get; }
        public float ReloadSeconds { get; }

        private static float Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }
}
