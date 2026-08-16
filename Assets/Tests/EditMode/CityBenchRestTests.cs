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
                    decorations,
                    streetSurface);
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
        public void CreateAll_DocksResolvedSeatsOnTheWalkableSurface()
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
            System.Collections.Generic.List<CityBenchSitPlan> benches =
                CityBenchSitPlan.CreateAll(
                    layout,
                    openArea,
                    busPlan,
                    decorations,
                    streetSurface);

            // Every seat whose plan resolves its dock ground: the seats
            // hidden in street decorations and the shelter bench of
            // every bus stop.
            var resolvedSeats =
                new System.Collections.Generic.List<CityBenchSeat>();
            foreach (CityDecorationDescriptor descriptor in
                     decorations.Descriptors)
            {
                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    descriptor,
                    resolvedSeats);
            }

            Assert.That(
                resolvedSeats,
                Is.Not.Empty,
                "The default city must hide seats in its decorations.");
            Assert.That(
                busPlan.Stops,
                Is.Not.Empty,
                "The default city must run bus stops.");
            foreach (CityBusStopDescriptor stop in busPlan.Stops)
            {
                resolvedSeats.Add(
                    CityBusStopWorldBuilder.DescribeShelterBenchSeat(
                        stop));
            }

            int regroundedSeats = 0;
            foreach (CityBenchSeat seat in resolvedSeats)
            {
                CityBenchSitPlan plan = benches.Find(
                    candidate => candidate.Id == seat.Id);
                Assert.That(
                    plan.IsPresent,
                    Is.True,
                    seat.Id);

                // The sitter docks in front of the seat; the plan must
                // stand him on the surface that is really there — the
                // continuous district ground, the raised sidewalk and
                // park path strips, or the carriageway a kerb-side dock
                // overhangs.
                Vector3 dock = seat.SeatTopCenter + seat.FaceDirection *
                    (seat.SeatDepth * 0.5f +
                     CityBenchSitPlan.EntryEdgeDistance);
                float expectedGround = seat.GroundY;
                if (CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        new Vector2(dock.x, dock.z),
                        out float terrainTop,
                        out _))
                {
                    expectedGround = terrainTop;
                }

                expectedGround = SampleWalkwayTops(
                    streetSurface.SidewalkGeometry,
                    dock,
                    expectedGround);
                expectedGround = SampleWalkwayTops(
                    streetSurface.ParkPathGeometry,
                    dock,
                    expectedGround);
                expectedGround = SampleWalkwayTops(
                    streetSurface.StreetGeometry,
                    dock,
                    expectedGround);

                Assert.That(
                    plan.EntryRootPosition.y,
                    Is.EqualTo(
                        expectedGround +
                        PlayerFactory.GroundedRootOffset).Within(0.001f),
                    seat.Id);
                if (Mathf.Abs(expectedGround - seat.GroundY) > 0.001f)
                {
                    regroundedSeats++;
                }
            }

            // The guard is only meaningful while the generated city
            // really docks some seats off their described ground plane —
            // on sloped lots, on the kerb-high sidewalk strip and on
            // graded roadside edges.
            Assert.That(
                regroundedSeats,
                Is.GreaterThan(0),
                "No seat needed re-grounding; the dock ground " +
                "resolution has become vacuous.");
        }

        private static float SampleWalkwayTops(
            System.Collections.Generic.IReadOnlyList<RuntimeOrientedBox>
                walkways,
            Vector3 position,
            float groundY)
        {
            foreach (RuntimeOrientedBox walkway in walkways)
            {
                if (walkway.TrySampleTop(position, out float top))
                {
                    groundY = Mathf.Max(groundY, top);
                }
            }

            return groundY;
        }

        [Test]
        public void ApproachWaypoints_RouteAroundTheTimber()
        {
            // A 2 m plank at the origin, 0.6 m deep, facing +Z; the
            // entry dock waits at z = 0.3 + EntryEdgeDistance.
            var seat = new CityBenchSeat(
                "test-approach-bench",
                new Vector3(0f, 0.7f, 0f),
                2f,
                0.6f,
                0f,
                Vector3.forward);
            var plan = new CityBenchSitPlan(seat);
            var buffer = new Vector3[
                CityBenchSitPlan.MaximumApproachWaypoints];
            float corridorX = 1f + CityBenchSeat.DefaultApproachClearance;
            float frontZ = 0.3f + CityBenchSitPlan.EntryEdgeDistance;

            // Head-on: no detour.
            Assert.That(
                plan.BuildApproachWaypoints(
                    new Vector3(0.4f, 0f, 2f),
                    buffer),
                Is.Zero);

            // Beside the east plank end: one corner on that side, level
            // with the dock.
            Assert.That(
                plan.BuildApproachWaypoints(
                    new Vector3(2.5f, 0f, 0f),
                    buffer),
                Is.EqualTo(1));
            Assert.That(buffer[0].x, Is.EqualTo(corridorX).Within(0.001f));
            Assert.That(buffer[0].z, Is.EqualTo(frontZ).Within(0.001f));

            // Behind, west of centre: rear corner first, then the front
            // corner, both on the west side.
            float rearZ = -(0.3f + Mathf.Max(
                CityBenchSitPlan.EntryEdgeDistance,
                CityBenchSeat.DefaultApproachClearance));
            Assert.That(
                plan.BuildApproachWaypoints(
                    new Vector3(-0.2f, 0f, -1.5f),
                    buffer),
                Is.EqualTo(2));
            Assert.That(
                buffer[0].x,
                Is.EqualTo(-corridorX).Within(0.001f));
            Assert.That(buffer[0].z, Is.EqualTo(rearZ).Within(0.001f));
            Assert.That(
                buffer[1].x,
                Is.EqualTo(-corridorX).Within(0.001f));
            Assert.That(buffer[1].z, Is.EqualTo(frontZ).Within(0.001f));
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
