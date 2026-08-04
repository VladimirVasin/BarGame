using UnityEngine;

namespace BarPromenade
{
    public enum PlayerFacialExpression
    {
        Neutral = 0,
        HalfBlink = 1,
        ClosedBlink = 2,
        Watchful = 3,
        Tense = 4
    }

    /// <summary>
    /// Advances deterministic, frame-rate independent blink and idle
    /// expression schedules. The state owns timing only; the active player
    /// presentation chooses how the expression is rendered.
    /// </summary>
    public sealed class PlayerFacialAnimationState
    {
        public const float InitialBlinkDelaySeconds = 2.8f;
        public const float HalfBlinkDurationSeconds = 0.055f;
        public const float ClosedBlinkDurationSeconds = 0.12f;
        public const float BlinkDurationSeconds =
            (HalfBlinkDurationSeconds * 2f) +
            ClosedBlinkDurationSeconds;
        public const float InitialWatchfulDelaySeconds = 1.6f;
        public const float WatchfulDurationSeconds = 0.75f;
        public const float InitialTenseDelaySeconds = 4.7f;
        public const float TenseDurationSeconds = 0.95f;

        private static readonly float[] BlinkIntervalsSeconds =
        {
            3.6f,
            4.9f,
            0.3f,
            5.4f,
            4.1f
        };

        private static readonly float[] WatchfulIntervalsSeconds =
        {
            4.6f,
            5.5f,
            4.2f,
            6.1f
        };

        private static readonly float[] TenseIntervalsSeconds =
        {
            7.8f,
            9.2f,
            8.4f,
            10.1f
        };

        private float blinkElapsedSeconds;
        private float nextBlinkStartSeconds;
        private int completedBlinkCount;
        private float idleElapsedSeconds;
        private float nextWatchfulStartSeconds;
        private int completedWatchfulCount;
        private float nextTenseStartSeconds;
        private int completedTenseCount;

        public PlayerFacialExpression CurrentExpression
        {
            get;
            private set;
        }

        public PlayerFacialAnimationState()
        {
            Reset();
        }

        public PlayerFacialExpression Advance(
            float deltaTime,
            bool allowIdleExpressions = true)
        {
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            blinkElapsedSeconds += safeDeltaTime;
            PlayerFacialExpression blinkExpression =
                GetBlinkExpression();

            if (!allowIdleExpressions)
            {
                ResetIdleExpressionSchedule();
                CurrentExpression = blinkExpression;
                return CurrentExpression;
            }

            idleElapsedSeconds += safeDeltaTime;
            bool tenseActive = IsIdleExpressionActive(
                ref nextTenseStartSeconds,
                ref completedTenseCount,
                TenseDurationSeconds,
                TenseIntervalsSeconds);
            bool watchfulActive = IsIdleExpressionActive(
                ref nextWatchfulStartSeconds,
                ref completedWatchfulCount,
                WatchfulDurationSeconds,
                WatchfulIntervalsSeconds);

            if (blinkExpression != PlayerFacialExpression.Neutral)
            {
                CurrentExpression = blinkExpression;
            }
            else if (tenseActive)
            {
                CurrentExpression = PlayerFacialExpression.Tense;
            }
            else if (watchfulActive)
            {
                CurrentExpression = PlayerFacialExpression.Watchful;
            }
            else
            {
                CurrentExpression = PlayerFacialExpression.Neutral;
            }

            return CurrentExpression;
        }

        public void Reset()
        {
            blinkElapsedSeconds = 0f;
            nextBlinkStartSeconds = InitialBlinkDelaySeconds;
            completedBlinkCount = 0;
            ResetIdleExpressionSchedule();
            CurrentExpression = PlayerFacialExpression.Neutral;
        }

        private PlayerFacialExpression GetBlinkExpression()
        {
            float blinkEnd =
                nextBlinkStartSeconds + BlinkDurationSeconds;

            while (blinkElapsedSeconds >= blinkEnd)
            {
                float interval = BlinkIntervalsSeconds[
                    completedBlinkCount %
                    BlinkIntervalsSeconds.Length];
                completedBlinkCount++;
                nextBlinkStartSeconds = blinkEnd + interval;
                blinkEnd =
                    nextBlinkStartSeconds + BlinkDurationSeconds;
            }

            float blinkTime =
                blinkElapsedSeconds - nextBlinkStartSeconds;
            if (blinkTime < 0f)
            {
                return PlayerFacialExpression.Neutral;
            }

            if (blinkTime < HalfBlinkDurationSeconds)
            {
                return PlayerFacialExpression.HalfBlink;
            }

            if (blinkTime <
                HalfBlinkDurationSeconds +
                ClosedBlinkDurationSeconds)
            {
                return PlayerFacialExpression.ClosedBlink;
            }

            return PlayerFacialExpression.HalfBlink;
        }

        private bool IsIdleExpressionActive(
            ref float nextStartSeconds,
            ref int completedCount,
            float durationSeconds,
            float[] intervalsSeconds)
        {
            float endSeconds = nextStartSeconds + durationSeconds;
            while (idleElapsedSeconds >= endSeconds)
            {
                float interval = intervalsSeconds[
                    completedCount % intervalsSeconds.Length];
                completedCount++;
                nextStartSeconds = endSeconds + interval;
                endSeconds = nextStartSeconds + durationSeconds;
            }

            return idleElapsedSeconds >= nextStartSeconds;
        }

        private void ResetIdleExpressionSchedule()
        {
            idleElapsedSeconds = 0f;
            nextWatchfulStartSeconds = InitialWatchfulDelaySeconds;
            completedWatchfulCount = 0;
            nextTenseStartSeconds = InitialTenseDelaySeconds;
            completedTenseCount = 0;
        }
    }
}
