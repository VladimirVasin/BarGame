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
        OpenStewCan = 5
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
                "home.refrigerator.item.vodka.name",
                "home.refrigerator.item.vodka.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.ChickenEgg,
                InventoryItemCategory.Consumable,
                "home.refrigerator.item.egg.name",
                "home.refrigerator.item.egg.description",
                9),
            new InventoryItemDefinition(
                InventoryItemId.OpenStewCan,
                InventoryItemCategory.Consumable,
                "home.refrigerator.item.stew_can.name",
                "home.refrigerator.item.stew_can.description",
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
