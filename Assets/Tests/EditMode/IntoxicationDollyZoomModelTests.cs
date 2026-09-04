using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class IntoxicationDollyZoomModelTests
    {
        private const float Step = 1f / 120f;
        private const int Seed = 4242;

        [Test]
        public void ZeroStrength_StaysExactlyAtRest()
        {
            var model = new IntoxicationDollyZoomModel(Seed);

            for (int index = 0; index < 120 * 60; index++)
            {
                model.Advance(Step, 0f, 1f, true);
                Assert.That(model.Exponent, Is.EqualTo(0f));
                Assert.That(
                    model.Phase,
                    Is.EqualTo(IntoxicationDollyZoomPhase.Rest));
            }
        }

        [Test]
        public void SameSeed_ReplaysTheSameBreath_AndSeedsDiffer()
        {
            var first = new IntoxicationDollyZoomModel(Seed);
            var second = new IntoxicationDollyZoomModel(Seed);
            var other = new IntoxicationDollyZoomModel(Seed + 1);
            bool differed = false;

            for (int index = 0; index < 120 * 30; index++)
            {
                first.Advance(Step, 1f, 1f, true);
                second.Advance(Step, 1f, 1f, true);
                other.Advance(Step, 1f, 1f, true);
                Assert.That(second.Exponent, Is.EqualTo(first.Exponent));
                if (!Mathf.Approximately(first.Exponent, other.Exponent))
                {
                    differed = true;
                }
            }

            Assert.That(differed, Is.True);
        }

        [Test]
        public void FullStrength_BreathesOutHoldsComesBackAndHoldsAgain()
        {
            var model = new IntoxicationDollyZoomModel(Seed);
            var transitions = new List<IntoxicationDollyZoomPhase>();
            IntoxicationDollyZoomPhase previous = model.Phase;
            float expectedHold = model.PhaseDuration;
            float heldFor = 0f;
            int peaks = 0;

            for (int index = 0; index < 120 * 40; index++)
            {
                model.Advance(Step, 1f, 1f, true);
                if (model.Phase != previous)
                {
                    Assert.That(
                        model.Phase,
                        Is.EqualTo(NextPhase(previous)),
                        "The breath must run rest, out, peak, back, rest.");
                    if (previous == IntoxicationDollyZoomPhase.Peak ||
                        previous == IntoxicationDollyZoomPhase.Rest)
                    {
                        Assert.That(
                            heldFor,
                            Is.GreaterThanOrEqualTo(expectedHold - 2f * Step),
                            $"The {previous} hold ended early.");
                    }

                    transitions.Add(model.Phase);
                    previous = model.Phase;
                    heldFor = 0f;
                    expectedHold = model.PhaseDuration;
                    if (model.Phase == IntoxicationDollyZoomPhase.Peak)
                    {
                        peaks++;
                        Assert.That(
                            model.PeakHoldSeconds,
                            Is.GreaterThanOrEqualTo(
                                model.OutLegSeconds *
                                IntoxicationDollyZoomModel
                                    .PeakHoldMinimumFraction -
                                0.0001f));
                        Assert.That(
                            model.CycleAmplitude,
                            Is.InRange(
                                IntoxicationDollyZoomModel
                                    .MinimumAmplitudeFraction -
                                0.0001f,
                                1f + 0.0001f));
                    }
                    else if (model.Phase == IntoxicationDollyZoomPhase.Rest)
                    {
                        Assert.That(
                            model.RestHoldSeconds,
                            Is.GreaterThanOrEqualTo(
                                model.BackLegSeconds *
                                IntoxicationDollyZoomModel
                                    .RestHoldMinimumFraction -
                                0.0001f));
                    }
                }
                else
                {
                    heldFor += Step;
                }

                Assert.That(
                    Mathf.Abs(model.Exponent),
                    Is.LessThanOrEqualTo(model.CycleAmplitude + 0.00001f));
                switch (model.Phase)
                {
                    case IntoxicationDollyZoomPhase.Peak:
                        Assert.That(
                            Mathf.Abs(model.Exponent),
                            Is.EqualTo(model.CycleAmplitude).Within(0.00001f),
                            "The peak lingers at the full reach.");
                        break;
                    case IntoxicationDollyZoomPhase.Rest:
                        Assert.That(
                            model.Exponent,
                            Is.EqualTo(0f),
                            "Rest is exactly zero.");
                        break;
                }
            }

            Assert.That(
                peaks,
                Is.GreaterThanOrEqualTo(3),
                "Forty seconds at the top pace must breathe several times.");
        }

        [Test]
        public void Steps_StaySmooth_AcrossEveryPhaseBoundary()
        {
            float steepest = 0f;
            const float sample = 1f / 4000f;
            float[] shapes =
            {
                IntoxicationDollyZoomModel.MinimumShapeExponent,
                0.8f,
                1f,
                1.3f,
                IntoxicationDollyZoomModel.MaximumShapeExponent
            };
            foreach (float shape in shapes)
            {
                for (float t = 0f; t < 1f; t += sample)
                {
                    float slope =
                        (IntoxicationDollyZoomModel.Ease(t + sample, shape) -
                         IntoxicationDollyZoomModel.Ease(t, shape)) /
                        sample;
                    steepest = Mathf.Max(steepest, slope);
                }
            }

            Assert.That(
                steepest,
                Is.InRange(1.5f, 4f),
                "The easing's steepest slope is what bounds a frame step.");
            float bound =
                steepest /
                IntoxicationDollyZoomModel.FastLegMinimumSeconds *
                Step *
                1.05f +
                0.0001f;

            var model = new IntoxicationDollyZoomModel(Seed);
            float previous = model.Exponent;
            float largest = 0f;
            for (int index = 0; index < 120 * 60; index++)
            {
                model.Advance(Step, 1f, 1f, true);
                float change = Mathf.Abs(model.Exponent - previous);
                largest = Mathf.Max(largest, change);
                Assert.That(
                    change,
                    Is.LessThanOrEqualTo(bound),
                    $"Step {index} in {model.Phase} jumped.");
                previous = model.Exponent;
            }

            Assert.That(
                largest,
                Is.GreaterThan(bound * 0.2f),
                "The breath must actually move.");
        }

        [Test]
        public void HigherPace_DrawsShorterLegs()
        {
            List<float> slow = CollectOutLegs(0f, 240f);
            List<float> fast = CollectOutLegs(1f, 120f);

            Assert.That(slow.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(fast.Count, Is.GreaterThanOrEqualTo(8));
            foreach (float leg in slow)
            {
                Assert.That(
                    leg,
                    Is.InRange(
                        IntoxicationDollyZoomModel.SlowLegMinimumSeconds,
                        IntoxicationDollyZoomModel.SlowLegMaximumSeconds));
            }

            foreach (float leg in fast)
            {
                Assert.That(
                    leg,
                    Is.InRange(
                        IntoxicationDollyZoomModel.FastLegMinimumSeconds,
                        IntoxicationDollyZoomModel.FastLegMaximumSeconds));
            }

            Assert.That(Mean(fast), Is.LessThan(Mean(slow)));
        }

        [Test]
        public void StrengthDroppingMidCycle_FinishesTheBreathThenRests()
        {
            var model = new IntoxicationDollyZoomModel(Seed);
            int guard = 0;
            while (!(model.Phase == IntoxicationDollyZoomPhase.Out &&
                     model.PhaseElapsed > 0.2f) &&
                   guard++ < 120 * 20)
            {
                model.Advance(Step, 1f, 1f, true);
            }

            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Out));
            float amplitude = model.CycleAmplitude;
            float sign = model.CycleSign;
            bool sawPeak = false;
            guard = 0;
            while (model.Phase != IntoxicationDollyZoomPhase.Rest &&
                   guard++ < 120 * 30)
            {
                model.Advance(Step, 0f, 1f, true);
                Assert.That(
                    model.CycleAmplitude,
                    Is.EqualTo(amplitude),
                    "A running breath keeps the reach it latched.");
                if (model.Phase == IntoxicationDollyZoomPhase.Peak)
                {
                    sawPeak = true;
                    Assert.That(
                        model.Exponent,
                        Is.EqualTo(sign * amplitude).Within(0.00001f));
                }
            }

            Assert.That(sawPeak, Is.True);
            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Rest));
            for (int index = 0; index < 120 * 30; index++)
            {
                model.Advance(Step, 0f, 1f, true);
                Assert.That(model.Exponent, Is.EqualTo(0f));
                Assert.That(
                    model.Phase,
                    Is.EqualTo(IntoxicationDollyZoomPhase.Rest));
            }
        }

        [Test]
        public void NarrowDisallowed_OnlyPushesIn_AndRoomLetsBothSidesBreathe()
        {
            var confined = new IntoxicationDollyZoomModel(Seed);
            var open = new IntoxicationDollyZoomModel(Seed);
            bool pulledBack = false;
            bool pushedIn = false;
            bool confinedPushedIn = false;

            for (int index = 0; index < 120 * 120; index++)
            {
                confined.Advance(Step, 1f, 1f, false);
                open.Advance(Step, 1f, 1f, true);
                Assert.That(
                    confined.Exponent,
                    Is.GreaterThanOrEqualTo(0f),
                    "With no room behind the camera the breath only pushes in.");
                confinedPushedIn |= confined.Exponent > 0.1f;
                pulledBack |= open.Exponent < -0.1f;
                pushedIn |= open.Exponent > 0.1f;
            }

            Assert.That(confinedPushedIn, Is.True);
            Assert.That(
                pulledBack,
                Is.True,
                "With room behind the camera some breaths pull back.");
            Assert.That(
                pushedIn,
                Is.True,
                "With room behind the camera most breaths still push in.");
        }

        [Test]
        public void ALargeStep_IsClampedAndALeftoverCarriesAcrossABoundary()
        {
            var model = new IntoxicationDollyZoomModel(Seed);
            model.Advance(0.5f, 1f, 1f, true);
            Assert.That(
                model.PhaseElapsed,
                Is.EqualTo(IntoxicationDollyZoomModel.MaximumStepSeconds)
                    .Within(0.00001f));
            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Rest));

            model.Reset(0.05f);
            model.Advance(0.1f, 1f, 1f, true);
            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Out));
            Assert.That(model.PhaseElapsed, Is.EqualTo(0.05f).Within(0.00001f));

            float before = model.Exponent;
            model.Advance(-1f, 1f, 1f, true);
            model.Advance(float.NaN, 1f, 1f, true);
            model.Advance(0f, 1f, 1f, true);
            Assert.That(model.Exponent, Is.EqualTo(before));
        }

        [Test]
        public void Reset_ReturnsToRestAndHoldsForTheGivenTime()
        {
            var model = new IntoxicationDollyZoomModel(Seed);
            int guard = 0;
            while (model.Phase != IntoxicationDollyZoomPhase.Peak &&
                   guard++ < 120 * 20)
            {
                model.Advance(Step, 1f, 1f, true);
            }

            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Peak));
            model.Reset(0.5f);
            Assert.That(model.Exponent, Is.EqualTo(0f));
            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Rest));
            Assert.That(model.PhaseDuration, Is.EqualTo(0.5f));

            for (int index = 0; index < 48; index++)
            {
                model.Advance(Step, 1f, 1f, true);
                Assert.That(
                    model.Phase,
                    Is.EqualTo(IntoxicationDollyZoomPhase.Rest));
            }

            for (int index = 0; index < 24; index++)
            {
                model.Advance(Step, 1f, 1f, true);
            }

            Assert.That(model.Phase, Is.EqualTo(IntoxicationDollyZoomPhase.Out));
        }

        private static IntoxicationDollyZoomPhase NextPhase(
            IntoxicationDollyZoomPhase phase)
        {
            switch (phase)
            {
                case IntoxicationDollyZoomPhase.Rest:
                    return IntoxicationDollyZoomPhase.Out;
                case IntoxicationDollyZoomPhase.Out:
                    return IntoxicationDollyZoomPhase.Peak;
                case IntoxicationDollyZoomPhase.Peak:
                    return IntoxicationDollyZoomPhase.Back;
                default:
                    return IntoxicationDollyZoomPhase.Rest;
            }
        }

        private static List<float> CollectOutLegs(float pace, float seconds)
        {
            var model = new IntoxicationDollyZoomModel(Seed);
            var legs = new List<float>();
            IntoxicationDollyZoomPhase previous = model.Phase;
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                model.Advance(Step, 1f, pace, true);
                if (model.Phase != previous &&
                    model.Phase == IntoxicationDollyZoomPhase.Out)
                {
                    legs.Add(model.OutLegSeconds);
                }

                previous = model.Phase;
            }

            return legs;
        }

        private static float Mean(List<float> values)
        {
            float sum = 0f;
            foreach (float value in values)
            {
                sum += value;
            }

            return sum / values.Count;
        }
    }
}
