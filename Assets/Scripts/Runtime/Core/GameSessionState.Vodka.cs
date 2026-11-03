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

        public static InventoryItemId EquippedDrinkItem => inventory.EquippedDrinkItem;
        public static int DrinkContainerId(InventoryItemId item) => inventory.DrinkContainerId(item);
        public static int DrinkRemainingMl(InventoryItemId item) => inventory.DrinkRemainingMl(item);
        public static int DrinkPreviewRemainingMl(InventoryItemId item) => inventory.DrinkPreviewRemainingMl(item);
        public static bool DrinkOpened(InventoryItemId item) => inventory.DrinkOpened(item);
        public static bool DrinkPreviewOpened(InventoryItemId item) => inventory.DrinkPreviewOpened(item);
        public static bool SetDrinkOpened(InventoryItemId item, int id, bool opened) => inventory.SetDrinkOpened(item, id, opened);
        public static bool CanDrinkContainerSip(InventoryItemId item) => item != InventoryItemId.None &&
            EquippedDrinkItem == item && DrinkContainerId(item) != 0 && DrinkRemainingMl(item) > 0 &&
            IntoxicationLevel < IntoxicationStageRules.MaximumLevel;

        /// <summary>The item, container and fill identify one swallow, rejecting switched or repeated commits.</summary>
        public static bool TryDrinkVodkaSip(int expectedBottleId, int expectedRemainingMl, out VodkaBottleSipResult result) =>
            TryDrinkContainerSip(InventoryItemId.VodkaBottle, expectedBottleId, expectedRemainingMl, out result);

        public static bool TryDrinkContainerSip(InventoryItemId item, int expectedId, int expectedMl, out VodkaBottleSipResult result)
        {
            result = default;
            if (!CanDrinkContainerSip(item) || DrinkRemainingMl(item) != expectedMl ||
                !inventory.TryDrinkContainerSip(item, expectedId, out result)) return false;
            CommitAcceptedDrinkingProgress(
                Math.Min(IntoxicationStageRules.MaximumLevel, IntoxicationLevel + result.AlcoholDose),
                InventoryConsumableCatalog.Get(item).DrinkId, (int)Math.Min(int.MaxValue, (long)DrinksConsumed + result.Servings),
                result.StressRelief, result.AlcoholDose);
            if (result.Emptied) InventoryEquipmentChanged?.Invoke();
            GameLog.Info("inventory", "drink_container_sip_committed",
                GameLog.Field("item", item.ToString()), GameLog.Field("bottle_id", result.BottleId),
                GameLog.Field("remaining_ml", result.RemainingMl));
            return true;
        }
    }
}
