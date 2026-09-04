using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public readonly struct BarDrinkOffer
    {
        internal BarDrinkOffer(
            DrinkId drinkId,
            string nameKey,
            string descriptionKey,
            int price)
        {
            DrinkId = drinkId;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Price = price;
        }

        public DrinkId DrinkId { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
        public int Price { get; }
    }

    public static class BarDrinkCatalog
    {
        // The complete lookup remains stable for saves and existing systems
        // that may still carry an older DrinkId. Only menuOffers is exposed
        // by the bar UI and instantiated on its service shelf.
        private static readonly BarDrinkOffer[] purchaseOffers =
        {
            new BarDrinkOffer(
                DrinkId.Water,
                "drink.water",
                string.Empty,
                2),
            new BarDrinkOffer(
                DrinkId.LightBeer,
                "drink.light_beer",
                "drink.light_beer.description",
                8),
            new BarDrinkOffer(
                DrinkId.DarkBeer,
                "drink.dark_beer",
                string.Empty,
                10),
            new BarDrinkOffer(
                DrinkId.WhiteWine,
                "drink.white_wine",
                string.Empty,
                12),
            new BarDrinkOffer(
                DrinkId.RedWine,
                "drink.red_wine",
                "drink.red_wine.description",
                14),
            new BarDrinkOffer(
                DrinkId.Vodka,
                "drink.vodka",
                "drink.vodka.description",
                15),
            new BarDrinkOffer(
                DrinkId.PepperVodka,
                "drink.pepper_vodka",
                string.Empty,
                18),
            new BarDrinkOffer(
                DrinkId.CognacVs,
                "drink.cognac_vs",
                "drink.cognac_vs.description",
                20),
            new BarDrinkOffer(
                DrinkId.CognacVsop,
                "drink.cognac_vsop",
                string.Empty,
                25)
        };

        private static readonly BarDrinkOffer[] menuOffers =
        {
            purchaseOffers[1], // beer
            purchaseOffers[4], // wine
            purchaseOffers[7], // unaged distillate
            purchaseOffers[5]  // vodka
        };

        private static readonly IReadOnlyList<BarDrinkOffer> menuOffersView =
            Array.AsReadOnly(menuOffers);

        /// <summary>
        /// The exact ordered set printed in the physical bar menu.
        /// </summary>
        public static IReadOnlyList<BarDrinkOffer> Offers => menuOffersView;

        public static bool TryGetOffer(
            DrinkId drinkId,
            out BarDrinkOffer offer)
        {
            for (int index = 0; index < purchaseOffers.Length; index++)
            {
                if (purchaseOffers[index].DrinkId == drinkId)
                {
                    offer = purchaseOffers[index];
                    return true;
                }
            }

            offer = default;
            return false;
        }

        public static BarDrinkOffer GetOffer(DrinkId drinkId)
        {
            if (TryGetOffer(drinkId, out BarDrinkOffer offer))
            {
                return offer;
            }

            throw new ArgumentOutOfRangeException(
                nameof(drinkId),
                drinkId,
                null);
        }
    }
}
