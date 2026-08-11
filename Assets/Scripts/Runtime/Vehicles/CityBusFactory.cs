using System;
using UnityEngine;

namespace BarPromenade
{
    public static class CityBusCollision
    {
        public const string LayerName = "CityBus";
        public const int LayerIndex = 9;
        public const int DefaultLayerIndex = 0;

        public static int NonBusMask => ~(1 << LayerIndex);

        public static void EnsureRuntimePolicy()
        {
            int configuredLayer = LayerMask.NameToLayer(LayerName);
            if (configuredLayer != LayerIndex)
            {
                throw new InvalidOperationException(
                    $"The {LayerName} layer must occupy layer " +
                    $"{LayerIndex}.");
            }

            Physics.IgnoreLayerCollision(
                DefaultLayerIndex,
                LayerIndex,
                false);
            Physics.IgnoreLayerCollision(
                CityPedestrianCollision.LayerIndex,
                LayerIndex,
                false);
            Physics.IgnoreLayerCollision(
                LayerIndex,
                LayerIndex,
                true);
        }
    }

    public static class CityBusFactory
    {
        public const string RuntimeRootName = "City Bus Runtime";

        public static CityBusDirector Create(
            Transform parent,
            CityBusPlan plan,
            Transform player,
            CityPedestrianDirector pedestrians,
            Func<float> nightFactorProvider = null)
        {
            return Create(
                parent,
                plan,
                player,
                pedestrians,
                CityBusResources.LoadPrefab(),
                nightFactorProvider);
        }

        public static CityBusDirector Create(
            Transform parent,
            CityBusPlan plan,
            Transform player,
            CityPedestrianDirector pedestrians,
            GameObject presentationPrefab,
            Func<float> nightFactorProvider = null)
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

            if (!plan.IsEmpty && presentationPrefab == null)
            {
                throw new InvalidOperationException(
                    "The city bus presentation prefab is missing at " +
                    $"Resources/{CityBusResources.PrefabResourcePath}.");
            }

            CityBusCollision.EnsureRuntimePolicy();
            GameObject runtimeRoot = new GameObject(RuntimeRootName);
            runtimeRoot.transform.SetParent(parent, false);
            try
            {
                Transform routeRoot = new GameObject("Route").transform;
                routeRoot.SetParent(runtimeRoot.transform, false);
                Transform modelPoolRoot =
                    new GameObject("Model Pool").transform;
                modelPoolRoot.SetParent(runtimeRoot.transform, false);

                CityBusActor actor = null;
                CityBusPresentation presentation = null;
                if (!plan.IsEmpty)
                {
                    if (!CityBusResources.TryInstantiate(
                            presentationPrefab,
                            modelPoolRoot,
                            out CityBusAssetRegistry registry))
                    {
                        throw new InvalidOperationException(
                            "The city bus prefab must own a " +
                            "CityBusAssetRegistry on its root.");
                    }

                    ValidatePassivePresentation(registry);
                    ValidateRegistry(registry, plan.Vehicle);
                    registry.gameObject.name = "City Bus Model 01";
                    presentation = registry.GetComponent<
                        CityBusPresentation>();
                    if (presentation == null)
                    {
                        presentation = registry.gameObject.AddComponent<
                            CityBusPresentation>();
                    }

                    presentation.Initialize(registry);
                    presentation.gameObject.SetActive(false);

                    GameObject actorObject =
                        new GameObject("City Bus Slot 01");
                    actorObject.layer = CityBusCollision.LayerIndex;
                    actorObject.transform.SetParent(routeRoot, false);
                    actor = actorObject.AddComponent<CityBusActor>();
                    actor.Initialize(
                        registry.LocalBounds,
                        registry.Dimensions);
                }

                CityBusDirector director =
                    runtimeRoot.AddComponent<CityBusDirector>();
                director.Initialize(
                    plan,
                    actor,
                    presentation,
                    player,
                    pedestrians,
                    modelPoolRoot,
                    nightFactorProvider);
                return director;
            }
            catch
            {
                CityBusResources.DestroyObject(runtimeRoot);
                throw;
            }
        }

        private static void ValidatePassivePresentation(
            CityBusAssetRegistry registry)
        {
            if (registry.GetComponentsInChildren<Collider>(true).Length > 0 ||
                registry.GetComponentsInChildren<Collider2D>(true).Length >
                0 ||
                registry.GetComponentsInChildren<Rigidbody>(true).Length >
                0 ||
                registry.GetComponentsInChildren<Rigidbody2D>(true).Length >
                0)
            {
                throw new InvalidOperationException(
                    "City bus presentation prefabs must remain " +
                    "collider-free and must not own rigidbodies.");
            }

            MonoBehaviour[] behaviours =
                registry.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < behaviours.Length; index++)
            {
                if (behaviours[index] is IInteractable)
                {
                    throw new InvalidOperationException(
                        "City bus presentations must not be interactive.");
                }
            }
        }

        private static void ValidateRegistry(
            CityBusAssetRegistry registry,
            CityBusDesignVehicle vehicle)
        {
            if (registry.ModelRoot == null ||
                registry.Body == null ||
                registry.FrontDoorForwardLeaf == null ||
                registry.FrontDoorRearwardLeaf == null ||
                registry.RearDoorForwardLeaf == null ||
                registry.RearDoorRearwardLeaf == null ||
                registry.FrontLeftWheel == null ||
                registry.FrontRightWheel == null ||
                registry.RearLeftWheel == null ||
                registry.RearRightWheel == null ||
                registry.FrontLeftSteeringPivot == null ||
                registry.FrontRightSteeringPivot == null)
            {
                throw new InvalidOperationException(
                    "The city bus registry is missing a required runtime " +
                    "articulation binding.");
            }

            CityBusDimensions dimensions = registry.Dimensions;
            if (!Approximately(dimensions.Length, vehicle.BodyLength) ||
                !Approximately(dimensions.Width, vehicle.BodyWidth) ||
                !Approximately(dimensions.Height, vehicle.BodyHeight) ||
                !Approximately(dimensions.Wheelbase, vehicle.Wheelbase) ||
                dimensions.WheelRadius <= 0f)
            {
                throw new InvalidOperationException(
                    "The city bus prefab dimensions do not match the " +
                    "planned design vehicle.");
            }

            Vector3 boundsSize = registry.LocalBounds.size;
            if (boundsSize.x <= 0f ||
                boundsSize.y <= 0f ||
                boundsSize.z <= 0f)
            {
                throw new InvalidOperationException(
                    "The city bus registry requires non-empty local bounds.");
            }
        }

        private static bool Approximately(float first, float second)
        {
            return Mathf.Abs(first - second) <= 0.05f;
        }
    }
}
