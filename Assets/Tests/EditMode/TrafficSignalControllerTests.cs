using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class TrafficSignalControllerTests
    {
        [Test]
        public void EvaluateLit_UsesSlowBoundedAmberCycle()
        {
            float litEnd =
                TrafficSignalController.BlinkPeriod *
                TrafficSignalController.LitFraction;

            Assert.That(
                TrafficSignalController.EvaluateLit(0f, 0f),
                Is.True);
            Assert.That(
                TrafficSignalController.EvaluateLit(
                    litEnd - 0.001f,
                    0f),
                Is.True);
            Assert.That(
                TrafficSignalController.EvaluateLit(
                    litEnd + 0.001f,
                    0f),
                Is.False);
            Assert.That(
                TrafficSignalController.EvaluateLit(
                    TrafficSignalController.BlinkPeriod,
                    0f),
                Is.True);
            Assert.That(
                1f / TrafficSignalController.BlinkPeriod,
                Is.LessThan(1f),
                "The signal must not flash at one hertz or faster.");
        }

        [Test]
        public void EvaluateLit_PhaseOffsetDesynchronizesIntersections()
        {
            bool first = TrafficSignalController.EvaluateLit(0f, 0f);
            bool second = TrafficSignalController.EvaluateLit(
                0f,
                TrafficSignalController.BlinkPeriod * 0.75f);

            Assert.That(first, Is.Not.EqualTo(second));
        }
    }
}
