using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class CityBusDriverResources
    {
        public const string PrefabResourcePath =
            "Vehicles/CityBusDriver3D";

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public static bool TryInstantiate(
            Transform parent,
            out CityBusDriverAssetRegistry registry)
        {
            return TryInstantiate(LoadPrefab(), parent, out registry);
        }

        public static bool TryInstantiate(
            GameObject prefab,
            Transform parent,
            out CityBusDriverAssetRegistry registry)
        {
            if (prefab == null)
            {
                registry = null;
                return false;
            }

            GameObject instance = Object.Instantiate(prefab, parent, false);
            registry = instance.GetComponent<CityBusDriverAssetRegistry>();
            if (registry != null)
            {
                registry.ApplyBaseColors();
                return true;
            }

            DestroyObject(instance);
            return false;
        }

        public static CityBusDriverAssetRegistry Instantiate(Transform parent)
        {
            if (TryInstantiate(
                    parent,
                    out CityBusDriverAssetRegistry registry))
            {
                return registry;
            }

            throw new InvalidOperationException(
                "The City bus driver prefab is missing or invalid at " +
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
