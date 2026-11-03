using System;

namespace BarPromenade
{
    public static partial class GameSessionState
    {
        private static object hawthornUseOwner;
        private static MeleeCombatant hawthornUseRecipient;
        private static int hawthornUseBottleId;
        private static bool hawthornUseStarted;

        public static bool IsHawthornUseOwned(object owner) => owner != null && ReferenceEquals(hawthornUseOwner, owner) &&
            hawthornUseBottleId != 0 && hawthornUseBottleId == DrinkContainerId(InventoryItemId.HawthornTincture) &&
            DrinkRemainingMl(InventoryItemId.HawthornTincture) > 0;

        public static bool TryQueueHawthornUse(object owner, MeleeCombatant recipient)
        {
            if (owner == null || hawthornUseOwner != null || recipient == null || !recipient.CanHeal ||
                !inventory.SelectHawthornForUse()) return false;
            hawthornUseOwner = owner;
            hawthornUseRecipient = recipient;
            hawthornUseBottleId = DrinkContainerId(InventoryItemId.HawthornTincture);
            hawthornUseStarted = false;
            return true;
        }

        public static bool StartHawthornUse(object owner, MeleeCombatant recipient)
        {
            if (!IsHawthornUseOwned(owner) || hawthornUseStarted || !ReferenceEquals(hawthornUseRecipient, recipient) || !recipient.CanHeal) return false;
            hawthornUseStarted = true;
            inventory.ClearHandEquipment();
            InventoryEquipmentChanged?.Invoke();
            return true;
        }

        public static void CancelHawthornUse(object owner)
        {
            if (ReferenceEquals(hawthornUseOwner, owner)) ClearHawthornUse();
        }

        private static void ClearHawthornUse()
        { hawthornUseOwner = null; hawthornUseRecipient = null; hawthornUseBottleId = 0; hawthornUseStarted = false; }

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
        public static bool CanDrinkContainerSip(InventoryItemId item, MeleeCombatant recipient = null, object useOwner = null) =>
            item != InventoryItemId.None && DrinkContainerId(item) != 0 && DrinkRemainingMl(item) > 0 &&
            (item == InventoryItemId.HawthornTincture
                ? IsHawthornUseOwned(useOwner) && hawthornUseStarted && ReferenceEquals(hawthornUseRecipient, recipient) && recipient.CanHeal
                : EquippedDrinkItem == item && IntoxicationLevel < IntoxicationStageRules.MaximumLevel);

        /// <summary>The item, container and fill identify one swallow, rejecting switched or repeated commits.</summary>
        public static bool TryDrinkVodkaSip(int expectedBottleId, int expectedRemainingMl, out VodkaBottleSipResult result) =>
            TryDrinkContainerSip(InventoryItemId.VodkaBottle, expectedBottleId, expectedRemainingMl, out result);

        public static bool TryDrinkContainerSip(InventoryItemId item, int expectedId, int expectedMl, out VodkaBottleSipResult result,
            MeleeCombatant recipient = null, object useOwner = null)
        {
            result = default;
            if (!CanDrinkContainerSip(item, recipient, useOwner) || DrinkRemainingMl(item) != expectedMl ||
                !inventory.TryDrinkContainerSip(item, expectedId, out result,
                    ownedMedicineUse: item == InventoryItemId.HawthornTincture)) return false;
            // No callbacks run between the inventory debit and the health receipt.
            // Ordinary drinks retain their existing contract; medicine needs a live wounded recipient.
            if (item == InventoryItemId.HawthornTincture)
            {
                ClearHawthornUse();
                recipient.Heal(InventoryConsumableCatalog.HawthornHealthRestored);
            }
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
