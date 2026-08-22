using System;

namespace BarPromenade
{
    /// <summary>
    /// The fisherman's repertoire: a deterministic, seeded walk over the
    /// localized line keys that never repeats the same answer twice in a
    /// row. Pure — the interaction stub owns the state and the tests own
    /// the distribution. Structurally the watchman's; in register, his
    /// exact inversion, and the inversion is a rule rather than a mood:
    ///
    /// * the second person is forbidden. He never says "ты", never
    ///   addresses the player, never asks them anything;
    /// * no line mentions the hero, the city, or anything on land except
    ///   the weather;
    /// * one or two short clauses. The watchman's lines run long and
    ///   land like punchlines; these land like nothing;
    /// * at least three are pure weather, at least three are
    ///   superstition, and at least two flatly contradict another line
    ///   in the pool.
    ///
    /// The joke is that the player is being answered, at length, by a
    /// man who has not looked round.
    /// </summary>
    public static class SeacoastFishermanQuips
    {
        public static readonly string[] LineKeys =
        {
            "seacoast.fisherman.line.01",
            "seacoast.fisherman.line.02",
            "seacoast.fisherman.line.03",
            "seacoast.fisherman.line.04",
            "seacoast.fisherman.line.05",
            "seacoast.fisherman.line.06",
            "seacoast.fisherman.line.07",
            "seacoast.fisherman.line.08",
            "seacoast.fisherman.line.09",
            "seacoast.fisherman.line.10",
            "seacoast.fisherman.line.11",
            "seacoast.fisherman.line.12",
            "seacoast.fisherman.line.13",
            "seacoast.fisherman.line.14",
            "seacoast.fisherman.line.15"
        };

        /// <summary>Seed stream from the city seed — the watchman's
        /// hash idiom, never zero so xorshift never sticks.</summary>
        public static uint CreateState(int citySeed)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             0x46515053u; // "FQPS"
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
                    "The fisherman has no lines to speak.");
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
