using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the sun stands as seen from inside the church.
    ///
    /// The interior is a scene of its own and the model is placed in it
    /// at identity (ChurchInteriorWorldBuilder), so nothing in here
    /// shares the City's compass. The sun the clock hands out is a
    /// WORLD rotation, and used raw it lights whichever wall the
    /// interior's untransformed axes happen to point at - which is how
    /// the old lighting came to fire both aisles at once, at the same
    /// brightness, at every hour.
    ///
    /// Everything that answers "is this window lit, how brightly, and
    /// which way does its shaft run" comes from here, so the light, the
    /// beams and the glass cannot disagree with each other.
    /// </summary>
    public static class ChurchInteriorSunRules
    {
        /// <summary>
        /// The quarter turn between the two frames.
        ///
        /// CityChurchPlan requires the church's street frontage to face
        /// west and puts the altar along the access normal, world +X
        /// (east); the interior model's own +Z is the altar. So local
        /// +Z is world east, and local +X is world SOUTH. Pinned by
        /// ChurchInteriorSunTests, which derives it from the same
        /// Vector3.right the city planner enforces rather than
        /// repeating this number.
        /// </summary>
        public const float InteriorYawFromWorldDegrees = -90f;

        /// <summary>
        /// The +X aisle wall is the south one, so its inward normal
        /// points along -X. A wall takes direct sun only when the
        /// light travels INTO the room through it.
        /// </summary>
        public const float SouthWallSide = 1f;
        public const float NorthWallSide = -1f;

        /// <summary>
        /// Below this the sun is too square-on to the wall to be worth
        /// drawing; above it the window is fully lit. The band is wide
        /// enough that the handover at the crossing hour is a dissolve
        /// rather than a switch.
        /// </summary>
        public const float FacingFadeStart = 0.06f;
        public const float FacingFadeEnd = 0.30f;

        /// <summary>
        /// The church's light is BAKED at one pose. It does not track
        /// the sun across the day.
        ///
        /// The pose is the sun at its own solar noon: due south, at the
        /// top of its arc. Chosen by measurement rather than taste -
        /// a pose with any lean ALONG the nave lets each window's light
        /// slip past the piers and run the length of the building, and
        /// the room's mean brightness goes from 71 to 104 with the
        /// columns dissolving into a general wash. Square-on, each
        /// lancet's light stays in its own bay, the nave stays dark,
        /// and the five columns read as five columns.
        ///
        /// Everything about the hour still lives: the light warms and
        /// dies with the clock, and the shafts are simply there while
        /// it is day and gone once it is not.
        /// </summary>
        public static Quaternion BakedWorldSun =>
            GameTimeDayNightRules.SunRotationAt(
                GameTimeDayNightRules.SolarNoonMinutes);

        public static Quaternion InteriorFromWorld =>
            Quaternion.Euler(0f, InteriorYawFromWorldDegrees, 0f);

        /// <summary>The baked pose, in the interior's own axes.</summary>
        public static Quaternion BakedInteriorSun =>
            ToInteriorLocal(BakedWorldSun);

        /// <summary>
        /// The one direction every daylight consumer in this room uses.
        /// </summary>
        public static Vector3 BakedLocalTravel =>
            BakedInteriorSun * Vector3.forward;

        /// <summary>
        /// The rotation the interior's own directional light needs so
        /// that it stands where the City's sun stands.
        /// </summary>
        public static Quaternion ToInteriorLocal(
            Quaternion worldSunRotation)
        {
            return InteriorFromWorld * worldSunRotation;
        }

        /// <summary>
        /// The direction sunlight TRAVELS, in interior-local axes.
        /// </summary>
        public static Vector3 LocalTravelDirection(
            Quaternion worldSunRotation)
        {
            return ToInteriorLocal(worldSunRotation) * Vector3.forward;
        }

        /// <summary>
        /// How squarely the sun faces a wall, from -1 to 1. Positive
        /// means the light enters through it. This is the Dot that the
        /// church has never had: with it, one aisle is the sun wall and
        /// the other is not, which is the whole reading of a basilica.
        /// </summary>
        public static float WallFacing(
            float wallSideX,
            Vector3 localTravel)
        {
            if (localTravel.y >= 0f)
            {
                // The sun is on or under the horizon; nothing enters
                // any window, whatever the compass says.
                return 0f;
            }

            return -Mathf.Sign(wallSideX) * localTravel.x;
        }

        /// <summary>
        /// The single weight every daylight consumer shares: the light,
        /// the beam, the mote and the glass all fade together because
        /// they all read this.
        ///
        /// With the pose baked there is only one variable left, and it
        /// is the clock. A wall either faces the baked sun or it never
        /// does; the shafts are there while it is day and gone when it
        /// is not, and the hour either side of each is the ramp.
        /// </summary>
        public static float WindowWeight(
            float wallSideX,
            float dayFactor)
        {
            float facing = WallFacing(wallSideX, BakedLocalTravel);
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    FacingFadeStart,
                    FacingFadeEnd,
                    facing)) * Mathf.Clamp01(dayFactor);
        }

        /// <summary>
        /// How far the light runs from an aperture at
        /// <paramref name="apertureHeight"/> before it reaches the
        /// floor. Grazing sun would run to infinity, so it is bounded.
        /// </summary>
        public static float FloorThrow(
            float apertureHeight,
            Vector3 localTravel,
            float shortest,
            float longest)
        {
            float descent = -localTravel.y;
            if (descent <= 0.0001f)
            {
                return longest;
            }

            return Mathf.Clamp(
                apertureHeight / descent,
                shortest,
                longest);
        }

        /// <summary>
        /// Where a window's pool actually lands. The test that proves
        /// the shafts move across the day reads this, and so does the
        /// one that proves they land on floor a person walks on.
        /// </summary>
        public static Vector3 FloorPool(
            Vector3 apertureCentre,
            Vector3 localTravel,
            float floorY = 0f)
        {
            float descent = -localTravel.y;
            if (descent <= 0.0001f)
            {
                return apertureCentre;
            }

            float distance = (apertureCentre.y - floorY) / descent;
            return apertureCentre + (localTravel * distance);
        }
    }
}
