using System;

namespace BarPromenade
{
    /// <summary>A bounded bar pool. Looking at an unheard entry never spends it.</summary>
    public sealed class BarSpeechVariantDeck
    {
        private readonly int[] order;
        private readonly bool[] used;
        private uint random;
        private int last = -1;
        private int pending = -1;

        public BarSpeechVariantDeck(int count, int seed)
        {
            if (count < 1 || count > 64) throw new ArgumentOutOfRangeException(nameof(count));
            order = new int[count];
            used = new bool[count];
            random = unchecked((uint)seed) ^ 0x42535043u;
            if (random == 0u) random = 1u;
            Shuffle();
        }

        public int Count => order.Length;
        public int ConsumedCount { get; private set; }

        public int Peek()
        {
            TryPeek(order.Length == 64 ? ulong.MaxValue : (1UL << order.Length) - 1UL, out int variant);
            return variant;
        }

        /// <summary>Unavailable physical conditions never spend a line or hold up its eligible peers.</summary>
        public bool TryPeek(int eligibleVariantsBits, out int variant) =>
            TryPeek(unchecked((uint)eligibleVariantsBits), out variant);

        private bool TryPeek(ulong eligibleVariants, out int variant)
        {
            int first = FindEligible(eligibleVariants);
            if (first < 0)
            {
                bool any = false;
                for (int i = 0; i < used.Length; i++)
                {
                    if ((eligibleVariants & (1UL << i)) == 0UL) continue;
                    used[i] = false;
                    any = true;
                }
                if (any)
                {
                    Shuffle();
                    first = FindEligible(eligibleVariants);
                    if (first >= 0 && order[first] == last)
                    {
                        for (int i = first + 1; i < order.Length; i++)
                        {
                            if ((eligibleVariants & (1UL << order[i])) == 0UL) continue;
                            order[first] = order[i];
                            order[i] = last;
                            break;
                        }
                    }
                }
            }
            pending = variant = first >= 0 ? order[first] : -1;
            return first >= 0;
        }

        private int FindEligible(ulong mask)
        {
            for (int i = 0; i < order.Length; i++)
                if (!used[order[i]] && (mask & (1UL << order[i])) != 0UL) return i;
            return -1;
        }

        public void Commit(int variant)
        {
            if (variant < 0 || variant != pending)
                throw new ArgumentException("Only the pending bar variant can be committed.", nameof(variant));
            last = variant;
            used[variant] = true;
            pending = -1;
            ConsumedCount++;
        }

        private void Shuffle()
        {
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int other = Next(i + 1);
                int value = order[i];
                order[i] = order[other];
                order[other] = value;
            }
        }

