using System;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeServicePhase
    {
        Wiping = 0,
        CoupleDrink = 2,
        Notice = 3,
        WalkToCup = 4,
        Pour = 5,
        WalkBack = 6,
        MenuNotice = 7,
        WalkToHero = 8,
        PlaceMenu = 9,
        MenuWalkBack = 10,
        WalkToMenu = 11,
        TakeMenu = 12,
        CarryMenuBack = 13
    }

    /// <summary>
    /// Immutable view of the cafe's two-cup service state and the two halves
    /// of one menu handoff.
    /// The sleeping lone patron and hero remain absent from the cup arrays:
    /// the hero's booleans describe only the physical booklet delivery.
    /// </summary>
    public readonly struct MountainRoadCafeServiceFrame
    {
        internal MountainRoadCafeServiceFrame(
            MountainRoadCafeServicePhase phase,
            float phaseElapsedSeconds,
            float phaseDurationSeconds,
            int sequence,
            MountainRoadCafeCastRole serviceTarget,
            bool hasServiceTarget,
            MountainRoadCafeCastRole walkOrigin,
            bool hasWalkOrigin,
            float pairManFill,
            float pairWomanFill,
            bool heroMenuRequested,
            bool heroMenuPlaced,
            bool heroMenuRetrievalRequested,
            bool heroMenuRetrieved)
        {
            Phase = phase;
            PhaseElapsedSeconds = phaseElapsedSeconds;
            PhaseDurationSeconds = phaseDurationSeconds;
            Sequence = sequence;
            ServiceTarget = serviceTarget;
            HasServiceTarget = hasServiceTarget;
            WalkOrigin = walkOrigin;
            HasWalkOrigin = hasWalkOrigin;
            PairManFill = pairManFill;
            PairWomanFill = pairWomanFill;
            HeroMenuRequested = heroMenuRequested;
            HeroMenuPlaced = heroMenuPlaced;
            HeroMenuRetrievalRequested = heroMenuRetrievalRequested;
            HeroMenuRetrieved = heroMenuRetrieved;
        }

        public MountainRoadCafeServicePhase Phase { get; }
        public float PhaseElapsedSeconds { get; }
        public float PhaseDurationSeconds { get; }
        public int Sequence { get; }
        public MountainRoadCafeCastRole ServiceTarget { get; }
        public bool HasServiceTarget { get; }
        public MountainRoadCafeCastRole WalkOrigin { get; }
        public bool HasWalkOrigin { get; }
        public float PairManFill { get; }
        public float PairWomanFill { get; }
        public bool HeroMenuRequested { get; }
        public bool HeroMenuPlaced { get; }
        public bool HeroMenuRetrievalRequested { get; }
        public bool HeroMenuRetrieved { get; }
        public float PhaseNormalized => PhaseDurationSeconds > 0f
            ? Mathf.Clamp01(PhaseElapsedSeconds / PhaseDurationSeconds)
            : 1f;

        public float GetFill(MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.PairMan:
                    return PairManFill;
                case MountainRoadCafeCastRole.PairWoman:
                    return PairWomanFill;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role),
                        role,
                        "Only the authored pair own cafe cups.");
            }
        }

        public bool IsDrinking(MountainRoadCafeCastRole role)
        {
            if (Phase != MountainRoadCafeServicePhase.CoupleDrink)
            {
                return false;
            }

            float localElapsed = PhaseElapsedSeconds -
                MountainRoadCafeServiceTimeline.GetPairDrinkStartSeconds(
                    role);
            return localElapsed >= 0f &&
                   localElapsed <
                   MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds;
        }

        public float GetDrinkElapsedSeconds(
            MountainRoadCafeCastRole role)
        {
            if (!IsDrinking(role))
            {
                return 0f;
            }

            return Mathf.Clamp(
                PhaseElapsedSeconds -
                MountainRoadCafeServiceTimeline
                    .GetPairDrinkStartSeconds(role),
                0f,
                MountainRoadCafeServiceTimeline
                    .PairPatronDrinkSeconds);
        }

        public float GetDrinkNormalized(MountainRoadCafeCastRole role)
        {
            float duration =
                MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds;
            return duration > 0f
                ? Mathf.Clamp01(GetDrinkElapsedSeconds(role) / duration)
                : 0f;
        }
    }

    /// <summary>
    /// Pure, deterministic and hitch-safe timeline for the silent cafe loop.
    /// Advance consumes the complete supplied delta, carrying remainder over
    /// every phase boundary instead of losing time to a per-frame clamp.
    /// </summary>
    public sealed class MountainRoadCafeServiceTimeline
    {
        public const float PairPatronDrinkSeconds = 4.75f;
        public const float PairDrinkGapSeconds = 0.50f;
        public const float PairWomanDrinkStartSeconds =
            PairPatronDrinkSeconds + PairDrinkGapSeconds;
        public const float CoupleDrinkSeconds =
            PairWomanDrinkStartSeconds + PairPatronDrinkSeconds;
        public const float DrinkSipStartNormalized = 0.48f;
        public const float DrinkSipEndNormalized = 0.62f;
        public const float NoticeSeconds = 2.5f;
        public const float WalkSeconds = 1.25f;
        public const float PourSeconds = 3.5f;
        public const float PourFlowStartNormalized = 0.38f;
        public const float PourFlowEndNormalized = 0.72f;
        public const float MenuPlaceStartNormalized = 0.30f;
        public const float MenuPlaceEndNormalized = 0.82f;
        public const float MinimumWipeSeconds = 18f;
        public const float MaximumWipeSeconds = 32f;
        public const float RefilledLevel = 0.90f;
        public const float RefillRequestLevel = 0.30f;
        public const float InitialPairManFill = 0.44f;
        public const float InitialPairWomanFill = 0.56f;
        public const bool ServesHero = false;
        public const bool OffersHeroMenu = true;

        private const float PairManSipConsumedAmount = 0.16f;
        private const float PairWomanSipConsumedAmount = 0.18f;
        private const int MaximumTransitionsPerAdvance = 4096;

        private readonly float[] fills = new float[2];
        private readonly float[] phaseStartFills = new float[2];
        private readonly float[] phaseTargetFills = new float[2];
        private uint randomState;
        private MountainRoadCafeServicePhase phase;
        private float phaseElapsedSeconds;
        private float phaseDurationSeconds;
        private int sequence;
        private int serviceTargetIndex;
        private int queuedServiceTargetIndex;
        private int walkOriginIndex;
        private bool heroMenuRequested;
        private bool heroMenuPlaced;
        private bool heroMenuRetrievalRequested;
        private bool heroMenuRetrieved;

        public MountainRoadCafeServiceTimeline(int seed)
        {
            Reset(seed);
        }

        public MountainRoadCafeServiceFrame Frame =>
            new MountainRoadCafeServiceFrame(
                phase,
                phaseElapsedSeconds,
                phaseDurationSeconds,
                sequence,
                RoleFromCupIndex(serviceTargetIndex),
                serviceTargetIndex >= 0,
                RoleFromCupIndex(walkOriginIndex),
                walkOriginIndex >= 0,
                fills[0],
                fills[1],
                heroMenuRequested,
                heroMenuPlaced,
                heroMenuRetrievalRequested,
                heroMenuRetrieved);

        public float RemainingPhaseSeconds => Mathf.Max(
            0f,
            phaseDurationSeconds - phaseElapsedSeconds);

        public static bool IsPatronWithCup(
            MountainRoadCafeCastRole role)
        {
            return role == MountainRoadCafeCastRole.PairMan ||
                   role == MountainRoadCafeCastRole.PairWoman;
        }

        public static bool IsPourFlowActive(float phaseNormalized)
        {
            return phaseNormalized >= PourFlowStartNormalized &&
                   phaseNormalized <= PourFlowEndNormalized;
        }

        public static float ResolveDrinkSipNormalized(
            float phaseNormalized)
        {
            return Mathf.InverseLerp(
                DrinkSipStartNormalized,
                DrinkSipEndNormalized,
                phaseNormalized);
        }

        public static float ResolvePourFlowNormalized(
            float phaseNormalized)
        {
            return Mathf.InverseLerp(
                PourFlowStartNormalized,
                PourFlowEndNormalized,
                phaseNormalized);
        }

        public static float ResolveMenuPlacementNormalized(
            float phaseNormalized)
        {
            return Mathf.InverseLerp(
                MenuPlaceStartNormalized,
                MenuPlaceEndNormalized,
                phaseNormalized);
        }

        public static float ResolveMenuPickupNormalized(float phaseNormalized)
        {
            return ResolveMenuPlacementNormalized(phaseNormalized);
        }

        public void Reset(int seed)
        {
            randomState = unchecked((uint)seed) ^ 0x9E3779B9u;
            if (randomState == 0u)
            {
                randomState = 0x6D2B79F5u;
            }

            // The pair starts at different levels and drinks in separate
            // windows. The man's first sip crosses the service threshold, so
            // the tableau the player reaches after the climb exposes its
            // complete drink/refill contract within the first visible loop.
            fills[0] = InitialPairManFill;
            fills[1] = InitialPairWomanFill;
            Array.Copy(fills, phaseStartFills, fills.Length);
            Array.Copy(fills, phaseTargetFills, fills.Length);
            sequence = 0;
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            heroMenuRequested = false;
            heroMenuPlaced = false;
            heroMenuRetrievalRequested = false;
            heroMenuRetrieved = false;
            BeginPhase(
                MountainRoadCafeServicePhase.Wiping,
                NextWipeDuration());
        }

        public void Advance(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds),
                    "Cafe service time must be finite and non-negative.");
            }

            float remaining = deltaSeconds;
            int transitions = 0;
            while (remaining > 0f)
            {
                float toBoundary = Mathf.Max(
                    0f,
                    phaseDurationSeconds - phaseElapsedSeconds);
                float step = Mathf.Min(remaining, toBoundary);
                phaseElapsedSeconds += step;
                remaining -= step;
                ApplyContinuousState();

                if (phaseElapsedSeconds + 0.000001f <
                    phaseDurationSeconds)
                {
                    break;
                }

                CompletePhase();
                transitions++;
                if (transitions > MaximumTransitionsPerAdvance)
                {
                    throw new InvalidOperationException(
                        "Cafe service timeline crossed too many phases in " +
                        "one Advance call.");
                }
            }
        }

        public bool TryRequestDrink(MountainRoadCafeCastRole role)
        {
            if (phase != MountainRoadCafeServicePhase.Wiping)
            {
                return false;
            }

            if (role == MountainRoadCafeCastRole.PairMan ||
                role == MountainRoadCafeCastRole.PairWoman)
            {
                BeginCoupleDrink();
                return true;
            }

            return false;
        }

        /// <summary>
        /// The hero may interrupt wiping with the attendant's authored
        /// notice beat. It intentionally carries no service target and
        /// therefore cannot enter Walk/Pour or mutate any cup.
        /// </summary>
        public bool TryRequestHeroNotice()
        {
            if (phase != MountainRoadCafeServicePhase.Wiping)
            {
                return false;
            }

            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            BeginPhase(
                MountainRoadCafeServicePhase.Notice,
                NoticeSeconds);
            return true;
        }

        /// <summary>
        /// Queues the one physical menu handoff. Unlike the old notice-only
        /// beat, a request made while the attendant is pouring is retained
        /// and starts as soon as the current service walk returns to its
        /// dock. It never creates a cup target or mutates either fill.
        /// </summary>
        public bool TryRequestHeroMenu()
        {
            if (heroMenuRequested || heroMenuPlaced ||
                heroMenuRetrievalRequested || heroMenuRetrieved)
            {
                return false;
            }

            heroMenuRequested = true;
            if (phase == MountainRoadCafeServicePhase.Wiping)
            {
                BeginMenuNotice();
            }

            return true;
        }

        public bool TryResetHeroMenuRoundTrip()
        {
            if (!heroMenuRetrieved || heroMenuRequested ||
                heroMenuPlaced || heroMenuRetrievalRequested)
            {
                return false;
            }

            heroMenuRetrieved = false;
            return true;
        }

        /// <summary>
        /// Queues the reverse handoff after the booklet is on the counter.
        /// Active drink, delivery and return phases finish first; the same
        /// attendant then walks back, takes the menu and carries it to the
        /// service dock without touching either patron cup.
        /// </summary>
        public bool TryRequestHeroMenuRetrieval()
        {
            if (!heroMenuPlaced || heroMenuRetrievalRequested ||
                heroMenuRetrieved)
            {
                return false;
            }

            heroMenuRetrievalRequested = true;
            if (phase == MountainRoadCafeServicePhase.Wiping)
            {
                BeginWalkToMenu();
            }

            return true;
        }

        private void CompletePhase()
        {
            ApplyContinuousState();
            switch (phase)
            {
                case MountainRoadCafeServicePhase.Wiping:
                    BeginCoupleDrink();
                    break;
                case MountainRoadCafeServicePhase.CoupleDrink:
                    BeginCoupleRefillOrWiping();
                    break;
                case MountainRoadCafeServicePhase.Notice:
                    if (serviceTargetIndex < 0)
                    {
                        BeginWiping();
                    }
                    else
                    {
                        BeginPhase(
                            MountainRoadCafeServicePhase.WalkToCup,
                            WalkSeconds);
                    }
                    break;
                case MountainRoadCafeServicePhase.WalkToCup:
                    CapturePourTargets();
                    BeginPhase(
                        MountainRoadCafeServicePhase.Pour,
                        PourSeconds);
                    break;
                case MountainRoadCafeServicePhase.Pour:
                    if (queuedServiceTargetIndex >= 0)
                    {
                        walkOriginIndex = serviceTargetIndex;
                        serviceTargetIndex = queuedServiceTargetIndex;
                        queuedServiceTargetIndex = -1;
                        BeginPhase(
                            MountainRoadCafeServicePhase.WalkToCup,
                            WalkSeconds);
                    }
                    else
                    {
                        walkOriginIndex = serviceTargetIndex;
                        BeginPhase(
                            MountainRoadCafeServicePhase.WalkBack,
                            WalkSeconds);
                    }
                    break;
                case MountainRoadCafeServicePhase.WalkBack:
                    BeginWiping();
                    break;
                case MountainRoadCafeServicePhase.MenuNotice:
                    BeginPhase(
                        MountainRoadCafeServicePhase.WalkToHero,
                        WalkSeconds);
                    break;
                case MountainRoadCafeServicePhase.WalkToHero:
                    BeginPhase(
                        MountainRoadCafeServicePhase.PlaceMenu,
                        NoticeSeconds);
                    break;
                case MountainRoadCafeServicePhase.PlaceMenu:
                    heroMenuRequested = false;
                    heroMenuPlaced = true;
                    BeginPhase(
                        MountainRoadCafeServicePhase.MenuWalkBack,
                        WalkSeconds);
                    break;
                case MountainRoadCafeServicePhase.MenuWalkBack:
                    BeginWiping();
                    break;
                case MountainRoadCafeServicePhase.WalkToMenu:
                    BeginPhase(
                        MountainRoadCafeServicePhase.TakeMenu,
                        NoticeSeconds);
                    break;
                case MountainRoadCafeServicePhase.TakeMenu:
                    heroMenuPlaced = false;
                    BeginPhase(
                        MountainRoadCafeServicePhase.CarryMenuBack,
                        WalkSeconds);
                    break;
                case MountainRoadCafeServicePhase.CarryMenuBack:
                    heroMenuRetrievalRequested = false;
                    heroMenuRetrieved = true;
                    BeginWiping();
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown cafe service phase.");
            }
        }

        private void BeginCoupleDrink()
        {
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            phaseTargetFills[0] = Mathf.Max(
                0f,
                fills[0] - PairManSipConsumedAmount);
            phaseTargetFills[1] = Mathf.Max(
                0f,
                fills[1] - PairWomanSipConsumedAmount);
            BeginPhase(
                MountainRoadCafeServicePhase.CoupleDrink,
                CoupleDrinkSeconds);
        }

        private void BeginNotice(int targetIndex, int queuedTargetIndex)
        {
            serviceTargetIndex = targetIndex;
            queuedServiceTargetIndex = queuedTargetIndex;
            walkOriginIndex = -1;
            BeginPhase(
                MountainRoadCafeServicePhase.Notice,
                NoticeSeconds);
        }

        private void BeginCoupleRefillOrWiping()
        {
            bool manNeedsRefill = fills[0] <= RefillRequestLevel;
            bool womanNeedsRefill = fills[1] <= RefillRequestLevel;
            if (manNeedsRefill)
            {
                BeginNotice(0, womanNeedsRefill ? 1 : -1);
            }
            else if (womanNeedsRefill)
            {
                BeginNotice(1, -1);
            }
            else
            {
                BeginWiping();
            }
        }

        private void BeginWiping()
        {
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            if (heroMenuRequested && !heroMenuPlaced)
            {
                BeginMenuNotice();
                return;
            }

            if (heroMenuRetrievalRequested && heroMenuPlaced)
            {
                BeginWalkToMenu();
                return;
            }

            BeginPhase(
                MountainRoadCafeServicePhase.Wiping,
                NextWipeDuration());
        }

        private void BeginMenuNotice()
        {
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            BeginPhase(
                MountainRoadCafeServicePhase.MenuNotice,
                NoticeSeconds);
        }

        private void BeginWalkToMenu()
        {
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            BeginPhase(
                MountainRoadCafeServicePhase.WalkToMenu,
                WalkSeconds);
        }

        private void CapturePourTargets()
        {
            CaptureCurrentFills();
            phaseTargetFills[serviceTargetIndex] = RefilledLevel;
        }

        private void CaptureCurrentFills()
        {
            Array.Copy(fills, phaseStartFills, fills.Length);
            Array.Copy(fills, phaseTargetFills, fills.Length);
        }

        private void ApplyContinuousState()
        {
            float amount = phaseDurationSeconds > 0f
                ? Mathf.Clamp01(
                    phaseElapsedSeconds / phaseDurationSeconds)
                : 1f;
            if (phase == MountainRoadCafeServicePhase.CoupleDrink)
            {
                amount = ResolveDrinkSipNormalized(
                    ResolvePairDrinkNormalized(0));
                fills[0] = Mathf.Lerp(
                    phaseStartFills[0],
                    phaseTargetFills[0],
                    amount);
                amount = ResolveDrinkSipNormalized(
                    ResolvePairDrinkNormalized(1));
                fills[1] = Mathf.Lerp(
                    phaseStartFills[1],
                    phaseTargetFills[1],
                    amount);
            }
            else if (phase == MountainRoadCafeServicePhase.Pour &&
                     serviceTargetIndex >= 0)
            {
                amount = ResolvePourFlowNormalized(amount);
                fills[serviceTargetIndex] = Mathf.Lerp(
                    phaseStartFills[serviceTargetIndex],
                    phaseTargetFills[serviceTargetIndex],
                    amount);
            }
        }

        private float ResolvePairDrinkNormalized(int cupIndex)
        {
            float start = cupIndex == 0
                ? 0f
                : PairWomanDrinkStartSeconds;
            return Mathf.Clamp01(
                (phaseElapsedSeconds - start) /
                PairPatronDrinkSeconds);
        }

        private void BeginPhase(
            MountainRoadCafeServicePhase nextPhase,
            float durationSeconds)
        {
            if (durationSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(durationSeconds),
                    "Cafe service phases require positive durations.");
            }

            phase = nextPhase;
            phaseElapsedSeconds = 0f;
            phaseDurationSeconds = durationSeconds;
            sequence++;
        }

        private float NextWipeDuration()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            float sample = (randomState & 0x00FFFFFFu) /
                           16777215f;
            return Mathf.Lerp(
                MinimumWipeSeconds,
                MaximumWipeSeconds,
                sample);
        }

        private static MountainRoadCafeCastRole RoleFromCupIndex(
            int index)
        {
            switch (index)
            {
                case 0:
                    return MountainRoadCafeCastRole.PairMan;
                case 1:
                    return MountainRoadCafeCastRole.PairWoman;
                default:
                    return default;
            }
        }

        internal static float GetPairDrinkStartSeconds(
            MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.PairMan:
                    return 0f;
                case MountainRoadCafeCastRole.PairWoman:
                    return PairWomanDrinkStartSeconds;
                default:
                    return float.PositiveInfinity;
            }
        }
    }
}
