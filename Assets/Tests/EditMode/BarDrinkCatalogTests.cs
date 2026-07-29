using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class BarDrinkCatalogTests
    {
        private static readonly object[] expectedOffers =
        {
            new object[] { DrinkId.Water, "drink.water", 2 },
            new object[] { DrinkId.LightBeer, "drink.light_beer", 8 },
            new object[] { DrinkId.DarkBeer, "drink.dark_beer", 10 },
            new object[] { DrinkId.WhiteWine, "drink.white_wine", 12 },
            new object[] { DrinkId.RedWine, "drink.red_wine", 14 },
            new object[] { DrinkId.Vodka, "drink.vodka", 15 },
            new object[]
            {
                DrinkId.PepperVodka,
                "drink.pepper_vodka",
                18
            },
            new object[] { DrinkId.CognacVs, "drink.cognac_vs", 20 },
            new object[]
            {
                DrinkId.CognacVsop,
                "drink.cognac_vsop",
                25
            }
        };

        [Test]
        public void Offers_ExposeExactOrderedRetailCatalog()
        {
            IReadOnlyList<BarDrinkOffer> offers =
                BarDrinkCatalog.Offers;

            Assert.That(offers, Has.Count.EqualTo(expectedOffers.Length));
            var uniqueDrinks = new HashSet<DrinkId>();
            for (int index = 0; index < expectedOffers.Length; index++)
            {
                object[] expected = (object[])expectedOffers[index];
                DrinkId expectedDrink = (DrinkId)expected[0];
                string expectedNameKey = (string)expected[1];
                int expectedPrice = (int)expected[2];
                BarDrinkOffer offer = offers[index];

                Assert.That(offer.DrinkId, Is.EqualTo(expectedDrink));
                Assert.That(offer.NameKey, Is.EqualTo(expectedNameKey));
                Assert.That(offer.Price, Is.EqualTo(expectedPrice));
                Assert.That(offer.Price, Is.GreaterThan(0));
                Assert.That(uniqueDrinks.Add(offer.DrinkId), Is.True);
                Assert.That(
                    DrinkRules.GetDefinition(offer.DrinkId).Id,
                    Is.EqualTo(offer.DrinkId));
            }

            Assert.That(uniqueDrinks.Contains(DrinkId.None), Is.False);
            Assert.That(
                uniqueDrinks.Contains(DrinkId.Moonshine),
                Is.False);
        }

        [TestCaseSource(nameof(expectedOffers))]
        public void Lookup_ReturnsMatchingOffer(
            DrinkId drinkId,
            string expectedNameKey,
            int expectedPrice)
        {
            Assert.That(
                BarDrinkCatalog.TryGetOffer(
                    drinkId,
                    out BarDrinkOffer offer),
                Is.True);
            Assert.That(offer.DrinkId, Is.EqualTo(drinkId));
            Assert.That(offer.NameKey, Is.EqualTo(expectedNameKey));
            Assert.That(offer.Price, Is.EqualTo(expectedPrice));
            Assert.That(
                BarDrinkCatalog.GetOffer(drinkId),
                Is.EqualTo(offer));
        }

        [TestCase(DrinkId.None)]
        [TestCase(DrinkId.Moonshine)]
        [TestCase((DrinkId)999)]
        public void Lookup_RejectsNonRetailDrink(DrinkId drinkId)
        {
            Assert.That(
                BarDrinkCatalog.TryGetOffer(
                    drinkId,
                    out BarDrinkOffer offer),
                Is.False);
            Assert.That(offer, Is.EqualTo(default(BarDrinkOffer)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BarDrinkCatalog.GetOffer(drinkId));
        }
    }
}
