using UnityEngine;

namespace BarPromenade
{
    public enum IntoxicationStage
    {
        Sober = 0,
        LightBuzz,
        Tipsy,
        Drunk,
        Unsteady,
        VeryDrunk
    }

    public readonly struct IntoxicationProfile
    {
        internal IntoxicationProfile(
            int level,
            IntoxicationStage stage,
            string stageNameKey,
            float speedMultiplier,
            float puppetSwayDegrees,
            float cameraRollDegrees,
            float vignetteStrength,
            float ghostPixels,
            float warpStrength,
            float warmth,
            float exposurePulse,
            float balanceDifficulty,
            float chromaticAberration,
            float lensDistortion)
        {
            Level = level;
            Stage = stage;
            StageNameKey = stageNameKey;
            Normalized = level / 100f;
            SpeedMultiplier = speedMultiplier;
            PuppetSwayDegrees = puppetSwayDegrees;
            CameraRollDegrees = cameraRollDegrees;
            VignetteStrength = vignetteStrength;
            GhostPixels = ghostPixels;
            WarpStrength = warpStrength;
            Warmth = warmth;
            ExposurePulse = exposurePulse;
            BalanceDifficulty = balanceDifficulty;
            ChromaticAberration = chromaticAberration;
            LensDistortion = lensDistortion;
        }

        public int Level { get; }
        public IntoxicationStage Stage { get; }
        public string StageNameKey { get; }
        public float Normalized { get; }
        public float SpeedMultiplier { get; }
        public float PuppetSwayDegrees { get; }
        public float CameraRollDegrees { get; }
        public float VignetteStrength { get; }
        public float GhostPixels { get; }
        public float WarpStrength { get; }
        public float Warmth { get; }
        public float ExposurePulse { get; }
        public float BalanceDifficulty { get; }
        public float ChromaticAberration { get; }
        public float LensDistortion { get; }
        public bool BalanceEnabled =>
            Level > IntoxicationStageRules.BalanceThreshold;
    }

    public static class IntoxicationStageRules
    {
        public const int MaximumLevel = 100;
        public const int StageSize = 20;
        public const int BalanceThreshold = 60;
        public const float RecoverySecondsPerPointAtMinimumLevel = 3f;
        public const float RecoverySecondsPerPointAtMaximumLevel = 12f;

        public static IntoxicationProfile Evaluate(int level)
        {
            int clampedLevel = Mathf.Clamp(
                level,
                0,
                MaximumLevel);
            IntoxicationStage stage = GetStage(clampedLevel);
            float balanceDifficulty = clampedLevel > BalanceThreshold
                ? Mathf.InverseLerp(
                    BalanceThreshold,
                    MaximumLevel,
                    clampedLevel)
                : 0f;

            return new IntoxicationProfile(
                clampedLevel,
                stage,
                GetStageNameKey(stage),
                Interpolate(
                    clampedLevel,
                    1f,
                    1f,
                    0.97f,
                    0.92f,
                    0.82f,
                    0.70f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0.5f,
                    2f,
                    4f,
                    7f,
                    10f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0f,
                    0.15f,
                    0.6f,
                    1.5f,
                    2.5f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0.03f,
                    0.06f,
                    0.12f,
                    0.20f,
                    0.28f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0f,
                    0.5f,
                    1f,
                    2f,
                    3f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0f,
                    0.0005f,
                    0.0025f,
                    0.009f,
                    0.015f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0.05f,
                    0.07f,
                    0.08f,
                    0.09f,
                    0.10f),
                Interpolate(
                    clampedLevel,
                    0f,
                    0f,
                    0.015f,
                    0.03f,
                    0.05f,
                    0.08f),
                balanceDifficulty,
                Interpolate(
                    clampedLevel,
                    0f,
                    0.05f,
                    0.10f,
                    0.20f,
                    0.32f,
                    0.45f),
                // Small and negative: the frame pinches inward like a
                // heavy lens, and |0.14| stays under the two internal
                // pixels the PS1 point-sample can hide at the rim.
                Interpolate(
                    clampedLevel,
                    0f,
                    0f,
                    -0.02f,
                    -0.05f,
                    -0.09f,
                    -0.14f));
        }

        public static IntoxicationStage GetStage(int level)
        {
            int clampedLevel = Mathf.Clamp(
                level,
                0,
                MaximumLevel);
            if (clampedLevel == 0)
            {
                return IntoxicationStage.Sober;
            }

            int stageIndex = Mathf.CeilToInt(
                clampedLevel / (float)StageSize);
            return (IntoxicationStage)Mathf.Clamp(stageIndex, 1, 5);
        }

        public static float GetRecoverySecondsPerPoint(int level)
        {
            float normalizedLevel = Mathf.InverseLerp(
                1f,
                MaximumLevel,
                Mathf.Clamp(level, 1, MaximumLevel));
            return Mathf.Lerp(
                RecoverySecondsPerPointAtMinimumLevel,
                RecoverySecondsPerPointAtMaximumLevel,
                normalizedLevel);
        }

        public static string GetStageNameKey(
            IntoxicationStage stage)
        {
            switch (stage)
            {
                case IntoxicationStage.Sober:
                    return "intoxication.stage.sober";
                case IntoxicationStage.LightBuzz:
                    return "intoxication.stage.light_buzz";
                case IntoxicationStage.Tipsy:
                    return "intoxication.stage.tipsy";
                case IntoxicationStage.Drunk:
                    return "intoxication.stage.drunk";
                case IntoxicationStage.Unsteady:
                    return "intoxication.stage.unsteady";
                case IntoxicationStage.VeryDrunk:
                    return "intoxication.stage.very_drunk";
                default:
                    return "intoxication.stage.sober";
            }
        }

        private static float Interpolate(
            int level,
            float atZero,
            float atTwenty,
            float atForty,
            float atSixty,
            float atEighty,
            float atHundred)
        {
            if (level <= 20)
            {
                return Mathf.Lerp(
                    atZero,
                    atTwenty,
                    level / 20f);
            }

            if (level <= 40)
            {
                return Mathf.Lerp(
                    atTwenty,
                    atForty,
                    (level - 20) / 20f);
            }

            if (level <= 60)
            {
                return Mathf.Lerp(
                    atForty,
                    atSixty,
                    (level - 40) / 20f);
            }

            if (level <= 80)
            {
                return Mathf.Lerp(
                    atSixty,
                    atEighty,
                    (level - 60) / 20f);
            }

            return Mathf.Lerp(
                atEighty,
                atHundred,
                (level - 80) / 20f);
        }
    }
}
