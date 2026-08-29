using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The few authored realtime lights that live on district sites
    /// outside the pooled street/bar atmosphere — the drying yard's
    /// pole floodlight, the cemetery lamps, the pier and hut bulbs.
    /// Builders register each light with its full night intensity and
    /// optional fog halo, and the same night-factor path that drives
    /// the lamp bulbs breathes them down to the §20 floor: every
    /// fixture burns always, the day takes at most a third off it, and
    /// the fog halo is never taken away. The authored always-on
    /// bar-side yard spotlight deliberately stays outside this
    /// registry, exactly as it stays outside the glow registry.
    /// </summary>
    internal static class CityNightSiteLightRegistry
    {
        /// <summary>
        /// Kept for the tests that read it; under the §20 law no
        /// registered light is ever disabled, so nothing compares
        /// against it any more.
        /// </summary>
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
            // The §20 floor overrides every authored day intensity from
            // below: a fixture gives at least two thirds of its night
            // strength at noon, whatever the builder asked for. The
            // authored value survives only where it is MORE generous.
            // Night-only registrations (day zero) are the same fixtures
            // seen before the law - they no longer exist as a behaviour,
            // only as a calling convention.
            float lawFloor = entry.NightIntensity *
                             GameTimeDayNightRules.DayFixtureFloor;
            entry.Light.intensity = Mathf.Lerp(
                Mathf.Max(entry.DayIntensity, lawFloor),
                entry.NightIntensity,
                nightFactor);
            entry.Light.enabled = true;
            if (entry.Halo != null)
            {
                // "И туманный ореол вокруг него не снимается никогда."
                // It was fog-follows-the-night-factor here, and the law
                // repealed that: the fog is there at noon too.
                entry.Halo.SetIntensityFactor(
                    GameTimeDayNightRules.FixtureFactor(nightFactor));
                entry.Halo.SetVisible(true);
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
