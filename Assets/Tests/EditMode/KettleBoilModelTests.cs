using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The kettle's boil is a pure model so that every claim the effect
    /// makes about it - one phase for lid and steam, a vent that fires
    /// exactly once per cycle, amplitudes that never leave their authored
    /// band, a delta that is accelerated without changing what happens -
    /// can be pinned here without a scene, a bone or a particle system.
    /// </summary>
    public class KettleBoilModelTests
    {
        private const float FrameSeconds = 1f / 60f;
        private const float DistantMultiplier = 2.75f;
        private const int LongRunSteps = 3000;
        private const float Tolerance = 0.000001f;

        [Test]
        public void SameSeed_ProducesIdenticalSequences()
        {
            var first = new KettleBoilModel(305521u);
            var second = new KettleBoilModel(305521u);
            Assert.That(first.CycleTime, Is.EqualTo(second.CycleTime));
            Assert.That(first.Period, Is.EqualTo(second.Period));
            for (int step = 0; step < LongRunSteps; step++)
            {
                first.Advance(FrameSeconds);
                second.Advance(FrameSeconds);
                Assert.That(first.LidLift, Is.EqualTo(second.LidLift));
                Assert.That(first.LidTilt, Is.EqualTo(second.LidTilt));
                Assert.That(first.SteamRate, Is.EqualTo(second.SteamRate));
                Assert.That(
                    first.VentJustFired,
                    Is.EqualTo(second.VentJustFired));
                Assert.That(first.Period, Is.EqualTo(second.Period));
            }
        }

        [Test]
        public void DifferentSeeds_StartAtDifferentPointsOfTheCycle()
        {
            // Three kettles on one street must not vent in step: the pool
            // hands each instance its own index as a seed.
            var seeds = new uint[] { 0u, 1u, 2u, 3u, 7u };
            for (int a = 0; a < seeds.Length; a++)
            {
                for (int b = a + 1; b < seeds.Length; b++)
                {
                    var first = new KettleBoilModel(seeds[a]);
                    var second = new KettleBoilModel(seeds[b]);
                    Assert.That(
                        Mathf.Abs(first.CycleTime - second.CycleTime),
                        Is.GreaterThan(0.01f),
                        $"Seeds {seeds[a]} and {seeds[b]} start the cycle " +
                        "at the same moment.");
                }
            }
        }

        [Test]
        public void SixtySeconds_VentBetweenNineteenAndTwentyEightTimes()
        {
            var model = new KettleBoilModel(11u);
            int vents = 0;
            int steps = Mathf.RoundToInt(60f / FrameSeconds);
            for (int step = 0; step < steps; step++)
            {
                model.Advance(FrameSeconds);
                if (model.VentJustFired)
                {
                    vents++;
                }

                Assert.That(
                    model.Period,
                    Is.InRange(
                        KettleBoilModel.MinimumVentPeriodSeconds,
                        KettleBoilModel.MaximumVentPeriodSeconds));
            }

            Assert.That(vents, Is.InRange(19, 28));
        }

        [Test]
        public void Vent_FiresExactlyOnceOnTheCycleBoundary()
        {
            var model = new KettleBoilModel(5u);
            int guard = 0;
            float previousCycle = model.CycleTime;
            float previousPeriod = model.Period;
            while (!model.VentJustFired)
            {
                previousCycle = model.CycleTime;
                previousPeriod = model.Period;
                model.Advance(FrameSeconds);
                Assert.That(++guard, Is.LessThan(400), "No vent in 6 s.");
            }

            Assert.That(
                previousCycle + FrameSeconds,
                Is.GreaterThanOrEqualTo(previousPeriod - Tolerance),
                "The vent fired before the cycle reached its period.");
            Assert.That(
                model.CycleTime,
                Is.LessThan(FrameSeconds + Tolerance),
                "The cycle did not restart on the vent.");
            model.Advance(FrameSeconds);
            Assert.That(
                model.VentJustFired,
                Is.False,
                "The vent fired twice for one boundary.");
        }

        [Test]
        public void ZeroStep_NeverFiresAndMovesNothing()
        {
            var model = new KettleBoilModel(9u);
            for (int step = 0; step < 100; step++)
            {
                model.Advance(FrameSeconds);
            }

            float cycle = model.CycleTime;
            float lift = model.LidLift;
            Vector2 tilt = model.LidTilt;
            for (int step = 0; step < 1000; step++)
            {
                model.Advance(0f);
                Assert.That(model.VentJustFired, Is.False);
                Assert.That(model.CycleTime, Is.EqualTo(cycle));
                Assert.That(model.LidLift, Is.EqualTo(lift));
                Assert.That(model.LidTilt, Is.EqualTo(tilt));
            }
        }

        [Test]
        public void Outputs_StayInsideTheirAuthoredBands()
        {
            var model = new KettleBoilModel(13u);
            float liftCeiling = KettleBoilModel.VentLidLiftMetres +
                                KettleBoilModel.TrembleLiftMetres;
            float tiltCeiling = KettleBoilModel.VentLidTiltDegrees +
                                KettleBoilModel.TrembleTiltDegrees;
            float steamFloor = KettleBoilModel.SteamPressureFloor *
                               KettleBoilModel.RestSteamRate;
            float peakLift = 0f;
            for (int step = 0; step < LongRunSteps; step++)
            {
                model.Advance(FrameSeconds);
                Assert.That(
                    model.LidLift,
                    Is.InRange(0f, liftCeiling + Tolerance));
                Assert.That(
                    Mathf.Abs(model.LidTilt.x),
                    Is.LessThanOrEqualTo(tiltCeiling + Tolerance));
                Assert.That(
                    Mathf.Abs(model.LidTilt.y),
                    Is.LessThanOrEqualTo(tiltCeiling + Tolerance));
                Assert.That(
                    model.SteamRate,
                    Is.InRange(
                        steamFloor - Tolerance,
                        KettleBoilModel.VentSteamRate + Tolerance));
                Assert.That(model.Pressure, Is.InRange(0f, 1f));
                Assert.That(model.VentAmount, Is.InRange(0f, 1f));
                peakLift = Mathf.Max(peakLift, model.LidLift);
            }

            // The amplitudes are chosen to read at 640x360; a model that
            // never actually reaches its vent lift has been tuned flat.
            Assert.That(
                peakLift,
                Is.GreaterThan(KettleBoilModel.VentLidLiftMetres * 0.9f),
                "The lid never lifted through a vent.");
        }

        [Test]
        public void UnusableSteps_ChangeNothing()
        {
            var model = new KettleBoilModel(21u);
            for (int step = 0; step < 50; step++)
            {
                model.Advance(FrameSeconds);
            }

            float cycle = model.CycleTime;
            float period = model.Period;
            float lift = model.LidLift;
            Vector2 tilt = model.LidTilt;
            float steam = model.SteamRate;
            float[] steps =
            {
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity,
                -1f,
                -FrameSeconds
            };
            foreach (float step in steps)
            {
                model.Advance(step);
                Assert.That(model.VentJustFired, Is.False);
                Assert.That(model.CycleTime, Is.EqualTo(cycle));
                Assert.That(model.Period, Is.EqualTo(period));
                Assert.That(model.LidLift, Is.EqualTo(lift));
                Assert.That(model.LidTilt, Is.EqualTo(tilt));
                Assert.That(model.SteamRate, Is.EqualTo(steam));
            }
        }

        [Test]
        public void HugeStep_IsClampedToOneCycleAtMost()
        {
            var model = new KettleBoilModel(3u);
            model.Advance(1000f);
            Assert.That(
                model.CycleTime,
                Is.LessThanOrEqualTo(
                    KettleBoilModel.MaximumVentPeriodSeconds));
        }

        [Test]
        public void DistantAcceleration_VentsAsOftenPerSecond()
        {
            // A far walker's body runs 2.75x fast and the boil is fed the
            // same delta, so over equal wall time it must vent the same
            // number of times give or take the boundary.
            const float wallSeconds = 55f;
            var nearby = new KettleBoilModel(31u);
            var distant = new KettleBoilModel(31u);
            int nearbySteps = Mathf.RoundToInt(wallSeconds / FrameSeconds);
            float distantStep = FrameSeconds * DistantMultiplier;
            int distantSteps = Mathf.RoundToInt(wallSeconds / distantStep);
            Assert.That(
                distantSteps * distantStep,
                Is.EqualTo(wallSeconds).Within(0.001f),
                "The accelerated run must cover the same wall time.");

            int nearbyVents = 0;
            for (int step = 0; step < nearbySteps; step++)
            {
                nearby.Advance(FrameSeconds);
                if (nearby.VentJustFired)
                {
                    nearbyVents++;
                }
            }

            int distantVents = 0;
            for (int step = 0; step < distantSteps; step++)
            {
                distant.Advance(distantStep);
                if (distant.VentJustFired)
                {
                    distantVents++;
                }
            }

            Assert.That(
                Mathf.Abs(nearbyVents - distantVents),
                Is.LessThanOrEqualTo(1),
                $"{nearbyVents} vents at 1x against {distantVents} at " +
                $"{DistantMultiplier}x.");
        }

        [Test]
        public void VentEnvelope_RisesToItsPeakAndFallsToNothing()
        {
            Assert.That(KettleBoilModel.VentEnvelope(0f), Is.EqualTo(0f));
            Assert.That(
                KettleBoilModel.VentEnvelope(
                    KettleBoilModel.VentAttackFraction),
                Is.EqualTo(1f).Within(Tolerance));
            Assert.That(KettleBoilModel.VentEnvelope(1f), Is.EqualTo(0f));
            Assert.That(KettleBoilModel.VentEnvelope(4f), Is.EqualTo(0f));
            Assert.That(
                KettleBoilModel.VentEnvelope(-0.5f),
                Is.EqualTo(0f));
            Assert.That(
                KettleBoilModel.VentEnvelope(float.NaN),
                Is.EqualTo(0f));

            float previous = 0f;
            for (float x = 0.01f;
                 x <= KettleBoilModel.VentAttackFraction;
                 x += 0.01f)
            {
                float value = KettleBoilModel.VentEnvelope(x);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous));
                previous = value;
            }

            for (float x = KettleBoilModel.VentAttackFraction + 0.01f;
                 x < 1f;
                 x += 0.01f)
            {
                float value = KettleBoilModel.VentEnvelope(x);
                Assert.That(value, Is.LessThanOrEqualTo(previous));
                Assert.That(value, Is.InRange(0f, 1f));
                previous = value;
            }
        }
    }
}
