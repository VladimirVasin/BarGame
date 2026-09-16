using System;

namespace BarPromenade
{
    public enum CityArchShelterConversationKind { Warmers, Sleeper }

    /// <summary>A complete authored exchange between two shelter residents: the reply is never shuffled separately.</summary>
    public readonly struct CityArchShelterConversationExchange
    {
        public CityArchShelterConversationExchange(CityArchShelterConversationKind kind, int variant, int first, int second)
        {
            Kind = kind;
            Variant = variant;
            FirstRole = first;
            SecondRole = second;
            string group = kind == CityArchShelterConversationKind.Warmers ? "warmers" : "sleeper";
            string prefix = "city.shelter." + group + "." + (variant + 1).ToString("D2");
            FirstKey = prefix + ".a";
            SecondKey = prefix + ".b";
        }
        public CityArchShelterConversationKind Kind { get; }
        public int Variant { get; }
        public int FirstRole { get; }
        public int SecondRole { get; }
        public string FirstKey { get; }
        public string SecondKey { get; }
    }

    /// <summary>
    /// The three residents of the Nightlife arch: the two men at the barrel
    /// talk to each other, and the sleeper answers from under the blanket a
    /// few times. Nobody addresses the hero; the pools are everyday things.
    /// </summary>
    public static class CityArchShelterConversationCatalog
    {
        public const int StandingRole = 0;
        public const int SeatedRole = 1;
        public const int SleeperRole = 2;
        public const int RoleCount = 3;
        public const int WarmerCount = 20;
        public const int SleeperCount = 4;

        private static readonly CityArchShelterConversationExchange[] warmers =
        {
            W(0, StandingRole, SeatedRole), W(1, SeatedRole, StandingRole), W(2, StandingRole, SeatedRole),
            W(3, SeatedRole, StandingRole), W(4, StandingRole, SeatedRole), W(5, SeatedRole, StandingRole),
            W(6, StandingRole, SeatedRole), W(7, SeatedRole, StandingRole), W(8, SeatedRole, StandingRole),
            W(9, StandingRole, SeatedRole), W(10, StandingRole, SeatedRole), W(11, SeatedRole, StandingRole),
            W(12, StandingRole, SeatedRole), W(13, SeatedRole, StandingRole), W(14, StandingRole, SeatedRole),
            W(15, SeatedRole, StandingRole), W(16, SeatedRole, StandingRole), W(17, StandingRole, SeatedRole),
            W(18, StandingRole, SeatedRole), W(19, SeatedRole, StandingRole)
        };

        private static readonly CityArchShelterConversationExchange[] sleeper =
        {
            S(0, StandingRole), S(1, SeatedRole), S(2, StandingRole), S(3, SeatedRole)
        };

        private static CityArchShelterConversationExchange W(int variant, int first, int second) =>
            new CityArchShelterConversationExchange(CityArchShelterConversationKind.Warmers, variant, first, second);

        private static CityArchShelterConversationExchange S(int variant, int first) =>
            new CityArchShelterConversationExchange(CityArchShelterConversationKind.Sleeper, variant, first, SleeperRole);

        public static int Count(CityArchShelterConversationKind kind) => Entries(kind).Length;

        public static CityArchShelterConversationExchange Get(CityArchShelterConversationKind kind, int variant)
        {
            if (variant < 0 || variant >= Count(kind)) throw new ArgumentOutOfRangeException(nameof(variant));
            return Entries(kind)[variant];
        }

