using System;

namespace BarPromenade
{
    /// <summary>The bounded weapon choices offered by the isolated combat test.</summary>
    public enum CombatWeaponId { Crowbar, Pistol, Shotgun }

    public enum CombatTestMode { Standard, Endless }

    /// <summary>The complete isolated combat launch selection, consumed as one session value.</summary>
    public readonly struct CombatTestSelection : IEquatable<CombatTestSelection>
    {
        public CombatLoadoutSelection Loadout { get; }
        public CombatTestMode Mode { get; }
        public CombatEncounterSettings EncounterSettings { get; }
        public static CombatTestSelection Default => new CombatTestSelection(CombatLoadoutSelection.Default);

        public CombatTestSelection(CombatLoadoutSelection loadout, CombatTestMode mode = CombatTestMode.Standard,
            CombatEncounterSettings? encounterSettings = null)
        {
            if (mode != CombatTestMode.Standard && mode != CombatTestMode.Endless)
                throw new ArgumentOutOfRangeException(nameof(mode));
            Loadout = loadout;
            Mode = mode;
            EncounterSettings = encounterSettings ?? CombatEncounterSettings.Default;
            EncounterSettings.Validate();
        }

        public bool Equals(CombatTestSelection other) => Loadout == other.Loadout && Mode == other.Mode &&
            EncounterSettings == other.EncounterSettings;
        public override bool Equals(object obj) => obj is CombatTestSelection other && Equals(other);
        public override int GetHashCode() => ((Loadout.GetHashCode() * 397) ^ (int)Mode) * 397 ^ EncounterSettings.GetHashCode();
        public static bool operator ==(CombatTestSelection left, CombatTestSelection right) => left.Equals(right);
        public static bool operator !=(CombatTestSelection left, CombatTestSelection right) => !left.Equals(right);
    }

    /// <summary>All three weapons are carried; only the starting hand choice varies.</summary>
    public readonly struct CombatLoadoutSelection : IEquatable<CombatLoadoutSelection>
    {
        public CombatWeaponId InitialHeld { get; }
        public static CombatLoadoutSelection Default => new CombatLoadoutSelection(CombatWeaponId.Crowbar);

        public CombatLoadoutSelection(CombatWeaponId initialHeld)
        {
            if (!ContainsWeapon(initialHeld))
                throw new ArgumentOutOfRangeException(nameof(initialHeld));
            InitialHeld = initialHeld;
        }

        /// <summary>Legacy pair selections retain the starting hand choice, with every weapon now carried.</summary>
        public CombatLoadoutSelection(CombatWeaponId secondary, CombatWeaponId initialHeld) : this(initialHeld)
        {
            if (secondary != CombatWeaponId.Crowbar && secondary != CombatWeaponId.Shotgun)
                throw new ArgumentOutOfRangeException(nameof(secondary));
        }

        public bool Contains(CombatWeaponId weapon) => ContainsWeapon(weapon);

        private static bool ContainsWeapon(CombatWeaponId weapon) => weapon == CombatWeaponId.Pistol ||
            weapon == CombatWeaponId.Crowbar || weapon == CombatWeaponId.Shotgun;

        public CombatLoadoutSelection WithInitialHeld(CombatWeaponId weapon) =>
            new CombatLoadoutSelection(weapon);

        /// <summary>Legacy single-weapon launches retain their starting hand choice.</summary>
        public static CombatLoadoutSelection FromLegacyWeapon(CombatWeaponId weapon) => new CombatLoadoutSelection(weapon);

        public bool Equals(CombatLoadoutSelection other) => InitialHeld == other.InitialHeld;
        public override bool Equals(object obj) => obj is CombatLoadoutSelection other && Equals(other);
        public override int GetHashCode() => (int)InitialHeld;
        public static bool operator ==(CombatLoadoutSelection left, CombatLoadoutSelection right) => left.Equals(right);
        public static bool operator !=(CombatLoadoutSelection left, CombatLoadoutSelection right) => !left.Equals(right);
    }
}
