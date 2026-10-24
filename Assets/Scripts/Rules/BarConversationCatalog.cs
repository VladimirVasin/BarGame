using System;

namespace BarPromenade
{
    public enum BarConversationKind { Counter, FrontBooth, RearBooth, HighTables }

    /// <summary>The two localized lines are one shuffle entry, including their speaker order.</summary>
    public readonly struct BarConversationExchange
    {
        internal BarConversationExchange(BarConversationKind kind, int variant, int first, int second,
            string prefix)
        {
            Kind = kind;
            Variant = variant;
            FirstRole = first;
            SecondRole = second;
            FirstKey = prefix + ".a";
            SecondKey = prefix + ".b";
        }

        public BarConversationKind Kind { get; }
        public int Variant { get; }
        public int FirstRole { get; }
        public int SecondRole { get; }
        public string FirstKey { get; }
        public string SecondKey { get; }
    }

    /// <summary>Existing visitor places; appearance and identity remain owned by the global population.</summary>
    public static class BarConversationCatalog
    {
        public const int CounterLeft = 0;
        public const int CounterRight = 1;
        public const int FrontBoothNear = 2;
        public const int FrontBoothFar = 3;
        public const int RearBoothNear = 4;
        public const int RearBoothFar = 5;
        public const int HighTableLeft = 6;
        public const int HighTableRight = 7;
        public const int RoleCount = 8;
        public const int KindCount = 4;
        public const int ActiveGroupCount = 3;
        public const int EnabledGroups = (1 << ActiveGroupCount) - 1;
        public const int CounterCount = 56;
        public const int FrontBoothCount = 32;
        public const int RearBoothCount = 32;
        public const int HighTablesCount = 24;

        private static readonly string[] roleIds =
        {
            "counter-seat-left", "counter-seat-right", "booth-1-near", "booth-1-far",
            "booth-2-near", "booth-2-far", "social-left-front", "social-right-front"
        };
        private static readonly BarConversationExchange[][] entries =
        {
            Build(BarConversationKind.Counter, "ct", CounterCount, CounterLeft, CounterRight),
            Build(BarConversationKind.FrontBooth, "bf", FrontBoothCount, FrontBoothNear, FrontBoothFar),
            Build(BarConversationKind.RearBooth, "br", RearBoothCount, RearBoothNear, RearBoothFar),
            Build(BarConversationKind.HighTables, "ht", HighTablesCount, HighTableLeft, HighTableRight)
        };

        public static int Count(BarConversationKind kind) => Entries(kind).Length;

        /// <summary>The separate high-table visitors have no conversation partner.</summary>
        public static bool IsEnabled(BarConversationKind kind)
        {
            int group = (int)kind;
            return group >= 0 && group < KindCount && (EnabledGroups & (1 << group)) != 0;
        }

        /// <summary>Guest speech permission; bartender comments have their own catalog.</summary>
        public static bool CanSpeak(int role) =>
            role >= 0 && role < RoleCount && IsEnabled((BarConversationKind)(role / 2));

        /// <summary>Raw pools remain addressable, including the dormant high-table bank.</summary>
        public static BarConversationExchange Get(BarConversationKind kind, int variant)
        {
            BarConversationExchange[] group = Entries(kind);
            if (variant < 0 || variant >= group.Length)
                throw new ArgumentOutOfRangeException(nameof(variant));
            return group[variant];
        }

        public static string RoleId(int role)
        {
            if (role < 0 || role >= roleIds.Length) throw new ArgumentOutOfRangeException(nameof(role));
            return roleIds[role];
        }

        private static BarConversationExchange[] Entries(BarConversationKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= entries.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            return entries[index];
        }

        private static BarConversationExchange[] Build(BarConversationKind kind, string id, int count,
            int first, int second)
        {
            var group = new BarConversationExchange[count];
            for (int i = 0; i < count; i++)
            {
                bool reverse = (i & 1) != 0;
                group[i] = new BarConversationExchange(kind, i, reverse ? second : first,
                    reverse ? first : second, "bar.speech." + id + (i + 1).ToString("D2"));
            }
            return group;
        }
    }

    public enum BarBartenderCommentKind { Accepted, Repeat, Beer, Wine, Distillate, Vodka }

    [Flags]
    public enum BarBartenderCommentRequirement
    {
        None = 0,
        VisibleFoam = 1,
        FoamSettled = 2,
        HandleRight = 4
    }

    /// <summary>One service line per accepted transaction; the runtime supplies the actual drink/phase.</summary>
    public static class BarBartenderCommentCatalog
    {
        public const int KindCount = 6;
        private static readonly string[][] keys =
        {
            Build("bg", 8), Build("bp", 8), Build("bb", 12),
            Build("bw", 12), Build("bc", 12), Build("bv", 12)
        };

        public static int Count(BarBartenderCommentKind kind) => Entries(kind).Length;

        public static string Key(BarBartenderCommentKind kind, int variant)
        {
            string[] group = Entries(kind);
            if (variant < 0 || variant >= group.Length)
                throw new ArgumentOutOfRangeException(nameof(variant));
            return group[variant];
        }

        public static BarBartenderCommentRequirement Requirements(BarBartenderCommentKind kind, int variant)
        {
            if (variant < 0 || variant >= Count(kind)) throw new ArgumentOutOfRangeException(nameof(variant));
            if (kind != BarBartenderCommentKind.Beer) return BarBartenderCommentRequirement.None;
            switch (variant)
            {
                case 1:
                case 2: return BarBartenderCommentRequirement.VisibleFoam;
                case 3: return BarBartenderCommentRequirement.FoamSettled;
                case 11: return BarBartenderCommentRequirement.HandleRight;
                default: return BarBartenderCommentRequirement.None;
            }
        }

        private static string[] Entries(BarBartenderCommentKind kind)
        {
            int index = (int)kind;
            if (index < 0 || index >= keys.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            return keys[index];
        }

        private static string[] Build(string id, int count)
        {
            var group = new string[count];
            for (int i = 0; i < count; i++) group[i] = "bar.speech." + id + (i + 1).ToString("D2");
            return group;
        }
    }
}
