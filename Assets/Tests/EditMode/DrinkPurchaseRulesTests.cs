using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class DrinkPurchaseRulesTests
    {
        [Test]
        public void Alcohol_WithExactFunds_CommitsExpectedResult()
        {
            DrinkPurchaseResult result = DrinkPurchaseRules.Evaluate(
                DrinkId.Vodka,
                15,
                10,
                DrinkId.RedWine,
                2);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(
                result.Status,
                Is.EqualTo(DrinkPurchaseStatus.Success));
            Assert.That(result.RequestedDrink, Is.EqualTo(DrinkId.Vodka));
            Assert.That(result.Offer.DrinkId, Is.EqualTo(DrinkId.Vodka));
            Assert.That(result.Offer.Price, Is.EqualTo(15));
            Assert.That(result.CashBefore, Is.EqualTo(15));
            Assert.That(result.CashAfter, Is.Zero);
            Assert.That(result.IntoxicationBefore, Is.EqualTo(10));
            Assert.That(result.IntoxicationAfter, Is.EqualTo(28));
            Assert.That(result.ActualIntoxicationDelta, Is.EqualTo(18));
            Assert.That(
                result.LastAlcoholicDrinkBefore,
                Is.EqualTo(DrinkId.RedWine));
            Assert.That(
                result.LastAlcoholicDrinkAfter,
                Is.EqualTo(DrinkId.Vodka));
            Assert.That(result.DrinksConsumedBefore, Is.EqualTo(2));
            Assert.That(result.DrinksConsumedAfter, Is.EqualTo(3));
        }

        [Test]
        public void Alcohol_AtNinetyNine_ClampsAndReportsActualDelta()
        {
            DrinkPurchaseResult result = DrinkPurchaseRules.Evaluate(
                DrinkId.LightBeer,
                8,
                99,
                DrinkId.CognacVs,
                4);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.IntoxicationAfter, Is.EqualTo(100));
            Assert.That(result.ActualIntoxicationDelta, Is.EqualTo(1));
            Assert.That(
                result.LastAlcoholicDrinkAfter,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(result.DrinksConsumedAfter, Is.EqualTo(5));
        }

        [Test]
        public void Alcohol_AtMaximum_IsRejectedWithoutMutation()
        {
            DrinkPurchaseResult result = DrinkPurchaseRules.Evaluate(
                DrinkId.LightBeer,
                100,
                100,
                DrinkId.Vodka,
                5);

            AssertFailurePreservesState(
                result,
                DrinkPurchaseStatus.MaximumIntoxication,
                100,
                100,
                DrinkId.Vodka,
                5);
            Assert.That(
                result.Offer.DrinkId,
                Is.EqualTo(DrinkId.LightBeer));
        }

        [Test]
        public void Water_ChargesAndCountsWithoutChangingDrinkingContext()
        {
            DrinkPurchaseResult result = DrinkPurchaseRules.Evaluate(
                DrinkId.Water,
                12,
                67,
                DrinkId.PepperVodka,
                3);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.CashAfter, Is.EqualTo(10));
            Assert.That(result.IntoxicationAfter, Is.EqualTo(67));
            Assert.That(result.ActualIntoxicationDelta, Is.Zero);
            Assert.That(
                result.LastAlcoholicDrinkAfter,
                Is.EqualTo(DrinkId.PepperVodka));
            Assert.That(result.DrinksConsumedAfter, Is.EqualTo(4));
        }

        [Test]
        public void InsufficientFunds_IsRejectedWithoutMutation()
        {
            DrinkPurchaseResult result = DrinkPurchaseRules.Evaluate(
                DrinkId.DarkBeer,
                9,
                24,
                DrinkId.LightBeer,
                2);

            AssertFailurePreservesState(
                result,
                DrinkPurchaseStatus.InsufficientFunds,
                9,
                24,
                DrinkId.LightBeer,
                2);
            Assert.That(result.Offer.DrinkId, Is.EqualTo(DrinkId.DarkBeer));
            Assert.That(result.Offer.Price, Is.EqualTo(10));
        }

        [TestCase(DrinkId.None)]
        [TestCase(DrinkId.Moonshine)]
        [TestCase((DrinkId)999)]
        public void NonRetailDrink_IsRejectedWithoutMutation(
            DrinkId drinkId)
        {
            DrinkPurchaseResult result = DrinkPurchaseRules.Evaluate(
                drinkId,
                50,
                24,
                DrinkId.RedWine,
                2);

            AssertFailurePreservesState(
                result,
                DrinkPurchaseStatus.NotOffered,
                50,
                24,
                DrinkId.RedWine,
                2);
            Assert.That(result.RequestedDrink, Is.EqualTo(drinkId));
            Assert.That(
                result.Offer,
                Is.EqualTo(default(BarDrinkOffer)));
        }

        [Test]
        public void InvalidInputState_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkPurchaseRules.Evaluate(
                    DrinkId.Water,
                    -1,
                    0,
                    DrinkId.None,
                    0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkPurchaseRules.Evaluate(
                    DrinkId.Water,
                    10,
                    -1,
                    DrinkId.None,
                    0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkPurchaseRules.Evaluate(
                    DrinkId.Water,
                    10,
                    101,
                    DrinkId.None,
                    0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkPurchaseRules.Evaluate(
                    DrinkId.Water,
                    10,
                    0,
                    DrinkId.Water,
                    0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkPurchaseRules.Evaluate(
                    DrinkId.Water,
                    10,
                    0,
                    DrinkId.None,
                    -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DrinkPurchaseRules.Evaluate(
                    DrinkId.Water,
                    10,
                    0,
                    DrinkId.None,
                    int.MaxValue));
        }

        private static void AssertFailurePreservesState(
            DrinkPurchaseResult result,
            DrinkPurchaseStatus expectedStatus,
            int cash,
            int intoxication,
            DrinkId lastAlcoholicDrink,
            int drinksConsumed)
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Status, Is.EqualTo(expectedStatus));
            Assert.That(result.CashBefore, Is.EqualTo(cash));
            Assert.That(result.CashAfter, Is.EqualTo(cash));
            Assert.That(
                result.IntoxicationBefore,
                Is.EqualTo(intoxication));
            Assert.That(
                result.IntoxicationAfter,
                Is.EqualTo(intoxication));
            Assert.That(result.ActualIntoxicationDelta, Is.Zero);
            Assert.That(
                result.LastAlcoholicDrinkBefore,
                Is.EqualTo(lastAlcoholicDrink));
            Assert.That(
                result.LastAlcoholicDrinkAfter,
                Is.EqualTo(lastAlcoholicDrink));
            Assert.That(
                result.DrinksConsumedBefore,
                Is.EqualTo(drinksConsumed));
            Assert.That(
                result.DrinksConsumedAfter,
                Is.EqualTo(drinksConsumed));
        }
    }
}
