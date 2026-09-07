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
        public const float FirstShoulderRubSeconds = 10f;

        private const float RubBlendSeconds = 0.2f;
        private const double IntervalSequenceSeconds = 77d;
        private static readonly double[] RubStartIntervals =
            { 13d, 9d, 12d, 8d, 14d, 11d, 10d };

        private double secondsUntilRub = FirstShoulderRubSeconds;
        private double rubElapsedSeconds = -1d;
        private int intervalIndex;

        public double ElapsedSeconds { get; private set; }
        public float BreathPhase01 =>
            (float)((ElapsedSeconds % BreathCycleSeconds) /
                    BreathCycleSeconds);
        public bool IsRubbing => rubElapsedSeconds >= 0d;
        public float RubNormalizedTime => IsRubbing
            ? (float)(rubElapsedSeconds / ShoulderRubDurationSeconds)
            : 0f;

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

        public float RubWeight01
        {
            get
            {
                if (!IsRubbing)
                {
                    return 0f;
                }

                double edge = Math.Min(
                    rubElapsedSeconds,
                    ShoulderRubDurationSeconds - rubElapsedSeconds);
                double blend = Math.Min(1d, Math.Max(0d, edge / RubBlendSeconds));
                return (float)(blend * blend * (3d - 2d * blend));
            }
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
                // Running releases the hands and defers the next gesture.
                // Its elapsed time must not queue several gestures on stopping.
                rubElapsedSeconds = -1d;
                return;
            }

            if (IsRubbing)
            {
                rubElapsedSeconds += deltaTime;
            }

            secondsUntilRub -= deltaTime;
            if (secondsUntilRub <= -IntervalSequenceSeconds)
            {
                // Skip whole repeating schedules after an unusually large
                // externally supplied step without iterating once per gesture.
                secondsUntilRub += Math.Floor(
                    -secondsUntilRub / IntervalSequenceSeconds) *
                    IntervalSequenceSeconds;
            }

            while (secondsUntilRub <= 0d)
            {
                rubElapsedSeconds = -secondsUntilRub;
                secondsUntilRub += RubStartIntervals[intervalIndex];
                intervalIndex = (intervalIndex + 1) % RubStartIntervals.Length;
            }

            if (rubElapsedSeconds >= ShoulderRubDurationSeconds)
            {
                rubElapsedSeconds = -1d;
            }
        }

        public void Reset()
        {
            ElapsedSeconds = 0d;
            secondsUntilRub = FirstShoulderRubSeconds;
            rubElapsedSeconds = -1d;
            intervalIndex = 0;
        }
    }
}
