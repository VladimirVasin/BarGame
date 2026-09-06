using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The drops a shut tap sheds: a rate-driven patter first, then the
    /// static hold's fixed run of four with growing gaps, every one of
    /// them landed before the three seconds are up, and nothing after.
    /// </summary>
    public sealed class HomeShowerDripModelTests
    {
        private const float Step = 1f / 60f;

        [Test]
        public void TheHoldTimesAreAGeometricRun()
        {
            Assert.That(HomeShowerDripModel.HoldDropTime(0), Is.EqualTo(0.30f).Within(0.0001f));
            Assert.That(HomeShowerDripModel.HoldDropTime(1), Is.EqualTo(0.72f).Within(0.0001f));
            Assert.That(HomeShowerDripModel.HoldDropTime(2), Is.EqualTo(1.308f).Within(0.0001f));
            Assert.That(HomeShowerDripModel.HoldDropTime(3), Is.EqualTo(2.1312f).Within(0.0001f));
            Assert.That(
                HomeShowerDripModel.LastHoldLandingSeconds,
                Is.LessThan(HomeShowerSceneTimeline.DripHoldSeconds),
                "The last drop must land inside the static hold.");
            Assert.Throws<ArgumentOutOfRangeException>(() => HomeShowerDripModel.HoldDropTime(-1));
        }

        [Test]
        public void TheHoldShedsItsRunWithGrowingGapsAndGoesDry()
        {
            var model = new HomeShowerDripModel();
            model.BeginHold();
            var emissions = new List<float>();
            int landed = 0;
            float elapsed = 0f;
            while (elapsed < HomeShowerSceneTimeline.DripHoldSeconds - 0.0001f)
            {
                int drops = model.Advance(Step, 99f);
                elapsed += Step;
                for (int drop = 0; drop < drops; drop++)
                {
                    emissions.Add(elapsed);
                }

                landed += model.ConsumeLandings();
            }

            Assert.That(emissions.Count, Is.EqualTo(HomeShowerDripModel.HoldDropCount));
            Assert.That(model.HoldEmitted, Is.EqualTo(HomeShowerDripModel.HoldDropCount));
            for (int index = 2; index < emissions.Count; index++)
            {
                float previousGap = emissions[index - 1] - emissions[index - 2];
                float gap = emissions[index] - emissions[index - 1];
                Assert.That(gap, Is.GreaterThan(previousGap * 1.2f), "Each pause must be longer than the last.");
            }

            Assert.That(landed, Is.EqualTo(HomeShowerDripModel.HoldDropCount), "Every drop lands before the hold ends.");
            Assert.That(model.PendingLandings, Is.Zero);
            Assert.That(model.IsDry, Is.True);
            Assert.That(model.Advance(Step, 99f), Is.Zero, "Nothing after the run, whatever rate is offered.");
            Assert.That(model.TotalEmitted, Is.EqualTo(HomeShowerDripModel.HoldDropCount));
            Assert.That(model.TotalLanded, Is.EqualTo(HomeShowerDripModel.HoldDropCount));
        }

        [Test]
        public void ADropLandsAFallLater()
        {
            var model = new HomeShowerDripModel();
            model.BeginHold();
            float elapsed = 0f;
            while (model.HoldEmitted == 0)
            {
                model.Advance(Step, 0f);
                elapsed += Step;
            }

            Assert.That(elapsed, Is.EqualTo(HomeShowerDripModel.HoldFirstGapSeconds).Within(Step));
            Assert.That(model.PendingLandings, Is.EqualTo(1));
            Assert.That(model.ConsumeLandings(), Is.Zero);
            float fall = 0f;
            while (fall < HomeShowerDripModel.FallSeconds - Step)
            {
                model.Advance(Step, 0f);
                fall += Step;
                Assert.That(model.ConsumeLandings(), Is.Zero, "Still in the air.");
            }

            model.Advance(Step * 2f, 0f);
            Assert.That(model.ConsumeLandings(), Is.EqualTo(1));
        }

        [Test]
        public void ThePatterIsRateDrivenAndKeepsFractions()
        {
            var model = new HomeShowerDripModel();
            int drops = 0;
            for (int frame = 0; frame < 60; frame++)
            {
                drops += model.Advance(Step, HomeShowerDripModel.SteadyDropsPerSecond);
            }

            Assert.That(drops, Is.EqualTo(Mathf.RoundToInt(HomeShowerDripModel.SteadyDropsPerSecond)).Within(1));
            Assert.That(model.Advance(Step, 0f), Is.Zero, "Rate zero sheds nothing.");
            Assert.That(model.HoldActive, Is.False);
            Assert.That(model.IsDry, Is.False, "A patter that never went static is not dry.");
        }

        [Test]
        public void TheRunIsDeterministic()
        {
            var first = new HomeShowerDripModel();
            var second = new HomeShowerDripModel();
            var steps = new[] { 0.016f, 0.033f, 0.010f, 0.050f, 0.016f, 0.016f, 0.100f, 0.016f };
            for (int round = 0; round < 40; round++)
            {
                float step = steps[round % steps.Length];
                if (round == 12)
                {
                    first.BeginHold();
                    second.BeginHold();
                }

                Assert.That(first.Advance(step, 4f), Is.EqualTo(second.Advance(step, 4f)));
                Assert.That(first.ConsumeLandings(), Is.EqualTo(second.ConsumeLandings()));
            }

            Assert.That(first.TotalEmitted, Is.EqualTo(second.TotalEmitted));
            Assert.That(first.TotalLanded, Is.EqualTo(second.TotalLanded));
        }

        [Test]
        public void ResetClearsTheBasin()
        {
            var model = new HomeShowerDripModel();
            model.Advance(0.5f, 4f);
            model.BeginHold();
            model.Advance(0.5f, 0f);
            Assert.That(model.TotalEmitted, Is.GreaterThan(0));
            model.Reset();
            Assert.That(model.Clock, Is.Zero);
            Assert.That(model.TotalEmitted, Is.Zero);
            Assert.That(model.TotalLanded, Is.Zero);
            Assert.That(model.PendingLandings, Is.Zero);
            Assert.That(model.HoldActive, Is.False);
            Assert.That(model.HoldEmitted, Is.Zero);
        }

        [Test]
        public void AdvanceRejectsNonFiniteInput()
        {
            var model = new HomeShowerDripModel();
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Advance(float.NaN, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => model.Advance(0.1f, float.PositiveInfinity));
        }
    }
}
