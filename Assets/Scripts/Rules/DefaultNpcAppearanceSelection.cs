using System;
using System.Collections.Generic;

namespace BarPromenade
{
    /// <summary>Stable appearance traversal without engine random state or scene order.</summary>
    public static class DefaultNpcAppearanceSelection
    {
        public static int Index(string key, string channel, int count)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A stable character identity is required.", nameof(key));
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("A selection channel is required.", nameof(channel));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            uint hash = 2166136261u;
            void Append(string value)
            {
                // Explicit UTF-16 bytes keep non-ASCII IDs stable across platforms.
                foreach (char c in value)
                {
                    hash = unchecked((hash ^ (byte)c) * 16777619u);
                    hash = unchecked((hash ^ (byte)(c >> 8)) * 16777619u);
                }
            }
            Append(key); Append("\0"); Append(channel);
            return (int)(hash % (uint)count);
        }

        /// <summary>Prefer an unused visible tuple; after exhaustion choose its least-used valid alternative.</summary>
        public static string[] Choose(string key, IReadOnlyList<string[]> dimensions,
            Func<string[], string> signature, IDictionary<string, int> useCounts)
        {
            if (dimensions == null || dimensions.Count == 0 || signature == null || useCounts == null)
                throw new ArgumentException("Appearance dimensions, signatures and usage are required.");
            int total = 1;
            foreach (string[] dimension in dimensions)
            {
                if (dimension == null || dimension.Length == 0)
                    throw new ArgumentException("Every appearance dimension needs a valid choice.", nameof(dimensions));
                total = checked(total * dimension.Length);
            }
            int start = Index(key, "population", total), bestCount = int.MaxValue;
            string[] best = null;
            string bestSignature = null;
            for (int offset = 0; offset < total; offset++)
            {
                int code = (int)(((long)start + offset) % total);
                var candidate = new string[dimensions.Count];
                for (int index = dimensions.Count - 1; index >= 0; index--)
                {
                    candidate[index] = dimensions[index][code % dimensions[index].Length];
                    code /= dimensions[index].Length;
                }
                string identity = signature(candidate);
                int used = useCounts.TryGetValue(identity, out int value) ? value : 0;
                if (used >= bestCount) continue;
                best = candidate; bestSignature = identity; bestCount = used;
                if (used == 0) break;
            }
            useCounts[bestSignature] = bestCount + 1;
            return best;
        }
    }
}
