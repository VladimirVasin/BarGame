using System;
using UnityEngine;

namespace BarPromenade
{
    public enum WeatherKind
    {
        Clear = 0,
        LightRain = 1,
        HeavyRain = 2,
        Thunderstorm = 3
    }

    public readonly struct LightningSample
    {
        public LightningSample(
            long strikeId,
            float flashIntensity,
            float azimuthDegrees,
            float distanceFactor)
        {
            StrikeId = strikeId;
            FlashIntensity = Mathf.Clamp01(flashIntensity);
            AzimuthDegrees = azimuthDegrees;
            DistanceFactor = distanceFactor;
        }

        public static LightningSample None =>
            new LightningSample(-1L, 0f, 0f, 1f);

        public long StrikeId { get; }
        public float FlashIntensity { get; }
        public float AzimuthDegrees { get; }

        /// <summary>`0` reads as overhead, `1` as the far fog band.</summary>
        public float DistanceFactor { get; }

        public bool IsFlashing =>
            StrikeId >= 0L && FlashIntensity > 0f;
    }

    public readonly struct WeatherVisualSample
    {
        public WeatherVisualSample(
            WeatherKind kind,
            float rainIntensity)
        {
            Kind = kind;
            RainIntensity = Mathf.Clamp01(rainIntensity);
        }

        public WeatherKind Kind { get; }
        public float RainIntensity { get; }
        public bool HasRain => RainIntensity > 0f;

        public bool IsVisuallyEquivalentTo(WeatherVisualSample other)
        {
            return Kind == other.Kind &&
                   RainIntensity.Equals(other.RainIntensity);
        }
    }

    /// <summary>
    /// Deterministic exterior weather: a pure function of the city seed and
    /// the absolute game time, so City and the Home balcony always agree and
    /// scene loads cannot desynchronize the sky.
    /// </summary>
    public static class GameWeatherRules
    {
        /// <summary>One weather slot lasts 90 game minutes.</summary>
        public const double SlotMinutes = 90d;

        /// <summary>
        /// Rain fades between slot targets over the first game minutes of a
        /// slot instead of switching on a hard boundary.
        /// </summary>
        public const double TransitionMinutes = 5d;

        public const float ClearIntensity = 0f;
        public const float LightRainIntensity = 0.45f;
        public const float HeavyRainIntensity = 1f;
        public const float ThunderstormIntensity = 1f;

        /// <summary>Percent of slots that stay dry.</summary>
        public const int ClearPercent = 55;

        /// <summary>Percent of slots with light rain.</summary>
        public const int LightRainPercent = 27;

        /// <summary>
        /// Percent of slots with plain heavy rain; the remainder is a
        /// thunderstorm.
        /// </summary>
        public const int HeavyRainPercent = 12;

        /// <summary>
        /// One potential lightning strike per window; at one game minute per
        /// real second a window lasts 12 real seconds.
        /// </summary>
        public const double LightningWindowMinutes = 12d;

        /// <summary>Percent of storm windows that actually strike.</summary>
        public const int LightningStrikePercent = 70;

        public const double LightningFlashMinutes = 0.5d;

        public static WeatherKind EvaluateSlotKind(
            int seed,
            long slotIndex)
        {
            int percent = (int)(HashSlot(seed, slotIndex) % 100u);
            if (percent < ClearPercent)
            {
                return WeatherKind.Clear;
            }

            if (percent < ClearPercent + LightRainPercent)
            {
                return WeatherKind.LightRain;
            }

            return percent <
                   ClearPercent + LightRainPercent + HeavyRainPercent
                ? WeatherKind.HeavyRain
                : WeatherKind.Thunderstorm;
        }

