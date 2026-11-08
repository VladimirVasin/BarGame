using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Each kiosk keeps its three replenishable samples and its own stable till sources.</summary>
    public static class RidgeKioskOffers
    {
        public static IReadOnlyList<RidgeKioskPurchase> Build(Transform kiosk, Transform beerAnchor, Transform vodkaAnchor,
            Transform foodAnchor, Transform approachAnchor, bool interactive, string siteStableId = "ridge-kiosk")
        {
            if (string.IsNullOrWhiteSpace(siteStableId))
                throw new ArgumentException("A kiosk needs its permanent site ID.", nameof(siteStableId));
            string sourcePrefix = siteStableId == "ridge-kiosk" ? "city.ridge.kiosk" : "city.kiosk." + siteStableId;
            var offers = new List<RidgeKioskPurchase>(3);
            Add(foodAnchor, InventoryItemId.PotatoPirozhok, "pirozhok");
            Add(beerAnchor, InventoryItemId.BeerCan, "beer");
            Add(vodkaAnchor, InventoryItemId.VodkaBottle, "vodka");
            return offers.AsReadOnly();
            void Add(Transform anchor, InventoryItemId item, string key)
            {
                var offer = BuildOffer(kiosk, anchor, item, key, interactive, sourcePrefix);
                if (offer) offers.Add(offer);
            }
        }

        private static RidgeKioskPurchase BuildOffer(Transform kiosk, Transform anchor,
            InventoryItemId item, string key, bool interactive, string sourcePrefix)
        {
            // FBX anchors can carry the importer's unit scale. The product and trigger use metres.
            var host = new GameObject("Kiosk offer " + item).transform;
            host.SetParent(kiosk, true);
            host.SetPositionAndRotation(anchor.position, kiosk.rotation);
            Transform display = InventoryItemModelFactory.BuildWorldModel(item, host, Vector3.one * .4f);
            // Counter samples cannot move. The view already set their full fill/closed lid;
            // gravity-plane updates are needed only on movable models, such as the receipt.
            if (display.TryGetComponent(out VodkaBottleView bottle)) bottle.enabled = false;
            if (!interactive) return null;
            float height = DrinkContainerCatalog.IsDrink(item) ? DrinkContainerCatalog.Get(item).Height : .06f;
            var focus = new GameObject("Product centre").transform;
            focus.SetParent(host, false);
            focus.localPosition = Vector3.up * (height * .5f);
            var purchase = host.gameObject.AddComponent<RidgeKioskPurchase>();
            purchase.Initialize(item, key, focus, display, sourcePrefix);
            return purchase;
        }
    }
}
