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
    /// </summary>
    internal static class CityNightSiteLightRegistry
    {
        /// <summary>Below this factor the light is fully disabled
        /// instead of idling at a fraction of a lumen.</summary>
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
            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }

            var entry = new Entry(light, nightIntensity, halo);
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
            bool lit = nightFactor > EnableThreshold;
            entry.Light.intensity = entry.NightIntensity * nightFactor;
            entry.Light.enabled = lit;
            if (entry.Halo != null)
            {
                entry.Halo.SetIntensityFactor(nightFactor);
                entry.Halo.SetVisible(lit);
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
                CityLightHalo halo)
            {
                Light = light;
                NightIntensity = nightIntensity;
                Halo = halo;
            }

            public Light Light { get; }
            public float NightIntensity { get; }
            public CityLightHalo Halo { get; }
        }
    }
}
