using System;

namespace BarPromenade
{
    /// <summary>
    /// The Ferryman's repertoire: a deterministic, seeded walk over the
    /// localized line keys that never repeats the same answer twice in a
    /// row. Pure - the interaction owns the state and the tests own the
    /// distribution. Structurally the fisherman's; in register, a third
    /// thing again, and the rules are rules rather than a mood:
    ///
    /// * he NEVER offers to take anybody anywhere. Not once, in any line.
    ///   The offer is on the menu and only on the menu, which is both the
    ///   joke and an honest interface: nothing in his mouth promises a
    ///   thing the game does not have. A test greps the pool for it;
    /// * he addresses the player directly - "ты" - which is the exact
    ///   inversion of the fisherman, who never acknowledges anyone;
    /// * two short clauses at most, level, no exclamations. Charon does
    ///   not refuse; he mentions that you have not paid, or that the boat
    ///   is not going yet;
    /// * he talks about waiting, the route, the fare and the fact that it
    ///   is not time. Never about where the route goes.
    /// </summary>
    public static class LastRouteFerrymanQuips
    {
        public static readonly string[] LineKeys =
        {
            "lastroute.ferryman.line.01",
            "lastroute.ferryman.line.02",
            "lastroute.ferryman.line.03",
            "lastroute.ferryman.line.04",
            "lastroute.ferryman.line.05",
            "lastroute.ferryman.line.06",
            "lastroute.ferryman.line.07",
            "lastroute.ferryman.line.08",
            "lastroute.ferryman.line.09",
            "lastroute.ferryman.line.10",
            "lastroute.ferryman.line.11",
            "lastroute.ferryman.line.12"
        };

        /// <summary>Seed stream from the city seed - the watchman's hash
        /// idiom, never zero so xorshift never sticks.</summary>
        public static uint CreateState(int citySeed)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             0x4652524Du; // "FRRM"
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

        /// <summary>The next answer: uniform over the pool, but a draw
        /// landing on the previous one slides to its neighbour.</summary>
        public static int NextIndex(ref uint state, int previousIndex)
        {
            if (LineKeys.Length == 0)
            {
                throw new InvalidOperationException(
                    "The Ferryman has no lines to speak.");
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