        private static CityArchShelterConversationExchange[] Entries(CityArchShelterConversationKind kind)
        {
            switch (kind)
            {
                case CityArchShelterConversationKind.Warmers: return warmers;
                case CityArchShelterConversationKind.Sleeper: return sleeper;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public readonly struct CityArchShelterConversationTurn
    {
        internal CityArchShelterConversationTurn(CityArchShelterConversationExchange exchange, int phase, int serial)
        {
            HasExchange = true;
            Exchange = exchange;
            IsSpeaking = phase == 1 || phase == 3;
            SpeakerRole = phase == 1 ? exchange.FirstRole : phase == 3 ? exchange.SecondRole : -1;
            PartnerRole = phase == 1 ? exchange.SecondRole : phase == 3 ? exchange.FirstRole : -1;
            LineKey = phase == 1 ? exchange.FirstKey : phase == 3 ? exchange.SecondKey : string.Empty;
            LineSerial = serial;
        }
        public bool HasExchange { get; }
        public bool IsSpeaking { get; }
        public CityArchShelterConversationExchange Exchange { get; }
        public int SpeakerRole { get; }
        public int PartnerRole { get; }
        public string LineKey { get; }
        public int LineSerial { get; }
    }

    /// <summary>
    /// One exchange at a time owns the shelter's speech channel, the same way
    /// the port crew shares theirs: authored pairs, no repeat until a pool is
    /// exhausted, absent or out-of-earshot partners defer their entries, and
    /// a missed exchange is discarded rather than replayed. Line lengths come
    /// from the shared delivery, supplied by the caller per line.
    /// </summary>
    public sealed class CityArchShelterConversationSchedule
    {
        public const double MaximumContinuousStepSeconds = 2d;
        public const double FirstAttemptMinimumSeconds = 5d;
        public const double FirstAttemptMaximumSeconds = 9d;
        public const double PauseMinimumSeconds = 16d;
        public const double PauseMaximumSeconds = 30d;
        public const double AnticipationMinimumSeconds = .35d;
        public const double AnticipationMaximumSeconds = .8d;
        public const double ReplyGapMinimumSeconds = .6d;
        public const double ReplyGapMaximumSeconds = 1.3d;
        public const double DefaultLineSeconds = 4.4d;
        /// <summary>One sleeper exchange for roughly this many warmer exchanges.</summary>
        public const int SleeperEvery = 4;

        private static readonly int kindCount = Enum.GetValues(typeof(CityArchShelterConversationKind)).Length;
        private uint random;
        private bool initialized, active;
        private double previousLife, nextAttempt, phaseEnd;
        private int phase, serial, sinceSleeper;
        private readonly bool[][] spoken = new bool[kindCount][];
        private readonly int[] spokenCount = new int[kindCount];
        private readonly int[] lastSpoken = new int[kindCount];
        private CityArchShelterConversationExchange exchange;

        public CityArchShelterConversationSchedule(int seed)
        {
            random = unchecked((uint)seed) ^ 0x41524348u;
            if (random == 0) random = 1;
            for (int kind = 0; kind < spoken.Length; kind++)
            {
                spoken[kind] = new bool[CityArchShelterConversationCatalog.Count((CityArchShelterConversationKind)kind)];
                lastSpoken[kind] = -1;
            }
        }

        public CityArchShelterConversationTurn Current => active ? new CityArchShelterConversationTurn(exchange, phase, serial) : default;
        public int StartedLineCount => serial;
        public double NextAttemptSeconds => nextAttempt;

        public void Reset()
        {
            initialized = active = false;
        }

        /// <summary>
        /// Advances the channel. <paramref name="availableRoles"/> is a bit per
        /// role that is present, in earshot and free to speak; the two line
        /// durations are the shared delivery's resolved lengths for the
        /// current exchange's lines (any non-positive value falls back to the
        /// ambient default).
        /// </summary>
        public CityArchShelterConversationTurn Advance(double lifeSeconds, int availableRoles, bool audible,
            double firstLineSeconds = 0d, double secondLineSeconds = 0d)
        {
            if (!Finite(lifeSeconds) || lifeSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(lifeSeconds));
            double step = lifeSeconds - previousLife;
            bool discontinuity = initialized && (step < 0d || step > MaximumContinuousStepSeconds);
            if (!initialized || discontinuity)
            {
                initialized = true;
                active = false;
                nextAttempt = lifeSeconds + FirstAttemptMinimumSeconds +
                    Unit() * (FirstAttemptMaximumSeconds - FirstAttemptMinimumSeconds);
            }
            previousLife = lifeSeconds;

            if (!audible)
            {
                // Out of earshot nothing is said; the channel neither queues
                // nor replays what the listener walked away from.
                active = false;
                nextAttempt = lifeSeconds + 3d;
                return default;
            }

            if (active)
            {
                int pair = (1 << exchange.FirstRole) | (1 << exchange.SecondRole);
                if ((availableRoles & pair) != pair)
                {
                    active = false;
                    nextAttempt = lifeSeconds + .8d + Unit();
                }
                else if (lifeSeconds >= phaseEnd)
                {
                    // At most one transition per observed frame, never a catch-up burst.
                    phase++;
                    if (phase == 4)
                    {
                        active = false;
                        nextAttempt = lifeSeconds + PauseMinimumSeconds + Unit() * (PauseMaximumSeconds - PauseMinimumSeconds);
                    }
                    else
                    {
                        phaseEnd = lifeSeconds + (phase == 2
                            ? ReplyGapMinimumSeconds + Unit() * (ReplyGapMaximumSeconds - ReplyGapMinimumSeconds)
                            : LineDuration(phase == 1 ? firstLineSeconds : secondLineSeconds));
                        if (phase == 1)
                        {
                            // Only an audible first line consumes the paired exchange.
                            int kind = (int)exchange.Kind;
                            spoken[kind][exchange.Variant] = true;
                            spokenCount[kind]++;
                            lastSpoken[kind] = exchange.Variant;
                            if (exchange.Kind == CityArchShelterConversationKind.Sleeper) sinceSleeper = 0;
                            else sinceSleeper++;
                        }
                        if (phase == 1 || phase == 3) serial++;
                    }
                }
                return Current;
            }

            if (lifeSeconds < nextAttempt) return default;
            nextAttempt = lifeSeconds + .8d + Unit() * .8d;
            bool preferSleeper = sinceSleeper >= SleeperEvery - 1 && Next(2) == 0;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var kind = (attempt == 0) == preferSleeper
                    ? CityArchShelterConversationKind.Sleeper
                    : CityArchShelterConversationKind.Warmers;
                int variant = SelectUnspoken(kind, availableRoles);
                if (variant < 0) continue;
                exchange = CityArchShelterConversationCatalog.Get(kind, variant);
                active = true;
                phase = 0;
                phaseEnd = lifeSeconds + AnticipationMinimumSeconds +
                    Unit() * (AnticipationMaximumSeconds - AnticipationMinimumSeconds);
                return Current;
            }
            return default;
        }

        public static double LineDuration(double resolvedSeconds) =>
            Finite(resolvedSeconds) && resolvedSeconds > 0d ? resolvedSeconds : DefaultLineSeconds;

        private int SelectUnspoken(CityArchShelterConversationKind kind, int available)
        {
            int pool = (int)kind;
            bool[] used = spoken[pool];
            if (spokenCount[pool] == used.Length)
            {
                Array.Clear(used, 0, used.Length);
                spokenCount[pool] = 0;
            }
            // Draw uniformly from the currently possible, unheard exchanges.
            // Absent partners defer their entries; they never refill the pool.
            int chosen = -1, eligibleCount = 0;
            for (int variant = 0; variant < used.Length; variant++)
            {
                if (used[variant] || variant == lastSpoken[pool]) continue;
                var candidate = CityArchShelterConversationCatalog.Get(kind, variant);
                int pair = (1 << candidate.FirstRole) | (1 << candidate.SecondRole);
                if ((available & pair) != pair) continue;
                if (Next(++eligibleCount) == 0) chosen = variant;
            }
            return chosen;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private int Next(int count) => (int)(NextRandom() % (uint)count);
        private double Unit() => (NextRandom() & 0xFFFFFFu) / 16777216d;
        private uint NextRandom()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }
    }
}
