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

    public readonly struct WindSample
    {
        public WindSample(float directionDegrees, float strength01)
        {
            DirectionDegrees = directionDegrees;
            Strength01 = Mathf.Clamp01(strength01);
        }

        /// <summary>World yaw: `0` blows toward +Z, `90` toward +X.</summary>
        public float DirectionDegrees { get; }

        public float Strength01 { get; }

        public Vector3 HorizontalDirection
        {
            get
            {
                float radians = DirectionDegrees * Mathf.Deg2Rad;
                return new Vector3(
                    Mathf.Sin(radians),
                    0f,
                    Mathf.Cos(radians));
            }
        }

        public Vector3 Velocity(float speedAtFullStrength)
        {
            return HorizontalDirection *
                   (Strength01 * speedAtFullStrength);
        }
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

        public const float ClearWindStrength = 0.15f;
        public const float LightRainWindStrength = 0.40f;
        public const float HeavyRainWindStrength = 0.65f;
        public const float ThunderstormWindStrength = 0.95f;

        /// <summary>
        /// Gust periods in game minutes; at one game minute per real
        /// second the primary gust swells roughly every seven seconds.
        /// </summary>
        public const double WindGustPrimaryPeriodMinutes = 7.3d;

        public const double WindGustSecondaryPeriodMinutes = 1.9d;

        /// <summary>
        /// The gust rhythm's shape: a mean with a primary swell and a
        /// quicker secondary ripple riding on it. Named because the village
        /// keys its haze on the RAW rhythm (<see cref="EvaluateGust"/>) and
        /// its tests pin the rhythm's floor (`0.62 - 0.24 - 0.14`) and crest
        /// (`1.0`) from these rather than from a re-typed literal. Doubles,
        /// because the rhythm is summed in double before it is cast: a float
        /// mean promoted to double is not `0.62`, and the wind would stop
        /// being bit-identical to what every scene already shakes to.
        /// </summary>
        public const double WindGustMean = 0.62d;

        public const double WindGustPrimaryAmplitude = 0.24d;
        public const double WindGustSecondaryAmplitude = 0.14d;
        public const double WindSwayPeriodMinutes = 3.1d;
        public const float WindSwayDegrees = 9f;

        /// <summary>
        /// Horizontal speed in meters per second at full wind strength;
        /// shared by the rain drift and any other wind consumer so the
        /// whole exterior agrees on one wind.
        /// </summary>
        public const float WindSpeedAtFullStrength = 3.2f;

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

        public static float GetTargetWindStrength(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Clear:
                    return ClearWindStrength;
                case WeatherKind.LightRain:
                    return LightRainWindStrength;
                case WeatherKind.HeavyRain:
                    return HeavyRainWindStrength;
                case WeatherKind.Thunderstorm:
                    return ThunderstormWindStrength;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// Wind is the same pure schedule as rain: each slot hashes a
        /// base bearing and takes its base strength from the slot's
        /// weather kind, both smoothed across the slot transition; on
        /// top of the base ride continuous deterministic gusts and a
        /// slow directional sway, so every scene shakes to the
        /// identical wind.
        /// </summary>
        public static WindSample EvaluateWind(
            int seed,
            double absoluteGameMinutes)
        {
            ValidateMinutes(absoluteGameMinutes);

            long slotIndex = (long)Math.Floor(
                absoluteGameMinutes / SlotMinutes);
            float targetDirection =
                HashWind(seed, slotIndex) % 360u;
            float targetStrength = GetTargetWindStrength(
                EvaluateSlotKind(seed, slotIndex));
            float baseDirection = targetDirection;
            float baseStrength = targetStrength;
            double minutesIntoSlot =
                absoluteGameMinutes - slotIndex * SlotMinutes;
            if (minutesIntoSlot < TransitionMinutes)
            {
                float previousDirection =
                    HashWind(seed, slotIndex - 1) % 360u;
                float previousStrength = GetTargetWindStrength(
                    EvaluateSlotKind(seed, slotIndex - 1));
                float linear = Mathf.Clamp01(
                    (float)(minutesIntoSlot / TransitionMinutes));
                float progress = linear * linear * (3f - (2f * linear));
                baseDirection = Mathf.LerpAngle(
                    previousDirection,
                    targetDirection,
                    progress);
                baseStrength = Mathf.Lerp(
                    previousStrength,
                    targetStrength,
                    progress);
            }

            uint phaseHash = HashWind(seed, -1L);
            double tau = 2d * Math.PI;
            double phaseC = ((phaseHash >> 16) & 0xFFu) / 255d * tau;
            float gust = EvaluateGust(seed, absoluteGameMinutes);
            float sway = WindSwayDegrees * (float)Math.Sin(
                tau * absoluteGameMinutes /
                WindSwayPeriodMinutes + phaseC);
            return new WindSample(
                baseDirection + sway,
                Mathf.Clamp01(baseStrength * gust));
        }

        public static WindSample EvaluateCurrentWind()
        {
            return EvaluateWind(
                GameSessionState.CitySeed,
                CurrentAbsoluteGameMinutes());
        }

        /// <summary>
        /// The bare gust rhythm every wind consumer rides, before the slot's
        /// base strength scales it: `[0.24, 1]`, with the slot-independent
        /// phases hashed once per seed.
        ///
        /// Exposed on its own because the village's haze has to breathe on
        /// THIS and not on the shaped strength the spindrift reads. The
        /// shaped strength saturates - in a thunderstorm slot the village's
        /// gale pulse is pinned at `1` for the whole ninety minutes - so a
        /// wave keyed on it would close the lane and never open it again;
        /// the raw rhythm always comes back down.
        /// </summary>
        public static float EvaluateGust(
            int seed,
            double absoluteGameMinutes)
        {
            ValidateMinutes(absoluteGameMinutes);

            uint phaseHash = HashWind(seed, -1L);
            double tau = 2d * Math.PI;
            double phaseA = (phaseHash & 0xFFu) / 255d * tau;
            double phaseB = ((phaseHash >> 8) & 0xFFu) / 255d * tau;
            return (float)(
                WindGustMean +
                WindGustPrimaryAmplitude * Math.Sin(
                    tau * absoluteGameMinutes /
                    WindGustPrimaryPeriodMinutes + phaseA) +
                WindGustSecondaryAmplitude * Math.Sin(
                    tau * absoluteGameMinutes /
                    WindGustSecondaryPeriodMinutes + phaseB));
        }

        public static float EvaluateCurrentGust()
        {
            return EvaluateGust(
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

        private static uint HashWind(int seed, long slotIndex)
        {
            uint value = unchecked((uint)seed) ^ 0x57494E44u;
            value ^= unchecked((uint)slotIndex);
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= unchecked((uint)(slotIndex >> 32));
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
