using System;

namespace BarPromenade
{
    /// <summary>Capacity and active-time pacing for the isolated endless encounter.</summary>
    public readonly struct CombatEncounterSettings : IEquatable<CombatEncounterSettings>
    {
        public const int OpponentCapacity = 5;
        public int MaximumLiveOpponents { get; }
        public float FirstSpawnMinSeconds { get; }
        public float FirstSpawnMaxSeconds { get; }
        public float SpawnIntervalMinSeconds { get; }
        public float SpawnIntervalMaxSeconds { get; }
        public float MinimumHeroDistance { get; }
        public static CombatEncounterSettings Default => new CombatEncounterSettings(5);

        public CombatEncounterSettings(int maximumLiveOpponents,
            float firstSpawnMinSeconds = 2f, float firstSpawnMaxSeconds = 4f,
            float spawnIntervalMinSeconds = 3f, float spawnIntervalMaxSeconds = 7f,
            float minimumHeroDistance = 3f)
        {
            if (maximumLiveOpponents < 1 || maximumLiveOpponents > OpponentCapacity)
                throw new ArgumentOutOfRangeException(nameof(maximumLiveOpponents));
            ValidatePositive(firstSpawnMinSeconds, nameof(firstSpawnMinSeconds));
            ValidatePositive(firstSpawnMaxSeconds, nameof(firstSpawnMaxSeconds));
            ValidatePositive(spawnIntervalMinSeconds, nameof(spawnIntervalMinSeconds));
            ValidatePositive(spawnIntervalMaxSeconds, nameof(spawnIntervalMaxSeconds));
            ValidatePositive(minimumHeroDistance, nameof(minimumHeroDistance));
            if (firstSpawnMaxSeconds < firstSpawnMinSeconds)
                throw new ArgumentOutOfRangeException(nameof(firstSpawnMaxSeconds));
            if (spawnIntervalMaxSeconds < spawnIntervalMinSeconds)
                throw new ArgumentOutOfRangeException(nameof(spawnIntervalMaxSeconds));
            MaximumLiveOpponents = maximumLiveOpponents;
            FirstSpawnMinSeconds = firstSpawnMinSeconds;
            FirstSpawnMaxSeconds = firstSpawnMaxSeconds;
            SpawnIntervalMinSeconds = spawnIntervalMinSeconds;
            SpawnIntervalMaxSeconds = spawnIntervalMaxSeconds;
            MinimumHeroDistance = minimumHeroDistance;
        }

        public CombatEncounterSettings WithMaximumLiveOpponents(int maximum) => new CombatEncounterSettings(
            maximum, FirstSpawnMinSeconds, FirstSpawnMaxSeconds,
            SpawnIntervalMinSeconds, SpawnIntervalMaxSeconds, MinimumHeroDistance);

        public void Validate()
        {
            _ = new CombatEncounterSettings(MaximumLiveOpponents, FirstSpawnMinSeconds, FirstSpawnMaxSeconds,
                SpawnIntervalMinSeconds, SpawnIntervalMaxSeconds, MinimumHeroDistance);
        }

        private static void ValidatePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        public bool Equals(CombatEncounterSettings other) =>
            MaximumLiveOpponents == other.MaximumLiveOpponents &&
            FirstSpawnMinSeconds == other.FirstSpawnMinSeconds && FirstSpawnMaxSeconds == other.FirstSpawnMaxSeconds &&
            SpawnIntervalMinSeconds == other.SpawnIntervalMinSeconds && SpawnIntervalMaxSeconds == other.SpawnIntervalMaxSeconds &&
            MinimumHeroDistance == other.MinimumHeroDistance;
        public override bool Equals(object obj) => obj is CombatEncounterSettings other && Equals(other);
        public override int GetHashCode()
        {
            int hash = MaximumLiveOpponents;
            hash = hash * 397 ^ FirstSpawnMinSeconds.GetHashCode();
            hash = hash * 397 ^ FirstSpawnMaxSeconds.GetHashCode();
            hash = hash * 397 ^ SpawnIntervalMinSeconds.GetHashCode();
            hash = hash * 397 ^ SpawnIntervalMaxSeconds.GetHashCode();
            return hash * 397 ^ MinimumHeroDistance.GetHashCode();
        }
        public static bool operator ==(CombatEncounterSettings left, CombatEncounterSettings right) => left.Equals(right);
        public static bool operator !=(CombatEncounterSettings left, CombatEncounterSettings right) => !left.Equals(right);
    }
}
