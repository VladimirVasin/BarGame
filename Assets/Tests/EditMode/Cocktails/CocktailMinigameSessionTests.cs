using System;
using System.Linq;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class CocktailMinigameSessionTests
    {
        [TestCase(1, 0, 10)]
        [TestCase(2, 0, 25)]
        [TestCase(3, 0, 45)]
        [TestCase(4, 0, 70)]
        [TestCase(5, 0, 100)]
        [TestCase(3, 1, 30)]
        [TestCase(2, 1, 10)]
        [TestCase(1, 2, 0)]
        public void CalculateScore_UsesGoodCountTableAndBadPenalty(
            int goodCount,
            int badCount,
            int expected)
        {
            Assert.That(
                CocktailMinigameSession.CalculateScore(
                    goodCount,
                    badCount),
                Is.EqualTo(expected));
        }

        [Test]
        public void Constructor_PreservesSessionContextAndDrinkVariant()
        {
            var session = new CocktailMinigameSession(
                -77,
                "bar-persist",
                34,
                DrinkId.PepperVodka,
                12);

            Assert.That(session.CitySeed, Is.EqualTo(-77));
            Assert.That(session.BarId, Is.EqualTo("bar-persist"));
            Assert.That(session.Intoxication, Is.EqualTo(34));
            Assert.That(
                session.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.PepperVodka));
            Assert.That(session.CocktailsConsumed, Is.EqualTo(12));
            Assert.That(session.CurrentRoundNumber, Is.EqualTo(1));
            Assert.That(
                session.Phase,
                Is.EqualTo(CocktailRoundPhase.AwaitingBase));
        }

        [Test]
        public void Additions_DoNotApplyIntoxicationUntilCocktailIsServed()
        {
            CocktailMinigameSession session = CreateSession(7);
            CocktailIngredientId[] offer =
                session.BeginRound(CocktailBaseId.Vodka);
            CocktailIngredientId[] compatible = CompatibleOffers(
                CocktailBaseId.Vodka,
                offer);

            CocktailIngredientSelectionResult first =
                session.AddIngredient(compatible[0]);
            CocktailIngredientSelectionResult second =
                session.AddIngredient(compatible[1]);

            Assert.That(first.CanServe, Is.False);
            Assert.That(second.CanServe, Is.True);
            Assert.That(second.CurrentScore, Is.EqualTo(45));
            Assert.That(session.Intoxication, Is.EqualTo(7));
            Assert.That(session.HasPendingWastedDebuff, Is.False);

            CocktailRoundResult result = session.Serve();

            Assert.That(result.Score, Is.EqualTo(45));
            Assert.That(result.AlcoholIntoxicationGain, Is.EqualTo(18));
            Assert.That(result.BadMixIntoxicationPenalty, Is.Zero);
            Assert.That(result.CurrentIntoxication, Is.EqualTo(25));
            Assert.That(result.LastAlcoholicDrink, Is.EqualTo(DrinkId.Vodka));
            Assert.That(session.CocktailsConsumed, Is.EqualTo(1));
        }

        [Test]
        public void FourCompatibleAdditions_ScorePerfectHundred()
        {
            CocktailMinigameSession session = CreateSession();
            CocktailIngredientId[] offer =
                session.BeginRound(CocktailBaseId.Cognac);
            CocktailIngredientId[] compatible = CompatibleOffers(
                CocktailBaseId.Cognac,
                offer);

            foreach (CocktailIngredientId ingredient in compatible)
            {
                session.AddIngredient(ingredient);
            }

            Assert.That(session.MustServe, Is.True);
            CocktailRoundResult result = session.Serve();

            Assert.That(result.GoodIngredientCount, Is.EqualTo(5));
            Assert.That(result.BadIngredientCount, Is.Zero);
            Assert.That(result.Score, Is.EqualTo(100));
            Assert.That(result.Ingredients, Has.Count.EqualTo(5));
        }

        [Test]
        public void BadIngredient_IsDeferredUntilServeAndDoesNotPoisonGoodOnes()
        {
            CocktailMinigameSession session = CreateSession();
            CocktailIngredientId[] offer =
                session.BeginRound(CocktailBaseId.Wine);
            CocktailIngredientId good = offer.First(candidate =>
                CocktailRules.AreCompatible(
                    CocktailBaseId.Wine,
                    candidate));
            CocktailIngredientId trap = offer.First(candidate =>
                !CocktailRules.AreCompatible(
                    CocktailBaseId.Wine,
                    candidate));

            CocktailIngredientSelectionResult badSelection =
                session.AddIngredient(trap);
            CocktailIngredientSelectionResult goodSelection =
                session.AddIngredient(good);

            Assert.That(badSelection.WasCompatible, Is.False);
            Assert.That(goodSelection.WasCompatible, Is.True);
            Assert.That(session.HasPendingWastedDebuff, Is.False);
            Assert.That(session.Intoxication, Is.Zero);

            CocktailRoundResult result = session.Serve();
            int expectedAlcohol = result.Ingredients
                .Select(CocktailRules.GetDefinition)
                .Sum(definition => definition.IntoxicationGain);

            Assert.That(result.GoodIngredientCount, Is.EqualTo(2));
            Assert.That(result.BadIngredientCount, Is.EqualTo(1));
            Assert.That(result.Score, Is.EqualTo(10));
            Assert.That(
                result.BadMixIntoxicationPenalty,
                Is.EqualTo(10));
            Assert.That(
                result.CurrentIntoxication,
                Is.EqualTo(expectedAlcohol + 10));
            Assert.That(result.RequiresWastedDebuff, Is.True);
            Assert.That(session.HasPendingWastedDebuff, Is.True);
            Assert.That(
                session.Outcome,
                Is.EqualTo(CocktailSessionOutcome.InProgress));
        }

        [Test]
        public void ExactlyThreePerfectCocktails_CompleteWithThreeHundredPoints()
        {
            CocktailMinigameSession session = CreateSession();
            for (int round = 1;
                 round <= CocktailMinigameSession.RoundLimit;
                 round++)
            {
                CocktailIngredientId[] offer =
                    session.BeginRound(CocktailBaseId.Beer);
                foreach (CocktailIngredientId ingredient
                         in CompatibleOffers(CocktailBaseId.Beer, offer))
                {
                    session.AddIngredient(ingredient);
                }

                CocktailRoundResult result = session.Serve();
                Assert.That(result.RoundNumber, Is.EqualTo(round));
            }

            Assert.That(session.RoundsCompleted, Is.EqualTo(3));
            Assert.That(session.CocktailsConsumed, Is.EqualTo(3));
            Assert.That(session.TotalScore, Is.EqualTo(300));
            Assert.That(
                session.Outcome,
                Is.EqualTo(CocktailSessionOutcome.Completed));
            Assert.That(session.IsFinished, Is.True);
            Assert.That(
                session.Phase,
                Is.EqualTo(CocktailRoundPhase.Finished));
            Assert.That(session.HasPendingWastedDebuff, Is.False);
        }

        [Test]
        public void ReachingMaximumIntoxication_EndsEarlyAsWasted()
        {
            CocktailMinigameSession session = CreateSession(90);
            CocktailIngredientId[] offer =
                session.BeginRound(CocktailBaseId.Beer);
            CocktailIngredientId good = offer.First(candidate =>
                CocktailRules.AreCompatible(
                    CocktailBaseId.Beer,
                    candidate));
            CocktailIngredientId trap = offer.First(candidate =>
                !CocktailRules.AreCompatible(
                    CocktailBaseId.Beer,
                    candidate));
            session.AddIngredient(good);
            session.AddIngredient(trap);

            CocktailRoundResult result = session.Serve();

            Assert.That(result.CurrentIntoxication, Is.EqualTo(100));
            Assert.That(
                result.SessionOutcome,
                Is.EqualTo(CocktailSessionOutcome.Wasted));
            Assert.That(session.RoundsCompleted, Is.EqualTo(1));
            Assert.That(session.IsFinished, Is.True);
            Assert.That(session.HasPendingWastedDebuff, Is.True);
        }

        [Test]
        public void Round_RequiresTwoAndAcceptsAtMostFourUniqueAdditions()
        {
            CocktailMinigameSession session = CreateSession();
            CocktailIngredientId[] offer =
                session.BeginRound(CocktailBaseId.Vodka);

            session.AddIngredient(offer[0]);
            Assert.Throws<InvalidOperationException>(() => session.Serve());
            Assert.Throws<InvalidOperationException>(
                () => session.AddIngredient(offer[0]));

            session.AddIngredient(offer[1]);
            session.AddIngredient(offer[2]);
            session.AddIngredient(offer[3]);
            Assert.That(session.MustServe, Is.True);
            Assert.Throws<InvalidOperationException>(
                () => session.AddIngredient(offer[4]));
        }

        [Test]
        public void Round_RejectsIngredientOutsideDeterministicOffer()
        {
            CocktailMinigameSession session = CreateSession();
            CocktailIngredientId[] offer =
                session.BeginRound(CocktailBaseId.Beer);
            CocktailIngredientId missing = CocktailRules.Definitions
                .Select(definition => definition.Id)
                .First(candidate => !offer.Contains(candidate));

            Assert.Throws<ArgumentException>(
                () => session.AddIngredient(missing));
        }

        [Test]
        public void StartingAtMaximum_IsImmediatelyWasted()
        {
            CocktailMinigameSession session = CreateSession(100);

            Assert.That(session.IsFinished, Is.True);
            Assert.That(
                session.Outcome,
                Is.EqualTo(CocktailSessionOutcome.Wasted));
            Assert.That(session.HasPendingWastedDebuff, Is.True);
            Assert.That(session.GetCurrentOffers(), Is.Empty);
            Assert.Throws<InvalidOperationException>(
                () => session.BeginRound(CocktailBaseId.Beer));
        }

        private static CocktailMinigameSession CreateSession(
            int initialIntoxication = 0)
        {
            return new CocktailMinigameSession(
                20260727,
                "bar-test",
                initialIntoxication,
                DrinkId.None,
                0);
        }

        private static CocktailIngredientId[] CompatibleOffers(
            CocktailBaseId baseId,
            CocktailIngredientId[] offer)
        {
            return offer.Where(candidate =>
                    CocktailRules.AreCompatible(baseId, candidate))
                .ToArray();
        }
    }
}
