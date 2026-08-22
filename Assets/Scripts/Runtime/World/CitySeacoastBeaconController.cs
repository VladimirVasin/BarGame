using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The beacon's characteristic — its light signature, in the
    /// navigational sense. An occulting light: lit most of its period
    /// with one dark break, which is the signature of a harbour mark
    /// rather than a warning strobe, and the slow trapezoid shoulders
    /// keep the 640x360 composite from reading the cut as flicker.
    ///
    /// Pure and stateless, like the weather: a function of the city
    /// seed and the absolute game minute (one game minute is one real
    /// second), so every scene sees the identical pulse and a test can
    /// pin the whole signature without a scene at all. The seed only
    /// turns the phase, so two cities do not blink in unison.
    /// </summary>
    public static class CitySeacoastBeaconRules
    {
        /// <summary>One full occultation, in game minutes.</summary>
        public const double PeriodMinutes = 6.0;

        /// <summary>The dark break inside each period.</summary>
        public const double DarkMinutes = 1.2;

        /// <summary>The rise and fall shoulders either side of it.</summary>
        public const double ShoulderMinutes = 0.25;

        public static float EvaluateIntensity(
            int seed,
            double absoluteGameMinutes)
        {
            if (double.IsNaN(absoluteGameMinutes) ||
                double.IsInfinity(absoluteGameMinutes))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(absoluteGameMinutes),
                    "Absolute game time must be finite.");
            }

            double phase = Hash(seed) % 65536u / 65535d *
                           PeriodMinutes;
            double t = (absoluteGameMinutes + phase) % PeriodMinutes;
            if (t < 0d)
            {
                t += PeriodMinutes;
            }

            double lit = PeriodMinutes - DarkMinutes;
            if (t >= lit)
            {
                return 0f;
            }

            if (t < ShoulderMinutes)
            {
                return (float)(t / ShoulderMinutes);
            }

            if (t > lit - ShoulderMinutes)
            {
                return (float)((lit - t) / ShoulderMinutes);
            }

            return 1f;
        }

        public static float EvaluateCurrentIntensity()
        {
            return EvaluateIntensity(
                GameSessionState.CitySeed,
                GameSessionState.GameDayIndex *
                GameTimeDayNightRules.MinutesPerDay +
                GameSessionState.GameTimeOfDayMinutes);
        }

        private static uint Hash(int seed)
        {
            unchecked
            {
                uint value = (uint)seed ^ 0x42434F4Eu; // "BCON"
                value *= 16777619u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                return value;
            }
        }
    }

    /// <summary>
    /// Drives the beacon's real light. It deliberately does NOT
    /// register with <see cref="CityNightSiteLightRegistry"/> — the
    /// registry rewrites every registered light's intensity when the
    /// night factor moves, and would fight a per-frame pulse — but it
    /// obeys the same contract by hand: scaled by the registry's own
    /// night factor, disabled below its enable threshold, halo toggled
    /// with the light. The lens's steady emissive glass stays on the
    /// glow registry; a steadily lit lamp room under a pulsing light
    /// is how an occulting lantern reads from shore.
    /// </summary>
    public sealed class CitySeacoastBeaconController : MonoBehaviour
    {
        private Light beaconLight;
        private CityLightHalo halo;
        private float nightIntensity;

        public void Initialize(
            Light light,
            CityLightHalo lightHalo,
            float fullNightIntensity)
        {
            beaconLight = light != null
                ? light
                : throw new ArgumentNullException(nameof(light));
            halo = lightHalo;
            nightIntensity = fullNightIntensity;
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (beaconLight == null)
            {
                return;
            }

            float pulse =
                CitySeacoastBeaconRules.EvaluateCurrentIntensity();
            float value = pulse * CityNightSiteLightRegistry.NightFactor;
            bool lit = value >
                       CityNightSiteLightRegistry.EnableThreshold;
            beaconLight.enabled = lit;
            beaconLight.intensity = nightIntensity * value;
            if (halo != null && halo.gameObject.activeSelf != lit)
            {
                halo.gameObject.SetActive(lit);
            }
        }
    }
}
