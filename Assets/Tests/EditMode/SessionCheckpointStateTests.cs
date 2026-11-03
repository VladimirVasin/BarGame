using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SessionCheckpointStateTests
    {
        [SetUp] public void SetUp() => GameSessionState.BeginNewGame();
        [TearDown] public void TearDown() => GameSessionState.BeginNewGame();

        [Test]
        public void RepeatedRestore_RewindsDeepProgressAndExactNextSipWithoutRegranting()
        {
            GameSessionState.SetCitySeed(-7013);
            GameSessionState.EnterBar("checkpoint.bar");
            GameSessionState.UpdateNeeds(37, 45);
            GameSessionState.UpdateFatigue(12);
            Assert.That(GameSessionState.TryStartGameTimeAt(8 * 60), Is.True);
            GameSessionState.AdvanceGameTime(41.375f);
            GameSessionState.TrySetDebugHeroCorruptionLevel(3);
            GameSessionState.TryActivateQuest(QuestId.FeedTheCat);
            GameSessionState.TryAdvanceGraveWork("checkpoint.grave", CemeteryGraveWorkStage.Dug);
            GameSessionState.VillageHousehold.TryLoadLog(0, 0);
            GameSessionState.StairwellLabyrinthPuzzles.TryApply(StairwellLabyrinthPuzzleAction.Roll0);
            GameSessionState.StairwellEncounter.TryAwaken(true, true);
            GameSessionState.StairwellEncounter.MarkDefeated(3);
            GameSessionState.WarehouseGate.TryUnlock(true);
            GameSessionState.WarehouseGate.Advance(1.237f, true);
            Assert.That(GameSessionState.TryCollectWorldItem("checkpoint.vodka", InventoryItemId.VodkaBottle,
                2, VodkaBottleState.RefrigeratorMl), Is.True);
            Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.VodkaBottle, true), Is.True);
            int bottle = GameSessionState.VodkaBottleId;
            GameSessionState.SetDrinkOpened(InventoryItemId.VodkaBottle, bottle, true);
            Assert.That(GameSessionState.TryDrinkVodkaSip(bottle, out _), Is.True);
            GameSessionState.AdvanceIntoxicationRecovery(.371f);
            ChurchGardenPotSessionState.SetDock("checkpoint.pot", 1);
            CityWetSurfaceRegistry.SetImmediate(.31f);
            CityFishSupplySession.TrySetDebugWorkingSeconds(123.75d);
            CityPedestrianInsultSessionState.BeginWithSalt(1701);
            CityPedestrianInsultSessionState.Walk(GameSessionState.CitySeed).Take();
            var world = CheckpointWorldState.Capture();
            var saved = GameSessionState.CaptureCheckpoint();
            int nextInsult = CityPedestrianInsultSessionState.Walk(GameSessionState.CitySeed).Peek();
            int nextMotherLine = MothersHouseMotherSpeechSession.State.TakeSpokenLine(false);
            int cash = GameSessionState.CashBalance;
            int fill = GameSessionState.VodkaBottleRemainingMl;
            int corruption = GameSessionState.HeroCorruptionDose;
            double minute = GameSessionState.GameTimeOfDayMinutes;
            float gate = GameSessionState.WarehouseGate.Progress;
            Assert.That(GameSessionState.TryDrinkVodkaSip(bottle, out var expectedSip), Is.True);
            GameSessionState.AdvanceGameTime(31.813f);
            int nextHunger = GameSessionState.HungerLevel, nextFatigue = GameSessionState.FatigueLevel;
            double nextMinute = GameSessionState.GameTimeOfDayMinutes;

            for (int repeat = 0; repeat < 2; repeat++)
            {
                GameSessionState.TryCompleteQuest(QuestId.FeedTheCat);
                GameSessionState.TryAdvanceGraveWork("checkpoint.grave", CemeteryGraveWorkStage.Sealed);
                GameSessionState.VillageHousehold.TryLoadLog(0, 1);
                GameSessionState.StairwellEncounter.MarkDefeated(4);
                GameSessionState.WarehouseGate.Advance(5f, true);
                GameSessionState.TrySetDebugHeroCorruptionLevel(7);
                GameSessionState.TryEarnCash(19, "checkpoint_test");
                GameSessionState.ResetInventoryState();
                ChurchGardenPotSessionState.SetDock("checkpoint.pot", 0);
                CityWetSurfaceRegistry.SetImmediate(.85f);
                CityFishSupplySession.TrySetDebugWorkingSeconds(900d);
                // Consume a full shuffled bag to exercise both its array and RNG state.
                for (int line = 0; line < CityPedestrianInsultLines.LineCount; line++)
                    CityPedestrianInsultSessionState.Walk(GameSessionState.CitySeed).Take();
                MothersHouseMotherSpeechSession.State.TakeSpokenLine(false);
                world.Restore();
                GameSessionState.RestoreCheckpoint(saved);

                Assert.That(GameSessionState.CitySeed, Is.EqualTo(-7013));
                Assert.That(GameSessionState.ActiveBarId, Is.EqualTo("checkpoint.bar"));
                Assert.That(GameSessionState.CashBalance, Is.EqualTo(cash));
                Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(corruption));
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(minute));
                Assert.That(GameSessionState.GetQuestStatus(QuestId.FeedTheCat), Is.EqualTo(QuestStatus.Active));
                Assert.That(GameSessionState.GetGraveWorkStage("checkpoint.grave"), Is.EqualTo(CemeteryGraveWorkStage.Dug));
                Assert.That(GameSessionState.FirstSealedGravePlotId, Is.Null);
                Assert.That(GameSessionState.VillageHousehold.GetBasketLogCount(0), Is.EqualTo(1));
                Assert.That(GameSessionState.StairwellLabyrinthPuzzles.Current.Roll0, Is.EqualTo(2));
                Assert.That(GameSessionState.StairwellEncounter.IsDefeated(3), Is.True);
                Assert.That(GameSessionState.StairwellEncounter.IsDefeated(4), Is.False);
                Assert.That(GameSessionState.WarehouseGate.Progress, Is.EqualTo(gate));
                Assert.That(ChurchGardenPotSessionState.GetDock("checkpoint.pot"), Is.EqualTo(1));
                Assert.That(CityWetSurfaceRegistry.CurrentWetness, Is.EqualTo(.31f));
                Assert.That(CityFishSupplySession.WorkingSeconds, Is.EqualTo(123.75d));
                Assert.That(CityPedestrianInsultSessionState.Walk(GameSessionState.CitySeed).Peek(), Is.EqualTo(nextInsult));
                Assert.That(MothersHouseMotherSpeechSession.State.TakeSpokenLine(false), Is.EqualTo(nextMotherLine));
                Assert.That(GameSessionState.VodkaBottleId, Is.EqualTo(bottle));
                Assert.That(GameSessionState.VodkaBottleRemainingMl, Is.EqualTo(fill));
                Assert.That(GameSessionState.GetInventoryItemCount(InventoryItemId.VodkaBottle), Is.EqualTo(2));
                Assert.That(GameSessionState.DrinkOpened(InventoryItemId.VodkaBottle), Is.True);
                Assert.That(GameSessionState.TryCollectWorldItem("checkpoint.vodka", InventoryItemId.VodkaBottle), Is.False);
                Assert.That(GameSessionState.TryDrinkVodkaSip(bottle, out var actualSip), Is.True);
                Assert.That(actualSip.RemainingMl, Is.EqualTo(expectedSip.RemainingMl));
                Assert.That(actualSip.AlcoholDose, Is.EqualTo(expectedSip.AlcoholDose));
                Assert.That(actualSip.StressRelief, Is.EqualTo(expectedSip.StressRelief));
                Assert.That(actualSip.Servings, Is.EqualTo(expectedSip.Servings));
                GameSessionState.AdvanceGameTime(31.813f);
                Assert.That(GameSessionState.GameTimeOfDayMinutes, Is.EqualTo(nextMinute));
                Assert.That(GameSessionState.HungerLevel, Is.EqualTo(nextHunger));
                Assert.That(GameSessionState.FatigueLevel, Is.EqualTo(nextFatigue));
            }
        }

        [Test]
        public void ShotgunRestore_PreservesInsertedShellAndInterruptedReloadWithoutDoubleDebit()
        {
            var shotgun = new ShotgunState(new ShotgunSettings(startingReserveRounds: 5));
            shotgun.SetAim(true);
            shotgun.Advance(shotgun.Settings.RaiseSeconds);
            Assert.That(shotgun.TryFire(), Is.True);
            Assert.That(shotgun.TryReload(), Is.True);
            shotgun.Advance(shotgun.Settings.LoadFirstSeconds + .01f);
            Assert.That(shotgun.ReserveRounds, Is.EqualTo(4));
            var saved = shotgun.Capture();
            float elapsed = shotgun.ReloadElapsed;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                shotgun.Advance(shotgun.Settings.ReloadSeconds);
                shotgun.AddReserveRounds(2);
                shotgun.Restore(saved);
                Assert.That(shotgun.ReserveRounds, Is.EqualTo(4));
                Assert.That(shotgun.Rounds, Is.EqualTo(2));
                Assert.That(shotgun.ReloadElapsed, Is.EqualTo(elapsed));
                Assert.That(shotgun.ReloadPending, Is.True);
                Assert.That(shotgun.IsReloading, Is.False);
                Assert.That(shotgun.AimRequested, Is.False);
                Assert.That(shotgun.TryReload(), Is.True);
                shotgun.Advance(shotgun.Settings.ReloadSeconds);
                Assert.That(shotgun.ReloadPending, Is.False);
                Assert.That(shotgun.ReserveRounds, Is.EqualTo(4));
                Assert.That(shotgun.Rounds, Is.EqualTo(2));
            }
        }

        [Test]
        public void Restore_InvalidatesPaidDrinkTokensAndOldRideLeases_AndNotifiesOnce()
        {
            Assert.That(GameSessionState.TryOrderDrink(DrinkId.LightBeer, out var order).Succeeded, Is.True);
            var previousRide = GameSessionState.AcquireCablewayRide();
            var saved = GameSessionState.CaptureCheckpoint();
            int inventoryEvents = 0, worldEvents = 0, questEvents = 0;
            System.Action inventory = () => inventoryEvents++;
            System.Action world = () => worldEvents++;
            System.Action quests = () => questEvents++;
            GameSessionState.InventoryEquipmentChanged += inventory;
            GameSessionState.WorldItemsChanged += world;
            GameSessionState.QuestLogChanged += quests;
            try
            {
                GameSessionState.RestoreCheckpoint(saved);
                Assert.That(GameSessionState.IsRidingTheCableway, Is.False);
                Assert.That(GameSessionState.TryConsumeOrderedDrink(order), Is.False);
                var newRide = GameSessionState.AcquireCablewayRide();
                previousRide.Dispose();
                Assert.That(GameSessionState.IsRidingTheCableway, Is.True);
                newRide.Dispose();
                Assert.That(inventoryEvents, Is.EqualTo(1));
                Assert.That(worldEvents, Is.EqualTo(1));
                Assert.That(questEvents, Is.EqualTo(1));
            }
            finally
            {
                GameSessionState.InventoryEquipmentChanged -= inventory;
                GameSessionState.WorldItemsChanged -= world;
                GameSessionState.QuestLogChanged -= quests;
                previousRide.Dispose();
            }
        }
    }
}
