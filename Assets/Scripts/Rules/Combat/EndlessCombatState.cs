using System;

namespace BarPromenade
{
    /// <summary>One pending spawn, driven only by active gameplay time and confirmed actual population.</summary>
    public sealed class EndlessCombatState
    {
        // Retained only for legacy single-opponent callers.
        public const float OpponentIntervalSeconds = 2.5f;
        private CombatEncounterSettings settings;
        private Random random;
        private bool atCapacity;
        private bool spawningEnabled = true;
        private double remaining;
        private bool legacyOpponentDefeated;
        private int legacyLiveCount;

        public long Kills { get; private set; }
        public bool IsWaiting => spawningEnabled && !IsFinished && !atCapacity;
        public bool SpawningEnabled => spawningEnabled;
        public bool IsFinished { get; private set; }
        public float RemainingSeconds => (float)remaining;

        public void Reset(CombatEncounterSettings encounterSettings, int seed)
        {
            encounterSettings.Validate();
            settings = encounterSettings;
            random = new Random(seed);
            Kills = 0;
            IsFinished = false;
            spawningEnabled = true;
            atCapacity = false;
            remaining = Sample(settings.FirstSpawnMinSeconds, settings.FirstSpawnMaxSeconds);
        }

        /// <summary>The caller reports each participant's defeat once, including a mutual defeat.</summary>
        public void Observe(bool heroDefeated, int liveCount, int newlyDefeatedCount = 0)
        {
            ValidateLiveCount(liveCount);
            if (newlyDefeatedCount < 0) throw new ArgumentOutOfRangeException(nameof(newlyDefeatedCount));
            if (IsFinished) return;
            Kills += newlyDefeatedCount;
            if (heroDefeated) { IsFinished = true; remaining = 0d; return; }
            if (!spawningEnabled) return;
            UpdateCapacity(liveCount);
        }

        public bool Advance(float seconds, int liveCount)
        {
            ValidateSeconds(seconds);
            ValidateLiveCount(liveCount);
            if (IsFinished || !spawningEnabled) return false;
            UpdateCapacity(liveCount);
            if (!IsWaiting) return false;
            remaining = Math.Max(0d, remaining - seconds);
            return remaining <= .0000001d;
        }

        /// <summary>Clears pending arrivals without finishing combat; reopening starts a fresh interval.</summary>
        public void SetSpawningEnabled(bool enabled, int liveCount)
        {
            ValidateLiveCount(liveCount);
            if (spawningEnabled == enabled) return;
            spawningEnabled = enabled;
            remaining = 0d;
            if (!enabled || IsFinished) return;
            atCapacity = liveCount >= settings.MaximumLiveOpponents;
            if (!atCapacity)
                remaining = liveCount == 0 ? Sample(settings.FirstSpawnMinSeconds, settings.FirstSpawnMaxSeconds) :
                    Sample(settings.SpawnIntervalMinSeconds, settings.SpawnIntervalMaxSeconds);
        }

        /// <summary>A successful creation consumes readiness and starts a fresh interval without carrying excess time.</summary>
        public void ConfirmSpawn(int liveCountAfterSpawn)
        {
            ValidateLiveCount(liveCountAfterSpawn);
            if (!IsWaiting || remaining > .0000001d || liveCountAfterSpawn < 1)
                throw new InvalidOperationException("The next opponent is not ready.");
            atCapacity = liveCountAfterSpawn >= settings.MaximumLiveOpponents;
            remaining = atCapacity ? 0d : Sample(settings.SpawnIntervalMinSeconds, settings.SpawnIntervalMaxSeconds);
        }

        /// <summary>A blocked arena delays this single pending spawn rather than queuing creations.</summary>
        public void DeferSpawn(float retrySeconds = .5f)
        {
            ValidateSeconds(retrySeconds);
            if (!IsWaiting || remaining > .0000001d)
                throw new InvalidOperationException("Only a ready spawn can be deferred.");
            remaining = retrySeconds;
        }

        private void UpdateCapacity(int liveCount)
        {
            if (liveCount >= settings.MaximumLiveOpponents)
            {
                atCapacity = true;
                remaining = 0d;
            }
            else if (atCapacity)
            {
                atCapacity = false;
                remaining = Sample(settings.SpawnIntervalMinSeconds, settings.SpawnIntervalMaxSeconds);
            }
        }

        private double Sample(float min, float max) => min + random.NextDouble() * (max - min);
        private static void ValidateLiveCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        }
        private static void ValidateSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        // Compatibility entry points for the former one-opponent encounter.
        public void Reset()
        {
            Reset(new CombatEncounterSettings(1, OpponentIntervalSeconds, OpponentIntervalSeconds,
                OpponentIntervalSeconds, OpponentIntervalSeconds), 0);
            legacyOpponentDefeated = false;
            legacyLiveCount = 1;
            Observe(false, legacyLiveCount);
        }
        public void ObserveDefeats(bool heroDefeated, bool opponentDefeated)
        {
            int newlyDefeated = opponentDefeated && !legacyOpponentDefeated ? 1 : 0;
            legacyOpponentDefeated = opponentDefeated;
            legacyLiveCount = opponentDefeated ? 0 : 1;
            Observe(heroDefeated, legacyLiveCount, newlyDefeated);
        }
        public bool Advance(float seconds) => Advance(seconds, legacyLiveCount);
        public void BeginNextOpponent()
        {
            ConfirmSpawn(1);
            legacyLiveCount = 1;
            legacyOpponentDefeated = false;
        }
    }
}
