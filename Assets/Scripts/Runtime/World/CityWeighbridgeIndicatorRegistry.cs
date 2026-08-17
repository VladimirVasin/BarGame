using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The weighbridge's mechanical indicator needle, registered by
    /// the world builder and read back by the needle controller so
    /// the scale can answer weight standing on its deck — the
    /// drying-yard carpet registry pattern with a Transform slot.
    /// The default city carries exactly one weighbridge, so a single
    /// fixed id is enough, like the carpets' "south"/"north".
    /// </summary>
    public static class CityWeighbridgeIndicatorRegistry
    {
        public const string NeedleId = "industrial";

        private static readonly Dictionary<string, Transform> needles =
            new Dictionary<string, Transform>(StringComparer.Ordinal);

        public static int Count => needles.Count;

        public static void Register(string id, Transform needle)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException(
                    "A needle registration needs an id.",
                    nameof(id));
            }

            if (needle == null)
            {
                throw new ArgumentNullException(nameof(needle));
            }

            needles[id] = needle;
        }

        public static Transform Find(string id)
        {
            if (string.IsNullOrEmpty(id) ||
                !needles.TryGetValue(id, out Transform needle) ||
                needle == null)
            {
                return null;
            }

            return needle;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEntries()
        {
            needles.Clear();
        }
    }
}
