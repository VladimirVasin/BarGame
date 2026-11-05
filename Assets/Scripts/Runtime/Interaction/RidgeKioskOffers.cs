using UnityEngine;

namespace BarPromenade
{
    /// <summary>The same three physical samples stand on the counter in both views of the ridge.</summary>
    public static class RidgeKioskOffers
    {
        public static void Build(Transform kiosk, Transform beerAnchor, Transform vodkaAnchor,
            Transform foodAnchor, Transform approachAnchor, bool interactive)
        {
            BuildOffer(kiosk, beerAnchor, approachAnchor, InventoryItemId.BeerCan, "beer", interactive);
            BuildOffer(kiosk, vodkaAnchor, approachAnchor, InventoryItemId.VodkaBottle, "vodka", interactive);
            BuildOffer(kiosk, foodAnchor, approachAnchor, InventoryItemId.PotatoPirozhok, "pirozhok", interactive);
        }

        private static void BuildOffer(Transform kiosk, Transform anchor, Transform approach,
            InventoryItemId item, string key, bool interactive)
        {
            // FBX anchors can carry the importer's unit scale. The product and trigger use metres.
            var host = new GameObject("Kiosk offer " + item).transform;
            host.SetParent(kiosk, true);
            host.SetPositionAndRotation(anchor.position, kiosk.rotation);
            Transform display = InventoryItemModelFactory.BuildWorldModel(item, host, Vector3.one * .4f);
            // Counter samples cannot move. The view already set their full fill/closed lid;
            // gravity-plane updates are needed only on movable models, such as the receipt.
            if (display.TryGetComponent(out VodkaBottleView bottle)) bottle.enabled = false;
            if (!interactive) return;
            float height = DrinkContainerCatalog.IsDrink(item) ? DrinkContainerCatalog.Get(item).Height : .06f;
            var focus = new GameObject("Product centre").transform;
            focus.SetParent(host, false);
            focus.localPosition = Vector3.up * (height * .5f);
            BoxCollider trigger = host.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = focus.localPosition;
            trigger.size = new Vector3(.3f, Mathf.Max(.25f, height), .3f);
            var target = RidgeKioskTarget.Attach(kiosk, host, focus, approach);
            host.gameObject.AddComponent<RidgeKioskPurchase>().Initialize(item, key, target, focus);
        }
    }
}
