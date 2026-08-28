using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// How a fixed-grip line starts and stops.
    ///
    /// The whole reason this exists as a distance function rather than a timer
    /// is that a cabin has to come to rest ON the boarding point, not near it:
    /// the hero's dock refuses a seat more than a couple of centimetres out of
    /// place, and it refuses it silently. A constant-deceleration profile
    /// reaches zero exactly where the remaining distance does, so the arrival
    /// is a consequence of the maths instead of a snap at the end.
    /// </summary>
    public static class MountainCablewayDriveRules
    {
        /// <summary>
        /// How much rope runs through the station while the line comes to
        /// rest. About seven seconds at the authored cabin speed - long
        /// enough to read as a machine being brought down rather than a
        /// switch being thrown.
        /// </summary>
        public const float BrakeDistance = 7.5f;

        /// <summary>
        /// Shorter than the brake: an empty line picks up faster than a
        /// loaded one is set down.
        /// </summary>
        public const float LaunchDistance = 5f;

        /// <summary>
        /// The slowest the line ever turns while getting under way, as a
        /// fraction of cruise. See <see cref="EvaluateLaunchSpeed"/> for why a
        /// distance-driven ramp cannot be allowed to reach zero.
        /// </summary>
        public const float MinimumLaunchFraction = 0.08f;

        /// <summary>
        /// Below this the cabin is treated as docked. It is an order of
        /// magnitude under the `2 cm` dock tolerance, so the seat plan never
        /// sees the difference.
        /// </summary>
        public const float DockEpsilon = 0.002f;

        /// <summary>
        /// The ceiling on how far a dock request may make the line run. A
        /// cabin that has only just left has to go all the way round, and
        /// this is what says how long that is allowed to be.
        /// </summary>
        public const float MinimumApproachDistance = 1.5f;

        /// <summary>
        /// Speed while closing on the dock: `v = sqrt(2 a d)` capped at
        /// cruise, with `a` chosen so cruise is reached exactly one brake
        /// distance out.
        /// </summary>
        public static float EvaluateApproachSpeed(
            float remainingDistance,
            float cruiseSpeed,
            float brakeDistance = BrakeDistance)
        {
            if (remainingDistance <= 0f || cruiseSpeed <= 0f)
            {
                return 0f;
            }

            float brake = Mathf.Max(0.01f, brakeDistance);
            if (remainingDistance >= brake)
            {
                return cruiseSpeed;
            }

            return cruiseSpeed * Mathf.Sqrt(remainingDistance / brake);
        }

        /// <summary>
        /// Speed while getting under way again, by how far the line has run
        /// since the lever went over.
        /// </summary>
        public static float EvaluateLaunchSpeed(
            float travelledSinceStart,
            float cruiseSpeed,
            float launchDistance = LaunchDistance)
        {
            if (cruiseSpeed <= 0f)
            {
                return 0f;
            }

            float launch = Mathf.Max(0.01f, launchDistance);
            if (travelledSinceStart >= launch)
            {
                return cruiseSpeed;
            }

            // The floor is not styling, it is what lets the line start at
            // all. This ramp is a function of DISTANCE RUN, so a speed of
            // zero at zero distance is a fixed point: the rope never moves,
            // the distance never grows, and the line sits there for ever
            // looking like a stuck dock.
            float fraction = Mathf.Sqrt(
                Mathf.Max(0f, travelledSinceStart) / launch);
            return cruiseSpeed *
                   Mathf.Max(MinimumLaunchFraction, fraction);
        }

        /// <summary>
        /// How far a cabin at <paramref name="cabinDistance"/> must travel to
        /// reach <paramref name="dockDistance"/>.
        ///
        /// A cabin already sitting on the point is sent round the whole loop
        /// rather than declared arrived: the offer is "the next one", and a
        /// line that stops the instant you ask reads as a cheat.
        /// </summary>
        public static float EvaluateApproachDistance(
            float cabinDistance,
            float dockDistance,
            float loopLength)
        {
            float remaining = Mathf.Repeat(
                dockDistance - cabinDistance,
                loopLength);
            return remaining < MinimumApproachDistance
                ? remaining + loopLength
                : remaining;
        }
    }
}
