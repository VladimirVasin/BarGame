using UnityEngine;

namespace BarPromenade
{
    public enum PlayerFacialExpression
    {
        Neutral = 0,
        HalfBlink = 1,
        ClosedBlink = 2,
        Watchful = 3,
        Tense = 4,

        /// <summary>Lids that will not stay up: the drink's first face.</summary>
        Drowsy = 5,

        /// <summary>Eyes open but not focusing, one lid lower than the other.</summary>
        Glazed = 6,

        /// <summary>The jaw hangs and one brow has let go: blind drunk.</summary>
        Slack = 7,

        /// <summary>The wince of a fall coming, or of the floor just met.</summary>
        Grimace = 8
    }

    /// <summary>
    /// Advances deterministic, frame-rate independent blink and idle
    /// expression schedules, and the drink's faces on top of them: the
    /// blinks grow heavy with the level, the resting face goes from
    /// neutral through drowsy spells to glazed and then slack, and the
    /// moment's mood — a strain, a wince, out cold — overrides the rest.
    /// The state owns timing only; the active player presentation
    /// chooses how the expression is rendered. Sober, every number is
    /// exactly what it was before the drink's faces existed.
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

        /// <summary>Blind drunk the lids close this slowly and stay shut this long; the blinks come a little more often.</summary>
        public const float DrunkHalfBlinkDurationSeconds = 0.12f;
        public const float DrunkClosedBlinkDurationSeconds = 0.30f;
        public const float DrunkBlinkIntervalScale = 0.8f;

        /// <summary>The resting face by level: drowsy spells from here, glazed from here, slack from here.</summary>
        public const float DrowsyLevel = 0.35f;
        public const float GlazedLevel = 0.6f;
        public const float SlackLevel = 0.85f;
        public const float InitialDrowsyDelaySeconds = 3.1f;
        public const float DrowsyDurationSeconds = 1.4f;

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

        private static readonly float[] DrowsyIntervalsSeconds =
        {
            5.2f,
            7.6f,
            4.4f,
            6.9f
        };

