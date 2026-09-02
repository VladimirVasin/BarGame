using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored seacoast fisherman. There is no pool,
    /// no director and no spawn band: he keeps the end of the pier for
    /// as long as the City lives — the watchman pattern applied to the
    /// boat station, plus the same separate talk trigger carrying his
    /// repertoire.
    ///
    /// Three things are added around the staged prefab rather than into
    /// it, and all three for the same reason: that prefab is validated
    /// passive, with no collider, light, audio or interaction anywhere
    /// in it. The talk stub, the burning pipe and the fishing line are
    /// each a separate object hung off this root, so the art stays art
    /// and the guard keeps working.
    /// </summary>
    public static class SeacoastFishermanFactory
    {
        public const string RuntimeRootName = "Seacoast Fisherman";

        /// <summary>Trigger box dimensions mirrored from the watchman's
        /// talk stub.</summary>
        public const float TriggerSpan = 1.7f;
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;

        /// <summary>How far behind him the trigger docks. He faces the
        /// water, so the watchman's in-front dock would put this box
        /// out over the pond where nobody can reach it.</summary>
        public const float TriggerBackOffMeters = 0.55f;

        public static SeacoastFishermanPresentation Create(
            Transform parent,
            SeacoastFishermanPlan plan,
            int citySeed)
        {
            return Create(
                parent,
                plan,
                citySeed,
                SeacoastFishermanProvider.Load());
        }

        public static SeacoastFishermanPresentation Create(
            Transform parent,
            SeacoastFishermanPlan plan,
            int citySeed,
            SeacoastFishermanProvider provider)
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
                    "seacoast_fisherman_provider_missing");
                return null;
            }

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            SeacoastFishermanStance stance = plan.Stance;
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

            var anchors = instance
                .GetComponentInChildren<SeacoastFishermanRigAnchors>(true);
            if (anchors == null ||
                anchors.PipeEmberAnchor == null ||
                anchors.RodTipAnchor == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The staged fisherman prefab requires bound " +
                    nameof(SeacoastFishermanRigAnchors) + " for its pipe " +
                    "and rod tip.");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<SeacoastFishermanPresentation>();
            presentation.Initialize(registry, stance);

            // The pipe. The prefab is validated to carry no light, so
            // the ember, its glow and the plume are raised here and
            // driven from the loop the presentation is already running.
            var pipe = new GameObject("Fisherman Pipe");
            pipe.transform.SetParent(root, false);
            pipe.AddComponent<SeacoastFishermanPipeEffect>().Initialize(
                presentation,
                anchors.PipeEmberAnchor,
                anchors.PipeEmberRenderer);

            // And the line, from the live rod tip down to the water the
            // coast plan measured. The float rides the sea's swell -
            // the world build has already anchored the shore fade, so
            // the profile it hands over matches the sheets drawn a few
            // metres past his boots.
            var line = new GameObject("Fisherman Line");
            line.transform.SetParent(root, false);
            line.AddComponent<SeacoastFishermanLine>().Initialize(
                anchors.RodTipAnchor,
                plan.WaterTopY,
                CitySeaResources.CreateWaveProfile());

            // Colliderless like every staged NPC. The focus height is
            // measured off the authored lean rather than guessed: tipped
            // out over the end board he carries his head at `1.45 m`,
            // lower than the watchman's upright `1.34 m` post would
            // suggest for a standing man.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = 1.45f;

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
                .AddComponent<SeacoastFishermanInteraction>();
            interaction.Initialize(stance.Position, citySeed);
            // His own tone and his own head. The source stays on the
            // speech service: the passive-presentation guard below
            // rejects an AudioSource inside this instance, and should.
            interaction.AttachSpeaker(
                NpcSpeaker.FromRegistry(
                    presentation,
                    registry,
                    NpcEarshotProfile.Conversation));

            GameLog.Info(
                "city",
                "seacoast_fisherman_spawned",
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
