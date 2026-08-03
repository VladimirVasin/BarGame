using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum InventoryConsumableKind
    {
        Food = 0,
        Alcohol = 1
    }

    public readonly struct InventoryConsumableDefinition
    {
        internal InventoryConsumableDefinition(
            InventoryItemId itemId,
            InventoryConsumableKind kind,
            int hungerRelief,
            int minimumHungerAfterUse,
            DrinkId drinkId,
            double servings)
        {
            ItemId = itemId;
            Kind = kind;
            HungerRelief = hungerRelief;
            MinimumHungerAfterUse = minimumHungerAfterUse;
            DrinkId = drinkId;
            Servings = servings;
        }

        public InventoryItemId ItemId { get; }
        public InventoryConsumableKind Kind { get; }
        public int HungerRelief { get; }
        public int MinimumHungerAfterUse { get; }
        public DrinkId DrinkId { get; }
        public double Servings { get; }
        public int StressRelief =>
            Kind == InventoryConsumableKind.Alcohol
                ? PlayerNeedsRules.ScaleRelief(
                    DrinkRules.GetStressRelief(DrinkId),
                    Servings)
                : 0;
    }

    public static class InventoryConsumableCatalog
    {
        private static readonly InventoryConsumableDefinition[] definitions =
        {
            CreateAlcohol(
                InventoryItemId.VodkaBottle,
                DrinkId.Vodka,
                4d),
            CreateFood(InventoryItemId.ChickenEgg, 10),
            CreateFood(InventoryItemId.OpenStewCan, 35),
            CreateFood(InventoryItemId.ClosedStewCan, 35),
            CreateFood(InventoryItemId.InstantNoodles, 22),
            CreateFood(InventoryItemId.DayOldLoaf, 18)
        };

        private static readonly IReadOnlyList<InventoryConsumableDefinition>
            definitionsView = Array.AsReadOnly(definitions);

        public static IReadOnlyList<InventoryConsumableDefinition> All =>
            definitionsView;

        public static bool TryGet(
            InventoryItemId itemId,
            out InventoryConsumableDefinition definition)
        {
            for (int index = 0; index < definitions.Length; index++)
            {
                if (definitions[index].ItemId == itemId)
                {
                    definition = definitions[index];
                    return true;
                }
            }

            definition = default;
            return false;
        }

        public static InventoryConsumableDefinition Get(
            InventoryItemId itemId)
        {
            if (TryGet(
                    itemId,
                    out InventoryConsumableDefinition definition))
            {
                return definition;
            }

            throw new ArgumentOutOfRangeException(
                nameof(itemId),
                itemId,
                "The item is not present in the consumable catalog.");
        }

        private static InventoryConsumableDefinition CreateFood(
            InventoryItemId itemId,
            int hungerRelief)
        {
            return new InventoryConsumableDefinition(
                itemId,
                InventoryConsumableKind.Food,
                hungerRelief,
                PlayerNeedsRules.PoorFoodMinimumHunger,
                DrinkId.None,
                0d);
        }

        private static InventoryConsumableDefinition CreateAlcohol(
            InventoryItemId itemId,
            DrinkId drinkId,
            double servings)
        {
            return new InventoryConsumableDefinition(
                itemId,
                InventoryConsumableKind.Alcohol,
                0,
                PlayerNeedsRules.MinimumLevel,
                drinkId,
                servings);
        }
    }
}
