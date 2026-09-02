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

        /// <summary>
        /// The same man one chapter later, at the far end of the road he
        /// was waiting to drive. The rules do not relax, they move:
        ///
        /// * he still NEVER offers to take anybody anywhere, and up here
        ///   that matters more rather than less: there IS a way back down
        ///   now, and it is on the menu and only on the menu, exactly as
        ///   the way up is on the island. Nothing in his mouth sells it;
        /// * still "ты", still two short clauses, still level;
        /// * and still not one word about where the route goes. He has
        ///   arrived and he says nothing about what he arrived at - what
        ///   he talks about is the car, the cold, the drive, and the fact
        ///   that the waiting is over without anything having replaced it.
        /// </summary>
        public static readonly string[] MountainLineKeys =
        {
            "lastroute.ferryman.mountain.line.01",
            "lastroute.ferryman.mountain.line.02",
            "lastroute.ferryman.mountain.line.03",
            "lastroute.ferryman.mountain.line.04",
            "lastroute.ferryman.mountain.line.05",
            "lastroute.ferryman.mountain.line.06",
            "lastroute.ferryman.mountain.line.07",
            "lastroute.ferryman.mountain.line.08",
            "lastroute.ferryman.mountain.line.09",
            "lastroute.ferryman.mountain.line.10",
            "lastroute.ferryman.mountain.line.11",
            "lastroute.ferryman.mountain.line.12"
        };

        /// <summary>Seed stream from the city seed - the watchman's hash
        /// idiom, never zero so xorshift never sticks.</summary>
        public static uint CreateState(int citySeed)
        {
            return CreateState(citySeed, 0x4652524Du); // "FRRM"
        }

        /// <summary>
        /// The mountain pool walks its own stream. Sharing one with the
        /// island would have the two repertoires march in step off the
        /// same seed, so the same ordinal answer would come up in both
        /// places on the same visit.
        /// </summary>
        public static uint CreateMountainState(int citySeed)
        {
            return CreateState(citySeed, 0x4652504Bu); // "FRPK"
        }

        private static uint CreateState(int citySeed, uint salt)
        {
            unchecked
            {
                uint state = ((uint)citySeed * 2654435761u) ^ salt;
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
            return NextIndex(ref state, previousIndex, LineKeys);
        }

        /// <summary>The same walk over whichever pool he is speaking from.</summary>
        public static int NextIndex(
            ref uint state,
            int previousIndex,
            string[] pool)
        {
            if (pool == null || pool.Length == 0)
            {
                throw new InvalidOperationException(
                    "The Ferryman has no lines to speak.");
            }

            int index = (int)(NextRandomState(ref state) %
                              (uint)pool.Length);
            if (index == previousIndex && pool.Length > 1)
            {
                index = (index + 1) % pool.Length;
            }

            return index;
        }
    }

    /// <summary>
    /// Everything about how one instance of the Ferryman answers: which pool
    /// of small talk he draws from, which stream he draws it on, and what the
    /// second line of his menu asks.
    ///
    /// It exists so the two ends of his road are chosen in ONE place. They are
    /// the same problem seen from opposite sides - the island asks whether you
    /// want to leave the city and the terrace asks whether you want to go back
    /// to it - and the thing that must never drift between them is that the
    /// two pools walk separate streams. Off one stream they march in step and
    /// the same ordinal answer comes up in both places on the same visit.
    /// </summary>
    public readonly struct LastRouteFerrymanVoice
    {
        private LastRouteFerrymanVoice(
            string[] lineKeys,
            string confirmationPromptKey,
            uint quipStream)
        {
            LineKeys = lineKeys;
            ConfirmationPromptKey = confirmationPromptKey;
            QuipStream = quipStream;
        }

        /// <summary>On the bonnet on the last route island, with the whole
        /// road still in front of him.</summary>
        public static LastRouteFerrymanVoice Island(int citySeed)
        {
            return new LastRouteFerrymanVoice(
                LastRouteFerrymanQuips.LineKeys,
                LastRouteFerrymanInteraction.LeaveConfirmationPromptKey,
                LastRouteFerrymanQuips.CreateState(citySeed));
        }

        /// <summary>On the same bonnet on the terrace by the mountain cafe,
        /// with the same road behind him.</summary>
        public static LastRouteFerrymanVoice Mountain(int citySeed)
        {
            return new LastRouteFerrymanVoice(
                LastRouteFerrymanQuips.MountainLineKeys,
                LastRouteFerrymanInteraction.ReturnConfirmationPromptKey,
                LastRouteFerrymanQuips.CreateMountainState(citySeed));
        }

        public string[] LineKeys { get; }
        public string ConfirmationPromptKey { get; }
        public uint QuipStream { get; }

        public bool IsPresent => LineKeys != null && LineKeys.Length > 0;
    }
}
