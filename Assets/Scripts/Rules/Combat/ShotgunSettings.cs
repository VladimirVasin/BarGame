using System;

namespace BarPromenade
{
    /// <summary>A heavy two-chamber break-action shotgun. Spread offsets are tangent-plane coordinates.</summary>
    public sealed class ShotgunSettings : FirearmSettings
    {
        public static ShotgunSettings Prototype { get; } = new ShotgunSettings();

        public ShotgunSettings(float fireCooldownSeconds = .55f, float raiseSeconds = .35f, float reloadSeconds = 2.8f)
            : base(2, fireCooldownSeconds, raiseSeconds, reloadSeconds) { }

        public int PelletCount => 12;
        public float PelletDamage => 15f;
        public float SpreadHalfAngleDegrees => 3f;
        public float FullDamageRange => 8f;
        public float MinimumDamageRange => 25f;
        public float MinimumDamageMultiplier => .25f;
        public ProjectileDamageProfile DamageProfile => ProjectileDamageProfile.Shotgun;
        public float OpenSeconds => ReloadSeconds * (.4f / 2.8f);
        public float EjectSpentSeconds => ReloadSeconds * (.65f / 2.8f);
        public float LoadFirstSeconds => ReloadSeconds * (1.6f / 2.8f);
        public float LoadSecondSeconds => ReloadSeconds * (2.2f / 2.8f);
        public float CloseStartSeconds => ReloadSeconds * (2.4f / 2.8f);

        public float ResolvePelletDamage(float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
                throw new ArgumentOutOfRangeException(nameof(distance));
            float falloff = Math.Max(0f, Math.Min(1f,
                (distance - FullDamageRange) / (MinimumDamageRange - FullDamageRange)));
            return PelletDamage * (1f + (MinimumDamageMultiplier - 1f) * falloff);
        }

        /// <summary>Even disk coverage with a deterministic per-shot rotation; no global random state.</summary>
        public (float x, float y) PelletSpread(int pelletIndex, int shotSequence)
        {
            if (pelletIndex < 0 || pelletIndex >= PelletCount)
                throw new ArgumentOutOfRangeException(nameof(pelletIndex));
            uint seed = unchecked((uint)shotSequence * 747796405u + 2891336453u);
            seed = ((seed >> (int)((seed >> 28) + 4)) ^ seed) * 277803737u;
            seed = (seed >> 22) ^ seed;
            double rotation = seed / ((double)uint.MaxValue + 1d) * Math.PI * 2d;
            double angle = rotation + pelletIndex * 2.399963229728653d;
            double radius = Math.Sqrt((pelletIndex + .5d) / PelletCount) *
                Math.Tan(SpreadHalfAngleDegrees * Math.PI / 180d);
            return ((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
        }
    }
}
