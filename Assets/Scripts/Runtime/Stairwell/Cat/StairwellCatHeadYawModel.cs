using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure continuous head tracking, replacing the three discrete
    /// atlas rows of the sprite cat. It keeps the old selector's
    /// hysteresis idea re-expressed in angle space: the head only
    /// starts turning once the error is worth the effort, then turns
    /// cat-fast until it settles - no servo-smooth perpetual chase.
    /// </summary>
    public sealed class StairwellCatHeadYawModel
    {
        public const float DefaultMaxTrackYawDegrees = 65f;
        public const float DefaultEnterErrorDegrees = 8f;
        public const float DefaultSettleErrorDegrees = 2f;
        public const float DefaultTurnDegreesPerSecond = 240f;

        private readonly float maxTrackYawDegrees;
        private readonly float enterErrorDegrees;
        private readonly float settleErrorDegrees;
        private readonly float turnDegreesPerSecond;
        private bool turning;

        public StairwellCatHeadYawModel(
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

        public float Update(float targetYawDegrees, float deltaTime)
        {
            if (float.IsNaN(targetYawDegrees) ||
                float.IsInfinity(targetYawDegrees) ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return CurrentYawDegrees;
            }

            float target = Mathf.Clamp(
                targetYawDegrees,
                -maxTrackYawDegrees,
                maxTrackYawDegrees);
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
