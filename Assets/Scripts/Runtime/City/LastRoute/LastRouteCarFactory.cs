using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Stands the Ferryman's car beside the last route island.
    ///
    /// The art prefab is validated pure presentation - no collider, no
    /// Animator, no light - so the obstacle box is added here, on the
    /// runtime root, sized from the registry's own bounds. That is the bus's
    /// arrangement: the vehicle art stays a rendering asset and the physics
    /// belongs to whatever spawned it. Unlike the bus there is no rigidbody
    /// and no actor, because this car never moves again.
    /// </summary>
    public static class LastRouteCarFactory
    {
        public const string RuntimeRootName = "Last Route Car";
        // Sized against the street masts' own halos: smaller, because a
        // headlight is a smaller lamp, but paired and low so the two read
        // as a car rather than as one more lamp post.
        private const float HeadlightHaloInnerSize = 0.55f;
        private const float HeadlightHaloOuterSize = 2.10f;

        public static LastRouteCarAssetRegistry Create(
            Transform parent,
            LastRouteCarPlan plan)
        {
            return Create(parent, plan, default, null);
        }

        /// <summary>
        /// The same car, plus the passenger seat the hero can take. The
        /// player is optional so the world can be built without one, which
        /// is what the placement tests do.
        /// </summary>
        public static LastRouteCarAssetRegistry Create(
            Transform parent,
            LastRouteCarPlan plan,
            PlayerRuntime player,
            Camera camera)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null || !plan.IsPresent)
            {
                return null;
            }

            GameObject prefab = LastRouteCarAssetRegistry.LoadPrefab();
            if (prefab == null)
            {
                GameLog.Warning("city", "last_route_car_prefab_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            root.position = plan.Position;
            root.rotation = Quaternion.LookRotation(plan.Facing, Vector3.up);

            GameObject instance = UnityEngine.Object.Instantiate(prefab, root);
            instance.name = prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var registry =
                instance.GetComponentInChildren<LastRouteCarAssetRegistry>(true);
            if (registry == null || !registry.IsBound)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                GameLog.Warning("city", "last_route_car_registry_missing");
                return null;
            }

            ValidatePassivePresentation(instance);
            AddObstacleCollider(root.gameObject, registry);
            InstallHeadlightHalos(root, registry);
            InstallPassengerSeat(root, registry, plan, player, camera);

            GameLog.Info(
                "city",
                "last_route_car_spawned",
                GameLog.Field("x", plan.Position.x),
                GameLog.Field("z", plan.Position.z));
            return registry;
        }

        /// <summary>
        /// Gives each burning headlight its own fog halo.
        ///
        /// An emissive lens is a couple of pixels the ExpSquared fog eats by
        /// about thirty metres, which is the whole reason every fixed lamp in
        /// this city carries a halo of its own rather than relying on the
        /// pooled night spots. These are the only lit thing on an abandoned
        /// lot, so they are what has to carry: warm, low and paired, read as
        /// a waiting car long before the car itself resolves.
        ///
        /// Halos rather than real Lights on purpose - the night light budget
        /// belongs to the street masts, and the lighthouse already set the
        /// precedent that a beacon does not need a Light to be seen.
        /// </summary>
        private static void InstallHeadlightHalos(
            Transform root,
            LastRouteCarAssetRegistry registry)
        {
            Transform lens = null;
            for (int index = 0; index < registry.Bindings.Count; index++)
            {
                LastRouteCarRendererBinding binding = registry.Bindings[index];
                if (binding.Role == "headlight" && binding.Renderer != null)
                {
                    lens = binding.Renderer.transform;
                    break;
                }
            }

            if (lens == null)
            {
                return;
            }

            // Both lamps live in one mesh, so the halos are placed from the
            // renderer's own bounds rather than from two transforms that do
            // not exist: one at each end of the lit face.
            Bounds bounds = lens.GetComponent<Renderer>().bounds;
            float lateral = bounds.extents.x;
            Vector3 centre = bounds.center;
            var inner = new Color(1f, 0.94f, 0.74f, 0.85f);
            var outer = new Color(0.86f, 0.72f, 0.40f, 0f);
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 world = centre + root.right * (lateral * side * 0.72f);
                CityLightHalo.CreateNightRegistered(
                    root,
                    root.InverseTransformPoint(world),
                    HeadlightHaloInnerSize,
                    HeadlightHaloOuterSize,
                    inner,
                    outer);
            }
        }

        /// <summary>
        /// Hangs the passenger seat off the car as a sibling of the art.
        /// The prefab is validated passive, so the trigger cannot live in
        /// it - and the seat is a runtime thing anyway: it needs the player
        /// and the shared animated-interaction controller.
        /// </summary>
        private static void InstallPassengerSeat(
            Transform root,
            LastRouteCarAssetRegistry registry,
            LastRouteCarPlan plan,
            PlayerRuntime player,
            Camera camera)
        {
            if (player.GameObject == null)
            {
                return;
            }

            LastRouteCarSeatPlan seatPlan =
                LastRouteCarSeatPlan.Create(registry, plan.Position.y);
            if (!seatPlan.IsPresent)
            {
                return;
            }

            var controller = player.GameObject
                .GetComponent<PlayerAnimatedInteractionController>();
            if (controller == null)
            {
                controller = player.GameObject
                    .AddComponent<PlayerAnimatedInteractionController>();
            }

            if (!controller.IsInitialized)
            {
                controller.Initialize(player, camera);
            }

            var seatObject = new GameObject("Passenger Seat");
            seatObject.transform.SetParent(root, false);
            seatObject.transform.position = seatPlan.TriggerCenter;
            seatObject.transform.rotation = seatPlan.TriggerRotation;
            var trigger = seatObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = seatPlan.TriggerSize;

            seatObject
                .AddComponent<LastRouteCarSeatInteraction>()
                .Initialize(player, controller, seatPlan);
        }

        /// <summary>
        /// One box around the bodywork. The mirror and the drooping bumper
        /// are trimmed off the collider on purpose: a wing mirror that stops
        /// the hero walking past reads as a bug, not as a car.
        /// </summary>
        private static void AddObstacleCollider(
            GameObject root,
            LastRouteCarAssetRegistry registry)
        {
            Bounds bounds = registry.LocalBounds;
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(
                bounds.center.x,
                Mathf.Max(bounds.center.y, bounds.size.y * 0.5f),
                bounds.center.z);
            collider.size = new Vector3(
                Mathf.Min(bounds.size.x, registry.Dimensions.Width),
                bounds.size.y,
                Mathf.Min(bounds.size.z, registry.Dimensions.Length));
        }

        private static void ValidatePassivePresentation(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "The Last Route car prefab must stay pure presentation; " +
                    "the obstacle collider belongs on the runtime root.");
            }
        }
    }
}
