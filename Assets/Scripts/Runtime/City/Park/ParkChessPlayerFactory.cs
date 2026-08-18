using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Instantiates the one authored park chess player. There is no
    /// pool, no director and no spawn band: he keeps his plank for as
    /// long as the City lives — the watchman and fisherman pattern
    /// applied to the chess set.
    ///
    /// He also claims his seat. The chess benches are real sittable
    /// furniture: the hero can sit on them and `CityBenchNpcRestController`
    /// walks pedestrians over to rest on them. Claiming through the
    /// shared registry is what stops a walker lowering himself onto the
    /// man already there and takes the hero's prompt off that plank.
    ///
    /// Nothing is added into the staged prefab, which is validated
    /// passive with no collider, light, audio or interaction anywhere in
    /// it; whatever this factory raises hangs off its own root.
    /// </summary>
    public static class ParkChessPlayerFactory
    {
        public const string RuntimeRootName = "Park Chess Player";

        /// <summary>
        /// How far forward of the plank's middle he perches. A man
        /// reaching his elbows onto a board sits on the front half of
        /// the bench; parked dead centre he would be stretching, and the
        /// elbows this design was fitted with would miss the rim.
        /// </summary>
        public const float PerchForwardOffsetMeters = 0.06f;

        /// <summary>
        /// Focus height for the attention magnet, measured off the
        /// authored brooding pose rather than guessed. Folded over the
        /// board with his head in his hands he carries it far lower
        /// than the watchman's upright 1.34 m post would suggest.
        /// </summary>
        public const float FocusHeightMeters = 1.02f;

        // The talk stub is deliberately not built yet. When it is, it
        // docks behind him the way the fisherman's does - he faces the
        // table, so an in-front dock would put the box out over the
        // board where nobody can stand - and it is a separate object on
        // this root, never a component added into the staged art.
        public const float TriggerSpan = 1.7f;
        public const float TriggerHeight = 1.8f;
        public const float TriggerReach = 1.5f;
        public const float TriggerBackOffMeters = 0.75f;

        public static ParkChessPlayerPresentation Create(
            Transform parent,
            ParkChessPlayerPlan plan)
        {
            return Create(
                parent,
                plan,
                ParkChessPlayerProvider.Load());
        }

        public static ParkChessPlayerPresentation Create(
            Transform parent,
            ParkChessPlayerPlan plan,
            ParkChessPlayerProvider provider)
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
                    "park_chess_player_provider_missing");
                return null;
            }

            ParkChessPlayerStance stance = plan.Stance;
            Vector3 facing = stance.Facing;
            facing.y = 0f;
            facing = facing.sqrMagnitude > 0.0001f
                ? facing.normalized
                : Vector3.forward;

            Transform root = new GameObject(RuntimeRootName).transform;
            root.SetParent(parent, false);

            GameObject instance = UnityEngine.Object.Instantiate(
                provider.StagedPrefab,
                root);
            instance.name = "Chess Player";

            // Horizontal placement only carries the perch across the
            // plank; the height is corrected every frame by the
            // presentation, which stands the seat of his coat on the
            // drawn timber whatever the lawn under it is doing.
            instance.transform.SetPositionAndRotation(
                stance.SeatTopCenter +
                    (facing * PerchForwardOffsetMeters),
                Quaternion.LookRotation(facing, Vector3.up));

            var registry = instance
                .GetComponentInChildren<CityPedestrianAssetRegistry>(
                    true);
            if (registry == null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                throw new InvalidOperationException(
                    "The staged chess player prefab requires a " +
                    nameof(CityPedestrianAssetRegistry) + ".");
            }

            ValidatePassivePresentation(instance);

            var presentation = instance
                .AddComponent<ParkChessPlayerPresentation>();
            presentation.Initialize(registry, stance);

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
                    "park_chess_player_seat_taken",
                    GameLog.Field("seat_id", stance.SeatId));
            }

            GameLog.Info(
                "city",
                "park_chess_player_spawned",
                GameLog.Field("position_x", stance.SeatTopCenter.x),
                GameLog.Field("position_z", stance.SeatTopCenter.z));
            return presentation;
        }

        /// <summary>
        /// The staged prefab is authored passive on purpose.
        /// Instantiating it must not smuggle in physics, audio, light or
        /// interaction.
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
                    "The staged chess player presentation must stay passive.");
            }
        }
    }
}
