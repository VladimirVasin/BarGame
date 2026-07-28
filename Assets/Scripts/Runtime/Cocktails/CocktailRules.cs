using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public static class CocktailRules
    {
        private static readonly CocktailBaseDefinition[] baseDefinitions =
        {
            new CocktailBaseDefinition(
                CocktailBaseId.Beer,
                CocktailIngredientId.Beer,
                DrinkId.LightBeer,
                8),
            new CocktailBaseDefinition(
                CocktailBaseId.Wine,
                CocktailIngredientId.Wine,
                DrinkId.RedWine,
                13),
            new CocktailBaseDefinition(
                CocktailBaseId.Vodka,
                CocktailIngredientId.Vodka,
                DrinkId.Vodka,
                18),
            new CocktailBaseDefinition(
                CocktailBaseId.Cognac,
                CocktailIngredientId.Cognac,
                DrinkId.CognacVs,
                16)
        };

        private static readonly CocktailIngredientDefinition[] definitions =
        {
            Alcohol(
                CocktailIngredientId.Beer,
                CocktailBaseId.Beer,
                DrinkId.LightBeer,
                8),
            Alcohol(
                CocktailIngredientId.Wine,
                CocktailBaseId.Wine,
                DrinkId.RedWine,
                13),
            Alcohol(
                CocktailIngredientId.Vodka,
                CocktailBaseId.Vodka,
                DrinkId.Vodka,
                18),
            Alcohol(
                CocktailIngredientId.Cognac,
                CocktailBaseId.Cognac,
                DrinkId.CognacVs,
                16),
            NonAlcoholic(
                CocktailIngredientId.Tonic,
                CocktailIngredientKind.Mixer),
            NonAlcoholic(
                CocktailIngredientId.Soda,
                CocktailIngredientKind.Mixer),
            NonAlcoholic(
                CocktailIngredientId.Cola,
                CocktailIngredientKind.Mixer),
            NonAlcoholic(
                CocktailIngredientId.Orange,
                CocktailIngredientKind.Fruit),
            NonAlcoholic(
                CocktailIngredientId.Lemon,
                CocktailIngredientKind.Fruit),
            NonAlcoholic(
                CocktailIngredientId.GingerAle,
                CocktailIngredientKind.Mixer),
            NonAlcoholic(
                CocktailIngredientId.Honey,
                CocktailIngredientKind.Sweetener),
            NonAlcoholic(
                CocktailIngredientId.Mint,
                CocktailIngredientKind.Herb),
            NonAlcoholic(
                CocktailIngredientId.Berries,
                CocktailIngredientKind.Fruit),
            NonAlcoholic(
                CocktailIngredientId.Cherry,
                CocktailIngredientKind.Fruit),
            NonAlcoholic(
                CocktailIngredientId.Ice,
                CocktailIngredientKind.Ice)
        };

        private static readonly IReadOnlyList<CocktailBaseDefinition>
            readOnlyBaseDefinitions = Array.AsReadOnly(baseDefinitions);
        private static readonly IReadOnlyList<CocktailIngredientDefinition>
            readOnlyDefinitions = Array.AsReadOnly(definitions);

        public static IReadOnlyList<CocktailBaseDefinition> BaseDefinitions =>
            readOnlyBaseDefinitions;
        public static IReadOnlyList<CocktailIngredientDefinition> Definitions =>
            readOnlyDefinitions;

        public static CocktailBaseDefinition GetBaseDefinition(
            CocktailBaseId id)
        {
            switch (id)
            {
                case CocktailBaseId.Beer:
                    return baseDefinitions[0];
                case CocktailBaseId.Wine:
                    return baseDefinitions[1];
                case CocktailBaseId.Vodka:
                    return baseDefinitions[2];
                case CocktailBaseId.Cognac:
                    return baseDefinitions[3];
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }
        }

        public static CocktailIngredientDefinition GetDefinition(
            CocktailIngredientId id)
        {
            int definitionIndex = (int)id - 1;
            if (definitionIndex < 0 || definitionIndex >= definitions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }

            return definitions[definitionIndex];
        }

        public static CocktailIngredientId GetBaseIngredient(
            CocktailBaseId baseId)
        {
            return GetBaseDefinition(baseId).IngredientId;
        }

        public static DrinkId ToPersistentDrinkId(CocktailBaseId baseId)
        {
            return GetBaseDefinition(baseId).PersistentDrinkId;
        }

        public static bool TryFromPersistentDrinkId(
            DrinkId drinkId,
            out CocktailBaseId baseId)
        {
            switch (drinkId)
            {
                case DrinkId.LightBeer:
                case DrinkId.DarkBeer:
                    baseId = CocktailBaseId.Beer;
                    return true;
                case DrinkId.WhiteWine:
                case DrinkId.RedWine:
                    baseId = CocktailBaseId.Wine;
                    return true;
                case DrinkId.Vodka:
                case DrinkId.PepperVodka:
                case DrinkId.Moonshine:
                    baseId = CocktailBaseId.Vodka;
                    return true;
                case DrinkId.CognacVs:
                case DrinkId.CognacVsop:
                    baseId = CocktailBaseId.Cognac;
                    return true;
                default:
                    baseId = CocktailBaseId.None;
                    return false;
            }
        }

        public static bool TryGetAlcoholBase(
            CocktailIngredientId ingredientId,
            out CocktailBaseId baseId)
        {
            if (ingredientId == CocktailIngredientId.None)
            {
                baseId = CocktailBaseId.None;
                return false;
            }

            CocktailIngredientDefinition definition =
                GetDefinition(ingredientId);
            baseId = definition.AlcoholBase;
            return definition.IsAlcoholic;
        }

        public static bool AreCompatible(
            CocktailBaseId baseId,
            CocktailIngredientId ingredientId)
        {
            return AreCompatible(
                GetBaseIngredient(baseId),
                ingredientId);
        }

        public static bool AreCompatible(
            CocktailIngredientId first,
            CocktailIngredientId second)
        {
            if (first == CocktailIngredientId.None ||
                second == CocktailIngredientId.None)
            {
                return false;
            }

            if (first == second)
            {
                return true;
            }

            bool firstIsAlcohol =
                TryGetAlcoholBase(first, out CocktailBaseId firstBase);
            bool secondIsAlcohol =
                TryGetAlcoholBase(second, out CocktailBaseId secondBase);
            if (firstIsAlcohol && secondIsAlcohol)
            {
                return firstBase == secondBase ||
                       IsWineAndCognac(firstBase, secondBase);
            }

            if (firstIsAlcohol)
            {
                return IsAdditionCompatible(firstBase, second);
            }

            if (secondIsAlcohol)
            {
                return IsAdditionCompatible(secondBase, first);
            }

            return true;
        }

        public static bool IsCompatibleWithAll(
            CocktailIngredientId candidate,
            IEnumerable<CocktailIngredientId> existingIngredients)
        {
            if (existingIngredients == null)
            {
                throw new ArgumentNullException(nameof(existingIngredients));
            }

            if (candidate == CocktailIngredientId.None)
            {
                return false;
            }

            foreach (CocktailIngredientId existing in existingIngredients)
            {
                if (!AreCompatible(existing, candidate))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAdditionCompatible(
            CocktailBaseId baseId,
            CocktailIngredientId addition)
        {
            switch (baseId)
            {
                case CocktailBaseId.Beer:
                    return addition == CocktailIngredientId.GingerAle ||
                           addition == CocktailIngredientId.Lemon ||
                           addition == CocktailIngredientId.Orange ||
                           addition == CocktailIngredientId.Honey ||
                           addition == CocktailIngredientId.Ice;
                case CocktailBaseId.Wine:
                    return addition == CocktailIngredientId.Soda ||
                           addition == CocktailIngredientId.Orange ||
                           addition == CocktailIngredientId.Lemon ||
                           addition == CocktailIngredientId.Berries ||
                           addition == CocktailIngredientId.Cherry ||
                           addition == CocktailIngredientId.Ice;
                case CocktailBaseId.Vodka:
                    return addition == CocktailIngredientId.Tonic ||
                           addition == CocktailIngredientId.Soda ||
                           addition == CocktailIngredientId.Orange ||
                           addition == CocktailIngredientId.Lemon ||
                           addition == CocktailIngredientId.Mint ||
                           addition == CocktailIngredientId.Berries ||
                           addition == CocktailIngredientId.Ice;
                case CocktailBaseId.Cognac:
                    return addition == CocktailIngredientId.Cola ||
                           addition == CocktailIngredientId.Orange ||
                           addition == CocktailIngredientId.Lemon ||
                           addition == CocktailIngredientId.GingerAle ||
                           addition == CocktailIngredientId.Honey ||
                           addition == CocktailIngredientId.Cherry ||
                           addition == CocktailIngredientId.Ice;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(baseId),
                        baseId,
                        null);
            }
        }

        private static bool IsWineAndCognac(
            CocktailBaseId first,
            CocktailBaseId second)
        {
            return
                (first == CocktailBaseId.Wine &&
                 second == CocktailBaseId.Cognac) ||
                (first == CocktailBaseId.Cognac &&
                 second == CocktailBaseId.Wine);
        }

        private static CocktailIngredientDefinition Alcohol(
            CocktailIngredientId id,
            CocktailBaseId alcoholBase,
            DrinkId persistentDrinkId,
            int intoxicationGain)
        {
            return new CocktailIngredientDefinition(
                id,
                CocktailIngredientKind.Alcohol,
                alcoholBase,
                persistentDrinkId,
                intoxicationGain);
        }

        private static CocktailIngredientDefinition NonAlcoholic(
            CocktailIngredientId id,
            CocktailIngredientKind kind)
        {
            return new CocktailIngredientDefinition(
                id,
                kind,
                CocktailBaseId.None,
                DrinkId.None,
                0);
        }
    }
}
