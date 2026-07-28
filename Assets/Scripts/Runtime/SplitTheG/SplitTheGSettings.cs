using System;

namespace BarPromenade
{
    public sealed class SplitTheGSettings
    {
        public const double NormalTargetLevel = 0.5d;
        public const double NormalDrinkSpeed = 0.22d;
        public const double NormalMaximumDrinkTime = 4.8d;
        public const double NormalSettlingTime = 1.4d;
        public const int NormalMaximumAttempts = 3;
        public const double NormalCountdownTime = 1.25d;

        public SplitTheGSettings(
            double targetLevel,
            double drinkSpeed,
            double maximumDrinkTime,
            double settlingTime,
            int maximumAttempts,
            double countdownTime = NormalCountdownTime)
        {
            RequireNormalized(
                targetLevel,
                nameof(targetLevel));
            RequirePositiveFinite(
                drinkSpeed,
                nameof(drinkSpeed));
            RequirePositiveFinite(
                maximumDrinkTime,
                nameof(maximumDrinkTime));
            RequireNonNegativeFinite(
                settlingTime,
                nameof(settlingTime));
            RequireNonNegativeFinite(
                countdownTime,
                nameof(countdownTime));
            if (maximumAttempts < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAttempts));
            }

            TargetLevel = targetLevel;
            DrinkSpeed = drinkSpeed;
            MaximumDrinkTime = maximumDrinkTime;
            SettlingTime = settlingTime;
            MaximumAttempts = maximumAttempts;
            CountdownTime = countdownTime;
        }

        public double TargetLevel { get; }
        public double DrinkSpeed { get; }
        public double MaximumDrinkTime { get; }
        public double SettlingTime { get; }
        public int MaximumAttempts { get; }
        public double CountdownTime { get; }

        public static SplitTheGSettings Normal =>
            new SplitTheGSettings(
                NormalTargetLevel,
                NormalDrinkSpeed,
                NormalMaximumDrinkTime,
                NormalSettlingTime,
                NormalMaximumAttempts,
                NormalCountdownTime);

        private static void RequireNormalized(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0d || value > 1d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequirePositiveFinite(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequireNonNegativeFinite(
            double value,
            string parameterName)
        {
            if (!IsFinite(value) || value < 0d)
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
