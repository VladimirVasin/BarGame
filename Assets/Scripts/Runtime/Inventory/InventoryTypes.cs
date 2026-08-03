using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum InventoryItemId
    {
        None = 0,
        ApartmentKeys = 1,
        Lighter = 2,
        VodkaBottle = 3,
        ChickenEgg = 4,
        OpenStewCan = 5,
        ClosedStewCan = 6,
        InstantNoodles = 7,
        DayOldLoaf = 8
    }

    public enum InventoryItemCategory
    {
        KeyItem = 0,
        Tool = 1,
        Consumable = 2
    }

    public readonly struct InventoryItemDefinition
    {
        internal InventoryItemDefinition(
            InventoryItemId id,
            InventoryItemCategory category,
            string nameLocalizationKey,
            string descriptionLocalizationKey,
            int maximumStack)
        {
            Id = id;
            Category = category;
            NameLocalizationKey = nameLocalizationKey;
            DescriptionLocalizationKey = descriptionLocalizationKey;
            MaximumStack = maximumStack;
        }

        public InventoryItemId Id { get; }
        public InventoryItemCategory Category { get; }
        public string NameLocalizationKey { get; }
        public string DescriptionLocalizationKey { get; }
        public int MaximumStack { get; }
    }

    public readonly struct InventoryItemStack
    {
        internal InventoryItemStack(InventoryItemId itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }

        public InventoryItemId ItemId { get; }
        public int Count { get; }
    }

    public static class InventoryItemCatalog
    {
        private static readonly InventoryItemDefinition[] Definitions =
        {
            new InventoryItemDefinition(
                InventoryItemId.ApartmentKeys,
                InventoryItemCategory.KeyItem,
                "inventory.item.apartment_keys.name",
                "inventory.item.apartment_keys.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.Lighter,
                InventoryItemCategory.Tool,
                "inventory.item.lighter.name",
                "inventory.item.lighter.description",
                1),
            new InventoryItemDefinition(
                InventoryItemId.VodkaBottle,
                InventoryItemCategory.Consumable,
                "inventory.item.vodka_bottle.name",
                "inventory.item.vodka_bottle.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.ChickenEgg,
                InventoryItemCategory.Consumable,
                "inventory.item.chicken_egg.name",
                "inventory.item.chicken_egg.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.OpenStewCan,
                InventoryItemCategory.Consumable,
                "inventory.item.open_stew_can.name",
                "inventory.item.open_stew_can.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.ClosedStewCan,
                InventoryItemCategory.Consumable,
                "inventory.item.closed_stew_can.name",
                "inventory.item.closed_stew_can.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.InstantNoodles,
                InventoryItemCategory.Consumable,
                "inventory.item.instant_noodles.name",
                "inventory.item.instant_noodles.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.DayOldLoaf,
                InventoryItemCategory.Consumable,
                "inventory.item.day_old_loaf.name",
                "inventory.item.day_old_loaf.description",
                9)
        };

        private static readonly IReadOnlyList<InventoryItemDefinition>
            DefinitionsView = Array.AsReadOnly(Definitions);

        public static IReadOnlyList<InventoryItemDefinition> All =>
            DefinitionsView;

        public static bool TryGet(
            InventoryItemId id,
            out InventoryItemDefinition definition)
        {
            for (int index = 0; index < Definitions.Length; index++)
            {
                if (Definitions[index].Id == id)
                {
                    definition = Definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static InventoryItemDefinition Get(InventoryItemId id)
        {
            if (TryGet(id, out InventoryItemDefinition definition))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "The item is not present in the inventory catalog.");
        }
    }
}
