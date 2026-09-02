using System;
using UnityEngine;

namespace BarPromenade
{
    public static class SupermarketProductModelResources
    {
        private const string ResourceFolder =
            "Supermarket/Products/";

        public static bool IsAuthoredProduct(InventoryItemId itemId)
        {
            switch (itemId)
            {
                case InventoryItemId.InstantNoodles:
                case InventoryItemId.DayOldLoaf:
                case InventoryItemId.VodkaBottle:
                case InventoryItemId.ClosedStewCan:
                case InventoryItemId.OpenStewCan:
                case InventoryItemId.ChickenEgg:
                    return true;
                default:
                    return false;
            }
        }

        public static string GetResourcePath(InventoryItemId itemId)
        {
            switch (itemId)
            {
                case InventoryItemId.InstantNoodles:
                    return ResourceFolder + "InstantNoodles3D";
                case InventoryItemId.DayOldLoaf:
                    return ResourceFolder + "DayOldLoaf3D";
                case InventoryItemId.VodkaBottle:
                    return ResourceFolder + "VodkaBottle3D";
                case InventoryItemId.ClosedStewCan:
                    return ResourceFolder + "ClosedStewCan3D";
                case InventoryItemId.OpenStewCan:
                    return ResourceFolder + "OpenStewCan3D";
                case InventoryItemId.ChickenEgg:
                    return ResourceFolder + "ChickenEgg3D";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(itemId),
                        itemId,
                        "The item has no authored supermarket model.");
            }
        }

        public static GameObject LoadPrefab(InventoryItemId itemId)
        {
            return Resources.Load<GameObject>(GetResourcePath(itemId));
        }

        public static Transform Instantiate(
            InventoryItemId itemId,
            Transform parent,
            Vector3 availableSize,
            string rootPrefix)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!IsPositiveFinite(availableSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(availableSize),
                    "Available model size must be positive and finite.");
            }

            GameObject prefab = LoadPrefab(itemId);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing authored supermarket product prefab " +
                    $"'{GetResourcePath(itemId)}'.");
            }

            GameObject wrapper = new GameObject(
                $"{rootPrefix} {GetDisplaySuffix(itemId)}");
            wrapper.transform.SetParent(parent, false);
            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                wrapper.transform,
                false);
            instance.name = $"Authored {itemId} Visual";
            SupermarketProductAssetRegistry registry =
                instance.GetComponent<SupermarketProductAssetRegistry>();
            if (registry == null)
            {
                Destroy(wrapper);
                throw new InvalidOperationException(
                    $"Authored supermarket product '{itemId}' has no registry.");
            }

            try
            {
                registry.ValidateOrThrow();
            }
            catch
            {
                Destroy(wrapper);
                throw;
            }
            if (registry.ItemId != itemId)
            {
                Destroy(wrapper);
                throw new InvalidOperationException(
                    $"Authored supermarket product '{itemId}' loaded the " +
                    $"wrong registry '{registry.ItemId}'.");
            }

            Bounds bounds = registry.LocalBounds;
            float scale = Mathf.Min(
                1f,
                Mathf.Min(
                    availableSize.x / bounds.size.x,
                    Mathf.Min(
                        availableSize.y / bounds.size.y,
                        availableSize.z / bounds.size.z)));
            if (!IsPositiveFinite(scale))
            {
                Destroy(wrapper);
                throw new InvalidOperationException(
                    $"Authored supermarket product '{itemId}' cannot be fitted.");
            }

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;
            registry.ApplyAppearance();
            return wrapper.transform;
        }

        private static string GetDisplaySuffix(InventoryItemId itemId)
        {
            switch (itemId)
            {
                case InventoryItemId.InstantNoodles:
                    return "Instant Noodles";
                case InventoryItemId.DayOldLoaf:
                    return "Day Old Loaf";
                case InventoryItemId.VodkaBottle:
                    return "Vodka Bottle";
                case InventoryItemId.ClosedStewCan:
                    return "Closed Stew Can";
                case InventoryItemId.OpenStewCan:
                    return "Open Stew Can";
                case InventoryItemId.ChickenEgg:
                    return "Chicken Egg";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(itemId),
                        itemId,
                        "The item has no authored supermarket display name.");
            }
        }

        private static void Destroy(GameObject instance)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static bool IsPositiveFinite(Vector3 value)
        {
            return IsPositiveFinite(value.x) &&
                IsPositiveFinite(value.y) &&
                IsPositiveFinite(value.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
