using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class CityPedestrianResources
    {
        public const string PrefabResourcePath =
            "Pedestrians/CityPedestrian3D";

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public static bool TryInstantiate(
            Transform parent,
            out CityPedestrianAssetRegistry registry)
        {
            return TryInstantiate(
                LoadPrefab(),
                parent,
                out registry);
        }

        public static bool TryInstantiate(
            GameObject prefab,
            Transform parent,
            out CityPedestrianAssetRegistry registry)
        {
            if (prefab == null)
            {
                registry = null;
                return false;
            }

            GameObject instance = Object.Instantiate(
                prefab,
                parent,
                false);
            registry = instance.GetComponent<
                CityPedestrianAssetRegistry>();
            if (registry != null)
            {
                return true;
            }

            DestroyObject(instance);
            return false;
        }

        public static CityPedestrianAssetRegistry Instantiate(
            Transform parent)
        {
            if (TryInstantiate(
                    parent,
                    out CityPedestrianAssetRegistry registry))
            {
                return registry;
            }

            throw new InvalidOperationException(
                "The city pedestrian prefab is missing or invalid at " +
                $"Resources/{PrefabResourcePath}.");
        }

        internal static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            CityPedestrianPresentation[] presentations =
                gameObject.GetComponentsInChildren<
                    CityPedestrianPresentation>(true);
            for (int index = 0; index < presentations.Length; index++)
            {
                presentations[index].Shutdown();
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
