namespace BarPromenade
{
    /// <summary>
    /// The street's one shared pool of insults: twenty localized lines in
    /// the voice of the anonymous passer-by, drawn by a deterministic
    /// seeded walk that never says the same thing twice running. Pure —
    /// the insult controller owns the state, the tests own the
    /// distribution, and the register test owns the words.
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

        public static uint NextRandomState(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return value;
        }

        /// <summary>The next line index: uniform over the pool, but a draw
        /// landing on the previous line slides to its neighbour — the
        /// street never says the same thing twice running.</summary>
        public static int NextIndex(ref uint state, int previousIndex)
        {
            if (LineKeys.Length == 0)
            {
                return -1;
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
