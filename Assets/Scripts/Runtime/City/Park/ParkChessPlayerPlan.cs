using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Which plank the chess player is on and which way he looks:
    /// derived entirely from the chess set's own drawn seats, so the
    /// timber and the man on it can never disagree.
    /// </summary>
    public readonly struct ParkChessPlayerStance
    {
        public ParkChessPlayerStance(
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
    /// The authored population of the park chess set: one man at one of
    /// the two tables, with nobody across the board from him. Absent
    /// when the blueprint grows no park or the decoration planner could
    /// not fit the set.
    ///
    /// He takes the seat on the negative-forward side, which is the one
    /// facing the park centre. That is a decision about what the player
    /// is shown rather than about the furniture: the fisherman keeps his
    /// back to everyone who walks out on the pier and that is his whole
    /// character, but this design's content is a face with its head in
    /// its hands, and a man approached from behind would just be a coat.
    /// The lit pendant on the wire hangs over the same table.
    /// </summary>
    public sealed class ParkChessPlayerPlan
    {
        /// <summary>
        /// The suffix the chess recipe gives the seat at the table on
        /// the negative tangent, on the negative-forward side. Restated
        /// here rather than rebuilt, because the recipe owns the ids.
        /// </summary>
        public const string SeatIdSuffix = "-seat-a1";

        private static readonly ParkChessPlayerPlan AbsentPlan =
            new ParkChessPlayerPlan(default, false);

        private ParkChessPlayerPlan(
            ParkChessPlayerStance stance,
            bool isPresent)
        {
            Stance = stance;
            IsPresent = isPresent;
        }

        public ParkChessPlayerStance Stance { get; }
        public bool IsPresent { get; }

        public static ParkChessPlayerPlan Create(
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

                    return new ParkChessPlayerPlan(
                        new ParkChessPlayerStance(
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
