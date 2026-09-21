using System;

namespace BarPromenade
{
    public enum MeleeBodyRegion { Torso, Head, LeftArm, RightArm, LeftLeg, RightLeg }
    public enum MeleeHitSide { Front, Rear, Left, Right, Top, Bottom }

    /// <summary>An anatomical surface captured when the weapon reaches the posed body.</summary>
    public readonly struct MeleeHitLocation
    {
        public MeleeBodyRegion Region { get; }
        public MeleeHitSide Side { get; }
        public bool IsCritical => Region == MeleeBodyRegion.Head;
        public bool IsFinisher => IsCritical && Side == MeleeHitSide.Rear;

        public MeleeHitLocation(MeleeBodyRegion region, MeleeHitSide side)
        {
            ValidateRegion(region);
            if (side < MeleeHitSide.Front || side > MeleeHitSide.Bottom)
                throw new ArgumentOutOfRangeException(nameof(side));
            Region = region;
            Side = side;
        }

        /// <summary>Front/rear own inclusive 45-degree cones in the anatomical frame.
        /// The remaining surface belongs to its dominant lateral or vertical axis.</summary>
        public static MeleeHitLocation FromLocalSurface(MeleeBodyRegion region, float right, float up, float forward)
        {
            ValidateRegion(region);
            Finite(right, nameof(right));
            Finite(up, nameof(up));
            Finite(forward, nameof(forward));
            // Squared doubles keep the boundary stable, scale-independent and finite
            // even when the supplied finite floats are very large or very small.
            double lateralSquared = (double)right * right + (double)up * up;
            MeleeHitSide side;
            if (forward != 0f && (double)forward * forward >= lateralSquared)
                side = forward > 0f ? MeleeHitSide.Front : MeleeHitSide.Rear;
            else if (Math.Abs(up) > Math.Abs(right))
                side = up > 0f ? MeleeHitSide.Top : MeleeHitSide.Bottom;
            else if (right != 0f)
                side = right > 0f ? MeleeHitSide.Right : MeleeHitSide.Left;
            else
                side = MeleeHitSide.Front;
            return new MeleeHitLocation(region, side);
        }

        private static void ValidateRegion(MeleeBodyRegion region)
        {
            if (region < MeleeBodyRegion.Torso || region > MeleeBodyRegion.RightLeg)
                throw new ArgumentOutOfRangeException(nameof(region));
        }

        private static void Finite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>The immutable crowbar damage table shared by both combatants.</summary>
    public sealed class MeleeDamageProfile
    {
        public static MeleeDamageProfile Crowbar { get; } = new MeleeDamageProfile();

        private MeleeDamageProfile() { }

        /// <summary>Resolves damage before guard reduction. A rear head contact requests
        /// full defeat; the combatant applies that only after resolving its protection.</summary>
        public float ResolveDamage(float baseDamage, float maxHealth, MeleeHitLocation location)
        {
            if (float.IsNaN(baseDamage) || float.IsInfinity(baseDamage) || baseDamage < 0f)
                throw new ArgumentOutOfRangeException(nameof(baseDamage));
            if (float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || maxHealth <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (baseDamage == 0f) return 0f;
            if (location.IsFinisher) return maxHealth;
            if (location.IsCritical) return (float)Math.Min((double)baseDamage * 2d, (double)maxHealth * .99d);
            double scale = location.Region switch
            {
                MeleeBodyRegion.Torso => location.Side == MeleeHitSide.Rear ? 1.25d : 1d,
                MeleeBodyRegion.LeftArm => .5d,
                MeleeBodyRegion.RightArm => .5d,
                MeleeBodyRegion.LeftLeg => .75d,
                MeleeBodyRegion.RightLeg => .75d,
                _ => throw new ArgumentOutOfRangeException(nameof(location))
            };
            return (float)Math.Min(float.MaxValue, baseDamage * scale);
        }
    }
}
