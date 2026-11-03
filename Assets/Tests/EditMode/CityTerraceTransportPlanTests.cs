using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityTerraceTransportPlanTests
    {
        private const float Tolerance = .001f;

        [Test]
        public void SwitchbacksKeepPhysicalGradesFlatLandingsAndExposeExistingCityConflicts()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var heights = layout.ElevationPlan.NodeElevations.ToDictionary(pair => pair.Key, pair => pair.Value);
            Vector3 homeDoor = layout.PlayerHome.DoorPosition;
            Vector3 homeReturn = layout.PlayerHome.ReturnPosition;
            BuildingLot bar = layout.BuildingLots.First(lot => lot.IsBar);
            Vector3 barDoor = bar.DoorPosition, barReturn = bar.ReturnPosition;

            foreach (float summit in new[] { 35f, 40f, 45f })
            {
                CityTerraceMasterPlan master = CityTerraceMasterPlan.Create(layout, summit);
                CityTerraceTransportPlan plan = CityTerraceTransportPlan.Create(layout, master);
                CityTerraceTransportPlan repeated = CityTerraceTransportPlan.Create(layout, master);
                Assert.That(plan.Routes.Select(route => route.Bank), Is.EquivalentTo(new[] {
                    CityTerraceTransportBank.West, CityTerraceTransportBank.East }));
                Assert.That(plan.TargetSummitElevation, Is.EqualTo(summit));
                Assert.That(plan.PilotCandidateBounds.width, Is.GreaterThanOrEqualTo(40f));
                Assert.That(plan.PilotCandidateBounds.height, Is.GreaterThanOrEqualTo(34f));
                Assert.That(plan.PilotCandidateBounds.center, Is.EqualTo(new Vector2(120f, 64f)));

                for (int routeIndex = 0; routeIndex < plan.Routes.Count; routeIndex++)
                {
                    CityTerraceTransportRoute route = plan.Routes[routeIndex];
                    CityTerraceTransportRoute second = repeated.Routes[routeIndex];
                    Assert.That(route.Samples.Count, Is.InRange(2, CityTerraceTransportPlan.MaximumRouteSamples));
                    Assert.That(route.Segments.Count, Is.EqualTo(route.Samples.Count - 1));
                    Assert.That(route.Samples.Last().Position.y, Is.EqualTo(summit).Within(Tolerance));
                    Vector3 start = route.Samples[0].Position;
                    Assert.That(start.z, Is.EqualTo(master.CoreWorldXZBounds.yMax).Within(Tolerance));
                    Assert.That(layout.ElevationPlan.TrySampleSurface(new Vector2(start.x, start.z),
                        CitySurfaceRole.RoadDatum, out float coastalDatum, out _), Is.True);
                    Assert.That(start.y, Is.EqualTo(coastalDatum).Within(Tolerance));
                    Assert.That(route.Segments.Count(segment => segment.Kind == CityTerraceTransportSegmentKind.StopLanding), Is.EqualTo(3));
                    float distance = 0f, climbDistance = 0f, previousY = start.y;
                    for (int i = 0; i < route.Segments.Count; i++)
                    {
                        CityTerraceTransportSegment segment = route.Segments[i];
                        Assert.That(segment.Start, Is.EqualTo(route.Samples[i].Position));
                        Assert.That(segment.End, Is.EqualTo(route.Samples[i + 1].Position));
                        Vector2 firstXZ = new Vector2(segment.Start.x, segment.Start.z);
                        Vector2 secondXZ = new Vector2(segment.End.x, segment.End.z);
                        float horizontal = Vector2.Distance(firstXZ, secondXZ);
                        float physicalGrade = Mathf.Abs(segment.End.y - segment.Start.y) / horizontal * 100f;
                        Assert.That(horizontal, Is.GreaterThan(.01f));
                        Assert.That(segment.HorizontalLength, Is.EqualTo(horizontal).Within(Tolerance));
                        Assert.That(segment.GradePercent, Is.EqualTo(physicalGrade).Within(Tolerance));
                        Assert.That(physicalGrade, Is.LessThanOrEqualTo(6f + Tolerance));
                        Assert.That(segment.End.y, Is.GreaterThanOrEqualTo(previousY));
                        Assert.That(segment.End.z, Is.GreaterThan(master.CoreWorldXZBounds.yMin));
                        if (segment.Kind == CityTerraceTransportSegmentKind.Climb)
                            climbDistance += horizontal;
                        else
                            Assert.That(segment.End.y, Is.EqualTo(segment.Start.y), "Turn/stop/approach must be level.");
                        if (segment.Kind == CityTerraceTransportSegmentKind.StopLanding)
                            Assert.That(horizontal, Is.EqualTo(12f).Within(Tolerance), "A usable stop owns a full flat platform.");
                        // Both routes remain outside the protected first node
                        // beside the river even at their sampled inner turns.
                        float riverEdge = layout.GetGridWorldPosition(new Vector2Int(
                            layout.Blueprint.River.CorridorCellX + (route.Bank == CityTerraceTransportBank.West ? -1 : 2), 0)).x;
                        if (route.Bank == CityTerraceTransportBank.West)
                            Assert.That(segment.End.x + CityTerraceTransportPlan.CorridorWidth * .5f, Is.LessThan(riverEdge));
                        else
                            Assert.That(segment.End.x - CityTerraceTransportPlan.CorridorWidth * .5f, Is.GreaterThan(riverEdge));
                        distance += horizontal; previousY = segment.End.y;
                        Assert.That(route.Samples[i + 1].HorizontalDistance, Is.EqualTo(distance).Within(Tolerance));
                    }
                    Assert.That(route.TotalHorizontalLength, Is.EqualTo(distance).Within(Tolerance));
                    Assert.That(climbDistance, Is.GreaterThanOrEqualTo((summit - coastalDatum) / .06f));
                    Assert.That(route.TotalHorizontalLength, Is.GreaterThan(climbDistance));
                    Assert.That(route.MaximumGradePercent, Is.EqualTo(route.Segments.Max(segment => segment.GradePercent)));
                    Assert.That(second.Samples.Select(sample => new { sample.Position, sample.HorizontalDistance }),
                        Is.EqualTo(route.Samples.Select(sample => new { sample.Position, sample.HorizontalDistance })));
                    Assert.That(route.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.Building), Is.True);
                    foreach (CityTerraceReservation reservation in route.Reservations)
                    {
                        Assert.That(reservation.RouteId, Is.EqualTo(route.Id));
                        Assert.That(reservation.SegmentIndex, Is.InRange(0, route.Segments.Count - 1));
                        Assert.That(reservation.Bounds.width * reservation.Bounds.height, Is.GreaterThan(0f));
                    }
                }

                Assert.That(plan.HasProtectedConflicts, Is.True, "Grade feasibility must not hide the inhabited city.");
                Assert.That(plan.Reservations.Any(reservation => reservation.Id == "player-home" && reservation.IsProtected), Is.True);
                Assert.That(plan.Reservations.Any(reservation => reservation.Id == bar.BarId && reservation.IsProtected), Is.True);
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.Coast), Is.True,
                    "The existing coastal attachment also needs an explicit terrain tie-in.");
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.River), Is.False);
                Assert.That(plan.PilotReservations, Is.Not.Empty, "The development envelope is occupied, not an approved live insertion.");
                Assert.That(plan.PilotReservations.All(reservation => reservation.SegmentIndex == -1), Is.True);
                Assert.That(plan.PilotReservations.Any(reservation => reservation.Id == "player-home" || reservation.Id == bar.BarId), Is.False);
                Assert.That(repeated.Reservations.Select(reservation => new { reservation.RouteId, reservation.SegmentIndex,
                    reservation.Id, reservation.Kind, reservation.Bounds }), Is.EqualTo(plan.Reservations.Select(reservation => new {
                        reservation.RouteId, reservation.SegmentIndex, reservation.Id, reservation.Kind, reservation.Bounds })));
            }

            Assert.That(layout.ElevationPlan.NodeElevations, Is.EquivalentTo(heights));
            Assert.That(layout.PlayerHome.DoorPosition, Is.EqualTo(homeDoor));
            Assert.That(layout.PlayerHome.ReturnPosition, Is.EqualTo(homeReturn));
            Assert.That(bar.DoorPosition, Is.EqualTo(barDoor));
            Assert.That(bar.ReturnPosition, Is.EqualTo(barReturn));
            CityTerraceMasterPlan valid = CityTerraceMasterPlan.Create(layout);
            Assert.Throws<ArgumentNullException>(() => CityTerraceTransportPlan.Create(null, valid));
            Assert.Throws<ArgumentNullException>(() => CityTerraceTransportPlan.Create(layout, null));
            CityLayout other = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default, CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed + 1);
            Assert.Throws<ArgumentException>(() => CityTerraceTransportPlan.Create(other, valid));
        }
    }
}
