using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class CocktailRulesTests
    {
        private static readonly object[] persistentMappings =
        {
            new object[] { DrinkId.LightBeer, CocktailBaseId.Beer },
            new object[] { DrinkId.DarkBeer, CocktailBaseId.Beer },
            new object[] { DrinkId.WhiteWine, CocktailBaseId.Wine },
            new object[] { DrinkId.RedWine, CocktailBaseId.Wine },
            new object[] { DrinkId.Vodka, CocktailBaseId.Vodka },
            new object[] { DrinkId.PepperVodka, CocktailBaseId.Vodka },
            new object[] { DrinkId.CognacVs, CocktailBaseId.Cognac },
            new object[] { DrinkId.CognacVsop, CocktailBaseId.Cognac }
        };

        [Test]
        public void Definitions_ExposeEveryIngredientExactlyOnce()
        {
            var ids = new HashSet<CocktailIngredientId>();
            foreach (CocktailIngredientDefinition definition
                     in CocktailRules.Definitions)
            {
                Assert.That(ids.Add(definition.Id), Is.True);
                Assert.That(
                    definition.Id,
                    Is.Not.EqualTo(CocktailIngredientId.None));
            }

            Assert.That(ids.Count, Is.EqualTo(15));
        }

        [Test]
        public void BaseDefinitions_ExposeExpectedPersistentValues()
        {
            Assert.That(CocktailRules.BaseDefinitions, Has.Count.EqualTo(4));
            AssertBase(CocktailBaseId.Beer, DrinkId.LightBeer, 8);
            AssertBase(CocktailBaseId.Wine, DrinkId.RedWine, 13);
            AssertBase(CocktailBaseId.Vodka, DrinkId.Vodka, 18);
            AssertBase(CocktailBaseId.Cognac, DrinkId.CognacVs, 16);
        }

        [TestCaseSource(nameof(persistentMappings))]
        public void PersistentDrinkVariants_MapToCocktailBase(
            DrinkId drinkId,
            CocktailBaseId expectedBase)
        {
            bool mapped = CocktailRules.TryFromPersistentDrinkId(
                drinkId,
                out CocktailBaseId actualBase);

            Assert.That(mapped, Is.True);
            Assert.That(actualBase, Is.EqualTo(expectedBase));
        }

        [TestCase(
            CocktailIngredientId.Beer,
            CocktailIngredientId.GingerAle,
            true)]
        [TestCase(
            CocktailIngredientId.Wine,
            CocktailIngredientId.Cognac,
            true)]
        [TestCase(
            CocktailIngredientId.Vodka,
            CocktailIngredientId.Tonic,
            true)]
        [TestCase(
            CocktailIngredientId.Wine,
            CocktailIngredientId.Vodka,
            false)]
        [TestCase(
            CocktailIngredientId.Cognac,
            CocktailIngredientId.Beer,
            false)]
        [TestCase(
            CocktailIngredientId.Cognac,
            CocktailIngredientId.Tonic,
            false)]
        public void Compatibility_IsSymmetric(
            CocktailIngredientId first,
            CocktailIngredientId second,
            bool expected)
        {
            Assert.That(
                CocktailRules.AreCompatible(first, second),
                Is.EqualTo(expected));
            Assert.That(
                CocktailRules.AreCompatible(second, first),
                Is.EqualTo(expected));
        }

        [Test]
        public void NonAlcoholicIngredients_AreMutuallyCompatible()
        {
            Assert.That(
                CocktailRules.AreCompatible(
                    CocktailIngredientId.Tonic,
                    CocktailIngredientId.Cherry),
                Is.True);
        }

        [Test]
        public void Candidate_MustMatchEveryCompatibleAlcoholInGlass()
        {
            CocktailIngredientId[] existing =
            {
                CocktailIngredientId.Wine,
                CocktailIngredientId.Cognac
            };

            Assert.That(
                CocktailRules.IsCompatibleWithAll(
                    CocktailIngredientId.Orange,
                    existing),
                Is.True);
            Assert.That(
                CocktailRules.IsCompatibleWithAll(
                    CocktailIngredientId.Cola,
                    existing),
                Is.False);
        }

        [Test]
        public void NoneAndUnknownValues_AreRejectedOrIncompatible()
        {
            Assert.That(
                CocktailRules.AreCompatible(
                    CocktailIngredientId.None,
                    CocktailIngredientId.Ice),
                Is.False);
            Assert.That(
                CocktailRules.TryFromPersistentDrinkId(
                    DrinkId.Water,
                    out CocktailBaseId baseId),
                Is.False);
            Assert.That(baseId, Is.EqualTo(CocktailBaseId.None));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CocktailRules.GetDefinition(
                    (CocktailIngredientId)999));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CocktailRules.GetBaseDefinition(
                    CocktailBaseId.None));
        }

        private static void AssertBase(
            CocktailBaseId baseId,
            DrinkId persistentDrinkId,
            int intoxicationGain)
        {
            CocktailBaseDefinition definition =
                CocktailRules.GetBaseDefinition(baseId);
            CocktailIngredientDefinition ingredient =
                CocktailRules.GetDefinition(definition.IngredientId);

            Assert.That(definition.Id, Is.EqualTo(baseId));
            Assert.That(
                definition.PersistentDrinkId,
                Is.EqualTo(persistentDrinkId));
            Assert.That(
                definition.IntoxicationGain,
                Is.EqualTo(intoxicationGain));
            Assert.That(ingredient.IsAlcoholic, Is.True);
            Assert.That(ingredient.AlcoholBase, Is.EqualTo(baseId));
            Assert.That(
                ingredient.PersistentDrinkId,
                Is.EqualTo(persistentDrinkId));
            Assert.That(
                ingredient.IntoxicationGain,
                Is.EqualTo(intoxicationGain));
            Assert.That(
                CocktailRules.ToPersistentDrinkId(baseId),
                Is.EqualTo(persistentDrinkId));
        }
    }
}
