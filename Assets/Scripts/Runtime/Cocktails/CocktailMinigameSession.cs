using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public sealed class CocktailMinigameSession
    {
        public const int RoundLimit = 3;
        public const int MinimumAdditions = 2;
        public const int MaximumAdditions = 4;
        public const int MaximumIntoxication = 100;
        public const int BadIngredientScorePenalty = 15;
        public const int BadIngredientIntoxicationPenalty = 10;

        private static readonly int[] scoreByGoodIngredientCount =
        {
            0,
            10,
            25,
            45,
            70,
            100
        };

        private readonly List<CocktailIngredientId> roundIngredients =
            new List<CocktailIngredientId>(MaximumAdditions + 1);
        private readonly ReadOnlyCollection<CocktailIngredientId>
            roundIngredientsView;
        private readonly List<CocktailIngredientId> compatibleIngredients =
            new List<CocktailIngredientId>(MaximumAdditions + 1);

        private CocktailIngredientId[] offers =
            Array.Empty<CocktailIngredientId>();
        private int badIngredientCount;

        public CocktailMinigameSession(
            int citySeed,
            string barId,
            int initialIntoxication,
            DrinkId lastAlcoholicDrink,
            int cocktailsConsumed)
        {
            if (initialIntoxication < 0 ||
                initialIntoxication > MaximumIntoxication)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialIntoxication));
            }

            if (cocktailsConsumed < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cocktailsConsumed));
            }

            if (lastAlcoholicDrink == DrinkId.Water)
            {
                lastAlcoholicDrink = DrinkId.None;
            }
            else if (lastAlcoholicDrink != DrinkId.None &&
                     !CocktailRules.TryFromPersistentDrinkId(
                         lastAlcoholicDrink,
                         out _))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lastAlcoholicDrink));
            }

            CitySeed = citySeed;
            BarId = barId ?? string.Empty;
            Intoxication = initialIntoxication;
            LastAlcoholicDrink = lastAlcoholicDrink;
            CocktailsConsumed = cocktailsConsumed;
            roundIngredientsView = roundIngredients.AsReadOnly();

            bool startsWasted = initialIntoxication >= MaximumIntoxication;
            Outcome = startsWasted
                ? CocktailSessionOutcome.Wasted
                : CocktailSessionOutcome.InProgress;
            Phase = startsWasted
                ? CocktailRoundPhase.Finished
                : CocktailRoundPhase.AwaitingBase;
            HasPendingWastedDebuff = startsWasted;
        }

        public int CitySeed { get; }
        public string BarId { get; }
        public int Intoxication { get; private set; }
        public DrinkId LastAlcoholicDrink { get; private set; }
        public int CocktailsConsumed { get; private set; }
        public int RoundsCompleted { get; private set; }
        public int TotalScore { get; private set; }
        public CocktailBaseId CurrentBase { get; private set; }
        public CocktailRoundPhase Phase { get; private set; }
        public CocktailSessionOutcome Outcome { get; private set; }
        public bool HasPendingWastedDebuff { get; private set; }
        public bool IsFinished =>
            Outcome != CocktailSessionOutcome.InProgress;
        public int CurrentRoundNumber =>
            IsFinished
                ? Math.Min(RoundLimit, Math.Max(1, RoundsCompleted))
                : RoundsCompleted + 1;
        public IReadOnlyList<CocktailIngredientId> RoundIngredients =>
            roundIngredientsView;
        public int AdditionCount =>
            Phase == CocktailRoundPhase.Mixing
                ? Math.Max(0, roundIngredients.Count - 1)
                : 0;
        public int GoodIngredientCount => compatibleIngredients.Count;
        public int BadIngredientCount => badIngredientCount;
        public int CurrentRoundScore =>
            Phase == CocktailRoundPhase.Mixing
                ? CalculateScore(
                    GoodIngredientCount,
                    BadIngredientCount)
                : 0;
        public bool CanServe =>
            Phase == CocktailRoundPhase.Mixing &&
            AdditionCount >= MinimumAdditions;
        public bool MustServe =>
            Phase == CocktailRoundPhase.Mixing &&
            AdditionCount >= MaximumAdditions;

        public CocktailIngredientId[] BeginRound(CocktailBaseId baseId)
        {
            RequireInProgress();
            if (Phase != CocktailRoundPhase.AwaitingBase)
            {
                throw new InvalidOperationException(
                    "A cocktail round has already started.");
            }

            CocktailBaseDefinition baseDefinition =
                CocktailRules.GetBaseDefinition(baseId);
            CurrentBase = baseId;
            roundIngredients.Clear();
            compatibleIngredients.Clear();
            badIngredientCount = 0;
            roundIngredients.Add(baseDefinition.IngredientId);
            compatibleIngredients.Add(baseDefinition.IngredientId);
            offers = CocktailOfferGenerator.Generate(
                CitySeed,
                BarId,
                CocktailsConsumed,
                RoundsCompleted + 1,
                baseId);
            Phase = CocktailRoundPhase.Mixing;
            return CopyOffers();
        }

        public CocktailIngredientId[] GetCurrentOffers()
        {
            return Phase == CocktailRoundPhase.Mixing
                ? CopyOffers()
                : Array.Empty<CocktailIngredientId>();
        }

        public CocktailIngredientSelectionResult AddIngredient(
            CocktailIngredientId ingredientId)
        {
            RequireInProgress();
            if (Phase != CocktailRoundPhase.Mixing)
            {
                throw new InvalidOperationException(
                    "Choose a cocktail base before adding ingredients.");
            }

            if (AdditionCount >= MaximumAdditions)
            {
                throw new InvalidOperationException(
                    "The cocktail already contains the maximum additions.");
            }

            if (Array.IndexOf(offers, ingredientId) < 0)
            {
                throw new ArgumentException(
                    "The ingredient is not part of the current offer.",
                    nameof(ingredientId));
            }

            if (roundIngredients.Contains(ingredientId))
            {
                throw new InvalidOperationException(
                    "An ingredient cannot be poured twice.");
            }

            int previousScore = CurrentRoundScore;
            bool compatible = CocktailRules.IsCompatibleWithAll(
                ingredientId,
                compatibleIngredients);
            roundIngredients.Add(ingredientId);
            if (compatible)
            {
                compatibleIngredients.Add(ingredientId);
            }
            else
            {
                badIngredientCount++;
            }

            return new CocktailIngredientSelectionResult(
                ingredientId,
                compatible,
                GoodIngredientCount,
                BadIngredientCount,
                AdditionCount,
                previousScore,
                CurrentRoundScore,
                CanServe,
                MustServe);
        }

        public CocktailRoundResult Serve()
        {
            RequireInProgress();
            if (!CanServe)
            {
                throw new InvalidOperationException(
                    $"A cocktail needs at least {MinimumAdditions} additions.");
            }

            int roundNumber = RoundsCompleted + 1;
            int previousIntoxication = Intoxication;
            int alcoholGain = 0;
            DrinkId resultingLastAlcohol = LastAlcoholicDrink;
            foreach (CocktailIngredientId ingredientId in roundIngredients)
            {
                CocktailIngredientDefinition definition =
                    CocktailRules.GetDefinition(ingredientId);
                alcoholGain += definition.IntoxicationGain;
                if (definition.IsAlcoholic)
                {
                    resultingLastAlcohol = definition.PersistentDrinkId;
                }
            }

            int badMixPenalty =
                badIngredientCount * BadIngredientIntoxicationPenalty;
            Intoxication = Math.Min(
                MaximumIntoxication,
                Intoxication + alcoholGain + badMixPenalty);
            LastAlcoholicDrink = resultingLastAlcohol;
            CocktailsConsumed++;
            RoundsCompleted++;

            int roundScore = CurrentRoundScore;
            TotalScore += roundScore;
            bool roundRequiresDebuff =
                badIngredientCount > 0 ||
                Intoxication >= MaximumIntoxication;
            HasPendingWastedDebuff |= roundRequiresDebuff;

            if (Intoxication >= MaximumIntoxication)
            {
                Outcome = CocktailSessionOutcome.Wasted;
            }
            else if (RoundsCompleted >= RoundLimit)
            {
                Outcome = CocktailSessionOutcome.Completed;
            }

            var result = new CocktailRoundResult(
                roundNumber,
                CurrentBase,
                roundIngredients,
                GoodIngredientCount,
                BadIngredientCount,
                roundScore,
                previousIntoxication,
                alcoholGain,
                badMixPenalty,
                Intoxication,
                LastAlcoholicDrink,
                roundRequiresDebuff,
                Outcome);

            ResetRound(Outcome == CocktailSessionOutcome.InProgress);
            return result;
        }

        public static int CalculateScore(
            int goodIngredientCount,
            int badIngredientCount)
        {
            if (goodIngredientCount < 1 ||
                goodIngredientCount >= scoreByGoodIngredientCount.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(goodIngredientCount));
            }

            if (badIngredientCount < 0 ||
                goodIngredientCount + badIngredientCount >
                MaximumAdditions + 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(badIngredientCount));
            }

            return Math.Max(
                0,
                scoreByGoodIngredientCount[goodIngredientCount] -
                badIngredientCount * BadIngredientScorePenalty);
        }

        private void ResetRound(bool allowNextRound)
        {
            CurrentBase = CocktailBaseId.None;
            offers = Array.Empty<CocktailIngredientId>();
            roundIngredients.Clear();
            compatibleIngredients.Clear();
            badIngredientCount = 0;
            Phase = allowNextRound
                ? CocktailRoundPhase.AwaitingBase
                : CocktailRoundPhase.Finished;
        }

        private CocktailIngredientId[] CopyOffers()
        {
            var copy = new CocktailIngredientId[offers.Length];
            Array.Copy(offers, copy, offers.Length);
            return copy;
        }

        private void RequireInProgress()
        {
            if (IsFinished)
            {
                throw new InvalidOperationException(
                    "A finished cocktail session cannot be changed.");
            }
        }
    }
}
