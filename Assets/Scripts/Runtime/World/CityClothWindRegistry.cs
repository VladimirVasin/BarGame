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
        private static Vector3 acceleration, flutter;

        public static int Count => entries.Count;

        public static void Register(Cloth cloth)
        {
            if (cloth == null)
            {
                throw new ArgumentNullException(nameof(cloth));
            }

            entries.Add(cloth);
            Apply(cloth);
        }

        public static void SetWind(WindSample sample) => UpdateWind(sample, false);

        internal static void SetCityWind(WindSample gust) => UpdateWind(gust, true);

        private static void UpdateWind(WindSample sample, bool city)
        {
            float push = sample.Strength01 * ClothAccelerationAtFullStrength;
            Vector3 steady = sample.HorizontalDirection * push;
            // Let the common gust move the hem. Leave only a small random solver input.
            acceleration = city ? steady * 5.2f + Vector3.up * (3.2f * sample.Strength01) : steady;
            flutter = steady * (city ? .13f : RandomAccelerationFraction) + Vector3.up * (push * RandomLiftFraction);
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                Cloth cloth = entries[index];
                if (cloth == null)
                {
                    entries.RemoveAt(index);
                    continue;
                }

                if (cloth.enabled && cloth.gameObject.activeInHierarchy) Apply(cloth);
            }
        }

        private static void Apply(Cloth cloth)
        {
            cloth.externalAcceleration = acceleration;
            cloth.randomAcceleration = flutter;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEntries()
        {
            entries.Clear();
            acceleration = flutter = Vector3.zero;
        }
    }

    /// <summary>
    /// Every runtime cloth a walking body should part instead of clip
    /// through. PhysX cloth ignores scene collision entirely and only
    /// presses against the capsules listed on each Cloth, so this
    /// registry is the second bridge beside the wind one: builders
    /// register each walk-through cloth once, bodies register their
    /// capsule once, and either arrival rewrites the cloth's capsule
    /// list.
    /// </summary>
    internal static class CityClothBodyRegistry
    {
        private static readonly List<Cloth> cloths = new List<Cloth>();
        private static readonly List<CapsuleCollider> bodies =
            new List<CapsuleCollider>();

        public static int ClothCount => cloths.Count;
        public static int BodyCount => bodies.Count;

        public static void RegisterCloth(Cloth cloth)
        {
            if (cloth == null)
            {
                throw new ArgumentNullException(nameof(cloth));
            }

            cloths.Add(cloth);
            Apply(cloth);
        }

        public static void RegisterBody(CapsuleCollider body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            bodies.Add(body);
            for (int index = cloths.Count - 1; index >= 0; index--)
            {
                if (cloths[index] == null)
                {
                    cloths.RemoveAt(index);
                    continue;
                }

                Apply(cloths[index]);
            }
        }

        private static void Apply(Cloth cloth)
        {
            for (int index = bodies.Count - 1; index >= 0; index--)
            {
                if (bodies[index] == null)
                {
                    bodies.RemoveAt(index);
                }
            }

            cloth.capsuleColliders = bodies.ToArray();
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEntries()
        {
            cloths.Clear();
            bodies.Clear();
        }
    }
}
