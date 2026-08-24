using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored Ferryman. There is no pool, no
    /// director and no spawn band: he keeps the bonnet of his own car for
    /// as long as the City lives, or he is absent along with the car.
    ///
    /// Four things are added around the staged prefab rather than into it,
    /// and all four for the same reason: that prefab is validated passive,
    /// with no collider, light, audio or interaction anywhere in it. The
    /// coin, the cloth coat, the attention magnet and the talk trigger are
    /// each raised separately, so the art stays art and the guard keeps
    /// working.
    /// </summary>
    public static class LastRouteFerrymanFactory
    {
        public const string RuntimeRootName = "Last Route Ferryman";

        /// <summary>Trigger box, mirrored from the fisherman's talk
        /// stub.</summary>
        public const float TriggerSpan = 1.7f;
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;

        /// <summary>
        /// How far IN FRONT of him the trigger docks. The fisherman's sits
        /// behind him because he faces the water; this one is the exact
        /// opposite, and deliberately so - the Ferryman is facing out over
        /// his own bonnet at whoever is walking up, and being met head-on
        /// is the entire first impression of the character.
        /// </summary>
        public const float TriggerReachOutMeters = 1.15f;

        /// <summary>Where the eye is drawn. Perched rather than standing,
        /// so lower than a standing man's - measured off the authored
        /// bonnet pose rather than guessed.</summary>
        public const float FocusHeightMeters = 1.42f;

        public static LastRouteFerrymanPresentation Create(
            Transform parent,
            LastRouteFerrymanPlan plan,
            LastRouteCarAssetRegistry car,
            InventoryTargetInteractionController targetInteraction,
            int citySeed)
        {
            return Create(
                parent,
                plan,
                car,
                targetInteraction,
                citySeed,
                LastRouteFerrymanProvider.Load());
        }

        public static LastRouteFerrymanPresentation Create(
            Transform parent,
            LastRouteFerrymanPlan plan,
            LastRouteCarAssetRegistry car,
            InventoryTargetInteractionController targetInteraction,
            int citySeed,
            LastRouteFerrymanProvider provider)
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

            if (car == null)
            {
                // The plan cannot be present without a car, so this is a
                // caller passing two things that disagree rather than a
                // seed with nowhere to park.
                throw new ArgumentNullException(nameof(car));
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "city",
                    "last_route_ferryman_provider_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            LastRouteFerrymanStance stance = plan.Stance;
            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                root);
            instance.name = "Ferryman";
            instance.transform.SetPositionAndRotation(
                stance.Position,
                Quaternion.LookRotation(stance.Facing, Vector3.up));

            var registry = instance
                .GetComponentInChildren<CityPedestrianAssetRegistry>(true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The staged Ferryman prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            var anchors = instance
                .GetComponentInChildren<LastRouteFerrymanRigAnchors>(true);
            if (anchors == null ||
                anchors.CoinRestAnchor == null ||
                anchors.CoatHemAnchor == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The staged Ferryman prefab requires bound " +
                    nameof(LastRouteFerrymanRigAnchors) + " for its coin " +
                    "and its coat.");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<LastRouteFerrymanPresentation>();
            presentation.Initialize(registry, stance, car);

            // The coin and the coat are both children of the ROOT rather
            // than of any bone: the imported hierarchy carries Unity's 100x
            // FBX scale, and both of these are written into world space
            // every frame instead of inheriting it. Neither needs the
            // inverse-scale dance that bone-socket props do.
            var coinObject = new GameObject("Ferryman Coin Rig");
            coinObject.transform.SetParent(root, false);
            coinObject.AddComponent<LastRouteFerrymanCoin>().Initialize(
                presentation,
                anchors.CoinRestAnchor,
                instance.transform);

            var coatObject = new GameObject("Ferryman Coat");
            coatObject.transform.SetParent(root, false);
            coatObject.AddComponent<LastRouteFerrymanCoat>().Initialize(
                anchors,
                instance.transform);

            // Colliderless like every staged NPC.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = FocusHeightMeters;

            // The talk trigger docks in front of him, which for once is the
            // side the player arrives on.
            var trigger = new GameObject("Ferryman Talk Trigger");
            trigger.transform.SetParent(root, false);
            trigger.transform.SetPositionAndRotation(
                stance.Position +
                Vector3.up * 0.9f +
                stance.Facing * TriggerReachOutMeters,
                Quaternion.LookRotation(stance.Facing, Vector3.up));
            var collider = trigger.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(
                TriggerSpan,
                TriggerHeight,
                TriggerReach);
            trigger
                .AddComponent<LastRouteFerrymanInteraction>()
                .Initialize(
                    stance.Position,
                    citySeed,
                    presentation,
                    targetInteraction);

            GameLog.Info(
                "city",
                "last_route_ferryman_spawned",
                GameLog.Field("position_x", stance.Position.x),
                GameLog.Field("position_z", stance.Position.z));
            return presentation;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose. Instantiating
        /// it must not smuggle in physics, audio, light or interaction -
        /// the coin, the coat and the talk trigger are added separately.
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
                    "The staged Ferryman presentation must stay passive.");
            }
        }
    }
}
