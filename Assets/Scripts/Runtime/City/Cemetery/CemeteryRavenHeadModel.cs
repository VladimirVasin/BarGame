using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure hysteretic head tracking, cloned from the stairwell cat's
    /// yaw model rather than reused so bird tuning never edits the
    /// cat: the head only starts turning once the error is worth the
    /// effort, then turns fast until it settles — no servo-smooth
    /// perpetual chase. On top of the cat's numbers the raven adds a
    /// distance cutoff the cat deliberately lacks: past
    /// <see cref="MaxTrackDistanceMeters"/> the target counts as gone
    /// and the head comes back to neutral, because a bird visibly
    /// following a man far into the fog is a bird leading him, and
    /// these two lead nobody. Target selection stays with the
    /// director — this model only takes what it is handed.
    /// </summary>
    public sealed class CemeteryRavenHeadModel
    {
        public const float DefaultMaxTrackYawDegrees = 65f;
        public const float DefaultEnterErrorDegrees = 8f;
        public const float DefaultSettleErrorDegrees = 2f;
        public const float DefaultTurnDegreesPerSecond = 240f;
        public const float MaxTrackDistanceMeters = 18f;

        private readonly float maxTrackYawDegrees;
        private readonly float enterErrorDegrees;
        private readonly float settleErrorDegrees;
        private readonly float turnDegreesPerSecond;
        private bool turning;

        public CemeteryRavenHeadModel(
            float maxTrackYawDegrees = DefaultMaxTrackYawDegrees,
            float enterErrorDegrees = DefaultEnterErrorDegrees,
            float settleErrorDegrees = DefaultSettleErrorDegrees,
            float turnDegreesPerSecond = DefaultTurnDegreesPerSecond)
        {
            this.maxTrackYawDegrees =
                Mathf.Max(0f, maxTrackYawDegrees);
            this.enterErrorDegrees = Mathf.Max(0f, enterErrorDegrees);
            this.settleErrorDegrees = Mathf.Clamp(
                settleErrorDegrees,
                0f,
                this.enterErrorDegrees);
            this.turnDegreesPerSecond =
                Mathf.Max(0f, turnDegreesPerSecond);
        }

        public float CurrentYawDegrees { get; private set; }
        public bool IsTurning => turning;

        /// <summary>
        /// One step of tracking. A missing target — none at all, or
        /// one beyond the cutoff — is not "hold the last pose": it
        /// becomes a neutral target, so the head drifts home through
        /// the same hysteresis instead of freezing mid-stare.
        /// </summary>
        public float Update(
            bool hasTarget,
            float targetDistanceMeters,
            float targetYawDegrees,
            float deltaTime)
        {
            if (float.IsNaN(targetYawDegrees) ||
                float.IsInfinity(targetYawDegrees) ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return CurrentYawDegrees;
            }

            bool tracking =
                hasTarget &&
                !float.IsNaN(targetDistanceMeters) &&
                !float.IsInfinity(targetDistanceMeters) &&
                targetDistanceMeters <= MaxTrackDistanceMeters;
            float target = tracking
                ? Mathf.Clamp(
                    targetYawDegrees,
                    -maxTrackYawDegrees,
                    maxTrackYawDegrees)
                : 0f;
            float error = Mathf.Abs(
                Mathf.DeltaAngle(CurrentYawDegrees, target));
            if (!turning && error > enterErrorDegrees)
            {
                turning = true;
            }

            if (turning)
            {
                CurrentYawDegrees = Mathf.MoveTowardsAngle(
                    CurrentYawDegrees,
                    target,
                    turnDegreesPerSecond * deltaTime);
                if (Mathf.Abs(
                        Mathf.DeltaAngle(
                            CurrentYawDegrees,
                            target)) <=
                    settleErrorDegrees)
                {
                    turning = false;
                }
            }

            return CurrentYawDegrees;
        }

        public void Reset()
        {
            CurrentYawDegrees = 0f;
            turning = false;
        }
    }
}
