using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeArrivalKind
    {
        Normal = 0,
        OpeningSleep = 1
    }

    public enum AlpineVillageArrivalKind
    {
        Default = 0,
        MothersHouseDoor = 1
    }

    public enum InventoryItemUseStatus
    {
        Success = 0,
        NotConsumable,
        MissingItem,
        NoEffect,
        MaximumIntoxication,
        ReservedForQuest
    }

    public readonly struct InventoryItemUseResult
    {
        internal InventoryItemUseResult(
            InventoryItemUseStatus status,
            InventoryItemId itemId,
            InventoryConsumableKind kind,
            int itemCountBefore,
            int itemCountAfter,
            int hungerBefore,
            int hungerAfter,
            int stressBefore,
            int stressAfter,
            int intoxicationBefore,
            int intoxicationAfter,
            int drinksConsumedBefore,
            int drinksConsumedAfter,
            int requestedStressRelief)
        {
            Status = status;
            ItemId = itemId;
            Kind = kind;
            ItemCountBefore = itemCountBefore;
            ItemCountAfter = itemCountAfter;
            HungerBefore = hungerBefore;
            HungerAfter = hungerAfter;
            StressBefore = stressBefore;
            StressAfter = stressAfter;
            IntoxicationBefore = intoxicationBefore;
            IntoxicationAfter = intoxicationAfter;
            DrinksConsumedBefore = drinksConsumedBefore;
            DrinksConsumedAfter = drinksConsumedAfter;
            RequestedStressRelief = requestedStressRelief;
        }

        public bool Succeeded => Status == InventoryItemUseStatus.Success;
        public InventoryItemUseStatus Status { get; }
        public InventoryItemId ItemId { get; }
        public InventoryConsumableKind Kind { get; }
        public int ItemCountBefore { get; }
        public int ItemCountAfter { get; }
        public int HungerBefore { get; }
        public int HungerAfter { get; }
        public int ActualHungerRelief => HungerBefore - HungerAfter;
        public int StressBefore { get; }
        public int StressAfter { get; }
        public int ActualStressRelief => StressBefore - StressAfter;
        public int IntoxicationBefore { get; }
        public int IntoxicationAfter { get; }
        public int ActualIntoxicationGain =>
            IntoxicationAfter - IntoxicationBefore;
        public int DrinksConsumedBefore { get; }
        public int DrinksConsumedAfter { get; }
        public int RequestedStressRelief { get; }
    }

    public static class GameSessionState
    {
        public const int DefaultCitySeed = 20260727;
        public const string DefaultCityBlueprintId =
            CityBlueprintCatalog.DefaultBlueprintId;
        public const int DefaultCash = 999;
        public const int DefaultHunger = 0;
        public const int DefaultStress = 0;
        public const int DefaultFatigue = 0;
        public const int FirstDebugGameDayNumber = 1;
        public const int LastDebugGameDayNumber = 7;

        private static readonly List<string> plannedBarRoute =
            new List<string>();
        private static readonly ReadOnlyCollection<string> plannedBarRouteView =
            plannedBarRoute.AsReadOnly();
        private static readonly HashSet<string> collectedWorldItems =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly InventoryState inventory =
            new InventoryState();
        private static readonly QuestLogState questLog =
            new QuestLogState();
        private static readonly CemeteryGraveWorkLedger graveWork =
            new CemeteryGraveWorkLedger();
        private static readonly GameTimeState gameTime =
            new GameTimeState();
        private static readonly PlayerNeedsProgressionState needsProgression =
            new PlayerNeedsProgressionState();
        private static float intoxicationRecoveryElapsed;

        public static int CitySeed { get; private set; } = DefaultCitySeed;
        public static string CityBlueprintId { get; private set; } =
            DefaultCityBlueprintId;
        public static string ActiveBarId { get; private set; } = string.Empty;
        public static BarActivityKind ActiveBarActivity { get; private set; } =
            BarActivityKind.None;

        /// <summary>
        /// Which district's bar the hero is in (or entering) — the
        /// key the interior reads its district identity by. Falls
        /// back to Nightlife for direct scene loads, the character
        /// the shared bar has always effectively worn.
        /// </summary>
        public static CityDistrictKind ActiveBarDistrict
        {
            get;
            private set;
        } = BarDistrictIdentityCatalog.FallbackDistrict;
        public static CityReturnKind ReturnKind { get; private set; }
        public static bool IsReturningToCity =>
            ReturnKind != CityReturnKind.None;
        public static StairwellArrivalKind StairwellArrival
        {
            get;
            private set;
        } = StairwellArrivalKind.StreetDoor;
        public static HomeArrivalKind HomeArrival
        {
            get;
            private set;
        } = HomeArrivalKind.Normal;
        public static AlpineVillageArrivalKind AlpineVillageArrival
        {
            get;
            private set;
        } = AlpineVillageArrivalKind.Default;
        public static int IntoxicationLevel { get; private set; }
        public static int LastTeethBrushingDayIndex
        {
            get;
            private set;
        } = -1;
        public static int HungerLevel => needsProgression.HungerLevel;
        public static int StressLevel { get; private set; } = DefaultStress;
        public static int FatigueLevel => needsProgression.FatigueLevel;
        public static DrinkId LastAlcoholicDrink { get; private set; } = DrinkId.None;
        public static int DrinksConsumed { get; private set; }
        public static int CashBalance { get; private set; } = DefaultCash;

        /// <summary>
        /// Every grave the cemetery watchman has sent the hero to,
        /// with how far the work on each has got and the line cut
        /// into its board. It is carried here rather than in the
        /// quest log because the log only knows taken and finished,
        /// and each grave has three separate acts in between.
        /// </summary>
        public static IReadOnlyList<CemeteryGraveWorkRecord>
            GraveWork => graveWork.Records;

        /// <summary>How many graves he is holding open at once: taken
        /// and not yet closed with a stone at the head.</summary>
        public static int UnfinishedGraveWorkCount =>
            graveWork.UnfinishedCount;

        /// <summary>
        /// The plot id of the first grave the hero ever closed with a
        /// stone, or null while none has been. It survives every area
        /// change and dies with the session, exactly like the grave
        /// itself — the cemetery's raven pair keys its claim off this
        /// one value and nothing else.
        /// </summary>
        public static string FirstSealedGravePlotId =>
            graveWork.FirstSealedPlotId;

        /// <summary>
        /// How far the one journey out of the city has got. Both areas build
        /// the Ferryman and his car from this and nothing else, so he is never
        /// in two places and never in none.
        /// </summary>
        public static LastRouteFerrymanRideStage FerrymanRide
        {
            get;
            private set;
        } = LastRouteFerrymanRideStage.NotTaken;

        /// <summary>
        /// True while the hero is sitting in the Ferryman's moving car.
        ///
        /// The map may be opened and read throughout - watching your own
        /// marker climb the mountain is worth having - but nothing that MOVES
        /// him may fire: he is strapped into a scene, and a teleport out of a
        /// car doing eight metres a second leaves the car, the driver and the
        /// journey behind without him.
        /// </summary>
        public static bool IsRidingTheFerryman =>
            FerrymanRide == LastRouteFerrymanRideStage.InTransit;

        /// <summary>
        /// True while the hero is on a cableway cabin's bench and the line is
        /// running.
        ///
        /// Same rule as the car, and for the same reason. Unlike the car this
        /// is a plain flag rather than a stage of a monotone ladder: the
        /// cableway is a two-way, repeatable link, so there is nothing
        /// permanent to remember - where he is IS which scene is loaded.
        /// </summary>
        public static bool IsRidingTheCableway { get; private set; }

        public static void SetRidingTheCableway(bool riding)
        {
            IsRidingTheCableway = riding;
        }

        /// <summary>
        /// What the passenger has done to the Ferryman's dash: the radio,
        /// where its tuning knob stands, and the glovebox lid. On the
        /// session rather than the car because the ride crosses an area
        /// boundary and the mountain raises a NEW car from the ride stage -
        /// a radio switched on at the island has to still be on when the
        /// lights come back.
        /// </summary>
        public static LastRouteCarDashboardState CarDashboard
        {
            get;
            private set;
        } = LastRouteCarDashboardState.Default;

        public static void SetCarDashboard(LastRouteCarDashboardState state)
        {
            CarDashboard = state;
        }

        /// <summary>Either vehicle, for the gates that do not care which.
        /// </summary>
        public static bool IsRidingAVehicle =>
            IsRidingTheFerryman || IsRidingTheCableway;
        public static float BalanceCheckDelayRemaining { get; private set; }
        public static int BalanceCheckSequence { get; private set; }
        public static IReadOnlyList<string> PlannedBarRoute =>
            plannedBarRouteView;
        public static IReadOnlyList<InventoryItemStack> InventoryItems =>
            inventory.Items;
        public static IReadOnlyList<QuestLogEntry> Quests =>
            questLog.Entries;
        public static int CollectedWorldItemCount =>
            collectedWorldItems.Count;
        public static bool IsGameTimeRunning => gameTime.IsRunning;
        public static int GameDayIndex => gameTime.DayIndex;
        public static int GameDayNumber => gameTime.DayNumber;
        public static int GameHour => gameTime.Hour;
        public static int GameMinute => gameTime.Minute;
        public static int GameMinuteOfDay => gameTime.MinuteOfDay;
        public static double GameTimeOfDayMinutes =>
            gameTime.TimeOfDayMinutes;
        public static double GameDayFraction => gameTime.DayFraction;
        public static bool DebugCityMapOnArrivalRequested
        {
            get;
            private set;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ResetToDefaults();
        }

        public static void BeginNewGame()
        {
            ResetToDefaults();
            GameLog.Info(
                "session",
                "new_game_started",
                GameLog.Field("city_seed", CitySeed),
                GameLog.Field(
                    "city_blueprint_id",
                    CityBlueprintId),
                GameLog.Field("cash_balance", CashBalance),
                GameLog.Field("hunger", HungerLevel),
                GameLog.Field("stress", StressLevel),
                GameLog.Field("fatigue", FatigueLevel),
                GameLog.Field(
                    "home_arrival",
                    HomeArrival.ToString()));
        }

        public static bool TryStartGameTimeFromWake()
        {
            return gameTime.TryStartFromWake();
        }

        public static bool TrySetDebugGameDay(int dayNumber)
        {
            if (dayNumber < FirstDebugGameDayNumber ||
                dayNumber > LastDebugGameDayNumber)
            {
                return false;
            }

            int previousDayNumber = gameTime.DayNumber;
            if (!gameTime.TrySetDayNumber(dayNumber))
            {
                return false;
            }

            GameLog.Info(
                "session",
                "debug_game_day_changed",
                GameLog.Field(
                    "previous_day_number",
                    previousDayNumber),
                GameLog.Field("day_number", gameTime.DayNumber),
                GameLog.Field("hour", gameTime.Hour),
                GameLog.Field("minute", gameTime.Minute));
            return true;
        }

        public static void AdvanceGameTime(float scaledDelta)
        {
            double advancedGameMinutes = gameTime.Advance(scaledDelta);
            PlayerNeedsProgressionResult progression =
                needsProgression.Advance(advancedGameMinutes);
            if (!progression.Changed)
            {
                return;
            }

            GameLog.Info(
                "needs",
                "passive_progressed",
                GameLog.Field(
                    "elapsed_game_minutes",
                    advancedGameMinutes),
                GameLog.Field(
                    "previous_hunger",
                    progression.HungerBefore),
                GameLog.Field("hunger", progression.HungerAfter),
                GameLog.Field(
                    "previous_fatigue",
                    progression.FatigueBefore),
                GameLog.Field("fatigue", progression.FatigueAfter));
        }

        private static void ResetToDefaults()
        {
            CityWetSurfaceRegistry.ResetForNewSession();
            CitySeed = DefaultCitySeed;
            CityBlueprintId = DefaultCityBlueprintId;
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            ActiveBarDistrict =
                BarDistrictIdentityCatalog.FallbackDistrict;
            ReturnKind = CityReturnKind.None;
            StairwellArrival = StairwellArrivalKind.StreetDoor;
            HomeArrival = HomeArrivalKind.Normal;
            AlpineVillageArrival = AlpineVillageArrivalKind.Default;
            DebugCityMapOnArrivalRequested = false;
            IntoxicationLevel = 0;
            needsProgression.Reset();
            StressLevel = DefaultStress;
            intoxicationRecoveryElapsed = 0f;
            LastAlcoholicDrink = DrinkId.None;
            DrinksConsumed = 0;
            CashBalance = DefaultCash;
            graveWork.Reset();
            FerrymanRide = LastRouteFerrymanRideStage.NotTaken;
            CarDashboard = LastRouteCarDashboardState.Default;
            gameTime.Reset();
            BalanceCheckDelayRemaining = 0f;
            BalanceCheckSequence = 0;
            plannedBarRoute.Clear();
            LastTeethBrushingDayIndex = -1;
            collectedWorldItems.Clear();
            inventory.ResetWithStarterItems();
            questLog.ResetWithStarterQuests();
            GameLog.SetCitySeed(CitySeed);
        }

        public static QuestStatus GetQuestStatus(QuestId questId)
        {
            return questLog.GetStatus(questId);
        }

        public static bool IsQuestActive(QuestId questId)
        {
            return questLog.GetStatus(questId) == QuestStatus.Active;
        }

        public static bool TryActivateQuest(QuestId questId)
        {
            bool activated = questLog.TryActivate(questId);
            if (activated)
            {
                GameLog.Info(
                    "quest",
                    "quest_activated",
                    GameLog.Field("quest_id", questId.ToString()));
            }

            return activated;
        }

        public static bool TryCompleteQuest(QuestId questId)
        {
            bool completed = questLog.TryComplete(questId);
            if (completed)
            {
                GameLog.Info(
                    "quest",
                    "quest_completed",
                    GameLog.Field("quest_id", questId.ToString()));
            }

            return completed;
        }

        /// <summary>How far the work on one plot has got. Unclaimed
        /// for a plot he was never sent to.</summary>
        public static CemeteryGraveWorkStage GetGraveWorkStage(
            string plotId)
        {
            return graveWork.GetStage(plotId);
        }

        /// <summary>
        /// Moves one grave one or more rungs up its ladder, opening a
        /// record for it the first time. Refuses anything that is not
        /// forward: each worksite is rebuilt from its own record
        /// alone, so a stage that could go back would be a grave that
        /// closes and opens again.
        ///
        /// The quest log is kept in step here rather than by the
        /// worksites, because it is one entry over however many holes
        /// are open: taking any grave puts it up, and only the last
        /// unfinished one takes it down again.
        /// </summary>
        public static bool TryAdvanceGraveWork(
            string plotId,
            CemeteryGraveWorkStage stage)
        {
            CemeteryGraveWorkStage previous =
                graveWork.GetStage(plotId);
            string firstSealedBefore = graveWork.FirstSealedPlotId;
            if (!graveWork.TryAdvance(plotId, stage))
            {
                return false;
            }

            GameLog.Info(
                "quest",
                "grave_work_advanced",
                GameLog.Field("plot", plotId),
                GameLog.Field("previous_stage", previous.ToString()),
                GameLog.Field("stage", stage.ToString()));
            // The null-to-id transition happens once per session, and
            // only the live build in which it happens can observe it —
            // which is why it is logged here and not derived later.
            if (firstSealedBefore == null &&
                graveWork.FirstSealedPlotId != null)
            {
                GameLog.Info(
                    "city",
                    "cemetery_first_grave_sealed",
                    GameLog.Field("plot", plotId));
            }

            if (stage == CemeteryGraveWorkStage.Marked)
            {
                TryActivateQuest(QuestId.DigTheGrave);
            }
            else if (graveWork.UnfinishedCount == 0)
            {
                TryCompleteQuest(QuestId.DigTheGrave);
            }

            return true;
        }

        /// <summary>
        /// Moves the one journey out of the city one rung up its ladder.
        /// Refuses anything that is not forward, for the graves' own reason:
        /// both areas are rebuilt from this value alone, so a stage that could
        /// go back would be a car that arrives at the cafe and then reappears
        /// on the island it left.
        /// </summary>
        public static bool TryAdvanceFerrymanRide(
            LastRouteFerrymanRideStage stage)
        {
            if (stage <= FerrymanRide)
            {
                return false;
            }

            LastRouteFerrymanRideStage previous = FerrymanRide;
            FerrymanRide = stage;
            GameLog.Info(
                "lastroute",
                "ferryman_ride_advanced",
                GameLog.Field("previous_stage", previous.ToString()),
                GameLog.Field("stage", stage.ToString()));
            return true;
        }

        /// <summary>The line standing on one grave's board, or empty
        /// while it is bare.</summary>
        public static string GetGraveEpitaph(string plotId)
        {
            return graveWork.GetEpitaph(plotId);
        }

        /// <summary>
        /// Cuts the hero's line into one plaque. It is written once:
        /// a second attempt is refused rather than allowed to correct
        /// the first, because the board has already been nailed on and
        /// the whole point of the thing is that it is final.
        ///
        /// Whatever is handed in is trimmed to what a plaque holds
        /// before it is kept, so the stored value and the rendered one
        /// are the same string.
        /// </summary>
        public static bool TrySetGraveEpitaph(
            string plotId,
            string text)
        {
            if (!graveWork.TrySetEpitaph(plotId, text))
            {
                return false;
            }

            GameLog.Info(
                "quest",
                "grave_epitaph_written",
                GameLog.Field("plot", plotId),
                GameLog.Field(
                    "words",
                    CemeteryEpitaph.CountWords(
                        graveWork.GetEpitaph(plotId))));
            return true;
        }

        /// <summary>
        /// Pays money into the hero's pocket. The only way cash goes
        /// up outside a new game, so every earning passes one logged
        /// gate.
        /// </summary>
        public static bool TryEarnCash(int amount, string reason)
        {
            if (amount <= 0)
            {
                return false;
            }

            int previousCash = CashBalance;
            CashBalance = (int)Math.Min(
                (long)CashBalance + amount,
                int.MaxValue);
            GameLog.Info(
                "session",
                "cash_earned",
                GameLog.Field(
                    "reason",
                    string.IsNullOrWhiteSpace(reason)
                        ? string.Empty
                        : reason),
                GameLog.Field("amount", CashBalance - previousCash),
                GameLog.Field("cash_before", previousCash),
                GameLog.Field("cash_balance", CashBalance));
            return true;
        }

        public static bool IsInventoryItemReservedForQuest(
            InventoryItemId itemId)
        {
            return itemId == InventoryItemId.OpenStewCan &&
                   IsQuestActive(QuestId.FeedTheCat);
        }

        public static bool TryAddInventoryItem(
            InventoryItemId itemId,
            int count = 1)
        {
            bool added = inventory.TryAdd(itemId, count);
            if (added)
            {
                GameLog.Info(
                    "inventory",
                    "item_added",
                    GameLog.Field("item_id", itemId.ToString()),
                    GameLog.Field("count", count),
                    GameLog.Field(
                        "total_count",
                        inventory.GetCount(itemId)));
            }

            return added;
        }

        public static int GetInventoryItemCount(
            InventoryItemId itemId)
        {
            return inventory.GetCount(itemId);
        }

        public static bool HasInventoryItem(
            InventoryItemId itemId,
            int count = 1)
        {
            return count > 0 &&
                   inventory.GetCount(itemId) >= count;
        }

        public static bool TryRemoveInventoryItem(
            InventoryItemId itemId,
            int count = 1)
        {
            bool removed = inventory.TryRemove(itemId, count);
            if (removed)
            {
                GameLog.Info(
                    "inventory",
                    "item_removed",
                    GameLog.Field("item_id", itemId.ToString()),
                    GameLog.Field("count", count),
                    GameLog.Field(
                        "remaining_count",
                        inventory.GetCount(itemId)));
            }

            return removed;
        }

        public static void UpdateNeeds(int hunger, int stress)
        {
            int nextHunger = Mathf.Clamp(
                hunger,
                PlayerNeedsRules.MinimumLevel,
                PlayerNeedsRules.MaximumLevel);
            int nextStress = Mathf.Clamp(
                stress,
                PlayerNeedsRules.MinimumLevel,
                PlayerNeedsRules.MaximumLevel);
            if (HungerLevel == nextHunger && StressLevel == nextStress)
            {
                return;
            }

            int previousHunger = HungerLevel;
            int previousStress = StressLevel;
            if (previousHunger != nextHunger)
            {
                needsProgression.SetHunger(nextHunger);
            }

            StressLevel = nextStress;
            GameLog.Info(
                "needs",
                "levels_changed",
                GameLog.Field("previous_hunger", previousHunger),
                GameLog.Field("hunger", HungerLevel),
                GameLog.Field("previous_stress", previousStress),
                GameLog.Field("stress", StressLevel));
        }

        public static void UpdateFatigue(int fatigue)
        {
            int nextFatigue = Mathf.Clamp(
                fatigue,
                PlayerNeedsRules.MinimumLevel,
                PlayerNeedsRules.MaximumLevel);
            int previousFatigue = FatigueLevel;
            needsProgression.SetFatigue(nextFatigue);
            if (previousFatigue == nextFatigue)
            {
                return;
            }

            GameLog.Info(
                "needs",
                "fatigue_changed",
                GameLog.Field("previous_fatigue", previousFatigue),
                GameLog.Field("fatigue", FatigueLevel));
        }

        public static void ResetFatigueAfterSleep()
        {
            UpdateFatigue(DefaultFatigue);
        }

        /// <summary>
        /// A completed bathroom ritual (toilet, shower) relieves a
        /// little stress. Ungated: every completed scene commits.
        /// </summary>
        public static void CommitBathroomStressRelief(
            string sourceId,
            int relief)
        {
            PlayerNeedReliefResult result =
                PlayerNeedsRules.ApplyStressRelief(
                    StressLevel,
                    relief);
            UpdateNeeds(HungerLevel, result.LevelAfter);
            GameLog.Info(
                "needs",
                "bathroom_stress_relief",
                GameLog.Field(
                    "source_id",
                    sourceId ?? string.Empty),
                GameLog.Field("requested_relief", relief),
                GameLog.Field(
                    "actual_relief",
                    result.ActualRelief),
                GameLog.Field("stress_after", result.LevelAfter));
        }

        /// <summary>
        /// Teeth brushing relieves stress once per game day; the scene
        /// stays replayable, only the relief is gated.
        /// </summary>
        public static bool TryCommitTeethBrushingRelief(int relief)
        {
            if (GameDayIndex == LastTeethBrushingDayIndex)
            {
                GameLog.Info(
                    "needs",
                    "teeth_brushing_relief_ignored",
                    GameLog.Field("day_index", GameDayIndex));
                return false;
            }

            LastTeethBrushingDayIndex = GameDayIndex;
            PlayerNeedReliefResult result =
                PlayerNeedsRules.ApplyStressRelief(
                    StressLevel,
                    relief);
            UpdateNeeds(HungerLevel, result.LevelAfter);
            GameLog.Info(
                "needs",
                "teeth_brushing_relief",
                GameLog.Field("day_index", GameDayIndex),
                GameLog.Field("requested_relief", relief),
                GameLog.Field(
                    "actual_relief",
                    result.ActualRelief),
                GameLog.Field("stress_after", result.LevelAfter));
            return true;
        }

        public static InventoryItemUseResult EvaluateInventoryItemUse(
            InventoryItemId itemId)
        {
            int itemCount = inventory.GetCount(itemId);
            if (!InventoryConsumableCatalog.TryGet(
                    itemId,
                    out InventoryConsumableDefinition definition))
            {
                return CreateInventoryItemUseResult(
                    InventoryItemUseStatus.NotConsumable,
                    itemId,
                    default,
                    itemCount,
                    itemCount);
            }

            if (itemCount <= 0)
            {
                return CreateInventoryItemUseResult(
                    InventoryItemUseStatus.MissingItem,
                    itemId,
                    definition.Kind,
                    itemCount,
                    itemCount);
            }

            if (IsInventoryItemReservedForQuest(itemId))
            {
                return CreateInventoryItemUseResult(
                    InventoryItemUseStatus.ReservedForQuest,
                    itemId,
                    definition.Kind,
                    itemCount,
                    itemCount);
            }

            if (definition.Kind == InventoryConsumableKind.Food)
            {
                PlayerNeedReliefResult relief =
                    PlayerNeedsRules.ApplyFoodRelief(
                        HungerLevel,
                        definition.HungerRelief,
                        definition.MinimumHungerAfterUse);
                return new InventoryItemUseResult(
                    relief.Changed
                        ? InventoryItemUseStatus.Success
                        : InventoryItemUseStatus.NoEffect,
                    itemId,
                    definition.Kind,
                    itemCount,
                    relief.Changed ? itemCount - 1 : itemCount,
                    HungerLevel,
                    relief.LevelAfter,
                    StressLevel,
                    StressLevel,
                    IntoxicationLevel,
                    IntoxicationLevel,
                    DrinksConsumed,
                    DrinksConsumed,
                    0);
            }

            if (IntoxicationLevel >= IntoxicationStageRules.MaximumLevel)
            {
                return CreateInventoryItemUseResult(
                    InventoryItemUseStatus.MaximumIntoxication,
                    itemId,
                    definition.Kind,
                    itemCount,
                    itemCount);
            }

            int requestedStressRelief = definition.StressRelief;
            PlayerNeedReliefResult stressRelief =
                PlayerNeedsRules.ApplyStressRelief(
                    StressLevel,
                    requestedStressRelief);
            int intoxicationGain = PlayerNeedsRules.ScaleRelief(
                DrinkRules.GetIntoxicationGain(definition.DrinkId),
                definition.Servings);
            int servingCount = PlayerNeedsRules.ScaleRelief(
                1,
                definition.Servings);
            int intoxicationAfter = Math.Min(
                IntoxicationStageRules.MaximumLevel,
                IntoxicationLevel + intoxicationGain);
            int drinksAfter = DrinksConsumed <= int.MaxValue - servingCount
                ? DrinksConsumed + servingCount
                : int.MaxValue;
            return new InventoryItemUseResult(
                InventoryItemUseStatus.Success,
                itemId,
                definition.Kind,
                itemCount,
                itemCount - 1,
                HungerLevel,
                HungerLevel,
                StressLevel,
                stressRelief.LevelAfter,
                IntoxicationLevel,
                intoxicationAfter,
                DrinksConsumed,
                drinksAfter,
                requestedStressRelief);
        }

        public static InventoryItemUseResult TryConsumeInventoryItem(
            InventoryItemId itemId)
        {
            InventoryItemUseResult result =
                EvaluateInventoryItemUse(itemId);
            if (!result.Succeeded)
            {
                LogInventoryItemUse(result);
                return result;
            }

            if (!inventory.TryRemove(itemId))
            {
                result = EvaluateInventoryItemUse(itemId);
                LogInventoryItemUse(result);
                return result;
            }

            InventoryConsumableDefinition definition =
                InventoryConsumableCatalog.Get(itemId);
            if (definition.Kind == InventoryConsumableKind.Food)
            {
                UpdateNeeds(result.HungerAfter, result.StressAfter);
            }
            else
            {
                CommitAcceptedDrinkingProgress(
                    result.IntoxicationAfter,
                    definition.DrinkId,
                    result.DrinksConsumedAfter,
                    result.RequestedStressRelief);
            }

            LogInventoryItemUse(result);
            return result;
        }

        public static bool TryCollectWorldItem(
            string sourceId,
            InventoryItemId itemId,
            int count = 1)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return false;
            }

            string normalizedSourceId = sourceId.Trim();
            if (collectedWorldItems.Contains(normalizedSourceId) ||
                !inventory.TryAdd(itemId, count))
            {
                return false;
            }

            collectedWorldItems.Add(normalizedSourceId);
            GameLog.Info(
                "inventory",
                "world_item_collected",
                GameLog.Field("source_id", normalizedSourceId),
                GameLog.Field("item_id", itemId.ToString()),
                GameLog.Field("count", count),
                GameLog.Field(
                    "collected_world_item_count",
                    collectedWorldItems.Count));
            return true;
        }

        public static SupermarketPurchaseResult TryPurchaseWorldItem(
            string sourceId,
            InventoryItemId itemId)
        {
            string normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
                ? string.Empty
                : sourceId.Trim();
            int itemCountBefore = inventory.GetCount(itemId);
            SupermarketPurchaseResult result =
                SupermarketPurchaseRules.Evaluate(
                    normalizedSourceId,
                    itemId,
                    normalizedSourceId.Length > 0 &&
                    collectedWorldItems.Contains(normalizedSourceId),
                    CashBalance,
                    itemCountBefore);
            if (!result.Succeeded)
            {
                LogWorldItemPurchase(result);
                return result;
            }

            if (!collectedWorldItems.Add(normalizedSourceId))
            {
                result = SupermarketPurchaseRules.Evaluate(
                    normalizedSourceId,
                    itemId,
                    true,
                    CashBalance,
                    itemCountBefore);
                LogWorldItemPurchase(result);
                return result;
            }

            if (!inventory.TryAdd(itemId))
            {
                collectedWorldItems.Remove(normalizedSourceId);
                result = SupermarketPurchaseRules.Evaluate(
                    normalizedSourceId,
                    itemId,
                    false,
                    CashBalance,
                    inventory.GetCount(itemId));
                LogWorldItemPurchase(result);
                return result;
            }

            CashBalance = result.CashAfter;
            LogWorldItemPurchase(result);
            return result;
        }

        public static bool IsWorldItemCollected(string sourceId)
        {
            return !string.IsNullOrWhiteSpace(sourceId) &&
                   collectedWorldItems.Contains(sourceId.Trim());
        }

        public static void ResetInventoryState()
        {
            int previousStackCount = inventory.Items.Count;
            int previousCollectedCount = collectedWorldItems.Count;
            collectedWorldItems.Clear();
            inventory.ResetWithStarterItems();
            GameLog.Info(
                "inventory",
                "inventory_reset",
                GameLog.Field(
                    "previous_stack_count",
                    previousStackCount),
                GameLog.Field(
                    "previous_collected_world_item_count",
                    previousCollectedCount));
        }

        public static void SetCitySeed(int seed)
        {
            if (CitySeed == seed)
            {
                GameLog.SetCitySeed(seed);
                return;
            }

            int previousSeed = CitySeed;
            int clearedRouteCount = plannedBarRoute.Count;
            CitySeed = seed;
            GameLog.SetCitySeed(seed);
            ClearRoute();
            GameLog.Info(
                "session",
                "city_seed_changed",
                GameLog.Field("previous_seed", previousSeed),
                GameLog.Field("new_seed", CitySeed),
                GameLog.Field(
                    "cleared_route_count",
                    clearedRouteCount));
        }

        public static void SetCityBlueprint(string blueprintId)
        {
            CityBlueprint blueprint =
                CityBlueprintCatalog.Resolve(blueprintId);
            string resolvedId = blueprint.Id;
            if (string.Equals(
                    CityBlueprintId,
                    resolvedId,
                    StringComparison.Ordinal))
            {
                return;
            }

            string previousBlueprintId = CityBlueprintId;
            int clearedRouteCount = plannedBarRoute.Count;
            CityBlueprintId = resolvedId;
            ClearRoute();
            GameLog.Info(
                "session",
                "city_blueprint_changed",
                GameLog.Field(
                    "previous_blueprint_id",
                    previousBlueprintId),
                GameLog.Field(
                    "new_blueprint_id",
                    CityBlueprintId),
                GameLog.Field(
                    "cleared_route_count",
                    clearedRouteCount));
        }

        public static bool TryAddRouteStop(string barId)
        {
            if (string.IsNullOrWhiteSpace(barId) ||
                plannedBarRoute.Contains(barId))
            {
                return false;
            }

            plannedBarRoute.Add(barId);
            GameLog.Info(
                "session",
                "route_stop_added",
                GameLog.Field("bar_id", barId),
                GameLog.Field(
                    "route_index",
                    plannedBarRoute.Count - 1),
                GameLog.Field("route", FormatRoute()));
            return true;
        }

        public static bool RemoveRouteStop(string barId)
        {
            if (string.IsNullOrWhiteSpace(barId))
            {
                return false;
            }

            int routeIndex = plannedBarRoute.IndexOf(barId);
            if (routeIndex < 0)
            {
                return false;
            }

            plannedBarRoute.RemoveAt(routeIndex);
            GameLog.Info(
                "session",
                "route_stop_removed",
                GameLog.Field("bar_id", barId),
                GameLog.Field("previous_index", routeIndex),
                GameLog.Field("route", FormatRoute()));
            return true;
        }

        public static bool MoveRouteStop(string barId, int direction)
        {
            if (string.IsNullOrWhiteSpace(barId) ||
                (direction != -1 && direction != 1))
            {
                return false;
            }

            int currentIndex = plannedBarRoute.IndexOf(barId);
            int targetIndex = currentIndex + direction;
            if (currentIndex < 0 ||
                targetIndex < 0 ||
                targetIndex >= plannedBarRoute.Count)
            {
                return false;
            }

            string displacedBarId = plannedBarRoute[targetIndex];
            plannedBarRoute[targetIndex] = barId;
            plannedBarRoute[currentIndex] = displacedBarId;
            GameLog.Info(
                "session",
                "route_stop_moved",
                GameLog.Field("bar_id", barId),
                GameLog.Field("previous_index", currentIndex),
                GameLog.Field("new_index", targetIndex),
                GameLog.Field("displaced_bar_id", displacedBarId),
                GameLog.Field("route", FormatRoute()));
            return true;
        }

        public static void ClearRoute()
        {
            if (plannedBarRoute.Count == 0)
            {
                return;
            }

            string previousRoute = FormatRoute();
            int previousCount = plannedBarRoute.Count;
            plannedBarRoute.Clear();
            GameLog.Info(
                "session",
                "route_cleared",
                GameLog.Field("previous_route", previousRoute),
                GameLog.Field("previous_count", previousCount));
        }

        public static void EnterBar(string barId)
        {
            EnterBar(barId, BarActivityKind.Cocktail);
        }

        public static void EnterBar(
            string barId,
            BarActivityKind barActivity)
        {
            EnterBar(
                barId,
                barActivity,
                BarDistrictIdentityCatalog.FallbackDistrict);
        }

        public static void EnterBar(
            string barId,
            BarActivityKind barActivity,
            CityDistrictKind barDistrict)
        {
            string nextBarId = barId ?? string.Empty;
            BarActivityKind nextActivity =
                string.IsNullOrEmpty(nextBarId)
                ? BarActivityKind.None
                : NormalizeBarActivity(barActivity);
            CityDistrictKind nextDistrict =
                BarDistrictIdentityCatalog.Normalize(barDistrict);
            if (string.Equals(
                    ActiveBarId,
                    nextBarId,
                    StringComparison.Ordinal) &&
                ActiveBarActivity == nextActivity &&
                ActiveBarDistrict == nextDistrict &&
                ReturnKind == CityReturnKind.None)
            {
                return;
            }

            ActiveBarId = nextBarId;
            ActiveBarActivity = nextActivity;
            ActiveBarDistrict = nextDistrict;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "bar_entered",
                GameLog.Field("bar_id", ActiveBarId),
                GameLog.Field(
                    "activity",
                    ActiveBarActivity.ToString()),
                GameLog.Field(
                    "district",
                    ActiveBarDistrict.ToString()));
        }

        public static void PrepareCityReturn()
        {
            CityReturnKind nextKind =
                string.IsNullOrEmpty(ActiveBarId)
                    ? CityReturnKind.None
                    : CityReturnKind.Bar;
            if (ReturnKind == nextKind)
            {
                return;
            }

            ReturnKind = nextKind;
            GameLog.Info(
                "session",
                "city_return_prepared",
                GameLog.Field("bar_id", ActiveBarId),
                GameLog.Field(
                    "activity",
                    ActiveBarActivity.ToString()),
                GameLog.Field(
                    "return_kind",
                    ReturnKind.ToString()),
                GameLog.Field(
                    "is_returning",
                    IsReturningToCity));
        }

        public static void EnterHome()
        {
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            ActiveBarDistrict =
                BarDistrictIdentityCatalog.FallbackDistrict;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "home_entered");
        }

        public static void EnterSupermarket()
        {
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            ActiveBarDistrict =
                BarDistrictIdentityCatalog.FallbackDistrict;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "supermarket_entered");
        }

        public static void EnterChurch()
        {
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            ActiveBarDistrict =
                BarDistrictIdentityCatalog.FallbackDistrict;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "church_entered");
        }

        public static void EnterMothersHouse()
        {
            AlpineVillageArrival = AlpineVillageArrivalKind.Default;
            GameLog.Info(
                "session",
                "mothers_house_entered");
        }

        public static void PrepareHomeReturn()
        {
            if (ReturnKind == CityReturnKind.PlayerHome)
            {
                return;
            }

            ReturnKind = CityReturnKind.PlayerHome;
            GameLog.Info(
                "session",
                "city_return_prepared",
                GameLog.Field(
                    "return_kind",
                    ReturnKind.ToString()),
                GameLog.Field("is_returning", true));
        }

        public static bool RequestDebugCityMapOnArrival()
        {
            if (DebugCityMapOnArrivalRequested)
            {
                return false;
            }

            DebugCityMapOnArrivalRequested = true;
            GameLog.Info(
                "session",
                "debug_city_map_on_arrival_requested");
            return true;
        }

        public static bool CompleteDebugCityMapOnArrival()
        {
            if (!DebugCityMapOnArrivalRequested)
            {
                return false;
            }

            DebugCityMapOnArrivalRequested = false;
            GameLog.Info(
                "session",
                "debug_city_map_on_arrival_completed");
            return true;
        }

        public static void PrepareSupermarketReturn()
        {
            if (ReturnKind == CityReturnKind.Supermarket)
            {
                return;
            }

            ReturnKind = CityReturnKind.Supermarket;
            GameLog.Info(
                "session",
                "city_return_prepared",
                GameLog.Field(
                    "return_kind",
                    ReturnKind.ToString()),
                GameLog.Field("is_returning", true));
        }

        public static void PrepareChurchReturn()
        {
            if (ReturnKind == CityReturnKind.Church)
            {
                return;
            }

            ReturnKind = CityReturnKind.Church;
            GameLog.Info(
                "session",
                "city_return_prepared",
                GameLog.Field(
                    "return_kind",
                    ReturnKind.ToString()),
                GameLog.Field("is_returning", true));
        }

        public static void PrepareStairwellArrival(
            StairwellArrivalKind arrival)
        {
            if (arrival != StairwellArrivalKind.StreetDoor &&
                arrival != StairwellArrivalKind.ApartmentDoor)
            {
                throw new ArgumentOutOfRangeException(nameof(arrival));
            }

            StairwellArrival = arrival;
            GameLog.Info(
                "session",
                "stairwell_arrival_prepared",
                GameLog.Field(
                    "arrival",
                    StairwellArrival.ToString()));
        }

        public static StairwellArrivalKind ConsumeStairwellArrival()
        {
            StairwellArrivalKind arrival = StairwellArrival;
            StairwellArrival = StairwellArrivalKind.StreetDoor;
            GameLog.Info(
                "session",
                "stairwell_arrival_consumed",
                GameLog.Field("arrival", arrival.ToString()));
            return arrival;
        }

        public static void PrepareHomeArrival(
            HomeArrivalKind arrival)
        {
            if (arrival != HomeArrivalKind.Normal &&
                arrival != HomeArrivalKind.OpeningSleep)
            {
                throw new ArgumentOutOfRangeException(nameof(arrival));
            }

            HomeArrival = arrival;
            GameLog.Info(
                "session",
                "home_arrival_prepared",
                GameLog.Field(
                    "arrival",
                    HomeArrival.ToString()));
        }

        public static HomeArrivalKind ConsumeHomeArrival()
        {
            HomeArrivalKind arrival = HomeArrival;
            HomeArrival = HomeArrivalKind.Normal;
            GameLog.Info(
                "session",
                "home_arrival_consumed",
                GameLog.Field("arrival", arrival.ToString()));
            return arrival;
        }

        public static void PrepareAlpineVillageArrival(
            AlpineVillageArrivalKind arrival)
        {
            if (arrival != AlpineVillageArrivalKind.Default &&
                arrival != AlpineVillageArrivalKind.MothersHouseDoor)
            {
                throw new ArgumentOutOfRangeException(nameof(arrival));
            }

            AlpineVillageArrival = arrival;
            GameLog.Info(
                "session",
                "alpine_village_arrival_prepared",
                GameLog.Field(
                    "arrival",
                    AlpineVillageArrival.ToString()));
        }

        public static AlpineVillageArrivalKind ConsumeAlpineVillageArrival()
        {
            AlpineVillageArrivalKind arrival = AlpineVillageArrival;
            AlpineVillageArrival = AlpineVillageArrivalKind.Default;
            GameLog.Info(
                "session",
                "alpine_village_arrival_consumed",
                GameLog.Field("arrival", arrival.ToString()));
            return arrival;
        }

        public static bool TryGetReturnBarId(out string barId)
        {
            barId = ActiveBarId;
            return ReturnKind == CityReturnKind.Bar &&
                   !string.IsNullOrEmpty(barId);
        }

        public static bool TryGetCityReturnKind(
            out CityReturnKind returnKind)
        {
            returnKind = ReturnKind;
            return returnKind != CityReturnKind.None;
        }

        public static void CompleteCityReturn()
        {
            if (!IsReturningToCity)
            {
                return;
            }

            CityReturnKind completedKind = ReturnKind;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "city_return_completed",
                GameLog.Field("bar_id", ActiveBarId),
                GameLog.Field(
                    "activity",
                    ActiveBarActivity.ToString()),
                GameLog.Field(
                    "return_kind",
                    completedKind.ToString()));
        }

        public static void UpdateDrinkingProgress(
            int intoxication,
            DrinkId lastDrink,
            int drinksConsumed)
        {
            int nextIntoxication = Mathf.Clamp(intoxication, 0, 100);
            int nextDrinksConsumed = Mathf.Max(0, drinksConsumed);
            bool resetBalanceDelay =
                nextIntoxication <=
                IntoxicationStageRules.BalanceThreshold &&
                BalanceCheckDelayRemaining > 0f;
            if (IntoxicationLevel == nextIntoxication &&
                LastAlcoholicDrink == lastDrink &&
                DrinksConsumed == nextDrinksConsumed &&
                !resetBalanceDelay)
            {
                return;
            }

            int previousIntoxication = IntoxicationLevel;
            DrinkId previousDrink = LastAlcoholicDrink;
            int previousDrinksConsumed = DrinksConsumed;
            IntoxicationLevel = nextIntoxication;
            if (IntoxicationLevel != previousIntoxication)
            {
                intoxicationRecoveryElapsed = 0f;
            }

            LastAlcoholicDrink = lastDrink;
            DrinksConsumed = nextDrinksConsumed;
            if (IntoxicationLevel <=
                IntoxicationStageRules.BalanceThreshold)
            {
                BalanceCheckDelayRemaining = 0f;
            }

            GameLog.Info(
                "session",
                "drinking_changed",
                GameLog.Field(
                    "previous_intoxication",
                    previousIntoxication),
                GameLog.Field(
                    "intoxication",
                    IntoxicationLevel),
                GameLog.Field(
                    "previous_drink",
                    previousDrink.ToString()),
                GameLog.Field(
                    "last_drink",
                    LastAlcoholicDrink.ToString()),
                GameLog.Field(
                    "previous_drinks_consumed",
                    previousDrinksConsumed),
                GameLog.Field(
                    "drinks_consumed",
                    DrinksConsumed),
                GameLog.Field(
                    "balance_delay_reset",
                    resetBalanceDelay));
        }

        public static void CommitDrinkingProgress(
            int intoxication,
            DrinkId lastDrink,
            int drinksConsumed,
            int requestedStressRelief)
        {
            int normalizedDrinksConsumed = Math.Max(0, drinksConsumed);
            if (normalizedDrinksConsumed <= DrinksConsumed)
            {
                GameLog.Info(
                    "needs",
                    "alcohol_stress_relief_ignored",
                    GameLog.Field("drink", lastDrink.ToString()),
                    GameLog.Field(
                        "current_drinks_consumed",
                        DrinksConsumed),
                    GameLog.Field(
                        "snapshot_drinks_consumed",
                        normalizedDrinksConsumed),
                    GameLog.Field("reason", "stale_or_duplicate"));
                return;
            }

            CommitAcceptedDrinkingProgress(
                intoxication,
                lastDrink,
                normalizedDrinksConsumed,
                requestedStressRelief);
        }

        private static void CommitAcceptedDrinkingProgress(
            int intoxication,
            DrinkId lastDrink,
            int drinksConsumed,
            int requestedStressRelief)
        {
            int previousDrinksConsumed = DrinksConsumed;
            UpdateDrinkingProgress(
                intoxication,
                lastDrink,
                drinksConsumed);
            int normalizedRelief = Math.Max(0, requestedStressRelief);
            PlayerNeedReliefResult relief =
                PlayerNeedsRules.ApplyStressRelief(
                    StressLevel,
                    normalizedRelief);
            if (relief.Changed)
            {
                UpdateNeeds(HungerLevel, relief.LevelAfter);
            }

            GameLog.Info(
                "needs",
                "alcohol_stress_relief_committed",
                GameLog.Field("drink", lastDrink.ToString()),
                GameLog.Field(
                    "drink_count_delta",
                    DrinksConsumed - previousDrinksConsumed),
                GameLog.Field(
                    "requested_stress_relief",
                    normalizedRelief),
                GameLog.Field(
                    "actual_stress_relief",
                    relief.ActualRelief),
                GameLog.Field("stress", StressLevel));
        }

        public static void AdvanceIntoxicationRecovery(
            float unscaledDeltaTime)
        {
            float remainingTime = Mathf.Max(0f, unscaledDeltaTime);
            if (IntoxicationLevel <= 0 || remainingTime <= 0f)
            {
                return;
            }

            int previousIntoxication = IntoxicationLevel;
            while (IntoxicationLevel > 0)
            {
                float secondsPerPoint =
                    IntoxicationStageRules.GetRecoverySecondsPerPoint(
                        IntoxicationLevel);
                float timeUntilNextPoint = Mathf.Max(
                    0f,
                    secondsPerPoint - intoxicationRecoveryElapsed);
                if (remainingTime < timeUntilNextPoint)
                {
                    intoxicationRecoveryElapsed += remainingTime;
                    break;
                }

                remainingTime -= timeUntilNextPoint;
                intoxicationRecoveryElapsed = 0f;
                IntoxicationLevel--;
                if (remainingTime <= 0f)
                {
                    break;
                }
            }

            if (IntoxicationLevel == 0)
            {
                intoxicationRecoveryElapsed = 0f;
            }

            if (IntoxicationLevel <=
                IntoxicationStageRules.BalanceThreshold)
            {
                BalanceCheckDelayRemaining = 0f;
            }

            if (IntoxicationLevel != previousIntoxication)
            {
                GameLog.Debug(
                    "intoxication",
                    "recovered",
                    GameLog.Field(
                        "previous_level",
                        previousIntoxication),
                    GameLog.Field("level", IntoxicationLevel),
                    GameLog.Field(
                        "recovered_points",
                        previousIntoxication - IntoxicationLevel));
            }
        }

        public static DrinkPurchaseResult TryPurchaseDrink(
            DrinkId drinkId)
        {
            DrinkPurchaseResult result =
                DrinkPurchaseRules.Evaluate(
                    drinkId,
                    CashBalance,
                    IntoxicationLevel,
                    LastAlcoholicDrink,
                    DrinksConsumed);
            if (result.Succeeded)
            {
                CashBalance = result.CashAfter;
                CommitDrinkingProgress(
                    result.IntoxicationAfter,
                    result.LastAlcoholicDrinkAfter,
                    result.DrinksConsumedAfter,
                    DrinkRules.GetStressRelief(drinkId));
            }

            LogDrinkPurchase(result);
            return result;
        }

        public static void SetBalanceCheckDelay(float seconds)
        {
            float nextValue = Mathf.Max(0f, seconds);
            if (BalanceCheckDelayRemaining == nextValue)
            {
                return;
            }

            float previousValue = BalanceCheckDelayRemaining;
            BalanceCheckDelayRemaining = nextValue;
            GameLog.Debug(
                "balance",
                "delay_changed",
                GameLog.Field("previous_seconds", previousValue),
                GameLog.Field(
                    "remaining_seconds",
                    BalanceCheckDelayRemaining));
        }

        public static void AdvanceBalanceCheckDelay(
            float unscaledDeltaTime)
        {
            BalanceCheckDelayRemaining = Mathf.Max(
                0f,
                BalanceCheckDelayRemaining -
                Mathf.Max(0f, unscaledDeltaTime));
        }

        public static int ConsumeBalanceCheckSequence()
        {
            int sequence = BalanceCheckSequence;
            BalanceCheckSequence++;
            GameLog.Debug(
                "balance",
                "sequence_consumed",
                GameLog.Field("sequence", sequence),
                GameLog.Field(
                    "next_sequence",
                    BalanceCheckSequence));
            return sequence;
        }

        public static void ResetDrinkingState()
        {
            if (IntoxicationLevel == 0 &&
                LastAlcoholicDrink == DrinkId.None &&
                DrinksConsumed == 0 &&
                BalanceCheckDelayRemaining <= 0f &&
                BalanceCheckSequence == 0)
            {
                return;
            }

            int previousIntoxication = IntoxicationLevel;
            int previousDrinksConsumed = DrinksConsumed;
            IntoxicationLevel = 0;
            intoxicationRecoveryElapsed = 0f;
            LastAlcoholicDrink = DrinkId.None;
            DrinksConsumed = 0;
            BalanceCheckDelayRemaining = 0f;
            BalanceCheckSequence = 0;
            GameLog.Info(
                "session",
                "drinking_reset",
                GameLog.Field(
                    "previous_intoxication",
                    previousIntoxication),
                GameLog.Field(
                    "previous_drinks_consumed",
                    previousDrinksConsumed));
        }

        public static void ResetEconomyState()
        {
            if (CashBalance == DefaultCash)
            {
                return;
            }

            int previousCash = CashBalance;
            CashBalance = DefaultCash;
            GameLog.Info(
                "session",
                "economy_reset",
                GameLog.Field("previous_cash", previousCash),
                GameLog.Field("cash_balance", CashBalance));
        }

        private static BarActivityKind NormalizeBarActivity(
            BarActivityKind barActivity)
        {
            // The minigames are gone; the activity only flavours the
            // interior layout now, so anything undefined falls back to
            // the ordinary cocktail-bar composition.
            return System.Enum.IsDefined(
                       typeof(BarActivityKind),
                       barActivity) &&
                   barActivity != BarActivityKind.None
                ? barActivity
                : BarActivityKind.Cocktail;
        }

        private static InventoryItemUseResult CreateInventoryItemUseResult(
            InventoryItemUseStatus status,
            InventoryItemId itemId,
            InventoryConsumableKind kind,
            int itemCountBefore,
            int itemCountAfter)
        {
            return new InventoryItemUseResult(
                status,
                itemId,
                kind,
                itemCountBefore,
                itemCountAfter,
                HungerLevel,
                HungerLevel,
                StressLevel,
                StressLevel,
                IntoxicationLevel,
                IntoxicationLevel,
                DrinksConsumed,
                DrinksConsumed,
                0);
        }

        private static void LogInventoryItemUse(
            InventoryItemUseResult result)
        {
            GameLog.Info(
                "inventory",
                "item_use_resolved",
                GameLog.Field("accepted", result.Succeeded),
                GameLog.Field("status", result.Status.ToString()),
                GameLog.Field("item_id", result.ItemId.ToString()),
                GameLog.Field("kind", result.Kind.ToString()),
                GameLog.Field(
                    "item_count_before",
                    result.ItemCountBefore),
                GameLog.Field(
                    "item_count_after",
                    result.ItemCountAfter),
                GameLog.Field(
                    "hunger_relief",
                    result.ActualHungerRelief),
                GameLog.Field(
                    "stress_relief",
                    result.ActualStressRelief),
                GameLog.Field(
                    "intoxication_gain",
                    result.ActualIntoxicationGain));
        }

        private static void LogDrinkPurchase(
            DrinkPurchaseResult result)
        {
            GameLog.Info(
                "session",
                "drink_purchase_resolved",
                GameLog.Field("accepted", result.Succeeded),
                GameLog.Field("status", result.Status.ToString()),
                GameLog.Field(
                    "drink",
                    result.RequestedDrink.ToString()),
                GameLog.Field("price", result.Offer.Price),
                GameLog.Field("cash_before", result.CashBefore),
                GameLog.Field("cash_after", result.CashAfter),
                GameLog.Field(
                    "intoxication_before",
                    result.IntoxicationBefore),
                GameLog.Field(
                    "intoxication_after",
                    result.IntoxicationAfter),
                GameLog.Field(
                    "actual_intoxication_delta",
                    result.ActualIntoxicationDelta),
                GameLog.Field(
                    "drinks_before",
                    result.DrinksConsumedBefore),
                GameLog.Field(
                    "drinks_after",
                    result.DrinksConsumedAfter));
        }

        private static void LogWorldItemPurchase(
            SupermarketPurchaseResult result)
        {
            GameLog.Info(
                "session",
                "world_item_purchase_resolved",
                GameLog.Field("accepted", result.Succeeded),
                GameLog.Field("status", result.Status.ToString()),
                GameLog.Field("source_id", result.SourceId),
                GameLog.Field(
                    "item_id",
                    result.RequestedItemId.ToString()),
                GameLog.Field("price", result.Offer.Price),
                GameLog.Field("cash_before", result.CashBefore),
                GameLog.Field("cash_after", result.CashAfter),
                GameLog.Field(
                    "item_count_before",
                    result.ItemCountBefore),
                GameLog.Field(
                    "item_count_after",
                    result.ItemCountAfter));
        }

        private static string FormatRoute()
        {
            return string.Join(",", plannedBarRoute);
        }
    }
}
