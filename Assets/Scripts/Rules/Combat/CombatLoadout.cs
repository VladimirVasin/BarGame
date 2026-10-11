using System;

namespace BarPromenade
{
    /// <summary>The bounded weapon choices offered by the isolated combat test.</summary>
    public enum CombatWeaponId { Crowbar, Pistol, Shotgun }

    /// <summary>A pistol and exactly one long weapon, with an independent starting hand choice.</summary>
    public readonly struct CombatLoadoutSelection : IEquatable<CombatLoadoutSelection>
    {
        public CombatWeaponId Secondary { get; }
        public CombatWeaponId InitialHeld { get; }
        public static CombatLoadoutSelection Default => new CombatLoadoutSelection(
            CombatWeaponId.Crowbar, CombatWeaponId.Crowbar);

        public CombatLoadoutSelection(CombatWeaponId secondary, CombatWeaponId initialHeld)
        {
            if (secondary != CombatWeaponId.Crowbar && secondary != CombatWeaponId.Shotgun)
                throw new ArgumentOutOfRangeException(nameof(secondary));
            if (initialHeld != CombatWeaponId.Pistol && initialHeld != secondary)
                throw new ArgumentOutOfRangeException(nameof(initialHeld));
            Secondary = secondary;
            InitialHeld = initialHeld;
        }

        public bool Contains(CombatWeaponId weapon) => weapon == CombatWeaponId.Pistol || weapon == Secondary;

        public CombatLoadoutSelection WithSecondary(CombatWeaponId secondary) =>
            new CombatLoadoutSelection(secondary,
                InitialHeld == CombatWeaponId.Pistol ? CombatWeaponId.Pistol : secondary);

        public CombatLoadoutSelection WithInitialHeld(CombatWeaponId weapon) =>
            new CombatLoadoutSelection(Secondary, weapon);

        /// <summary>Legacy single-weapon launches retain their starting hand choice.</summary>
        public static CombatLoadoutSelection FromLegacyWeapon(CombatWeaponId weapon)
        {
            if (weapon == CombatWeaponId.Pistol)
                return new CombatLoadoutSelection(CombatWeaponId.Crowbar, CombatWeaponId.Pistol);
            return new CombatLoadoutSelection(weapon, weapon);
        }

        public bool Equals(CombatLoadoutSelection other) => Secondary == other.Secondary && InitialHeld == other.InitialHeld;
        public override bool Equals(object obj) => obj is CombatLoadoutSelection other && Equals(other);
        public override int GetHashCode() => ((int)Secondary * 397) ^ (int)InitialHeld;
        public static bool operator ==(CombatLoadoutSelection left, CombatLoadoutSelection right) => left.Equals(right);
        public static bool operator !=(CombatLoadoutSelection left, CombatLoadoutSelection right) => !left.Equals(right);
    }
}
