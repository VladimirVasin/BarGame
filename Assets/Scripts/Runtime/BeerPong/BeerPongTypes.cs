using System;
using UnityEngine;

namespace BarPromenade
{
    public enum BeerPongBallStatus
    {
        Ready = 0,
        InFlight,
        Sunk,
        Missed
    }

    public enum BeerPongMissReason
    {
        None = 0,
        OutOfBounds,
        Settled,
        Timeout
    }

    public enum BeerPongSessionOutcome
    {
        InProgress = 0,
        Cleared,
        ThrowLimitReached,
        MaxIntoxicationReached
    }

    public readonly struct BeerPongCupDefinition
    {
        public BeerPongCupDefinition(
            int index,
            Vector3 mouthCenter,
            float mouthRadius,
            float height)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (!BeerPongMath.IsFinite(mouthCenter))
            {
                throw new ArgumentException(
                    "Cup position must be finite.",
                    nameof(mouthCenter));
            }

            if (!BeerPongMath.IsFinitePositive(mouthRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(mouthRadius));
            }

            if (!BeerPongMath.IsFinitePositive(height))
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Index = index;
            MouthCenter = mouthCenter;
            MouthRadius = mouthRadius;
            Height = height;
        }

        public int Index { get; }
        public Vector3 MouthCenter { get; }
        public float MouthRadius { get; }
        public float Height { get; }
        public Vector3 BaseCenter =>
            MouthCenter + Vector3.down * Height;
    }

    public readonly struct BeerPongBallSnapshot
    {
        internal BeerPongBallSnapshot(
            BeerPongBallStatus status,
            Vector3 position,
            Vector3 velocity,
            float elapsedTime,
            int tableBounceCount,
            int rimBounceCount)
        {
            Status = status;
            Position = position;
            Velocity = velocity;
            ElapsedTime = elapsedTime;
            TableBounceCount = tableBounceCount;
            RimBounceCount = rimBounceCount;
        }

        public BeerPongBallStatus Status { get; }
        public Vector3 Position { get; }
        public Vector3 Velocity { get; }
        public float ElapsedTime { get; }
        public int TableBounceCount { get; }
        public int RimBounceCount { get; }
    }

    public readonly struct BeerPongFlightResult
    {
        private BeerPongFlightResult(
            BeerPongBallStatus status,
            int cupIndex,
            BeerPongMissReason missReason,
            bool isBankShot,
            float flightTime,
            int tableBounceCount,
            int rimBounceCount,
            Vector3 finalPosition,
            Vector3 finalVelocity)
        {
            Status = status;
            CupIndex = cupIndex;
            MissReason = missReason;
            IsBankShot = isBankShot;
            FlightTime = flightTime;
            TableBounceCount = tableBounceCount;
            RimBounceCount = rimBounceCount;
            FinalPosition = finalPosition;
            FinalVelocity = finalVelocity;
        }

        public BeerPongBallStatus Status { get; }
        public int CupIndex { get; }
        public BeerPongMissReason MissReason { get; }
        public bool IsBankShot { get; }
        public float FlightTime { get; }
        public int TableBounceCount { get; }
        public int RimBounceCount { get; }
        public Vector3 FinalPosition { get; }
        public Vector3 FinalVelocity { get; }
        public bool IsTerminal =>
            Status == BeerPongBallStatus.Sunk ||
            Status == BeerPongBallStatus.Missed;
        public bool WasSunk => Status == BeerPongBallStatus.Sunk;

        public static BeerPongFlightResult CreateSink(
            int cupIndex,
            bool isBankShot = false,
            float flightTime = 0f,
            int tableBounceCount = 0,
            int rimBounceCount = 0,
            Vector3 finalPosition = default,
            Vector3 finalVelocity = default)
        {
            if (cupIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cupIndex));
            }

            ValidateDiagnostics(
                flightTime,
                tableBounceCount,
                rimBounceCount,
                finalPosition,
                finalVelocity);

            int resolvedTableBounces = isBankShot
                ? Math.Max(1, tableBounceCount)
                : tableBounceCount;
            return new BeerPongFlightResult(
                BeerPongBallStatus.Sunk,
                cupIndex,
                BeerPongMissReason.None,
                isBankShot,
                flightTime,
                resolvedTableBounces,
                rimBounceCount,
                finalPosition,
                finalVelocity);
        }

        public static BeerPongFlightResult CreateMiss(
            BeerPongMissReason reason,
            float flightTime = 0f,
            int tableBounceCount = 0,
            int rimBounceCount = 0,
            Vector3 finalPosition = default,
            Vector3 finalVelocity = default)
        {
            if (reason == BeerPongMissReason.None)
            {
                throw new ArgumentOutOfRangeException(nameof(reason));
            }

            ValidateDiagnostics(
                flightTime,
                tableBounceCount,
                rimBounceCount,
                finalPosition,
                finalVelocity);

            return new BeerPongFlightResult(
                BeerPongBallStatus.Missed,
                -1,
                reason,
                false,
                flightTime,
                tableBounceCount,
                rimBounceCount,
                finalPosition,
                finalVelocity);
        }

        private static void ValidateDiagnostics(
            float flightTime,
            int tableBounceCount,
            int rimBounceCount,
            Vector3 finalPosition,
            Vector3 finalVelocity)
        {
            if (!BeerPongMath.IsFiniteNonNegative(flightTime))
            {
                throw new ArgumentOutOfRangeException(nameof(flightTime));
            }

            if (tableBounceCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableBounceCount));
            }

            if (rimBounceCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rimBounceCount));
            }

            if (!BeerPongMath.IsFinite(finalPosition))
            {
                throw new ArgumentException(
                    "Final position must be finite.",
                    nameof(finalPosition));
            }

            if (!BeerPongMath.IsFinite(finalVelocity))
            {
                throw new ArgumentException(
                    "Final velocity must be finite.",
                    nameof(finalVelocity));
            }
        }
    }

    public readonly struct BeerPongThrowResult
    {
        internal BeerPongThrowResult(
            int throwNumber,
            bool wasSunk,
            int cupIndex,
            bool wasBankShot,
            BeerPongMissReason missReason,
            int scoreAwarded,
            int earlyClearBonus,
            int totalScore,
            int intoxicationDelta,
            int currentIntoxication,
            DrinkId consumedDrink,
            int drinksConsumed,
            int cupsRemaining,
            int throwsRemaining,
            BeerPongSessionOutcome sessionOutcome)
        {
            ThrowNumber = throwNumber;
            WasSunk = wasSunk;
            CupIndex = cupIndex;
            WasBankShot = wasBankShot;
            MissReason = missReason;
            ScoreAwarded = scoreAwarded;
            EarlyClearBonus = earlyClearBonus;
            TotalScore = totalScore;
            IntoxicationDelta = intoxicationDelta;
            CurrentIntoxication = currentIntoxication;
            ConsumedDrink = consumedDrink;
            DrinksConsumed = drinksConsumed;
            CupsRemaining = cupsRemaining;
            ThrowsRemaining = throwsRemaining;
            SessionOutcome = sessionOutcome;
        }

        public int ThrowNumber { get; }
        public bool WasSunk { get; }
        public int CupIndex { get; }
        public bool WasBankShot { get; }
        public BeerPongMissReason MissReason { get; }
        public int ScoreAwarded { get; }
        public int EarlyClearBonus { get; }
        public int TotalScore { get; }
        public int IntoxicationDelta { get; }
        public int CurrentIntoxication { get; }
        public DrinkId ConsumedDrink { get; }
        public int DrinksConsumed { get; }
        public int CupsRemaining { get; }
        public int ThrowsRemaining { get; }
        public BeerPongSessionOutcome SessionOutcome { get; }
    }

    internal static class BeerPongMath
    {
        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        public static bool IsFiniteNonNegative(float value)
        {
            return IsFinite(value) && value >= 0f;
        }

        public static bool IsFinite(Vector3 value)
        {
            return
                IsFinite(value.x) &&
                IsFinite(value.y) &&
                IsFinite(value.z);
        }
    }
}
