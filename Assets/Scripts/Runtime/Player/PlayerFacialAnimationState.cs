using UnityEngine;

namespace BarPromenade
{
    public enum PlayerFacialExpression
    {
        Neutral = 0,
        HalfBlink = 1,
        ClosedBlink = 2
    }

    /// <summary>
    /// Advances a deterministic, frame-rate independent blink schedule.
    /// The state owns timing only; the sprite rig chooses the matching
    /// direction-specific body frame.
    /// </summary>
    public sealed class PlayerFacialAnimationState
    {
        public const float InitialBlinkDelaySeconds = 4.2f;
        public const float HalfBlinkDurationSeconds = 0.04f;
        public const float ClosedBlinkDurationSeconds = 0.07f;
        public const float BlinkDurationSeconds =
            (HalfBlinkDurationSeconds * 2f) +
            ClosedBlinkDurationSeconds;

        private static readonly float[] BlinkIntervalsSeconds =
        {
            4.8f,
            5.7f,
            3.9f,
            6.2f,
            4.4f
        };

        private float elapsedSeconds;
        private float nextBlinkStartSeconds;
        private int completedBlinkCount;

        public PlayerFacialExpression CurrentExpression
        {
            get;
            private set;
        }

        public PlayerFacialAnimationState()
        {
            Reset();
        }

        public PlayerFacialExpression Advance(float deltaTime)
        {
            elapsedSeconds += Mathf.Max(0f, deltaTime);
            float blinkEnd =
                nextBlinkStartSeconds + BlinkDurationSeconds;

            while (elapsedSeconds >= blinkEnd)
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
                elapsedSeconds - nextBlinkStartSeconds;
            if (blinkTime < 0f)
            {
                CurrentExpression =
                    PlayerFacialExpression.Neutral;
            }
            else if (blinkTime < HalfBlinkDurationSeconds)
            {
                CurrentExpression =
                    PlayerFacialExpression.HalfBlink;
            }
            else if (blinkTime <
                     HalfBlinkDurationSeconds +
                     ClosedBlinkDurationSeconds)
            {
                CurrentExpression =
                    PlayerFacialExpression.ClosedBlink;
            }
            else
            {
                CurrentExpression =
                    PlayerFacialExpression.HalfBlink;
            }

            return CurrentExpression;
        }

        public void Reset()
        {
            elapsedSeconds = 0f;
            nextBlinkStartSeconds = InitialBlinkDelaySeconds;
            completedBlinkCount = 0;
            CurrentExpression = PlayerFacialExpression.Neutral;
        }
    }
}
