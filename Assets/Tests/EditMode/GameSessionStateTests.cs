using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameSessionStateTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetPublicState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetPublicState();
        }

        [Test]
        public void EnterAndReturnLifecycle_PreservesSeedAndSelectedBar()
        {
            const int seed = -481516;
            const string barId = "bar-contract-test";

            GameSessionState.SetCitySeed(seed);
            GameSessionState.EnterBar(barId, BarActivityKind.BeerPong);

            Assert.That(GameSessionState.CitySeed, Is.EqualTo(seed));
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(barId));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            Assert.That(
                GameSessionState.TryGetReturnBarId(out _),
                Is.False,
                "Entering a bar must not look like a pending return to the city.");

            GameSessionState.PrepareCityReturn();

            Assert.That(GameSessionState.IsReturningToCity, Is.True);
            Assert.That(GameSessionState.TryGetReturnBarId(out string returnedBarId), Is.True);
            Assert.That(returnedBarId, Is.EqualTo(barId));
            Assert.That(GameSessionState.CitySeed, Is.EqualTo(seed));

            GameSessionState.CompleteCityReturn();

            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            Assert.That(GameSessionState.TryGetReturnBarId(out _), Is.False);
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(barId));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(GameSessionState.CitySeed, Is.EqualTo(seed));
        }

        [Test]
        public void EnteringAnotherBar_CancelsAnEarlierPendingReturn()
        {
            GameSessionState.EnterBar(
                "bar-first",
                BarActivityKind.BeerPong);
            GameSessionState.PrepareCityReturn();
            Assert.That(GameSessionState.IsReturningToCity, Is.True);

            GameSessionState.EnterBar("bar-second");

            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo("bar-second"));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.Cocktail),
                "The legacy overload must safely preserve cocktail behavior.");
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            Assert.That(GameSessionState.TryGetReturnBarId(out _), Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        public void MissingBarId_CannotCreateReturnDestination(string barId)
        {
            GameSessionState.EnterBar(barId);
            GameSessionState.PrepareCityReturn();

            Assert.That(GameSessionState.ActiveBarId, Is.Empty);
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.None));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            Assert.That(GameSessionState.TryGetReturnBarId(out string returnedBarId), Is.False);
            Assert.That(returnedBarId, Is.Empty);
        }

        [TestCase(BarActivityKind.None)]
        [TestCase((BarActivityKind)999)]
        public void EnterBar_UnsupportedActivityFallsBackToCocktail(
            BarActivityKind activity)
        {
            GameSessionState.EnterBar("bar-fallback", activity);

            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo("bar-fallback"));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
        }

        [TestCase(-25, 0, -4, 0)]
        [TestCase(48, 48, 7, 7)]
        [TestCase(140, 100, 12, 12)]
        public void UpdateDrinkingProgress_ClampsPublicValues(
            int intoxication,
            int expectedIntoxication,
            int drinksConsumed,
            int expectedDrinksConsumed)
        {
            const DrinkId drink = DrinkId.RedWine;

            GameSessionState.UpdateDrinkingProgress(
                intoxication,
                drink,
                drinksConsumed);

            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(expectedIntoxication));
            Assert.That(GameSessionState.LastAlcoholicDrink, Is.EqualTo(drink));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(expectedDrinksConsumed));
        }

        [Test]
        public void BarReturnLifecycle_PreservesDrinkingProgress()
        {
            const DrinkId drink = DrinkId.CognacVs;
            GameSessionState.UpdateDrinkingProgress(63, drink, 4);
            GameSessionState.ApplyWasted(18f);

            GameSessionState.EnterBar("bar-drinking-state");
            GameSessionState.PrepareCityReturn();
            GameSessionState.CompleteCityReturn();

            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(63));
            Assert.That(GameSessionState.LastAlcoholicDrink, Is.EqualTo(drink));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(4));
            Assert.That(GameSessionState.WastedSecondsRemaining, Is.EqualTo(18f));
            Assert.That(GameSessionState.IsWasted, Is.True);
        }

        [Test]
        public void WastedTimer_ApplicationAdvanceAndExpiry_AreClamped()
        {
            GameSessionState.ApplyWasted(-3f);
            Assert.That(GameSessionState.IsWasted, Is.False);

            GameSessionState.ApplyWasted(12f);
            GameSessionState.ApplyWasted(5f);
            Assert.That(GameSessionState.WastedSecondsRemaining, Is.EqualTo(12f));
            Assert.That(GameSessionState.IsWasted, Is.True);

            GameSessionState.AdvanceWasted(-2f);
            Assert.That(GameSessionState.WastedSecondsRemaining, Is.EqualTo(12f));

            GameSessionState.AdvanceWasted(4.5f);
            Assert.That(GameSessionState.WastedSecondsRemaining, Is.EqualTo(7.5f));

            GameSessionState.AdvanceWasted(20f);
            Assert.That(GameSessionState.WastedSecondsRemaining, Is.Zero);
            Assert.That(GameSessionState.IsWasted, Is.False);
        }

        [Test]
        public void ResetDrinkingState_ClearsOnlyDrinkingProgress()
        {
            GameSessionState.SetCitySeed(9876);
            GameSessionState.EnterBar("bar-reset-contract");
            GameSessionState.UpdateDrinkingProgress(84, DrinkId.Vodka, 6);
            GameSessionState.ApplyWasted(30f);

            GameSessionState.ResetDrinkingState();

            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            Assert.That(GameSessionState.LastAlcoholicDrink, Is.EqualTo(DrinkId.None));
            Assert.That(GameSessionState.DrinksConsumed, Is.Zero);
            Assert.That(GameSessionState.WastedSecondsRemaining, Is.Zero);
            Assert.That(GameSessionState.IsWasted, Is.False);
            Assert.That(GameSessionState.CitySeed, Is.EqualTo(9876));
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo("bar-reset-contract"));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
        }

        [Test]
        public void TryAddRouteStop_PreservesOrderAndRejectsInvalidOrDuplicateIds()
        {
            Assert.That(GameSessionState.TryAddRouteStop(null), Is.False);
            Assert.That(GameSessionState.TryAddRouteStop(string.Empty), Is.False);
            Assert.That(GameSessionState.TryAddRouteStop("   "), Is.False);
            Assert.That(GameSessionState.TryAddRouteStop("bar-a"), Is.True);
            Assert.That(GameSessionState.TryAddRouteStop("bar-b"), Is.True);
            Assert.That(GameSessionState.TryAddRouteStop("bar-a"), Is.False);
            Assert.That(GameSessionState.TryAddRouteStop("bar-c"), Is.True);

            CollectionAssert.AreEqual(
                new[] { "bar-a", "bar-b", "bar-c" },
                GameSessionState.PlannedBarRoute);
        }

        [Test]
        public void RemoveAndClearRoute_UpdateOnlyThePlannedStops()
        {
            GameSessionState.TryAddRouteStop("bar-a");
            GameSessionState.TryAddRouteStop("bar-b");
            GameSessionState.TryAddRouteStop("bar-c");

            Assert.That(GameSessionState.RemoveRouteStop("bar-b"), Is.True);
            Assert.That(GameSessionState.RemoveRouteStop("bar-missing"), Is.False);
            CollectionAssert.AreEqual(
                new[] { "bar-a", "bar-c" },
                GameSessionState.PlannedBarRoute);

            GameSessionState.ClearRoute();

            Assert.That(GameSessionState.PlannedBarRoute, Is.Empty);
        }

        [Test]
        public void MoveRouteStop_MovesOnePositionAndRespectsBoundaries()
        {
            GameSessionState.TryAddRouteStop("bar-a");
            GameSessionState.TryAddRouteStop("bar-b");
            GameSessionState.TryAddRouteStop("bar-c");

            Assert.That(GameSessionState.MoveRouteStop("bar-b", -1), Is.True);
            CollectionAssert.AreEqual(
                new[] { "bar-b", "bar-a", "bar-c" },
                GameSessionState.PlannedBarRoute);
            Assert.That(GameSessionState.MoveRouteStop("bar-b", -1), Is.False);
            Assert.That(GameSessionState.MoveRouteStop("bar-c", 1), Is.False);
            Assert.That(GameSessionState.MoveRouteStop("bar-a", 0), Is.False);
            Assert.That(GameSessionState.MoveRouteStop("bar-missing", 1), Is.False);

            Assert.That(GameSessionState.MoveRouteStop("bar-a", 1), Is.True);
            CollectionAssert.AreEqual(
                new[] { "bar-b", "bar-c", "bar-a" },
                GameSessionState.PlannedBarRoute);
        }

        [Test]
        public void MarkBarVisited_PersistsVisitAndRemovesMatchingRouteStop()
        {
            GameSessionState.TryAddRouteStop("bar-a");
            GameSessionState.TryAddRouteStop("bar-b");
            GameSessionState.TryAddRouteStop("bar-c");

            Assert.That(GameSessionState.IsBarVisited("bar-b"), Is.False);
            Assert.That(GameSessionState.MarkBarVisited("bar-b"), Is.True);
            Assert.That(GameSessionState.IsBarVisited("bar-b"), Is.True);
            Assert.That(GameSessionState.VisitedBarCount, Is.EqualTo(1));
            Assert.That(
                GameSessionState.MarkBarVisited("bar-b"),
                Is.False,
                "Repeated completion must remain idempotent.");
            CollectionAssert.AreEqual(
                new[] { "bar-a", "bar-c" },
                GameSessionState.PlannedBarRoute);

            Assert.That(GameSessionState.MarkBarVisited(null), Is.False);
            Assert.That(GameSessionState.MarkBarVisited(string.Empty), Is.False);
            Assert.That(GameSessionState.MarkBarVisited("   "), Is.False);
            Assert.That(GameSessionState.IsBarVisited("   "), Is.False);
            Assert.That(GameSessionState.VisitedBarCount, Is.EqualTo(1));
        }

        [Test]
        public void SetCitySeed_ClearsRouteAndVisitsOnlyWhenSeedChanges()
        {
            const int seed = 8877;
            GameSessionState.SetCitySeed(seed);
            GameSessionState.TryAddRouteStop("bar-a");
            GameSessionState.TryAddRouteStop("bar-b");
            GameSessionState.MarkBarVisited("bar-visited");

            GameSessionState.SetCitySeed(seed);

            CollectionAssert.AreEqual(
                new[] { "bar-a", "bar-b" },
                GameSessionState.PlannedBarRoute);
            Assert.That(
                GameSessionState.IsBarVisited("bar-visited"),
                Is.True);

            GameSessionState.SetCitySeed(seed + 1);

            Assert.That(GameSessionState.PlannedBarRoute, Is.Empty);
            Assert.That(GameSessionState.VisitedBarCount, Is.Zero);
            Assert.That(
                GameSessionState.IsBarVisited("bar-visited"),
                Is.False);
        }

        private static void ResetPublicState()
        {
            GameSessionState.SetCitySeed(GameSessionState.DefaultCitySeed);
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
