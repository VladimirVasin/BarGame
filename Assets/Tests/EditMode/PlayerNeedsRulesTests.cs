using System;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class PlayerNeedsRulesTests
    {
        [TestCase(100, 35, 20, 65, 35)]
        [TestCase(30, 35, 20, 20, 10)]
        [TestCase(20, 35, 20, 20, 0)]
        [TestCase(10, 35, 20, 10, 0)]
        [TestCase(30, 35, 0, 0, 30)]
        [TestCase(0, 10, 20, 0, 0)]
        public void FoodRelief_RespectsDefinitionFloorWithoutIncreasingHunger(
            int hunger,
            int relief,
            int minimumHungerAfterUse,
            int expectedLevel,
            int expectedActualRelief)
        {
            PlayerNeedReliefResult result =
                PlayerNeedsRules.ApplyFoodRelief(
                    hunger,
                    relief,
                    minimumHungerAfterUse);

            Assert.That(result.LevelBefore, Is.EqualTo(hunger));
            Assert.That(result.LevelAfter, Is.EqualTo(expectedLevel));
            Assert.That(result.RequestedRelief, Is.EqualTo(relief));
            Assert.That(
                result.ActualRelief,
                Is.EqualTo(expectedActualRelief));
            Assert.That(
                result.Changed,
                Is.EqualTo(expectedActualRelief > 0));
        }

        [TestCase(100, 12, 88, 12)]
        [TestCase(5, 12, 0, 5)]
        [TestCase(0, 12, 0, 0)]
        [TestCase(40, 0, 40, 0)]
        public void StressRelief_ClampsAtZero(
            int stress,
            int relief,
            int expectedLevel,
            int expectedActualRelief)
        {
            PlayerNeedReliefResult result =
                PlayerNeedsRules.ApplyStressRelief(stress, relief);

            Assert.That(result.LevelBefore, Is.EqualTo(stress));
            Assert.That(result.LevelAfter, Is.EqualTo(expectedLevel));
            Assert.That(result.RequestedRelief, Is.EqualTo(relief));
            Assert.That(
                result.ActualRelief,
                Is.EqualTo(expectedActualRelief));
        }

        [TestCase(6, 0d, 0)]
        [TestCase(6, 0.01d, 0)]
        [TestCase(6, 0.25d, 2)]
        [TestCase(8, 0.5d, 4)]
        [TestCase(5, 0.5d, 3)]
        [TestCase(12, 4d, 48)]
        public void ScaleRelief_RoundsAwayFromZeroWithoutAMinimum(
            int reliefPerServing,
            double servings,
            int expected)
        {
            Assert.That(
                PlayerNeedsRules.ScaleRelief(
                    reliefPerServing,
                    servings),
                Is.EqualTo(expected));
        }

        [Test]
        public void InvalidLevelsAndRelief_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyFoodRelief(-1, 1, 20));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyFoodRelief(101, 1, 20));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyFoodRelief(50, -1, 20));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyFoodRelief(50, 1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyFoodRelief(50, 1, 101));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyStressRelief(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyStressRelief(101, 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ApplyStressRelief(50, -1));
        }

        [Test]
        public void InvalidScalingInput_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ScaleRelief(-1, 1d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ScaleRelief(1, -0.1d));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ScaleRelief(1, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ScaleRelief(
                    1,
                    double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PlayerNeedsRules.ScaleRelief(
                    int.MaxValue,
                    2d));
        }
    }
}
