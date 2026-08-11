using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class CityBusResources
    {
        public const string PrefabResourcePath = "Vehicles/CityBus3D";

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public static bool TryInstantiate(
            Transform parent,
            out CityBusAssetRegistry registry)
        {
            return TryInstantiate(LoadPrefab(), parent, out registry);
        }

        public static bool TryInstantiate(
            GameObject prefab,
            Transform parent,
            out CityBusAssetRegistry registry)
        {
            if (prefab == null)
            {
                registry = null;
                return false;
            }

            GameObject instance = Object.Instantiate(prefab, parent, false);
            registry = instance.GetComponent<CityBusAssetRegistry>();
            if (registry != null)
            {
                registry.ResetArticulation();
                return true;
            }

            DestroyObject(instance);
            return false;
        }

        public static CityBusAssetRegistry Instantiate(Transform parent)
        {
            if (TryInstantiate(parent, out CityBusAssetRegistry registry))
            {
                return registry;
            }

            throw new InvalidOperationException(
                "The City bus prefab is missing or invalid at " +
                $"Resources/{PrefabResourcePath}.");
        }

        public static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
            }
            else
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
