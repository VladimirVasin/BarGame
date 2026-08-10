using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityPedestrianCollision
    {
        public const string LayerName = "CityPedestrian";
        public const int LayerIndex = 8;
        public const int DefaultLayerIndex = 0;

        public static int NonPedestrianMask => ~(1 << LayerIndex);

        public static void EnsureRuntimePolicy()
        {
            int configuredLayer = LayerMask.NameToLayer(LayerName);
            if (configuredLayer != LayerIndex)
            {
                throw new InvalidOperationException(
                    $"The {LayerName} layer must occupy layer {LayerIndex}.");
            }

            Physics.IgnoreLayerCollision(
                DefaultLayerIndex,
                LayerIndex,
                false);
            Physics.IgnoreLayerCollision(
                LayerIndex,
                LayerIndex,
                true);
        }
    }

    public static class CityPedestrianFactory
    {
        public const string RuntimeRootName =
            "City Pedestrian Runtime";

        public static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            RoadWalkableArea walkableArea,
            Camera camera)
        {
            return Create(
                parent,
                plan,
                player,
                walkableArea,
                camera,
                CityPedestrianResources.LoadPrefab());
        }

        public static CityPedestrianDirector Create(
            Transform parent,
            CityPedestrianPlan plan,
            Transform player,
            IWalkableArea walkableArea,
            Camera camera,
            GameObject presentationPrefab)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            if (walkableArea == null)
            {
                throw new ArgumentNullException(nameof(walkableArea));
            }

            int slotCount = Mathf.Min(
                plan.Count,
                CityPedestrianDirector.MaximumActiveModels);
            if (slotCount > 0 && presentationPrefab == null)
            {
                throw new InvalidOperationException(
                    "The city pedestrian presentation prefab is missing at " +
                    $"Resources/{CityPedestrianResources.PrefabResourcePath}.");
            }

            CityPedestrianCollision.EnsureRuntimePolicy();

            GameObject runtimeRoot = new GameObject(RuntimeRootName);
            runtimeRoot.transform.SetParent(parent, false);
            try
            {
                GameObject routesRoot = new GameObject("Routes");
                routesRoot.transform.SetParent(
                    runtimeRoot.transform,
                    false);
                GameObject modelPoolRoot = new GameObject("Model Pool");
                modelPoolRoot.transform.SetParent(
                    runtimeRoot.transform,
                    false);

                var actors = new List<CityPedestrianActor>(slotCount);
                for (int index = 0; index < slotCount; index++)
                {
                    GameObject actorObject = new GameObject(
                        $"Pedestrian Slot {index + 1:00}");
                    actorObject.layer =
                        CityPedestrianCollision.LayerIndex;
                    actorObject.transform.SetParent(
                        routesRoot.transform,
                        false);
                    CityPedestrianActor actor =
                        actorObject.AddComponent<CityPedestrianActor>();
                    actor.Initialize(
                        walkableArea,
                        plan.AgentRadius);
                    actors.Add(actor);
                }

                var presentations =
                    new List<CityPedestrianPresentation>(slotCount);
                for (int index = 0; index < slotCount; index++)
                {
                    if (!CityPedestrianResources.TryInstantiate(
                            presentationPrefab,
                            modelPoolRoot.transform,
                            out CityPedestrianAssetRegistry registry))
                    {
                        throw new InvalidOperationException(
                            "The city pedestrian presentation prefab has no " +
                            "CityPedestrianAssetRegistry on its root.");
                    }

                    ValidatePassivePresentation(registry);
                    registry.gameObject.name =
                        $"Pedestrian Model {index + 1:00}";
                    CityPedestrianPresentation presentation =
                        registry.GetComponent<
                            CityPedestrianPresentation>();
                    if (presentation == null)
                    {
                        presentation = registry.gameObject.AddComponent<
                            CityPedestrianPresentation>();
                    }

                    presentation.Initialize(registry);
                    presentation.gameObject.SetActive(false);
                    presentations.Add(presentation);
                }

                CityPedestrianDirector director =
                    runtimeRoot.AddComponent<CityPedestrianDirector>();
                director.Initialize(
                    plan,
                    actors,
                    presentations,
                    player,
                    modelPoolRoot.transform,
                    camera);
                return director;
            }
            catch
            {
                CityPedestrianResources.DestroyObject(runtimeRoot);
                throw;
            }
        }

        private static void ValidatePassivePresentation(
            CityPedestrianAssetRegistry registry)
        {
            if (registry.GetComponentsInChildren<Collider>(true).Length > 0 ||
                registry.GetComponentsInChildren<Collider2D>(true).Length > 0 ||
                registry.GetComponentsInChildren<Rigidbody>(true).Length > 0 ||
                registry.GetComponentsInChildren<Rigidbody2D>(true).Length > 0)
            {
                throw new InvalidOperationException(
                    "City pedestrian presentations must remain collider-free " +
                    "and must not own rigidbodies.");
            }

            MonoBehaviour[] behaviours =
                registry.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IInteractable)
                {
                    throw new InvalidOperationException(
                        "City pedestrian presentations must not be interactive.");
                }
            }
        }
    }
}
