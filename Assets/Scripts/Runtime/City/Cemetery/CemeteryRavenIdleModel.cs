using System;

namespace BarPromenade
{
    public enum CemeteryRavenIdleKind
    {
        Breathe = 0,
        WeightShift = 1,
        WingRuffle = 2,
        Preen = 3
    }

    /// <summary>
    /// A deterministic, absolute-time idle timeline for one perched
    /// raven — the stairwell cat's model reshaped for a bird. Its
    /// result depends only on the seed, the start offset and total
    /// elapsed time, so frame chunking cannot change the selected
    /// moment. The start offset exists because there are two of these
    /// birds: a pair breathing and shifting in perfect unison would
    /// read as one animation played twice, and offsetting the whole
    /// timeline desynchronizes every cycle at once instead of each
    /// being patched separately.
    /// </summary>
    public sealed class CemeteryRavenIdleModel
    {
        public const float BreathePeriodSeconds = 3.2f;
        public const float CycleSeconds = 9f;
        public const float SpecialWindowStartSeconds = 4.0f;
        public const float SpecialWindowEndSeconds = 7.5f;
        public const float WeightShiftDurationSeconds = 0.8f;
        public const float WingRuffleDurationSeconds = 0.9f;
        public const float FirstPreenStartSeconds = 21f;
        public const float PreenIntervalSeconds = 33f;
        public const float PreenDurationSeconds = 2.4f;

        /// <summary>
        /// Roughly four cycles in ten ruffle the wings instead of
        /// shifting weight. A bird that ruffled every cycle would
        /// look agitated, and this yard is deliberately quiet.
        /// </summary>
        public const int WingRufflePercent = 40;

        private readonly int seed;
        private readonly double startOffsetSeconds;
        private double elapsedSeconds;

        public CemeteryRavenIdleModel(
            int seed,
            double startOffsetSeconds)
        {
            this.seed = seed;
            this.startOffsetSeconds =
                double.IsNaN(startOffsetSeconds) ||
                double.IsInfinity(startOffsetSeconds) ||
                startOffsetSeconds < 0d
                    ? 0d
                    : startOffsetSeconds;
            elapsedSeconds = this.startOffsetSeconds;
            Evaluate();
        }

        public double ElapsedSeconds => elapsedSeconds;
        public CemeteryRavenIdleKind CurrentKind { get; private set; }

        /// <summary>Progress 0..1 through the running special, and 0
        /// while the bird is only breathing.</summary>
        public float EventProgress01 { get; private set; }

        /// <summary>+1 or -1, hashed once per event: which side a
        /// weight shift leans, so the bird does not always favour the
        /// same leg.</summary>
        public float EventSign { get; private set; }

        /// <summary>Which wing the running preen works. Only
        /// meaningful while <see cref="IsPreening"/>.</summary>
        public bool PreenOnLeftWing { get; private set; }

        /// <summary>The continuous breath, 0..1. It never stops, even
        /// under a special — lungs do not wait for wings.</summary>
        public float Breathe01 { get; private set; }

        /// <summary>
        /// True while preening. The director hands the head model a
        /// neutral target for the whole span: a preening raven does
        /// not track the hero — the cat's grooming rule, kept.
        /// </summary>
        public bool IsPreening =>
            CurrentKind == CemeteryRavenIdleKind.Preen;

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return;
            }

            elapsedSeconds += deltaTime;
            Evaluate();
        }

        public void Reset()
        {
            elapsedSeconds = startOffsetSeconds;
            Evaluate();
        }

        private void Evaluate()
        {
            Breathe01 = 0.5f + 0.5f * (float)Math.Sin(
                elapsedSeconds / BreathePeriodSeconds *
                (Math.PI * 2d));
            if (TryEvaluatePreen())
            {
                return;
            }

            if (TryEvaluateCycleSpecial())
            {
                return;
            }

            CurrentKind = CemeteryRavenIdleKind.Breathe;
            EventProgress01 = 0f;
            EventSign = 1f;
        }

        /// <summary>
        /// The preen outranks the cycle specials exactly as the cat's
        /// groom does: a bird with its beak in its coverts is not
        /// also shifting its weight.
        /// </summary>
        private bool TryEvaluatePreen()
        {
            if (elapsedSeconds < FirstPreenStartSeconds)
            {
                return false;
            }

            double sinceFirstPreen =
                elapsedSeconds - FirstPreenStartSeconds;
            double preenCycleTime =
                sinceFirstPreen % PreenIntervalSeconds;
            if (preenCycleTime >= PreenDurationSeconds)
            {
                return false;
            }

            int preenIndex = (int)Math.Floor(
                sinceFirstPreen / PreenIntervalSeconds);
            uint preenHash = Hash(unchecked(
                (uint)seed ^
                ((uint)(preenIndex + 1) * 0x01000193u)));
            CurrentKind = CemeteryRavenIdleKind.Preen;
            EventProgress01 = (float)(
                preenCycleTime / PreenDurationSeconds);
            EventSign = 1f;
            PreenOnLeftWing = (preenHash & 1u) == 0u;
            return true;
        }

        private bool TryEvaluateCycleSpecial()
        {
            int cycleIndex = (int)Math.Floor(
                elapsedSeconds / CycleSeconds);
            double cycleTime =
                elapsedSeconds -
                (cycleIndex * (double)CycleSeconds);
            uint cycleHash = Hash(
                unchecked((uint)(seed + cycleIndex)));
            double specialStart =
                SpecialWindowStartSeconds +
                ((cycleHash & 0xffffu) / 65535d) *
                (SpecialWindowEndSeconds -
                 SpecialWindowStartSeconds);
            // One special per cycle, the cat's rule: the hash picks
            // WHICH, so ruffle and shift can never overlap, and the
            // window plus the longer duration still ends inside the
            // cycle.
            bool ruffle =
                ((cycleHash >> 16) % 100u) <
                (uint)WingRufflePercent;
            double duration = ruffle
                ? WingRuffleDurationSeconds
                : WeightShiftDurationSeconds;
            if (cycleTime < specialStart ||
                cycleTime >= specialStart + duration)
            {
                return false;
            }

            CurrentKind = ruffle
                ? CemeteryRavenIdleKind.WingRuffle
                : CemeteryRavenIdleKind.WeightShift;
            EventProgress01 = (float)(
                (cycleTime - specialStart) / duration);
            EventSign =
                (cycleHash & 0x1000000u) == 0u ? 1f : -1f;
            return true;
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
