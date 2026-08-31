using System;
using UnityEngine;

namespace BarPromenade
{
    public enum ExteriorCloudProfileKind
    {
        City = 0,
        MountainRoad = 1,
        AlpineVillage = 2
    }

    /// <summary>
    /// Immutable presentation data for the one shared exterior cloud dome.
    /// The shell radius is a rendering distance, not a physical cloud base:
    /// the dome follows the camera and therefore carries no translation
    /// parallax.
    /// </summary>
    public readonly struct ExteriorCloudProfile
    {
        public ExteriorCloudProfile(
            ExteriorCloudProfileKind kind,
            float shellRadius,
            Color hazeColor,
            Color cloudShadowColor,
            Color cloudLightColor,
            float coverage,
            float contrast,
            float opacity,
            float edgeSoftness,
            float broadScale,
            float detailScale,
            float detailStrength,
            float erosionStrength,
            float broadSpeed,
            float detailSpeed,
            float horizonFadeStart,
            float horizonFadeEnd,
            float nightDarkening,
            float stormContrastLoss,
            bool supportsLightning)
        {
            if (shellRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shellRadius));
            }

            if (broadScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(broadScale));
            }

            if (detailScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(detailScale));
            }

            if (horizonFadeEnd <= horizonFadeStart)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(horizonFadeEnd),
                    "The horizon fade end must be above its start.");
            }

            Kind = kind;
            ShellRadius = shellRadius;
            HazeColor = hazeColor;
            CloudShadowColor = cloudShadowColor;
            CloudLightColor = cloudLightColor;
            Coverage = Mathf.Clamp01(coverage);
            Contrast = Mathf.Clamp01(contrast);
            Opacity = Mathf.Clamp01(opacity);
            EdgeSoftness = Mathf.Clamp01(edgeSoftness);
            BroadScale = broadScale;
            DetailScale = detailScale;
            DetailStrength = Mathf.Clamp01(detailStrength);
            ErosionStrength = Mathf.Clamp01(erosionStrength);
            BroadSpeed = Mathf.Max(0f, broadSpeed);
            DetailSpeed = Mathf.Max(0f, detailSpeed);
            HorizonFadeStart = horizonFadeStart;
            HorizonFadeEnd = horizonFadeEnd;
            NightDarkening = Mathf.Clamp01(nightDarkening);
            StormContrastLoss = Mathf.Clamp01(stormContrastLoss);
            SupportsLightning = supportsLightning;
        }

        public ExteriorCloudProfileKind Kind { get; }
        public float ShellRadius { get; }
        public Color HazeColor { get; }
        public Color CloudShadowColor { get; }
        public Color CloudLightColor { get; }
        public float Coverage { get; }
        public float Contrast { get; }
        public float Opacity { get; }
        public float EdgeSoftness { get; }
        public float BroadScale { get; }
        public float DetailScale { get; }
        public float DetailStrength { get; }
        public float ErosionStrength { get; }
        public float BroadSpeed { get; }
        public float DetailSpeed { get; }
        public float HorizonFadeStart { get; }
        public float HorizonFadeEnd { get; }
        public float NightDarkening { get; }
        public float StormContrastLoss { get; }
        public bool SupportsLightning { get; }
    }

    /// <summary>
    /// The three readings of one cloud system. City and the Home balcony use
    /// <see cref="City"/> verbatim; the two mountain areas change scale and
    /// density without inventing a second clock or wind bearing.
    /// </summary>
    public static class ExteriorCloudProfiles
    {
        public static ExteriorCloudProfile City { get; } =
            new ExteriorCloudProfile(
                ExteriorCloudProfileKind.City,
                47f,
                RuntimeSceneSetup.CityFogColor,
                new Color(0.205f, 0.245f, 0.230f, 1f),
                new Color(0.405f, 0.430f, 0.410f, 1f),
                0.91f,
                0.22f,
                0.95f,
                0.18f,
                1.35f,
                3.45f,
                0.34f,
                0.18f,
                0.00120f,
                0.00205f,
                -0.025f,
                0.20f,
                0.34f,
                0.20f,
                true);

        public static ExteriorCloudProfile MountainRoad { get; } =
            new ExteriorCloudProfile(
                ExteriorCloudProfileKind.MountainRoad,
                119f,
                RuntimeSceneSetup.MountainRoadFogColor,
                new Color(0.155f, 0.195f, 0.195f, 1f),
                new Color(0.360f, 0.395f, 0.385f, 1f),
                0.82f,
                0.28f,
                0.91f,
                0.22f,
                1.12f,
                2.85f,
                0.40f,
                0.24f,
                0.00162f,
                0.00275f,
                0.015f,
                0.255f,
                0.40f,
                0.28f,
                false);

        public static ExteriorCloudProfile AlpineVillage { get; } =
            new ExteriorCloudProfile(
                ExteriorCloudProfileKind.AlpineVillage,
                109f,
                RuntimeSceneSetup.AlpineVillageFogColor,
                new Color(0.370f, 0.390f, 0.390f, 1f),
                new Color(0.685f, 0.635f, 0.555f, 1f),
                0.96f,
                0.24f,
                0.94f,
                0.25f,
                1.04f,
                2.55f,
                0.46f,
                0.20f,
                0.00195f,
                0.00330f,
                -0.04f,
                0.18f,
                0.36f,
                0.82f,
                false);

        public static ExteriorCloudProfile Resolve(
            ExteriorCloudProfileKind kind)
        {
            switch (kind)
            {
                case ExteriorCloudProfileKind.City:
                    return City;
                case ExteriorCloudProfileKind.MountainRoad:
                    return MountainRoad;
                case ExteriorCloudProfileKind.AlpineVillage:
                    return AlpineVillage;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "Unknown exterior cloud profile.");
            }
        }
    }
}
