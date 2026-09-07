using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class HomeShowerWashingModelTests
    {
        [TestCase(HomeShowerWashRegion.Torso)]
        [TestCase(HomeShowerWashRegion.LeftArm)]
        [TestCase(HomeShowerWashRegion.LeftLeg)]
        [TestCase(HomeShowerWashRegion.RightLeg)]
        [TestCase(HomeShowerWashRegion.Intimate)]
        public void AnySingleRegionCanFillTheWholeGaugeAfterTenActiveSeconds(HomeShowerWashRegion region)
        {
            var progress = new HomeShowerWashingProgress();
            const float step = 0.05f;
            float elapsed = 0f;
            int samples = 0;
            while (!progress.Complete && samples++ < 1000)
            {
                float credited = progress.Credit(region, 0.08f, 0.02f, true, step);
                Assert.That(credited, Is.LessThanOrEqualTo(step * HomeShowerWashingProgress.MaximumCreditSpeed));
                elapsed += step;
            }

            Assert.That(progress.Complete, Is.True);
            Assert.That(progress.Amount, Is.EqualTo(1f));
            Assert.That(elapsed, Is.InRange(9.999f, 10.06f));
            Assert.That(progress.GetRegionAmount(HomeShowerWashRegion.LeftLeg), Is.EqualTo(1f),
                "The marker reports the shared progress even on an untouched body region.");
            Assert.That(progress.Credit(region, 0.08f, 0.02f, true, step), Is.Zero,
                "The completed shared gauge cannot exceed one hundred percent.");
            progress.Reset();
            Assert.That(progress.Amount, Is.Zero);
            Assert.That(progress.Complete, Is.False);
        }

        [TestCase(0f, 0.01f, true, 0.05f)]
        [TestCase(0.01f, 0f, true, 0.05f)]
        [TestCase(0.01f, 0.01f, false, 0.05f)]
        [TestCase(0.01f, 0.01f, true, 0f)]
        [TestCase(0.01f, 0.01f, true, 0.30f)]
        [TestCase(0.20f, 0.20f, true, 0.05f)]
        [TestCase(0.02f, 0.02f, true, 0.01f)]
        [TestCase(float.NaN, 0.01f, true, 0.05f)]
        [TestCase(0.01f, float.PositiveInfinity, true, 0.05f)]
        [TestCase(0.01f, 0.01f, true, -0.05f)]
        public void InactiveOrDiscontinuousSamplesCannotCleanOrBankInput(
            float commanded, float actual, bool contact, float seconds)
        {
            var progress = new HomeShowerWashingProgress();
            Assert.That(progress.Credit(HomeShowerWashRegion.Torso, commanded, actual, contact, seconds), Is.Zero);
            Assert.That(progress.Credit(HomeShowerWashRegion.Torso, 0f, 0.01f, true, 0.05f), Is.Zero);
            Assert.That(progress.Amount, Is.Zero);
        }

        [Test]
        public void CreditRequiresBothCommandedAndMeasuredContactDistance()
        {
            var progress = new HomeShowerWashingProgress();
            Assert.That(progress.Credit(HomeShowerWashRegion.None, 0.001f, 0.002f, true, 0.05f), Is.Zero);
            Assert.That(progress.Credit(HomeShowerWashRegion.RightArm, 0.001f, 0.002f, true, 0.05f), Is.Zero,
                "The soap-holding right arm cannot be washed or contribute progress.");
            Assert.That(progress.Amount, Is.Zero);
            Assert.That(progress.Credit(HomeShowerWashRegion.Torso, 0.001f, 0.002f, true, 0.05f),
                Is.EqualTo(0.001f).Within(0.000001f));
            Assert.That(progress.Credit(HomeShowerWashRegion.Torso, 0.002f, 0.001f, true, 0.05f),
                Is.EqualTo(0.001f).Within(0.000001f));
            Assert.That(progress.GetRegionAmount(HomeShowerWashRegion.RightArm), Is.Zero,
                "The excluded right arm does not show a washing marker even after other skin has earned progress.");
        }
    }
}
