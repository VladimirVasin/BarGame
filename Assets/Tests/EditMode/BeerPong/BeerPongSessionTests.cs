using System;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class BeerPongSessionTests
    {
        [Test]
        public void Constructor_PreservesDrinkingContextAndSetsSixCups()
        {
            var session = new BeerPongSession(
                24,
                DrinkId.RedWine,
                7);

            Assert.That(session.Intoxication, Is.EqualTo(24));
            Assert.That(
                session.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.RedWine));
            Assert.That(session.DrinksConsumed, Is.EqualTo(7));
            Assert.That(
                session.CupsRemaining,
                Is.EqualTo(BeerPongTableLayout.CupCount));
            Assert.That(session.ThrowsRemaining, Is.EqualTo(10));
            Assert.That(session.CanBeginThrow, Is.True);
        }

        [Test]
        public void Miss_AddsOneLightBeerAndEightIntoxication()
        {
            var session = CreateSession(17, DrinkId.CognacVs, 4);
            session.BeginThrow();

            BeerPongThrowResult result = session.CompleteThrow(
                BeerPongFlightResult.CreateMiss(
                    BeerPongMissReason.OutOfBounds));

            Assert.That(result.WasSunk, Is.False);
            Assert.That(result.IntoxicationDelta, Is.EqualTo(8));
            Assert.That(result.CurrentIntoxication, Is.EqualTo(25));
            Assert.That(result.ConsumedDrink, Is.EqualTo(DrinkId.LightBeer));
            Assert.That(result.DrinksConsumed, Is.EqualTo(5));
            Assert.That(
                session.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(session.CupsRemaining, Is.EqualTo(6));
            Assert.That(session.TotalScore, Is.Zero);
        }

        [Test]
        public void BankSink_RemovesCupAndAwardsOneHundredFifty()
        {
            var session = CreateSession();
            int startingMask = session.BeginThrow();
            Assert.That(startingMask, Is.EqualTo(0b11_1111));

            BeerPongThrowResult result = session.CompleteThrow(
                BeerPongFlightResult.CreateSink(3, true));

            Assert.That(result.WasSunk, Is.True);
            Assert.That(result.WasBankShot, Is.True);
            Assert.That(result.ScoreAwarded, Is.EqualTo(150));
            Assert.That(result.EarlyClearBonus, Is.Zero);
            Assert.That(session.IsCupStanding(3), Is.False);
            Assert.That(session.CupsRemaining, Is.EqualTo(5));
            Assert.That(session.Intoxication, Is.Zero);
            Assert.That(session.DrinksConsumed, Is.Zero);
        }

        [Test]
        public void SixStraightSinks_ClearAndAwardUnusedThrowBonus()
        {
            var session = CreateSession();

            BeerPongThrowResult finalResult = default;
            for (int cupIndex = 0;
                 cupIndex < BeerPongTableLayout.CupCount;
                 cupIndex++)
            {
                session.BeginThrow();
                finalResult = session.CompleteThrow(
                    BeerPongFlightResult.CreateSink(cupIndex));
            }

            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(
                BeerPongSessionOutcome.Cleared));
            Assert.That(session.ThrowsCompleted, Is.EqualTo(6));
            Assert.That(session.ThrowsRemaining, Is.EqualTo(4));
            Assert.That(finalResult.EarlyClearBonus, Is.EqualTo(200));
            Assert.That(session.TotalScore, Is.EqualTo(800));
        }

        [Test]
        public void TenMisses_EndAtThrowLimit()
        {
            var session = CreateSession();

            for (int throwIndex = 0;
                 throwIndex < BeerPongSession.ThrowLimit;
                 throwIndex++)
            {
                session.BeginThrow();
                session.CompleteThrow(BeerPongFlightResult.CreateMiss(
                    BeerPongMissReason.Settled));
            }

            Assert.That(session.Outcome, Is.EqualTo(
                BeerPongSessionOutcome.ThrowLimitReached));
            Assert.That(session.Intoxication, Is.EqualTo(80));
            Assert.That(session.DrinksConsumed, Is.EqualTo(10));
            Assert.That(session.ThrowsRemaining, Is.Zero);
            Assert.Throws<InvalidOperationException>(
                () => session.BeginThrow());
        }

        [Test]
        public void MissAtNinetyTwo_EndsAtMaximumBeforeThrowLimit()
        {
            var session = CreateSession(92);
            session.BeginThrow();

            BeerPongThrowResult result = session.CompleteThrow(
                BeerPongFlightResult.CreateMiss(
                    BeerPongMissReason.Timeout));

            Assert.That(result.IntoxicationDelta, Is.EqualTo(8));
            Assert.That(result.CurrentIntoxication, Is.EqualTo(100));
            Assert.That(session.Outcome, Is.EqualTo(
                BeerPongSessionOutcome.MaxIntoxicationReached));
            Assert.That(session.ThrowsCompleted, Is.EqualTo(1));
        }

        [Test]
        public void StartingAtMaximum_IsImmediatelyFinished()
        {
            BeerPongSession session = CreateSession(100);

            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.Outcome, Is.EqualTo(
                BeerPongSessionOutcome.MaxIntoxicationReached));
            Assert.That(session.CanBeginThrow, Is.False);
            Assert.Throws<InvalidOperationException>(
                () => session.BeginThrow());
        }

        [Test]
        public void ThrowMustBeBegunAndCannotSinkRemovedCup()
        {
            var session = CreateSession();
            BeerPongFlightResult sink =
                BeerPongFlightResult.CreateSink(2);

            Assert.Throws<InvalidOperationException>(
                () => session.CompleteThrow(sink));

            session.BeginThrow();
            session.CompleteThrow(sink);
            session.BeginThrow();
            Assert.Throws<ArgumentException>(
                () => session.CompleteThrow(sink));
            Assert.That(session.IsThrowInProgress, Is.True);
            session.CancelThrow();
            Assert.That(session.IsThrowInProgress, Is.False);
            Assert.That(session.ThrowsCompleted, Is.EqualTo(1));
        }

        [Test]
        public void CancellingFlight_DoesNotSpendThrowOrApplyPenalty()
        {
            var session = CreateSession(12, DrinkId.Vodka, 2);
            session.BeginThrow();

            session.CancelThrow();

            Assert.That(session.ThrowsCompleted, Is.Zero);
            Assert.That(session.Intoxication, Is.EqualTo(12));
            Assert.That(session.DrinksConsumed, Is.EqualTo(2));
            Assert.That(session.CanBeginThrow, Is.True);
        }

        private static BeerPongSession CreateSession(
            int intoxication = 0,
            DrinkId lastDrink = DrinkId.None,
            int drinksConsumed = 0)
        {
            return new BeerPongSession(
                intoxication,
                lastDrink,
                drinksConsumed);
        }
    }
}
