using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Every exterior electric glow that is not a facade window: the
    /// nightlife neon and backlit-sign decoration batches, the
    /// supermarket sign, the home porch light, the site lamps. Builders
    /// register each renderer with its lit colour, and the same
    /// night-factor path that drives the lamp bulbs breathes them
    /// between the §20 day floor and full glow. Nothing electric ever
    /// reads dead: the city is overcast and foggy at noon too, and the
    /// law says the day takes a third off a fixture, no more. Working
    /// instruments (traffic signals, the weighbridge indicator) and the
    /// authored always-on yard spotlight stay outside the registry on
    /// purpose.
    /// </summary>
    internal static class CityNightGlowRegistry
    {
        /// <summary>
        /// What a glow keeps under the day sky. This was `0.10` - "a
        /// dead tube... enough hue to read what it is, no glow" - and
        /// the §20 law repealed the dead tube outright: every fixture
        /// gives at least two thirds of its night strength at noon.
        /// </summary>
        public const float DeadGlowFraction =
            GameTimeDayNightRules.DayFixtureFloor;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly List<CityLightHalo> halos =
            new List<CityLightHalo>();
        private static readonly MaterialPropertyBlock properties =
            new MaterialPropertyBlock();
        private static float nightFactor = 1f;

        public static float NightFactor => nightFactor;
        public static int Count => entries.Count;

        public static void Register(Renderer renderer, Color litColor)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var entry = new Entry(renderer, litColor);
            entries.Add(entry);
            Apply(entry);
        }

        /// <summary>
        /// A fog halo with no Light of its own — the river's waterside
        /// lanterns burn this way: too many for the pooled budget, but
        /// a bare emissive lens is a couple of pixels the fog swallows,
        /// where the halo billboard is the blurred ball of light a
        /// lamp actually is at a distance in fog. Follows the same
        /// night factor as every electric glow: dead by day, full at
        /// night.
        /// </summary>
        public static void RegisterHalo(CityLightHalo halo)
        {
            if (halo == null)
            {
                throw new ArgumentNullException(nameof(halo));
            }

            halos.Add(halo);
            halo.SetIntensityFactor(
                GameTimeDayNightRules.FixtureFactor(nightFactor));
        }

        public static void SetNightFactor(float factor)
        {
            float clamped = Mathf.Clamp01(factor);
            if (clamped.Equals(nightFactor))
            {
                return;
            }

            nightFactor = clamped;
            CityWaterResources.SetNightFactor(nightFactor);
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index].Renderer == null)
                {
                    entries.RemoveAt(index);
                    continue;
                }

                Apply(entries[index]);
            }

            // "И туманный ореол вокруг него не снимается никогда" - the
            // halo rides the same fixture floor as the glow it belongs to.
            float haloFactor =
                GameTimeDayNightRules.FixtureFactor(nightFactor);
            for (int index = halos.Count - 1; index >= 0; index--)
            {
                if (halos[index] == null)
                {
                    halos.RemoveAt(index);
                    continue;
                }

                halos[index].SetIntensityFactor(haloFactor);
            }
        }

        private static void Apply(in Entry entry)
        {
            Color dead = new Color(
                entry.LitColor.r * DeadGlowFraction,
                entry.LitColor.g * DeadGlowFraction,
                entry.LitColor.b * DeadGlowFraction,
                entry.LitColor.a);
            Color color = Color.Lerp(dead, entry.LitColor, nightFactor);
            properties.Clear();
            entry.Renderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            entry.Renderer.SetPropertyBlock(properties);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEntries()
        {
            entries.Clear();
            halos.Clear();
            nightFactor = 1f;
        }

        private readonly struct Entry
        {
            public Entry(Renderer renderer, Color litColor)
            {
                Renderer = renderer;
                LitColor = litColor;
            }

            public Renderer Renderer { get; }
            public Color LitColor { get; }
        }
    }
}
