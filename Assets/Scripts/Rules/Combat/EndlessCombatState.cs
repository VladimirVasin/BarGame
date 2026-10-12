using System;

namespace BarPromenade
{
    /// <summary>One terminal observation per encounter, with a pause-aware clock supplied by runtime.</summary>
    public sealed class EndlessCombatState
    {
        public const float OpponentIntervalSeconds = 2.5f;
        private enum Phase { Fighting, Waiting, Finished }
        private Phase phase;
        private double remaining;

        public long Kills { get; private set; }
        public bool IsWaiting => phase == Phase.Waiting;
        public bool IsFinished => phase == Phase.Finished;
        public float RemainingSeconds => (float)remaining;

        public void Reset()
        {
            phase = Phase.Fighting;
            Kills = 0;
            remaining = 0d;
        }

        public void ObserveDefeats(bool heroDefeated, bool opponentDefeated)
        {
            if (phase == Phase.Finished) return;
            if (phase == Phase.Fighting && opponentDefeated)
            {
                Kills++;
                phase = Phase.Waiting;
                remaining = OpponentIntervalSeconds;
            }
            // A mutual defeat still counts the enemy, but never schedules another.
            if (heroDefeated) { phase = Phase.Finished; remaining = 0d; }
        }

        public bool Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!IsWaiting) return false;
            remaining = Math.Max(0d, remaining - seconds);
            return remaining <= .0000001d;
        }

        public void BeginNextOpponent()
        {
            if (!IsWaiting || remaining > .0000001d)
                throw new InvalidOperationException("The next opponent is not ready.");
            phase = Phase.Fighting;
        }
    }
}