        private float blinkElapsedSeconds;
        private float nextBlinkStartSeconds;
        private int completedBlinkCount;
        private float idleElapsedSeconds;
        private float nextWatchfulStartSeconds;
        private int completedWatchfulCount;
        private float nextTenseStartSeconds;
        private int completedTenseCount;
        private float drowsyElapsedSeconds;
        private float nextDrowsyStartSeconds;
        private int completedDrowsyCount;

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
            bool allowIdleExpressions = true,
            float intoxication = 0f,
            PlayerFacialMood mood = PlayerFacialMood.None)
        {
            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            float level = Mathf.Clamp01(intoxication);
            blinkElapsedSeconds += safeDeltaTime;
            PlayerFacialExpression blinkExpression =
                GetBlinkExpression(level);

            if (mood == PlayerFacialMood.Out)
            {
                // Out cold: the eyes are shut and stay shut; no blink,
                // no idle face, and the idle clocks start over after.
                ResetIdleExpressionSchedule();
                CurrentExpression = PlayerFacialExpression.ClosedBlink;
                return CurrentExpression;
            }

            // The idle glances are the sober man's: from the drowsy level
            // up the drink's own faces take the resting slot.
            bool tenseActive = false;
            bool watchfulActive = false;
            if (!allowIdleExpressions || level >= DrowsyLevel)
            {
                ResetIdleExpressionSchedule();
            }
            else
            {
                idleElapsedSeconds += safeDeltaTime;
                tenseActive = IsIdleExpressionActive(
                    idleElapsedSeconds,
                    ref nextTenseStartSeconds,
                    ref completedTenseCount,
                    TenseDurationSeconds,
                    TenseIntervalsSeconds);
                watchfulActive = IsIdleExpressionActive(
                    idleElapsedSeconds,
                    ref nextWatchfulStartSeconds,
                    ref completedWatchfulCount,
                    WatchfulDurationSeconds,
                    WatchfulIntervalsSeconds);
            }

            if (blinkExpression != PlayerFacialExpression.Neutral)
            {
                CurrentExpression = blinkExpression;
            }
            else if (mood == PlayerFacialMood.Grimace)
            {
                CurrentExpression = PlayerFacialExpression.Grimace;
            }
            else if (mood == PlayerFacialMood.Tense)
            {
                CurrentExpression = PlayerFacialExpression.Tense;
            }
            else if (mood == PlayerFacialMood.Drowsy)
            {
                CurrentExpression = PlayerFacialExpression.Drowsy;
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
                CurrentExpression = GetLevelExpression(level, safeDeltaTime);
            }

            return CurrentExpression;
        }

        public void Reset()
        {
            blinkElapsedSeconds = 0f;
            nextBlinkStartSeconds = InitialBlinkDelaySeconds;
            completedBlinkCount = 0;
            ResetIdleExpressionSchedule();
            ResetDrowsySchedule();
            CurrentExpression = PlayerFacialExpression.Neutral;
        }

        /// <summary>How long the lids take to close (each way) and how long they stay shut, at this level.</summary>
        public static float HalfBlinkSeconds(float intoxication)
        {
            return Mathf.Lerp(
                HalfBlinkDurationSeconds,
                DrunkHalfBlinkDurationSeconds,
                Mathf.Clamp01(intoxication));
        }

        public static float ClosedBlinkSeconds(float intoxication)
        {
            return Mathf.Lerp(
                ClosedBlinkDurationSeconds,
                DrunkClosedBlinkDurationSeconds,
                Mathf.Clamp01(intoxication));
        }

        public static float BlinkSeconds(float intoxication)
        {
            return HalfBlinkSeconds(intoxication) * 2f +
                   ClosedBlinkSeconds(intoxication);
        }

        public static float BlinkIntervalScale(float intoxication)
        {
            return Mathf.Lerp(1f, DrunkBlinkIntervalScale, Mathf.Clamp01(intoxication));
        }

        /// <summary>The face the level leaves him when nothing else claims it.</summary>
        public static PlayerFacialExpression RestingExpression(float intoxication)
        {
            float level = Mathf.Clamp01(intoxication);
            if (level >= SlackLevel)
            {
                return PlayerFacialExpression.Slack;
            }

            if (level >= GlazedLevel)
            {
                return PlayerFacialExpression.Glazed;
            }

            return PlayerFacialExpression.Neutral;
        }

        private PlayerFacialExpression GetBlinkExpression(float level)
        {
            float blinkDuration = BlinkSeconds(level);
            float blinkEnd =
                nextBlinkStartSeconds + blinkDuration;

            while (blinkElapsedSeconds >= blinkEnd)
            {
                float interval = BlinkIntervalsSeconds[
                                     completedBlinkCount %
                                     BlinkIntervalsSeconds.Length] *
                                 BlinkIntervalScale(level);
                completedBlinkCount++;
                nextBlinkStartSeconds = blinkEnd + interval;
                blinkEnd =
                    nextBlinkStartSeconds + blinkDuration;
            }

            float blinkTime =
                blinkElapsedSeconds - nextBlinkStartSeconds;
            if (blinkTime < 0f)
            {
                return PlayerFacialExpression.Neutral;
            }

            float half = HalfBlinkSeconds(level);
            if (blinkTime < half)
            {
                return PlayerFacialExpression.HalfBlink;
            }

            if (blinkTime < half + ClosedBlinkSeconds(level))
            {
                return PlayerFacialExpression.ClosedBlink;
            }

            return PlayerFacialExpression.HalfBlink;
        }

        /// <summary>
        /// The level's own face: below the drowsy level nothing; from it,
        /// spells of heavy lids on their own clock (walking or not); and
        /// between the spells the level's resting face.
        /// </summary>
        private PlayerFacialExpression GetLevelExpression(float level, float deltaTime)
        {
            if (level < DrowsyLevel)
            {
                ResetDrowsySchedule();
                return PlayerFacialExpression.Neutral;
            }

            drowsyElapsedSeconds += deltaTime;
            bool drowsyActive = IsIdleExpressionActive(
                drowsyElapsedSeconds,
                ref nextDrowsyStartSeconds,
                ref completedDrowsyCount,
                DrowsyDurationSeconds,
                DrowsyIntervalsSeconds);
            return drowsyActive
                ? PlayerFacialExpression.Drowsy
                : RestingExpression(level);
        }

        private static bool IsIdleExpressionActive(
            float elapsedSeconds,
            ref float nextStartSeconds,
            ref int completedCount,
            float durationSeconds,
            float[] intervalsSeconds)
        {
            float endSeconds = nextStartSeconds + durationSeconds;
            while (elapsedSeconds >= endSeconds)
            {
                float interval = intervalsSeconds[
                    completedCount % intervalsSeconds.Length];
                completedCount++;
                nextStartSeconds = endSeconds + interval;
                endSeconds = nextStartSeconds + durationSeconds;
            }

            return elapsedSeconds >= nextStartSeconds;
        }

        private void ResetIdleExpressionSchedule()
        {
            idleElapsedSeconds = 0f;
            nextWatchfulStartSeconds = InitialWatchfulDelaySeconds;
            completedWatchfulCount = 0;
            nextTenseStartSeconds = InitialTenseDelaySeconds;
            completedTenseCount = 0;
        }

        private void ResetDrowsySchedule()
        {
            drowsyElapsedSeconds = 0f;
            nextDrowsyStartSeconds = InitialDrowsyDelaySeconds;
            completedDrowsyCount = 0;
        }
    }
}
