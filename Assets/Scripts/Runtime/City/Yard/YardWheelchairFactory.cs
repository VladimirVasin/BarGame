using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the single staged rider on the yard circuit. There is
    /// no pool, no director and no spawn band: one man, one ring, always
    /// there.
    /// </summary>
    public static class YardWheelchairFactory
    {
        public const string RuntimeRootName = "Yard Wheelchair Runtime";

        public static YardWheelchairActor Create(
            Transform parent,
            YardWheelchairPlan plan)
        {
            return Create(parent, plan, YardWheelchairProvider.Load());
        }

        public static YardWheelchairActor Create(
            Transform parent,
            YardWheelchairPlan plan,
            YardWheelchairProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "yard_wheelchair_provider_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                root);
            instance.name = provider.StagedPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var wheelchairRegistry =
                instance.GetComponentInChildren<
                    CityWheelchairNpcAssetRegistry>(true);
            if (wheelchairRegistry == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The staged wheelchair prefab requires a " +
                    nameof(CityWheelchairNpcAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<YardWheelchairPresentation>();
            presentation.Initialize(wheelchairRegistry);

            var actor = instance.AddComponent<YardWheelchairActor>();
            actor.Initialize(plan, presentation);

            // The rider is colliderless, so the hero's attention finds
            // him through a magnet at seated head height.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = 1.15f;
            ReportSpawn(instance, plan);
            return actor;
        }

        /// <summary>
        /// Records where the rider actually materialised and how big he is,
        /// so "he is not there" can be answered from the log instead of
        /// from guesswork.
        /// </summary>
        private static void ReportSpawn(
            GameObject instance,
            YardWheelchairPlan plan)
        {
            Renderer[] renderers =
                instance.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            int visible = 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                if (!renderers[index].enabled)
                {
                    continue;
                }

                if (visible == 0)
                {
                    bounds = renderers[index].bounds;
                }
                else
                {
                    bounds.Encapsulate(renderers[index].bounds);
                }

                visible++;
            }

            GameLog.Info(
                "city",
                "yard_wheelchair_spawned",
                GameLog.Field("renderer_count", renderers.Length),
                GameLog.Field("visible_renderer_count", visible),
                GameLog.Field("position_x", instance.transform.position.x),
                GameLog.Field("position_y", instance.transform.position.y),
                GameLog.Field("position_z", instance.transform.position.z),
                GameLog.Field("bounds_center_x", bounds.center.x),
                GameLog.Field("bounds_center_y", bounds.center.y),
                GameLog.Field("bounds_center_z", bounds.center.z),
                GameLog.Field("bounds_size_x", bounds.size.x),
                GameLog.Field("bounds_size_y", bounds.size.y),
                GameLog.Field("bounds_size_z", bounds.size.z),
                GameLog.Field("circuit_center_x", plan.Center.x),
                GameLog.Field("circuit_center_z", plan.Center.z),
                GameLog.Field("circuit_ground_y", plan.GroundY),
                GameLog.Field("circuit_radius", plan.Radius));
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose. Instantiating
        /// it must not smuggle in physics, audio, light or interaction.
        /// </summary>
        private static void ValidatePassivePresentation(GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "The staged wheelchair presentation must stay passive.");
            }
        }
    }
}
