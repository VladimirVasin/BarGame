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

    public static class GameSessionState
    {
        public const int DefaultCitySeed = 20260727;
        public const int DefaultCash = 999;

        private static readonly List<string> plannedBarRoute =
            new List<string>();
        private static readonly ReadOnlyCollection<string> plannedBarRouteView =
            plannedBarRoute.AsReadOnly();
        private static readonly HashSet<string> visitedBars =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> collectedWorldItems =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly InventoryState inventory =
            new InventoryState();
        private static float intoxicationRecoveryElapsed;

        public static int CitySeed { get; private set; } = DefaultCitySeed;
        public static string ActiveBarId { get; private set; } = string.Empty;
        public static BarActivityKind ActiveBarActivity { get; private set; } =
            BarActivityKind.None;
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
        public static int IntoxicationLevel { get; private set; }
        public static DrinkId LastAlcoholicDrink { get; private set; } = DrinkId.None;
        public static int DrinksConsumed { get; private set; }
        public static int CashBalance { get; private set; } = DefaultCash;
        public static float BalanceCheckDelayRemaining { get; private set; }
        public static int BalanceCheckSequence { get; private set; }
        public static IReadOnlyList<string> PlannedBarRoute =>
            plannedBarRouteView;
        public static int VisitedBarCount => visitedBars.Count;
        public static IReadOnlyList<InventoryItemStack> InventoryItems =>
            inventory.Items;
        public static int CollectedWorldItemCount =>
            collectedWorldItems.Count;

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
                GameLog.Field("cash_balance", CashBalance),
                GameLog.Field(
                    "home_arrival",
                    HomeArrival.ToString()));
        }

        private static void ResetToDefaults()
        {
            CitySeed = DefaultCitySeed;
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            ReturnKind = CityReturnKind.None;
            StairwellArrival = StairwellArrivalKind.StreetDoor;
            HomeArrival = HomeArrivalKind.Normal;
            IntoxicationLevel = 0;
            intoxicationRecoveryElapsed = 0f;
            LastAlcoholicDrink = DrinkId.None;
            DrinksConsumed = 0;
            CashBalance = DefaultCash;
            BalanceCheckDelayRemaining = 0f;
            BalanceCheckSequence = 0;
            plannedBarRoute.Clear();
            visitedBars.Clear();
            collectedWorldItems.Clear();
            inventory.ResetWithStarterItems();
            GameLog.SetCitySeed(CitySeed);
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
            int clearedVisitedCount = visitedBars.Count;
            CitySeed = seed;
            GameLog.SetCitySeed(seed);
            ClearRoute();
            ClearVisitedBars();
            GameLog.Info(
                "session",
                "city_seed_changed",
                GameLog.Field("previous_seed", previousSeed),
                GameLog.Field("new_seed", CitySeed),
                GameLog.Field(
                    "cleared_route_count",
                    clearedRouteCount),
                GameLog.Field(
                    "cleared_visited_count",
                    clearedVisitedCount));
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

        public static bool MarkBarVisited(string barId)
        {
            if (string.IsNullOrWhiteSpace(barId))
            {
                return false;
            }

            bool firstVisit = visitedBars.Add(barId);
            bool removedFromRoute = RemoveRouteStop(barId);
            if (firstVisit || removedFromRoute)
            {
                GameLog.Info(
                    "session",
                    "bar_visited",
                    GameLog.Field("bar_id", barId),
                    GameLog.Field("first_visit", firstVisit),
                    GameLog.Field(
                        "removed_from_route",
                        removedFromRoute),
                    GameLog.Field(
                        "visited_count",
                        visitedBars.Count));
            }

            return firstVisit;
        }

        public static bool IsBarVisited(string barId)
        {
            return !string.IsNullOrWhiteSpace(barId) &&
                   visitedBars.Contains(barId);
        }

        public static void ClearVisitedBars()
        {
            if (visitedBars.Count == 0)
            {
                return;
            }

            int previousCount = visitedBars.Count;
            visitedBars.Clear();
            GameLog.Info(
                "session",
                "visited_bars_cleared",
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
            string nextBarId = barId ?? string.Empty;
            BarActivityKind nextActivity =
                string.IsNullOrEmpty(nextBarId)
                ? BarActivityKind.None
                : NormalizeBarActivity(barActivity);
            if (string.Equals(
                    ActiveBarId,
                    nextBarId,
                    StringComparison.Ordinal) &&
                ActiveBarActivity == nextActivity &&
                ReturnKind == CityReturnKind.None)
            {
                return;
            }

            ActiveBarId = nextBarId;
            ActiveBarActivity = nextActivity;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "bar_entered",
                GameLog.Field("bar_id", ActiveBarId),
                GameLog.Field(
                    "activity",
                    ActiveBarActivity.ToString()));
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
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "home_entered");
        }

        public static void EnterSupermarket()
        {
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            ReturnKind = CityReturnKind.None;
            GameLog.Info(
                "session",
                "supermarket_entered");
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
                UpdateDrinkingProgress(
                    result.IntoxicationAfter,
                    result.LastAlcoholicDrinkAfter,
                    result.DrinksConsumedAfter);
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
            return BarMinigameCatalog.NormalizeActivity(barActivity);
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
