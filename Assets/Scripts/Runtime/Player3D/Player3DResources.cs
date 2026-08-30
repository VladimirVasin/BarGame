using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Selects a packaged player presentation explicitly. Ordinary gameplay
    /// uses <see cref="ProductionV2"/>; Hero V1 remains packaged as an
    /// explicit fallback and is never deleted by a production promotion.
    /// </summary>
    public enum Player3DVariant
    {
        ProductionV1 = 0,
        ProductionV2 = 1
    }

    public static class Player3DResources
    {
        public const string V1PrefabResourcePath = "Player/Player3D";
        public const string V2PrefabResourcePath = "Player/Player3DV2";
        public const string PrefabResourcePath = V2PrefabResourcePath;

        public static GameObject LoadPrefab()
        {
            return LoadPrefab(Player3DVariant.ProductionV2);
        }

        public static GameObject LoadPrefab(Player3DVariant variant)
        {
            return Resources.Load<GameObject>(GetPrefabResourcePath(variant));
        }

        public static bool TryInstantiate(
            Transform parent,
            out Player3DAssetRegistry registry)
        {
            return TryInstantiate(
                parent,
                Player3DVariant.ProductionV2,
                out registry);
        }

        public static bool TryInstantiate(
            Transform parent,
            Player3DVariant variant,
            out Player3DAssetRegistry registry)
        {
            GameObject prefab = LoadPrefab(variant);
            if (prefab == null)
            {
                registry = null;
                return false;
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                parent,
                false);
            registry = instance.GetComponent<Player3DAssetRegistry>();
            if (registry != null)
            {
                // MaterialPropertyBlocks are runtime state and are not stored
                // in the prefab. Apply the packaged palette explicitly after
                // instantiation so direct Resources users receive Neutral V2
                // face atlas state as reliably as PlayerFactory users.
                registry.ApplyPalette();
                return true;
            }

            UnityEngine.Object.Destroy(instance);
            return false;
        }

        public static Player3DAssetRegistry Instantiate(Transform parent)
        {
            return Instantiate(parent, Player3DVariant.ProductionV2);
        }

        public static Player3DAssetRegistry Instantiate(
            Transform parent,
            Player3DVariant variant)
        {
            if (TryInstantiate(parent, variant, out Player3DAssetRegistry registry))
            {
                return registry;
            }

            string resourcePath = GetPrefabResourcePath(variant);
            throw new InvalidOperationException(
                $"Player 3D prefab is missing or invalid at Resources/" +
                $"{resourcePath}.");
        }

        public static string GetPrefabResourcePath(Player3DVariant variant)
        {
            switch (variant)
            {
                case Player3DVariant.ProductionV1:
                    return V1PrefabResourcePath;
                case Player3DVariant.ProductionV2:
                    return PrefabResourcePath;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(variant),
                        variant,
                        "Unknown player 3D variant.");
            }
        }
    }
}
