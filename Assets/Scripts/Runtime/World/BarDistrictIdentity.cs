using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The mood a district's bar answers to, per the zone art bible:
    /// four ways of living with one tiredness.
    /// </summary>
    public enum BarDistrictMood
    {
        /// <summary>Old Town — memory, debt and other people's traces.</summary>
        Memory = 0,

        /// <summary>Residential — the place next to home.</summary>
        Household = 1,

        /// <summary>Industrial — after the shift.</summary>
        AfterShift = 2,

        /// <summary>Nightlife — short oblivion.</summary>
        Escape = 3
    }

    /// <summary>Which packaged surface set an interior wears.</summary>
    public enum BarSurfaceSetKind
    {
        /// <summary>
        /// Large surfaces use the district's authored block colours.
        /// </summary>
        None = 0,

        /// <summary>
        /// The worn set: trodden planks, old wallpaper, tired dark
        /// wood, upholstery rubbed to the weave — a bar for people
        /// without money.
        /// </summary>
        Worn = 1
    }

    /// <summary>
    /// One district bar's authored character sheet: the technical
    /// hooks every consumer (interior palette, lights, signage,
    /// soundscape, naming) reads from one place.
    /// </summary>
    public readonly struct BarDistrictIdentity
    {
        public BarDistrictIdentity(
            CityDistrictKind district,
            BarDistrictMood mood,
            string displayNameKey,
            Color counterWoodTint,
            Color wallTint,
            Color pendantColor,
            float pendantIntensityScale,
            Color signAccentColor,
            float crowdDensityScale,
            BarSurfaceSetKind surfaceSet)
        {
            District = district;
            Mood = mood;
            DisplayNameKey = displayNameKey ?? string.Empty;
            CounterWoodTint = counterWoodTint;
            WallTint = wallTint;
            PendantColor = pendantColor;
            PendantIntensityScale = pendantIntensityScale;
            SignAccentColor = signAccentColor;
            CrowdDensityScale = crowdDensityScale;
            SurfaceSet = surfaceSet;
        }

        public CityDistrictKind District { get; }
        public BarDistrictMood Mood { get; }
        public string DisplayNameKey { get; }
        public Color CounterWoodTint { get; }
        public Color WallTint { get; }
        public Color PendantColor { get; }
        public float PendantIntensityScale { get; }
        public Color SignAccentColor { get; }
        public float CrowdDensityScale { get; }
        public BarSurfaceSetKind SurfaceSet { get; }

        // These derived colours keep the catalog compact while giving
        // the world builder a complete, coherent material family. The
        // broad value steps are deliberate: they survive the 640x360
        // composite better than small texture or normal-map changes.
        public Color FloorTint =>
            Mood == BarDistrictMood.Household
                ? new Color(0.14f, 0.06f, 0.042f)
                : Color.Lerp(CounterWoodTint, WallTint, 0.22f);
        public Color CeilingTint =>
            Color.Lerp(WallTint, Color.black, 0.78f);
        public Color WallPanelTint =>
            Color.Lerp(WallTint, Color.black, 0.58f);
        public Color DarkWoodTint =>
            Color.Lerp(CounterWoodTint, Color.black, 0.28f);
        public Color WoodTint =>
            Mood == BarDistrictMood.Household
                ? new Color(0.16f, 0.055f, 0.028f)
                : Color.Lerp(CounterWoodTint, WallTint, 0.25f);
        public Color UpholsteryTint =>
            Mood == BarDistrictMood.Household
                ? new Color(0.30f, 0.035f, 0.045f)
                : Color.Lerp(WallTint, SignAccentColor, 0.12f);
        public Color MetalTint =>
            Mood == BarDistrictMood.Memory
                ? new Color(0.62f, 0.34f, 0.13f)
                : Mood == BarDistrictMood.Household
                    ? new Color(0.86f, 0.46f, 0.14f)
                    : Mood == BarDistrictMood.AfterShift
                        ? new Color(0.32f, 0.40f, 0.38f)
                        : new Color(0.52f, 0.18f, 0.44f);
        public Color GlassTint =>
            Color.Lerp(
                new Color(0.055f, 0.18f, 0.19f),
                PendantColor,
                0.20f);
        public Color SignGlowColor => SignAccentColor * 2.6f;
    }

    /// <summary>
    /// Resolves which of the four bar districts a lot belongs to and
    /// serves its identity. Districts that carry no bar (park, river
    /// banks, service kinds) normalize to Nightlife — the safe
    /// default the direct-loaded bar scene has always effectively
    /// been.
    /// </summary>
    public static class BarDistrictIdentityCatalog
    {
        public const CityDistrictKind FallbackDistrict =
            CityDistrictKind.Nightlife;

        public static CityDistrictKind Normalize(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                case CityDistrictKind.Residential:
                case CityDistrictKind.Industrial:
                case CityDistrictKind.Nightlife:
                    return district;
                default:
                    return FallbackDistrict;
            }
        }

        public static BarDistrictIdentity Get(
            CityDistrictKind district)
        {
            switch (Normalize(district))
            {
                case CityDistrictKind.OldTown:
                    return new BarDistrictIdentity(
                        CityDistrictKind.OldTown,
                        BarDistrictMood.Memory,
                        "bar.district.oldtown",
                        new Color(0.12f, 0.060f, 0.025f),
                        new Color(0.20f, 0.135f, 0.075f),
                        new Color(0.96f, 0.42f, 0.16f),
                        0.82f,
                        new Color(0.80f, 0.32f, 0.10f),
                        1f,
                        BarSurfaceSetKind.None);
                case CityDistrictKind.Residential:
                    // «Огонёк» — the bar for people without money:
                    // worn surfaces, cheap incandescent bulbs a step
                    // warmer and dimmer than the shared amber.
                    return new BarDistrictIdentity(
                        CityDistrictKind.Residential,
                        BarDistrictMood.Household,
                        "bar.district.residential",
                        new Color(0.075f, 0.024f, 0.017f),
                        new Color(0.29f, 0.075f, 0.075f),
                        new Color(1.0f, 0.54f, 0.20f),
                        0.9f,
                        new Color(1.0f, 0.62f, 0.28f),
                        1f,
                        BarSurfaceSetKind.Worn);
                case CityDistrictKind.Industrial:
                    return new BarDistrictIdentity(
                        CityDistrictKind.Industrial,
                        BarDistrictMood.AfterShift,
                        "bar.district.industrial",
                        new Color(0.065f, 0.075f, 0.070f),
                        new Color(0.12f, 0.145f, 0.14f),
                        new Color(0.82f, 0.92f, 0.72f),
                        1.08f,
                        new Color(1.0f, 0.42f, 0.08f),
                        1f,
                        BarSurfaceSetKind.None);
                default:
                    return new BarDistrictIdentity(
                        CityDistrictKind.Nightlife,
                        BarDistrictMood.Escape,
                        "bar.district.nightlife",
                        new Color(0.055f, 0.025f, 0.090f),
                        new Color(0.19f, 0.045f, 0.18f),
                        new Color(0.20f, 0.70f, 1.0f),
                        0.98f,
                        new Color(1.0f, 0.12f, 0.58f),
                        1f,
                        BarSurfaceSetKind.None);
            }
        }
    }
}
