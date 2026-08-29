using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The decision a driver keeps making all the way down the road: ease
    /// off, or drive.
    ///
    /// The give-way beside it (<see cref="LastRouteCarGiveWayModel"/>) is one
    /// decision at one authored line - the only place the car leaves its own
    /// lane. This one is everything else a driver does with his right foot:
    /// the bus dwelling in his lane ahead, the bus sweeping a junction he is
    /// coming up on, a walker stepping off the kerb. It is told where the
    /// nearest conflict on his own road currently is - or
    /// <see cref="float.PositiveInfinity"/> when there is none - and answers
    /// with the distance to hand <see cref="LastRouteCarDriveModel.SetHold"/>.
    ///
    /// Pure, hand-stepped, and shaped on the give-way model because its three
    /// hard-won rules are this model's rules too:
    ///
    /// **A driver who cannot stop does not try.** A conflict discovered
    /// nearer than the car's own braking distance is driven through, whatever
    /// it is. The alternative is a car braked to a standstill ACROSS the
    /// junction it could not stop short of - standing in the one lane the bus
    /// will not stop for, which is how two vehicles that each behave
    /// reasonably lock a street. Stopping short or not at all is what keeps
    /// the wait graph between this car and Route 01 acyclic.
    ///
    /// **He wants to see it clear for a moment.** The bus is sensed off its
    /// instantaneous heading, which rotates through a turn and can flick the
    /// sweep out of the corridor for a frame; one clear frame is not a gap.
    ///
    /// **And he never waits forever.** The one ride out of the city cannot be
    /// allowed to soft-lock on a bus that never moves or a walker wedged in
    /// the road; the wait is capped past the longest lawful dwell, the car
    /// goes, and the log says it did. After a waited-out release the same
    /// still-standing conflict may not re-arm - without that the car brakes
    /// straight back into the thing it just decided to pass, forever.
    /// </summary>
    public sealed class LastRouteCarTrafficYieldModel
    {
        /// <summary>The give-way's own margin, for the same rounding reason.
        /// </summary>
        public const float CommitMarginMeters =
            LastRouteCarGiveWayModel.CommitMarginMeters;

        /// <summary>The give-way's own settle, for the same flicker reason.
        /// </summary>
        public const float SettleSeconds =
            LastRouteCarGiveWayModel.SettleSeconds;

        /// <summary>
        /// The longest he will trail his foot on the brake at a standstill.
        /// The longest lawful stationary bus is a `10 s` dwell plus up to
        /// `5 s` of service hold; this outlasts that with the settle and a
        /// margin on top, and unlike the give-way's cap it only counts while
        /// the car is actually STOPPED - following a slow bus spends none of
        /// it.
        /// </summary>
        public const float MaximumWaitSeconds = 18f;

        private float clearSeconds = SettleSeconds;
        private float waitedSeconds;
        private float lastHoldDistance = float.PositiveInfinity;
        private bool waitedOut;

        /// <summary>He is easing off for something right now.</summary>
        public bool IsYielding { get; private set; }

        /// <summary>He gave up waiting and is driving through; stays up
        /// until the road ahead has read clear once.</summary>
        public bool IsWaitedOut => waitedOut;

        /// <summary>How long the current standstill has lasted, for the log.
        /// </summary>
        public float WaitedSeconds => waitedSeconds;

        /// <summary>
        /// One step. <paramref name="conflictHoldDistance"/> is where along
        /// the road the car should stand to be short of the nearest conflict,
        /// or <see cref="float.PositiveInfinity"/> when the road ahead is
        /// his. Returns the distance to hand the drive model.
        /// </summary>
        public float Advance(
            float deltaTime,
            float carDistance,
            float carSpeed,
            float braking,
            float conflictHoldDistance)
        {
            float step = Mathf.Max(0f, Sanitize(deltaTime));
            float distance = Sanitize(carDistance);
            float speed = Mathf.Max(0f, Sanitize(carSpeed));
            bool clear = float.IsPositiveInfinity(conflictHoldDistance) ||
                         float.IsNaN(conflictHoldDistance);

            if (clear)
            {
                clearSeconds += step;
                if (clearSeconds >= SettleSeconds)
                {
                    // Genuinely gone, not flickered away: stand down, and a
                    // waited-out driver gets his patience back.
                    IsYielding = false;
                    waitedOut = false;
                    waitedSeconds = 0f;
                    lastHoldDistance = float.PositiveInfinity;
                    return float.PositiveInfinity;
                }

                // Inside the settle the last hold stands. It is what keeps
                // the car from surging at a bus whose heading blinked out of
                // the corridor for two frames of its own turn.
                return waitedOut ? float.PositiveInfinity : lastHoldDistance;
            }

            clearSeconds = 0f;
            if (waitedOut)
            {
                // He has already decided this one is never moving and is
                // going round the decision, not back into it.
                return float.PositiveInfinity;
            }

            float toHold = conflictHoldDistance - distance;
            float stopping = braking > 0.0001f
                ? (speed * speed) / (2f * braking)
                : 0f;

            // Past the point of no return: stopping now means stopping
            // INSIDE the conflict, in the lane the other vehicle will not
            // yield in. Drive through.
            if (toHold < stopping - CommitMarginMeters)
            {
                IsYielding = false;
                lastHoldDistance = float.PositiveInfinity;
                return float.PositiveInfinity;
            }

            if (speed <= LastRouteCarDriveModel.StoppedSpeed)
            {
                waitedSeconds += step;
                if (waitedSeconds >= MaximumWaitSeconds)
                {
                    waitedOut = true;
                    IsYielding = false;
                    lastHoldDistance = float.PositiveInfinity;
                    return float.PositiveInfinity;
                }
            }

            IsYielding = true;
            lastHoldDistance = conflictHoldDistance;
            return conflictHoldDistance;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
