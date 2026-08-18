using System;

namespace BarPromenade
{
    /// <summary>
    /// What the two old men shout at each other. Two repertoires, two
    /// seeded streams, the fisherman's deterministic walk over each so
    /// neither man repeats himself back to back. Pure — the controller
    /// owns the state and the tests own the distribution.
    ///
    /// The register is a rule rather than a mood, and it is the same
    /// rule twice with the subject swapped:
    ///
    /// * the line is aimed at the neighbour, never at the hero. Nobody
    ///   here has noticed that anyone walked up;
    /// * the other man's game is never conceded to be a game. It is a
    ///   chore, a habit, a piece of furniture — anything but a game;
    /// * one or two short clauses, at most 48 characters, because the
    ///   bubble is 180 logical pixels wide and a third line pushes the
    ///   panel over his own head;
    /// * the insult is about the game and lands on the man. Neither of
    ///   them has ever said anything about the other's person that was
    ///   not really about a piece.
    ///
    /// The joke is that they have been having this exact argument long
    /// enough that both boards have gone empty and neither has noticed.
    /// </summary>
    public static class ParkQuarrelTaunts
    {
        /// <summary>The chess player, on draughts.</summary>
        public static readonly string[] ChessLineKeys =
        {
            "park.quarrel.chess.line.01",
            "park.quarrel.chess.line.02",
            "park.quarrel.chess.line.03",
            "park.quarrel.chess.line.04",
            "park.quarrel.chess.line.05",
            "park.quarrel.chess.line.06",
            "park.quarrel.chess.line.07",
            "park.quarrel.chess.line.08",
            "park.quarrel.chess.line.09",
            "park.quarrel.chess.line.10",
            "park.quarrel.chess.line.11",
            "park.quarrel.chess.line.12"
        };

        /// <summary>The draughts player, on chess.</summary>
        public static readonly string[] CheckersLineKeys =
        {
            "park.quarrel.checkers.line.01",
            "park.quarrel.checkers.line.02",
            "park.quarrel.checkers.line.03",
            "park.quarrel.checkers.line.04",
            "park.quarrel.checkers.line.05",
            "park.quarrel.checkers.line.06",
            "park.quarrel.checkers.line.07",
            "park.quarrel.checkers.line.08",
            "park.quarrel.checkers.line.09",
            "park.quarrel.checkers.line.10",
            "park.quarrel.checkers.line.11",
            "park.quarrel.checkers.line.12"
        };

        /// <summary>The longest a line may render before the bubble
        /// needs a third row. Asserted by the tests against both
        /// catalogs, not merely intended.</summary>
        public const int MaximumLineLength = 48;

        public static string[] LineKeysFor(ParkQuarrelSpeaker speaker)
        {
            return speaker == ParkQuarrelSpeaker.Chess
                ? ChessLineKeys
                : CheckersLineKeys;
        }

        /// <summary>
        /// Seed stream from the city seed — the watchman's hash idiom,
        /// never zero so xorshift never sticks. Each man carries his own
        /// salt: one seed must not hand both of them the same walk.
        /// </summary>
        public static uint CreateState(
            int citySeed,
            ParkQuarrelSpeaker speaker)
        {
            unchecked
            {
                uint salt = speaker == ParkQuarrelSpeaker.Chess
                    ? 0x43485353u  // "CHSS"
                    : 0x44524754u; // "DRGT"
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

        /// <summary>The next line: uniform over that man's pool, but a
        /// draw landing on the one he just used slides to its
        /// neighbour.</summary>
        public static int NextIndex(
            ParkQuarrelSpeaker speaker,
            ref uint state,
            int previousIndex)
        {
            string[] keys = LineKeysFor(speaker);
            if (keys.Length == 0)
            {
                throw new InvalidOperationException(
                    "The park quarrel has no lines to shout.");
            }

            int index = (int)(NextRandomState(ref state) %
                              (uint)keys.Length);
            if (index == previousIndex && keys.Length > 1)
            {
                index = (index + 1) % keys.Length;
            }

            return index;
        }
    }
}