        public static float GetTargetIntensity(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Clear:
                    return ClearIntensity;
                case WeatherKind.LightRain:
                    return LightRainIntensity;
                case WeatherKind.HeavyRain:
                    return HeavyRainIntensity;
                case WeatherKind.Thunderstorm:
                    return ThunderstormIntensity;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static WeatherVisualSample Evaluate(
            int seed,
            double absoluteGameMinutes)
        {
            ValidateMinutes(absoluteGameMinutes);

            long slotIndex = (long)Math.Floor(
                absoluteGameMinutes / SlotMinutes);
            WeatherKind kind = EvaluateSlotKind(seed, slotIndex);
            float targetIntensity = GetTargetIntensity(kind);
            double minutesIntoSlot =
                absoluteGameMinutes - slotIndex * SlotMinutes;
            if (minutesIntoSlot >= TransitionMinutes)
            {
                return new WeatherVisualSample(kind, targetIntensity);
            }

            float previousIntensity = GetTargetIntensity(
                EvaluateSlotKind(seed, slotIndex - 1));
            float linear = Mathf.Clamp01(
                (float)(minutesIntoSlot / TransitionMinutes));
            float progress = linear * linear * (3f - (2f * linear));
            return new WeatherVisualSample(
                kind,
                Mathf.Lerp(
                    previousIntensity,
                    targetIntensity,
                    progress));
        }

        public static WeatherVisualSample EvaluateCurrent()
        {
            return Evaluate(
                GameSessionState.CitySeed,
                CurrentAbsoluteGameMinutes());
        }

        /// <summary>
        /// Lightning is the same pure schedule: each 12-game-minute window of
        /// a fully developed thunderstorm slot hashes into at most one strike
        /// with its own start offset, direction and distance, so every scene
        /// flashes the identical storm.
        /// </summary>
        public static LightningSample EvaluateLightning(
            int seed,
            double absoluteGameMinutes)
        {
            ValidateMinutes(absoluteGameMinutes);

            long windowIndex = (long)Math.Floor(
                absoluteGameMinutes / LightningWindowMinutes);
            uint hash = HashLightning(seed, windowIndex);
            if (hash % 100u >= LightningStrikePercent)
            {
                return LightningSample.None;
            }

            double offsetFraction = ((hash >> 7) & 0x3FFu) / 1023d;
            double strikeStart =
                windowIndex * LightningWindowMinutes +
                offsetFraction *
                (LightningWindowMinutes - LightningFlashMinutes);
            if (absoluteGameMinutes < strikeStart ||
                absoluteGameMinutes >=
                strikeStart + LightningFlashMinutes)
            {
                return LightningSample.None;
            }

            long strikeSlot = (long)Math.Floor(
                strikeStart / SlotMinutes);
            if (EvaluateSlotKind(seed, strikeSlot) !=
                WeatherKind.Thunderstorm ||
                strikeStart - strikeSlot * SlotMinutes <
                TransitionMinutes)
            {
                return LightningSample.None;
            }

            float flashProgress = (float)(
                (absoluteGameMinutes - strikeStart) /
                LightningFlashMinutes);
            float envelope =
                Mathf.Pow(1f - flashProgress, 1.6f) *
                (0.72f + 0.28f * Mathf.Cos(flashProgress * 22f));
            float azimuth = (hash >> 17) % 360u;
            float distance =
                0.35f + 0.65f * (((hash >> 3) & 0xFFu) / 255f);
            return new LightningSample(
                windowIndex,
                Mathf.Clamp01(envelope),
                azimuth,
                distance);
        }

        public static LightningSample EvaluateCurrentLightning()
        {
            return EvaluateLightning(
                GameSessionState.CitySeed,
                CurrentAbsoluteGameMinutes());
        }

        private static double CurrentAbsoluteGameMinutes()
        {
            return GameSessionState.GameDayIndex *
                   GameTimeDayNightRules.MinutesPerDay +
                   GameSessionState.GameTimeOfDayMinutes;
        }

        private static void ValidateMinutes(double absoluteGameMinutes)
        {
            if (double.IsNaN(absoluteGameMinutes) ||
                double.IsInfinity(absoluteGameMinutes))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(absoluteGameMinutes),
                    "Absolute game time must be finite.");
            }
        }

        private static uint HashLightning(int seed, long windowIndex)
        {
            uint value = unchecked((uint)seed) ^ 0x424F4C54u;
            value ^= unchecked((uint)windowIndex);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= unchecked((uint)(windowIndex >> 32));
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private static uint HashSlot(int seed, long slotIndex)
        {
            uint value = unchecked((uint)seed) ^ 0x5241494Eu;
            value ^= unchecked((uint)slotIndex);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= unchecked((uint)(slotIndex >> 32));
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
