using System;

namespace BarPromenade
{
    /// <summary>
    /// The watchman's snide repertoire: a deterministic, seeded walk
    /// over the localized line keys that never repeats the same quip
    /// twice in a row. Pure — the interaction stub owns the state and
    /// the tests own the distribution.
    /// </summary>
    public static class CemeteryWatchmanQuips
    {
        public static readonly string[] LineKeys =
        {
            "cemetery.watchman.line.01",
            "cemetery.watchman.line.02",
            "cemetery.watchman.line.03",
            "cemetery.watchman.line.04",
            "cemetery.watchman.line.05",
            "cemetery.watchman.line.06",
            "cemetery.watchman.line.07",
            "cemetery.watchman.line.08",
            "cemetery.watchman.line.09",
            "cemetery.watchman.line.10",
            "cemetery.watchman.line.11",
            "cemetery.watchman.line.12",
            "cemetery.watchman.line.13",
            "cemetery.watchman.line.14",
            "cemetery.watchman.line.15"
        };

        /// <summary>Seed stream from the city seed — the mourner's
        /// hash idiom, never zero so xorshift never sticks.</summary>
        public static uint CreateState(int citySeed)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             0x57515053u; // "WQPS"
                return state == 0u ? 0x9E3779B9u : state;
            }
        }

        public static uint NextRandomState(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        /// <summary>The next quip index: uniform over the pool, but a
        /// draw landing on the previous quip slides to its neighbour —
        /// he never says the same thing twice running.</summary>
        public static int NextIndex(ref uint state, int previousIndex)
        {
            if (LineKeys.Length == 0)
            {
                throw new InvalidOperationException(
                    "The watchman has no lines to speak.");
            }

            int index = (int)(NextRandomState(ref state) %
                              (uint)LineKeys.Length);
            if (index == previousIndex && LineKeys.Length > 1)
            {
                index = (index + 1) % LineKeys.Length;
            }

            return index;
        }
    }
}
