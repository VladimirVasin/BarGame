using System;

namespace BarPromenade
{
    public enum SplitTheGPhase
    {
        Countdown = 0,
        Armed,
        Drinking,
        Settling,
        AttemptResult,
        FinalResult
    }

    public enum SplitTheGResultBand
    {
        Perfect = 0,
        Excellent,
        Good,
        Close,
        Miss
    }

    public enum SplitTheGLevelDirection
    {
        OnTarget = 0,
        UnderDrank,
        OverDrank
    }

    public readonly struct SplitTheGAttemptResult
    {
        internal SplitTheGAttemptResult(
            int attemptNumber,
            double targetLevel,
            double finalLevel,
            int score,
            SplitTheGResultBand band,
            SplitTheGLevelDirection direction,
            bool wasAutoStopped)
        {
            AttemptNumber = attemptNumber;
            TargetLevel = targetLevel;
            FinalLevel = finalLevel;
            ConsumedFraction = Math.Max(0d, 1d - finalLevel);
            LevelDelta = finalLevel - targetLevel;
            AbsoluteError = Math.Abs(LevelDelta);
            Score = score;
            Band = band;
            Direction = direction;
            WasAutoStopped = wasAutoStopped;
        }

        public int AttemptNumber { get; }
        public double TargetLevel { get; }
        public double FinalLevel { get; }
        public double ConsumedFraction { get; }
        public double LevelDelta { get; }
        public double AbsoluteError { get; }
        public int Score { get; }
        public SplitTheGResultBand Band { get; }
        public SplitTheGLevelDirection Direction { get; }
        public bool WasAutoStopped { get; }
    }

    public static class SplitTheGScoring
    {
        public const double PerfectTolerance = 0.01d;
        public const double ExcellentTolerance = 0.03d;
        public const double GoodTolerance = 0.06d;
        public const double CloseTolerance = 0.10d;
        public const double MaximumScoringError = CloseTolerance;

        private const double ComparisonEpsilon = 0.000000001d;
        private const double ScoreRoundingEpsilon = 0.000000001d;

        public static SplitTheGResultBand GetBand(double absoluteError)
        {
            RequireFiniteNonNegative(
                absoluteError,
                nameof(absoluteError));

            if (absoluteError <= PerfectTolerance + ComparisonEpsilon)
            {
                return SplitTheGResultBand.Perfect;
            }

            if (absoluteError <= ExcellentTolerance + ComparisonEpsilon)
            {
                return SplitTheGResultBand.Excellent;
            }

            if (absoluteError <= GoodTolerance + ComparisonEpsilon)
            {
                return SplitTheGResultBand.Good;
            }

            return absoluteError <=
                   CloseTolerance + ComparisonEpsilon
                ? SplitTheGResultBand.Close
                : SplitTheGResultBand.Miss;
        }

        public static int CalculateScore(double absoluteError)
        {
            RequireFiniteNonNegative(
                absoluteError,
                nameof(absoluteError));
            double normalized = Math.Max(
                0d,
                1d - absoluteError / MaximumScoringError);
            return (int)Math.Round(
                100d * normalized + ScoreRoundingEpsilon,
                MidpointRounding.AwayFromZero);
        }

        public static SplitTheGLevelDirection GetDirection(
            double finalLevel,
            double targetLevel)
        {
            RequireNormalizedLevel(finalLevel, nameof(finalLevel));
            RequireNormalizedLevel(targetLevel, nameof(targetLevel));

            double delta = finalLevel - targetLevel;
            if (Math.Abs(delta) <= ComparisonEpsilon)
            {
                return SplitTheGLevelDirection.OnTarget;
            }

            return delta > 0d
                ? SplitTheGLevelDirection.UnderDrank
                : SplitTheGLevelDirection.OverDrank;
        }

        internal static SplitTheGAttemptResult Evaluate(
            int attemptNumber,
            double finalLevel,
            double targetLevel,
            bool wasAutoStopped)
        {
            if (attemptNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attemptNumber));
            }

            RequireNormalizedLevel(finalLevel, nameof(finalLevel));
            RequireNormalizedLevel(targetLevel, nameof(targetLevel));

            double error = Math.Abs(finalLevel - targetLevel);
            return new SplitTheGAttemptResult(
                attemptNumber,
                targetLevel,
                finalLevel,
                CalculateScore(error),
                GetBand(error),
                GetDirection(finalLevel, targetLevel),
                wasAutoStopped);
        }

        private static void RequireFiniteNonNegative(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequireNormalizedLevel(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0d || value > 1d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }
    }
}
