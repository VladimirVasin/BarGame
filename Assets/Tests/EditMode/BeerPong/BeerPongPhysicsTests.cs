using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests
{
    public sealed class BeerPongPhysicsTests
    {
        [Test]
        public void DownwardMouthCrossing_SinksWithoutTunneling()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            BeerPongCupDefinition cup = layout.GetCup(2);
            var simulation = new BeerPongPhysicsSimulation(layout);
            simulation.Launch(
                cup.MouthCenter + Vector3.up * 0.5f,
                Vector3.down * 120f,
                1 << cup.Index);

            simulation.StepFixed();

            Assert.That(simulation.Status, Is.EqualTo(
                BeerPongBallStatus.Sunk));
            Assert.That(simulation.Result.CupIndex, Is.EqualTo(cup.Index));
            Assert.That(simulation.Result.IsBankShot, Is.False);
            Assert.That(simulation.Result.MissReason, Is.EqualTo(
                BeerPongMissReason.None));
        }

        [Test]
        public void InactiveCup_CannotCaptureBall()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            BeerPongCupDefinition cup = layout.GetCup(0);
            var simulation = new BeerPongPhysicsSimulation(layout);
            simulation.Launch(
                cup.MouthCenter + Vector3.up * 0.25f,
                Vector3.down,
                0);

            RunToCompletion(simulation);

            Assert.That(simulation.Status, Is.EqualTo(
                BeerPongBallStatus.Missed));
            Assert.That(simulation.Result.CupIndex, Is.EqualTo(-1));
        }

        [Test]
        public void RimContact_ReboundsBallAndRecordsCollision()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            BeerPongCupDefinition cup = layout.GetCup(0);
            var simulation = new BeerPongPhysicsSimulation(layout);
            simulation.Launch(
                cup.MouthCenter +
                Vector3.right * cup.MouthRadius +
                Vector3.up * 0.2f,
                Vector3.down * 3f,
                1 << cup.Index);

            for (int step = 0;
                 step < 60 &&
                 simulation.Snapshot.RimBounceCount == 0;
                 step++)
            {
                simulation.StepFixed();
            }

            Assert.That(simulation.IsInFlight, Is.True);
            Assert.That(simulation.Snapshot.RimBounceCount, Is.EqualTo(1));
            Assert.That(simulation.Snapshot.Velocity.y, Is.GreaterThan(0f));
        }

        [Test]
        public void FastTableCrossing_UsesSweptBounce()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            var simulation = new BeerPongPhysicsSimulation(layout);
            simulation.Launch(
                new Vector3(0f, 1f, 1f),
                Vector3.down * 80f,
                0);

            simulation.StepFixed();
            simulation.StepFixed();

            Assert.That(
                simulation.Snapshot.TableBounceCount,
                Is.EqualTo(1));
            Assert.That(
                simulation.Snapshot.Position.y,
                Is.GreaterThanOrEqualTo(
                    layout.TableSurfaceY + layout.BallRadius));
            Assert.That(simulation.Snapshot.Velocity.y, Is.GreaterThan(0f));
        }

        [Test]
        public void TableBounceBeforeSink_IsReportedAsBankShot()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            var simulation = new BeerPongPhysicsSimulation(layout);
            simulation.Launch(
                layout.ThrowOrigin,
                new Vector3(0f, -1f, 6.725f),
                1);

            RunToCompletion(simulation);

            Assert.That(simulation.Status, Is.EqualTo(
                BeerPongBallStatus.Sunk));
            Assert.That(simulation.Result.CupIndex, Is.EqualTo(0));
            Assert.That(simulation.Result.TableBounceCount, Is.GreaterThan(0));
            Assert.That(simulation.Result.IsBankShot, Is.True);
        }

        [Test]
        public void BallOutsideSafetyVolume_EndsAsOutOfBounds()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            BeerPongPhysicsSettings settings =
                BeerPongPhysicsSettings.Default;
            var simulation = new BeerPongPhysicsSimulation(layout, settings);
            simulation.Launch(
                new Vector3(
                    layout.TableHalfWidth +
                    settings.OutOfBoundsMargin -
                    0.01f,
                    1f,
                    1f),
                Vector3.right * 5f,
                0);

            RunToCompletion(simulation);

            Assert.That(simulation.Result.MissReason, Is.EqualTo(
                BeerPongMissReason.OutOfBounds));
        }

        [Test]
        public void LowEnergyBall_EndsAsSettled()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            var simulation = new BeerPongPhysicsSimulation(layout);
            simulation.Launch(
                new Vector3(
                    0f,
                    layout.TableSurfaceY + layout.BallRadius + 0.01f,
                    1f),
                Vector3.down * 0.1f,
                0);

            RunToCompletion(simulation);

            Assert.That(simulation.Result.MissReason, Is.EqualTo(
                BeerPongMissReason.Settled));
            Assert.That(
                simulation.Result.FlightTime,
                Is.LessThan(BeerPongPhysicsSettings.Default.MaxFlightDuration));
        }

        [Test]
        public void FlightEndsAtConfiguredFiveSecondStyleTimeout()
        {
            var settings = new BeerPongPhysicsSettings(
                maxFlightDuration: 0.02f,
                outOfBoundsMargin: 100f,
                maximumHeight: 100f);
            var simulation = new BeerPongPhysicsSimulation(
                BeerPongTableLayout.Default,
                settings);
            simulation.Launch(
                new Vector3(0f, 2f, 1f),
                Vector3.up,
                0);

            RunToCompletion(simulation);

            Assert.That(simulation.Result.MissReason, Is.EqualTo(
                BeerPongMissReason.Timeout));
            Assert.That(
                simulation.Result.FlightTime,
                Is.GreaterThanOrEqualTo(settings.MaxFlightDuration));
        }

        [Test]
        public void RenderAccumulator_MatchesDirectFixedStepping()
        {
            var renderDriven = new BeerPongPhysicsSimulation();
            var fixedDriven = new BeerPongPhysicsSimulation();
            Vector3 origin = new Vector3(0f, 1.5f, 0.5f);
            Vector3 velocity = new Vector3(0.4f, 3f, 2f);
            renderDriven.Launch(origin, velocity, 0);
            fixedDriven.Launch(origin, velocity, 0);

            for (int frame = 0; frame < 12; frame++)
            {
                Assert.That(
                    renderDriven.Advance(1f / 30f),
                    Is.EqualTo(4));
            }

            for (int step = 0; step < 48; step++)
            {
                fixedDriven.StepFixed();
            }

            AssertSnapshotsEqual(
                renderDriven.Snapshot,
                fixedDriven.Snapshot);
            Assert.That(
                renderDriven.InterpolationAlpha,
                Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void EveryCup_IsReachableThroughPlayerAimRanges()
        {
            BeerPongTableLayout layout = BeerPongTableLayout.Default;
            var simulation = new BeerPongPhysicsSimulation(layout);

            for (int cupIndex = 0;
                 cupIndex < BeerPongTableLayout.CupCount;
                 cupIndex++)
            {
                BeerPongCupDefinition cup = layout.GetCup(cupIndex);
                Vector3 horizontalTarget =
                    cup.MouthCenter - layout.ThrowOrigin;
                float yaw = Mathf.Atan2(
                    horizontalTarget.x,
                    horizontalTarget.z) * Mathf.Rad2Deg;
                bool reached = false;

                for (float pitch = BeerPongAim.MinimumPitchDegrees;
                     pitch <= BeerPongAim.MaximumPitchDegrees && !reached;
                     pitch += 0.5f)
                {
                    for (float power =
                             BeerPongMinigameController.MinimumChargePower;
                         power <= 1.0001f && !reached;
                         power += 0.01f)
                    {
                        simulation.LaunchFromAim(
                            yaw,
                            pitch,
                            power,
                            1 << cupIndex);
                        RunToCompletion(simulation);
                        reached =
                            simulation.Status ==
                                BeerPongBallStatus.Sunk &&
                            simulation.Result.CupIndex == cupIndex;
                    }
                }

                Assert.That(
                    reached,
                    Is.True,
                    $"Cup {cupIndex} must be reachable through the " +
                    "same yaw, pitch and charge ranges exposed to players.");
            }
        }

        [Test]
        public void InvalidLaunchAndDelta_AreRejected()
        {
            var simulation = new BeerPongPhysicsSimulation();

            Assert.Throws<System.ArgumentException>(() => simulation.Launch(
                Vector3.zero,
                Vector3.zero,
                0));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => simulation.Advance(-0.1f));
            Assert.Throws<System.InvalidOperationException>(
                () => simulation.StepFixed());
        }

        private static BeerPongFlightResult RunToCompletion(
            BeerPongPhysicsSimulation simulation)
        {
            const int maxSteps = 1000;
            for (int step = 0;
                 step < maxSteps && simulation.IsInFlight;
                 step++)
            {
                simulation.StepFixed();
            }

            Assert.That(
                simulation.IsComplete,
                Is.True,
                "The physics simulation did not reach a terminal state.");
            return simulation.Result;
        }

        private static void AssertSnapshotsEqual(
            BeerPongBallSnapshot first,
            BeerPongBallSnapshot second)
        {
            Assert.That(first.Status, Is.EqualTo(second.Status));
            Assert.That(
                first.Position.x,
                Is.EqualTo(second.Position.x).Within(0.00001f));
            Assert.That(
                first.Position.y,
                Is.EqualTo(second.Position.y).Within(0.00001f));
            Assert.That(
                first.Position.z,
                Is.EqualTo(second.Position.z).Within(0.00001f));
            Assert.That(
                first.Velocity.x,
                Is.EqualTo(second.Velocity.x).Within(0.00001f));
            Assert.That(
                first.Velocity.y,
                Is.EqualTo(second.Velocity.y).Within(0.00001f));
            Assert.That(
                first.Velocity.z,
                Is.EqualTo(second.Velocity.z).Within(0.00001f));
            Assert.That(
                first.TableBounceCount,
                Is.EqualTo(second.TableBounceCount));
            Assert.That(
                first.RimBounceCount,
                Is.EqualTo(second.RimBounceCount));
        }
    }
}
