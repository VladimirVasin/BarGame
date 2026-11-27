using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class PawnshopFindCatalog
    {
        internal readonly struct Offer
        {
            internal Offer(InventoryItemId item, int chance, int price, RetroSfxId sound)
            {
                Item = item;
                Chance = chance;
                Price = price;
                Sound = sound;
            }

            internal InventoryItemId Item { get; }
            internal int Chance { get; }
            internal int Price { get; }
            internal RetroSfxId Sound { get; }
        }

        internal static readonly Offer[] Offers =
        {
            new Offer(InventoryItemId.FoldingScrewdriver, 10, 5, RetroSfxId.BinFoundMetal),
            new Offer(InventoryItemId.Caliper, 8, 8, RetroSfxId.BinFoundMetal),
            new Offer(InventoryItemId.PocketRadio, 6, 14, RetroSfxId.BinPaperRustle),
            new Offer(InventoryItemId.MechanicalWatch, 4, 22, RetroSfxId.PawnshopWatchTick),
            new Offer(InventoryItemId.SilverRing, 2, 34, RetroSfxId.BinPaperRustle)
        };

        // The first 40 rolls are the old waste range. Its unused tail stays empty.
        internal static InventoryItemId Roll(int roll)
        {
            foreach (var offer in Offers)
            {
                if (roll < offer.Chance) return offer.Item;
                roll -= offer.Chance;
            }
            return InventoryItemId.None;
        }

        internal static bool TryGet(InventoryItemId item, out Offer offer)
        {
            foreach (var candidate in Offers)
                if (candidate.Item == item) { offer = candidate; return true; }
            offer = default;
            return false;
        }

        internal static Transform BuildModel(InventoryItemId item, Transform parent)
        {
            var asset = Resources.Load<GameObject>("Pawnshop/Finds/" + item);
            if (!asset) throw new InvalidOperationException("Missing authored pawnshop find " + item);
            var model = UnityEngine.Object.Instantiate(asset, parent, false).transform;
            if (item == InventoryItemId.MechanicalWatch)
                model.gameObject.AddComponent<PawnshopWatchHand>();
            return model;
        }

        internal static Transform FindAnchor(Transform model, string name)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException(model.name + " lacks " + name);
        }
    }
}
