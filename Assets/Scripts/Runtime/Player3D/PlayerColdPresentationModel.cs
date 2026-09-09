using System;

namespace BarPromenade
{
    /// <summary>
    /// One externally advanced clock for the cold pose and its visible breath.
    /// No scene, input or unscaled time is read by the presentation model.
    /// </summary>
    public sealed class PlayerColdPresentationModel
    {
        public const float BreathCycleSeconds = 4f;
        public const float ShoulderRubDurationSeconds = 2.5f;
        public const float FirstShoulderRubSeconds = 1.8f;
        public const float ShiverDurationSeconds = 1f;
        public const float FirstShiverSeconds = 5.75f;
        public const float EquippedScarfShiverScale = 0.5f;

        private const float RubBlendSeconds = 0.2f;
        private const float ShiverBlendSeconds = 0.12f;
        private const double IntervalSequenceSeconds = 36.25d;
        private static readonly double[] RubStartIntervals =
            { 5.5d, 4.25d, 6d, 4.75d, 5d, 6.25d, 4.5d };
        // Shares the rub schedule's period, with every one-second shiver
        // placed inside a gap. No gesture is delayed or queued by the other.
        private static readonly double[] ShiverStartIntervals =
            { 8.75d, 5.75d, 5.25d, 5.5d, 5.25d, 5.75d };

        private double secondsUntilRub = FirstShoulderRubSeconds;
        private double rubElapsedSeconds = -1d;
        private int intervalIndex;
        private double secondsUntilShiver = FirstShiverSeconds;
        private double shiverElapsedSeconds = -1d;
        private int shiverIntervalIndex;

        public double ElapsedSeconds { get; private set; }
        public float BreathPhase01 =>
            (float)((ElapsedSeconds % BreathCycleSeconds) /
                    BreathCycleSeconds);
        public bool IsRubbing => rubElapsedSeconds >= 0d;
        public float RubNormalizedTime => IsRubbing
            ? (float)(rubElapsedSeconds / ShoulderRubDurationSeconds)
            : 0f;
        public bool IsShivering => shiverElapsedSeconds >= 0d;
        public float ShiverNormalizedTime => IsShivering
            ? (float)(shiverElapsedSeconds / ShiverDurationSeconds)
            : 0f;
        public float ShiverWeight01 => GestureWeight(
            shiverElapsedSeconds, ShiverDurationSeconds, ShiverBlendSeconds);

        public float GetShiverWeight(bool scarfEquipped) => ShiverWeight01 *
            (scarfEquipped ? EquippedScarfShiverScale : 1f);

        public float ExhaleEnvelope01
        {
            get
            {
                double progress = (BreathPhase01 - 0.35d) / 0.4d;
                return progress > 0d && progress < 1d
                    ? (float)Math.Sin(progress * Math.PI)
                    : 0f;
            }
        }

        public float RubWeight01 => GestureWeight(
            rubElapsedSeconds, ShoulderRubDurationSeconds, RubBlendSeconds);

        private static float GestureWeight(double elapsed, float duration, float blendSeconds)
        {
            if (elapsed < 0d) return 0f;
            double edge = Math.Min(elapsed, duration - elapsed);
            double blend = Math.Min(1d, Math.Max(0d, edge / blendSeconds));
            return (float)(blend * blend * (3d - 2d * blend));
        }

        public void Step(float deltaTime, bool allowRub = true)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            ElapsedSeconds += deltaTime;
            if (!allowRub)
            {
                // Protective actions own the hands and defer both gestures.
                // Their elapsed time must not queue gestures on release.
                rubElapsedSeconds = -1d;
                shiverElapsedSeconds = -1d;
                return;
            }

            AdvanceGesture(deltaTime, RubStartIntervals, ShoulderRubDurationSeconds,
                ref secondsUntilRub, ref rubElapsedSeconds, ref intervalIndex);
            AdvanceGesture(deltaTime, ShiverStartIntervals, ShiverDurationSeconds,
                ref secondsUntilShiver, ref shiverElapsedSeconds, ref shiverIntervalIndex);
        }

        private static void AdvanceGesture(double deltaTime, double[] intervals, float duration,
            ref double untilStart, ref double elapsed, ref int nextInterval)
        {
            if (elapsed >= 0d)
            {
                elapsed += deltaTime;
            }

            untilStart -= deltaTime;
            if (untilStart <= -IntervalSequenceSeconds)
            {
                // Skip whole repeating schedules after an unusually large
                // externally supplied step without iterating once per gesture.
                untilStart += Math.Floor(
                    -untilStart / IntervalSequenceSeconds) *
                    IntervalSequenceSeconds;
            }

            while (untilStart <= 0d)
            {
                elapsed = -untilStart;
                untilStart += intervals[nextInterval];
                nextInterval = (nextInterval + 1) % intervals.Length;
            }

            if (elapsed >= duration)
            {
                elapsed = -1d;
            }
        }

        public void Reset()
        {
            ElapsedSeconds = 0d;
            secondsUntilRub = FirstShoulderRubSeconds;
            rubElapsedSeconds = -1d;
            intervalIndex = 0;
            secondsUntilShiver = FirstShiverSeconds;
            shiverElapsedSeconds = -1d;
            shiverIntervalIndex = 0;
        }
    }
}
