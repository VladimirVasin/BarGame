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
    public static class LakeFishermanQuips
    {
        public static readonly string[] LineKeys =
        {
            "lake.fisherman.line.01",
            "lake.fisherman.line.02",
            "lake.fisherman.line.03",
            "lake.fisherman.line.04",
            "lake.fisherman.line.05",
            "lake.fisherman.line.06",
            "lake.fisherman.line.07",
            "lake.fisherman.line.08",
            "lake.fisherman.line.09",
            "lake.fisherman.line.10",
            "lake.fisherman.line.11",
            "lake.fisherman.line.12",
            "lake.fisherman.line.13",
            "lake.fisherman.line.14",
            "lake.fisherman.line.15"
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
