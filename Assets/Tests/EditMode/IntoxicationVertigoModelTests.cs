using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class IntoxicationVertigoModelTests
    {
        private const float Step = 1f / 120f;
        private const int Seed = 7311;

        [Test]
        public void ZeroStrength_KeepsStillWaterAndAnEmptyDisc()
        {
            var model = new IntoxicationVertigoModel(Seed);

            for (int index = 0; index < 120 * 60; index++)
            {
                model.Advance(Step, 0f, 1f);
                Assert.That(model.Twist, Is.EqualTo(0f));
                Assert.That(model.TwistRadians, Is.EqualTo(0f));
                Assert.That(
                    model.CoreOffsetPixels,
                    Is.EqualTo(Vector2.zero));
                Assert.That(
                    model.Phase,
                    Is.EqualTo(IntoxicationVertigoPhase.Rest));
            }
        }

        [Test]
        public void SameSeed_ReplaysTheSameWater_AndSeedsDiffer()
        {
            var first = new IntoxicationVertigoModel(Seed);
            var second = new IntoxicationVertigoModel(Seed);
            var other = new IntoxicationVertigoModel(Seed + 1);
            bool differed = false;

            for (int index = 0; index < 120 * 60; index++)
            {
                first.Advance(Step, 1f, 1f);
                second.Advance(Step, 1f, 1f);
                other.Advance(Step, 1f, 1f);
                Assert.That(second.Twist, Is.EqualTo(first.Twist));
                Assert.That(
                    second.CoreOffsetPixels,
                    Is.EqualTo(first.CoreOffsetPixels));
                if (!Mathf.Approximately(first.Twist, other.Twist))
                {
                    differed = true;
                }
            }

            Assert.That(differed, Is.True);
        }

        [Test]
        public void FullStrength_WindsUpHoldsUnwindsAndTurnsBothWays()
        {
            var model = new IntoxicationVertigoModel(Seed);
            var signs = new HashSet<float>();
            IntoxicationVertigoPhase previous = model.Phase;
            int cycles = 0;

            for (int index = 0; index < 120 * 300; index++)
            {
                model.Advance(Step, 1f, 1f);
                Assert.That(
                    Mathf.Abs(model.TwistRadians),
                    Is.LessThanOrEqualTo(
                        IntoxicationVertigoModel.MaximumTwistRadians +
                        0.0001f),
                    "The water may never wind past its maximum.");
                if (model.Phase == previous)
                {
                    continue;
                }

                Assert.That(
                    model.Phase,
                    Is.EqualTo(NextPhase(previous)),
                    "An attack must run rest, out, peak, back, rest.");
                if (model.Phase == IntoxicationVertigoPhase.Out)
                {
                    signs.Add(model.CycleSign);
                    cycles++;
                }

                previous = model.Phase;
            }

            Assert.That(
                cycles,
                Is.GreaterThan(4),
                "Five minutes of the top level must carry several attacks.");
            Assert.That(
                signs,
                Is.EquivalentTo(new[] { 1f, -1f }),
                "The water has to turn both ways over many attacks.");
        }

        [Test]
        public void Pace_ShortensTheLegsIntoTheDocumentedRanges()
        {
            AssertLegsWithin(
                0f,
                IntoxicationVertigoModel.SlowLegMinimumSeconds,
                IntoxicationVertigoModel.SlowLegMaximumSeconds);
            AssertLegsWithin(
                1f,
                IntoxicationVertigoModel.FastLegMinimumSeconds,
                IntoxicationVertigoModel.FastLegMaximumSeconds);
        }

        [Test]
        public void TheDisc_FloatsWhileTheWhirlpoolIsStillResting()
        {
            var model = new IntoxicationVertigoModel(Seed);

            for (int index = 0; index < 60; index++)
            {
                model.Advance(Step, 1f, 1f);
            }

            Vector2 early = model.CoreOffsetPixels;
            Assert.That(
                model.Phase,
                Is.EqualTo(IntoxicationVertigoPhase.Rest),
                "Half a second in, the opening rest must still be running.");
            Assert.That(model.Twist, Is.EqualTo(0f));
            Assert.That(
                early.magnitude,
                Is.EqualTo(
                    IntoxicationVertigoModel.CoreWobbleInternalPixels)
                    .Within(0.001f),
                "The disc's drift is the level's, not the attack's.");

            for (int index = 0; index < 60; index++)
            {
                model.Advance(Step, 1f, 1f);
            }

            Assert.That(
                Vector2.Distance(early, model.CoreOffsetPixels),
                Is.GreaterThan(0.05f),
                "The disc has to keep circling through the rest.");
            Assert.That(
                model.CoreOffsetPixels.magnitude,
                Is.EqualTo(
                    IntoxicationVertigoModel.CoreWobbleInternalPixels)
                    .Within(0.001f));
        }

        [Test]
        public void HalfStrength_HalvesTheDiscsDrift()
        {
            var model = new IntoxicationVertigoModel(Seed);

            model.Advance(Step, 0.5f, 1f);

            Assert.That(
                model.CoreOffsetPixels.magnitude,
                Is.EqualTo(
                    IntoxicationVertigoModel.CoreWobbleInternalPixels *
                    0.5f).Within(0.001f));
        }

        [Test]
        public void HugeStep_IsClampedAndTheLeftoverCarriesOver()
        {
            var model = new IntoxicationVertigoModel(Seed);

            model.Advance(1e6f, 1f, 1f);

            Assert.That(
                model.Phase,
                Is.EqualTo(IntoxicationVertigoPhase.Rest),
                "One frame may never skip a whole attack.");
            Assert.That(
                model.PhaseElapsed,
                Is.EqualTo(IntoxicationVertigoModel.MaximumStepSeconds)
                    .Within(0.0001f));

            // Twenty-five clamped steps: the two-second opening rest ends and
            // the remaining half second has to land inside the wind-up.
            for (int index = 1; index < 25; index++)
            {
                model.Advance(
                    IntoxicationVertigoModel.MaximumStepSeconds,
                    1f,
                    1f);
            }

            Assert.That(
                model.Phase,
                Is.EqualTo(IntoxicationVertigoPhase.Out));
            Assert.That(
                model.PhaseElapsed,
                Is.EqualTo(
                    2.5f - IntoxicationVertigoModel.InitialRestSeconds)
                    .Within(0.02f),
                "The leftover of the step that crossed the boundary is lost.");
        }

        [Test]
        public void NaNOrNegativeStep_ChangesNothing()
        {
            var model = new IntoxicationVertigoModel(Seed);

            for (int index = 0; index < 120; index++)
            {
                model.Advance(Step, 1f, 1f);
            }

            float twist = model.Twist;
            Vector2 core = model.CoreOffsetPixels;
            float elapsed = model.PhaseElapsed;

            model.Advance(float.NaN, 1f, 1f);
            model.Advance(-1f, 1f, 1f);
            model.Advance(0f, 1f, 1f);

            Assert.That(model.Twist, Is.EqualTo(twist));
            Assert.That(model.CoreOffsetPixels, Is.EqualTo(core));
            Assert.That(model.PhaseElapsed, Is.EqualTo(elapsed));
        }

        [Test]
        public void SoberingUpMidAttack_FinishesTheLatchedWindUp()
        {
            var model = new IntoxicationVertigoModel(Seed);
            while (model.Phase != IntoxicationVertigoPhase.Peak)
            {
                model.Advance(Step, 1f, 1f);
            }

            float latched = model.CycleAmplitude;
            Assert.That(latched, Is.GreaterThan(0f));

            model.Advance(Step, 0f, 0f);
            Assert.That(
                model.CycleAmplitude,
                Is.EqualTo(latched),
                "A running attack keeps the reach it latched.");
            Assert.That(Mathf.Abs(model.Twist), Is.GreaterThan(0f));

            for (int index = 0; index < 120 * 60; index++)
            {
                model.Advance(Step, 0f, 0f);
            }

            Assert.That(
                model.Phase,
                Is.EqualTo(IntoxicationVertigoPhase.Rest));
            Assert.That(model.Twist, Is.EqualTo(0f));
            Assert.That(model.CoreOffsetPixels, Is.EqualTo(Vector2.zero));
        }

        private static void AssertLegsWithin(
            float pace,
            float minimum,
            float maximum)
        {
            var model = new IntoxicationVertigoModel(Seed);
            for (int index = 0; index < 120 * 240; index++)
            {
                model.Advance(Step, 1f, pace);
                if (model.Phase == IntoxicationVertigoPhase.Rest)
                {
                    continue;
                }

                Assert.That(
                    model.OutLegSeconds,
                    Is.InRange(minimum, maximum));
                Assert.That(
                    model.BackLegSeconds,
                    Is.InRange(minimum, maximum));
                Assert.That(
                    model.PeakHoldSeconds,
                    Is.InRange(
                        model.OutLegSeconds *
                        IntoxicationVertigoModel.PeakHoldMinimumFraction,
                        model.OutLegSeconds *
                        IntoxicationVertigoModel.PeakHoldMaximumFraction));
                Assert.That(
                    model.RestHoldSeconds,
                    Is.InRange(
                        model.BackLegSeconds *
                        IntoxicationVertigoModel.RestHoldMinimumFraction,
                        model.BackLegSeconds *
                        IntoxicationVertigoModel.RestHoldMaximumFraction));
            }
        }

        private static IntoxicationVertigoPhase NextPhase(
            IntoxicationVertigoPhase phase)
        {
            switch (phase)
            {
                case IntoxicationVertigoPhase.Rest:
                    return IntoxicationVertigoPhase.Out;
                case IntoxicationVertigoPhase.Out:
                    return IntoxicationVertigoPhase.Peak;
                case IntoxicationVertigoPhase.Peak:
                    return IntoxicationVertigoPhase.Back;
                default:
                    return IntoxicationVertigoPhase.Rest;
            }
        }
    }
}
