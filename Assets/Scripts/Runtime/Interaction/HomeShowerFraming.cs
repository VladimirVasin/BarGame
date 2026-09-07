using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where everything in the shower scene stands, in room-local metres
    /// (the room is parented at identity, so these are world metres too).
    ///
    /// The front curtain is opened by hand before crossing its plane;
    /// the left run stays closed to the tile. The front-right opening is
    /// the one way in and out. The wash dock leaves the feet behind the
    /// head and shoulders, leaning toward the water; both palms brace
    /// at the back tile, with the right arm above the soap sightline;
    /// the drips fall from the nozzle plate into the basin;
    /// each hand opens and closes its own cross handle, returning to the
    /// wall before the other hand moves.
    /// </summary>
    public static class HomeShowerFraming
    {
        /// <summary>The only way into and out of the stall: the front-right opening beside the gathered curtain.</summary>
        public static readonly Vector3 Waypoint = new Vector3(4.26f, 0f, 2.28f);

        /// <summary>Where he washes: the back of the tray, offset from the head so the crown clears the bell.</summary>
        public static readonly Vector3 Dock = new Vector3(3.88f, 0f, 3.08f);

        /// <summary>Leave facing the room: the toes point away from the curtain while the camera follows.</summary>
        public static readonly Vector3 ExitCameraDock = HomeShowerCurtainPose.OutsideDock;
        public static readonly Quaternion ExitCameraFacing = HomeShowerCurtainPose.InsideFacing;

        /// <summary>The authored outside closing gesture is the terminal gameplay pose.</summary>
        public static readonly Vector3 Exit = HomeShowerCurtainPose.OutsideDock;

        /// <summary>Where the prompt measures from.</summary>
        public static readonly Vector3 Stand = new Vector3(3.55f, 0f, 2.60f);

        /// <summary>The bathroom's back tile: its front face.</summary>
        public const float WallZ = 3.857f;

        /// <summary>The left palm's tile brace, at shoulder height on the tray.</summary>
        public static readonly Vector3 LeftPalm = new Vector3(3.66f, 1.55f, 3.847f);

        /// <summary>The right palm's higher tile brace leaves the soap shelf visible below the arm.</summary>
        public static readonly Vector3 RightPalm = new Vector3(4.16f, 1.65f, 3.847f);

        /// <summary>The mixer and riser sit on the tile directly ahead.</summary>
        public static readonly Vector3 Mixer = new Vector3(Dock.x, 1.24f, WallZ - 0.08f);

        public const float HeadPitchDegrees = 20f;
        public static readonly Quaternion HeadRotation = Quaternion.Euler(HeadPitchDegrees, 0f, 0f);
        private const float HeadShiftTowardsWall = 0.09f;
        // The old neck's centreline meets the horizontal arm here. Rotate
        // the existing three meshes together about that joint, then shorten
        // the arm toward the wall; their joins retain their original overlap.
        private static readonly Vector3 PreviousHeadJoint = new Vector3(Dock.x, 2.17f,
            3.485f + 0.06f * Mathf.Tan(35f * Mathf.Deg2Rad));
        public static readonly Vector3 HeadJoint = PreviousHeadJoint + Vector3.forward * HeadShiftTowardsWall;
        public static readonly Vector3 HeadArmEnd = new Vector3(Dock.x, 2.17f, 3.485f + HeadShiftTowardsWall);
        public static readonly Vector3 HeadNeckCenter = TiltHeadPart(new Vector3(Dock.x, 2.11f, 3.485f));
        public static readonly Vector3 HeadBodyCenter = TiltHeadPart(new Vector3(Dock.x, 2.045f, 3.445f));
        public static readonly Vector3 HeadFaceCenter = TiltHeadPart(new Vector3(Dock.x, 2.005f, 3.42f));
        public static readonly Vector3 StreamDirection = HeadRotation * Vector3.down;

        /// <summary>Two millimetres outside the tilted plate's 12 mm half-height.</summary>
        public static readonly Vector3 DripOrigin = HeadFaceCenter + StreamDirection * 0.014f;

        /// <summary>The existing basin landing stays fixed when the head is redirected.</summary>
        public static readonly Vector3 BasinLanding = new Vector3(Dock.x, 0.225f, 3.42f);

        private static Vector3 TiltHeadPart(Vector3 original) => HeadJoint +
            Quaternion.Euler(HeadPitchDegrees - 35f, 0f, 0f) * (original - PreviousHeadJoint);

        /// <summary>Cross-wheel bases sit on the mixer, using the sink's fixed-metre handle.</summary>
        public static readonly Vector3 HotHandlePivot = Mixer + new Vector3(0.07f, 0.05f, -0.03f);
        public static readonly Vector3 ColdHandlePivot = Mixer + new Vector3(-0.07f, 0.05f, -0.03f);

        /// <summary>Zero-turn hand points from FaucetHandle/HandGrip; each live hand follows its wheel rotation.</summary>
        public static readonly Vector3 HotHandleGrip = HotHandlePivot + new Vector3(-0.025f, 0.023f, -0.025f);
        public static readonly Vector3 ColdHandleGrip = ColdHandlePivot + new Vector3(-0.025f, 0.023f, -0.025f);

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
            return heroPosition.x > 3.35f && heroPosition.z > 2.384f;
        }
    }
}
