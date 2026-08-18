using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Which plank the checkers player is on and which way he looks:
    /// derived entirely from the chess set's own drawn seats, so the
    /// timber and the man on it can never disagree.
    /// </summary>
    public readonly struct ParkCheckersPlayerStance
    {
        public ParkCheckersPlayerStance(
            string seatId,
            Vector3 seatTopCenter,
            Vector3 facing,
            int paletteVariant,
            float playbackSpeed,
            float phaseOffsetSeconds)
        {
            SeatId = seatId;
            SeatTopCenter = seatTopCenter;
            Facing = facing;
            PaletteVariant = paletteVariant;
            PlaybackSpeed = playbackSpeed;
            PhaseOffsetSeconds = phaseOffsetSeconds;
        }

        /// <summary>
        /// The seat he holds, in the shared bench registry's own id
        /// space. He claims it for the lifetime of the City so neither
        /// the hero nor a resting walker is offered his lap.
        /// </summary>
        public string SeatId { get; }

        /// <summary>Middle of the drawn plank, at its top face.</summary>
        public Vector3 SeatTopCenter { get; }

        public Vector3 Facing { get; }
        public int PaletteVariant { get; }
        public float PlaybackSpeed { get; }
        public float PhaseOffsetSeconds { get; }
    }

    /// <summary>
    /// The second man at the park chess set: the other table, the seat
    /// that is his neighbour's rotated 180 degrees about the middle of
    /// the set. Absent when the blueprint grows no park or the
    /// decoration planner could not fit the set.
    ///
    /// The recipe draws four planks and only that one satisfies both
    /// halves of what this design is for. <c>-seat-b1</c> is at the
    /// other table but on the same side facing the same way, which
    /// seats the two of them shoulder to shoulder looking in one
    /// direction. <c>-seat-a2</c> is across the old man's own board,
    /// which is the one thing §10 forbids outright. <c>-seat-b2</c> is
    /// the other table AND the far side of it, so the two men are
    /// turned toward one another with the whole set between them and
    /// each still has an empty plank across his own board.
    ///
    /// What that buys is worth stating exactly, because it is easy to
    /// overclaim. Their two facings are antiparallel and offset by the
    /// 3.70 m between the tables, so each man sits about 59 degrees off
    /// the other's own axis — in front of him, plainly, but never
    /// looked at. Both are folded over their own boards with their
    /// heads in their hands anyway. Two old men turned to face each
    /// other across four metres of lawn, neither of them looking up,
    /// each with the seat opposite him empty, and not even playing the
    /// same game. That is §10 doubled, not spent.
    /// </summary>
    public sealed class ParkCheckersPlayerPlan
    {
        /// <summary>
        /// The suffix the chess recipe gives the seat at the table on
        /// the positive tangent, on the positive-forward side. Restated
        /// here rather than rebuilt, because the recipe owns the ids.
        /// </summary>
        public const string SeatIdSuffix = "-seat-b2";

        private static readonly ParkCheckersPlayerPlan AbsentPlan =
            new ParkCheckersPlayerPlan(default, false);

        private ParkCheckersPlayerPlan(
            ParkCheckersPlayerStance stance,
            bool isPresent)
        {
            Stance = stance;
            IsPresent = isPresent;
        }

        public ParkCheckersPlayerStance Stance { get; }
        public bool IsPresent { get; }

        public static ParkCheckersPlayerPlan Create(
            CityLayout layout,
            CityDecorationPlan decorations)
        {
            if (layout == null || decorations == null)
            {
                return AbsentPlan;
            }

            var seats = new List<CityBenchSeat>(4);
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                seats.Clear();
                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    descriptor,
                    seats);
                string wanted = descriptor.StableId + SeatIdSuffix;
                for (int seat = 0; seat < seats.Count; seat++)
                {
                    CityBenchSeat candidate = seats[seat];
                    if (!candidate.IsPresent ||
                        !string.Equals(
                            candidate.Id,
                            wanted,
                            System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Palette variant 1, the same as his neighbour's,
                    // and that is a requirement rather than a copy. The
                    // prefab build shifts every authored colour per
                    // variant, so the light circle on this coat only
                    // lands on the light square of the other coat if
                    // both are read through the same shift. The two
                    // men are told apart by shape; the one value they
                    // share must stay shared.
                    //
                    // Speed and phase are left at the authored defaults
                    // because CheckersMull already carries its own
                    // rhythm — a different breath depth and a settle at
                    // a different point in the lap. Two loops that
                    // differ in content do not need to be pushed out of
                    // step as well.
                    return new ParkCheckersPlayerPlan(
                        new ParkCheckersPlayerStance(
                            candidate.Id,
                            candidate.SeatTopCenter,
                            candidate.FaceDirection,
                            1,
                            1f,
                            0f),
                        true);
                }
            }

            return AbsentPlan;
        }
    }
}
