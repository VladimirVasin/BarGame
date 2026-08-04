using System;
using UnityEngine;

namespace BarPromenade
{
    public static class Player3DResources
    {
        public const string PrefabResourcePath = "Player/Player3D";

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public static bool TryInstantiate(
            Transform parent,
            out Player3DAssetRegistry registry)
        {
            GameObject prefab = LoadPrefab();
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
                return true;
            }

            UnityEngine.Object.Destroy(instance);
            return false;
        }

        public static Player3DAssetRegistry Instantiate(Transform parent)
        {
            if (TryInstantiate(parent, out Player3DAssetRegistry registry))
            {
                return registry;
            }

            throw new InvalidOperationException(
                $"Player 3D prefab is missing or invalid at Resources/" +
                $"{PrefabResourcePath}.");
        }
    }
}
