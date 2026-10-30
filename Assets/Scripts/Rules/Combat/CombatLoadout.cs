using System;

namespace BarPromenade
{
    /// <summary>The bounded weapon choices offered by the isolated combat test.</summary>
    public enum CombatWeaponId { Crowbar, Pistol, Shotgun }

    public enum CombatTestMode { Standard, Endless, Crane, Laboratory }

    /// <summary>Permanent role of a pooled combat opponent, independent of the encounter mode.</summary>
    public enum CombatEnemyKind { Zombie, Docker, Crane, Room, Imprint, LongExposure }

    /// <summary>The complete isolated combat launch selection, consumed as one session value.</summary>
    public readonly struct CombatTestSelection : IEquatable<CombatTestSelection>
    {
        public CombatLoadoutSelection Loadout { get; }
        public CombatTestMode Mode { get; }
        public CombatEncounterSettings EncounterSettings { get; }
        public CombatEnemyKind LaboratoryEnemy { get; }
        public static CombatTestSelection Default => new CombatTestSelection(CombatLoadoutSelection.Default);

        public CombatTestSelection(CombatLoadoutSelection loadout, CombatTestMode mode = CombatTestMode.Standard,
            CombatEncounterSettings? encounterSettings = null, CombatEnemyKind laboratoryEnemy = CombatEnemyKind.Zombie)
        {
            if (mode != CombatTestMode.Standard && mode != CombatTestMode.Endless && mode != CombatTestMode.Crane && mode != CombatTestMode.Laboratory)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if ((uint)laboratoryEnemy > (uint)CombatEnemyKind.LongExposure)
                throw new ArgumentOutOfRangeException(nameof(laboratoryEnemy));
            Loadout = loadout;
            Mode = mode;
            LaboratoryEnemy = laboratoryEnemy;
            EncounterSettings = encounterSettings ?? CombatEncounterSettings.Default;
            EncounterSettings.Validate();
        }

        public bool Equals(CombatTestSelection other) => Loadout == other.Loadout && Mode == other.Mode &&
            EncounterSettings == other.EncounterSettings && LaboratoryEnemy == other.LaboratoryEnemy;
        public override bool Equals(object obj) => obj is CombatTestSelection other && Equals(other);
        public override int GetHashCode() => (((Loadout.GetHashCode() * 397) ^ (int)Mode) * 397 ^ EncounterSettings.GetHashCode()) * 397 ^ (int)LaboratoryEnemy;
        public static bool operator ==(CombatTestSelection left, CombatTestSelection right) => left.Equals(right);
        public static bool operator !=(CombatTestSelection left, CombatTestSelection right) => !left.Equals(right);
    }

    /// <summary>The debug kit carries all weapons; the acquired story tool carries only its crowbar.</summary>
    public readonly struct CombatLoadoutSelection : IEquatable<CombatLoadoutSelection>
    {
        private readonly bool crowbarOnly;
        public CombatWeaponId InitialHeld { get; }
        public static CombatLoadoutSelection CrowbarOnly => new CombatLoadoutSelection(CombatWeaponId.Crowbar, true);
        public static CombatLoadoutSelection Default => new CombatLoadoutSelection(CombatWeaponId.Crowbar);

        public CombatLoadoutSelection(CombatWeaponId initialHeld) : this(initialHeld, false) { }

        private CombatLoadoutSelection(CombatWeaponId initialHeld, bool onlyCrowbar)
        {
            if (!ContainsWeapon(initialHeld) || onlyCrowbar && initialHeld != CombatWeaponId.Crowbar)
                throw new ArgumentOutOfRangeException(nameof(initialHeld));
            InitialHeld = initialHeld;
            crowbarOnly = onlyCrowbar;
        }

        /// <summary>Legacy pair selections retain the starting hand choice, with every weapon now carried.</summary>
        public CombatLoadoutSelection(CombatWeaponId secondary, CombatWeaponId initialHeld) : this(initialHeld)
        {
            if (secondary != CombatWeaponId.Crowbar && secondary != CombatWeaponId.Shotgun)
                throw new ArgumentOutOfRangeException(nameof(secondary));
        }

        public bool Contains(CombatWeaponId weapon) => ContainsWeapon(weapon) && (!crowbarOnly || weapon == CombatWeaponId.Crowbar);

        private static bool ContainsWeapon(CombatWeaponId weapon) => weapon == CombatWeaponId.Pistol ||
            weapon == CombatWeaponId.Crowbar || weapon == CombatWeaponId.Shotgun;

        public CombatLoadoutSelection WithInitialHeld(CombatWeaponId weapon) =>
            new CombatLoadoutSelection(weapon, crowbarOnly);

        /// <summary>Legacy single-weapon launches retain their starting hand choice.</summary>
        public static CombatLoadoutSelection FromLegacyWeapon(CombatWeaponId weapon) => new CombatLoadoutSelection(weapon);

        public bool Equals(CombatLoadoutSelection other) => InitialHeld == other.InitialHeld && crowbarOnly == other.crowbarOnly;
        public override bool Equals(object obj) => obj is CombatLoadoutSelection other && Equals(other);
        public override int GetHashCode() => ((int)InitialHeld * 397) ^ (crowbarOnly ? 1 : 0);
        public static bool operator ==(CombatLoadoutSelection left, CombatLoadoutSelection right) => left.Equals(right);
        public static bool operator !=(CombatLoadoutSelection left, CombatLoadoutSelection right) => !left.Equals(right);
    }
}
