using System;
using UnityEngine;

namespace BarPromenade
{
    public static partial class GameSessionState
    {
        /// <summary>
        /// Opaque, detached data for one scene-entry checkpoint. No scene objects,
        /// callbacks or temporary activity ownership can enter the snapshot.
        /// Each restore copies its collections again so the same checkpoint is reusable.
        /// </summary>
        internal sealed class CheckpointSnapshot
        {
            internal readonly int CitySeed;
            internal readonly string CityBlueprintId;
            internal readonly string ActiveBarId;
            internal readonly BarActivityKind ActiveBarActivity;
            internal readonly CityDistrictKind ActiveBarDistrict;
            internal readonly CityReturnKind ReturnKind;
            internal readonly StairwellArrivalKind StairwellArrival;
            internal readonly HomeArrivalKind HomeArrival;
            internal readonly bool HomeSleepAppearanceActive;
            internal readonly AlpineVillageArrivalKind AlpineVillageArrival;
            internal readonly int IntoxicationLevel;
            internal readonly int HeroCorruptionDose;
            internal readonly bool HeroMouthSoiled;
            internal readonly bool ScarfRestingLowered;
            internal readonly int LastTeethBrushingDayIndex;
            internal readonly int StressLevel;
            internal readonly DrinkId LastAlcoholicDrink;
            internal readonly int DrinksConsumed;
            internal readonly int CashBalance;
            internal readonly LastRouteFerrymanRideStage FerrymanRide;
            internal readonly LastRouteCarDashboardState CarDashboard;
            internal readonly float BalanceCheckDelayRemaining;
            internal readonly int BalanceCheckSequence;
            internal readonly bool HasUnreadQuests;
            internal readonly bool HasOpenedJournal;
            internal readonly bool DebugCityMapOnArrivalRequested;
            internal readonly int? WarehouseStorySeed;
            internal readonly bool WarehouseDoorAmbushPlayed;
            internal readonly InventoryState.Checkpoint Inventory;
            internal readonly QuestLogEntry[] Quests;
            internal readonly GameTimeState.Checkpoint Time;
            internal readonly PlayerNeedsProgressionState.Checkpoint Needs;
            internal readonly CemeteryGraveWorkLedger.Checkpoint Graves;
            internal readonly VillageHouseholdProgress.Checkpoint Household;
            internal readonly WarehouseFishAssemblyState.Checkpoint FishAssembly;
            internal readonly WarehouseGateState.Checkpoint Gate;
            internal readonly WarehouseFishLampState.Checkpoint FishLamp;
            internal readonly StairwellLabyrinthPuzzleState.Snapshot StairwellPuzzles;
            internal readonly StairwellEncounterState.Checkpoint StairwellCombat;
            internal readonly string[] CollectedItems;
            internal readonly GameDayEventId[] FiredEvents;
            internal readonly PistolState.Snapshot? Pistol;
            internal readonly float IntoxicationRecoveryElapsed, QuestNoticeAge;

            internal CheckpointSnapshot()
            {
                CitySeed = GameSessionState.CitySeed;
                CityBlueprintId = GameSessionState.CityBlueprintId;
                ActiveBarId = GameSessionState.ActiveBarId;
                ActiveBarActivity = GameSessionState.ActiveBarActivity;
                ActiveBarDistrict = GameSessionState.ActiveBarDistrict;
                ReturnKind = GameSessionState.ReturnKind;
                StairwellArrival = GameSessionState.StairwellArrival;
                HomeArrival = GameSessionState.HomeArrival;
                HomeSleepAppearanceActive = GameSessionState.HomeSleepAppearanceActive;
                AlpineVillageArrival = GameSessionState.AlpineVillageArrival;
                IntoxicationLevel = GameSessionState.IntoxicationLevel;
                HeroCorruptionDose = GameSessionState.HeroCorruptionDose;
                HeroMouthSoiled = GameSessionState.HeroMouthSoiled;
                ScarfRestingLowered = GameSessionState.ScarfRestingLowered;
                LastTeethBrushingDayIndex = GameSessionState.LastTeethBrushingDayIndex;
                StressLevel = GameSessionState.StressLevel;
                LastAlcoholicDrink = GameSessionState.LastAlcoholicDrink;
                DrinksConsumed = GameSessionState.DrinksConsumed;
                CashBalance = GameSessionState.CashBalance;
                FerrymanRide = GameSessionState.FerrymanRide;
                CarDashboard = GameSessionState.CarDashboard;
                BalanceCheckDelayRemaining = GameSessionState.BalanceCheckDelayRemaining;
                BalanceCheckSequence = GameSessionState.BalanceCheckSequence;
                HasUnreadQuests = GameSessionState.HasUnreadQuests;
                HasOpenedJournal = GameSessionState.HasOpenedJournal;
                DebugCityMapOnArrivalRequested = GameSessionState.DebugCityMapOnArrivalRequested;
                WarehouseStorySeed = GameSessionState.WarehouseStorySeed;
                WarehouseDoorAmbushPlayed = GameSessionState.WarehouseDoorAmbushPlayed;
                Inventory = inventory.CaptureCheckpoint();
                Quests = questLog.CaptureCheckpoint();
                Time = gameTime.CaptureCheckpoint();
                Needs = needsProgression.CaptureCheckpoint();
                Graves = graveWork.CaptureCheckpoint();
                Household = villageHousehold.CaptureCheckpoint();
                FishAssembly = warehouseFishAssembly.CaptureCheckpoint();
                Gate = warehouseGate.CaptureCheckpoint();
                FishLamp = warehouseFishLamp.CaptureCheckpoint();
                StairwellPuzzles = stairwellLabyrinthPuzzles.CaptureCheckpoint();
                StairwellCombat = stairwellEncounter.CaptureCheckpoint();
                CollectedItems = new string[collectedWorldItems.Count]; collectedWorldItems.CopyTo(CollectedItems);
                FiredEvents = new GameDayEventId[firedDayEvents.Count]; firedDayEvents.CopyTo(FiredEvents);
                Pistol = storyPistolState;
                IntoxicationRecoveryElapsed = intoxicationRecoveryElapsed;
                QuestNoticeAge = Mathf.Max(0f, UnityEngine.Time.unscaledTime - LastQuestActivatedUnscaledTime);
            }
        }

