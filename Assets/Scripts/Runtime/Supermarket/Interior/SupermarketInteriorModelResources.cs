using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class SupermarketInteriorModelResources
    {
        public const string PrefabResourcePath =
            SupermarketInteriorAssetRegistry.PrefabResourcePath;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        /// <summary>
        /// Instantiates only the passive authored dressing. Gameplay geometry,
        /// lights, stock, interactions and moving actors remain runtime-owned.
        /// </summary>
        public static SupermarketInteriorAssetRegistry Instantiate(
            Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            GameObject prefab = LoadPrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Missing authored supermarket interior prefab at " +
                    $"Resources/{PrefabResourcePath}.");
            }

            GameObject instance = Object.Instantiate(prefab, parent, false);
            instance.name = "Authored Supermarket Interior";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            SupermarketInteriorAssetRegistry registry =
                instance.GetComponent<SupermarketInteriorAssetRegistry>();
            if (registry == null)
            {
                DestroyObject(instance);
                throw new InvalidOperationException(
                    "The authored supermarket interior prefab has no " +
                    "SupermarketInteriorAssetRegistry.");
            }

            registry.ApplySurfaceAppearance();
            return registry;
        }

        private static void DestroyObject(GameObject gameObject)
        {
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
