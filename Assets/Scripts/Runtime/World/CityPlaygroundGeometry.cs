using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one set of numbers the park playground is made of. The baked
    /// frame recipe, the logical collision proxies, the bench seat offer
    /// and the hanging swings all read from here, so a seat cannot drift
    /// out of the frame it hangs in, and the arc it sweeps cannot drift
    /// into the bench it faces.
    ///
    /// Local axes are the decoration recipe's: +Z is the descriptor
    /// forward (the bench side, facing the park centre), X runs along
    /// the top beam, Y is height over the decoration's ground.
    /// </summary>
    internal static class CityPlaygroundGeometry
    {
        public const float PostOffsetX = 2.05f;
        public const float PostOffsetZ = 0.80f;
        public const float PostThickness = 0.18f;
        public const float PostHeight = 2.86f;

        // Each pair of posts is capped by its own cross beam along the
        // frame depth; the long beam then lands on top of both. Without
        // them the frame was four loose legs holding one bar.
        public const float CrossBeamY = 2.85f;
        public const float CrossBeamThickness = 0.22f;
        public const float CrossBeamDepth =
            (PostOffsetZ * 2f) + (PostThickness * 2f);

        public const float TopBeamY = 3.06f;
        public const float TopBeamWidth = 4.60f;
        public const float TopBeamThickness = 0.22f;

        /// <summary>The underside of the top beam: where a rope is
        /// tied, and the axis the seat below it swings around.</summary>
        public const float RopeAnchorY =
            TopBeamY - (TopBeamThickness * 0.5f);

        public const float SwingOffsetX = 0.98f;
        public const float RopeOffsetX = 0.30f;
        public const float RopeThickness = 0.05f;
        public const float SeatCenterY = 0.62f;
        public const float SeatWidth = 0.72f;
        public const float SeatThickness = 0.10f;
        public const float SeatDepth = 0.36f;
        public const float RopeLength = RopeAnchorY - SeatCenterY;

        /// <summary>How far the hinge lets the seat rise. A child's
        /// swing tops out well before the horizontal; the limit is also
        /// what keeps the arc off the bench and off whoever is walking
        /// past it.</summary>
        public const float SwingLimitDegrees = 50f;

        /// <summary>The plank is thin, but shins are not: the solid box
        /// the hero walks into is the plank grown to knee height.
        /// </summary>
        public const float SeatColliderHeight = 0.34f;

        // The volume that answers "someone is pushing this". Wide
        // enough that the hero's capsule is inside it while his own
        // collider still touches the plank.
        public const float PushVolumeWidth = SeatWidth + 0.38f;
        public const float PushVolumeHeight = 1.30f;
        public const float PushVolumeDepth = SeatDepth + 0.78f;

        /// <summary>The bench sits this far in front of the frame: far
        /// enough that a swing at its limit, and the hero docked to sit
        /// down, never share the same metre.</summary>
        public const float BenchZ = 3.60f;

        /// <summary>How far from the frame centre the swinging plank's
        /// leading face reaches at the joint limit.</summary>
        public static float SeatReach =>
            (Mathf.Sin(SwingLimitDegrees * Mathf.Deg2Rad) * RopeLength) +
            (SeatDepth * 0.5f);
    }
}
