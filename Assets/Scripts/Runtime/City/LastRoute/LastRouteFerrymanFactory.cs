using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored Ferryman. There is no pool, no
    /// director and no spawn band: he keeps the bonnet of his own car for
    /// as long as the City lives, or he is absent along with the car.
    ///
    /// Five things are added around the staged prefab rather than into it,
    /// and all five for the same reason: that prefab is validated passive,
    /// with no collider, light, audio or interaction anywhere in it. The
    /// coin, the cloth coat, the lamp over him, the attention magnet and
    /// the talk trigger are each raised separately, so the art stays art
    /// and the guard keeps working.
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

        /// <summary>
        /// The one lamp on the island that exists for a person rather
        /// than for a place.
        ///
        /// He is a man in the darkest coat in the game, sitting on an
        /// unlit lot under ExpSquared fog, and the two things that carry
        /// him - the coin and the hands throwing it - are the two
        /// smallest. Without this he is a silhouette with a bright chip
        /// somewhere in it, which is what he was. The cemetery lodge's
        /// porch bulb is the precedent and the same registry carries
        /// both: a fixture that never switches off, only drops to a
        /// floor by day, because the point is that he is lit whenever
        /// anybody walks up.
        ///
        /// Unlike the porch bulb there is NO drawn fixture and no fog
        /// halo, and that is on purpose. A halo is the blurred ball of a
        /// lamp, and there is no lamp here to blur - the warmth is the
        /// throw of his own burning headlights coming back off the mist
        /// in front of the car. So it lights him and draws nothing.
        /// </summary>
        public static readonly Color LampColor =
            new Color(1.00f, 0.87f, 0.66f);

        public const float LampNightIntensity = 70f;
        public const float LampDayIntensity = 22f;
        public const float LampRangeMeters = 5.2f;

        /// <summary>
        /// Where it hangs: in front of him and above his cap, so the
        /// light rakes DOWN over the brim. That direction is load
        /// bearing - the design draws no eyes and relies on the brim's
        /// own near-black shadow slab to keep the face unreadable, and
        /// lighting him from below would be the one angle that argues
        /// with it.
        /// </summary>
        public const float LampReachOutMeters = 1.50f;
        public const float LampHeightMeters = 2.10f;

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
            presentation.Initialize(registry, anchors, stance, car);

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

            InstallLamp(root, stance);

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
        /// Hangs the one lamp that exists to make him readable. See
        /// <see cref="LampColor"/> for why it has no drawn fixture.
        ///
        /// It is a child of the runtime root rather than of the art, so
        /// the staged prefab stays the passive thing the guard below
        /// insists it is.
        /// </summary>
        private static void InstallLamp(
            Transform root,
            LastRouteFerrymanStance stance)
        {
            var emitter = new GameObject("Ferryman Lamp");
            emitter.transform.SetParent(root, false);
            emitter.transform.position =
                stance.Position +
                Vector3.up * LampHeightMeters +
                stance.Facing * LampReachOutMeters;

            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LampColor;
            light.intensity = LampNightIntensity;
            light.range = LampRangeMeters;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            CityNightSiteLightRegistry.Register(
                light,
                LampNightIntensity,
                LampDayIntensity,
                null);
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
