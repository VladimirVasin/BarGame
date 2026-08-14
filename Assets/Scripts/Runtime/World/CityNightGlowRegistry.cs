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
    /// night-factor path that drives the lamp bulbs lerps them between
    /// a dead fixture and full glow — nothing electric burns under the
    /// day sky. Working instruments (traffic signals, the weighbridge
    /// indicator) and the authored always-on yard spotlight stay
    /// outside the registry on purpose.
    /// </summary>
    internal static class CityNightGlowRegistry
    {
        // A dead tube or unlit lightbox keeps a tenth of its colour:
        // enough hue to read what it is, no glow.
        public const float DeadGlowFraction = 0.10f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        private static readonly List<Entry> entries = new List<Entry>();
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

        public static void SetNightFactor(float factor)
        {
            float clamped = Mathf.Clamp01(factor);
            if (clamped.Equals(nightFactor))
            {
                return;
            }

            nightFactor = clamped;
            CityRiverResources.SetNightFactor(nightFactor);
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index].Renderer == null)
                {
                    entries.RemoveAt(index);
                    continue;
                }

                Apply(entries[index]);
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
