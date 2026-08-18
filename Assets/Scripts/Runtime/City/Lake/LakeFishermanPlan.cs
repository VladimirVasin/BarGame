using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the fisherman stands and which way he looks: derived
    /// entirely from the lake plan's own pier parts, so the drawn boards
    /// and the man on them can never disagree. He keeps the end of the
    /// мостки, on the side without a rail, with his back to the shore
    /// and his weight tipped out over the end board.
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
        /// <summary>How far back from the head of the deck he stands.
        /// Small on purpose: he is leaning on the end board, and a man
        /// standing a stride short of a parapet is not leaning on
        /// anything. His boots stop just clear of it.</summary>
        public const float StandInsetMeters = 0.05f;

        /// <summary>And how far off the deck's centre line, toward the
        /// side without a rail — the side a person can actually get
        /// their elbows onto.</summary>
        public const float SideOffsetMeters = 0.28f;

        private static readonly LakeFishermanPlan AbsentPlan =
            new LakeFishermanPlan(default, 0f, false);

        private LakeFishermanPlan(
            LakeFishermanStance stance,
            float waterTopY,
            bool isPresent)
        {
            Stance = stance;
            WaterTopY = waterTopY;
            IsPresent = isPresent;
        }

        public LakeFishermanStance Stance { get; }

        /// <summary>
        /// The waterline his float sits on, read straight off the lake's
        /// own basin. The line has to end in the water rather than at a
        /// guessed depth: the basin insets its waterline inside the
        /// blueprint cells, so nothing outside the plan knows where the
        /// surface is.
        /// </summary>
        public float WaterTopY { get; }

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
            // deck's negative lateral edge, so he stands toward the
            // other.
            float deckTop = head.Center.y + head.Size.y * 0.5f;
            Vector3 position =
                new Vector3(head.Center.x, deckTop, head.Center.z) -
                outward * StandInsetMeters +
                sideways * SideOffsetMeters;

            // Facing the water. This is the whole character: the player
            // arrives behind him and he does not turn round.
            return new LakeFishermanPlan(
                new LakeFishermanStance(
                    position,
                    outward,
                    2,
                    1f,
                    0f),
                lakePlan.Basin.WaterTopY,
                true);
        }
    }
}
