using System.Text;

namespace BarPromenade
{
    /// <summary>
    /// What goes on the plaque, and the one rule about it: it is short.
    ///
    /// The name and the years are not the hero's to write — nobody told
    /// him who this is, and the plaque says so. The line under them is
    /// his, and it is the only text in the game a player composes, so
    /// it is held to a word count rather than trusted. A plaque is a
    /// small board on a stone; a paragraph would not fit on it and
    /// would not be an epitaph.
    ///
    /// Pure and static: the counting and the trimming are the same code
    /// the field validates against and the plaque renders from, so what
    /// the player is allowed to type and what is kept can never differ.
    /// </summary>
    public static class CemeteryEpitaph
    {
        /// <summary>
        /// Words the hero can find for a stranger. Enough for a line
        /// somebody would actually cut into stone and no more.
        /// </summary>
        public const int MaximumWords = 8;

        /// <summary>
        /// A backstop under the word count, because one "word" can be
        /// arbitrarily long and the board is a fixed width.
        /// </summary>
        public const int MaximumCharacters = 64;

        /// <summary>Localized stand-ins for what nobody knows.
        /// </summary>
        public const string UnknownNameKey = "cemetery.plaque.name";
        public const string UnknownYearsKey = "cemetery.plaque.years";

        /// <summary>Words in a line, by any reasonable reading of the
        /// word "word".</summary>
        public static int CountWords(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int words = 0;
            bool inside = false;
            for (int index = 0; index < text.Length; index++)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    inside = false;
                    continue;
                }

                if (!inside)
                {
                    inside = true;
                    words++;
                }
            }

            return words;
        }

        /// <summary>
        /// True while a line is still something the hero could cut into
        /// a board. An empty one is allowed: a man may have nothing to
        /// say over a stranger, and that is its own epitaph.
        /// </summary>
        public static bool IsWithinLimits(string text)
        {
            return CountWords(text) <= MaximumWords &&
                   (text == null ||
                    text.Length <= MaximumCharacters);
        }

        /// <summary>
        /// Whatever the player typed, cut down to what fits: collapsed
        /// whitespace, at most <see cref="MaximumWords"/> words and at
        /// most <see cref="MaximumCharacters"/> characters. Runs on the
        /// way in as well as on the way out, so a pasted paragraph is
        /// trimmed rather than refused.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var built = new StringBuilder(text.Length);
            int words = 0;
            int index = 0;
            while (index < text.Length && words < MaximumWords)
            {
                while (index < text.Length &&
                       char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index >= text.Length)
                {
                    break;
                }

                if (words > 0)
                {
                    built.Append(' ');
                }

                words++;
                while (index < text.Length &&
                       !char.IsWhiteSpace(text[index]))
                {
                    built.Append(text[index]);
                    index++;
                }
            }

            string trimmed = built.ToString();
            return trimmed.Length > MaximumCharacters
                ? trimmed.Substring(0, MaximumCharacters).TrimEnd()
                : trimmed;
        }
    }
}
