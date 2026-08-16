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
