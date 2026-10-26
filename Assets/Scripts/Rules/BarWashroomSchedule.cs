using System;

namespace BarPromenade
{
    /// <summary>Bounded, visit-local WC cadence measured in active simulation seconds.</summary>
    public sealed class BarWashroomSchedule
    {
        public const float FirstVisitMinimumSeconds = 120f;
        public const float FirstVisitMaximumSeconds = 240f;
        public const float GlobalGapMinimumSeconds = 240f;
        public const float GlobalGapMaximumSeconds = 420f;
        public const float PersonalGapMinimumSeconds = 480f;
        public const float PersonalGapMaximumSeconds = 720f;
        public const float ResidenceMinimumSeconds = 60f;
        public const float ResidenceMaximumSeconds = 120f;
        public const float VisitMinimumSeconds = 25f;
        public const float VisitMaximumSeconds = 45f;
        private readonly bool[] residents;
        private readonly double[] arrivedAt;
        private readonly double[] readyAt;
        private readonly byte[] sips;
        private uint random;
        private double seconds;
        private double globalReadyAt;
        private bool isOpen;

        public BarWashroomSchedule(int seed, int capacity = BarBusinessHours.MaximumGuests)
        {
            if (capacity < 1 || capacity > BarBusinessHours.MaximumGuests)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            residents = new bool[capacity];
            arrivedAt = new double[capacity];
            readyAt = new double[capacity];
            sips = new byte[capacity];
            random = unchecked((uint)seed ^ 0xB5297A4Du) | 1u;
            globalReadyAt = Range(FirstVisitMinimumSeconds, FirstVisitMaximumSeconds);
        }

        public double ActiveSeconds => seconds;
        public float SecondsUntilNextVisit => (float)Math.Max(0d, globalReadyAt - seconds);
        public int ActiveSlot { get; private set; } = -1;
        public float VisitDurationSeconds { get; private set; }

        public void Advance(float activeSeconds, bool businessOpen)
        {
            if (float.IsNaN(activeSeconds) || float.IsInfinity(activeSeconds) || activeSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(activeSeconds));
            isOpen = businessOpen;
            if (isOpen) seconds += activeSeconds;
        }

        public void SetResident(int slot, bool present)
        {
            Validate(slot);
            if (residents[slot] == present) return;
            if (!present && ActiveSlot == slot)
                throw new InvalidOperationException("An active WC visitor must return before its resident slot can be removed.");
            residents[slot] = present;
            sips[slot] = 0;
            arrivedAt[slot] = seconds;
            readyAt[slot] = present
                ? seconds + Range(ResidenceMinimumSeconds, ResidenceMaximumSeconds) : 0d;
        }

        public void RecordSip(int slot)
        {
            Validate(slot);
            if (residents[slot] && sips[slot] < byte.MaxValue) sips[slot]++;
        }

        public bool CanVisit(int slot)
        {
            Validate(slot);
            return isOpen && ActiveSlot < 0 && residents[slot] && seconds >= globalReadyAt && seconds >= readyAt[slot];
        }

        public int ChooseCandidate()
        {
            int total = 0;
            for (int slot = 0; slot < residents.Length; slot++)
                if (CanVisit(slot)) total += Weight(slot);
            if (total == 0) return -1;
            int choice = (int)(NextRandom() % (uint)total);
            for (int slot = 0; slot < residents.Length; slot++)
                if (CanVisit(slot) && (choice -= Weight(slot)) < 0) return slot;
            throw new InvalidOperationException("WC candidate weights lost their resident.");
        }

        public bool BeginVisit(int slot)
        {
            if (!CanVisit(slot)) return false;
            ActiveSlot = slot;
            VisitDurationSeconds = Range(VisitMinimumSeconds, VisitMaximumSeconds);
            return true;
        }

        public void CompleteVisit(int slot)
        {
            Validate(slot);
            if (ActiveSlot != slot) throw new InvalidOperationException("Only the current visitor can complete its WC visit.");
            ActiveSlot = -1;
            globalReadyAt = seconds + Range(GlobalGapMinimumSeconds, GlobalGapMaximumSeconds);
            readyAt[slot] = seconds + Range(PersonalGapMinimumSeconds, PersonalGapMaximumSeconds);
            sips[slot] = 0;
        }

        public void Defer(int slot)
        {
            Validate(slot);
            if (ActiveSlot >= 0 && ActiveSlot != slot)
                throw new InvalidOperationException("A deferred guest cannot cancel another WC visitor.");
            ActiveSlot = -1;
            readyAt[slot] = Math.Max(readyAt[slot], seconds + Range(60f, 120f));
            globalReadyAt = Math.Max(globalReadyAt, seconds + Range(30f, 60f));
        }

        private int Weight(int slot) => 1 + Math.Min(5, (int)sips[slot]) +
            Math.Min(4, (int)Math.Min(int.MaxValue, (seconds - arrivedAt[slot]) / 120d));

        private void Validate(int slot)
        {
            if (slot < 0 || slot >= residents.Length) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private float Range(float min, float max) => min + (NextRandom() & 0xFFFFFFu) / 16777215f * (max - min);
        private uint NextRandom()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }
    }
}
