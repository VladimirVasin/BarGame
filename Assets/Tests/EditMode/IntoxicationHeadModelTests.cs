using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The drunk head in isolation: exactly still sober, sinking with the
    /// level, nodding off now and then when far gone on a seeded and
    /// bounded clock, trailing the body's lean late and past it, and the
    /// same from the same seed.
    /// </summary>
    public sealed class IntoxicationHeadModelTests
    {
        private const float Frame = 1f / 60f;

        [Test]
        public void Sober_IsExactlyStill()
        {
            var model = new IntoxicationHeadModel(3);
            for (int frame = 0; frame < 10 * 60; frame++)
            {
                IntoxicationHeadPose pose = model.Advance(Frame, 0f, 5f * Mathf.Sin(frame * 0.1f), 2f);
                Assert.That(pose.IsNone, Is.True);
            }

            Assert.That(model.NodsTaken, Is.Zero);
        }

        [Test]
        public void Droop_GrowsWithTheLevel()
        {
            float MeanDroop(float level)
            {
                var model = new IntoxicationHeadModel(3);
                float sum = 0f;
                int count = 0;
                for (int frame = 0; frame < 20 * 60; frame++)
                {
                    IntoxicationHeadPose pose = model.Advance(Frame, level, 0f, 0f);
                    if (!model.Nodding)
                    {
                        sum += pose.PitchDownDegrees;
                        count++;
                    }
                }

                return sum / count;
            }

            float mild = MeanDroop(0.3f);
            float blind = MeanDroop(1f);
            Assert.That(mild, Is.GreaterThan(2f));
            Assert.That(blind, Is.GreaterThan(mild + 5f), "blind drunk the chin is well down");
            Assert.That(blind, Is.LessThan(IntoxicationHeadRules.DroopMaximumDegrees + IntoxicationHeadRules.WanderPitchDegrees + 0.5f));
        }

        [Test]
        public void Wander_IsSlowAndBounded()
        {
            var model = new IntoxicationHeadModel(5);
            float yawMin = float.PositiveInfinity;
            float yawMax = float.NegativeInfinity;
            float largestYawStep = 0f;
            float previousYaw = 0f;
            for (int frame = 0; frame < 30 * 60; frame++)
            {
                IntoxicationHeadPose pose = model.Advance(Frame, 0.5f, 0f, 0f);
                yawMin = Mathf.Min(yawMin, pose.YawDegrees);
                yawMax = Mathf.Max(yawMax, pose.YawDegrees);
                if (frame > 0)
                {
                    largestYawStep = Mathf.Max(largestYawStep, Mathf.Abs(pose.YawDegrees - previousYaw));
                }

                previousYaw = pose.YawDegrees;
                Assert.That(Mathf.Abs(pose.RollDegrees), Is.LessThanOrEqualTo(IntoxicationHeadRules.WanderRollDegrees * 0.5f + 0.001f));
            }

            Assert.That(yawMax - yawMin, Is.GreaterThan(4f), "the head does wander");
            Assert.That(yawMax, Is.LessThanOrEqualTo(IntoxicationHeadRules.WanderYawDegrees * 0.5f + 0.001f));
            Assert.That(largestYawStep, Is.LessThan(0.1f), "and never jumps");
        }

        [Test]
        public void Nods_AreSeededBoundedAndOnlyWhenFarGone()
        {
            var sober = new IntoxicationHeadModel(7);
            for (int frame = 0; frame < 60 * 60; frame++)
            {
                sober.Advance(Frame, 0.5f, 0f, 0f);
            }

            Assert.That(sober.NodsTaken, Is.Zero, "half drunk he does not nod off");

            var gone = new IntoxicationHeadModel(7);
            float deepest = 0f;
            for (int frame = 0; frame < 60 * 60; frame++)
            {
                IntoxicationHeadPose pose = gone.Advance(Frame, 1f, 0f, 0f);
                deepest = Mathf.Max(deepest, pose.PitchDownDegrees);
            }

            Assert.That(gone.NodsTaken, Is.InRange(4, 10), "a minute of blind drunk nods off four to ten times");
            Assert.That(deepest, Is.GreaterThan(IntoxicationHeadRules.DroopMaximumDegrees + IntoxicationHeadRules.NodDegrees * 0.8f));

            var again = new IntoxicationHeadModel(7);
            for (int frame = 0; frame < 60 * 60; frame++)
            {
                IntoxicationHeadPose a = gone.Advance(Frame, 1f, 0f, 0f);
                IntoxicationHeadPose b = again.Advance(Frame, 1f, 0f, 0f);
                _ = a;
                _ = b;
            }

            var first = new IntoxicationHeadModel(11);
            var second = new IntoxicationHeadModel(11);
            for (int frame = 0; frame < 30 * 60; frame++)
            {
                IntoxicationHeadPose a = first.Advance(Frame, 0.9f, 3f, 1f);
                IntoxicationHeadPose b = second.Advance(Frame, 0.9f, 3f, 1f);
                Assert.That(b.YawDegrees, Is.EqualTo(a.YawDegrees));
                Assert.That(b.PitchDownDegrees, Is.EqualTo(a.PitchDownDegrees));
                Assert.That(b.RollDegrees, Is.EqualTo(a.RollDegrees));
            }

            Assert.That(second.NodsTaken, Is.EqualTo(first.NodsTaken));
        }

        [Test]
        public void Lag_ArrivesLateAndOvershoots()
        {
            // The same seed with no lean is the wander alone; the difference
            // between the two is the lag term by itself.
            var model = new IntoxicationHeadModel(13);
            var baseline = new IntoxicationHeadModel(13);
            for (int frame = 0; frame < 3 * 60; frame++)
            {
                model.Advance(Frame, 1f, 0f, 0f);
                baseline.Advance(Frame, 1f, 0f, 0f);
            }

            // The body leans ten degrees right at once.
            float firstRoll = model.Advance(Frame, 1f, 10f, 0f).RollDegrees -
                              baseline.Advance(Frame, 1f, 0f, 0f).RollDegrees;
            Assert.That(firstRoll, Is.LessThan(-3f), "on the first frame the head is still where it was: behind the lean");
            float overshoot = 0f;
            float lag = 0f;
            for (int frame = 0; frame < 2 * 60; frame++)
            {
                lag = model.Advance(Frame, 1f, 10f, 0f).RollDegrees -
                      baseline.Advance(Frame, 1f, 0f, 0f).RollDegrees;
                overshoot = Mathf.Max(overshoot, lag);
            }

            Assert.That(overshoot, Is.GreaterThan(0.5f), "then it swings past the lean");
            Assert.That(Mathf.Abs(lag), Is.LessThan(1f), "and settles on it");
        }

        [Test]
        public void Shape_OfANodIsAQuickDropAndASlowReturn()
        {
            Assert.That(IntoxicationHeadRules.NodShape(0f), Is.Zero);
            Assert.That(IntoxicationHeadRules.NodShape(IntoxicationHeadRules.NodDropSeconds), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(IntoxicationHeadRules.NodShape(IntoxicationHeadRules.NodDropSeconds + IntoxicationHeadRules.NodReturnSeconds * 0.5f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(IntoxicationHeadRules.NodShape(IntoxicationHeadRules.NodSeconds), Is.Zero);
            Assert.That(IntoxicationHeadRules.NodShape(IntoxicationHeadRules.NodSeconds + 1f), Is.Zero);
        }
    }
}
