using System;

namespace BarPromenade
{
    /// <summary>Finite post-crowbar encounter. Travel retains progress; retry never resets puzzles or inventory.</summary>
    public sealed class StairwellEncounterState
    {
        public const int Population = 21;
        public const int MaximumMobile = 4;
        public const int MaximumAttackers = 2;
        private uint defeated;
        public bool Awakened { get; private set; }
        public bool Completed { get; private set; }
        public bool FirstContactPassed { get; private set; }
        public float Elapsed { get; private set; }
        public int Retries { get; private set; }
        public int DefeatedCount { get; private set; }
        public int Generation { get; private set; }

        public bool TryAwaken(bool sourceCollected, bool controlsReady)
        {
            if (Awakened || Completed || !sourceCollected || !controlsReady) return false;
            Awakened = true;
            return true;
        }

        public bool IsDefeated(int index) { Validate(index); return (defeated & (1u << index)) != 0; }
        public bool MarkDefeated(int index)
        {
            Validate(index);
            if (IsDefeated(index)) return false;
            defeated |= 1u << index; DefeatedCount++;
            return true;
        }

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Awakened && !Completed) Elapsed = Math.Min(86400f, Elapsed + seconds);
        }

        public void Complete() { if (Awakened) Completed = true; }
        public void FinishIntroduction() { if (Awakened) FirstContactPassed = true; }
        public void Retry()
        {
            if (!Awakened || Completed) return;
            defeated = 0; DefeatedCount = 0; Elapsed = 0f; FirstContactPassed = false; Retries++; Generation++;
        }
        public readonly struct Checkpoint
        {
            internal readonly uint Defeated;
            internal readonly bool Awakened, Completed, FirstContactPassed;
            internal readonly float Elapsed;
            internal readonly int Retries, DefeatedCount;
            internal Checkpoint(StairwellEncounterState state)
            {
                Defeated = state.defeated; Awakened = state.Awakened; Completed = state.Completed;
                FirstContactPassed = state.FirstContactPassed; Elapsed = state.Elapsed;
                Retries = state.Retries; DefeatedCount = state.DefeatedCount;
            }
        }
        public Checkpoint CaptureCheckpoint() => new Checkpoint(this);
        public void RestoreCheckpoint(Checkpoint saved)
        {
            defeated = saved.Defeated; Awakened = saved.Awakened; Completed = saved.Completed;
            FirstContactPassed = saved.FirstContactPassed; Elapsed = saved.Elapsed;
            Retries = saved.Retries; DefeatedCount = saved.DefeatedCount;
            Generation = Generation == int.MaxValue ? 1 : Generation + 1;
        }

        public void Reset()
        {
            defeated = 0; DefeatedCount = Retries = 0; Elapsed = 0f;
            Awakened = Completed = FirstContactPassed = false; Generation++;
        }
        public static float WakeDelay(int index) { Validate(index); return index == 0 ? .35f : 1.2f + (index * 8 % Population) * .28f; }
        private static void Validate(int index)
        { if ((uint)index >= Population) throw new ArgumentOutOfRangeException(nameof(index)); }
    }
}
