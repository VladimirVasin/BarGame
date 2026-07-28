using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public enum CocktailBaseId
    {
        None = 0,
        Beer,
        Wine,
        Vodka,
        Cognac
    }

    public enum CocktailIngredientId
    {
        None = 0,
        Beer,
        Wine,
        Vodka,
        Cognac,
        Tonic,
        Soda,
        Cola,
        Orange,
        Lemon,
        GingerAle,
        Honey,
        Mint,
        Berries,
        Cherry,
        Ice
    }

    public enum CocktailIngredientKind
    {
        None = 0,
        Alcohol,
        Mixer,
        Fruit,
        Sweetener,
        Herb,
        Ice
    }

    public enum CocktailRoundPhase
    {
        AwaitingBase = 0,
        Mixing,
        Finished
    }

    public enum CocktailSessionOutcome
    {
        InProgress = 0,
        Completed,
        MaxIntoxicationReached
    }

    public readonly struct CocktailBaseDefinition
    {
        public CocktailBaseDefinition(
            CocktailBaseId id,
            CocktailIngredientId ingredientId,
            DrinkId persistentDrinkId,
            int intoxicationGain)
        {
            Id = id;
            IngredientId = ingredientId;
            PersistentDrinkId = persistentDrinkId;
            IntoxicationGain = intoxicationGain;
        }

        public CocktailBaseId Id { get; }
        public CocktailIngredientId IngredientId { get; }
        public DrinkId PersistentDrinkId { get; }
        public int IntoxicationGain { get; }
    }

    public readonly struct CocktailIngredientDefinition
    {
        public CocktailIngredientDefinition(
            CocktailIngredientId id,
            CocktailIngredientKind kind,
            CocktailBaseId alcoholBase,
            DrinkId persistentDrinkId,
            int intoxicationGain)
        {
            Id = id;
            Kind = kind;
            AlcoholBase = alcoholBase;
            PersistentDrinkId = persistentDrinkId;
            IntoxicationGain = intoxicationGain;
        }

        public CocktailIngredientId Id { get; }
        public CocktailIngredientKind Kind { get; }
        public CocktailBaseId AlcoholBase { get; }
        public DrinkId PersistentDrinkId { get; }
        public int IntoxicationGain { get; }
        public bool IsAlcoholic => Kind == CocktailIngredientKind.Alcohol;
    }

    public readonly struct CocktailIngredientSelectionResult
    {
        internal CocktailIngredientSelectionResult(
            CocktailIngredientId ingredientId,
            bool wasCompatible,
            int goodIngredientCount,
            int badIngredientCount,
            int additionCount,
            int previousScore,
            int currentScore,
            bool canServe,
            bool mustServe)
        {
            IngredientId = ingredientId;
            WasCompatible = wasCompatible;
            GoodIngredientCount = goodIngredientCount;
            BadIngredientCount = badIngredientCount;
            AdditionCount = additionCount;
            PreviousScore = previousScore;
            CurrentScore = currentScore;
            CanServe = canServe;
            MustServe = mustServe;
        }

        public CocktailIngredientId IngredientId { get; }
        public bool WasCompatible { get; }
        public int GoodIngredientCount { get; }
        public int BadIngredientCount { get; }
        public int AdditionCount { get; }
        public int PreviousScore { get; }
        public int CurrentScore { get; }
        public int ScoreDelta => CurrentScore - PreviousScore;
        public bool CanServe { get; }
        public bool MustServe { get; }
    }

    public readonly struct CocktailRoundResult
    {
        private readonly ReadOnlyCollection<CocktailIngredientId> ingredients;

        internal CocktailRoundResult(
            int roundNumber,
            CocktailBaseId baseId,
            IList<CocktailIngredientId> roundIngredients,
            int goodIngredientCount,
            int badIngredientCount,
            int score,
            int previousIntoxication,
            int alcoholIntoxicationGain,
            int badMixIntoxicationPenalty,
            int currentIntoxication,
            DrinkId lastAlcoholicDrink,
            CocktailSessionOutcome sessionOutcome)
        {
            var ingredientCopy = new CocktailIngredientId[roundIngredients.Count];
            roundIngredients.CopyTo(ingredientCopy, 0);

            RoundNumber = roundNumber;
            BaseId = baseId;
            ingredients = Array.AsReadOnly(ingredientCopy);
            GoodIngredientCount = goodIngredientCount;
            BadIngredientCount = badIngredientCount;
            Score = score;
            PreviousIntoxication = previousIntoxication;
            AlcoholIntoxicationGain = alcoholIntoxicationGain;
            BadMixIntoxicationPenalty = badMixIntoxicationPenalty;
            CurrentIntoxication = currentIntoxication;
            LastAlcoholicDrink = lastAlcoholicDrink;
            SessionOutcome = sessionOutcome;
        }

        public int RoundNumber { get; }
        public CocktailBaseId BaseId { get; }
        public IReadOnlyList<CocktailIngredientId> Ingredients => ingredients;
        public int GoodIngredientCount { get; }
        public int BadIngredientCount { get; }
        public int Score { get; }
        public int PreviousIntoxication { get; }
        public int AlcoholIntoxicationGain { get; }
        public int BadMixIntoxicationPenalty { get; }
        public int IntoxicationDelta =>
            CurrentIntoxication - PreviousIntoxication;
        public int CurrentIntoxication { get; }
        public DrinkId LastAlcoholicDrink { get; }
        public bool HasBadMix => BadIngredientCount > 0;
        public CocktailSessionOutcome SessionOutcome { get; }
    }
}
