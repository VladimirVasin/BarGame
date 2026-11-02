using System;

namespace BarPromenade
{
    public readonly struct VodkaBottleSipResult
    {
        public VodkaBottleSipResult(int bottleId, int beforeMl, int remainingMl,
            int alcoholDose, int stressRelief, int servings)
        {
            BottleId = bottleId; BeforeMl = beforeMl; RemainingMl = remainingMl;
            AlcoholDose = alcoholDose; StressRelief = stressRelief; Servings = servings;
        }

        public int BottleId { get; }
        public int BeforeMl { get; }
        public int RemainingMl { get; }
        public bool Emptied => BottleId != 0 && RemainingMl == 0;
        public int AlcoholDose { get; }
        public int StressRelief { get; }
        public int Servings { get; }
    }

    /// <summary>Finite bottle identities and exact cumulative dose accounting; no scene or presentation state.</summary>
    public sealed class VodkaBottleState
    {
        public const int Capacity = 9;
        public const int FullMl = 500;
        public const int FullBottleSipCount = 15;
        public const int RefrigeratorMl = 100;
        private readonly int[] ids = new int[Capacity];
        private readonly int[] remaining = new int[Capacity];
        private readonly int[] sipPhases = new int[Capacity];
        private int nextId, activeId, alcoholRemainder, stressRemainder, servingRemainder;
        public int Count { get; private set; }
        public int ActiveId => activeId;
        public int ActiveRemainingMl => GetRemainingMl(activeId);
        public int PreviewRemainingMl => Count == 0 ? 0 : remaining[SelectionIndex()];

        public int GetRemainingMl(int id)
        {
            int index = Find(id);
            return index >= 0 ? remaining[index] : 0;
        }

        public void Clear()
        {
            Array.Clear(ids, 0, ids.Length); Array.Clear(remaining, 0, remaining.Length);
            Array.Clear(sipPhases, 0, sipPhases.Length);
            Count = 0; activeId = 0;
            alcoholRemainder = stressRemainder = servingRemainder = 0;
            // IDs are not reused on an inventory reset: stale prepared swallows remain invalid.
        }

        public bool TryAdd(int count, int initialMl = FullMl)
        {
            if (count <= 0 || count > Capacity - Count || initialMl <= 0 || initialMl > FullMl) return false;
            for (int i = 0; i < count; i++)
            {
                nextId = nextId == int.MaxValue ? 1 : nextId + 1;
                ids[Count] = nextId; remaining[Count] = initialMl; sipPhases[Count++] = 0;
            }
            return true;
        }

        public bool SelectBottle()
        {
            if (Find(activeId) >= 0) return true;
            if (Count == 0) return false;
            activeId = ids[SelectionIndex()];
            return true;
        }

        public bool TryRemove(int count)
        {
            if (count <= 0 || count > Count) return false;
            for (int i = 0; i < count; i++)
            {
                int index = Count - 1;
                if (ids[index] == activeId && Count > 1) index--;
                RemoveAt(index);
            }
            return true;
        }

        public bool TrySip(int expectedBottleId, int fullAlcoholDose, int fullStressRelief, int fullServings,
            out VodkaBottleSipResult result)
        {
            result = default;
            int index = Find(expectedBottleId);
            if (expectedBottleId == 0 || expectedBottleId != activeId || index < 0 ||
                fullAlcoholDose < 0 || fullStressRelief < 0 || fullServings < 0) return false;
            int before = remaining[index];
            // Consecutive integer boundaries give 33, 33, 34 ml: fifteen sips
            // finish 500 ml exactly, and a 100 ml source finishes in three.
            // Keep each bottle's phase through stow/switch rather than restarting its rounding.
            int phase = sipPhases[index];
            int nominalVolume = (phase + 1) * FullMl / FullBottleSipCount - phase * FullMl / FullBottleSipCount;
            int volume = Math.Min(nominalVolume, before);
            int after = before - volume;
            int alcohol = TakeDose(ref alcoholRemainder, volume, fullAlcoholDose);
            int stress = TakeDose(ref stressRemainder, volume, fullStressRelief);
            int servings = TakeDose(ref servingRemainder, volume, fullServings);
            remaining[index] = after;
            sipPhases[index] = phase + 1;
            result = new VodkaBottleSipResult(expectedBottleId, before, after, alcohol, stress, servings);
            if (after == 0) RemoveAt(index);
            return true;
        }

        private static int TakeDose(ref int remainder, int volume, int fullDose)
        {
            long numerator = remainder + (long)volume * fullDose;
            remainder = (int)(numerator % FullMl);
            return (int)(numerator / FullMl);
        }

        private int Find(int id)
        {
            if (id == 0) return -1;
            for (int i = 0; i < Count; i++) if (ids[i] == id) return i;
            return -1;
        }

        private int SelectionIndex()
        {
            int active = Find(activeId);
            if (active >= 0) return active;
            for (int i = 0; i < Count; i++) if (remaining[i] < FullMl) return i;
            return 0;
        }

        private void RemoveAt(int index)
        {
            if (ids[index] == activeId) activeId = 0;
            for (int i = index; i < Count - 1; i++)
            { ids[i] = ids[i + 1]; remaining[i] = remaining[i + 1]; sipPhases[i] = sipPhases[i + 1]; }
            Count--; ids[Count] = 0; remaining[Count] = 0; sipPhases[Count] = 0;
        }
    }
}
