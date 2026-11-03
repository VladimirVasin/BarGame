using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Completed household changes within one game session. Carrying is
    /// transient: an undelivered basket still belongs to its original stand.
    /// </summary>
    public sealed class VillageHouseholdProgress
    {
        public const int BasketCount = 2;
        public const int LogsPerBasket = 3;
        public const int LogCount = BasketCount * LogsPerBasket;
        public const int ClearingStageCount = 3;
        public const string PorchClearingId = "village-house-11-threshold";
        public const string ChapelClearingId = "village-chapel-threshold";
        public const string StationEdgeClearingId = "village-station-edge";

        private readonly int[] logBaskets = new int[LogCount];
        private readonly int[] deliveryStands = new int[BasketCount];
        private readonly Dictionary<string, int> clearingStages = new Dictionary<string, int>(StringComparer.Ordinal);

        public VillageHouseholdProgress() => Reset();

        public bool ChairFixed { get; private set; }
        public int CompletedWaterVisits { get; private set; }
        public bool WaterFetched => CompletedWaterVisits > 0;
        public int LooseLogCount
        {
            get
            {
                int count = 0;
                foreach (int basket in logBaskets) if (basket < 0) count++;
                return count;
            }
        }
        public int DeliveredBasketCount
        {
            get
            {
                int count = 0;
                foreach (int stand in deliveryStands) if (stand >= 0) count++;
                return count;
            }
        }

        /// <summary>-1 is the loose stock; otherwise the one owning basket.</summary>
        public int GetLogBasket(int logId)
        {
            ValidateIndex(logId, LogCount, nameof(logId));
            return logBaskets[logId];
        }

        public int GetBasketLogCount(int basketId)
        {
            ValidateIndex(basketId, BasketCount, nameof(basketId));
            int count = 0;
            foreach (int basket in logBaskets) if (basket == basketId) count++;
            return count;
        }

        /// <summary>-1 retains the original pickup support, including during transit.</summary>
        public int GetBasketDeliveryStand(int basketId)
        {
            ValidateIndex(basketId, BasketCount, nameof(basketId));
            return deliveryStands[basketId];
        }

        public int GetBasketAtStand(int standId)
        {
            ValidateIndex(standId, BasketCount, nameof(standId));
            for (int basket = 0; basket < BasketCount; basket++)
                if (deliveryStands[basket] == standId) return basket;
            return -1;
        }

        public bool TryLoadLog(int basketId, int logId)
        {
            if (!ValidIndex(basketId, BasketCount) || !ValidIndex(logId, LogCount) ||
                logBaskets[logId] >= 0 || deliveryStands[basketId] >= 0 ||
                GetBasketLogCount(basketId) >= LogsPerBasket) return false;
            logBaskets[logId] = basketId;
            return true;
        }

        public bool TryDeliverBasket(int basketId, int standId)
        {
            if (!ValidIndex(basketId, BasketCount) || !ValidIndex(standId, BasketCount) ||
                deliveryStands[basketId] >= 0 || GetBasketAtStand(standId) >= 0 ||
                GetBasketLogCount(basketId) != LogsPerBasket) return false;
            deliveryStands[basketId] = standId;
            return true;
        }

        public bool TryCompleteChairRepair()
        {
            if (ChairFixed) return false;
            ChairFixed = true;
            return true;
        }

        public int GetClearingStage(string stableId)
        {
            ValidateId(stableId);
            return clearingStages.TryGetValue(stableId, out int stage) ? stage : 0;
        }

        public bool TryAdvanceClearing(string stableId, int expectedStage)
        {
            ValidateId(stableId);
            if (expectedStage < 0 || expectedStage >= ClearingStageCount ||
                GetClearingStage(stableId) != expectedStage) return false;
            clearingStages[stableId] = expectedStage + 1;
            return true;
        }

        public bool TryCompleteWaterFetch(int expectedVisitCount)
        {
            if (expectedVisitCount < 0 || expectedVisitCount != CompletedWaterVisits ||
                CompletedWaterVisits == int.MaxValue) return false;
            CompletedWaterVisits++;
            return true;
        }

        public sealed class Checkpoint
        {
            internal readonly int[] Logs, Stands;
            internal readonly KeyValuePair<string, int>[] Clearings;
            internal readonly bool ChairFixed;
            internal readonly int WaterVisits;
            internal Checkpoint(VillageHouseholdProgress state)
            {
                Logs = (int[])state.logBaskets.Clone(); Stands = (int[])state.deliveryStands.Clone();
                Clearings = new KeyValuePair<string, int>[state.clearingStages.Count];
                ((ICollection<KeyValuePair<string, int>>)state.clearingStages).CopyTo(Clearings, 0);
                ChairFixed = state.ChairFixed; WaterVisits = state.CompletedWaterVisits;
            }
        }
        public Checkpoint CaptureCheckpoint() => new Checkpoint(this);
        public void RestoreCheckpoint(Checkpoint saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            saved.Logs.CopyTo(logBaskets, 0); saved.Stands.CopyTo(deliveryStands, 0);
            clearingStages.Clear(); foreach (var pair in saved.Clearings) clearingStages.Add(pair.Key, pair.Value);
            ChairFixed = saved.ChairFixed; CompletedWaterVisits = saved.WaterVisits;
        }

        public void Reset()
        {
            for (int log = 0; log < LogCount; log++) logBaskets[log] = -1;
            for (int basket = 0; basket < BasketCount; basket++) deliveryStands[basket] = -1;
            clearingStages.Clear();
            ChairFixed = false;
            CompletedWaterVisits = 0;
        }

        private static bool ValidIndex(int value, int count) => value >= 0 && value < count;
        private static void ValidateIndex(int value, int count, string name)
        {
            if (!ValidIndex(value, count)) throw new ArgumentOutOfRangeException(name);
        }
        private static void ValidateId(string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId)) throw new ArgumentException("A household place needs a stable id.", nameof(stableId));
        }
    }
}
