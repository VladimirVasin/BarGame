using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Gives a permanent default NPC his place, clothes and Last Route actions.</summary>
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
            LastRouteFerrymanVoice voice)
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

            LastRouteFerrymanRigAnchors.LoadDriverBank();
            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);
            LastRouteFerrymanStance stance = plan.Stance;
            try
            {
                return PlaceFerryman(root, stance, car, targetInteraction, voice);
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root.gameObject);
                else UnityEngine.Object.DestroyImmediate(root.gameObject);
                throw;
            }
        }

        private static LastRouteFerrymanPresentation PlaceFerryman(
            Transform root, LastRouteFerrymanStance stance, LastRouteCarAssetRegistry car,
            InventoryTargetInteractionController targetInteraction, LastRouteFerrymanVoice voice)
        {
            VillageResidentPresentation actor = DefaultNpcFactory.CreateForCharacter(root, DefaultNpcPopulation.LastRouteFerryman);
            GameObject instance = actor.gameObject;
            instance.name = "Ferryman";
            instance.transform.SetPositionAndRotation(stance.Position, Quaternion.LookRotation(stance.Facing, Vector3.up));
            ValidatePassivePresentation(instance);
            actor.ReleaseAnimation();

            var anchors = instance.AddComponent<LastRouteFerrymanRigAnchors>();
            anchors.Bind(actor);
            var presentation = instance.AddComponent<LastRouteFerrymanPresentation>();
            presentation.Initialize(anchors, stance, car);
            NpcNameplateTarget.Attach(instance, "npc.name.ferryman", anchors.HeadAnchor, "ferryman")
                .SetInteractionRoot(root).SetHeadClearance(.5f);

            var coinObject = new GameObject("Ferryman Coin Rig");
            coinObject.transform.SetParent(root, false);
            coinObject.AddComponent<LastRouteFerrymanCoin>().Initialize(
                presentation, anchors.CoinRestAnchor, instance.transform);
            instance.AddComponent<LastRouteFerrymanCoat>().Initialize(presentation, instance.GetComponent<NpcWardrobe>());

            // He carries no light of his own any more. What lit him was a
            // bare Point with no fixture drawn anywhere, parented beside
            // him and rewritten every frame so it walked the lot with him -
            // warmth following a man around in the dark with nothing
            // casting it. The island's own mast now carries a second head
            // turned on the car; see BuildFerrymanCarFloodlight in
            // CityDistrictPointOfInterestWorldBuilder.

            // Colliderless like every staged NPC.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = FocusHeightMeters;

            // The talk trigger docks in front of him, which for once is the
            // side the player arrives on.
            //
            // The same box and the same dock either way; what changes is
            // what is behind it. A MENU belongs wherever this scene has armed
            // a ride for him to drive - the island going up, the terrace by
            // the cafe coming back down - and its second option is the
            // question that end of the road asks. Where nothing is armed he
            // gets the plain repertoire instead and answers without offering
            // anything, which is what he does in the city once he has been
            // driven home and the offer belongs to the next visit. Hand in
            // no voice at all and he is silent.
            if (voice.IsPresent)
            {
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
                // One tone and one head either way — whether he is
                // offering the road or only answering. The source lives
                // on the speech service: the passive-presentation guard
                // below rejects an AudioSource inside this instance.
                NpcSpeaker speaker = presentation.Speaker;
                if (targetInteraction != null)
                {
                    var menu = trigger
                        .AddComponent<LastRouteFerrymanInteraction>();
                    menu.Initialize(
                        stance.Position,
                        presentation,
                        targetInteraction,
                        voice.LineKeys,
                        voice.ConfirmationPromptKey,
                        voice.RefusalLineKey,
                        voice.QuipStream);
                    menu.AttachSpeaker(speaker);
                }
                else
                {
                    var talk = trigger
                        .AddComponent<LastRouteFerrymanTalkInteraction>();
                    talk.Initialize(
                        stance.Position,
                        presentation,
                        voice.LineKeys,
                        voice.QuipStream);
                    talk.AttachSpeaker(speaker);
                }
            }

            GameLog.Info(
                "city",
                "last_route_ferryman_spawned",
                GameLog.Field("position_x", stance.Position.x),
                GameLog.Field("position_z", stance.Position.z));
            return presentation;
        }

        /// <summary>
        /// Vehicle mechanics and speech own all scene resources around the actor.
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
                    "The Ferryman presentation must stay passive.");
            }
        }
    }
}
