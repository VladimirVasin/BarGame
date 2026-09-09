using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Puts the mother in her chair and starts the chair rocking.
    ///
    /// One woman, no pool, no director, no spawn band - the drying-yard
    /// babushka pattern with the population removed. She is where she is
    /// whenever the room exists.
    ///
    /// HER BODY IS PASSIVE AND STAYS PASSIVE. No collider (the chair's own
    /// blocker already stands there), no AudioSource (the room holds exactly
    /// three and a fourth breaks its soundscape contract). The hero's
    /// attention finds her through a magnet at her seated head height, the
    /// way it finds every other colliderless figure in the game.
    ///
    /// She speaks and she looks back from `2026-09-09`, by a §6 registry row.
    /// Both are attached the way a passive body allows: the head-look is a
    /// behaviour that writes bones behind her own graph, and the talk stub
    /// stands on its own trigger object BESIDE her, never on her, because
    /// the colliderless check above still holds and is still enforced.
    /// </summary>
    public static class MothersHouseMotherFactory
    {
        public const string RuntimeRootName = "Mother's House Mother";

        /// <summary>
        /// Her seated head, for the attention magnet. The cushion is at
        /// `0.57` and an old woman settled back adds about three quarters of
        /// a metre of spine and skull - well below the `1.45` the standing
        /// babushkas use, because she is sitting down.
        /// </summary>
        public const float SeatedFocusHeight = 1.32f;

        public static MothersHouseMotherPresentation Create(
            Transform roomRoot,
            MothersHouseMotherPlan plan,
            MothersHouseRockingChairMotion chairMotion)
        {
            return Create(
                roomRoot,
                plan,
                chairMotion,
                MothersHouseMotherProvider.Load());
        }

        public static MothersHouseMotherPresentation Create(
            Transform roomRoot,
            MothersHouseMotherPlan plan,
            MothersHouseRockingChairMotion chairMotion,
            MothersHouseMotherProvider provider)
        {
            if (roomRoot == null)
            {
                throw new ArgumentNullException(nameof(roomRoot));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (provider == null || provider.StagedPrefab == null)
            {
                GameLog.Warning(
                    "mothers_house",
                    "mother_provider_missing");
                return null;
            }

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                roomRoot);
            instance.name = RuntimeRootName;
            instance.transform.localPosition = plan.SeatPosition;
            instance.transform.localRotation =
                Quaternion.LookRotation(plan.Facing, Vector3.up);
            instance.transform.localScale = Vector3.one;

            CityPedestrianAssetRegistry registry =
                instance.GetComponentInChildren<
                    CityPedestrianAssetRegistry>(true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(instance);
                throw new InvalidOperationException(
                    "The staged mother prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            MothersHouseMotherPresentation presentation =
                instance.AddComponent<MothersHouseMotherPresentation>();
            presentation.Initialize(registry, plan.InitialPhase);

            PlayerAttentionMagnet magnet =
                instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = SeatedFocusHeight;

            // The chair takes her AFTER she is placed, so the pose it records
            // as her rest is the one the plan asked for. Handing her over
            // before placement would freeze her at the room origin and rock
            // her there for the rest of the scene.
            if (chairMotion != null)
            {
                chairMotion.Carry(instance.transform);
            }

            GameLog.Info(
                "mothers_house",
                "mother_seated",
                GameLog.Field("design_id", registry.DesignId),
                GameLog.Field("seat_x", plan.SeatPosition.x),
                GameLog.Field("seat_z", plan.SeatPosition.z),
                GameLog.Field("triangles", registry.SourceTriangleCount),
                GameLog.Field("has_face_atlas", registry.HasFaceAtlas));
            return presentation;
        }

        /// <summary>
        /// She turns her head to the hero, behind her own presentation.
        ///
        /// `NpcHeroAttentionLook` rather than the raw layer: her graph is
        /// evaluated at order `310` and the chair re-poses her root at `300`,
        /// and the look runs at `350` — after both, which is exactly the case
        /// it was written for. Doing it with the raw layer would mean
        /// bracketing her presentation's own evaluate, and nothing about her
        /// needs that.
        /// </summary>
        public static NpcHeroAttentionLook AttachHeroAttention(
            MothersHouseMotherPresentation presentation,
            Transform heroRoot)
        {
            if (presentation == null)
            {
                throw new ArgumentNullException(nameof(presentation));
            }

            CityPedestrianAssetRegistry registry = presentation.Registry;
            if (heroRoot == null ||
                registry == null ||
                registry.Head == null)
            {
                return null;
            }

            NpcHeroAttentionLook look = presentation.gameObject
                .AddComponent<NpcHeroAttentionLook>();
            look.Initialize(
                presentation.transform,
                registry.Head,
                NpcAttentionHeadLayer.FindBone(
                    registry.ModelRoot,
                    "neck"),
                heroRoot);
            return look;
        }

        /// <summary>
        /// Her talk stub, on its own trigger in front of the chair.
        ///
        /// Parented to the room rather than to her, and placed from the same
        /// seat the plan gave her, so moving the chair moves the trigger with
        /// it. It does not rock: the chair leans two and a half degrees, and
        /// a trigger box that chased that would be reach the hero cannot
        /// predict.
        /// </summary>
        public static MothersHouseMotherInteraction CreateTalkTrigger(
            Transform roomRoot,
            MothersHouseMotherPlan plan,
            MothersHouseMotherPresentation presentation)
        {
            if (roomRoot == null)
            {
                throw new ArgumentNullException(nameof(roomRoot));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var trigger = new GameObject("Mother Talk Trigger");
            trigger.transform.SetParent(roomRoot, false);
            trigger.transform.localPosition =
                plan.SeatPosition +
                Vector3.up * SeatedFocusHeight +
                plan.Facing * MothersHouseMotherInteraction.TriggerReach;
            trigger.transform.localRotation =
                Quaternion.LookRotation(plan.Facing, Vector3.up);

            BoxCollider collider = trigger.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(
                MothersHouseMotherInteraction.TriggerSpan,
                MothersHouseMotherInteraction.TriggerHeight,
                MothersHouseMotherInteraction.TriggerDepth);

            MothersHouseMotherInteraction interaction =
                trigger.AddComponent<MothersHouseMotherInteraction>();
            interaction.Initialize(
                plan.SeatPosition + Vector3.up * SeatedFocusHeight);
            interaction.AttachSpeaker(ResolveSpeaker(presentation));
            return interaction;
        }

        /// <summary>
        /// Her overhead voice: the bubble she talks to herself through
        /// and answers `E` through. Built here rather than in the room
        /// root because it needs her registry's head anchor, which is
        /// the one thing only the factory has seen.
        /// </summary>
        public static MothersHouseMotherSpeechController CreateSpeech(
            Transform roomRoot,
            MothersHouseMotherPlan plan,
            MothersHouseMotherPresentation presentation,
            Camera camera,
            Transform heroRoot)
        {
            if (roomRoot == null)
            {
                throw new ArgumentNullException(nameof(roomRoot));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            NpcSpeaker speaker = ResolveSpeaker(presentation);
            if (!speaker.IsValid)
            {
                return null;
            }

            return MothersHouseMotherSpeechController.Create(
                roomRoot,
                camera,
                heroRoot,
                plan.SeatPosition + Vector3.up * SeatedFocusHeight,
                speaker);
        }

        private static NpcSpeaker ResolveSpeaker(
            MothersHouseMotherPresentation presentation)
        {
            return presentation != null && presentation.Registry != null
                ? NpcSpeaker.FromRegistry(
                    presentation,
                    presentation.Registry,
                    NpcEarshotProfile.Conversation)
                : NpcSpeaker.None;
        }

        /// <summary>
        /// The staged prefab is authored passive and the editor pipeline
        /// enforces that. This is the same check on the other side of the
        /// import, because a prefab can gain a component between the two.
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
                    "The staged mother presentation must stay passive.");
            }
        }
    }
}
