using System;

namespace BarPromenade
{
    /// <summary>
    /// The street's one shared pool of insults: twenty localized lines in
    /// the voice of the anonymous passer-by, dealt out of a bag that is
    /// shuffled on a seeded stream — the street works through all twenty
    /// before any of them comes round again. Pure — the walk owns the
    /// place in the bag, the session state owns the walk, and the register
    /// test owns the words.
    ///
    /// One pool for every design that speaks, on purpose: the six roaming
    /// bodies are anonymous copies on the promenade, and the bible's §21
    /// treats «прохожий» as one role with one voice. Per-design pools
    /// would cost a hundred lines to say the same thing five ways.
    /// </summary>
    public static class CityPedestrianInsultLines
    {
        public const int LineCount = 20;

        /// <summary>The bubble is 180 logical pixels wide; a third row
        /// pushes the panel over the walker's own head. The park quarrel's
        /// number, for the same panel.</summary>
        public const int MaximumLineLength = 48;

        public static readonly string[] LineKeys =
        {
            "city.pedestrian.insult.01",
            "city.pedestrian.insult.02",
            "city.pedestrian.insult.03",
            "city.pedestrian.insult.04",
            "city.pedestrian.insult.05",
            "city.pedestrian.insult.06",
            "city.pedestrian.insult.07",
            "city.pedestrian.insult.08",
            "city.pedestrian.insult.09",
            "city.pedestrian.insult.10",
            "city.pedestrian.insult.11",
            "city.pedestrian.insult.12",
            "city.pedestrian.insult.13",
            "city.pedestrian.insult.14",
            "city.pedestrian.insult.15",
            "city.pedestrian.insult.16",
            "city.pedestrian.insult.17",
            "city.pedestrian.insult.18",
            "city.pedestrian.insult.19",
            "city.pedestrian.insult.20"
        };

        /// <summary>Seed stream from the city seed — the watchman's hash
        /// idiom with this pool's own salt, never zero so xorshift never
        /// sticks.</summary>
        public static uint CreateState(int citySeed)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^
                             0x494E534Cu; // "INSL"
                return state == 0u ? 0x9E3779B9u : state;
            }
        }

        /// <summary>
        /// The same stream moved off the city seed by a salt the session
        /// draws once. The city seed is a compile-time constant, so on it
        /// alone every playthrough of the same city heard the twenty lines
        /// in one fixed order; the salt is what makes tonight sound unlike
        /// last night. Explicit, so a fixture can pin it.
        /// </summary>
        public static uint CreateState(int citySeed, int sessionSalt)
        {
            unchecked
            {
                return CreateState(
                    citySeed ^ (int)((uint)sessionSalt * 2246822519u));
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

        /// <summary>
        /// Lays the whole pool out in a fresh order — Fisher–Yates on the
        /// seeded stream, so every round is a permutation and a man hears
        /// all twenty lines before he hears one of them twice. A round can
        /// only repeat at its own seam, and the head is swapped away from
        /// <paramref name="previousIndex"/> rather than reshuffled, which
        /// would bias everything behind it.
        /// </summary>
        public static void Shuffle(ref uint state, int[] order, int previousIndex)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            if (order.Length != LineKeys.Length)
            {
                throw new ArgumentException(
                    "The bag holds the whole pool and nothing else.",
                    nameof(order));
            }

            for (int index = 0; index < order.Length; index++)
            {
                order[index] = index;
            }

            for (int index = order.Length - 1; index > 0; index--)
            {
                int pick = (int)(NextRandomState(ref state) %
                                 (uint)(index + 1));
                int held = order[index];
                order[index] = order[pick];
                order[pick] = held;
            }

            if (order.Length > 1 && order[0] == previousIndex)
            {
                int swap = 1 + (int)(NextRandomState(ref state) %
                                     (uint)(order.Length - 1));
                int head = order[0];
                order[0] = order[swap];
                order[swap] = head;
            }
        }
    }
}
