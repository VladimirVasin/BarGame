using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class CocktailOfferGeneratorTests
    {
        [Test]
        public void Generate_WithSameContext_IsDeterministic()
        {
            CocktailIngredientId[] first = CocktailOfferGenerator.Generate(
                20260727,
                "bar-02",
                9,
                2,
                CocktailBaseId.Vodka);
            CocktailIngredientId[] second = CocktailOfferGenerator.Generate(
                20260727,
                "bar-02",
                9,
                2,
                CocktailBaseId.Vodka);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void Generate_AlwaysReturnsFourCompatibleAndThreeTraps()
        {
            foreach (CocktailBaseDefinition baseDefinition
                     in CocktailRules.BaseDefinitions)
            {
                for (int seed = -8; seed <= 8; seed++)
                {
                    for (int round = 1;
                         round <= CocktailMinigameSession.RoundLimit;
                         round++)
                    {
                        CocktailIngredientId[] offer =
                            CocktailOfferGenerator.Generate(
                                seed,
                                $"bar-{seed}",
                                seed + 8,
                                round,
                                baseDefinition.Id);

                        Assert.That(
                            offer,
                            Has.Length.EqualTo(
                                CocktailOfferGenerator.OfferSize));
                        Assert.That(
                            offer.Distinct().Count(),
                            Is.EqualTo(offer.Length));
                        Assert.That(
                            offer,
                            Has.None.EqualTo(baseDefinition.IngredientId));
                        Assert.That(
                            offer.Count(candidate =>
                                CocktailRules.AreCompatible(
                                    baseDefinition.Id,
                                    candidate)),
                            Is.EqualTo(
                                CocktailOfferGenerator.CompatibleOfferCount));
                        Assert.That(
                            offer.Count(candidate =>
                                !CocktailRules.AreCompatible(
                                    baseDefinition.Id,
                                    candidate)),
                            Is.EqualTo(
                                CocktailOfferGenerator.TrapOfferCount));
                    }
                }
            }
        }

        [Test]
        public void Generate_UsesContextWithoutLosingItsContract()
        {
            var signatures = new HashSet<string>();
            for (int index = 0; index < 24; index++)
            {
                CocktailIngredientId[] offer =
                    CocktailOfferGenerator.Generate(
                        100 + index,
                        index % 2 == 0 ? "bar-a" : "bar-b",
                        index,
                        index % 3 + 1,
                        CocktailBaseId.Cognac);
                signatures.Add(string.Join(",", offer));
            }

            Assert.That(signatures.Count, Is.GreaterThan(1));
        }

        [Test]
        public void Generate_RejectsInvalidProgressAndBase()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CocktailOfferGenerator.Generate(
                    1,
                    "bar",
                    -1,
                    1,
                    CocktailBaseId.Beer));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CocktailOfferGenerator.Generate(
                    1,
                    "bar",
                    0,
                    0,
                    CocktailBaseId.Beer));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CocktailOfferGenerator.Generate(
                    1,
                    "bar",
                    0,
                    1,
                    CocktailBaseId.None));
        }
    }
}
