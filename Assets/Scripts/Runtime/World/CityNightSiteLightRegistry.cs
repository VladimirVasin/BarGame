using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The few authored realtime lights that live on district sites
    /// outside the pooled street/bar atmosphere — currently the drying
    /// yard's pole floodlight. Builders register each light with its
    /// full night intensity and optional fog halo, and the same
    /// night-factor path that drives the lamp bulbs scales and disables
    /// them, so nothing electric burns under the day sky. The authored
    /// always-on bar-side yard spotlight deliberately stays outside
    /// this registry, exactly as it stays outside the glow registry.
    ///
    /// A light registered with a non-zero day intensity is the one
    /// exception: it never switches off, only dims toward that floor —
    /// for bulbs nobody bothers to switch, like the one over the
    /// cemetery lodge's door. Its fog halo still fades out by day.
    /// </summary>
    internal static class CityNightSiteLightRegistry
    {
        /// <summary>Below this factor a night-only light is fully
        /// disabled instead of idling at a fraction of a lumen.</summary>
        public const float EnableThreshold = 0.02f;

        private static readonly List<Entry> entries = new List<Entry>();
        private static float nightFactor = 1f;

        public static float NightFactor => nightFactor;
        public static int Count => entries.Count;

        public static void Register(
            Light light,
            float nightIntensity,
            CityLightHalo halo)
        {
            Register(light, nightIntensity, 0f, halo);
        }

        /// <summary>
        /// Registers a light that dims to <paramref name="dayIntensity"/>
        /// under the day sky instead of switching off. Pass zero for the
        /// ordinary night-only behaviour.
        /// </summary>
        public static void Register(
            Light light,
            float nightIntensity,
            float dayIntensity,
            CityLightHalo halo)
        {
            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }

            var entry = new Entry(
                light,
                nightIntensity,
                Mathf.Max(0f, dayIntensity),
                halo);
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
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index].Light == null)
                {
                    entries.RemoveAt(index);
                    continue;
                }

                Apply(entries[index]);
            }
        }

        private static void Apply(in Entry entry)
        {
            // The halo is fog, not filament: it follows the night
            // factor even for a bulb that never goes out.
            bool nightLit = nightFactor > EnableThreshold;
            entry.Light.intensity = Mathf.Lerp(
                entry.DayIntensity,
                entry.NightIntensity,
                nightFactor);
            entry.Light.enabled = nightLit || entry.DayIntensity > 0f;
            if (entry.Halo != null)
            {
                entry.Halo.SetIntensityFactor(nightFactor);
                entry.Halo.SetVisible(nightLit);
            }
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
            public Entry(
                Light light,
                float nightIntensity,
                float dayIntensity,
                CityLightHalo halo)
            {
                Light = light;
                NightIntensity = nightIntensity;
                DayIntensity = dayIntensity;
                Halo = halo;
            }

            public Light Light { get; }
            public float NightIntensity { get; }
            public float DayIntensity { get; }
            public CityLightHalo Halo { get; }
        }
    }
}
