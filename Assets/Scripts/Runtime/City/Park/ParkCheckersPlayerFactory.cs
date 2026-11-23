using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Seats the assigned default resident and claims his bench.</summary>
    public static class ParkCheckersPlayerFactory
    {
        public const string RuntimeRootName = "Park Checkers Player";

        /// <summary>
        /// How far forward of the plank's middle he perches. A man
        /// reaching his elbows onto a board sits on the front half of
        /// the bench; parked dead centre he would be stretching, and the
        /// elbows this design was fitted with would miss the rim. Same
        /// number as the chess player because it is the same plank and
        /// the same board rim, measured the same way.
        /// </summary>
        public const float PerchForwardOffsetMeters = 0.06f;

        /// <summary>
        /// Focus height for the attention magnet, measured off the
        /// authored mulling pose rather than guessed. Folded over the
        /// board with his head in his hands he carries it far lower
        /// than the watchman's upright 1.34 m post would suggest.
        /// </summary>
        public const float FocusHeightMeters = 1.02f;

        // The talk stub is deliberately not built yet, exactly as next
        // door. When it is, it docks behind him the way the fisherman's
        // does - he faces the table, so an in-front dock would put the
        // box out over the board where nobody can stand - and it is a
        // separate object on this root, never a component added into the
        // staged art.
        public const float TriggerSpan = 1.7f;
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;
        public const float TriggerBackOffMeters = 0.75f;

        public static ParkCheckersPlayerPresentation Create(
            Transform parent,
            ParkCheckersPlayerPlan plan)
        {
            return Create(
                parent,
                plan,
                ParkCheckersPlayerProvider.Load());
        }

        public static ParkCheckersPlayerPresentation Create(
            Transform parent,
            ParkCheckersPlayerPlan plan,
            ParkCheckersPlayerProvider provider)
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

            if (provider == null || provider.IdleClip == null)
            {
                GameLog.Warning(
                    "city",
                    "park_checkers_player_provider_missing");
                return null;
            }

            ParkCheckersPlayerStance stance = plan.Stance;
            Vector3 facing = stance.Facing;
            facing.y = 0f;
            facing = facing.sqrMagnitude > 0.0001f
                ? facing.normalized
                : Vector3.forward;

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            VillageResidentPresentation actor = DefaultNpcFactory.CreateForCharacter(
                root, DefaultNpcPopulation.ParkCheckersPlayer, initializeLocomotion: false);
            GameObject instance = actor.gameObject;
            instance.name = "Checkers Player";

            // Horizontal placement only carries the perch across the
            // plank; the height is corrected every frame by the
            // presentation, which stands the seat of his coat on the
            // drawn timber whatever the lawn under it is doing.
            instance.transform.SetPositionAndRotation(
                stance.SeatTopCenter +
                    (facing * PerchForwardOffsetMeters),
                Quaternion.LookRotation(facing, Vector3.up));

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<ParkCheckersPlayerPresentation>();
            presentation.Initialize(actor, provider, stance);
            NpcNameplateTarget.Attach(
                instance, "npc.name.checkers_player", actor.Head, "checkers_player").SetHeadClearance(0.5f);

            // Colliderless like every staged NPC.
            var magnet = instance.AddComponent<PlayerAttentionMagnet>();
            magnet.FocusHeight = FocusHeightMeters;

            // For as long as the City lives. The seat is never released:
            // he does not get up.
            if (!CityBenchSeatClaims.TryClaim(
                    stance.SeatId,
                    presentation))
            {
                GameLog.Warning(
                    "city",
                    "park_checkers_player_seat_taken",
                    GameLog.Field("seat_id", stance.SeatId));
            }

            // The seat id is logged as well as the position, because
            // there are two men on this set now and a pair of
            // coordinates is not enough to tell which one a line is
            // about.
            GameLog.Info(
                "city",
                "park_checkers_player_spawned",
                GameLog.Field("seat_id", stance.SeatId),
                GameLog.Field("position_x", stance.SeatTopCenter.x),
                GameLog.Field("position_z", stance.SeatTopCenter.z));
            return presentation;
        }

        /// <summary>
        /// The board owns interactions and the resident only presents the seated pose.
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
                    "The checkers player presentation must stay passive.");
            }
        }
    }
}
