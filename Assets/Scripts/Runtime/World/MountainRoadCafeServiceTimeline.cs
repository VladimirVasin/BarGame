using System;
using UnityEngine;

namespace BarPromenade
{
    public enum MountainRoadCafeServicePhase
    {
        Wiping = 0,
        LoneDrink = 1,
        CoupleDrink = 2,
        Notice = 3,
        WalkToCup = 4,
        Pour = 5,
        WalkBack = 6
    }

    /// <summary>
    /// Immutable view of the cafe's three-cup service state. The hero is
    /// deliberately absent: sitting at the spare stool can earn a glance,
    /// never a fourth cup or a place in the service queue.
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
            float loneFill,
            float pairManFill,
            float pairWomanFill)
        {
            Phase = phase;
            PhaseElapsedSeconds = phaseElapsedSeconds;
            PhaseDurationSeconds = phaseDurationSeconds;
            Sequence = sequence;
            ServiceTarget = serviceTarget;
            HasServiceTarget = hasServiceTarget;
            WalkOrigin = walkOrigin;
            HasWalkOrigin = hasWalkOrigin;
            LoneFill = loneFill;
            PairManFill = pairManFill;
            PairWomanFill = pairWomanFill;
        }

        public MountainRoadCafeServicePhase Phase { get; }
        public float PhaseElapsedSeconds { get; }
        public float PhaseDurationSeconds { get; }
        public int Sequence { get; }
        public MountainRoadCafeCastRole ServiceTarget { get; }
        public bool HasServiceTarget { get; }
        public MountainRoadCafeCastRole WalkOrigin { get; }
        public bool HasWalkOrigin { get; }
        public float LoneFill { get; }
        public float PairManFill { get; }
        public float PairWomanFill { get; }
        public float PhaseNormalized => PhaseDurationSeconds > 0f
            ? Mathf.Clamp01(PhaseElapsedSeconds / PhaseDurationSeconds)
            : 1f;

        public float GetFill(MountainRoadCafeCastRole role)
        {
            switch (role)
            {
                case MountainRoadCafeCastRole.LonePatron:
                    return LoneFill;
                case MountainRoadCafeCastRole.PairMan:
                    return PairManFill;
                case MountainRoadCafeCastRole.PairWoman:
                    return PairWomanFill;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(role),
                        role,
                        "Only the three authored patrons own cafe cups.");
            }
        }
    }

    /// <summary>
    /// Pure, deterministic and hitch-safe timeline for the silent cafe loop.
    /// Advance consumes the complete supplied delta, carrying remainder over
    /// every phase boundary instead of losing time to a per-frame clamp.
    /// </summary>
    public sealed class MountainRoadCafeServiceTimeline
    {
        public const float LoneDrinkSeconds = 5f;
        public const float CoupleDrinkSeconds = 4.75f;
        public const float DrinkSipStartNormalized = 0.48f;
        public const float DrinkSipEndNormalized = 0.62f;
        public const float NoticeSeconds = 2.5f;
        public const float WalkSeconds = 1.25f;
        public const float PourSeconds = 3.5f;
        public const float PourFlowStartNormalized = 0.38f;
        public const float PourFlowEndNormalized = 0.72f;
        public const float MinimumWipeSeconds = 18f;
        public const float MaximumWipeSeconds = 32f;
        public const float RefilledLevel = 0.90f;
        public const float RefillRequestLevel = 0.30f;
        public const bool ServesHero = false;

        private const float SipConsumedAmount = 0.22f;
        private const int MaximumTransitionsPerAdvance = 4096;

        private readonly float[] fills = new float[3];
        private readonly float[] phaseStartFills = new float[3];
        private readonly float[] phaseTargetFills = new float[3];
        private uint randomState;
        private MountainRoadCafeServicePhase phase;
        private float phaseElapsedSeconds;
        private float phaseDurationSeconds;
        private int sequence;
        private int serviceTargetIndex;
        private int queuedServiceTargetIndex;
        private int walkOriginIndex;
        private bool nextDrinkIsCouple;

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
                fills[2]);

        public float RemainingPhaseSeconds => Mathf.Max(
            0f,
            phaseDurationSeconds - phaseElapsedSeconds);

        public static bool IsPatronWithCup(
            MountainRoadCafeCastRole role)
        {
            return role == MountainRoadCafeCastRole.LonePatron ||
                   role == MountainRoadCafeCastRole.PairMan ||
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

        public void Reset(int seed)
        {
            randomState = unchecked((uint)seed) ^ 0x9E3779B9u;
            if (randomState == 0u)
            {
                randomState = 0x6D2B79F5u;
            }

            fills[0] = 0.62f;
            fills[1] = 0.78f;
            fills[2] = 0.78f;
            Array.Copy(fills, phaseStartFills, fills.Length);
            Array.Copy(fills, phaseTargetFills, fills.Length);
            nextDrinkIsCouple = (seed & 1) != 0;
            sequence = 0;
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
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

            if (role == MountainRoadCafeCastRole.LonePatron)
            {
                BeginLoneDrink();
                return true;
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

        private void CompletePhase()
        {
            ApplyContinuousState();
            switch (phase)
            {
                case MountainRoadCafeServicePhase.Wiping:
                    if (nextDrinkIsCouple)
                    {
                        BeginCoupleDrink();
                    }
                    else
                    {
                        BeginLoneDrink();
                    }
                    break;
                case MountainRoadCafeServicePhase.LoneDrink:
                    if (fills[0] <= RefillRequestLevel)
                    {
                        BeginNotice(0, -1);
                    }
                    else
                    {
                        BeginWiping();
                    }
                    break;
                case MountainRoadCafeServicePhase.CoupleDrink:
                    if (fills[1] <= RefillRequestLevel ||
                        fills[2] <= RefillRequestLevel)
                    {
                        BeginNotice(1, 2);
                    }
                    else
                    {
                        BeginWiping();
                    }
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
                default:
                    throw new InvalidOperationException(
                        "Unknown cafe service phase.");
            }
        }

        private void BeginLoneDrink()
        {
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            phaseTargetFills[0] = Mathf.Max(
                0f,
                fills[0] - SipConsumedAmount);
            BeginPhase(
                MountainRoadCafeServicePhase.LoneDrink,
                LoneDrinkSeconds);
        }

        private void BeginCoupleDrink()
        {
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            phaseTargetFills[1] = Mathf.Max(
                0f,
                fills[1] - SipConsumedAmount);
            phaseTargetFills[2] = Mathf.Max(
                0f,
                fills[2] - SipConsumedAmount);
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

        private void BeginWiping()
        {
            nextDrinkIsCouple = !nextDrinkIsCouple;
            serviceTargetIndex = -1;
            queuedServiceTargetIndex = -1;
            walkOriginIndex = -1;
            CaptureCurrentFills();
            BeginPhase(
                MountainRoadCafeServicePhase.Wiping,
                NextWipeDuration());
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
            if (phase == MountainRoadCafeServicePhase.LoneDrink)
            {
                amount = ResolveDrinkSipNormalized(amount);
                fills[0] = Mathf.Lerp(
                    phaseStartFills[0],
                    phaseTargetFills[0],
                    amount);
            }
            else if (phase == MountainRoadCafeServicePhase.CoupleDrink)
            {
                amount = ResolveDrinkSipNormalized(amount);
                fills[1] = Mathf.Lerp(
                    phaseStartFills[1],
                    phaseTargetFills[1],
                    amount);
                fills[2] = Mathf.Lerp(
                    phaseStartFills[2],
                    phaseTargetFills[2],
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
                    return MountainRoadCafeCastRole.LonePatron;
                case 1:
                    return MountainRoadCafeCastRole.PairMan;
                case 2:
                    return MountainRoadCafeCastRole.PairWoman;
                default:
                    return default;
            }
        }
    }
}
