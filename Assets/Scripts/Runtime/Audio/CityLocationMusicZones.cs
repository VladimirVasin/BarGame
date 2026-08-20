using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The pure boundary test behind location music. A place takes the mix
    /// the moment the hero is inside its grounds, and keeps it until he is a
    /// clear margin outside them. Without that margin a walk along a fence
    /// line would flap the mix, and every flap costs a full fade-out and a
    /// fade-in.
    /// </summary>
    public static class CityLocationMusicZones
    {
        /// <summary>
        /// How far outside its grounds a place keeps the mix. Wide enough to
        /// cover a gate step and a pavement, short enough that leaving still
        /// reads as leaving.
        /// </summary>
        public const float ExitMarginMeters = 4f;

        /// <summary>No place owns the mix; the scene theme does.</summary>
        public const int NoLocationIndex = -1;

        public static int Resolve(
            IReadOnlyList<Rect> grounds,
            int activeIndex,
            Vector2 heroWorldXZ)
        {
            return Resolve(
                grounds,
                activeIndex,
                heroWorldXZ,
                ExitMarginMeters);
        }

        public static int Resolve(
            IReadOnlyList<Rect> grounds,
            int activeIndex,
            Vector2 heroWorldXZ,
            float exitMarginMeters)
        {
            ValidateMargin(exitMarginMeters);
            if (grounds == null || grounds.Count == 0)
            {
                return NoLocationIndex;
            }

            if (float.IsNaN(heroWorldXZ.x) ||
                float.IsNaN(heroWorldXZ.y) ||
                float.IsInfinity(heroWorldXZ.x) ||
                float.IsInfinity(heroWorldXZ.y))
            {
                return activeIndex >= 0 && activeIndex < grounds.Count
                    ? activeIndex
                    : NoLocationIndex;
            }

            if (activeIndex >= 0 &&
                activeIndex < grounds.Count &&
                Hold(grounds[activeIndex], exitMarginMeters)
                    .Contains(heroWorldXZ))
            {
                return activeIndex;
            }

            for (int index = 0; index < grounds.Count; index++)
            {
                if (grounds[index].Contains(heroWorldXZ))
                {
                    return index;
                }
            }

            return NoLocationIndex;
        }

        /// <summary>
        /// The grounds a place holds on to once it already owns the mix.
        /// </summary>
        public static Rect Hold(Rect grounds, float exitMarginMeters)
        {
            ValidateMargin(exitMarginMeters);
            if (grounds.width <= 0f || grounds.height <= 0f)
            {
                return grounds;
            }

            return Rect.MinMaxRect(
                grounds.xMin - exitMarginMeters,
                grounds.yMin - exitMarginMeters,
                grounds.xMax + exitMarginMeters,
                grounds.yMax + exitMarginMeters);
        }

        private static void ValidateMargin(float exitMarginMeters)
        {
            if (float.IsNaN(exitMarginMeters) ||
                float.IsInfinity(exitMarginMeters) ||
                exitMarginMeters < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exitMarginMeters),
                    exitMarginMeters,
                    "A location music hold margin must be finite and " +
                    "non-negative.");
            }
        }
    }
}
