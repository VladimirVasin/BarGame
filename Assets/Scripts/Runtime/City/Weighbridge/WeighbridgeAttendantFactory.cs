using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the two authored weighbridge attendants. There is
    /// no pool, no director and no spawn band: the weigher reads her
    /// instrument beside the mechanism and the worker paces the deck,
    /// always there while the City lives — the drying-yard babushka
    /// pattern applied to the Industrial public place.
    /// </summary>
    public static class WeighbridgeAttendantFactory
    {
        public const string RuntimeRootName = "Weighbridge Attendants";

        public static IReadOnlyList<WeighbridgeAttendantPresentation>
            Create(
                Transform parent,
                WeighbridgeAttendantPlan plan)
        {
            return Create(
                parent,
                plan,
                WeighbridgeAttendantProvider.Load());
        }

        public static IReadOnlyList<WeighbridgeAttendantPresentation>
            Create(
                Transform parent,
                WeighbridgeAttendantPlan plan,
                WeighbridgeAttendantProvider provider)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return Array.Empty<WeighbridgeAttendantPresentation>();
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "weighbridge_attendant_provider_missing");
                return Array.Empty<WeighbridgeAttendantPresentation>();
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            var presentations =
                new List<WeighbridgeAttendantPresentation>(
                    plan.Stances.Count);
            for (int index = 0; index < plan.Stances.Count; index++)
            {
                WeighbridgeAttendantStance stance = plan.Stances[index];
                GameObject instance = UnityEngine.Object.Instantiate(
                    provider.StagedPrefab,
                    root);
                instance.name =
                    $"Weighbridge Attendant {index + 1} ({stance.Role})";
                instance.transform.SetPositionAndRotation(
                    stance.Position,
                    Quaternion.LookRotation(stance.Facing, Vector3.up));

                var registry = instance
                    .GetComponentInChildren<
                        CityPedestrianAssetRegistry>(true);
                if (registry == null)
                {
                    UnityEngine.Object.Destroy(root.gameObject);
                    throw new InvalidOperationException(
                        "The staged attendant prefab requires a " +
                        nameof(CityPedestrianAssetRegistry) + ".");
                }

                ValidatePassivePresentation(instance);

                var presentation = instance
                    .AddComponent<WeighbridgeAttendantPresentation>();
                presentation.Initialize(registry, stance);

                // The attendants are colliderless like the babushkas,
                // so the hero's attention finds them through magnets
                // at their upright head height.
                var magnet =
                    instance.AddComponent<PlayerAttentionMagnet>();
                magnet.FocusHeight = 1.60f;
                presentations.Add(presentation);
            }

            GameLog.Info(
                "city",
                "weighbridge_attendants_spawned",
                GameLog.Field("count", presentations.Count),
                GameLog.Field(
                    "position_x",
                    plan.Stances[0].Position.x),
                GameLog.Field(
                    "position_z",
                    plan.Stances[0].Position.z));
            return presentations;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio, light
        /// or interaction.
        /// </summary>
        private static void ValidatePassivePresentation(
            GameObject instance)
        {
            if (instance.GetComponentInChildren<Collider>(true) != null ||
                instance.GetComponentInChildren<Rigidbody>(true) != null ||
                instance.GetComponentInChildren<AudioSource>(true) != null ||
                instance.GetComponentInChildren<Light>(true) != null ||
                instance.GetComponentInChildren<Camera>(true) != null)
            {
                throw new InvalidOperationException(
                    "The staged attendant presentation must stay passive.");
            }
        }
    }
}
