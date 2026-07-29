using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public readonly struct BarDrinkOffer
    {
        internal BarDrinkOffer(
            DrinkId drinkId,
            string nameKey,
            int price)
        {
            DrinkId = drinkId;
            NameKey = nameKey;
            Price = price;
        }

        public DrinkId DrinkId { get; }
        public string NameKey { get; }
        public int Price { get; }
    }

    public static class BarDrinkCatalog
    {
        private static readonly BarDrinkOffer[] offers =
        {
            new BarDrinkOffer(
                DrinkId.Water,
                "drink.water",
                2),
            new BarDrinkOffer(
                DrinkId.LightBeer,
                "drink.light_beer",
                8),
            new BarDrinkOffer(
                DrinkId.DarkBeer,
                "drink.dark_beer",
                10),
            new BarDrinkOffer(
                DrinkId.WhiteWine,
                "drink.white_wine",
                12),
            new BarDrinkOffer(
                DrinkId.RedWine,
                "drink.red_wine",
                14),
            new BarDrinkOffer(
                DrinkId.Vodka,
                "drink.vodka",
                15),
            new BarDrinkOffer(
                DrinkId.PepperVodka,
                "drink.pepper_vodka",
                18),
            new BarDrinkOffer(
                DrinkId.CognacVs,
                "drink.cognac_vs",
                20),
            new BarDrinkOffer(
                DrinkId.CognacVsop,
                "drink.cognac_vsop",
                25)
        };

        private static readonly IReadOnlyList<BarDrinkOffer> offersView =
            Array.AsReadOnly(offers);

        public static IReadOnlyList<BarDrinkOffer> Offers => offersView;

        public static bool TryGetOffer(
            DrinkId drinkId,
            out BarDrinkOffer offer)
        {
            for (int index = 0; index < offers.Length; index++)
            {
                if (offers[index].DrinkId == drinkId)
                {
                    offer = offers[index];
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
