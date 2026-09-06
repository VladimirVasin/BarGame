using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where everything in the shower scene stands, in room-local metres
    /// (the room is parented at identity, so these are world metres too).
    ///
    /// The scene is seen from the hero's own eyes from the moment E is
    /// pressed, so there is no authored shot to frame against any more;
    /// what remains is the stall's geometry. The stall is curtained on its
    /// left and half its front, so the front-right opening beside the
    /// gathered curtain is the one way in and out; the dock sits at the
    /// back of the tray under the water; the palms press the bathroom's
    /// own back tile; the drips fall from the nozzle plate into the basin;
    /// and the hot cross handle is where the right hand goes to shut the
    /// water off.
    /// </summary>
    public static class HomeShowerFraming
    {
        /// <summary>The only way into and out of the stall: the front-right opening beside the gathered curtain.</summary>
        public static readonly Vector3 Waypoint = new Vector3(4.26f, 0f, 2.28f);

        /// <summary>Where he washes: the back of the tray, offset from the head so the crown clears the bell.</summary>
        public static readonly Vector3 Dock = new Vector3(3.88f, 0f, 3.28f);

        /// <summary>Where he stands when the camera leaves his head: in the opening, facing the room.</summary>
        public static readonly Vector3 Exit = new Vector3(4.26f, 0f, 2.28f);

        /// <summary>Where the prompt measures from.</summary>
        public static readonly Vector3 Stand = new Vector3(3.55f, 0f, 2.60f);

        /// <summary>The bathroom's back tile: its front face.</summary>
        public const float WallZ = 3.857f;

        /// <summary>Both palms on the tile, a centimetre proud of it, at shoulder height on the tray.</summary>
        public static readonly Vector3 LeftPalm = new Vector3(3.66f, 1.55f, 3.847f);
        public static readonly Vector3 RightPalm = new Vector3(4.10f, 1.55f, 3.847f);

        /// <summary>The mixer and riser sit on the tile directly ahead, between the braced hands.</summary>
        public static readonly Vector3 Mixer = new Vector3(Dock.x, 1.24f, WallZ - 0.08f);

        /// <summary>Just under the nozzle plate "Home Bathroom Shower Head Face", ahead of the dock.</summary>
        public static readonly Vector3 DripOrigin = new Vector3(Dock.x, 1.99f, 3.42f);

        /// <summary>Where a drop meets the basin.</summary>
        public static readonly Vector3 BasinLanding = new Vector3(DripOrigin.x, 0.225f, DripOrigin.z);

        /// <summary>The top of the hot cross handle the right hand closes on.</summary>
        public static readonly Vector3 HotHandleGrip = Mixer + new Vector3(0.07f, 0.025f, -0.06f);

        public const float WaypointArrivalRadius = 0.06f;

        /// <summary>The hero's CharacterController radius, as PlayerFactory builds it.</summary>
        public const float CapsuleRadius = 0.32f;

        /// <summary>
        /// The stall's footprint on the floor. From inside it a straight
        /// line to the dock crosses no curtain; from anywhere else the
        /// walk goes through the opening first, and so does the way out.
        /// </summary>
        public static bool IsInsideStall(Vector3 heroPosition)
        {
            return heroPosition.x > 3.03f && heroPosition.z > 2.30f;
        }
    }
}
