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
        public float PelletDamage => 26f;
        public float SpreadHalfAngleDegrees => 3f;
        private static readonly float[] Ranges = { 0f, 1f, 3f, 8f, 15f, 25f, 40f };
        private static readonly float[] Damage = { 26f, 24f, 20f, 10f, 4f, 1.5f, 0f };
        private static readonly float[] Momentum = { 420f, 380f, 200f, 50f, 10f, 2f, 0f };
        public static float MaximumVolleyMomentum => Momentum[0];
        public ProjectileDamageProfile DamageProfile => ProjectileDamageProfile.Shotgun;
        public float OpenSeconds => ReloadSeconds * (.4f / 2.8f);
        public float EjectSpentSeconds => ReloadSeconds * (.65f / 2.8f);
        public float LoadFirstSeconds => ReloadSeconds * (1.6f / 2.8f);
        public float LoadSecondSeconds => ReloadSeconds * (2.2f / 2.8f);
        public float CloseStartSeconds => ReloadSeconds * (2.4f / 2.8f);

        public float ResolvePelletDamage(float distance) => ResolveDistance(distance, Damage);

        /// <summary>Each actual pellet contributes its share; missed pellets add no launch force.</summary>
        public float ResolvePelletMomentum(float distance) => ResolveDistance(distance, Momentum) / PelletCount;

        /// <summary>A complete close head volley can remove fourteen of the sixteen authored sectors.</summary>
        public float ResolvePelletHeadTrauma(float distance) => ResolvePelletDamage(distance) / 24f * 14f / PelletCount;

        private static float ResolveDistance(float distance, float[] values)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
                throw new ArgumentOutOfRangeException(nameof(distance));
            for (int i = 1; i < Ranges.Length; i++)
                if (distance < Ranges[i])
                {
                    float t = (distance - Ranges[i - 1]) / (Ranges[i] - Ranges[i - 1]);
                    return values[i - 1] + (values[i] - values[i - 1]) * t;
                }
            return values[values.Length - 1];
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
