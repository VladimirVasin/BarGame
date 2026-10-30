using System;

namespace BarPromenade
{
    /// <summary>
    /// Seven irreversible appearance steps, independent of calendar, current
    /// intoxication and the story's 0-5 world scale. Drinking contributes one dose
    /// unit per authored intoxication point swallowed, including above the live cap;
    /// the accepted technical sleep cut may raise the dose to the final-stage floor.
    /// These presentation thresholds pace a roughly two-week mixed-drink run;
    /// they neither gate actions nor alter needs, movement speed or balance.
    /// </summary>
    public static class HeroCorruptionRules
    {
        public const int FirstLevel = 1;
        public const int LastLevel = 7;

        private static readonly int[] MinimumDoses =
            { 0, 120, 300, 540, 840, 1200, 1620 };
        private static readonly float[] Severities =
            { 0f, .10f, .23f, .40f, .60f, .80f, 1f };
        private static readonly float[] ShortHairWeights = { 1f, .65f, .25f, 0f, 0f, 0f, 0f };
        private static readonly float[] LongHairWeights = { 0f, 0f, 0f, .2f, .45f, .7f, 1f };

        public static float ShortHairWeight(int level) => ShortHairWeights[Math.Max(FirstLevel, Math.Min(LastLevel, level)) - FirstLevel];
        public static float LongHairWeight(int level) => LongHairWeights[Math.Max(FirstLevel, Math.Min(LastLevel, level)) - FirstLevel];

        public static int ResolveLevel(int accumulatedDose)
        {
            for (int index = MinimumDoses.Length - 1; index > 0; index--)
                if (accumulatedDose >= MinimumDoses[index]) return index + 1;
            return FirstLevel;
        }

        public static int MinimumDoseForLevel(int level)
        {
            if (level < FirstLevel || level > LastLevel)
                throw new ArgumentOutOfRangeException(nameof(level));
            return MinimumDoses[level - FirstLevel];
        }

        public static float Severity(int level)
        {
            int clamped = Math.Max(FirstLevel, Math.Min(LastLevel, level));
            return Severities[clamped - FirstLevel];
        }

        public static int AddDose(int accumulatedDose, int consumedDose)
        {
            int current = Math.Max(0, accumulatedDose);
            int added = Math.Max(0, consumedDose);
            return current <= int.MaxValue - added ? current + added : int.MaxValue;
        }
    }
}
