using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public readonly struct SupermarketProductOffer
    {
        internal SupermarketProductOffer(
            InventoryItemId itemId,
            string nameLocalizationKey,
            string descriptionLocalizationKey,
            int price)
        {
            ItemId = itemId;
            NameLocalizationKey = nameLocalizationKey;
            DescriptionLocalizationKey = descriptionLocalizationKey;
            Price = price;
        }

        public InventoryItemId ItemId { get; }
        public string NameLocalizationKey { get; }
        public string DescriptionLocalizationKey { get; }
        public int Price { get; }
    }

    public static class SupermarketProductCatalog
    {
        private static readonly SupermarketProductOffer[] offers =
        {
            new SupermarketProductOffer(
                InventoryItemId.ChickenEgg,
                "inventory.item.chicken_egg.name",
                "inventory.item.chicken_egg.description",
                2),
            new SupermarketProductOffer(
                InventoryItemId.VodkaBottle,
                "inventory.item.vodka_bottle.name",
                "inventory.item.vodka_bottle.description",
                28),
            new SupermarketProductOffer(
                InventoryItemId.ClosedStewCan,
                "inventory.item.closed_stew_can.name",
                "inventory.item.closed_stew_can.description",
                11),
            new SupermarketProductOffer(
                InventoryItemId.InstantNoodles,
                "inventory.item.instant_noodles.name",
                "inventory.item.instant_noodles.description",
                3),
            new SupermarketProductOffer(
                InventoryItemId.DayOldLoaf,
                "inventory.item.day_old_loaf.name",
                "inventory.item.day_old_loaf.description",
                4)
        };

        private static readonly IReadOnlyList<SupermarketProductOffer>
            offersView = Array.AsReadOnly(offers);

        public static IReadOnlyList<SupermarketProductOffer> Offers =>
            offersView;

        public static bool TryGetOffer(
            InventoryItemId itemId,
            out SupermarketProductOffer offer)
        {
            for (int index = 0; index < offers.Length; index++)
            {
                if (offers[index].ItemId == itemId)
                {
                    offer = offers[index];
                    return true;
                }
            }

            offer = default;
            return false;
        }

        public static SupermarketProductOffer GetOffer(
            InventoryItemId itemId)
        {
            if (TryGetOffer(itemId, out SupermarketProductOffer offer))
            {
                return offer;
            }

            throw new ArgumentOutOfRangeException(
                nameof(itemId),
                itemId,
                "The item is not offered by the supermarket.");
        }
    }
}
