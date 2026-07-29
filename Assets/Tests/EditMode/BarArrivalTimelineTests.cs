using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarArrivalTimelineTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void ClampDuration_EnforcesShortNonBlockingWindow()
        {
            Assert.That(
                BarArrivalTimeline.ClampDuration(0f),
                Is.EqualTo(
                    BarArrivalTimeline.MinimumDuration));
            Assert.That(
                BarArrivalTimeline.ClampDuration(9f),
                Is.EqualTo(
                    BarArrivalTimeline.MaximumDuration));
            Assert.That(
                BarArrivalTimeline.ClampDuration(float.NaN),
                Is.EqualTo(
                    BarArrivalTimeline.DefaultDuration));
            Assert.That(
                BarArrivalTimeline.ClampDuration(
                    float.NegativeInfinity),
                Is.EqualTo(
                    BarArrivalTimeline.MinimumDuration));
            Assert.That(
                BarArrivalTimeline.ClampDuration(
                    float.PositiveInfinity),
                Is.EqualTo(
                    BarArrivalTimeline.MaximumDuration));
        }

        [Test]
        public void Evaluate_InvalidAndCompleteTimesUseExactEndpoints()
        {
            BarArrivalFrame start =
                BarArrivalTimeline.Evaluate(
                    float.NaN,
                    BarArrivalTimeline.DefaultDuration);
            Assert.That(start.ElapsedTime, Is.Zero);
            Assert.That(start.NormalizedTime, Is.Zero);
            Assert.That(start.CameraBlend, Is.Zero);
            Assert.That(start.IsComplete, Is.False);

            BarArrivalFrame complete =
                BarArrivalTimeline.Evaluate(
                    float.PositiveInfinity,
                    BarArrivalTimeline.DefaultDuration);
            Assert.That(
                complete.ElapsedTime,
                Is.EqualTo(BarArrivalTimeline.DefaultDuration)
                    .Within(Tolerance));
            Assert.That(complete.NormalizedTime, Is.EqualTo(1f));
            Assert.That(complete.CameraBlend, Is.EqualTo(1f));
            Assert.That(complete.IsComplete, Is.True);
        }

        [Test]
        public void Evaluate_BlendIsBoundedAndMonotonic()
        {
            float previous = 0f;
            const int sampleCount = 64;
            for (int index = 0; index <= sampleCount; index++)
            {
                float elapsed =
                    BarArrivalTimeline.DefaultDuration *
                    index /
                    sampleCount;
                BarArrivalFrame frame =
                    BarArrivalTimeline.Evaluate(elapsed);

                Assert.That(
                    frame.NormalizedTime,
                    Is.InRange(0f, 1f));
                Assert.That(
                    frame.CameraBlend,
                    Is.InRange(0f, 1f));
                Assert.That(
                    frame.CameraBlend,
                    Is.GreaterThanOrEqualTo(
                        previous - Tolerance));
                previous = frame.CameraBlend;
            }
        }
    }
}
