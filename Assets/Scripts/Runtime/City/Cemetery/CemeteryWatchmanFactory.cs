using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored cemetery watchman. There is no
    /// pool, no director and no spawn band: the old man holds his
    /// window post beside the gate lodge for as long as the City
    /// lives — the weighbridge pattern applied to the cemetery, plus
    /// the cashier's separate talk trigger carrying his snide
    /// repertoire.
    /// </summary>
    public static class CemeteryWatchmanFactory
    {
        public const string RuntimeRootName = "Cemetery Watchman";

        /// <summary>Trigger box dimensions and dock offsets mirrored
        /// from the street-utility talk stubs.</summary>
        public const float TriggerSpan = 1.7f;
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;

        public static CemeteryWatchmanPresentation Create(
            Transform parent,
            CemeteryWatchmanPlan plan,
            int citySeed)
        {
            return Create(
                parent,
                plan,
                citySeed,
                CemeteryWatchmanProvider.Load());
        }

        public static CemeteryWatchmanPresentation Create(
            Transform parent,
            CemeteryWatchmanPlan plan,
            int citySeed,
            CemeteryWatchmanProvider provider)
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
                    "cemetery_watchman_provider_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            CemeteryWatchmanStance stance = plan.Stance;
            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                root);
            instance.name = "Watchman";
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
                    "The staged watchman prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<CemeteryWatchmanPresentation>();
            presentation.Initialize(registry, stance);

            // Colliderless like every staged NPC: the hero's
            // attention finds him through a magnet at his capped
            // head height.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = 1.58f;

            // The talk stub lives on its own trigger, the cashier
            // convention: a trigger box in front of the watchman on
            // his facing side, never inside the lodge's batched wall
            // collider.
            var trigger = new GameObject("Watchman Talk Trigger");
            trigger.transform.SetParent(root, false);
            trigger.transform.SetPositionAndRotation(
                stance.Position +
                Vector3.up * 0.9f +
                stance.Facing * 0.375f,
                Quaternion.LookRotation(stance.Facing, Vector3.up));
            var collider = trigger.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(
                TriggerSpan,
                TriggerHeight,
                TriggerReach);
            var interaction = trigger
                .AddComponent<CemeteryWatchmanInteraction>();
            interaction.Initialize(stance.Position, citySeed);
            // His own tone and his own head, so the answer types out
            // over there rather than arriving from nowhere. The source
            // itself lives on the speech service, never inside this
            // instance — the passive-presentation guard below rejects
            // an AudioSource in the model, and it is right to.
            interaction.AttachSpeaker(
                NpcSpeaker.FromRegistry(
                    presentation,
                    registry,
                    NpcEarshotProfile.Conversation));
            presentation.Talk = interaction;

            GameLog.Info(
                "city",
                "cemetery_watchman_spawned",
                GameLog.Field("position_x", stance.Position.x),
                GameLog.Field("position_z", stance.Position.z));
            return presentation;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio, light
        /// or interaction — the talk stub is added separately.
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
                    "The staged watchman presentation must stay passive.");
            }
        }
    }
}
