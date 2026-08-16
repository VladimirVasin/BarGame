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
        /// <summary>The shared flat-tint interior of today.</summary>
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
    /// soundscape, naming) reads from one place. The Residential
    /// entry is the first fully authored one; the rest still share
    /// the current shared-bar values until their own art passes.
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

        // The shared values of today's one bar interior; every entry
        // starts here so wiring the catalog changes nothing visually
        // until the per-district passes author real differences.
        private static readonly Color SharedCounterWood =
            new Color(0.30f, 0.17f, 0.09f);
        private static readonly Color SharedWall =
            new Color(0.22f, 0.13f, 0.10f);
        // Exactly the planner's authored counter-pendant amber, so
        // wiring the identity changes nothing for the shared bars.
        private static readonly Color SharedPendant =
            new Color(1.0f, 0.62f, 0.28f);
        private static readonly Color SharedSignAccent =
            new Color(1.0f, 0.62f, 0.28f);

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
                        SharedCounterWood,
                        SharedWall,
                        SharedPendant,
                        1f,
                        SharedSignAccent,
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
                        SharedCounterWood,
                        SharedWall,
                        new Color(1.0f, 0.54f, 0.20f),
                        0.9f,
                        SharedSignAccent,
                        1f,
                        BarSurfaceSetKind.Worn);
                case CityDistrictKind.Industrial:
                    return new BarDistrictIdentity(
                        CityDistrictKind.Industrial,
                        BarDistrictMood.AfterShift,
                        "bar.district.industrial",
                        SharedCounterWood,
                        SharedWall,
                        SharedPendant,
                        1f,
                        SharedSignAccent,
                        1f,
                        BarSurfaceSetKind.None);
                default:
                    return new BarDistrictIdentity(
                        CityDistrictKind.Nightlife,
                        BarDistrictMood.Escape,
                        "bar.district.nightlife",
                        SharedCounterWood,
                        SharedWall,
                        SharedPendant,
                        1f,
                        SharedSignAccent,
                        1f,
                        BarSurfaceSetKind.None);
            }
        }
    }
}
