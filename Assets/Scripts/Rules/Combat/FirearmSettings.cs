using System;

namespace BarPromenade
{
    /// <summary>Shared unpaused firearm clocks; each weapon owns its ammunition mechanism.</summary>
    public class FirearmSettings
    {
        public FirearmSettings(int magazineCapacity, float fireCooldownSeconds, float raiseSeconds, float reloadSeconds)
        {
            if (magazineCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(magazineCapacity));
            MagazineCapacity = magazineCapacity;
            FireCooldownSeconds = Positive(fireCooldownSeconds, nameof(fireCooldownSeconds));
            RaiseSeconds = Positive(raiseSeconds, nameof(raiseSeconds));
            ReloadSeconds = Positive(reloadSeconds, nameof(reloadSeconds));
        }

        public int MagazineCapacity { get; }
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
