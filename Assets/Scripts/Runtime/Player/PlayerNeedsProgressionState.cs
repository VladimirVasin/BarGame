using System;

namespace BarPromenade
{
    public readonly struct PlayerNeedsProgressionResult
    {
        internal PlayerNeedsProgressionResult(
            int hungerBefore,
            int hungerAfter,
            int fatigueBefore,
            int fatigueAfter)
        {
            HungerBefore = hungerBefore;
            HungerAfter = hungerAfter;
            FatigueBefore = fatigueBefore;
            FatigueAfter = fatigueAfter;
        }

        public int HungerBefore { get; }
        public int HungerAfter { get; }
        public int FatigueBefore { get; }
        public int FatigueAfter { get; }
        public bool Changed =>
            HungerBefore != HungerAfter ||
            FatigueBefore != FatigueAfter;
    }

    public sealed class PlayerNeedsProgressionState
    {
        public const double HungerGameMinutesToMaximum = 1440d;
        public const double FatigueGameMinutesToMaximum = 1080d;

        private const double VisibleLevelEpsilon = 0.000000001d;

        private double hunger;
        private double fatigue;

        public int HungerLevel => ToVisibleLevel(hunger);
        public int FatigueLevel => ToVisibleLevel(fatigue);

        public PlayerNeedsProgressionResult Advance(
            double elapsedGameMinutes)
        {
            int hungerBefore = HungerLevel;
            int fatigueBefore = FatigueLevel;
            if (!IsFinite(elapsedGameMinutes) ||
                elapsedGameMinutes <= 0d)
            {
                return new PlayerNeedsProgressionResult(
                    hungerBefore,
                    hungerBefore,
                    fatigueBefore,
                    fatigueBefore);
            }

            hunger = AdvanceLevel(
                hunger,
                elapsedGameMinutes,
                HungerGameMinutesToMaximum);
            fatigue = AdvanceLevel(
                fatigue,
                elapsedGameMinutes,
                FatigueGameMinutesToMaximum);
            return new PlayerNeedsProgressionResult(
                hungerBefore,
                HungerLevel,
                fatigueBefore,
                FatigueLevel);
        }

        public void SetHunger(int level)
        {
            ValidateLevel(level, nameof(level));
            hunger = level;
        }

        public void SetFatigue(int level)
        {
            ValidateLevel(level, nameof(level));
            fatigue = level;
        }

        public void Reset()
        {
            hunger = PlayerNeedsRules.MinimumLevel;
            fatigue = PlayerNeedsRules.MinimumLevel;
        }

        private static double AdvanceLevel(
            double level,
            double elapsedGameMinutes,
            double minutesToMaximum)
        {
            if (level >= PlayerNeedsRules.MaximumLevel)
            {
                return PlayerNeedsRules.MaximumLevel;
            }

            double increase =
                elapsedGameMinutes *
                PlayerNeedsRules.MaximumLevel /
                minutesToMaximum;
            return Math.Min(
                PlayerNeedsRules.MaximumLevel,
                level + increase);
        }

        private static int ToVisibleLevel(double level)
        {
            return Math.Min(
                PlayerNeedsRules.MaximumLevel,
                (int)Math.Floor(level + VisibleLevelEpsilon));
        }

        private static void ValidateLevel(
            int level,
            string parameterName)
        {
            if (level < PlayerNeedsRules.MinimumLevel ||
                level > PlayerNeedsRules.MaximumLevel)
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
