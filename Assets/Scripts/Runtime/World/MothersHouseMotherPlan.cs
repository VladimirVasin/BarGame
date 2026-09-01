using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the mother sits, in the room's own coordinates. Pure data, like
    /// every other plan in this project: no MonoBehaviour, no scene lookup,
    /// nothing a test cannot build in one line.
    ///
    /// There is exactly one of these and it takes no seed. She is not a
    /// population, not a spawn band and not a schedule - she is a fact about
    /// the room, and a planner that could place her twice or not at all would
    /// be describing a different room.
    /// </summary>
    public sealed class MothersHouseMotherPlan
    {
        /// <summary>
        /// The cushion's own centre in X. The drawn cushion runs from
        /// `-0.27` to `+0.31`, which is not symmetric about the chair - the
        /// upholstery was authored with a little more room on her left - so
        /// this is measured from the art rather than assumed to be zero.
        /// </summary>
        public const float SeatX = 0.02f;

        /// <summary>
        /// How far back on the cushion her hips sit. The cushion spans
        /// `1.26` to `1.80` in Z and the backrest closes it at `1.80`; she is
        /// settled back against it rather than perched on the front edge,
        /// which is the difference between a woman resting and a woman about
        /// to stand up.
        /// </summary>
        public const float SeatZ = 1.62f;

        /// <summary>
        /// Where in the rock she is when the room opens. Not zero: at zero
        /// the chair is level and motionless for the first instant, and the
        /// first thing the hero sees should already be moving. A quarter
        /// period puts it at the top of a lean, where the motion is slowest
        /// and reads most clearly as a rocking chair rather than a glitch.
        /// </summary>
        public const float InitialPhaseSeconds =
            MothersHouseRockingChairMotion.PeriodSeconds * 0.25f;

        public MothersHouseMotherPlan(
            Vector3 seatPosition,
            Vector3 facing,
            float initialPhaseSeconds)
        {
            SeatPosition = seatPosition;
            Facing = facing.sqrMagnitude > 0.0001f
                ? facing.normalized
                : Vector3.back;
            InitialPhase = initialPhaseSeconds;
        }

        /// <summary>Her hips, in room-local space, on the drawn cushion.
        /// </summary>
        public Vector3 SeatPosition { get; }

        /// <summary>
        /// The chair faces the room - the low table, the door, the way in -
        /// with its back to the hearth. She faces where it faces; a rocking
        /// chair does not let its sitter choose.
        /// </summary>
        public Vector3 Facing { get; }

        public float InitialPhase { get; }

        public static MothersHouseMotherPlan Create()
        {
            return new MothersHouseMotherPlan(
                new Vector3(SeatX, 0f, SeatZ),
                Vector3.back,
                InitialPhaseSeconds);
        }
    }
}
