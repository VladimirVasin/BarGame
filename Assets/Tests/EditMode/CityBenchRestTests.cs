using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBenchRestTests
    {
        [Test]
        public void Planner_OffersReachableSeatsAndSparesTheYard()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityOpenAreaDecorationPlan openArea =
                CityOpenAreaDecorationPlanner.Create(layout);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            CityDecorationPlan decorations =
                CityDecorationPlanner.CreatePlan(layout, fence, night);
            CityBusPlan busPlan = CityBusPlanner.Create(
                layout,
                decorations);
            CityStreetSurfacePlan streetSurface =
                CityStreetSurfacePlanner.Create(layout);
            CityPedestrianPlan pedestrianPlan =
                CityPedestrianPlanner.Create(
                    layout,
                    GameSessionState.DefaultCitySeed,
                    streetSurface);
            System.Collections.Generic.List<CityBenchSitPlan> benches =
                CityBenchSitPlan.CreateAll(
                    layout,
                    openArea,
                    busPlan,
                    decorations);
            CityBenchRestPlan plan = CityBenchRestPlanner.Create(
                benches,
                pedestrianPlan);

            Assert.That(
                plan.Points,
                Is.Not.Empty,
                "The default city must offer restable benches.");
            var seenIds =
                new System.Collections.Generic.HashSet<string>();
            foreach (CityBenchRestPoint point in plan.Points)
            {
                Assert.That(seenIds.Add(point.BenchId), Is.True);
                Assert.That(
                    point.BenchId,
                    Is.Not.EqualTo("home-yard-bench"),
                    "The bar-side yard bench stays outside the ambient pool.");
                Assert.That(
                    point.NodeIndex,
                    Is.InRange(0, pedestrianPlan.Nodes.Count - 1));
                Assert.That(
                    point.NodeDistances.Count,
                    Is.EqualTo(pedestrianPlan.Nodes.Count));
                Assert.That(
                    point.NodeDistances[point.NodeIndex],
                    Is.EqualTo(0f).Within(0.001f));
                Vector3 crossing =
                    pedestrianPlan.Nodes[point.NodeIndex].Position -
                    point.StandSlot;
                crossing.y = 0f;
                Assert.That(
                    crossing.magnitude,
                    Is.LessThanOrEqualTo(
                        CityBenchRestPlanner.MaximumCrossingDistance),
                    point.BenchId);
                Assert.That(
                    point.SitFacing.magnitude,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(point.SitFacing.y, Is.Zero);
                Assert.That(
                    point.SeatPosition.y,
                    Is.GreaterThan(point.StandSlot.y - 0.5f));
            }
        }

        [Test]
        public void SeatClaims_AreExclusiveAndReleasable()
        {
            const string benchId = "test-bench-claims";
            var first = new object();
            var second = new object();
            try
            {
                Assert.That(
                    CityBenchSeatClaims.IsClaimed(benchId),
                    Is.False);
                Assert.That(
                    CityBenchSeatClaims.TryClaim(benchId, first),
                    Is.True);
                Assert.That(
                    CityBenchSeatClaims.TryClaim(benchId, first),
                    Is.True,
                    "A claim must be idempotent for its owner.");
                Assert.That(
                    CityBenchSeatClaims.TryClaim(benchId, second),
                    Is.False);
                Assert.That(
                    CityBenchSeatClaims.IsClaimedByOther(
                        benchId,
                        second),
                    Is.True);
                Assert.That(
                    CityBenchSeatClaims.IsClaimedByOther(
                        benchId,
                        first),
                    Is.False);

                // A stranger's release changes nothing.
                CityBenchSeatClaims.Release(benchId, second);
                Assert.That(
                    CityBenchSeatClaims.IsClaimed(benchId),
                    Is.True);

                CityBenchSeatClaims.Release(benchId, first);
                Assert.That(
                    CityBenchSeatClaims.IsClaimed(benchId),
                    Is.False);
                Assert.That(
                    CityBenchSeatClaims.TryClaim(benchId, second),
                    Is.True);
            }
            finally
            {
                CityBenchSeatClaims.Release(benchId, first);
                CityBenchSeatClaims.Release(benchId, second);
            }
        }
    }
}
