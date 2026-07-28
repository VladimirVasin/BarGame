using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class DoorTransitionTimelineTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void Evaluate_InvalidStartTimesResolveToClosedBlackPose()
        {
            AssertStartPose(DoorTransitionTimeline.Evaluate(-2f));
            AssertStartPose(
                DoorTransitionTimeline.Evaluate(float.NegativeInfinity));
            AssertStartPose(DoorTransitionTimeline.Evaluate(float.NaN));
        }

        [Test]
        public void Evaluate_EndAndOvershootResolveToCompletePose()
        {
            AssertCompletePose(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.TotalDuration));
            AssertCompletePose(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.TotalDuration + 8f));
            AssertCompletePose(
                DoorTransitionTimeline.Evaluate(float.PositiveInfinity));
        }

        [Test]
        public void Evaluate_PhasesUseOrderedExactEndpoints()
        {
            Assert.That(
                DoorTransitionTimeline.RevealStartTime,
                Is.LessThan(DoorTransitionTimeline.HandleStartTime));
            Assert.That(
                DoorTransitionTimeline.HandleStartTime,
                Is.LessThan(DoorTransitionTimeline.RevealEndTime));
            Assert.That(
                DoorTransitionTimeline.RevealEndTime,
                Is.LessThan(DoorTransitionTimeline.DoorOpenStartTime));
            Assert.That(
                DoorTransitionTimeline.DoorOpenStartTime,
                Is.EqualTo(DoorTransitionTimeline.HandleEndTime));
            Assert.That(
                DoorTransitionTimeline.DoorOpenEndTime,
                Is.LessThan(DoorTransitionTimeline.FadeOutStartTime));
            Assert.That(
                DoorTransitionTimeline.FadeOutStartTime,
                Is.LessThan(DoorTransitionTimeline.CameraPushEndTime));
            Assert.That(
                DoorTransitionTimeline.CameraPushEndTime,
                Is.LessThan(DoorTransitionTimeline.TotalDuration));

            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.RevealStartTime)
                    .BlackOpacity,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.RevealEndTime)
                    .BlackOpacity,
                Is.Zero.Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.HandleStartTime)
                    .HandleTurn,
                Is.Zero.Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.HandleEndTime)
                    .HandleTurn,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.DoorOpenStartTime)
                    .DoorOpen,
                Is.Zero.Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.DoorOpenEndTime)
                    .DoorOpen,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.CameraPushStartTime)
                    .CameraPush,
                Is.Zero.Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.CameraPushEndTime)
                    .CameraPush,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                DoorTransitionTimeline.Evaluate(
                    DoorTransitionTimeline.FadeOutStartTime)
                    .BlackOpacity,
                Is.Zero.Within(Tolerance));
        }

        [Test]
        public void Evaluate_AnimationChannelsStayBoundedAndMonotonic()
        {
            DoorTransitionPose previous =
                DoorTransitionTimeline.Evaluate(0f);

            const int sampleCount = 128;
            for (int index = 1; index <= sampleCount; index++)
            {
                float elapsed =
                    DoorTransitionTimeline.TotalDuration *
                    index /
                    sampleCount;
                DoorTransitionPose current =
                    DoorTransitionTimeline.Evaluate(elapsed);

                Assert.That(current.NormalizedTime, Is.InRange(0f, 1f));
                Assert.That(current.HandleTurn, Is.InRange(0f, 1f));
                Assert.That(current.DoorOpen, Is.InRange(0f, 1f));
                Assert.That(current.CameraPush, Is.InRange(0f, 1f));
                Assert.That(current.BlackOpacity, Is.InRange(0f, 1f));
                Assert.That(
                    current.HandleTurn,
                    Is.GreaterThanOrEqualTo(
                        previous.HandleTurn - Tolerance));
                Assert.That(
                    current.DoorOpen,
                    Is.GreaterThanOrEqualTo(
                        previous.DoorOpen - Tolerance));
                Assert.That(
                    current.CameraPush,
                    Is.GreaterThanOrEqualTo(
                        previous.CameraPush - Tolerance));

                previous = current;
            }

            AssertBlackOpacityIsMonotonic(
                DoorTransitionTimeline.RevealStartTime,
                DoorTransitionTimeline.RevealEndTime,
                decreasing: true);
            AssertBlackOpacityIsMonotonic(
                DoorTransitionTimeline.FadeOutStartTime,
                DoorTransitionTimeline.TotalDuration,
                decreasing: false);
        }

        private static void AssertStartPose(DoorTransitionPose pose)
        {
            Assert.That(pose.ElapsedTime, Is.Zero.Within(Tolerance));
            Assert.That(pose.NormalizedTime, Is.Zero.Within(Tolerance));
            Assert.That(pose.HandleTurn, Is.Zero.Within(Tolerance));
            Assert.That(pose.DoorOpen, Is.Zero.Within(Tolerance));
            Assert.That(pose.CameraPush, Is.Zero.Within(Tolerance));
            Assert.That(
                pose.BlackOpacity,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(pose.IsComplete, Is.False);
        }

        private static void AssertCompletePose(
            DoorTransitionPose pose)
        {
            Assert.That(
                pose.ElapsedTime,
                Is.EqualTo(DoorTransitionTimeline.TotalDuration)
                    .Within(Tolerance));
            Assert.That(
                pose.NormalizedTime,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                pose.HandleTurn,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                pose.DoorOpen,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                pose.CameraPush,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(
                pose.BlackOpacity,
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(pose.IsComplete, Is.True);
        }

        private static void AssertBlackOpacityIsMonotonic(
            float startTime,
            float endTime,
            bool decreasing)
        {
            float previous =
                DoorTransitionTimeline.Evaluate(startTime).BlackOpacity;

            const int sampleCount = 32;
            for (int index = 1; index <= sampleCount; index++)
            {
                float elapsed =
                    startTime +
                    (endTime - startTime) *
                    index /
                    sampleCount;
                float current =
                    DoorTransitionTimeline.Evaluate(elapsed).BlackOpacity;

                if (decreasing)
                {
                    Assert.That(
                        current,
                        Is.LessThanOrEqualTo(previous + Tolerance));
                }
                else
                {
                    Assert.That(
                        current,
                        Is.GreaterThanOrEqualTo(previous - Tolerance));
                }

                previous = current;
            }
        }
    }
}
