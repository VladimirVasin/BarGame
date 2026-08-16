using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The two simulated carpets hung on the drying yard's beating
    /// rack, registered by the world builder and read back by the
    /// babushka factory so each beater's strike can shake the exact
    /// carpet she faces — the cloth-wind-registry pattern with named
    /// slots instead of a broadcast list.
    /// </summary>
    public static class CityDryingYardCarpetRegistry
    {
        public const string SouthCarpetId = "south";
        public const string NorthCarpetId = "north";

        private static readonly Dictionary<string, Cloth> carpets =
            new Dictionary<string, Cloth>(StringComparer.Ordinal);

        public static int Count => carpets.Count;

        public static void Register(string id, Cloth cloth)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException(
                    "A carpet registration needs an id.",
                    nameof(id));
            }

            if (cloth == null)
            {
                throw new ArgumentNullException(nameof(cloth));
            }

            carpets[id] = cloth;
        }

        public static Cloth Find(string id)
        {
            if (string.IsNullOrEmpty(id) ||
                !carpets.TryGetValue(id, out Cloth cloth) ||
                cloth == null)
            {
                return null;
            }

            return cloth;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEntries()
        {
            carpets.Clear();
        }
    }
}
