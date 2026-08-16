using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Every runtime cloth the exterior wind should push. PhysX cloth
    /// ignores WindZone entirely and only reads its own acceleration
    /// vectors, so this registry is the single bridge between the
    /// deterministic weather wind and the cloth simulation: builders
    /// register each Cloth once and the weather controller writes the
    /// current wind sample every frame.
    /// </summary>
    internal static class CityClothWindRegistry
    {
        /// <summary>
        /// Steady push in meters per second squared at full wind
        /// strength; kept below gravity so rags lean instead of flying
        /// horizontal.
        /// </summary>
        public const float ClothAccelerationAtFullStrength = 7.5f;

        /// <summary>Gust jitter relative to the steady push.</summary>
        public const float RandomAccelerationFraction = 0.6f;

        /// <summary>Small upward jitter that makes hems flutter.</summary>
        public const float RandomLiftFraction = 0.2f;

        private static readonly List<Cloth> entries = new List<Cloth>();
        private static WindSample lastWind = new WindSample(0f, 0f);

        public static int Count => entries.Count;

        public static void Register(Cloth cloth)
        {
            if (cloth == null)
            {
                throw new ArgumentNullException(nameof(cloth));
            }

            entries.Add(cloth);
            Apply(cloth, lastWind);
        }

        public static void SetWind(WindSample sample)
        {
            lastWind = sample;
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index] == null)
                {
                    entries.RemoveAt(index);
                    continue;
                }

                Apply(entries[index], sample);
            }
        }

        private static void Apply(Cloth cloth, in WindSample wind)
        {
            float push =
                wind.Strength01 * ClothAccelerationAtFullStrength;
            Vector3 steady = wind.HorizontalDirection * push;
            cloth.externalAcceleration = steady;
            cloth.randomAcceleration =
                (steady * RandomAccelerationFraction) +
                (Vector3.up * (push * RandomLiftFraction));
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEntries()
        {
            entries.Clear();
            lastWind = new WindSample(0f, 0f);
        }
    }
}