        internal static CheckpointSnapshot CaptureCheckpoint() => new CheckpointSnapshot();

        internal static void RestoreCheckpointArrival(HomeArrivalKind home, StairwellArrivalKind stairwell,
            AlpineVillageArrivalKind alpine, CityReturnKind cityReturn)
        {
            HomeArrival = home;
            StairwellArrival = stairwell;
            AlpineVillageArrival = alpine;
            ReturnKind = cityReturn;
        }

        /// <summary>
        /// Call after the old world has finished its disable/destroy writeback.
        /// This is a historical load, not a new game: no starter grants, calendar
        /// events or gameplay transactions are replayed while restoring data.
        /// </summary>
        internal static void RestoreCheckpoint(CheckpointSnapshot saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            ClearHawthornUse();
            sessionGeneration = sessionGeneration == int.MaxValue ? 1 : sessionGeneration + 1;
            nextDrinkOrderSequence = 0L;
            vehicleActivity.Reset();
            WarehouseStoryEntryService.ResetPending();
            CitySeed = saved.CitySeed;
            CityBlueprintId = saved.CityBlueprintId;
            ActiveBarId = saved.ActiveBarId;
            ActiveBarActivity = saved.ActiveBarActivity;
            ActiveBarDistrict = saved.ActiveBarDistrict;
            ReturnKind = saved.ReturnKind;
            StairwellArrival = saved.StairwellArrival;
            HomeArrival = saved.HomeArrival;
            HomeSleepAppearanceActive = saved.HomeSleepAppearanceActive;
            AlpineVillageArrival = saved.AlpineVillageArrival;
            IntoxicationLevel = saved.IntoxicationLevel;
            HeroCorruptionDose = saved.HeroCorruptionDose;
            HeroMouthSoiled = saved.HeroMouthSoiled;
            ScarfRestingLowered = saved.ScarfRestingLowered;
            LastTeethBrushingDayIndex = saved.LastTeethBrushingDayIndex;
            StressLevel = saved.StressLevel;
            LastAlcoholicDrink = saved.LastAlcoholicDrink;
            DrinksConsumed = saved.DrinksConsumed;
            CashBalance = saved.CashBalance;
            FerrymanRide = saved.FerrymanRide;
            CarDashboard = saved.CarDashboard;
            BalanceCheckDelayRemaining = saved.BalanceCheckDelayRemaining;
            BalanceCheckSequence = saved.BalanceCheckSequence;
            HasUnreadQuests = saved.HasUnreadQuests;
            HasOpenedJournal = saved.HasOpenedJournal;
            DebugCityMapOnArrivalRequested = saved.DebugCityMapOnArrivalRequested;
            WarehouseStorySeed = saved.WarehouseStorySeed;
            WarehouseDoorAmbushPlayed = saved.WarehouseDoorAmbushPlayed;
            inventory.RestoreCheckpoint(saved.Inventory);
            questLog.RestoreCheckpoint(saved.Quests);
            gameTime.RestoreCheckpoint(saved.Time);
            needsProgression.RestoreCheckpoint(saved.Needs);
            graveWork.RestoreCheckpoint(saved.Graves);
            villageHousehold.RestoreCheckpoint(saved.Household);
            warehouseFishAssembly.RestoreCheckpoint(saved.FishAssembly);
            warehouseGate.RestoreCheckpoint(saved.Gate);
            warehouseFishLamp.RestoreCheckpoint(saved.FishLamp);
            stairwellLabyrinthPuzzles.RestoreCheckpoint(saved.StairwellPuzzles);
            stairwellEncounter.RestoreCheckpoint(saved.StairwellCombat);
            collectedWorldItems.Clear(); collectedWorldItems.UnionWith(saved.CollectedItems);
            firedDayEvents.Clear(); firedDayEvents.UnionWith(saved.FiredEvents);
            storyPistolState = saved.Pistol;
            intoxicationRecoveryElapsed = saved.IntoxicationRecoveryElapsed;
            LastQuestActivatedUnscaledTime = UnityEngine.Time.unscaledTime - saved.QuestNoticeAge;
            GameLog.SetCitySeed(CitySeed);
            // Subscribers observe only the complete restored state.
            WorldItemsChanged?.Invoke();
            InventoryEquipmentChanged?.Invoke();
            QuestLogChanged?.Invoke();
        }
    }
}
