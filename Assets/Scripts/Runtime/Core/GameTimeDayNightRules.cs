using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct DayNightVisualSample
    {
        public DayNightVisualSample(
            Color directionalLightColor,
            float directionalLightIntensity,
            Color ambientLightColor,
            float reflectionIntensity,
            float shadowStrength,
            Quaternion directionalLightRotation,
            float nightFactor)
        {
            DirectionalLightColor = directionalLightColor;
            DirectionalLightIntensity = directionalLightIntensity;
            AmbientLightColor = ambientLightColor;
            ReflectionIntensity = reflectionIntensity;
            ShadowStrength = shadowStrength;
            DirectionalLightRotation = directionalLightRotation;
            NightFactor = Mathf.Clamp01(nightFactor);
        }

        public Color DirectionalLightColor { get; }
        public float DirectionalLightIntensity { get; }
        public Color AmbientLightColor { get; }
        public float ReflectionIntensity { get; }
        public float ShadowStrength { get; }
        public Quaternion DirectionalLightRotation { get; }
        public float NightFactor { get; }

        /// <summary>
        /// A hundredth of a degree: the sun does not move that far in
        /// a whole game day, and it is far more than the drift
        /// Quaternion.Slerp leaves behind. Without it the sample taken
        /// at the very first minute of dusk — identical to the day in
        /// every colour, intensity and factor — reads as a change,
        /// because Slerp renormalises its result and the bits stop
        /// matching the constant it interpolated from.
        /// </summary>
        public const float RotationEpsilonDegrees = 0.01f;

        public bool IsVisuallyEquivalentTo(DayNightVisualSample other)
        {
            return DirectionalLightColor.Equals(
                       other.DirectionalLightColor) &&
                   DirectionalLightIntensity.Equals(
                       other.DirectionalLightIntensity) &&
                   AmbientLightColor.Equals(other.AmbientLightColor) &&
                   ReflectionIntensity.Equals(
                       other.ReflectionIntensity) &&
                   ShadowStrength.Equals(other.ShadowStrength) &&
                   Quaternion.Angle(
                       DirectionalLightRotation,
                       other.DirectionalLightRotation) <=
                       RotationEpsilonDegrees &&
                   NightFactor.Equals(other.NightFactor);
        }
    }

    public static class GameTimeDayNightRules
    {
        public const double MinutesPerDay = 24d * 60d;
        public const double DawnStartMinutes = 6d * 60d;
        public const double DawnEndMinutes = 7d * 60d;
        public const double DuskStartMinutes = 18d * 60d;
        public const double DuskEndMinutes = 19d * 60d;

        /// <summary>
        /// The story bible's §20 law, as one number: the city is overcast
        /// and foggy at every hour, so every lighting FIXTURE burns always,
        /// and the day takes at most a third off it - at noon a fixture
        /// gives no less than two thirds of its night strength, and the fog
        /// halo around it is never taken away. Excluded as events rather
        /// than fixtures: vehicle headlights, lightning, a struck match,
        /// the lighthouse beam. The village above has its own stronger rule
        /// and no clock on its lights at all.
        /// </summary>
        public const float DayFixtureFloor = 2f / 3f;

        /// <summary>
        /// The multiplier a fixture's brightness rides, in place of the raw
        /// night factor. The raw factor is the SKY's - it still takes the
        /// sun, the ambient and everything else that genuinely belongs to
        /// the hour to zero at noon; a fixture that multiplied by it went
        /// black at midday, which §20 forbids.
        /// </summary>
        public static float FixtureFactor(float nightFactor)
        {
            return Mathf.Lerp(
                DayFixtureFloor,
                1f,
                Mathf.Clamp01(nightFactor));
        }

        private static readonly Color DaylightColor =
            new Color(1.00f, 0.93f, 0.78f);
        private static readonly Color DayAmbientColor =
            new Color(0.46f, 0.49f, 0.43f);

        private const float DaylightIntensity = 1.18f;
        private const float DayReflectionIntensity = 0.72f;
        private const float DayShadowStrength = 0.62f;

        /// <summary>
        /// Solar noon. The sun stands due south here and nowhere else.
        /// </summary>
        public const double SolarNoonMinutes = 12d * 60d;

        /// <summary>
        /// Sunrise and sunset. These are NOT knobs: with the
        /// declination that a twelve-hour day forces, the elevation
        /// crosses zero exactly a quarter turn either side of noon.
        /// They are named so the schedule above can be read against
        /// them - dawn ends an hour AFTER the sun clears the horizon
        /// and dusk begins as it touches it.
        /// </summary>
        public const double SunriseMinutes = SolarNoonMinutes - (6d * 60d);
        public const double SunsetMinutes = SolarNoonMinutes + (6d * 60d);

        /// <summary>
        /// The sun's height at noon, and the ONE authored number this
        /// whole path is built from. It is the elevation the old fixed
        /// pose Euler(52, 28, 0) already carried, so midday keeps the
        /// light the City was lit and tuned under.
        ///
        /// This is an equinox: a twelve-hour day forces declination to
        /// zero, which is not a simplification but the only consistent
        /// answer. The consequence is worth stating because the church
        /// is built on it - the sun rises due EAST and sets due WEST,
        /// never straying north of that line, so a wall facing north
        /// takes no direct sun at any minute of any day. A longer day
        /// would need a positive declination and a sunrise north of
        /// east, and the church's north aisle would catch an hour of
        /// grazing light that a basilica in this hemisphere should
        /// never see.
        /// </summary>
        public const float PeakSunElevationDegrees = 52f;

        /// <summary>
        /// Degrees of hour angle per minute: a full turn a day.
        /// </summary>
        private const double HourAngleDegreesPerMinute = 360d / MinutesPerDay;

        public static DayNightVisualSample Evaluate(double timeOfDayMinutes)
        {
            ValidateTimeOfDay(timeOfDayMinutes);

            double minute = NormalizeMinute(timeOfDayMinutes);
            if (IsNightMinute(minute))
            {
                return CreateNightSample();
            }

            if (minute < DawnEndMinutes)
            {
                float progress = SmoothProgress(
                    minute,
                    DawnStartMinutes,
                    DawnEndMinutes);
                return Interpolate(
                    CreateNightSample(),
                    CreateDaySample(minute),
                    progress);
            }

            if (minute < DuskStartMinutes)
            {
                return CreateDaySample(minute);
            }

            float duskProgress = SmoothProgress(
                minute,
                DuskStartMinutes,
                DuskEndMinutes);
            return Interpolate(
                CreateDaySample(minute),
                CreateNightSample(),
                duskProgress);
        }

        public static bool IsNight(double timeOfDayMinutes)
        {
            ValidateTimeOfDay(timeOfDayMinutes);
            return IsNightMinute(NormalizeMinute(timeOfDayMinutes));
        }

        /// <summary>
        /// Height of the sun above the horizon, in degrees. Zero at
        /// 06:00 and 18:00, <see cref="PeakSunElevationDegrees"/> at
        /// noon. Clamped at the horizon: through dusk the sample is
        /// still interpolating away from the day pose, and a
        /// directional light allowed below the ground plane lights the
        /// whole city from underneath.
        /// </summary>
        public static float SunElevationDegreesAt(double timeOfDayMinutes)
        {
            ValidateTimeOfDay(timeOfDayMinutes);
            double hourAngle = HourAngleDegreesAt(
                NormalizeMinute(timeOfDayMinutes));
            // Declination is zero, so the classic
            //   sin E = sin φ sin δ + cos φ cos δ cos h
            // collapses to cos φ cos h, and the latitude that puts noon
            // at PeakSunElevationDegrees is its complement - which is
            // why cos φ is written as the sine of the peak.
            double sinElevation =
                Math.Sin(PeakSunElevationDegrees * Mathf.Deg2Rad) *
                Math.Cos(hourAngle * Mathf.Deg2Rad);
            if (sinElevation <= 0d)
            {
                return 0f;
            }

            return (float)(Math.Asin(sinElevation) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Compass bearing of the sun, degrees clockwise from north.
        /// 90 due east at sunrise, 180 due south at noon, 270 due west
        /// at sunset. Never leaves that half of the compass.
        /// </summary>
        public static float SunAzimuthDegreesAt(double timeOfDayMinutes)
        {
            ValidateTimeOfDay(timeOfDayMinutes);
            double minute = NormalizeMinute(timeOfDayMinutes);
            float elevation = SunElevationDegreesAt(minute);
            // With zero declination the azimuth reduces to
            //   cos A = -tan E tan φ
            // and tan φ is the cotangent of the peak elevation, so the
            // identity closes exactly on 180 at noon rather than
            // landing a fraction of a degree off it.
            double cosAzimuth =
                -Math.Tan(elevation * Mathf.Deg2Rad) /
                Math.Tan(PeakSunElevationDegrees * Mathf.Deg2Rad);
            cosAzimuth = Math.Max(-1d, Math.Min(1d, cosAzimuth));
            double eastward = Math.Acos(cosAzimuth) * Mathf.Rad2Deg;
            return HourAngleDegreesAt(minute) < 0d
                ? (float)eastward
                : (float)(360d - eastward);
        }

        /// <summary>
        /// The rotation a directional light needs to BE the sun at this
        /// minute. Its forward is the direction the light travels, so
        /// the sun itself sits along the negation.
        /// </summary>
        public static Quaternion SunRotationAt(double timeOfDayMinutes)
        {
            float elevation = SunElevationDegreesAt(timeOfDayMinutes);
            float azimuth = SunAzimuthDegreesAt(timeOfDayMinutes);
            float elevationRadians = elevation * Mathf.Deg2Rad;
            float azimuthRadians = azimuth * Mathf.Deg2Rad;
            float horizontal = Mathf.Cos(elevationRadians);
            // +X is east and +Z is north, so a bearing from north reads
            // as (sin, cos) in that order.
            var towardSun = new Vector3(
                Mathf.Sin(azimuthRadians) * horizontal,
                Mathf.Sin(elevationRadians),
                Mathf.Cos(azimuthRadians) * horizontal);
            return Quaternion.LookRotation(-towardSun, Vector3.up);
        }

        private static double HourAngleDegreesAt(double minute)
        {
            return (minute - SolarNoonMinutes) *
                HourAngleDegreesPerMinute;
        }

        private static bool IsNightMinute(double minute)
        {
            return minute < DawnStartMinutes || minute >= DuskEndMinutes;
        }

        private static void ValidateTimeOfDay(double timeOfDayMinutes)
        {
            if (double.IsNaN(timeOfDayMinutes) ||
                double.IsInfinity(timeOfDayMinutes))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeOfDayMinutes),
                    "Time of day must be finite.");
            }
        }

        private static DayNightVisualSample CreateNightSample()
        {
            return new DayNightVisualSample(
                RuntimeSceneSetup.MoonlightColor,
                RuntimeSceneSetup.CityMoonlightIntensity,
                RuntimeSceneSetup.CityAmbientColor,
                RuntimeSceneSetup.CityNightReflectionIntensity,
                RuntimeSceneSetup.CityShadowStrength,
                RuntimeSceneSetup.CityMoonlightRotation,
                1f);
        }

        /// <summary>
        /// Colour, intensity and ambient are flat across the whole day
        /// on purpose - the sun MOVES, it does not warm and cool. That
        /// keeps the one thing this change adds isolated to a single
        /// field, and it is what lets the per-minute appliers keep
        /// skipping their expensive environment work.
        /// </summary>
        private static DayNightVisualSample CreateDaySample(double minute)
        {
            return new DayNightVisualSample(
                DaylightColor,
                DaylightIntensity,
                DayAmbientColor,
                DayReflectionIntensity,
                DayShadowStrength,
                SunRotationAt(minute),
                0f);
        }

        private static DayNightVisualSample Interpolate(
            DayNightVisualSample from,
            DayNightVisualSample to,
            float progress)
        {
            return new DayNightVisualSample(
                Color.Lerp(
                    from.DirectionalLightColor,
                    to.DirectionalLightColor,
                    progress),
                Mathf.Lerp(
                    from.DirectionalLightIntensity,
                    to.DirectionalLightIntensity,
                    progress),
                Color.Lerp(
                    from.AmbientLightColor,
                    to.AmbientLightColor,
                    progress),
                Mathf.Lerp(
                    from.ReflectionIntensity,
                    to.ReflectionIntensity,
                    progress),
                Mathf.Lerp(
                    from.ShadowStrength,
                    to.ShadowStrength,
                    progress),
                Quaternion.Slerp(
                    from.DirectionalLightRotation,
                    to.DirectionalLightRotation,
                    progress),
                Mathf.Lerp(
                    from.NightFactor,
                    to.NightFactor,
                    progress));
        }

        private static float SmoothProgress(
            double minute,
            double start,
            double end)
        {
            float linear = Mathf.Clamp01(
                (float)((minute - start) / (end - start)));
            return linear * linear * (3f - (2f * linear));
        }

        private static double NormalizeMinute(double minute)
        {
            double normalized = minute % MinutesPerDay;
            return normalized < 0d
                ? normalized + MinutesPerDay
                : normalized;
        }
    }
}
