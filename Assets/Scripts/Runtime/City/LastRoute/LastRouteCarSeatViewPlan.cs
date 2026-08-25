using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the hero's eyes are once he is in the Ferryman's passenger
    /// seat, and how far he may turn his head without leaving it.
    ///
    /// This is the park chess planks' arrangement rather than the bus's,
    /// and the reason is the glass. The bus puts the lens behind and
    /// inboard of its passenger so the shot is a man on a bus; that reads
    /// because a bus is a room. A saloon cabin is `1.4 m` across and the
    /// hero's own shoulder is `0.4 m` from the far window, so the same
    /// camera would be looking at the back of his own head through a door
    /// card. `CityBoardGameController` already owns the answer to that -
    /// sit the lens where his eyes are, take his head off while it is in
    /// there, and let his hands and knees stay in frame - and this is that
    /// arrangement pointed down a bonnet.
    ///
    /// The numbers are measured off the two rigs rather than chosen. The
    /// seat anchor IS the pelvis target - `LastRouteCarSeatPlan` docks the
    /// hero's hips onto it - and the car's roof was cut in
    /// `tools/build-last-route-car-3d-model.py` against this rig's seated
    /// head clearance band of `0.99-1.10 m` above that pelvis, which is
    /// what puts the eye line just under `0.80`.
    /// </summary>
    public static class LastRouteCarSeatViewPlan
    {
        /// <summary>
        /// How far above the seat anchor the lens sits.
        ///
        /// The anchor is a PELVIS target, not a cushion top, so this is the
        /// whole seated torso: the crown of a seated head lands `0.99-1.10`
        /// above it and the roof underside was then cut at `1.04`. An eye
        /// belongs a little below the crown, and every centimetre added
        /// here is a centimetre off the `0.24 m` of headroom the shot has
        /// left.
        /// </summary>
        public const float EyeHeightAboveSeat = 0.78f;

        /// <summary>
        /// And how far in front of it, along the car's own facing. Small,
        /// because a passenger's eye genuinely is a hand's breadth in
        /// front of his hips; large enough to clear his own chest and to
        /// put the dashboard's top edge low in the frame rather than
        /// across the middle of it.
        /// </summary>
        public const float EyeForwardMeters = 0.12f;

        /// <summary>
        /// Narrower than the board's `72`, which was solving for a `1.2 m`
        /// field seen from `0.4 m` away. Nothing in here is that close and
        /// the whole point of the seat is what is OUTSIDE it, so this is
        /// an ordinary interior lens - wide enough to hold both A-pillars,
        /// tight enough that the island through the windscreen is still a
        /// place rather than a fisheye.
        /// </summary>
        public const float FieldOfView = 62f;

        /// <summary>
        /// How far he may turn his head on the seat, and how far up and
        /// down.
        ///
        /// The yaw limit is wide on purpose and it is the Ferryman who
        /// sets it: he is sitting at the wheel roughly `0.9 m` to the
        /// hero's left with his cap almost level with the hero's own eye
        /// line, and a passenger who cannot turn and look at the driver is
        /// a passenger in a diorama. Past `105` degrees the shot is the
        /// rear bench and the door card behind him, so it stops there.
        /// </summary>
        public const float MaximumYawOffsetDegrees = 105f;
        public const float MinimumPitchDegrees = -34f;
        public const float MaximumPitchDegrees = 42f;

        /// <summary>Level. He is sitting in a car looking out of it, not
        /// studying anything on his knees - unlike the board, which has to
        /// derive its own resting pitch from the field it is framing.
        /// </summary>
        public const float BasePitchDegrees = 0f;

        /// <summary>
        /// Where the eyes are and what they look at, given how far he has
        /// turned his head off the windscreen.
        ///
        /// The seat point follows the sprung body - it is read off the
        /// car's own live anchor - while the view axes stay world level.
        /// The bus learned that one the hard way: axes taken off a rocking
        /// body couple mouse yaw into pitch and visibly tilt the horizon.
        /// </summary>
        public static void EvaluateCamera(
            Vector3 seatAnchor,
            Vector3 facing,
            float yawOffsetDegrees,
            float pitchDegrees,
            out Vector3 position,
            out Quaternion rotation)
        {
            Vector3 planar = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (planar.sqrMagnitude < 0.000001f)
            {
                throw new ArgumentException(
                    "The passenger view needs a facing with a horizontal " +
                    "component.",
                    nameof(facing));
            }

            planar = planar.normalized;
            position = seatAnchor +
                (Vector3.up * EyeHeightAboveSeat) +
                (planar * EyeForwardMeters);
            float yaw = Mathf.Clamp(
                yawOffsetDegrees,
                -MaximumYawOffsetDegrees,
                MaximumYawOffsetDegrees);
            float pitch = Mathf.Clamp(
                pitchDegrees,
                MinimumPitchDegrees,
                MaximumPitchDegrees);
            rotation = Quaternion.LookRotation(planar, Vector3.up) *
                Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
