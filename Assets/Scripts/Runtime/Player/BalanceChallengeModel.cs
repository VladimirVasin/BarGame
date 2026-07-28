using UnityEngine;

namespace BarPromenade
{
    public readonly struct BalanceChallengeSettings
    {
        public const float ArcDegrees = 140f;

        private BalanceChallengeSettings(
            float difficulty,
            float warningDuration,
            float duration,
            float safeSectorDegrees,
            float pointerFrequency,
            float disturbanceAcceleration,
            float inputAcceleration,
            float riskGain,
            float riskRecovery)
        {
            Difficulty = difficulty;
            WarningDuration = warningDuration;
            Duration = duration;
            SafeSectorDegrees = safeSectorDegrees;
            PointerFrequency = pointerFrequency;
            DisturbanceAcceleration = disturbanceAcceleration;
            InputAcceleration = inputAcceleration;
            RiskGain = riskGain;
            RiskRecovery = riskRecovery;
        }

        public float Difficulty { get; }
        public float WarningDuration { get; }
        public float Duration { get; }
        public float SafeSectorDegrees { get; }
        public float SafeHalfExtent =>
            SafeSectorDegrees / ArcDegrees;
        public float PointerFrequency { get; }
        public float DisturbanceAcceleration { get; }
        public float InputAcceleration { get; }
        public float RiskGain { get; }
        public float RiskRecovery { get; }

        public static BalanceChallengeSettings FromDifficulty(
            float difficulty)
        {
            float clamped = Mathf.Clamp01(difficulty);
            return new BalanceChallengeSettings(
                clamped,
                Mathf.Lerp(1f, 0.65f, clamped),
                Mathf.Lerp(3f, 4.5f, clamped),
                Mathf.Lerp(48f, 22f, clamped),
                Mathf.Lerp(0.55f, 1.05f, clamped),
                Mathf.Lerp(1.75f, 3.35f, clamped),
                Mathf.Lerp(4.4f, 3.5f, clamped),
                Mathf.Lerp(0.70f, 1.25f, clamped),
                0.5f);
        }
    }

    public static class BalanceChallengeRules
    {
        public static float GetMinimumInterval(int intoxication)
        {
            float difficulty = GetDifficulty(intoxication);
            return Mathf.Lerp(18f, 7f, difficulty);
        }

        public static float GetMaximumInterval(int intoxication)
        {
            float difficulty = GetDifficulty(intoxication);
            return Mathf.Lerp(28f, 12f, difficulty);
        }

        public static float GetNextInterval(
            int intoxication,
            int citySeed,
            int sequence)
        {
            float minimum = GetMinimumInterval(intoxication);
            float maximum = GetMaximumInterval(intoxication);
            return Mathf.Lerp(
                minimum,
                maximum,
                StableUnitValue(citySeed, sequence));
        }

        public static int GetChallengeSeed(
            int citySeed,
            int sequence)
        {
            uint mixed = Mix(
                unchecked((uint)citySeed) ^
                unchecked((uint)sequence * 0x9E3779B9u));
            return unchecked((int)mixed);
        }

        private static float GetDifficulty(int intoxication)
        {
            return intoxication >
                   IntoxicationStageRules.BalanceThreshold
                ? Mathf.InverseLerp(
                    IntoxicationStageRules.BalanceThreshold,
                    IntoxicationStageRules.MaximumLevel,
                    intoxication)
                : 0f;
        }

        private static float StableUnitValue(
            int citySeed,
            int sequence)
        {
            uint mixed = Mix(
                unchecked((uint)citySeed) +
                unchecked((uint)sequence * 0x85EBCA6Bu));
            return (mixed & 0x00FFFFFFu) /
                   16777215f;
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    public sealed class BalanceChallengeModel
    {
        public const float FixedStep = 1f / 120f;

        private readonly BalanceChallengeSettings settings;
        private float phase;
        private float secondaryPhase;
        private float accumulator;

        public BalanceChallengeModel(
            BalanceChallengeSettings challengeSettings,
            int seed)
        {
            settings = challengeSettings;
            uint seedBits = unchecked((uint)seed);
            phase = UnitFromBits(seedBits) * Mathf.PI * 2f;
            secondaryPhase =
                UnitFromBits(seedBits ^ 0xA511E9B3u) *
                Mathf.PI *
                2f;
            Position =
                (UnitFromBits(seedBits ^ 0x63D83595u) - 0.5f) *
                0.12f *
                settings.Difficulty;
        }

        public BalanceChallengeSettings Settings => settings;
        public float Position { get; private set; }
        public float Velocity { get; private set; }
        public float Risk { get; private set; }
        public float Elapsed { get; private set; }
        public bool IsComplete { get; private set; }
        public bool Succeeded { get; private set; }
        public bool Failed => IsComplete && !Succeeded;

        public void Advance(float deltaTime, float input)
        {
            if (IsComplete)
            {
                return;
            }

            accumulator += Mathf.Clamp(deltaTime, 0f, 0.5f);
            float clampedInput = Mathf.Clamp(input, -1f, 1f);
            while (accumulator + 0.000001f >= FixedStep &&
                   !IsComplete)
            {
                SimulateStep(FixedStep, clampedInput);
                accumulator -= FixedStep;
            }
        }

        private void SimulateStep(float step, float input)
        {
            float angularSpeed =
                settings.PointerFrequency * Mathf.PI * 2f;
            phase += angularSpeed * step;
            secondaryPhase += angularSpeed * 1.71f * step;

            float disturbance =
                Mathf.Sin(phase) +
                Mathf.Sin(secondaryPhase) * 0.42f;
            float acceleration =
                disturbance *
                settings.DisturbanceAcceleration +
                input * settings.InputAcceleration -
                Velocity * 0.82f;
            Velocity = Mathf.Clamp(
                Velocity + acceleration * step,
                -2.1f,
                2.1f);
            Position += Velocity * step;

            float absolutePosition = Mathf.Abs(Position);
            if (absolutePosition > 1f)
            {
                Position = Mathf.Sign(Position);
                Velocity *= -0.22f;
                Risk += settings.RiskGain * step * 1.5f;
                absolutePosition = 1f;
            }

            float outside =
                absolutePosition - settings.SafeHalfExtent;
            if (outside > 0f)
            {
                float outsideWeight =
                    outside /
                    Mathf.Max(
                        0.0001f,
                        1f - settings.SafeHalfExtent);
                Risk +=
                    settings.RiskGain *
                    Mathf.Lerp(0.45f, 1.25f, outsideWeight) *
                    step;
            }
            else
            {
                Risk -= settings.RiskRecovery * step;
            }

            Risk = Mathf.Clamp01(Risk);
            Elapsed += step;
            if (Risk >= 1f)
            {
                IsComplete = true;
                Succeeded = false;
            }
            else if (Elapsed + 0.000001f >= settings.Duration)
            {
                IsComplete = true;
                Succeeded = true;
            }
        }

        private static float UnitFromBits(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return (value & 0x00FFFFFFu) /
                   16777215f;
        }
    }
}
