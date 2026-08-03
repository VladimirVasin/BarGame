using System;

namespace BarPromenade
{
    public readonly struct PlayerNeedReliefResult
    {
        internal PlayerNeedReliefResult(
            int levelBefore,
            int levelAfter,
            int requestedRelief)
        {
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            RequestedRelief = requestedRelief;
        }

        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int RequestedRelief { get; }
        public int ActualRelief => LevelBefore - LevelAfter;
        public bool Changed => ActualRelief > 0;
    }

    public static class PlayerNeedsRules
    {
        public const int MinimumLevel = 0;
        public const int MaximumLevel = 100;
        public const int PoorFoodMinimumHunger = 20;

        public static PlayerNeedReliefResult ApplyFoodRelief(
            int hunger,
            int relief,
            int minimumHungerAfterUse)
        {
            ValidateLevel(hunger, nameof(hunger));
            ValidateRelief(relief, nameof(relief));
            ValidateLevel(
                minimumHungerAfterUse,
                nameof(minimumHungerAfterUse));

            int floorLimitedLevel = Math.Max(
                minimumHungerAfterUse,
                hunger - relief);
            int levelAfter = Math.Min(hunger, floorLimitedLevel);
            return new PlayerNeedReliefResult(
                hunger,
                levelAfter,
                relief);
        }

        public static PlayerNeedReliefResult ApplyStressRelief(
            int stress,
            int relief)
        {
            ValidateLevel(stress, nameof(stress));
            ValidateRelief(relief, nameof(relief));

            int levelAfter = Math.Max(MinimumLevel, stress - relief);
            return new PlayerNeedReliefResult(
                stress,
                levelAfter,
                relief);
        }

        public static int ScaleRelief(
            int reliefPerServing,
            double servings)
        {
            ValidateRelief(
                reliefPerServing,
                nameof(reliefPerServing));
            if (double.IsNaN(servings) ||
                double.IsInfinity(servings) ||
                servings < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(servings));
            }

            double roundedRelief = Math.Round(
                reliefPerServing * servings,
                MidpointRounding.AwayFromZero);
            if (roundedRelief > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(servings));
            }

            return (int)roundedRelief;
        }

        private static void ValidateLevel(
            int level,
            string parameterName)
        {
            if (level < MinimumLevel || level > MaximumLevel)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateRelief(
            int relief,
            string parameterName)
        {
            if (relief < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
