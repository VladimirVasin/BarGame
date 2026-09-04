using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarDrinkCatalogTests
    {
        private static readonly object[] expectedMenuOffers =
        {
            new object[]
            {
                DrinkId.LightBeer,
                "drink.light_beer",
                "drink.light_beer.description",
                8
            },
            new object[]
            {
                DrinkId.RedWine,
                "drink.red_wine",
                "drink.red_wine.description",
                14
            },
            new object[]
            {
                DrinkId.CognacVs,
                "drink.cognac_vs",
                "drink.cognac_vs.description",
                20
            },
            new object[]
            {
                DrinkId.Vodka,
                "drink.vodka",
                "drink.vodka.description",
                15
            }
        };

        private static readonly object[] expectedPurchaseOffers =
        {
            Entry(DrinkId.Water, "drink.water", string.Empty, 2),
            Entry(
                DrinkId.LightBeer,
                "drink.light_beer",
                "drink.light_beer.description",
                8),
            Entry(DrinkId.DarkBeer, "drink.dark_beer", string.Empty, 10),
            Entry(DrinkId.WhiteWine, "drink.white_wine", string.Empty, 12),
            Entry(
                DrinkId.RedWine,
                "drink.red_wine",
                "drink.red_wine.description",
                14),
            Entry(
                DrinkId.Vodka,
                "drink.vodka",
                "drink.vodka.description",
                15),
            Entry(
                DrinkId.PepperVodka,
                "drink.pepper_vodka",
                string.Empty,
                18),
            Entry(
                DrinkId.CognacVs,
                "drink.cognac_vs",
                "drink.cognac_vs.description",
                20),
            Entry(
                DrinkId.CognacVsop,
                "drink.cognac_vsop",
                string.Empty,
                25)
        };

        [Test]
        public void Offers_ExposeExactOrderedVisibleMenu()
        {
            IReadOnlyList<BarDrinkOffer> offers =
                BarDrinkCatalog.Offers;

            Assert.That(
                offers,
                Has.Count.EqualTo(expectedMenuOffers.Length));
            var uniqueDrinks = new HashSet<DrinkId>();
            for (int index = 0;
                 index < expectedMenuOffers.Length;
                 index++)
            {
                object[] expected = (object[])expectedMenuOffers[index];
                DrinkId expectedDrink = (DrinkId)expected[0];
                string expectedNameKey = (string)expected[1];
                string expectedDescriptionKey = (string)expected[2];
                int expectedPrice = (int)expected[3];
                BarDrinkOffer offer = offers[index];

                Assert.That(offer.DrinkId, Is.EqualTo(expectedDrink));
                Assert.That(offer.NameKey, Is.EqualTo(expectedNameKey));
                Assert.That(
                    offer.DescriptionKey,
                    Is.EqualTo(expectedDescriptionKey));
                Assert.That(offer.DescriptionKey, Is.Not.Empty);
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

        [TestCaseSource(nameof(expectedPurchaseOffers))]
        public void Lookup_ReturnsMatchingOffer(
            DrinkId drinkId,
            string expectedNameKey,
            string expectedDescriptionKey,
            int expectedPrice)
        {
            Assert.That(
                BarDrinkCatalog.TryGetOffer(
                    drinkId,
                    out BarDrinkOffer offer),
                Is.True);
            Assert.That(offer.DrinkId, Is.EqualTo(drinkId));
            Assert.That(offer.NameKey, Is.EqualTo(expectedNameKey));
            Assert.That(
                offer.DescriptionKey,
                Is.EqualTo(expectedDescriptionKey));
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

        private static object[] Entry(
            DrinkId drinkId,
            string nameKey,
            string descriptionKey,
            int price)
        {
            return new object[]
            {
                drinkId,
                nameKey,
                descriptionKey,
                price
            };
        }
    }
}
