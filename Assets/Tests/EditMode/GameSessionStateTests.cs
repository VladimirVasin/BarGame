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

        [Test]
        public void HomeReturn_PreservesRunStateAndUsesHomeDestination()
        {
            GameSessionState.EnterBar(
                "bar-before-home",
                BarActivityKind.BeerPong);
            GameSessionState.TryAddRouteStop("bar-route");
            GameSessionState.UpdateDrinkingProgress(
                37,
                DrinkId.RedWine,
                2);

            GameSessionState.EnterHome();

            Assert.That(GameSessionState.ActiveBarId, Is.Empty);
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.None));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            CollectionAssert.AreEqual(
                new[] { "bar-route" },
                GameSessionState.PlannedBarRoute);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(37));

            GameSessionState.PrepareHomeReturn();

            Assert.That(GameSessionState.IsReturningToCity, Is.True);
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.PlayerHome));
            Assert.That(
                GameSessionState.TryGetReturnBarId(out _),
                Is.False);
            Assert.That(
                GameSessionState.TryGetCityReturnKind(
                    out CityReturnKind returnKind),
                Is.True);
            Assert.That(
                returnKind,
                Is.EqualTo(CityReturnKind.PlayerHome));

            GameSessionState.CompleteCityReturn();

            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(37));
        }

        [Test]
        public void StairwellArrival_IsConsumedAndResetsToStreetDoor()
        {
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.ApartmentDoor);

            Assert.That(
                GameSessionState.StairwellArrival,
                Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
            Assert.That(
                GameSessionState.ConsumeStairwellArrival(),
                Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
            Assert.That(
                GameSessionState.StairwellArrival,
                Is.EqualTo(StairwellArrivalKind.StreetDoor));
            Assert.That(
                GameSessionState.ConsumeStairwellArrival(),
                Is.EqualTo(StairwellArrivalKind.StreetDoor));
        }

        [Test]
        public void StairwellArrival_RejectsUnknownValue()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => GameSessionState.PrepareStairwellArrival(
                    (StairwellArrivalKind)99));
        }

        [Test]
        public void HomeArrival_IsConsumedAndResetsToNormal()
        {
            Assert.That(
                GameSessionState.HomeArrival,
                Is.EqualTo(HomeArrivalKind.Normal));
            Assert.That(
                GameSessionState.ConsumeHomeArrival(),
                Is.EqualTo(HomeArrivalKind.Normal));

            GameSessionState.PrepareHomeArrival(
                HomeArrivalKind.OpeningSleep);

            Assert.That(
                GameSessionState.HomeArrival,
                Is.EqualTo(HomeArrivalKind.OpeningSleep));
            Assert.That(
                GameSessionState.ConsumeHomeArrival(),
                Is.EqualTo(HomeArrivalKind.OpeningSleep));
            Assert.That(
                GameSessionState.HomeArrival,
                Is.EqualTo(HomeArrivalKind.Normal));
            Assert.That(
                GameSessionState.ConsumeHomeArrival(),
                Is.EqualTo(HomeArrivalKind.Normal));
        }

        [Test]
        public void HomeArrival_RejectsUnknownValue()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => GameSessionState.PrepareHomeArrival(
                    (HomeArrivalKind)99));
        }

        [Test]
        public void BeginNewGame_RestoresEverySessionContract()
        {
            GameSessionState.SetCitySeed(-7001);
            GameSessionState.SetCityBlueprint(
                CityBlueprintCatalog.LegacyBlueprintId);
            GameSessionState.TryAddRouteStop("bar-route");
            GameSessionState.TryAddRouteStop("bar-second");
            GameSessionState.EnterBar(
                "bar-active",
                BarActivityKind.BeerPong);
            GameSessionState.PrepareCityReturn();
            GameSessionState.PrepareStairwellArrival(
                StairwellArrivalKind.ApartmentDoor);
            GameSessionState.PrepareHomeArrival(
                HomeArrivalKind.OpeningSleep);
            GameSessionState.UpdateDrinkingProgress(
                67,
                DrinkId.CognacVsop,
                4);
            GameSessionState.UpdateFatigue(64);
            Assert.That(
                GameSessionState.TryPurchaseDrink(
                    DrinkId.Water).Succeeded,
                Is.True);
            GameSessionState.SetBalanceCheckDelay(14f);
            GameSessionState.ConsumeBalanceCheckSequence();
            Assert.That(
                GameSessionState.TryCollectWorldItem(
                    "home.refrigerator.test",
                    InventoryItemId.ChickenEgg),
                Is.True);

            GameSessionState.BeginNewGame();

            Assert.That(
                GameSessionState.CitySeed,
                Is.EqualTo(GameSessionState.DefaultCitySeed));
            Assert.That(
                GameSessionState.CityBlueprintId,
                Is.EqualTo(
                    GameSessionState.DefaultCityBlueprintId));
            Assert.That(GameSessionState.ActiveBarId, Is.Empty);
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.None));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));
            Assert.That(
                GameSessionState.IsReturningToCity,
                Is.False);
            Assert.That(
                GameSessionState.StairwellArrival,
                Is.EqualTo(StairwellArrivalKind.StreetDoor));
            Assert.That(
                GameSessionState.HomeArrival,
                Is.EqualTo(HomeArrivalKind.Normal));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.Zero);
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.None));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.Zero);
            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(GameSessionState.DefaultFatigue));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(GameSessionState.DefaultCash));
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.Zero);
            Assert.That(
                GameSessionState.BalanceCheckSequence,
                Is.Zero);
            Assert.That(
                GameSessionState.PlannedBarRoute,
                Is.Empty);
            Assert.That(
                GameSessionState.InventoryItems,
                Has.Count.EqualTo(2));
            Assert.That(
                GameSessionState.InventoryItems[0].ItemId,
                Is.EqualTo(InventoryItemId.ApartmentKeys));
            Assert.That(
                GameSessionState.InventoryItems[1].ItemId,
                Is.EqualTo(InventoryItemId.Lighter));
            Assert.That(
                GameSessionState.CollectedWorldItemCount,
                Is.Zero);
            Assert.That(
                GameSessionState.IsWorldItemCollected(
                    "home.refrigerator.test"),
                Is.False);
        }

        [Test]
        public void CollectWorldItem_IsAtomicAndRejectsDuplicateSource()
        {
            const string sourceId =
                "home.refrigerator.shelf-middle-left";

            Assert.That(
                GameSessionState.TryCollectWorldItem(
                    sourceId,
                    InventoryItemId.ChickenEgg),
                Is.True);
            Assert.That(
                GameSessionState.TryCollectWorldItem(
                    sourceId,
                    InventoryItemId.OpenStewCan),
                Is.False);

            Assert.That(
                GameSessionState.IsWorldItemCollected(sourceId),
                Is.True);
            Assert.That(
                GameSessionState.InventoryItems,
                Has.Count.EqualTo(3));
            Assert.That(
                GameSessionState.InventoryItems[2].ItemId,
                Is.EqualTo(InventoryItemId.ChickenEgg));
            Assert.That(GameSessionState.CollectedWorldItemCount, Is.EqualTo(1));
        }

        [Test]
        public void InventoryQueries_ReportCountsAndRejectNonPositiveNeeds()
        {
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.Zero);
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.False);

            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan,
                    2),
                Is.True);

            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(2));
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.OpenStewCan,
                    2),
                Is.True);
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.OpenStewCan,
                    3),
                Is.False);
            Assert.That(
                GameSessionState.HasInventoryItem(
                    InventoryItemId.OpenStewCan,
                    0),
                Is.False);
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
        public void NewGame_StartsHungerStressAndFatigueAtZero()
        {
            GameSessionState.UpdateNeeds(73, 81);
            GameSessionState.UpdateFatigue(59);

            GameSessionState.BeginNewGame();

            Assert.That(GameSessionState.DefaultHunger, Is.Zero);
            Assert.That(GameSessionState.DefaultStress, Is.Zero);
            Assert.That(GameSessionState.DefaultFatigue, Is.Zero);
            Assert.That(GameSessionState.HungerLevel, Is.Zero);
            Assert.That(GameSessionState.StressLevel, Is.Zero);
            Assert.That(GameSessionState.FatigueLevel, Is.Zero);
        }

        [TestCase(-5, 0, 130, 100)]
        [TestCase(44, 44, 67, 67)]
        public void UpdateNeeds_ClampsPublicValues(
            int hunger,
            int expectedHunger,
            int stress,
            int expectedStress)
        {
            GameSessionState.UpdateNeeds(hunger, stress);

            Assert.That(
                GameSessionState.HungerLevel,
                Is.EqualTo(expectedHunger));
            Assert.That(
                GameSessionState.StressLevel,
                Is.EqualTo(expectedStress));
        }

        [TestCase(-5, 0)]
        [TestCase(44, 44)]
        [TestCase(130, 100)]
        public void UpdateFatigue_ClampsPublicValue(
            int fatigue,
            int expectedFatigue)
        {
            GameSessionState.UpdateFatigue(fatigue);

            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(expectedFatigue));
        }

        [Test]
        public void ResetFatigueAfterSleep_ClearsLevelAndFractionalProgress()
        {
            GameSessionState.UpdateFatigue(73);
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(5.5f);
            Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(73));

            GameSessionState.ResetFatigueAfterSleep();

            Assert.That(
                GameSessionState.FatigueLevel,
                Is.EqualTo(GameSessionState.DefaultFatigue));

            GameSessionState.AdvanceGameTime(5.5f);
            Assert.That(GameSessionState.FatigueLevel, Is.Zero);

            GameSessionState.AdvanceGameTime(5.5f);
            Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceGameTime_ProgressesNeedsOnlyAfterWake()
        {
            GameSessionState.AdvanceGameTime(720f);

            Assert.That(GameSessionState.HungerLevel, Is.Zero);
            Assert.That(GameSessionState.FatigueLevel, Is.Zero);

            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(360f);

            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(25));
            Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(33));
        }

        [Test]
        public void BeginNewGame_ClearsFractionalNeedsProgress()
        {
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(7.3f);
            Assert.That(GameSessionState.HungerLevel, Is.Zero);
            Assert.That(GameSessionState.FatigueLevel, Is.Zero);

            GameSessionState.BeginNewGame();
            GameSessionState.AdvanceGameTime(720f);
            Assert.That(GameSessionState.HungerLevel, Is.Zero);
            Assert.That(GameSessionState.FatigueLevel, Is.Zero);

            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(7.3f);

            Assert.That(GameSessionState.HungerLevel, Is.Zero);
            Assert.That(GameSessionState.FatigueLevel, Is.Zero);

            GameSessionState.AdvanceGameTime(7.2f);

            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(1));
            Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(1));
        }

        [Test]
        public void FoodUse_ClearsFractionalHungerProgress()
        {
            GameSessionState.UpdateNeeds(60, 0);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan),
                Is.True);
            Assert.That(
                GameSessionState.TryStartGameTimeFromWake(),
                Is.True);
            GameSessionState.AdvanceGameTime(7.3f);
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(60));

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.OpenStewCan);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(25));

            GameSessionState.AdvanceGameTime(7.3f);
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(25));

            GameSessionState.AdvanceGameTime(7.2f);
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(26));
        }

        [Test]
        public void CheapFoodUse_StopsAtFloorAndKeepsUnusedItem()
        {
            GameSessionState.UpdateNeeds(60, 40);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.OpenStewCan,
                    3),
                Is.True);

            InventoryItemUseResult first =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.OpenStewCan);
            InventoryItemUseResult second =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.OpenStewCan);
            InventoryItemUseResult blocked =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.OpenStewCan);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.ActualHungerRelief, Is.EqualTo(35));
            Assert.That(second.Succeeded, Is.True);
            Assert.That(second.ActualHungerRelief, Is.EqualTo(5));
            Assert.That(blocked.Succeeded, Is.False);
            Assert.That(
                blocked.Status,
                Is.EqualTo(InventoryItemUseStatus.NoEffect));
            Assert.That(GameSessionState.HungerLevel, Is.EqualTo(20));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(40));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.OpenStewCan),
                Is.EqualTo(1));
        }

        [Test]
        public void VodkaBottleUse_CommitsFourServingsAtomically()
        {
            GameSessionState.UpdateNeeds(0, 60);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.VodkaBottle),
                Is.True);

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.VodkaBottle);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ActualStressRelief, Is.EqualTo(48));
            Assert.That(result.ActualIntoxicationGain, Is.EqualTo(72));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(12));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(72));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.Vodka));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(4));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.VodkaBottle),
                Is.Zero);
        }

        [Test]
        public void VodkaBottleAtMaximumIntoxication_MutatesNothing()
        {
            GameSessionState.UpdateNeeds(0, 60);
            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.RedWine,
                2);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.VodkaBottle),
                Is.True);

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.VodkaBottle);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.Status,
                Is.EqualTo(
                    InventoryItemUseStatus.MaximumIntoxication));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(60));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(2));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.VodkaBottle),
                Is.EqualTo(1));
        }

        [Test]
        public void DuplicateDrinkCommit_DoesNotRelieveStressTwice()
        {
            GameSessionState.UpdateNeeds(0, 60);

            GameSessionState.CommitDrinkingProgress(
                8,
                DrinkId.LightBeer,
                1,
                DrinkRules.GetStressRelief(DrinkId.LightBeer));
            GameSessionState.CommitDrinkingProgress(
                80,
                DrinkId.Vodka,
                1,
                DrinkRules.GetStressRelief(DrinkId.Vodka));
            GameSessionState.CommitDrinkingProgress(
                0,
                DrinkId.None,
                0,
                DrinkRules.GetStressRelief(DrinkId.CognacVsop));

            Assert.That(GameSessionState.StressLevel, Is.EqualTo(54));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(8));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(1));
        }

        [Test]
        public void VodkaBottleAtSaturatedDrinkCount_CommitsOtherEffects()
        {
            GameSessionState.UpdateNeeds(0, 60);
            GameSessionState.UpdateDrinkingProgress(
                0,
                DrinkId.None,
                int.MaxValue);
            Assert.That(
                GameSessionState.TryAddInventoryItem(
                    InventoryItemId.VodkaBottle),
                Is.True);

            InventoryItemUseResult result =
                GameSessionState.TryConsumeInventoryItem(
                    InventoryItemId.VodkaBottle);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(12));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(72));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.Vodka));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(int.MaxValue));
            Assert.That(
                GameSessionState.GetInventoryItemCount(
                    InventoryItemId.VodkaBottle),
                Is.Zero);
        }

        [Test]
        public void BarReturnLifecycle_PreservesDrinkingProgress()
        {
            const DrinkId drink = DrinkId.CognacVs;
            GameSessionState.UpdateDrinkingProgress(63, drink, 4);
            DrinkPurchaseResult purchase =
                GameSessionState.TryPurchaseDrink(DrinkId.Water);
            GameSessionState.SetBalanceCheckDelay(18f);
            Assert.That(
                GameSessionState.ConsumeBalanceCheckSequence(),
                Is.Zero);

            GameSessionState.EnterBar("bar-drinking-state");
            GameSessionState.PrepareCityReturn();
            GameSessionState.CompleteCityReturn();

            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(63));
            Assert.That(GameSessionState.LastAlcoholicDrink, Is.EqualTo(drink));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(5));
            Assert.That(purchase.Succeeded, Is.True);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(GameSessionState.DefaultCash - 2));
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.EqualTo(18f));
            Assert.That(GameSessionState.BalanceCheckSequence, Is.EqualTo(1));
        }

        [Test]
        public void BalanceCheckSchedule_DelayAndSequence_AreClamped()
        {
            GameSessionState.SetBalanceCheckDelay(-3f);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.Zero);

            GameSessionState.SetBalanceCheckDelay(12f);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.EqualTo(12f));

            GameSessionState.AdvanceBalanceCheckDelay(-2f);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.EqualTo(12f));

            GameSessionState.AdvanceBalanceCheckDelay(4.5f);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.EqualTo(7.5f));

            GameSessionState.AdvanceBalanceCheckDelay(20f);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.Zero);

            Assert.That(
                GameSessionState.ConsumeBalanceCheckSequence(),
                Is.Zero);
            Assert.That(
                GameSessionState.ConsumeBalanceCheckSequence(),
                Is.EqualTo(1));
            Assert.That(GameSessionState.BalanceCheckSequence, Is.EqualTo(2));
        }

        [Test]
        public void ResetDrinkingState_ClearsOnlyDrinkingProgress()
        {
            GameSessionState.SetCitySeed(9876);
            GameSessionState.EnterBar("bar-reset-contract");
            GameSessionState.UpdateDrinkingProgress(84, DrinkId.Vodka, 6);
            Assert.That(
                GameSessionState.TryPurchaseDrink(DrinkId.Water).Succeeded,
                Is.True);
            int cashBeforeReset = GameSessionState.CashBalance;
            GameSessionState.SetBalanceCheckDelay(30f);
            GameSessionState.ConsumeBalanceCheckSequence();

            GameSessionState.ResetDrinkingState();

            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            Assert.That(GameSessionState.LastAlcoholicDrink, Is.EqualTo(DrinkId.None));
            Assert.That(GameSessionState.DrinksConsumed, Is.Zero);
            Assert.That(
                GameSessionState.BalanceCheckDelayRemaining,
                Is.Zero);
            Assert.That(GameSessionState.BalanceCheckSequence, Is.Zero);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBeforeReset));
            Assert.That(GameSessionState.CitySeed, Is.EqualTo(9876));
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo("bar-reset-contract"));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
        }

        [Test]
        public void TryPurchaseDrink_CommitsSuccessfulResultAtomically()
        {
            GameSessionState.UpdateDrinkingProgress(
                91,
                DrinkId.RedWine,
                2);
            GameSessionState.UpdateNeeds(0, 20);

            DrinkPurchaseResult result =
                GameSessionState.TryPurchaseDrink(
                    DrinkId.LightBeer);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CashBefore, Is.EqualTo(999));
            Assert.That(result.CashAfter, Is.EqualTo(991));
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(result.CashAfter));
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(99));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(3));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(14));

            DrinkPurchaseResult clamped =
                GameSessionState.TryPurchaseDrink(
                    DrinkId.DarkBeer);

            Assert.That(clamped.Succeeded, Is.True);
            Assert.That(clamped.ActualIntoxicationDelta, Is.EqualTo(1));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(981));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.DarkBeer));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(4));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(6));
        }

        [Test]
        public void IntoxicationRecovery_ConsumesTimeAndStopsAtSober()
        {
            GameSessionState.UpdateDrinkingProgress(
                2,
                DrinkId.LightBeer,
                1);
            float firstInterval =
                IntoxicationStageRules.GetRecoverySecondsPerPoint(2);

            GameSessionState.AdvanceIntoxicationRecovery(
                firstInterval - 0.01f);
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(2));

            GameSessionState.AdvanceIntoxicationRecovery(0.01f);
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(1));

            GameSessionState.AdvanceIntoxicationRecovery(
                IntoxicationStageRules.GetRecoverySecondsPerPoint(1));
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);

            GameSessionState.AdvanceIntoxicationRecovery(60f);
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(1));
        }

        [Test]
        public void TryPurchaseDrink_FailureDoesNotMutateSession()
        {
            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.Vodka,
                5);
            int cashBefore = GameSessionState.CashBalance;

            DrinkPurchaseResult result =
                GameSessionState.TryPurchaseDrink(
                    DrinkId.LightBeer);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.Status,
                Is.EqualTo(
                    DrinkPurchaseStatus.MaximumIntoxication));
            Assert.That(GameSessionState.CashBalance, Is.EqualTo(cashBefore));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.Vodka));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(5));
        }

        [Test]
        public void WaterAtMaximum_ChargesAndPreservesAlcoholContext()
        {
            GameSessionState.UpdateDrinkingProgress(
                100,
                DrinkId.CognacVsop,
                7);
            GameSessionState.UpdateNeeds(0, 30);

            DrinkPurchaseResult result =
                GameSessionState.TryPurchaseDrink(DrinkId.Water);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(GameSessionState.DefaultCash - 2));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.CognacVsop));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(8));
            Assert.That(GameSessionState.StressLevel, Is.EqualTo(30));
        }

        [Test]
        public void EconomyState_SurvivesSeedTransitionsAndDrinkingReset()
        {
            Assert.That(
                GameSessionState.TryPurchaseDrink(DrinkId.Water).Succeeded,
                Is.True);
            int expectedCash = GameSessionState.DefaultCash - 2;

            GameSessionState.SetCitySeed(776655);
            GameSessionState.EnterBar("bar-economy-contract");
            GameSessionState.PrepareCityReturn();
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();

            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(expectedCash));
        }

        [Test]
        public void ResetEconomyState_RestoresOnlyDefaultCash()
        {
            GameSessionState.SetCitySeed(9988);
            GameSessionState.EnterBar("bar-economy-reset");
            GameSessionState.TryAddRouteStop("bar-economy-reset");
            GameSessionState.UpdateDrinkingProgress(
                42,
                DrinkId.RedWine,
                3);
            Assert.That(
                GameSessionState.TryPurchaseDrink(DrinkId.Water).Succeeded,
                Is.True);

            GameSessionState.ResetEconomyState();

            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(GameSessionState.DefaultCash));
            Assert.That(GameSessionState.CitySeed, Is.EqualTo(9988));
            Assert.That(
                GameSessionState.ActiveBarId,
                Is.EqualTo("bar-economy-reset"));
            Assert.That(
                GameSessionState.PlannedBarRoute,
                Does.Contain("bar-economy-reset"));
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(42));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.RedWine));
            Assert.That(GameSessionState.DrinksConsumed, Is.EqualTo(4));
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
        public void SetCitySeed_ClearsRouteOnlyWhenSeedChanges()
        {
            const int seed = 8877;
            GameSessionState.SetCitySeed(seed);
            GameSessionState.TryAddRouteStop("bar-a");
            GameSessionState.TryAddRouteStop("bar-b");

            GameSessionState.SetCitySeed(seed);

            CollectionAssert.AreEqual(
                new[] { "bar-a", "bar-b" },
                GameSessionState.PlannedBarRoute);

            GameSessionState.SetCitySeed(seed + 1);

            Assert.That(GameSessionState.PlannedBarRoute, Is.Empty);
        }

        [Test]
        public void SetCityBlueprint_ClearsRouteOnlyWhenIdChanges()
        {
            GameSessionState.TryAddRouteStop("bar-a");
            GameSessionState.TryAddRouteStop("bar-b");

            GameSessionState.SetCityBlueprint(
                CityBlueprintCatalog.DefaultBlueprintId);

            CollectionAssert.AreEqual(
                new[] { "bar-a", "bar-b" },
                GameSessionState.PlannedBarRoute);

            GameSessionState.EnterBar(
                "bar-pending-return",
                BarActivityKind.BeerPong);
            GameSessionState.PrepareCityReturn();
            GameSessionState.SetCityBlueprint(
                CityBlueprintCatalog.LegacyBlueprintId);

            Assert.That(
                GameSessionState.CityBlueprintId,
                Is.EqualTo(CityBlueprintCatalog.LegacyBlueprintId));
            Assert.That(GameSessionState.PlannedBarRoute, Is.Empty);
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.Bar));
            Assert.That(
                GameSessionState.ActiveBarId,
                Is.EqualTo("bar-pending-return"));

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                GameSessionState.SetCityBlueprint("missing-blueprint"));
            Assert.That(
                GameSessionState.CityBlueprintId,
                Is.EqualTo(CityBlueprintCatalog.LegacyBlueprintId));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.Bar));
        }

        private static void ResetPublicState()
        {
            GameSessionState.BeginNewGame();
        }
    }
}
