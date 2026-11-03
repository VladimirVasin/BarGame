using System;
using System.Collections.Generic;
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
                    Assert.That(route.ElevationProfile.Stations.Count, Is.EqualTo(route.Samples.Count));
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
                        float middleDistance = (route.Samples[i].HorizontalDistance + route.Samples[i + 1].HorizontalDistance) * .5f;
                        CityRoadElevationSample elevation = route.ElevationProfile.SampleDistance(middleDistance);
                        Assert.That(elevation.Height, Is.EqualTo((segment.Start.y + segment.End.y) * .5f).Within(Tolerance));
                        Assert.That(elevation.GradePercent, Is.EqualTo(physicalGrade).Within(Tolerance));
                        Assert.That(elevation.SegmentIndex, Is.EqualTo(i));
                        Vector3 direction = segment.End - segment.Start;
                        Vector2 planar = new Vector2(direction.x, direction.z);
                        Assert.That(Vector3.Distance(elevation.GetTangent(planar), direction.normalized), Is.LessThan(Tolerance));
                        Assert.That(Vector3.Dot(elevation.GetNormal(planar), direction.normalized), Is.EqualTo(0f).Within(Tolerance));
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
                    Assert.That(route.MaximumGradePercent, Is.EqualTo(route.Segments.Max(segment => segment.GradePercent)).Within(Tolerance));
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
                Assert.That(plan.IsPlacementReady, Is.False, "A six-percent profile is not a reserved physical road.");
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.UnresolvedRoadCrossings), Is.True);
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.VehicleSweptPath), Is.True);
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.TerrainTieIns), Is.True);
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.ExistingRoad), Is.True);
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.Terrace), Is.True);
                Assert.That(plan.RoadCrossings, Is.Not.Empty);
                Assert.That(plan.RoadCrossings.Select(crossing => crossing.Id).Distinct().Count(), Is.EqualTo(plan.RoadCrossings.Count));
                Assert.That(plan.RoadCrossings.Count(crossing => crossing.Kind == CityTerraceRoadCrossingKind.CoastalAttachment), Is.EqualTo(2));
                Assert.That(plan.RoadCrossings.Any(crossing => crossing.Kind == CityTerraceRoadCrossingKind.GradeSeparatedUnresolved &&
                    Mathf.Abs(crossing.SignedSurfaceSeparation) > 4f), Is.True, "A large gap must remain unresolved, not silently become a tunnel.");
                foreach (CityTerraceRoadCrossing crossing in plan.RoadCrossings)
                {
                    CityTerraceTransportRoute route = plan.Routes.First(item => item.Id == crossing.RouteId);
                    Assert.That(route.Reservations.Any(item => item.Id == crossing.ReservationId &&
                        item.SegmentIndex == crossing.SegmentIndex && item.Kind == CityTerraceReservationKind.ExistingRoad), Is.True);
                    CityRoadPath road = layout.RoadGeometry.Get(crossing.RoadEdge);
                    CityRoadProjection projection = road.Project(crossing.WorldXZ);
                    float existing = layout.ElevationPlan.SampleRoadDatum(crossing.RoadEdge, projection.DistanceAlong / road.Length);
                    Assert.That(crossing.RoadDatumY, Is.EqualTo(existing).Within(Tolerance));
                    CityTerraceTransportSegment segment = route.Segments[crossing.SegmentIndex];
                    Vector2 startXZ = new Vector2(segment.Start.x, segment.Start.z);
                    Vector2 deltaXZ = new Vector2(segment.End.x - segment.Start.x, segment.End.z - segment.Start.z);
                    float t = Mathf.Clamp01(Vector2.Dot(crossing.WorldXZ - startXZ, deltaXZ) / deltaXZ.sqrMagnitude);
                    float proposed = route.ElevationProfile.SampleDistance(route.Samples[crossing.SegmentIndex].HorizontalDistance +
                        segment.HorizontalLength * t).Height;
                    Assert.That(crossing.RouteDatumY, Is.EqualTo(proposed).Within(Tolerance));
                    Assert.That(crossing.SignedSurfaceSeparation, Is.EqualTo(proposed - existing).Within(Tolerance));
                    if (crossing.Kind == CityTerraceRoadCrossingKind.CoastalAttachment)
                    {
                        Assert.That(crossing.SegmentIndex, Is.Zero);
                        Assert.That(crossing.RoutePosition, Is.EqualTo(route.Samples[0].Position +
                            Vector3.up * CityStreetSurfacePlanner.RoadTop));
                    }
                }
                Assert.That(repeated.RoadCrossings.Select(crossing => new { crossing.Id, crossing.WorldXZ,
                    crossing.RouteDatumY, crossing.RoadDatumY, crossing.Kind, crossing.CenterlinesIntersect }),
                    Is.EqualTo(plan.RoadCrossings.Select(crossing => new { crossing.Id, crossing.WorldXZ,
                        crossing.RouteDatumY, crossing.RoadDatumY, crossing.Kind, crossing.CenterlinesIntersect })));
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

        [Test]
        public void RoadElevationProfileKeepsLandingsSignedGradesFramesAndKnotOwnership()
        {
            var authored = new[] {
                new CityRoadElevationStation(0f, 3f), new CityRoadElevationStation(10f, 3.5f),
                new CityRoadElevationStation(20f, 3.5f), new CityRoadElevationStation(30f, 3f),
                new CityRoadElevationStation(40f, 3f) };
            var profile = new CityRoadElevationProfile(authored, 6f);
            authored[1] = new CityRoadElevationStation(10f, 100f);
            Assert.That(profile.Stations[1].Height, Is.EqualTo(3.5f), "The authoring buffer must not mutate an accepted profile.");
            Assert.Throws<NotSupportedException>(() => ((IList<CityRoadElevationStation>)profile.Stations)[0] = authored[1]);
            Assert.That(profile.HorizontalLength, Is.EqualTo(40f));
            Assert.That(profile.AllowedGradePercent, Is.EqualTo(6f));
            Assert.That(profile.MaximumGradePercent, Is.EqualTo(5f));

            float[] distances = { -10f, 0f, 5f, 10f, 15f, 20f, 25f, 30f, 35f, 40f, 50f };
            float[] heights = { 3f, 3f, 3.25f, 3.5f, 3.5f, 3.5f, 3.25f, 3f, 3f, 3f, 3f };
            float[] grades = { 5f, 5f, 5f, 0f, 0f, -5f, -5f, 0f, 0f, 0f, 0f };
            int[] segments = { 0, 0, 0, 1, 1, 2, 2, 3, 3, 3, 3 };
            Vector2 forward = new Vector2(3f, 4f);
            for (int i = 0; i < distances.Length; i++)
            {
                CityRoadElevationSample sample = profile.SampleDistance(distances[i]);
                Assert.That(sample.Height, Is.EqualTo(heights[i]).Within(Tolerance));
                Assert.That(sample.SignedGradePercent, Is.EqualTo(grades[i]).Within(Tolerance));
                Assert.That(sample.SegmentIndex, Is.EqualTo(segments[i]));
                Vector3 tangent = sample.GetTangent(forward), normal = sample.GetNormal(forward);
                Assert.That(tangent.magnitude, Is.EqualTo(1f).Within(Tolerance));
                Assert.That(normal.magnitude, Is.EqualTo(1f).Within(Tolerance));
                Assert.That(normal.y, Is.GreaterThan(0f));
                Assert.That(Vector3.Dot(tangent, normal), Is.EqualTo(0f).Within(Tolerance));
                Assert.That(tangent.y / new Vector2(tangent.x, tangent.z).magnitude * 100f,
                    Is.EqualTo(grades[i]).Within(Tolerance));
                if (grades[i] == 0f) Assert.That(normal, Is.EqualTo(Vector3.up));
            }

            // The bound is inclusive; its last interval still owns the terminal sample.
            CityRoadElevationStation[] bounded = Enumerable.Range(0, CityRoadElevationProfile.MaximumStations)
                .Select(i => new CityRoadElevationStation(i, 2f)).ToArray();
            var longest = new CityRoadElevationProfile(bounded, 0f);
            Assert.That(longest.SampleDistance(longest.HorizontalLength).SegmentIndex, Is.EqualTo(bounded.Length - 2));
            Assert.That(longest.SampleDistance(longest.HorizontalLength).Height, Is.EqualTo(2f));
        }

        [Test]
        public void RoadElevationProfileRejectsInvalidStationsLimitsAndQueries()
        {
            var first = new CityRoadElevationStation(0f, 0f);
            var flat = new[] { first, new CityRoadElevationStation(10f, 0f) };
            Assert.Throws<ArgumentNullException>(() => new CityRoadElevationProfile(null, 6f));
            Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(Array.Empty<CityRoadElevationStation>(), 6f));
            Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(new[] { first }, 6f));
            Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(
                new CityRoadElevationStation[CityRoadElevationProfile.MaximumStations + 1], 6f));
            Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(new[] {
                new CityRoadElevationStation(1f, 0f), new CityRoadElevationStation(2f, 0f) }, 6f));
            foreach (float distance in new[] { -1f, 0f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
                Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(new[] {
                    first, new CityRoadElevationStation(distance, 0f) }, 6f));
            Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(new[] {
                first, new CityRoadElevationStation(10f, 0f), new CityRoadElevationStation(9f, 0f) }, 6f));
            foreach (float height in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -1f, 1f })
                Assert.Throws<ArgumentException>(() => new CityRoadElevationProfile(new[] {
                    first, new CityRoadElevationStation(10f, height) }, 6f));
            foreach (float limit in new[] { -1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => new CityRoadElevationProfile(flat, limit));

            var profile = new CityRoadElevationProfile(flat, 0f);
            foreach (float distance in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => profile.SampleDistance(distance));
            CityRoadElevationSample sample = profile.SampleDistance(5f);
            foreach (Vector2 forward in new[] { Vector2.zero, new Vector2(float.NaN, 1f), new Vector2(1f, float.PositiveInfinity) })
            {
                Assert.Throws<ArgumentException>(() => sample.GetTangent(forward));
                Assert.Throws<ArgumentException>(() => sample.GetNormal(forward));
            }
        }
    }
}
