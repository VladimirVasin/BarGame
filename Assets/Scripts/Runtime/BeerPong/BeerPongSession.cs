using System;

namespace BarPromenade
{
    public sealed class BeerPongSession
    {
        public const int ThrowLimit = 10;
        public const int SinkScore = 100;
        public const int BankShotBonus = 50;
        public const int UnusedThrowBonus = 50;
        public const int MissIntoxicationGain = 8;
        public const int MaximumIntoxication = 100;

        private int standingCupMask;
        private bool throwInProgress;

        public BeerPongSession(
            int initialIntoxication,
            DrinkId lastAlcoholicDrink,
            int drinksConsumed)
        {
            if (initialIntoxication < 0 ||
                initialIntoxication > MaximumIntoxication)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialIntoxication));
            }

            if (drinksConsumed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(drinksConsumed));
            }

            if (lastAlcoholicDrink != DrinkId.None &&
                lastAlcoholicDrink != DrinkId.Water &&
                !DrinkRules.IsAlcoholic(lastAlcoholicDrink))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lastAlcoholicDrink));
            }

            Intoxication = initialIntoxication;
            LastAlcoholicDrink =
                lastAlcoholicDrink == DrinkId.Water
                    ? DrinkId.None
                    : lastAlcoholicDrink;
            DrinksConsumed = drinksConsumed;
            standingCupMask = BeerPongTableLayout.Default.AllCupsMask;
            Outcome = initialIntoxication >= MaximumIntoxication
                ? BeerPongSessionOutcome.MaxIntoxicationReached
                : BeerPongSessionOutcome.InProgress;
        }

        public int Intoxication { get; private set; }
        public DrinkId LastAlcoholicDrink { get; private set; }
        public int DrinksConsumed { get; private set; }
        public int ThrowsCompleted { get; private set; }
        public int TotalScore { get; private set; }
        public int StandingCupMask => standingCupMask;
        public int CupsRemaining { get; private set; } =
            BeerPongTableLayout.CupCount;
        public int ThrowsRemaining =>
            Math.Max(0, ThrowLimit - ThrowsCompleted);
        public BeerPongSessionOutcome Outcome { get; private set; }
        public bool IsFinished =>
            Outcome != BeerPongSessionOutcome.InProgress;
        public bool IsThrowInProgress => throwInProgress;
        public bool CanBeginThrow => !IsFinished && !throwInProgress;

        public int BeginThrow()
        {
            RequireInProgress();
            if (throwInProgress)
            {
                throw new InvalidOperationException(
                    "A beer-pong throw is already in progress.");
            }

            throwInProgress = true;
            return standingCupMask;
        }

        public BeerPongThrowResult CompleteThrow(
            BeerPongFlightResult flightResult)
        {
            RequireInProgress();
            if (!throwInProgress)
            {
                throw new InvalidOperationException(
                    "Begin a throw before completing it.");
            }

            if (!flightResult.IsTerminal)
            {
                throw new ArgumentException(
                    "A throw requires a terminal physics result.",
                    nameof(flightResult));
            }

            if (flightResult.WasSunk &&
                !IsCupStanding(flightResult.CupIndex))
            {
                throw new ArgumentException(
                    "The sunk cup is not standing.",
                    nameof(flightResult));
            }

            throwInProgress = false;
            ThrowsCompleted++;
            int scoreAwarded = 0;
            int earlyClearBonus = 0;
            int intoxicationDelta = 0;
            DrinkId consumedDrink = DrinkId.None;

            if (flightResult.WasSunk)
            {
                standingCupMask &= ~(1 << flightResult.CupIndex);
                CupsRemaining--;
                scoreAwarded =
                    SinkScore +
                    (flightResult.IsBankShot ? BankShotBonus : 0);
                TotalScore += scoreAwarded;
            }
            else
            {
                intoxicationDelta = Math.Min(
                    MissIntoxicationGain,
                    MaximumIntoxication - Intoxication);
                Intoxication += intoxicationDelta;
                LastAlcoholicDrink = DrinkId.LightBeer;
                DrinksConsumed++;
                consumedDrink = DrinkId.LightBeer;
            }

            if (CupsRemaining == 0)
            {
                earlyClearBonus = ThrowsRemaining * UnusedThrowBonus;
                TotalScore += earlyClearBonus;
                Outcome = BeerPongSessionOutcome.Cleared;
            }
            else if (Intoxication >= MaximumIntoxication)
            {
                Outcome =
                    BeerPongSessionOutcome.MaxIntoxicationReached;
            }
            else if (ThrowsCompleted >= ThrowLimit)
            {
                Outcome = BeerPongSessionOutcome.ThrowLimitReached;
            }

            return new BeerPongThrowResult(
                ThrowsCompleted,
                flightResult.WasSunk,
                flightResult.CupIndex,
                flightResult.IsBankShot,
                flightResult.MissReason,
                scoreAwarded,
                earlyClearBonus,
                TotalScore,
                intoxicationDelta,
                Intoxication,
                consumedDrink,
                DrinksConsumed,
                CupsRemaining,
                ThrowsRemaining,
                Outcome);
        }

        public void CancelThrow()
        {
            if (throwInProgress)
            {
                throwInProgress = false;
            }
        }

        public bool IsCupStanding(int cupIndex)
        {
            return
                cupIndex >= 0 &&
                cupIndex < BeerPongTableLayout.CupCount &&
                (standingCupMask & (1 << cupIndex)) != 0;
        }

        private void RequireInProgress()
        {
            if (IsFinished)
            {
                throw new InvalidOperationException(
                    "A finished beer-pong session cannot be changed.");
            }
        }
    }
}
