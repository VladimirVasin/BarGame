using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public static class GameSessionState
    {
        public const int DefaultCitySeed = 20260727;

        private static readonly List<string> plannedBarRoute =
            new List<string>();
        private static readonly ReadOnlyCollection<string> plannedBarRouteView =
            plannedBarRoute.AsReadOnly();
        private static readonly HashSet<string> visitedBars =
            new HashSet<string>(StringComparer.Ordinal);

        public static int CitySeed { get; private set; } = DefaultCitySeed;
        public static string ActiveBarId { get; private set; } = string.Empty;
        public static BarActivityKind ActiveBarActivity { get; private set; } =
            BarActivityKind.None;
        public static bool IsReturningToCity { get; private set; }
        public static int IntoxicationLevel { get; private set; }
        public static DrinkId LastAlcoholicDrink { get; private set; } = DrinkId.None;
        public static int DrinksConsumed { get; private set; }
        public static float BalanceCheckDelayRemaining { get; private set; }
        public static int BalanceCheckSequence { get; private set; }
        public static IReadOnlyList<string> PlannedBarRoute =>
            plannedBarRouteView;
        public static int VisitedBarCount => visitedBars.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            CitySeed = DefaultCitySeed;
            ActiveBarId = string.Empty;
            ActiveBarActivity = BarActivityKind.None;
            IsReturningToCity = false;
            ResetDrinkingState();
            ClearRoute();
            ClearVisitedBars();
        }

        public static void SetCitySeed(int seed)
        {
            if (CitySeed == seed)
            {
                return;
            }

            CitySeed = seed;
            ClearRoute();
            ClearVisitedBars();
        }

        public static bool TryAddRouteStop(string barId)
        {
            if (string.IsNullOrWhiteSpace(barId) ||
                plannedBarRoute.Contains(barId))
            {
                return false;
            }

            plannedBarRoute.Add(barId);
            return true;
        }

        public static bool RemoveRouteStop(string barId)
        {
            return !string.IsNullOrWhiteSpace(barId) &&
                   plannedBarRoute.Remove(barId);
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
            return true;
        }

        public static void ClearRoute()
        {
            plannedBarRoute.Clear();
        }

        public static bool MarkBarVisited(string barId)
        {
            if (string.IsNullOrWhiteSpace(barId))
            {
                return false;
            }

            bool firstVisit = visitedBars.Add(barId);
            RemoveRouteStop(barId);
            return firstVisit;
        }

        public static bool IsBarVisited(string barId)
        {
            return !string.IsNullOrWhiteSpace(barId) &&
                   visitedBars.Contains(barId);
        }

        public static void ClearVisitedBars()
        {
            visitedBars.Clear();
        }

        public static void EnterBar(string barId)
        {
            EnterBar(barId, BarActivityKind.Cocktail);
        }

        public static void EnterBar(
            string barId,
            BarActivityKind barActivity)
        {
            ActiveBarId = barId ?? string.Empty;
            ActiveBarActivity = string.IsNullOrEmpty(ActiveBarId)
                ? BarActivityKind.None
                : NormalizeBarActivity(barActivity);
            IsReturningToCity = false;
        }

        public static void PrepareCityReturn()
        {
            IsReturningToCity = !string.IsNullOrEmpty(ActiveBarId);
        }

        public static bool TryGetReturnBarId(out string barId)
        {
            barId = ActiveBarId;
            return IsReturningToCity && !string.IsNullOrEmpty(barId);
        }

        public static void CompleteCityReturn()
        {
            IsReturningToCity = false;
        }

        public static void UpdateDrinkingProgress(
            int intoxication,
            DrinkId lastDrink,
            int drinksConsumed)
        {
            IntoxicationLevel = Mathf.Clamp(intoxication, 0, 100);
            LastAlcoholicDrink = lastDrink;
            DrinksConsumed = Mathf.Max(0, drinksConsumed);
            if (IntoxicationLevel <=
                IntoxicationStageRules.BalanceThreshold)
            {
                BalanceCheckDelayRemaining = 0f;
            }
        }

        public static void SetBalanceCheckDelay(float seconds)
        {
            BalanceCheckDelayRemaining = Mathf.Max(0f, seconds);
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
            return sequence;
        }

        public static void ResetDrinkingState()
        {
            IntoxicationLevel = 0;
            LastAlcoholicDrink = DrinkId.None;
            DrinksConsumed = 0;
            BalanceCheckDelayRemaining = 0f;
            BalanceCheckSequence = 0;
        }

        private static BarActivityKind NormalizeBarActivity(
            BarActivityKind barActivity)
        {
            return BarMinigameCatalog.NormalizeActivity(barActivity);
        }
    }
}
