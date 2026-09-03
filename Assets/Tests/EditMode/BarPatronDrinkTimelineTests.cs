using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarPatronDrinkTimelineTests
    {
        private const float Step = 1f / 60f;

        private static void Advance(
            BarPatronDrinkTimeline timeline,
            float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                timeline.Advance(Step);
            }
        }

        private static void AdvanceToPhase(
            BarPatronDrinkTimeline timeline,
            BarPatronDrinkPhase phase)
        {
            // Generous cap: one full cadence never exceeds the summed
            // maxima, so a few cycles always reach any phase.
            int steps = Mathf.CeilToInt(60f / Step);
            for (int index = 0; index < steps; index++)
            {
                if (timeline.Phase == phase)
                {
                    return;
                }

                timeline.Advance(Step);
            }

            Assert.Fail(
                $"The timeline never reached the {phase} phase.");
        }

        [Test]
        public void Cadence_LoopsThroughAllPhasesWithBoundedWeights()
        {
            var timeline = new BarPatronDrinkTimeline(7);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(BarPatronDrinkPhase.Rest));
            Assert.That(timeline.ArmWeight, Is.Zero);
            Assert.That(timeline.SipTilt, Is.Zero);
            Assert.That(
                timeline.PhaseDuration,
                Is.InRange(
                    BarPatronDrinkTimeline
                        .MinimumInitialStaggerSeconds,
                    BarPatronDrinkTimeline
                        .MaximumInitialStaggerSeconds));

            AdvanceToPhase(timeline, BarPatronDrinkPhase.Raise);
            Assert.That(timeline.SipTilt, Is.Zero);
            AdvanceToPhase(timeline, BarPatronDrinkPhase.Sip);
            Assert.That(timeline.ArmWeight, Is.EqualTo(1f));
            Assert.That(
                timeline.PhaseDuration,
                Is.InRange(
                    BarPatronDrinkTimeline.MinimumSipSeconds,
                    BarPatronDrinkTimeline.MaximumSipSeconds));

            Advance(timeline, timeline.PhaseDuration * 0.5f);
            if (timeline.Phase == BarPatronDrinkPhase.Sip)
            {
                Assert.That(
                    timeline.SipTilt,
                    Is.GreaterThan(0.5f),
                    "Mid-sip the bottle must be visibly tipped.");
            }

            AdvanceToPhase(timeline, BarPatronDrinkPhase.Lower);
            AdvanceToPhase(timeline, BarPatronDrinkPhase.Rest);
            Assert.That(timeline.ArmWeight, Is.Zero);
            Assert.That(
                timeline.PhaseDuration,
                Is.InRange(
                    BarPatronDrinkTimeline.MinimumRestSeconds,
                    BarPatronDrinkTimeline.MaximumRestSeconds));

            // The full weight envelope stays inside [0, 1] across a
            // long stretch of the cadence.
            for (int index = 0; index < 3000; index++)
            {
                timeline.Advance(Step);
                Assert.That(
                    timeline.ArmWeight,
                    Is.InRange(0f, 1f));
                Assert.That(
                    timeline.SipTilt,
                    Is.InRange(0f, 1f));
                if (timeline.Phase != BarPatronDrinkPhase.Sip)
                {
                    Assert.That(timeline.SipTilt, Is.Zero);
                }
            }
        }

        [Test]
        public void Cadence_IsDeterministicPerSeedAndStaggersAcrossSeeds()
        {
            var first = new BarPatronDrinkTimeline(42);
            var second = new BarPatronDrinkTimeline(42);
            for (int index = 0; index < 4000; index++)
            {
                first.Advance(Step);
                second.Advance(Step);
                Assert.That(second.Phase, Is.EqualTo(first.Phase));
                Assert.That(
                    second.ArmWeight,
                    Is.EqualTo(first.ArmWeight).Within(0.0001f));
            }

            // Different seeds must not share the whole duration
            // sequence — the crowd never moves in unison.
            var left = new BarPatronDrinkTimeline(1);
            var right = new BarPatronDrinkTimeline(2);
            bool diverged = false;
            for (int cycle = 0; cycle < 8 && !diverged; cycle++)
            {
                AdvanceToPhase(left, BarPatronDrinkPhase.Rest);
                AdvanceToPhase(right, BarPatronDrinkPhase.Rest);
                diverged = Mathf.Abs(
                    left.PhaseDuration -
                    right.PhaseDuration) > 0.01f;
                AdvanceToPhase(left, BarPatronDrinkPhase.Raise);
                AdvanceToPhase(right, BarPatronDrinkPhase.Raise);
            }

            Assert.That(
                diverged,
                Is.True,
                "Two different seeds must produce different rests.");
        }

        [Test]
        public void GulpCue_FiresAtMostOncePerSipAndOnlyForSomeSips()
        {
            var timeline = new BarPatronDrinkTimeline(11);
            int sips = 0;
            int sipsWithGulp = 0;
            for (int cycle = 0; cycle < 40; cycle++)
            {
                AdvanceToPhase(timeline, BarPatronDrinkPhase.Sip);
                sips++;
                int gulps = 0;
                int guard = Mathf.CeilToInt(
                    (BarPatronDrinkTimeline.MaximumSipSeconds + 1f) /
                    Step);
                while (timeline.Phase == BarPatronDrinkPhase.Sip &&
                       guard-- > 0)
                {
                    if (timeline.ConsumeGulpCue())
                    {
                        gulps++;
                        Assert.That(
                            timeline.PhaseElapsed,
                            Is.GreaterThanOrEqualTo(
                                BarPatronDrinkTimeline
                                    .GulpDelaySeconds));
                    }

                    timeline.Advance(Step);
                }

                Assert.That(
                    gulps,
                    Is.LessThanOrEqualTo(1),
                    "A sip carries at most one audible gulp.");
                if (gulps > 0)
                {
                    sipsWithGulp++;
                }
            }

            Assert.That(
                sipsWithGulp,
                Is.GreaterThan(0),
                "Across forty sips some must gulp audibly.");
            Assert.That(
                sipsWithGulp,
                Is.LessThan(sips),
                "A bar must murmur, not gurgle in chorus.");
        }

        [Test]
        public void CafeActionWeight_BlendsOutBeforeTheLastClipSample()
        {
            const float clipLength = 4.75f;
            Assert.That(
                BarPatronDrinkingArmPose.ResolveAuthoredActionWeight(
                    0f,
                    clipLength),
                Is.Zero);
            Assert.That(
                BarPatronDrinkingArmPose.ResolveAuthoredActionWeight(
                    0.5f,
                    clipLength),
                Is.EqualTo(1f));
            Assert.That(
                BarPatronDrinkingArmPose.ResolveAuthoredActionWeight(
                    1f - 0.16f / clipLength,
                    clipLength),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                BarPatronDrinkingArmPose.ResolveAuthoredActionWeight(
                    1f,
                    clipLength),
                Is.Zero,
                "Drink must already match the seated base pose before " +
                "the timeline clears it on the following Rest frame.");
        }

        [Test]
        public void BottleRotation_ReachesHorizontalSipWithoutEndpointRoll()
        {
            Quaternion almostAtSip =
                BarPatronDrinkingArmPose.ResolveBottleRotation(
                    Vector3.right,
                    Vector3.up,
                    Vector3.back,
                    0.999f);
            Quaternion atSip =
                BarPatronDrinkingArmPose.ResolveBottleRotation(
                    Vector3.right,
                    Vector3.up,
                    Vector3.back,
                    1f);

            Assert.That(
                Quaternion.Angle(almostAtSip, atSip),
                Is.LessThan(0.2f),
                "The last sip sample must not snap the bottle roll.");
            Assert.That(
                Vector3.Angle(atSip * Vector3.up, Vector3.back),
                Is.LessThan(0.001f),
                "The bottle neck must finish horizontal into the mouth.");
            Assert.That(
                Vector3.Angle(atSip * Vector3.forward, Vector3.right),
                Is.LessThan(0.001f),
                "The bottle roll reference must stay stable at full sip.");
        }
    }
}
