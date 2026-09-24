using System;

namespace BarPromenade
{
    /// <summary>One key source for object thoughts and the two physical, readable notes.</summary>
    public static class AlpineVillageNarrativeContent
    {
        public const int Count = 32;
        public const int DocumentPageCount = 3;

        public static string Id(int ordinal)
        {
            if (ordinal < 1 || ordinal > Count) throw new ArgumentOutOfRangeException(nameof(ordinal));
            return "village.narrative." + ordinal.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string PromptKey(int ordinal) => Id(ordinal) + ".prompt";
        public static string TitleKey(int ordinal) => Id(ordinal) + ".title";
        public static bool IsDocument(int ordinal) => ordinal == 4 || ordinal == 16;

        public static NarrativePage[] GetPages(int ordinal)
        {
            string id = Id(ordinal);
            if (IsDocument(ordinal))
                return new[]
                {
                    new NarrativePage(id + ".document.1", true, id + ".attribution"),
                    new NarrativePage(id + ".document.2", true, id + ".attribution"),
                    new NarrativePage(id + ".document.3", true, id + ".attribution"),
                    new NarrativePage(id + ".thought.1")
                };

            int count = ordinal switch
            {
                1 or 10 or 21 or 22 or 29 => 3,
                17 or 27 or 32 => 2,
                _ => 1
            };
            var pages = new NarrativePage[count];
            for (int index = 0; index < count; index++)
                pages[index] = new NarrativePage(id + ".thought." + (index + 1));
            return pages;
        }
    }
}
