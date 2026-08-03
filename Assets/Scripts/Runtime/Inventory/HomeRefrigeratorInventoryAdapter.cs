using System;

namespace BarPromenade
{
    public static class HomeRefrigeratorInventoryAdapter
    {
        private const string SourcePrefix = "home.refrigerator.";

        public static bool TryGetInventoryItem(
            HomeRefrigeratorItemKind kind,
            out InventoryItemId itemId)
        {
            switch (kind)
            {
                case HomeRefrigeratorItemKind.VodkaBottle:
                    itemId = InventoryItemId.VodkaBottle;
                    return true;
                case HomeRefrigeratorItemKind.ChickenEgg:
                    itemId = InventoryItemId.ChickenEgg;
                    return true;
                case HomeRefrigeratorItemKind.OpenStewCan:
                    itemId = InventoryItemId.OpenStewCan;
                    return true;
                default:
                    itemId = InventoryItemId.None;
                    return false;
            }
        }

        public static string GetSourceId(string slotId)
        {
            if (string.IsNullOrWhiteSpace(slotId))
            {
                throw new ArgumentException(
                    "A refrigerator inventory source requires a slot ID.",
                    nameof(slotId));
            }

            return SourcePrefix + slotId.Trim();
        }
    }
}
