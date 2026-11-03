using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded visual stock. Only the seven source offers own transactions and selection.</summary>
    public static class SupermarketStockDisplay
    {
        public const int MaximumCopyCount = 200;
        public const string RootPrefix = "Passive Stock ";

        public static void Build(Transform shelfRoot, SupermarketShelfPlan shelf,
            SupermarketProductSlotPlan offer, Transform source, IReadOnlyList<Renderer> renderers)
        {
            List<Vector3> positions = Positions(shelf, offer);
            var stock = new GameObject(RootPrefix + offer.ItemId).transform;
            stock.SetParent(shelfRoot, false);
            var properties = new MaterialPropertyBlock();
            for (int copy = 0; copy < positions.Count; copy++)
            {
                Vector3 position = positions[copy];
                // Leave the real selectable offer's silhouette free at its authored anchor.
                Vector3 delta = position - offer.LocalPosition;
                if (Mathf.Abs(delta.y) < .05f && Mathf.Abs(delta.x) < .19f &&
                    Mathf.Abs(delta.z) < (offer.ItemId == InventoryItemId.DayOldLoaf ? .32f : .20f)) continue;
                var item = new GameObject("Stock " + copy).transform;
                item.SetParent(stock, false);
                item.localPosition = position;
                item.localRotation = offer.LocalRotation;
                for (int part = 0; part < renderers.Count; part++)
                {
                    Renderer original = renderers[part];
                    if (original == null || !original.enabled || !original.gameObject.activeInHierarchy ||
                        !original.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                    // Imported root scale remains in the matrix. Copy no liquid controller,
                    // registry, collider, selection view, audio or receipt behaviour.
                    Matrix4x4 matrix = source.worldToLocalMatrix * original.transform.localToWorldMatrix;
                    var holder = new GameObject(original.name);
                    holder.transform.SetParent(item, false);
                    holder.transform.localPosition = matrix.GetColumn(3);
                    holder.transform.localRotation = matrix.rotation;
                    holder.transform.localScale = matrix.lossyScale;
                    holder.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = original.sharedMaterials;
                    renderer.shadowCastingMode = original.shadowCastingMode;
                    renderer.receiveShadows = original.receiveShadows;
                    renderer.lightProbeUsage = original.lightProbeUsage;
                    renderer.reflectionProbeUsage = original.reflectionProbeUsage;
                    properties.Clear(); original.GetPropertyBlock(properties);
                    renderer.SetPropertyBlock(properties);
                }
            }
        }

        public static List<Vector3> Positions(SupermarketShelfPlan shelf, SupermarketProductSlotPlan offer)
        {
            var result = new List<Vector3>(32);
            float front = Mathf.Sign(shelf.FacingDirection.x) * .30f;
            float rear = Mathf.Sign(shelf.FacingDirection.x) * .14f;
            void Row(float y, int count, float start, float spacing, bool secondRow)
            {
                for (int i = 0; i < count; i++)
                {
                    result.Add(new Vector3(front, y, start + spacing * i));
                    if (secondRow) result.Add(new Vector3(rear, y, start + spacing * i));
                }
            }
            switch (offer.ItemId)
            {
                case InventoryItemId.InstantNoodles:
                    Row(SupermarketInteriorLayoutPlanner.GondolaFirstTierTop, 10, -2.18f, .46f, true);
                    Row(SupermarketInteriorLayoutPlanner.GondolaThirdTierTop, 5, -2.18f, .46f, true);
                    break;
                case InventoryItemId.DayOldLoaf:
                    Row(SupermarketInteriorLayoutPlanner.GondolaSecondTierTop, 8, -2.1f, .58f, false);
                    Row(SupermarketInteriorLayoutPlanner.GondolaThirdTierTop, 4, .3f, .58f, false);
                    break;
                case InventoryItemId.VodkaBottle:
                case InventoryItemId.WineBottle:
                    Row(offer.LocalPosition.y, 14, -2.02f, .30f, true);
                    break;
                case InventoryItemId.ClosedStewCan:
                    Row(offer.LocalPosition.y, 14, -2.02f, .30f, true);
                    break;
                case InventoryItemId.ChickenEgg:
                case InventoryItemId.BeerCan:
                    for (int row = 0; row < 2; row++)
                        for (int column = 0; column < 14; column++)
                            result.Add(new Vector3(-2.75f + column * .42f, offer.LocalPosition.y, -.12f + row * .17f));
                    // A modest upper row preserves the cabinet's dark recess.
                    if (offer.ItemId == InventoryItemId.BeerCan)
                        for (int column = 0; column < 4; column++)
                            result.Add(new Vector3(-1.1f + column * .42f,
                                SupermarketInteriorLayoutPlanner.ColdThirdTierTop, -.12f));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(offer));
            }
            if (result.Count > 32) throw new InvalidOperationException("A supermarket stock group exceeded its fixed copy budget.");
            return result;
        }
    }
}
