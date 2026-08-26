using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class ChurchResources
    {
        public const string ExteriorPrefabResourcePath =
            ChurchAssetRegistry.ExteriorPrefabResourcePath;
        public const string InteriorPrefabResourcePath =
            ChurchAssetRegistry.InteriorPrefabResourcePath;

        public static GameObject LoadPrefab(ChurchAssetKind kind)
        {
            return Resources.Load<GameObject>(ResourcePath(kind));
        }

        public static GameObject LoadExteriorPrefab()
        {
            return LoadPrefab(ChurchAssetKind.Exterior);
        }

        public static GameObject LoadInteriorPrefab()
        {
            return LoadPrefab(ChurchAssetKind.Interior);
        }

        public static bool TryInstantiate(
            ChurchAssetKind kind,
            Transform parent,
            out ChurchAssetRegistry registry)
        {
            GameObject prefab = LoadPrefab(kind);
            if (prefab == null)
            {
                registry = null;
                return false;
            }

            GameObject instance = Object.Instantiate(prefab, parent, false);
            registry = instance.GetComponent<ChurchAssetRegistry>();
            if (registry != null && registry.Kind == kind)
            {
                return true;
            }

            DestroyObject(instance);
            registry = null;
            return false;
        }

        public static ChurchAssetRegistry Instantiate(
            ChurchAssetKind kind,
            Transform parent)
        {
            if (TryInstantiate(kind, parent, out ChurchAssetRegistry registry))
            {
                return registry;
            }

            throw new InvalidOperationException(
                $"Church {kind} prefab is missing or invalid at " +
                $"Resources/{ResourcePath(kind)}.");
        }

        private static string ResourcePath(ChurchAssetKind kind)
        {
            switch (kind)
            {
                case ChurchAssetKind.Exterior:
                    return ExteriorPrefabResourcePath;
                case ChurchAssetKind.Interior:
                    return InteriorPrefabResourcePath;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown Church asset kind.");
            }
        }

        private static void DestroyObject(GameObject gameObject)
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
