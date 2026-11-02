using System;

namespace BarPromenade
{
    public static partial class GameSessionState
    {
        public static int VodkaBottleId => inventory.VodkaBottleId;
        public static int VodkaBottleRemainingMl => inventory.VodkaBottleRemainingMl;
        public static int VodkaBottlePreviewRemainingMl => inventory.VodkaBottlePreviewRemainingMl;
        public static bool CanDrinkVodkaSip => inventory.IsEquipped(InventoryItemId.VodkaBottle) &&
            VodkaBottleId != 0 && VodkaBottleRemainingMl > 0 &&
            IntoxicationLevel < IntoxicationStageRules.MaximumLevel;

        public static bool TryDrinkVodkaSip(int expectedBottleId, out VodkaBottleSipResult result) =>
            TryDrinkVodkaSip(expectedBottleId, VodkaBottleRemainingMl, out result);

        /// <summary>The prepared bottle and fill jointly identify one swallow, rejecting stale or repeated commits.</summary>
        public static bool TryDrinkVodkaSip(int expectedBottleId, int expectedRemainingMl, out VodkaBottleSipResult result)
        {
            result = default;
            if (!CanDrinkVodkaSip || VodkaBottleRemainingMl != expectedRemainingMl ||
                !inventory.TryDrinkVodkaSip(expectedBottleId, out result)) return false;
            CommitAcceptedDrinkingProgress(
                Math.Min(IntoxicationStageRules.MaximumLevel, IntoxicationLevel + result.AlcoholDose),
                DrinkId.Vodka, (int)Math.Min(int.MaxValue, (long)DrinksConsumed + result.Servings),
                result.StressRelief, result.AlcoholDose);
            if (result.Emptied) InventoryEquipmentChanged?.Invoke();
            GameLog.Info("inventory", "vodka_sip_committed",
                GameLog.Field("bottle_id", result.BottleId),
                GameLog.Field("remaining_ml", result.RemainingMl));
            return true;
        }
    }
}
