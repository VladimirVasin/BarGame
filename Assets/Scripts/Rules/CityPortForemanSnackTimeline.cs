using System;

namespace BarPromenade
{
    public enum CityPortForemanSnackPhase { Idle, Bite1, Bite2, Bite3, Discard, Take }

    public readonly struct CityPortForemanSnackSnapshot
    {
        internal CityPortForemanSnackSnapshot(CityPortForemanSnackPhase phase, double seconds,
            int bites, bool holding, long cycle)
        {
            Phase = phase; ActionSeconds = seconds; BitesTaken = bites;
            HoldingCarrot = holding; CycleCount = cycle;
        }
        public CityPortForemanSnackPhase Phase { get; }
        public double ActionSeconds { get; }
        public int BitesTaken { get; }
        public bool HoldingCarrot { get; }
        public long CycleCount { get; }
        public bool StemInFlight => Phase == CityPortForemanSnackPhase.Discard &&
            ActionSeconds >= CityPortForemanSnackTimeline.DiscardReleaseSeconds &&
            ActionSeconds < CityPortForemanSnackTimeline.DiscardFlightEndSeconds;
        public float ThrowProgress => Phase == CityPortForemanSnackPhase.Discard
            ? (float)Math.Max(0d, Math.Min(1d, (ActionSeconds - CityPortForemanSnackTimeline.DiscardReleaseSeconds) /
                (CityPortForemanSnackTimeline.DiscardFlightEndSeconds - CityPortForemanSnackTimeline.DiscardReleaseSeconds))) : 0f;
        public bool CanTalk => Phase != CityPortForemanSnackPhase.Discard && Phase != CityPortForemanSnackPhase.Take;
    }

    /// <summary>Finite carrot custody on the same pause-aware clock as speech.
    /// Only observed clip boundaries consume a bite, release a stem or take a new carrot.</summary>
    public sealed class CityPortForemanSnackTimeline
    {
        public const double IdleGapSeconds = 3d;
        public const double BiteDurationSeconds = 4d;
        public const double BiteCommitSeconds = 1.65d;
        public const double DiscardDurationSeconds = 2.4d;
        public const double DiscardReleaseSeconds = 1d;
        public const double DiscardFlightEndSeconds = 1.65d;
        public const double TakeDurationSeconds = 2.4d;
        public const double TakePickupSeconds = 1.2d;
        public const double MaximumContinuousStepSeconds = 2d;
        private CityPortForemanSnackPhase phase;
        private double actionSeconds, previousLife;
        private int bites;
        private bool holding = true, initialized, wasConversational;
        private long cycle;

        public CityPortForemanSnackSnapshot Current => new CityPortForemanSnackSnapshot(phase, actionSeconds, bites, holding, cycle);

        public CityPortForemanSnackSnapshot Advance(double lifeSeconds, bool conversational)
        {
            if (double.IsNaN(lifeSeconds) || double.IsInfinity(lifeSeconds) || lifeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(lifeSeconds));
            double step = lifeSeconds - previousLife;
            previousLife = lifeSeconds;
            if (!initialized || step < 0d || step > MaximumContinuousStepSeconds)
            {
                // A seek cannot eat skipped bites, replay a throw or create a
                // second pocket carrot. Preserve the actual custody reached.
                initialized = true;
                SetPhase(CityPortForemanSnackPhase.Idle);
                wasConversational = conversational;
                return Current;
            }

            bool bitePhase = phase >= CityPortForemanSnackPhase.Bite1 && phase <= CityPortForemanSnackPhase.Bite3;
            if (conversational && Current.CanTalk)
            {
                // Before contact, retry this same bite later. After contact,
                // its committed count already selects the next bite instead.
                if (bitePhase || !wasConversational) SetPhase(CityPortForemanSnackPhase.Idle);
                wasConversational = true;
                return Current;
            }
            wasConversational = conversational;
            if (step <= 0d) return Current;
            actionSeconds += step;

            if (phase == CityPortForemanSnackPhase.Idle)
            {
                if (actionSeconds >= IdleGapSeconds && !conversational)
                    SetPhase(!holding ? CityPortForemanSnackPhase.Take : bites == 3
                        ? CityPortForemanSnackPhase.Discard : (CityPortForemanSnackPhase)((int)CityPortForemanSnackPhase.Bite1 + bites));
            }
            else if (bitePhase)
            {
                int ordinal = (int)phase - (int)CityPortForemanSnackPhase.Bite1 + 1;
                if (actionSeconds >= BiteCommitSeconds && bites < ordinal) bites = ordinal;
                if (actionSeconds >= BiteDurationSeconds)
                    SetPhase(bites == 3 && !conversational ? CityPortForemanSnackPhase.Discard : CityPortForemanSnackPhase.Idle);
            }
            else if (phase == CityPortForemanSnackPhase.Discard)
            {
                if (actionSeconds >= DiscardReleaseSeconds) holding = false;
                if (actionSeconds >= DiscardDurationSeconds)
                    SetPhase(conversational ? CityPortForemanSnackPhase.Idle : CityPortForemanSnackPhase.Take);
            }
            else if (phase == CityPortForemanSnackPhase.Take)
            {
                if (actionSeconds >= TakePickupSeconds && !holding)
                {
                    holding = true;
                    bites = 0;
                    cycle++;
                }
                if (actionSeconds >= TakeDurationSeconds) SetPhase(CityPortForemanSnackPhase.Idle);
            }
            return Current;
        }

        private void SetPhase(CityPortForemanSnackPhase value)
        {
            phase = value;
            actionSeconds = 0d;
        }
    }
}
