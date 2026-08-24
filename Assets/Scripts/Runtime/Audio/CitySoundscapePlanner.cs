using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>
    /// Validates and orders physical city sound descriptors. It performs no
    /// scene lookup and creates no fallback anchors.
    /// </summary>
    public static class CitySoundscapePlanner
    {
        public static CitySoundscapePlan Create(
            int citySeed,
            IEnumerable<CitySoundSourceDescriptor> physicalSources)
        {
            if (physicalSources == null)
            {
                throw new ArgumentNullException(nameof(physicalSources));
            }

            var sources = new List<CitySoundSourceDescriptor>();
            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CitySoundSourceDescriptor source in physicalSources)
            {
                CitySoundSourceRules.ValidateOrThrow(source);
                if (!stableIds.Add(source.StableId))
                {
                    throw new ArgumentException(
                        $"Duplicate city sound stable ID '{source.StableId}'.",
                        nameof(physicalSources));
                }

                sources.Add(source);
            }

            sources.Sort((first, second) =>
                string.CompareOrdinal(first.StableId, second.StableId));
            return new CitySoundscapePlan(citySeed, sources, null);
        }

        public static CitySoundscapePlan CreateForDistrict(
            int citySeed,
            CityDistrictKind district,
            IEnumerable<CitySoundSourceDescriptor> physicalSources)
        {
            return CreateForDistrict(
                citySeed,
                CitySoundDistrictProfiles.Get(district),
                physicalSources);
        }

        public static CitySoundscapePlan CreateForDistrict(
            int citySeed,
            CitySoundDistrictProfile profile,
            IEnumerable<CitySoundSourceDescriptor> physicalSources)
        {
            return Filter(Create(citySeed, physicalSources), profile);
        }

        public static CitySoundscapePlan Filter(
            CitySoundscapePlan sourcePlan,
            CitySoundDistrictProfile profile)
        {
            if (sourcePlan == null)
            {
                throw new ArgumentNullException(nameof(sourcePlan));
            }

            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var selected = new List<CitySoundSourceDescriptor>();
            for (int index = 0; index < sourcePlan.Sources.Count; index++)
            {
                CitySoundSourceDescriptor source =
                    sourcePlan.Sources[index];
                if (profile.Allows(source))
                {
                    selected.Add(source);
                }
            }

            return new CitySoundscapePlan(
                sourcePlan.CitySeed,
                selected,
                profile);
        }
    }

    /// <summary>
    /// Explicit platform-stable hashing for schedule variation. It never uses
    /// string.GetHashCode or UnityEngine.Random.
    /// </summary>
    public static class CitySoundStableHash
    {
        public static uint String(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string safeValue = value ?? string.Empty;
                for (int index = 0; index < safeValue.Length; index++)
                {
                    char character = safeValue[index];
                    hash ^= (byte)character;
                    hash *= 16777619u;
                    hash ^= (byte)(character >> 8);
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        public static uint Combine(uint first, uint second)
        {
            unchecked
            {
                uint hash = first ^
                    (second + 0x9E3779B9u +
                     (first << 6) + (first >> 2));
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return hash;
            }
        }

        public static uint SourceEvent(
            int citySeed,
            string sourceStableId,
            uint eventOrdinal)
        {
            if (string.IsNullOrWhiteSpace(sourceStableId))
            {
                throw new ArgumentException(
                    "A schedule hash needs a source stable ID.",
                    nameof(sourceStableId));
            }

            uint source = String(sourceStableId);
            uint seeded = Combine(unchecked((uint)citySeed), source);
            return Combine(seeded, eventOrdinal ^ 0x534F554Eu);
        }

        public static float ToUnitFloat(uint hash)
        {
            return (hash & 0x00FFFFFFu) / 16777216f;
        }
    }
}
