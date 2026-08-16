using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the three authored drying-yard babushkas. There is
    /// no pool, no director and no spawn band: two beat the hung
    /// carpets, one smokes apart, always there while the City lives —
    /// the yard-wheelchair pattern applied to the Residential public
    /// place.
    /// </summary>
    public static class DryingYardBabushkaFactory
    {
        public const string RuntimeRootName = "Drying Yard Babushkas";

        public static IReadOnlyList<DryingYardBabushkaPresentation> Create(
            Transform parent,
            DryingYardBabushkaPlan plan)
        {
            return Create(parent, plan, DryingYardBabushkaProvider.Load());
        }

        public static IReadOnlyList<DryingYardBabushkaPresentation> Create(
            Transform parent,
            DryingYardBabushkaPlan plan,
            DryingYardBabushkaProvider provider)
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
                return Array.Empty<DryingYardBabushkaPresentation>();
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "drying_yard_babushka_provider_missing");
                return Array.Empty<DryingYardBabushkaPresentation>();
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            var presentations =
                new List<DryingYardBabushkaPresentation>(
                    plan.Stances.Count);
            for (int index = 0; index < plan.Stances.Count; index++)
            {
                DryingYardBabushkaStance stance = plan.Stances[index];
                GameObject instance = UnityEngine.Object.Instantiate(
                    provider.StagedPrefab,
                    root);
                instance.name =
                    $"Yard Babushka {index + 1} ({stance.Role})";
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
                        "The staged babushka prefab requires a " +
                        nameof(CityPedestrianAssetRegistry) + ".");
                }

                ValidatePassivePresentation(instance);

                var presentation = instance
                    .AddComponent<DryingYardBabushkaPresentation>();
                presentation.Initialize(registry, stance);

                // The babushkas are colliderless like the yard rider,
                // so the hero's attention finds them through magnets
                // at their hunched head height.
                var magnet =
                    instance.AddComponent<PlayerAttentionMagnet>();
                magnet.FocusHeight = 1.45f;
                presentations.Add(presentation);
            }

            GameLog.Info(
                "city",
                "drying_yard_babushkas_spawned",
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
                    "The staged babushka presentation must stay passive.");
            }
        }
    }
}
