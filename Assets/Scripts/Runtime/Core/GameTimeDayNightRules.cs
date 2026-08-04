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
                   DirectionalLightRotation.Equals(
                       other.DirectionalLightRotation) &&
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

        private static readonly Color DaylightColor =
            new Color(1.00f, 0.93f, 0.78f);
        private static readonly Color DayAmbientColor =
            new Color(0.46f, 0.49f, 0.43f);
        private static readonly Quaternion DaylightRotation =
            Quaternion.Euler(52f, 28f, 0f);

        private const float DaylightIntensity = 1.18f;
        private const float DayReflectionIntensity = 0.72f;
        private const float DayShadowStrength = 0.62f;

        public static DayNightVisualSample Evaluate(double timeOfDayMinutes)
        {
            if (double.IsNaN(timeOfDayMinutes) ||
                double.IsInfinity(timeOfDayMinutes))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeOfDayMinutes),
                    "Time of day must be finite.");
            }

            double minute = NormalizeMinute(timeOfDayMinutes);
            if (minute < DawnStartMinutes || minute >= DuskEndMinutes)
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
                    CreateDaySample(),
                    progress);
            }

            if (minute < DuskStartMinutes)
            {
                return CreateDaySample();
            }

            float duskProgress = SmoothProgress(
                minute,
                DuskStartMinutes,
                DuskEndMinutes);
            return Interpolate(
                CreateDaySample(),
                CreateNightSample(),
                duskProgress);
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

        private static DayNightVisualSample CreateDaySample()
        {
            return new DayNightVisualSample(
                DaylightColor,
                DaylightIntensity,
                DayAmbientColor,
                DayReflectionIntensity,
                DayShadowStrength,
                DaylightRotation,
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
