using System.Text;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What drunkenness does to a line on its way out of his mouth: a stuck
    /// syllable, stretched vowels, a doubled consonant, an eaten space, a lost
    /// letter. The text stays HIS text — this is the same sentence coming
    /// apart, not a different sentence — which is why the incoherence at the
    /// top of the scale needs no incoherent writing.
    ///
    /// APPLIED ONCE PER LINE, BEFORE DELIVERY, never per frame. Three things
    /// break otherwise: the typewriter reveals a prefix of the string, so a
    /// string that changed under it would rewrite letters already on screen;
    /// the bubble measures its panel from the whole line and must not be
    /// handed a different length afterwards; and a per-frame draw would not be
    /// reproducible from a seed.
    ///
    /// Pure and deterministic: the same text, amount and seed give the same
    /// output, and at zero amount it gives back the input STRING ITSELF, so a
    /// sober line is untouched by identity and not merely by equality.
    /// </summary>
    public static class HeroMutterSlur
    {
        /// <summary>Ceiling for a line that will be scattered glyph by glyph:
        /// it has to stay on one row of the panel.</summary>
        public const int MaximumSlurredLength = 32;

        /// <summary>Ceiling for a line the ordinary wrapped label draws.
        /// </summary>
        public const int MaximumWrappedSlurredLength = 64;

        public const float MaximumGrowthFactor = 2f;
        public const int MaximumGrowthCharacters = 4;

        /// <summary>Below this he still gets his syllables out whole.</summary>
        public const float StuckSyllableThreshold = 0.30f;

        public const int StuckSyllableLength = 2;
        public const float StuckSyllableSecondRepeatChance = 0.45f;
        public const float VowelStretchBaseChance = 0.10f;
        public const float VowelStretchAmountChance = 0.35f;
        public const float VowelSecondRepeatChance = 0.40f;
        public const float ConsonantDoubleBaseChance = 0.06f;
        public const float ConsonantDoubleAmountChance = 0.18f;
        public const float SpaceEatenChance = 0.25f;
        public const float CharacterDroppedChance = 0.20f;

        private const string Vowels = "аеёиоуыэюяaeiouy";

        /// <summary>
        /// The longest a line of this length may come out. Growth is checked
        /// against it before every insertion, so the bound holds by
        /// construction rather than by a trim at the end — a trim would cut a
        /// word in half and could leave a bare full stop.
        /// </summary>
        public static int ResolveBudget(int length, int maximumLength)
        {
            int ceiling = Mathf.Max(length, Mathf.Max(1, maximumLength));
            return Mathf.Min(
                Mathf.FloorToInt(length * MaximumGrowthFactor) +
                MaximumGrowthCharacters,
                ceiling);
        }

        public static string Apply(string text, float amount, int seed)
        {
            return Apply(text, amount, seed, MaximumSlurredLength);
        }

        public static string Apply(
            string text,
            float amount,
            int seed,
            int maximumLength)
        {
            if (string.IsNullOrEmpty(text) ||
                float.IsNaN(amount) ||
                amount <= 0f)
            {
                return text;
            }

            float clamped = Mathf.Clamp01(amount);
            var random = new System.Random(seed);
            int budget = ResolveBudget(text.Length, maximumLength);

            string slurred = StickSyllable(text, clamped, random, budget);
            slurred = StretchVowels(slurred, clamped, random, budget);
            slurred = DoubleConsonants(slurred, clamped, random, budget);
            slurred = EatSpaces(slurred, clamped, random);
            slurred = DropCharacters(slurred, clamped, random);
            return string.IsNullOrWhiteSpace(slurred) ? text : slurred;
        }

        public static bool IsVowel(char value)
        {
            return Vowels.IndexOf(char.ToLowerInvariant(value)) >= 0;
        }

        /// <summary>
        /// «поворот» → «по-поворот». At most one per line, and the repeat
        /// carries the word's own case while the word itself drops to lower —
        /// «Но-ноги», never «Но-Ноги».
        /// </summary>
        private static string StickSyllable(
            string text,
            float amount,
            System.Random random,
            int budget)
        {
            if (amount < StuckSyllableThreshold)
            {
                return text;
            }

            int start = PickWordStart(text, random);
            if (start < 0)
            {
                return text;
            }

            string syllable = text.Substring(start, StuckSyllableLength);
            int repeats =
                NextUnit(random) < StuckSyllableSecondRepeatChance ? 2 : 1;
            int growth = repeats * (StuckSyllableLength + 1);
            while (repeats > 0 && text.Length + growth > budget)
            {
                repeats--;
                growth = repeats * (StuckSyllableLength + 1);
            }

            if (repeats <= 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length + growth);
            builder.Append(text, 0, start);
            builder.Append(syllable).Append('-');
            for (int index = 1; index < repeats; index++)
            {
                builder
                    .Append(char.ToLowerInvariant(syllable[0]))
                    .Append(syllable, 1, syllable.Length - 1)
                    .Append('-');
            }

            builder.Append(char.ToLowerInvariant(text[start]));
            builder.Append(text, start + 1, text.Length - start - 1);
            return builder.ToString();
        }

        /// <summary>
        /// A word of at least three letters, so the two-letter syllable leaves
        /// something behind it. Returns `-1` when the line has none.
        /// </summary>
        private static int PickWordStart(string text, System.Random random)
        {
            int candidates = 0;
            int chosen = -1;
            for (int index = 0; index < text.Length; index++)
            {
                if (!IsWordStart(text, index) ||
                    WordLetterCount(text, index) < StuckSyllableLength + 1)
                {
                    continue;
                }

                candidates++;
                // Reservoir of one: every candidate is equally likely and the
                // line is walked once.
                if (NextUnit(random) < 1f / candidates)
                {
                    chosen = index;
                }
            }

            return chosen;
        }

        private static bool IsWordStart(string text, int index)
        {
            return char.IsLetter(text[index]) &&
                   (index == 0 || !char.IsLetter(text[index - 1]));
        }

        private static int WordLetterCount(string text, int start)
        {
            int count = 0;
            for (int index = start;
                 index < text.Length && char.IsLetter(text[index]);
                 index++)
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// «дойду» → «дооойду». Repeats are always lower case: «Ооо» is a man
        /// slurring, «ООО» is a man shouting, and nobody in this game shouts.
        /// </summary>
        private static string StretchVowels(
            string text,
            float amount,
            System.Random random,
            int budget)
        {
            float chance =
                VowelStretchBaseChance + VowelStretchAmountChance * amount;
            var builder = new StringBuilder(text.Length + 4);
            int length = text.Length;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                builder.Append(value);
                if (!IsVowel(value) ||
                    length >= budget ||
                    NextUnit(random) >= chance)
                {
                    continue;
                }

                char repeat = char.ToLowerInvariant(value);
                builder.Append(repeat);
                length++;
                if (length < budget &&
                    NextUnit(random) < VowelSecondRepeatChance)
                {
                    builder.Append(repeat);
                    length++;
                }
            }

            return builder.ToString();
        }

        private static string DoubleConsonants(
            string text,
            float amount,
            System.Random random,
            int budget)
        {
            float chance =
                ConsonantDoubleBaseChance +
                ConsonantDoubleAmountChance * amount;
            var builder = new StringBuilder(text.Length + 4);
            int length = text.Length;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                builder.Append(value);
                if (!char.IsLetter(value) ||
                    IsVowel(value) ||
                    length >= budget ||
                    NextUnit(random) >= chance)
                {
                    continue;
                }

                builder.Append(char.ToLowerInvariant(value));
                length++;
            }

            return builder.ToString();
        }

        /// <summary>
        /// Words run together. Never the space after a full stop: the two
        /// sentences §21 allows him have to stay two.
        /// </summary>
        private static string EatSpaces(
            string text,
            float amount,
            System.Random random)
        {
            float chance = SpaceEatenChance * amount;
            var builder = new StringBuilder(text.Length);
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                if (value == ' ' &&
                    index > 0 &&
                    text[index - 1] != '.' &&
                    NextUnit(random) < chance)
                {
                    continue;
                }

                builder.Append(value);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Letters fall out of the middle of words. Quadratic in the amount,
        /// so this only really begins at the top of the scale — and never the
        /// first character, never two in a row, and never the last letter a
        /// word has.
        /// </summary>
        private static string DropCharacters(
            string text,
            float amount,
            System.Random random)
        {
            float chance = CharacterDroppedChance * amount * amount;
            var builder = new StringBuilder(text.Length);
            bool droppedPrevious = false;
            for (int index = 0; index < text.Length; index++)
            {
                char value = text[index];
                bool droppable =
                    index > 0 &&
                    char.IsLetter(value) &&
                    !droppedPrevious &&
                    !IsOnlyLetterOfWord(text, index);
                if (droppable && NextUnit(random) < chance)
                {
                    droppedPrevious = true;
                    continue;
                }

                droppedPrevious = false;
                builder.Append(value);
            }

            return builder.ToString();
        }

        private static bool IsOnlyLetterOfWord(string text, int index)
        {
            bool letterBefore =
                index > 0 && char.IsLetter(text[index - 1]);
            bool letterAfter =
                index + 1 < text.Length && char.IsLetter(text[index + 1]);
            return !letterBefore && !letterAfter;
        }

        private static float NextUnit(System.Random random)
        {
            return (float)random.NextDouble();
        }
    }
}
