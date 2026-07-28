using System;
using NUnit.Framework;

namespace BarPromenade.Tests
{
    public sealed class SplitTheGScoringTests
    {
        [Test]
        public void NormalSettings_UseAcceptedGameplayDefaults()
        {
            SplitTheGSettings settings =
                SplitTheGSettings.Normal;

            Assert.That(settings.TargetLevel, Is.EqualTo(0.5d));
            Assert.That(settings.DrinkSpeed, Is.EqualTo(0.22d));
            Assert.That(
                settings.MaximumDrinkTime,
                Is.EqualTo(4.8d));
            Assert.That(settings.SettlingTime, Is.EqualTo(1.4d));
            Assert.That(settings.MaximumAttempts, Is.EqualTo(3));
        }

        [TestCase(0d, SplitTheGResultBand.Perfect)]
        [TestCase(0.01d, SplitTheGResultBand.Perfect)]
        [TestCase(0.0101d, SplitTheGResultBand.Excellent)]
        [TestCase(0.03d, SplitTheGResultBand.Excellent)]
        [TestCase(0.0301d, SplitTheGResultBand.Good)]
        [TestCase(0.06d, SplitTheGResultBand.Good)]
        [TestCase(0.0601d, SplitTheGResultBand.Close)]
        [TestCase(0.10d, SplitTheGResultBand.Close)]
        [TestCase(0.1001d, SplitTheGResultBand.Miss)]
        public void GetBand_UsesGlassHeightTolerances(
            double error,
            SplitTheGResultBand expected)
        {
            Assert.That(
                SplitTheGScoring.GetBand(error),
                Is.EqualTo(expected));
        }

        [TestCase(0d, 100)]
        [TestCase(0.01d, 90)]
        [TestCase(0.03d, 70)]
        [TestCase(0.06d, 40)]
        [TestCase(0.10d, 0)]
        [TestCase(0.50d, 0)]
        public void CalculateScore_ClampsLinearFormula(
            double error,
            int expected)
        {
            Assert.That(
                SplitTheGScoring.CalculateScore(error),
                Is.EqualTo(expected));
        }

        [TestCase(
            0.5d,
            0.5d,
            SplitTheGLevelDirection.OnTarget)]
        [TestCase(
            0.7d,
            0.5d,
            SplitTheGLevelDirection.UnderDrank)]
        [TestCase(
            0.3d,
            0.5d,
            SplitTheGLevelDirection.OverDrank)]
        public void GetDirection_ReportsTooLittleOrTooMuch(
            double finalLevel,
            double targetLevel,
            SplitTheGLevelDirection expected)
        {
            Assert.That(
                SplitTheGScoring.GetDirection(
                    finalLevel,
                    targetLevel),
                Is.EqualTo(expected));
        }

        [Test]
        public void Settings_RejectInvalidConfiguration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SplitTheGSettings(
                    -0.01d,
                    0.22d,
                    4.8d,
                    1.4d,
                    3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SplitTheGSettings(
                    0.5d,
                    0d,
                    4.8d,
                    1.4d,
                    3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SplitTheGSettings(
                    0.5d,
                    0.22d,
                    4.8d,
                    -0.1d,
                    3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SplitTheGSettings(
                    0.5d,
                    0.22d,
                    4.8d,
                    1.4d,
                    0));
        }
    }
}
