using System;

namespace BarPromenade
{
    public enum CityPortConversationKind { Rest, Work, Greeting, Farewell }

    /// <summary>A complete authored exchange: the reply is never shuffled separately.</summary>
    public readonly struct CityPortConversationExchange
    {
        public CityPortConversationExchange(CityPortConversationKind kind, int variant, int first, int second)
        {
            Kind = kind;
            Variant = variant;
            FirstRole = first;
            SecondRole = second;
            string group = kind == CityPortConversationKind.Rest ? "rest" :
                kind == CityPortConversationKind.Work ? "work" :
                kind == CityPortConversationKind.Greeting ? "greeting" : "farewell";
            string prefix = "city.port." + group + "." + (variant + 1).ToString("D2");
            FirstKey = prefix + ".a";
            SecondKey = prefix + ".b";
        }
        public CityPortConversationKind Kind { get; }
        public int Variant { get; }
        public int FirstRole { get; }
        public int SecondRole { get; }
        public string FirstKey { get; }
        public string SecondKey { get; }
    }

    public static class CityPortConversationCatalog
    {
        public const int DockerRole = 4;
        public const int DriverRole = 5;
        public const int RoleCount = 6;
        public const int RestCount = 36;
        public const int WorkCount = 36;
        public const int GreetingCount = 8;
        public const int FarewellCount = 8;
        public static bool IncludesDriver(in CityPortConversationExchange exchange) =>
            exchange.FirstRole == DriverRole || exchange.SecondRole == DriverRole;
        private static readonly CityPortConversationExchange[] rest =
        {
            new CityPortConversationExchange(CityPortConversationKind.Rest, 0, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 1, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 2, 4, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 3, 2, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 4, 3, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 5, 4, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 6, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 7, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 8, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 9, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 10, 4, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 11, 2, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 12, 3, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 13, 4, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 14, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 15, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 16, 4, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 17, 2, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 18, 3, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 19, 4, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 20, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 21, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 22, 4, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 23, 2, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 24, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 25, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 26, 4, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 27, 2, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 28, 3, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 29, 4, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 30, 2, 3),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 31, 3, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 32, 4, 2),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 33, 2, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 34, 3, 4),
            new CityPortConversationExchange(CityPortConversationKind.Rest, 35, 4, 3)
        };
        private static readonly CityPortConversationExchange[] work =
        {
            new CityPortConversationExchange(CityPortConversationKind.Work, 0, 2, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 1, 1, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 2, 3, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 3, 1, 3),
            new CityPortConversationExchange(CityPortConversationKind.Work, 4, 4, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 5, 1, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 6, 0, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 7, 3, 0),
            new CityPortConversationExchange(CityPortConversationKind.Work, 8, 2, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 9, 1, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 10, 3, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 11, 1, 3),
            new CityPortConversationExchange(CityPortConversationKind.Work, 12, 4, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 13, 1, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 14, 0, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 15, 3, 0),
            new CityPortConversationExchange(CityPortConversationKind.Work, 16, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 17, 4, 5),
            new CityPortConversationExchange(CityPortConversationKind.Work, 18, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 19, 4, 5),
            new CityPortConversationExchange(CityPortConversationKind.Work, 20, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 21, 4, 5),
            new CityPortConversationExchange(CityPortConversationKind.Work, 22, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 23, 4, 5),
            new CityPortConversationExchange(CityPortConversationKind.Work, 24, 0, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 25, 3, 0),
            new CityPortConversationExchange(CityPortConversationKind.Work, 26, 2, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 27, 1, 3),
            new CityPortConversationExchange(CityPortConversationKind.Work, 28, 4, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 29, 1, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 30, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Work, 31, 4, 5),
            new CityPortConversationExchange(CityPortConversationKind.Work, 32, 1, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 33, 3, 1),
            new CityPortConversationExchange(CityPortConversationKind.Work, 34, 0, 2),
            new CityPortConversationExchange(CityPortConversationKind.Work, 35, 4, 5)
        };
        private static readonly CityPortConversationExchange[] greetings =
        {
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 0, 2, 0),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 1, 2, 0),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 2, 3, 1),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 3, 3, 1),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 4, 4, 1),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 5, 4, 1),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 6, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Greeting, 7, 4, 5)
        };
        private static readonly CityPortConversationExchange[] farewells =
        {
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 0, 2, 0),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 1, 0, 2),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 2, 3, 1),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 3, 1, 3),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 4, 4, 1),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 5, 1, 4),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 6, 5, 4),
            new CityPortConversationExchange(CityPortConversationKind.Farewell, 7, 4, 5)
        };

        public static int Count(CityPortConversationKind kind) => Entries(kind).Length;

        public static CityPortConversationExchange Get(CityPortConversationKind kind, int variant)
        {
            if (variant < 0 || variant >= Count(kind)) throw new ArgumentOutOfRangeException(nameof(variant));
            return Entries(kind)[variant];
        }

        private static CityPortConversationExchange[] Entries(CityPortConversationKind kind)
        {
            switch (kind)
            {
                case CityPortConversationKind.Rest: return rest;
                case CityPortConversationKind.Work: return work;
                case CityPortConversationKind.Greeting: return greetings;
                case CityPortConversationKind.Farewell: return farewells;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static uint PairBit(int first, int second)
        {
            if (first < 0 || second < 0 || first >= RoleCount || second >= RoleCount || first == second)
                return 0u;
            return 1u << (Math.Min(first, second) * RoleCount + Math.Max(first, second));
        }
    }

    public readonly struct CityPortConversationTurn
    {
        internal CityPortConversationTurn(CityPortConversationExchange exchange, int phase, int serial)
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
        public CityPortConversationExchange Exchange { get; }
        public int SpeakerRole { get; }
        public int PartnerRole { get; }
        public string LineKey { get; }
        public int LineSerial { get; }
    }

    /// <summary>
    /// One pair owns the local speech channel. Arrival and departure each have
    /// three ship pairs and the driver's docker-only pair. Unavailable
    /// ambient conversations are discarded, never accumulated.
    /// Life time is independent of the supply cycle's paused harbour clock.
    /// </summary>
    public sealed class CityPortConversationSchedule
    {
        public const double LineSeconds = 4.3d;
        public const double FarewellLineSeconds = 3d;
        public const double DepartureWindowSeconds = 22d;
        public const double MaximumContinuousStepSeconds = 2d;
        public const double GreetingUnloadWindowSeconds = 54d;
        // The shore group now waits under the eastern canopy, beside the
        // approaching ship. Greet about three seconds earlier, within nine
        // metres of the berth, while keeping distant approaches silent.
        public const float GreetingApproachProgress = .86f;
        public const double DriverLineSeconds = 3d;
        private uint random;
        private bool initialized, active, arrivalArmed, departureArmed;
        private long cycle;
        private double previousLife, previousPort, nextAttempt, phaseEnd;
        private int phase, serial, pendingGreetings, pendingFarewells;
        private readonly bool[][] spoken = new bool[4][];
        private readonly int[] spokenCount = new int[4];
        private readonly int[] lastSpoken = { -1, -1, -1, -1 };
        private bool driverGreeting, driverFarewell, driverDockerAvailable;
        private CityPortConversationExchange exchange;

        public CityPortConversationSchedule(int seed)
        {
            random = unchecked((uint)seed) ^ 0x504F5254u;
            if (random == 0) random = 1;
            for (int kind = 0; kind < spoken.Length; kind++)
                spoken[kind] = new bool[CityPortConversationCatalog.Count((CityPortConversationKind)kind)];
        }

        public CityPortConversationTurn Current => active ? new CityPortConversationTurn(exchange, phase, serial) : default;
        public int StartedLineCount => serial;
        public int PendingGreetingCount => (pendingGreetings & 1) + ((pendingGreetings >> 1) & 1) +
            ((pendingGreetings >> 2) & 1) + ((pendingGreetings >> 3) & 1);
        public int PendingFarewellCount => (pendingFarewells & 1) + ((pendingFarewells >> 1) & 1) +
            ((pendingFarewells >> 2) & 1) + ((pendingFarewells >> 3) & 1);
        public static double LineDuration(CityPortConversationKind kind) =>
            kind == CityPortConversationKind.Farewell ? FarewellLineSeconds : LineSeconds;
        public static double LineDuration(in CityPortConversationExchange candidate) =>
            CityPortConversationCatalog.IncludesDriver(candidate) ? DriverLineSeconds : LineDuration(candidate.Kind);

        public void Reset()
        {
            initialized = active = arrivalArmed = departureArmed = false;
            driverGreeting = driverFarewell = false;
            pendingGreetings = pendingFarewells = 0;
        }

        public CityPortConversationTurn Advance(double lifeSeconds, double portSeconds,
            in CityPortCycleSnapshot snapshot, int availableRoles, int workingRoles, int restingRoles,
            uint nearbyPairs, uint closePairs, bool audible,
            bool driverGreetingWindow = false, bool driverFarewellWindow = false,
            bool dockerAvailableForDriver = false)
        {
            if (!Finite(lifeSeconds) || !Finite(portSeconds) || lifeSeconds < 0d || portSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(lifeSeconds));
            double step = lifeSeconds - previousLife;
            double portStep = portSeconds - previousPort;
            bool discontinuity = initialized && (step < 0d || step > MaximumContinuousStepSeconds ||
                portStep < 0d || portStep > step + MaximumContinuousStepSeconds);
            bool continuous = initialized && !discontinuity;
            if (!initialized || discontinuity)
            {
                initialized = true;
                active = false;
                pendingGreetings = pendingFarewells = 0;
                cycle = snapshot.CycleIndex;
                // Rebuilding at the berth must not greet a ship which arrived earlier.
                arrivalArmed = snapshot.Stage == CityPortCycleStage.Approach && snapshot.StageProgress < GreetingApproachProgress;
                // A future departure can be observed after rebuilding at the
                // berth; rebuilding once underway cannot replay its farewell.
                departureArmed = snapshot.Stage < CityPortCycleStage.Depart;
                nextAttempt = lifeSeconds + 2d + Unit() * 3d;
            }
            else if (snapshot.CycleIndex != cycle)
            {
                cycle = snapshot.CycleIndex;
                active = false;
                pendingGreetings = pendingFarewells = 0;
                arrivalArmed = snapshot.Stage == CityPortCycleStage.Approach && snapshot.StageProgress < GreetingApproachProgress;
                departureArmed = snapshot.Stage < CityPortCycleStage.Depart;
            }
            previousLife = lifeSeconds;
            previousPort = portSeconds;

            // Observe an actual edge, never replay a delivery greeting on a
            // seek, rebuild or when the listener returns after missing it.
            bool driverAudible = (availableRoles & (1 << CityPortConversationCatalog.DriverRole)) != 0 &&
                ((availableRoles & (1 << CityPortConversationCatalog.DockerRole)) != 0 || dockerAvailableForDriver);
            if (continuous && audible && driverAudible && driverGreetingWindow && !driverGreeting)
            { pendingGreetings |= 8; active = false; nextAttempt = lifeSeconds + .2d; }
            if (continuous && audible && driverAudible && driverFarewellWindow && !driverFarewell)
            { pendingFarewells |= 8; active = false; nextAttempt = lifeSeconds + .2d; }
            driverGreeting = driverGreetingWindow;
            driverFarewell = driverFarewellWindow;
            driverDockerAvailable = dockerAvailableForDriver;
            if (!driverGreeting) pendingGreetings &= 7;
            if (!driverFarewell) pendingFarewells &= 7;

            bool arrivalWindow = IsArrivalWindow(snapshot);
            if (arrivalArmed && arrivalWindow)
            {
                arrivalArmed = false;
                pendingGreetings |= audible ? 7 : 0;
                if (audible) nextAttempt = lifeSeconds + .5d + Unit() * 1.6d;
            }
            if (!arrivalWindow) pendingGreetings &= 8;
            bool departureWindow = IsDepartureWindow(snapshot);
            if (departureArmed && snapshot.Stage == CityPortCycleStage.Depart)
            {
                departureArmed = false;
                pendingFarewells |= audible && departureWindow ? 7 : 0;
                // A passing farewell takes the local channel before a new
                // break conversation; it cannot wait until the hull is gone.
                if (!active || !CityPortConversationCatalog.IncludesDriver(exchange)) active = false;
                nextAttempt = lifeSeconds + .2d + Unit() * .3d;
            }
            if (!departureWindow) pendingFarewells &= 8;
            if (!audible)
            {
                active = false;
                pendingGreetings = pendingFarewells = 0;
                nextAttempt = lifeSeconds + 3d;
                return default;
            }

            if (active)
            {
                if (!Eligible(exchange, snapshot, availableRoles, workingRoles, restingRoles, nearbyPairs, closePairs))
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
                        nextAttempt = lifeSeconds + (pendingFarewells != 0 ? .35d + Unit() * .75d :
                            pendingGreetings != 0 ? .6d + Unit() * 1.5d : 7d + Unit() * 10d);
                    }
                    else
                    {
                        phaseEnd = lifeSeconds + (phase == 2 ?
                            exchange.Kind == CityPortConversationKind.Farewell || CityPortConversationCatalog.IncludesDriver(exchange)
                                ? .35d + Unit() * .3d : .6d + Unit() * 1.1d :
                            LineDuration(exchange));
                        if (phase == 1)
                        {
                            // Anticipation can be interrupted before anyone speaks.
                            // Only an audible first line consumes the paired exchange.
                            int kind = (int)exchange.Kind;
                            spoken[kind][exchange.Variant] = true;
                            spokenCount[kind]++;
                            lastSpoken[kind] = exchange.Variant;
                        }
                        if (phase == 1 || phase == 3) serial++;
                    }
                }
                return Current;
            }
            if (lifeSeconds < nextAttempt) return default;
            nextAttempt = lifeSeconds + .8d + Unit() * .8d;
            if (pendingGreetings != 0 || pendingFarewells != 0)
            {
                bool departing = (pendingFarewells & 8) != 0 || (pendingGreetings & 8) == 0 && pendingFarewells != 0;
                int pending = departing ? pendingFarewells : pendingGreetings;
                var kind = departing ? CityPortConversationKind.Farewell : CityPortConversationKind.Greeting;
                int firstGroup = (pending & 8) != 0 ? 3 : Next(3);
                for (int offset = 0; offset < 4; offset++)
                {
                    int group = (firstGroup + offset) % 4;
                    if ((pending & (1 << group)) == 0) continue;
                    int variant = SelectUnspoken(kind, group * 2, 2, snapshot,
                        availableRoles, workingRoles, restingRoles, nearbyPairs, closePairs);
                    if (variant < 0) continue;
                    var candidate = CityPortConversationCatalog.Get(kind, variant);
                    if (departing) pendingFarewells &= ~(1 << group);
                    else pendingGreetings &= ~(1 << group);
                    Start(candidate, lifeSeconds);
                    return Current;
                }
                // Reserve the channel for the brief salutation window.
                return default;
            }
            bool preferWork = snapshot.Stage == CityPortCycleStage.Unload && Next(3) != 0;
            for (int category = 0; category < 2; category++)
            {
                CityPortConversationKind kind = (category == 0) == preferWork ?
                    CityPortConversationKind.Work : CityPortConversationKind.Rest;
                int variant = SelectUnspoken(kind, 0, CityPortConversationCatalog.Count(kind), snapshot,
                    availableRoles, workingRoles, restingRoles, nearbyPairs, closePairs);
                if (variant < 0) continue;
                Start(CityPortConversationCatalog.Get(kind, variant), lifeSeconds);
                return Current;
            }
            return default;
        }

        private void Start(CityPortConversationExchange candidate, double seconds)
        {
            exchange = candidate;
            active = true;
            phase = 0;
            phaseEnd = seconds + (CityPortConversationCatalog.IncludesDriver(candidate)
                ? .25d + Unit() * .2d : .35d + Unit() * .45d);
        }

        private int SelectUnspoken(CityPortConversationKind kind, int start, int count,
            in CityPortCycleSnapshot snapshot, int available, int working, int resting, uint nearby, uint close)
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
            for (int variant = start; variant < start + count; variant++)
            {
                if (used[variant] || variant == lastSpoken[pool]) continue;
                var candidate = CityPortConversationCatalog.Get(kind, variant);
                if (!CanStart(candidate, snapshot) || !Eligible(candidate, snapshot,
                    available, working, resting, nearby, close)) continue;
                if (Next(++eligibleCount) == 0) chosen = variant;
            }
            return chosen;
        }

        private bool Eligible(in CityPortConversationExchange candidate, in CityPortCycleSnapshot snapshot,
            int available, int working, int resting, uint nearby, uint close)
        {
            int pair = (1 << candidate.FirstRole) | (1 << candidate.SecondRole);
            if (CityPortConversationCatalog.IncludesDriver(candidate) && driverDockerAvailable)
                available |= 1 << CityPortConversationCatalog.DockerRole;
            if ((available & pair) != pair) return false;
            uint distance = CityPortConversationCatalog.PairBit(candidate.FirstRole, candidate.SecondRole);
            if (CityPortConversationCatalog.IncludesDriver(candidate))
            {
                if ((nearby & distance) == 0) return false;
                if (candidate.Kind == CityPortConversationKind.Greeting) return driverGreeting;
                if (candidate.Kind == CityPortConversationKind.Farewell) return driverFarewell;
                if (driverGreeting || driverFarewell) return false;
                if (candidate.Kind == CityPortConversationKind.Rest)
                    return (resting & pair) == pair && (close & distance) != 0;
                return (working & pair) != 0;
            }
            if (candidate.Kind == CityPortConversationKind.Rest)
                return (resting & pair) == pair && (close & distance) != 0;
            if ((nearby & distance) == 0) return false;
            if (candidate.Kind == CityPortConversationKind.Greeting) return IsArrivalWindow(snapshot);
            if (candidate.Kind == CityPortConversationKind.Farewell) return IsDepartureWindow(snapshot);
            return snapshot.Stage == CityPortCycleStage.Unload && (working & pair) != 0;
        }

        private static bool IsArrivalWindow(in CityPortCycleSnapshot snapshot) =>
            snapshot.Stage == CityPortCycleStage.Approach && snapshot.StageProgress >= GreetingApproachProgress ||
            snapshot.Stage == CityPortCycleStage.Moor || snapshot.Stage == CityPortCycleStage.Prepare ||
            snapshot.Stage == CityPortCycleStage.Unload && snapshot.SecondsInStage < GreetingUnloadWindowSeconds;

        private static bool IsDepartureWindow(in CityPortCycleSnapshot snapshot) =>
            snapshot.Stage == CityPortCycleStage.Depart && snapshot.SecondsInStage < DepartureWindowSeconds;

        private bool CanStart(in CityPortConversationExchange candidate, in CityPortCycleSnapshot snapshot)
        {
            if (CityPortConversationCatalog.IncludesDriver(candidate)) return true;
            const double completeExchange = LineSeconds * 2d + .8d + 1.7d + .4d;
            // Finish an ordinary canopy exchange before the arriving crew is
            // close enough to greet; never cut its reply off mid-conversation.
            if (candidate.Kind == CityPortConversationKind.Rest && snapshot.Stage == CityPortCycleStage.Approach)
                return snapshot.SecondsInStage + completeExchange + 1d <=
                    CityPortCycle.ApproachDurationSeconds * GreetingApproachProgress;
            if (candidate.Kind == CityPortConversationKind.Farewell)
                return IsDepartureWindow(snapshot) && snapshot.SecondsInStage +
                    FarewellLineSeconds * 2d + .8d + .65d + .6d <= DepartureWindowSeconds;
            // The deckhand walks between hatches at cargo seconds 12..24.
            // Reserve enough standing time for anticipation, both lines and
            // the longest reply gap instead of consuming a half-spoken greeting.
            if (candidate.FirstRole != 1 && candidate.SecondRole != 1 || snapshot.Stage != CityPortCycleStage.Unload)
                return true;
            double t = snapshot.SecondsInCargo;
            return t + completeExchange <= 12d || t >= 24d &&
                t + completeExchange <= CityPortCycle.CargoDurationSeconds;
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
