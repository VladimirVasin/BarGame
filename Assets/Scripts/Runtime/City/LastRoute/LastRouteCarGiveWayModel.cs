using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The decision a driver makes once, at one line: wait, or go.
    ///
    /// Pure, like <see cref="LastRouteCarDriveModel"/> beside it. It is told
    /// where the car is, how fast, how hard it can brake and whether the way
    /// across is clear, and it answers with the distance the car must not
    /// pass - the line while it is holding, and
    /// <see cref="float.PositiveInfinity"/> the moment it has gone.
    ///
    /// Three rules, and the order of them is the whole behaviour.
    ///
    /// **A driver who cannot stop does not try.** Once the line is nearer
    /// than the car's own braking distance the decision is over and the car
    /// goes, whatever is coming. Standing on the brakes through a line you
    /// are already over is worse than taking the turn, and the alternative -
    /// clamping the car where it stands - reads as the game stuttering.
    ///
    /// **He wants to see it clear for a moment**, not for a frame. A bus that
    /// has only just cleared the corner is still in the way of the swing, and
    /// a walker sampled between two footfalls can read as clear once.
    ///
    /// **And he never waits forever.** This is the one ride out of the city
    /// and the hero is sitting in the passenger seat; a pedestrian who stalls
    /// on the kerb must not be able to end the game. The wait is capped, the
    /// car goes, and the log says it did.
    /// </summary>
    public sealed class LastRouteCarGiveWayModel
    {
        /// <summary>
        /// How much nearer than its own braking distance the line has to be
        /// before the driver counts himself committed.
        ///
        /// It cannot be zero. A car braking at the model's own limit sits at
        /// exactly `toLine == stoppingDistance` the whole way down, so a
        /// commit test without a margin flickers on rounding alone and the
        /// car walks through its own stop line.
        /// </summary>
        public const float CommitMarginMeters = 1.5f;

        /// <summary>How long the way has to stay clear before he pulls
        /// out.</summary>
        public const float SettleSeconds = 0.4f;

        /// <summary>
        /// How far back, ON TOP of his own braking distance, the crossing
        /// starts being his problem.
        ///
        /// Without it a bus crossing the turn while the car is still a block
        /// away spends the wait budget on nothing, and the clock that stops
        /// him waiting forever would have run out before he ever arrived.
        /// </summary>
        public const float AttentionMeters = 12f;

        /// <summary>
        /// The longest he will ever sit there. The bus dwells `10 s` at a
        /// stop, so this is comfortably past one bus and short enough that a
        /// walker wedged on the kerb costs a pause rather than the beat.
        /// </summary>
        public const float MaximumWaitSeconds = 15f;

        private readonly float lineDistance;
        private float clearSeconds;
        private float waitedSeconds;
        private bool hasHeldBack;

        public LastRouteCarGiveWayModel(float lineDistance)
        {
            this.lineDistance = Sanitize(lineDistance);
        }

        /// <summary>Where the stop line is, in metres along the road.
        /// </summary>
        public float LineDistance => lineDistance;

        /// <summary>The decision is made and will not be revisited.</summary>
        public bool IsCommitted { get; private set; }

        /// <summary>He is giving way right now.</summary>
        public bool IsGivingWay { get; private set; }

        /// <summary>How long he has been held up, for the log.</summary>
        public float WaitedSeconds => waitedSeconds;

        /// <summary>Why he went, for the log. Empty until he has.</summary>
        public string CommitReason { get; private set; } = string.Empty;

        /// <summary>
        /// One step. Returns the distance to hand
        /// <see cref="LastRouteCarDriveModel.SetHold"/>.
        /// </summary>
        public float Advance(
            float deltaTime,
            float carDistance,
            float carSpeed,
            float braking,
            bool wayIsClear)
        {
            if (IsCommitted)
            {
                return float.PositiveInfinity;
            }

            float step = Mathf.Max(0f, Sanitize(deltaTime));
            float toLine = lineDistance - Sanitize(carDistance);
            float speed = Mathf.Max(0f, Sanitize(carSpeed));
            float stopping = braking > 0.0001f
                ? (speed * speed) / (2f * braking)
                : 0f;

            if (toLine > stopping + AttentionMeters)
            {
                // Still a street away. Nothing seen up there counts yet, and
                // in particular nothing seen up there is worth waiting for.
                IsGivingWay = false;
                clearSeconds = 0f;
                waitedSeconds = 0f;
                return float.PositiveInfinity;
            }

            if (wayIsClear)
            {
                clearSeconds += step;
            }
            else
            {
                clearSeconds = 0f;
                waitedSeconds += step;
            }

            // Past the point of no return, whatever the road looks like.
            if (toLine < stopping - CommitMarginMeters)
            {
                return Commit("too_late");
            }

            bool settled = clearSeconds >= SettleSeconds;

            // Clear for a beat, and near enough that going is the turn itself
            // rather than a guess made from up the road.
            if (settled && toLine <= stopping + CommitMarginMeters)
            {
                return Commit("clear");
            }

            if (waitedSeconds >= MaximumWaitSeconds)
            {
                return Commit("waited_out");
            }

            if (settled || (wayIsClear && !hasHeldBack))
            {
                // Free to run at the line - either it has been clear long
                // enough to trust, or it is clear and has never once made him
                // stop - and free to be put down again on the way in if
                // something pulls out.
                //
                // The second half is what keeps an empty road from costing
                // the car a single metre per second: without it the line
                // brakes every car for a settle it does not need. It must
                // ask whether the way is clear as well as whether he has
                // stopped before, or a crossing that is blocked the whole way
                // in never stops him at all - which is the entire point of
                // the line.
                IsGivingWay = false;
                return float.PositiveInfinity;
            }

            IsGivingWay = true;
            hasHeldBack = true;
            return lineDistance;
        }

        private float Commit(string reason)
        {
            IsCommitted = true;
            IsGivingWay = false;
            CommitReason = reason;
            return float.PositiveInfinity;
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
