using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored lake fisherman. There is no pool,
    /// no director and no spawn band: he keeps the end of the pier for
    /// as long as the City lives — the watchman pattern applied to the
    /// boat station, plus the same separate talk trigger carrying his
    /// repertoire.
    /// </summary>
    public static class LakeFishermanFactory
    {
        public const string RuntimeRootName = "Lake Fisherman";

        /// <summary>Trigger box dimensions mirrored from the watchman's
        /// talk stub.</summary>
        public const float TriggerSpan = 1.7f;
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;

        /// <summary>How far behind him the trigger docks. He faces the
        /// water, so the watchman's in-front dock would put this box
        /// out over the pond where nobody can reach it.</summary>
        public const float TriggerBackOffMeters = 0.55f;

        public static LakeFishermanPresentation Create(
            Transform parent,
            LakeFishermanPlan plan,
            int citySeed)
        {
            return Create(
                parent,
                plan,
                citySeed,
                LakeFishermanProvider.Load());
        }

        public static LakeFishermanPresentation Create(
            Transform parent,
            LakeFishermanPlan plan,
            int citySeed,
            LakeFishermanProvider provider)
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
                return null;
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "lake_fisherman_provider_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            LakeFishermanStance stance = plan.Stance;
            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                root);
            instance.name = "Fisherman";
            instance.transform.SetPositionAndRotation(
                stance.Position,
                Quaternion.LookRotation(stance.Facing, Vector3.up));

            var registry = instance
                .GetComponentInChildren<CityPedestrianAssetRegistry>(
                    true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The staged fisherman prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<LakeFishermanPresentation>();
            presentation.Initialize(registry, stance);

            // Colliderless like every staged NPC. The focus height is a
            // seated head, not the watchman's standing one.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = 1.34f;

            // The talk stub docks behind him, on the shore side, which
            // is the only side of him a person can stand on.
            var trigger = new GameObject("Fisherman Talk Trigger");
            trigger.transform.SetParent(root, false);
            trigger.transform.SetPositionAndRotation(
                stance.Position +
                Vector3.up * 0.9f -
                stance.Facing * TriggerBackOffMeters,
                Quaternion.LookRotation(-stance.Facing, Vector3.up));
            var collider = trigger.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(
                TriggerSpan,
                TriggerHeight,
                TriggerReach);
            var interaction = trigger
                .AddComponent<LakeFishermanInteraction>();
            interaction.Initialize(stance.Position, citySeed);

            GameLog.Info(
                "city",
                "lake_fisherman_spawned",
                GameLog.Field("position_x", stance.Position.x),
                GameLog.Field("position_z", stance.Position.z));
            return presentation;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio, light or
        /// interaction — the talk stub is added separately.
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
                    "The staged fisherman presentation must stay passive.");
            }
        }
    }
}
