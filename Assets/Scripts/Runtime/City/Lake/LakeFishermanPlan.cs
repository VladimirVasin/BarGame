using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the fisherman sits and which way he looks: derived
    /// entirely from the lake plan's own pier parts, so the drawn boards
    /// and the man on them can never disagree. He keeps the end of the
    /// мостки, on the side without a rail, with his back to the shore.
    /// </summary>
    public readonly struct LakeFishermanStance
    {
        public LakeFishermanStance(
            Vector3 position,
            Vector3 facing,
            int paletteVariant,
            float playbackSpeed,
            float phaseOffsetSeconds)
        {
            Position = position;
            Facing = facing;
            PaletteVariant = paletteVariant;
            PlaybackSpeed = playbackSpeed;
            PhaseOffsetSeconds = phaseOffsetSeconds;
        }

        public Vector3 Position { get; }
        public Vector3 Facing { get; }
        public int PaletteVariant { get; }
        public float PlaybackSpeed { get; }
        public float PhaseOffsetSeconds { get; }
    }

    /// <summary>
    /// The authored population of the boat station: one man on the end
    /// of the pier who has not turned round in some time. Absent when
    /// the blueprint has no lake or the lake plan carries no pier.
    /// </summary>
    public sealed class LakeFishermanPlan
    {
        /// <summary>How far back from the head of the deck he sits:
        /// far enough that the head boards are in front of him rather
        /// than under him.</summary>
        public const float SeatInsetMeters = 0.85f;

        /// <summary>And how far off the deck's centre line, toward the
        /// side without a rail — the side a person can actually put
        /// their legs over.</summary>
        public const float SeatSideOffsetMeters = 0.28f;

        private static readonly LakeFishermanPlan AbsentPlan =
            new LakeFishermanPlan(default, false);

        private LakeFishermanPlan(
            LakeFishermanStance stance,
            bool isPresent)
        {
            Stance = stance;
            IsPresent = isPresent;
        }

        public LakeFishermanStance Stance { get; }
        public bool IsPresent { get; }

        public static LakeFishermanPlan Create(CityLakePlan lakePlan)
        {
            if (lakePlan == null)
            {
                return AbsentPlan;
            }

            if (!lakePlan.TryGetPart(
                    CityLakePlanner.PierDeckHeadId,
                    out CityLakePartDescriptor head) ||
                !lakePlan.TryGetPart(
                    CityLakePlanner.PierDeckRootId,
                    out CityLakePartDescriptor root))
            {
                return AbsentPlan;
            }

            // Out along the pier, away from the bank. Flattened,
            // because the deck is level and a tilt here would lean him.
            Vector3 outward = head.Center - root.Center;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                return AbsentPlan;
            }

            outward = outward.normalized;
            Vector3 sideways = Vector3.Cross(Vector3.up, outward);

            // The rail runs down one side; the planner puts it on the
            // deck's negative lateral edge, so he sits toward the other.
            float deckTop = head.Center.y + head.Size.y * 0.5f;
            Vector3 position =
                new Vector3(head.Center.x, deckTop, head.Center.z) -
                outward * SeatInsetMeters +
                sideways * SeatSideOffsetMeters;

            // Facing the water. This is the whole character: the player
            // arrives behind him and he does not turn round.
            return new LakeFishermanPlan(
                new LakeFishermanStance(
                    position,
                    outward,
                    2,
                    1f,
                    0f),
                true);
        }
    }
}
