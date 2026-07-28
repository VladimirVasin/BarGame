using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class DrinkRulesTests
    {
        private static readonly object[] definitions =
        {
            new object[] { DrinkId.LightBeer, DrinkFamily.Beer, 8 },
            new object[] { DrinkId.DarkBeer, DrinkFamily.Beer, 10 },
            new object[] { DrinkId.WhiteWine, DrinkFamily.Wine, 11 },
            new object[] { DrinkId.RedWine, DrinkFamily.Wine, 13 },
            new object[] { DrinkId.Vodka, DrinkFamily.Vodka, 18 },
            new object[] { DrinkId.PepperVodka, DrinkFamily.Vodka, 20 },
            new object[] { DrinkId.CognacVs, DrinkFamily.Cognac, 16 },
            new object[] { DrinkId.CognacVsop, DrinkFamily.Cognac, 18 },
            new object[] { DrinkId.Water, DrinkFamily.Water, 0 },
            new object[] { DrinkId.Moonshine, DrinkFamily.Vodka, 24 }
        };

        [TestCaseSource(nameof(definitions))]
        public void Definitions_HaveExpectedFamilyAndGain(
            DrinkId id,
            DrinkFamily expectedFamily,
            int expectedGain)
        {
            DrinkDefinition definition = DrinkRules.GetDefinition(id);

            Assert.That(definition.Id, Is.EqualTo(id));
            Assert.That(definition.Family, Is.EqualTo(expectedFamily));
            Assert.That(definition.IntoxicationGain, Is.EqualTo(expectedGain));
            Assert.That(DrinkRules.GetFamily(id), Is.EqualTo(expectedFamily));
            Assert.That(DrinkRules.GetIntoxicationGain(id), Is.EqualTo(expectedGain));
        }

        [Test]
        public void Definitions_ExposeEverySelectableDrinkExactlyOnce()
        {
            var actual = new HashSet<DrinkId>();
            foreach (DrinkDefinition definition in DrinkRules.Definitions)
            {
                Assert.That(actual.Add(definition.Id), Is.True);
                Assert.That(definition.Id, Is.Not.EqualTo(DrinkId.None));
            }

            Assert.That(actual.Count, Is.EqualTo(10));
        }

        [TestCase(DrinkId.LightBeer, DrinkId.DarkBeer)]
        [TestCase(DrinkId.WhiteWine, DrinkId.RedWine)]
        [TestCase(DrinkId.Vodka, DrinkId.PepperVodka)]
        [TestCase(DrinkId.Vodka, DrinkId.Moonshine)]
        [TestCase(DrinkId.CognacVs, DrinkId.CognacVsop)]
        [TestCase(DrinkId.WhiteWine, DrinkId.CognacVs)]
        [TestCase(DrinkId.RedWine, DrinkId.CognacVsop)]
        [TestCase(DrinkId.Water, DrinkId.PepperVodka)]
        [TestCase(DrinkId.None, DrinkId.DarkBeer)]
        public void CompatiblePairs_AreSymmetric(
            DrinkId first,
            DrinkId second)
        {
            Assert.That(DrinkRules.AreCompatible(first, second), Is.True);
            Assert.That(DrinkRules.AreCompatible(second, first), Is.True);
        }

        [TestCase(DrinkId.LightBeer, DrinkId.WhiteWine)]
        [TestCase(DrinkId.DarkBeer, DrinkId.Vodka)]
        [TestCase(DrinkId.LightBeer, DrinkId.CognacVs)]
        [TestCase(DrinkId.WhiteWine, DrinkId.PepperVodka)]
        [TestCase(DrinkId.Vodka, DrinkId.CognacVsop)]
        public void IncompatiblePairs_AreSymmetric(
            DrinkId first,
            DrinkId second)
        {
            Assert.That(DrinkRules.AreCompatible(first, second), Is.False);
            Assert.That(DrinkRules.AreCompatible(second, first), Is.False);
        }

        [Test]
        public void EveryDrink_IsCompatibleWithItsOwnFamily()
        {
            foreach (DrinkDefinition first in DrinkRules.Definitions)
            {
                foreach (DrinkDefinition second in DrinkRules.Definitions)
                {
                    if (first.Family == second.Family)
                    {
                        Assert.That(
                            DrinkRules.AreCompatible(first.Id, second.Id),
                            Is.True,
                            $"{first.Id} and {second.Id}");
                    }
                }
            }
        }

        [Test]
        public void InvalidDrinkId_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkRules.GetDefinition((DrinkId)999));
        }
    }
}
