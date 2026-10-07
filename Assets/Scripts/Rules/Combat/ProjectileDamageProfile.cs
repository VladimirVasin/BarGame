using System;

namespace BarPromenade
{
    /// <summary>Pistol wounds have anatomical multipliers, without a rear-head finishing rule.</summary>
    public sealed class ProjectileDamageProfile
    {
        public static ProjectileDamageProfile Pistol { get; } = new ProjectileDamageProfile();

        private ProjectileDamageProfile() { }

        public float ResolveDamage(float baseDamage, MeleeHitLocation location)
        {
            if (float.IsNaN(baseDamage) || float.IsInfinity(baseDamage) || baseDamage < 0f)
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            double scale = location.Region switch
            {
                MeleeBodyRegion.Head => 2d,
                MeleeBodyRegion.Torso => 1d,
                MeleeBodyRegion.LeftArm or MeleeBodyRegion.RightArm => .5d,
                MeleeBodyRegion.LeftLeg or MeleeBodyRegion.RightLeg => .75d,
                _ => throw new ArgumentOutOfRangeException(nameof(location))
            };
            return (float)Math.Min(float.MaxValue, baseDamage * scale);
        }
    }
}
