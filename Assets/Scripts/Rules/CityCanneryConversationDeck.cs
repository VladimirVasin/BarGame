using System;

namespace BarPromenade
{
    public enum CityCanneryConversationKind { Work, Wait, Receiving }

    /// <summary>A reply belongs to its colleague's line, never to a second shuffle.</summary>
    public readonly struct CityCanneryConversationExchange
    {
        public CityCanneryConversationExchange(CityCanneryConversationKind kind, int variant, int first, int second)
        { Kind = kind; Variant = variant; FirstRole = first; SecondRole = second; }

        public CityCanneryConversationKind Kind { get; }
        public int Variant { get; }
        public int FirstRole { get; }
        public int SecondRole { get; }
        public string Key(bool reply) => "city.cannery." + Kind.ToString().ToLowerInvariant() + "." +
            (Variant + 1).ToString("D2") + (reply ? ".b" : ".a");
    }

    /// <summary>The four factory roles, including the woman's own lines at the seamer. No driver or hero.</summary>
    public static class CityCanneryConversationCatalog
    {
        public const int RoleCount = 4;
        private static readonly int[][] pairs =
        {
            new[] { 1, 2, 2, 1, 2, 3, 3, 2, 0, 1, 1, 0, 2, 1, 3, 2 },
            new[] { 0, 1, 1, 2, 2, 3, 3, 2, 2, 1, 1, 0, 2, 0, 1, 2, 2, 3, 0, 2 },
            new[] { 0, 1, 1, 0, 1, 2, 2, 3 }
        };

        public static int Count(CityCanneryConversationKind kind) => pairs[(int)kind].Length / 2;
        public static CityCanneryConversationExchange Get(CityCanneryConversationKind kind, int variant)
        {
            if (variant < 0 || variant >= Count(kind)) throw new ArgumentOutOfRangeException(nameof(variant));
            int[] roles = pairs[(int)kind];
            return new CityCanneryConversationExchange(kind, variant, roles[variant * 2], roles[variant * 2 + 1]);
        }
    }

    /// <summary>Each context keeps its own round across absence and seeks. Admission commits it.</summary>
    public sealed class CityCanneryConversationDeck
    {
        private readonly bool[][] spoken = new bool[3][];
        private readonly int[] last = { -1, -1, -1 };
        private readonly Random random;

        public CityCanneryConversationDeck(int seed)
        {
            random = new Random(seed);
            for (int i = 0; i < spoken.Length; i++)
                spoken[i] = new bool[CityCanneryConversationCatalog.Count((CityCanneryConversationKind)i)];
        }

        public bool TryPeek(CityCanneryConversationKind kind, uint eligibleVariants,
            out CityCanneryConversationExchange exchange)
        {
            int context = (int)kind;
            bool[] used = spoken[context];
            bool exhausted = true;
            foreach (bool value in used) exhausted &= value;
            if (exhausted) Array.Clear(used, 0, used.Length);
            bool roundStart = true;
            foreach (bool value in used) roundStart &= !value;
            int selected = -1, candidates = 0;
            for (int i = 0; i < used.Length; i++)
            {
                if (used[i] || roundStart && i == last[context] ||
                    (eligibleVariants & (1u << i)) == 0) continue;
                if (random.Next(++candidates) == 0) selected = i;
            }
            exchange = selected >= 0 ? CityCanneryConversationCatalog.Get(kind, selected) : default;
            return selected >= 0;
        }

        public void Commit(in CityCanneryConversationExchange exchange)
        {
            spoken[(int)exchange.Kind][exchange.Variant] = true;
            last[(int)exchange.Kind] = exchange.Variant;
        }
    }
}