        private int Next(int limit)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (int)(random % (uint)limit);
        }
    }

    public readonly struct BarConversationTurn
    {
        internal BarConversationTurn(BarConversationExchange exchange, int phase, int serial)
        {
            HasExchange = true;
            Exchange = exchange;
            NeedsLineStart = phase == 1 || phase == 4;
            IsSpeaking = phase == 2 || phase == 5;
            bool first = phase == 1 || phase == 2;
            bool reply = phase == 4 || phase == 5;
            SpeakerRole = first ? exchange.FirstRole : reply ? exchange.SecondRole : -1;
            PartnerRole = first ? exchange.SecondRole : reply ? exchange.FirstRole : -1;
            LineKey = first ? exchange.FirstKey : reply ? exchange.SecondKey : string.Empty;
            LineSerial = serial;
        }

        public bool HasExchange { get; }
        public bool NeedsLineStart { get; }
        public bool IsSpeaking { get; }
        public BarConversationExchange Exchange { get; }
        public int SpeakerRole { get; }
        public int PartnerRole { get; }
        public string LineKey { get; }
        public int LineSerial { get; }
    }

    /// <summary>
    /// One ordered conversation channel; independent instances may speak together.
    /// A begun exchange keeps its reply if a caller reserves this channel for service.
    /// The presentation owner acknowledges the first visible glyph and supplies
    /// the shared stack's remaining time.
    /// </summary>
    public sealed class BarConversationSchedule
    {
        public const int GroupCount = BarConversationCatalog.KindCount;
        public const int AllGroups = (1 << GroupCount) - 1;
        public const int ActiveGroupCount = BarConversationCatalog.ActiveGroupCount;
        public const int EnabledGroups = BarConversationCatalog.EnabledGroups;
        public const double MaximumContinuousStepSeconds = 2d;
        public const double FirstAttemptMinimumSeconds = .4d;
        public const double FirstAttemptMaximumSeconds = 2d;
        public const double ExchangeMinimumIntervalSeconds = 1.5d;
        public const double ExchangeMaximumIntervalSeconds = 4d;
        public const double ReplyMinimumGapSeconds = .3d;
        public const double ReplyMaximumGapSeconds = .75d;
        private readonly BarSpeechVariantDeck[] decks = new BarSpeechVariantDeck[GroupCount];
        private readonly int supportedGroups;
        private uint random;
        private bool initialized;
        private bool active;
        private bool committed;
        private double previousTime;
        private double nextAttempt;
        private double phaseEnd;
        // 0 anticipation, 1 ready A, 2 speaking A, 3 reply gap,
        // 4 ready B, 5 speaking B. No transition catches up missed time.
        private int phase;
        private int serial;
        private int nextGroup;
        private BarConversationExchange exchange;

        public BarConversationSchedule(int seed) : this(seed, AllGroups) { }

        /// <summary>A fixed enabled pair owns its pool; a disabled kind stays silent.</summary>
        public BarConversationSchedule(int seed, BarConversationKind kind) : this(seed, GroupMask(kind)) { }

        private BarConversationSchedule(int seed, int groupMask)
        {
            supportedGroups = groupMask & EnabledGroups;
            random = unchecked((uint)seed) ^ 0x42415253u;
            if (random == 0u) random = 1u;
            for (int group = 0; group < GroupCount; group++)
                if ((supportedGroups & (1 << group)) != 0)
                    decks[group] = new BarSpeechVariantDeck(BarConversationCatalog.Count((BarConversationKind)group),
                        unchecked(seed + group * 7919));
            nextGroup = Next(GroupCount);
        }

        public BarConversationTurn Current => active ? new BarConversationTurn(exchange, phase, serial) : default;
        public bool HasCommittedExchange => active && committed;
        public int StartedLineCount => serial;
        public double NextAttemptSeconds => nextAttempt;

        public BarConversationTurn Advance(double now, int eligibleGroups, bool audible,
            bool reserveServiceChannel = false)
        {
            ValidateTime(now);
            eligibleGroups &= supportedGroups;
            double step = now - previousTime;
            if (!initialized || step < 0d || step > MaximumContinuousStepSeconds)
            {
                Cancel(now);
                return default;
            }
            previousTime = now;
            if (!audible)
            {
                active = committed = false;
                nextAttempt = now + FirstAttemptMinimumSeconds;
                return default;
            }

            if (active)
            {
                if ((eligibleGroups & (1 << (int)exchange.Kind)) == 0 || reserveServiceChannel && !committed)
                {
                    active = committed = false;
                    nextAttempt = now + FirstAttemptMinimumSeconds;
                    return default;
                }
                if (now >= phaseEnd)
                {
                    if (phase == 0) phase = 1;
                    else if (phase == 2)
                    {
                        phase = 3;
                        phaseEnd = now + Between(ReplyMinimumGapSeconds, ReplyMaximumGapSeconds);
                    }
                    else if (phase == 3) phase = 4;
                    else if (phase == 5)
                    {
                        active = committed = false;
                        nextAttempt = now + Between(ExchangeMinimumIntervalSeconds, ExchangeMaximumIntervalSeconds);
                    }
                }
                return Current;
            }

            if (reserveServiceChannel || now < nextAttempt) return default;
            for (int offset = 0; offset < GroupCount; offset++)
            {
                int group = (nextGroup + offset) % GroupCount;
                if ((eligibleGroups & (1 << group)) == 0) continue;
                exchange = BarConversationCatalog.Get((BarConversationKind)group, decks[group].Peek());
                active = true;
                committed = false;
                phase = 0;
                phaseEnd = now + Between(.15d, .4d);
                return Current;
            }
            return default;
        }

        /// <summary>Call once the shared bubble reveals its first glyph, never merely on Show.</summary>
        public bool StartLine(double now, double remainingSeconds)
        {
            ValidateTime(now);
            if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(remainingSeconds));
            if (!active || phase != 1 && phase != 4) return false;
            if (phase == 1)
            {
                decks[(int)exchange.Kind].Commit(exchange.Variant);
                nextGroup = ((int)exchange.Kind + 1) % GroupCount;
                committed = true;
            }
            phase++;
            phaseEnd = now + remainingSeconds;
            previousTime = now;
            serial++;
            return true;
        }

        /// <summary>Cancel presentation, retaining each pool's heard progress across absence.</summary>
        public void Cancel(double now)
        {
            ValidateTime(now);
            initialized = true;
            active = committed = false;
            previousTime = now;
            nextAttempt = now + Between(FirstAttemptMinimumSeconds, FirstAttemptMaximumSeconds);
        }

        /// <summary>Service completion leaves a normal quiet pause, without breaking a begun pair.</summary>
        public void DeferAfterService(double now)
        {
            ValidateTime(now);
            if (HasCommittedExchange) return;
            initialized = true;
            active = committed = false;
            previousTime = now;
            nextAttempt = now + Between(2d, 4d);
        }

        private double Between(double minimum, double maximum) => minimum + Unit() * (maximum - minimum);
        private double Unit() => Next(16777216) / 16777216d;
        private int Next(int limit)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (int)(random % (uint)limit);
        }

        private static int GroupMask(BarConversationKind kind)
        {
            int group = (int)kind;
            if (group < 0 || group >= GroupCount) throw new ArgumentOutOfRangeException(nameof(kind));
            return 1 << group;
        }

        private static void ValidateTime(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d)
                throw new ArgumentOutOfRangeException(nameof(now));
        }
    }
}
