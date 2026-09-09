namespace BarPromenade
{
    /// <summary>
    /// Everything his mother says, as a table of keys.
    ///
    /// One pool, drawn by both channels: the line she says by herself
    /// while he is in the room and the line she answers `E` with come
    /// out of the same bag, so she does not repeat herself in one
    /// channel because the other happened to be listening.
    ///
    /// Two keys stand outside the pool. The greeting fires on coming
    /// through the door and never competes with the bag; the request
    /// is said once, on the talk that raises the scarf entry.
    /// </summary>
    public static class MothersHouseMotherQuips
    {
        /// <summary>Coming through the door. She greets him as though
        /// he has just arrived, every single time, because she does not
        /// hold that he already did — story bible §13.</summary>
        public const string GreetingLineKey =
            "mothers_house.mother.greeting";

        /// <summary>The request. Said once, on the talk that opens the
        /// entry.</summary>
        public const string RequestLineKey =
            "mothers_house.mother.request";

        /// <summary>
        /// The pool. Ordinary domestic speech in her own register:
        /// food, the house, the weather, the village, and the father
        /// in the present tense, which §13 allows her by name.
        ///
        /// The LAST key is the request again, worded as if she had
        /// never made it. It is in the pool rather than beside it
        /// precisely so it can come round again, and it leaves the
        /// pool once the scarf is actually worn.
        /// </summary>
        public static readonly string[] LineKeys =
        {
            "mothers_house.mother.line.01",
            "mothers_house.mother.line.02",
            "mothers_house.mother.line.03",
            "mothers_house.mother.line.04",
            "mothers_house.mother.line.05",
            "mothers_house.mother.line.06",
            "mothers_house.mother.line.07",
            "mothers_house.mother.line.08",
            "mothers_house.mother.line.09",
            "mothers_house.mother.line.10",
            "mothers_house.mother.line.11"
        };

        /// <summary>The re-ask, and the only line that can retire.
        /// </summary>
        public static readonly int ReAskIndex = LineKeys.Length - 1;

        /// <summary>Whether a drawn line may still be said. Everything
        /// but the re-ask stays true forever.</summary>
        public static bool IsLineLive(int index, bool scarfWorn)
        {
            return index != ReAskIndex || !scarfWorn;
        }

        /// <summary>How many of the pool are in play: ten once the
        /// scarf is on him, eleven before that.</summary>
        public static int ResolvePoolSize(bool scarfWorn)
        {
            return scarfWorn ? LineKeys.Length - 1 : LineKeys.Length;
        }
    }
}
