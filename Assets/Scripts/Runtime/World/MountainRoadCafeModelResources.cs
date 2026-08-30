using System;
using UnityEngine;

namespace BarPromenade
{
    public static class MountainRoadCafeModelResources
    {
        public const string PrefabResourcePath =
            MountainRoadCafeAssetRegistry.PrefabResourcePath;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        /// <summary>
        /// Instantiates the authored local +X/+Z frame directly on the cafe
        /// plan. The helper does not create gameplay colliders or lights.
        /// </summary>
        public static MountainRoadCafeAssetRegistry Instantiate(
            Transform parent,
            MountainRoadCafePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            GameObject prefab = LoadPrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Missing authored Mountain Road cafe prefab " +
                    $"'{PrefabResourcePath}'.");
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "Authored Mountain Road Cafe";
            instance.transform.SetPositionAndRotation(
                new Vector3(plan.Center.x, plan.FloorY, plan.Center.z),
                Quaternion.LookRotation(plan.Forward, Vector3.up));
            instance.transform.SetParent(parent, true);

            MountainRoadCafeAssetRegistry registry =
                instance.GetComponent<MountainRoadCafeAssetRegistry>();
            if (registry == null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(instance);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
                throw new InvalidOperationException(
                    "The authored Mountain Road cafe prefab has no " +
                    "MountainRoadCafeAssetRegistry.");
            }

            registry.ApplyAppearance();
            return registry;
        }
    }
}
