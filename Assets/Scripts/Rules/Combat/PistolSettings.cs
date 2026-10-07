using System;

namespace BarPromenade
{
    /// <summary>The isolated prototype's magazine and unpaused action timings.</summary>
    public sealed class PistolSettings
    {
        public static PistolSettings Prototype { get; } = new PistolSettings();

        public PistolSettings(int magazineCapacity = 8, float fireCooldownSeconds = .4f,
            float raiseSeconds = .25f, float reloadSeconds = 1.8f)
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
