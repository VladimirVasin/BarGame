using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed partial class CityTerraceTransportPlanTests
    {
        private const float Tolerance = .001f;

        [Test]
        public void EntranceConnectionsMeasureRealSurfacesPreserveThresholdsAndRejectSteepAprons()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            CityTerraceTransportPlan plan = CityTerraceTransportPlan.Create(layout, CityTerraceMasterPlan.Create(layout));
            Assert.That(plan.EntranceConnections, Is.Not.Empty);
            Assert.That(plan.EntranceConnections.Select(item => item.Id).Distinct().Count(), Is.EqualTo(plan.EntranceConnections.Count));
            Assert.That(plan.EntranceConnections.Any(item => item.MaximumHeightChange > 3f), Is.True,
                "A raised road cannot silently keep the old entrance apron at street height.");
            var homeSource = CityStreetSurfacePlanner.CreateEntranceForecourt(layout, layout.PlayerHome, PlayerHomeEntranceGeometry.WalkwayWidth);
            Vector3 near = homeSource.Top[0], far = homeSource.Top[homeSource.Columns - 1];
            Vector3 tangent = (homeSource.Top[(homeSource.Rows - 1) * homeSource.Columns] - near);
            tangent.y = 0f; tangent.Normalize();
            Vector3 towardStreet = far - near; towardStreet.y = 0f; towardStreet.Normalize();
            Vector3 routeStart = far + towardStreet * 5f - tangent * 2f;
            Vector3 routeEnd = homeSource.Top[homeSource.Top.Count - 1] + towardStreet * 5f + tangent * 2f;
            routeStart.y = routeEnd.y = 100f;
            float length = Vector3.Distance(routeStart, routeEnd);
            var path = new CityRoadPath(new[] { new Vector2(routeStart.x, routeStart.z), new Vector2(routeEnd.x, routeEnd.z) });
            var steepRoute = new CityTerraceTransportRoute("steep-test", CityTerraceTransportBank.East,
                new[] { new CityTerraceRouteSample(routeStart, 0f), new CityTerraceRouteSample(routeEnd, length) },
                new[] { new CityTerraceTransportSegment(routeStart, routeEnd, CityTerraceTransportSegmentKind.Approach) },
                new CityRoadElevationProfile(new[] { new CityRoadElevationStation(0f, 100f),
                    new CityRoadElevationStation(length, 100f) }, 6f), Array.Empty<CityTerraceReservation>(), path.Ribbon(10f));
            CityTerraceEntranceConnectionPlan steep = CityTerraceEntranceConnectionPlan.Create(layout, new[] { steepRoute })
                .Single(item => item.EntranceId == "player-home:forecourt");
            Assert.That(steep.State, Is.EqualTo(CityTerraceEntranceConnectionState.RequiresStreetRegrading),
                "Even a full straight pavement seam cannot approve an apron with an impossible walking slope.");
            foreach (CityTerraceEntranceConnectionPlan connection in plan.EntranceConnections.Concat(new[] { steep }))
            {
                CityTerraceTransportRoute route = connection == steep ? steepRoute : plan.Routes.Single(item => item.Id == connection.RouteId);
                CityEntranceForecourtPlan source = connection.ExistingSurface;
                Assert.That(plan.Footprints.Any(item => item.Id == connection.EntranceId &&
                    item.Kind == CityTerraceReservationKind.ProtectedAccess), Is.True);
                TestContext.Progress.WriteLine($"Entrance {connection.EntranceId}: {connection.State}, gap={connection.MaximumHorizontalGap:0.00}, change={connection.MaximumHeightChange:0.00}, grade={connection.RequiredGradePercent:0.00}");
                foreach (CityTerraceEntranceSeam seam in connection.Seams)
                {
                    Assert.That(seam.Existing, Is.EqualTo(source.Top[seam.Row * source.Columns + source.Columns - 1]));
                    Assert.That(seam.Existing.y, Is.EqualTo(source.SampleTop(seam.Existing)).Within(Tolerance));
                    Assert.That(seam.Proposed.y, Is.EqualTo(route.ElevationProfile.SampleDistance(seam.RouteDistance).Height +
                        CityStreetSurfacePlanner.SidewalkTop).Within(Tolerance));
                    Vector2 point = new Vector2(seam.Proposed.x, seam.Proposed.z);
                    Assert.That(ContainsConvex(route.CorridorPolygons[seam.SegmentIndex], point), Is.True,
                        "A straight seam must stay on the actual mitered pavement edge.");
                    CityTerraceTransportSegment segment = route.Segments[seam.SegmentIndex];
                    Vector2 start = new Vector2(segment.Start.x, segment.Start.z);
                    Vector2 direction = new Vector2(segment.End.x - segment.Start.x, segment.End.z - segment.Start.z).normalized;
                    Assert.That(Mathf.Abs(CityRoadPolygon.Cross(direction, point - start)), Is.EqualTo(5f).Within(Tolerance));
                }
                if (connection.State == CityTerraceEntranceConnectionState.PartialFrontage ||
                    connection.State == CityTerraceEntranceConnectionState.RequiresAccessRoute)
                {
                    Assert.That(connection.ProposedTop, Is.Empty);
                    continue;
                }
                Assert.That(connection.Seams.Count, Is.EqualTo(source.Rows));
                Assert.That(connection.Seams.Select(item => item.SegmentIndex).Distinct().Count(), Is.EqualTo(1),
                    "A missing profile knot between two rows cannot be accepted as a linear seam.");
                Assert.That(connection.ProposedTop.Count, Is.EqualTo(source.Top.Count));
                Assert.Throws<NotSupportedException>(() => ((IList<Vector3>)connection.ProposedTop)[0] = Vector3.zero);
                for (int row = 0; row < source.Rows; row++)
                {
                    Assert.That(connection.ProposedTop[row * source.Columns], Is.EqualTo(source.Top[row * source.Columns]));
                    Assert.That(connection.ProposedTop[row * source.Columns + source.Columns - 1], Is.EqualTo(connection.Seams[row].Proposed));
                }
                float maximum = 0f;
                void Measure(int a, int b, int c)
                {
                    Vector3 ab = connection.ProposedTop[b] - connection.ProposedTop[a];
                    Vector3 ac = connection.ProposedTop[c] - connection.ProposedTop[a];
                    float determinant = ab.x * ac.z - ab.z * ac.x;
                    float gradientX = (ab.y * ac.z - ac.y * ab.z) / determinant;
                    float gradientZ = (ab.x * ac.y - ac.x * ab.y) / determinant;
                    maximum = Mathf.Max(maximum, Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ) * 100f);
                }
                for (int row = 0; row < source.Rows - 1; row++)
                for (int column = 0; column < source.Columns - 1; column++)
                {
                    int a = row * source.Columns + column;
                    Measure(a, a + 1, a + source.Columns + 1);
                    Measure(a, a + source.Columns + 1, a + source.Columns);
                }
                Assert.That(connection.RequiredGradePercent, Is.EqualTo(maximum).Within(.01f));
                Assert.That(connection.State == CityTerraceEntranceConnectionState.GradedApron,
                    Is.EqualTo(maximum <= CityElevationPlan.MaximumPedestrianGradePercent + .001f));
            }
            Assert.That(plan.EntranceConnections.Any(item => item.State == CityTerraceEntranceConnectionState.RequiresAccessRoute), Is.True);
            Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.RoadConnections), Is.True);
            Assert.Throws<ArgumentNullException>(() => CityTerraceEntranceConnectionPlan.Create(null, plan.Routes));
            Assert.Throws<ArgumentNullException>(() => CityTerraceEntranceConnectionPlan.Create(layout, null));
        }

        [Test]
        public void StreetCorridorsClearBuildingsPreserveAnchorsAndExposePlacementLimits()
        {
            Vector2[] unit = { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(0f, 2f) };
            Assert.That(ConvexOverlapArea(unit, unit.Select(point => point + Vector2.one).ToArray()), Is.EqualTo(1d));
            Assert.That(ConvexOverlapArea(unit, unit.Select(point => point + Vector2.right * 2f).ToArray()), Is.Zero);
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var heights = layout.ElevationPlan.NodeElevations.ToDictionary(pair => pair.Key, pair => pair.Value);
            Vector3 homeDoor = layout.PlayerHome.DoorPosition;
            Vector3 homeReturn = layout.PlayerHome.ReturnPosition;
            BuildingLot bar = layout.BuildingLots.First(lot => lot.IsBar);
            Vector3 barDoor = bar.DoorPosition, barReturn = bar.ReturnPosition;
            var masses = layout.BuildingMasses.Select(lot => new { lot.Cell, lot.Center, lot.Size, lot.Height,
                lot.BuildingVariant, lot.District, lot.FacadeForward, lot.DoorPosition,
                lot.ReturnPosition, lot.SidewalkArrivalPosition }).ToArray();
            Assert.That(layout.BuildingMasses.Count, Is.GreaterThan(layout.BuildingLots.Count),
                "The clearance survey must include actual rear courtyard houses.");
            Assert.That(CityTerraceTransportPlan.CorridorWidth, Is.EqualTo(10f),
                "The reserved street contains six metres of asphalt and both two-metre public pavements.");

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
                    Assert.That(route.CorridorPolygons.Count, Is.EqualTo(route.Segments.Count));
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
                        if (route.IsGradeFeasible)
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
                        Assert.That(segment.End.z, Is.GreaterThanOrEqualTo(master.CoreWorldXZBounds.yMin - .26f));
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
                    Assert.That(route.MaximumReachableSummitElevation,
                        Is.EqualTo(coastalDatum + climbDistance * .06f).Within(Tolerance));
                    Assert.That(route.IsGradeFeasible,
                        Is.EqualTo(summit <= route.MaximumReachableSummitElevation + Tolerance));
                    if (summit <= 40f)
                        Assert.That(route.IsGradeFeasible, Is.True, "The supported default rise must remain physically attainable.");
                    if (summit == 45f && route.Bank == CityTerraceTransportBank.East)
                    {
                        Assert.That(route.IsGradeFeasible, Is.False, "The upper request must expose insufficient climbing length.");
                        Assert.That(route.MaximumGradePercent, Is.GreaterThan(6f));
                        Assert.That(route.MaximumReachableSummitElevation, Is.LessThan(summit));
                    }
                    Assert.That(route.TotalHorizontalLength, Is.GreaterThan(climbDistance));
                    Assert.That(route.MaximumGradePercent, Is.EqualTo(route.Segments.Max(segment => segment.GradePercent)).Within(Tolerance));
                    Assert.That(second.Samples.Select(sample => new { sample.Position, sample.HorizontalDistance }),
                        Is.EqualTo(route.Samples.Select(sample => new { sample.Position, sample.HorizontalDistance })));
                    for (int i = 0; i < route.CorridorPolygons.Count; i++)
                    {
                        IReadOnlyList<Vector2> corridor = route.CorridorPolygons[i];
                        Assert.That(second.CorridorPolygons[i], Is.EqualTo(corridor));
                        foreach (CityTerraceTransportFootprint footprint in plan.Footprints)
                        {
                            if (footprint.Kind == CityTerraceReservationKind.ProtectedAccess) continue;
                            Assert.That(ConvexOverlapArea(corridor, footprint.Polygon), Is.LessThan(.00001d),
                                $"{route.Bank} segment {i} clips {footprint.Kind} {footprint.Id}.");
                        }
                        for (int j = i + 2; j < route.CorridorPolygons.Count; j++)
                            Assert.That(ConvexOverlapArea(corridor, route.CorridorPolygons[j]), Is.LessThan(.00001d),
                                $"{route.Bank} non-neighbouring corridor segments {i}/{j} overlap.");
                    }
                    Assert.That(route.Reservations.All(reservation =>
                        reservation.Kind == CityTerraceReservationKind.ExistingRoad ||
                        reservation.Kind == CityTerraceReservationKind.ProtectedAccess), Is.True);
                    foreach (CityTerraceReservation reservation in route.Reservations)
                    {
                        Assert.That(reservation.RouteId, Is.EqualTo(route.Id));
                        Assert.That(reservation.SegmentIndex, Is.InRange(0, route.Segments.Count - 1));
                        Assert.That(reservation.Bounds.width * reservation.Bounds.height, Is.GreaterThan(0f));
                    }
                }

                Assert.That(plan.HasProtectedConflicts, Is.False);
                Assert.That(plan.HasBuildingConflicts, Is.False, "Every surveyed building and protected place must be clear.");
                Assert.That(plan.IsPlacementReady, Is.False, "A six-percent profile is not a reserved physical road.");
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.FootprintConflicts), Is.False);
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.GradeLimit),
                    Is.EqualTo(plan.Routes.Any(route => !route.IsGradeFeasible)));
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.RoadConnections), Is.True);
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.UnresolvedRoadCrossings), Is.True);
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.VehicleSweptPath), Is.True);
                Assert.That(plan.OutstandingPlacementRequirements.HasFlag(CityTerracePlacementRequirement.TerrainTieIns), Is.True);
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.ExistingRoad), Is.True);
                Assert.That(plan.RoadCrossings, Is.Not.Empty);
                Assert.That(plan.RoadCrossings.Select(crossing => crossing.Id).Distinct().Count(), Is.EqualTo(plan.RoadCrossings.Count));
                foreach (CityTerraceTransportRoute route in plan.Routes)
                    Assert.That(plan.RoadCrossings.Any(crossing => crossing.RouteId == route.Id &&
                        crossing.Kind == CityTerraceRoadCrossingKind.CoastalAttachment), Is.True);
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
                foreach (string owner in new[] { "player-home", bar.BarId, "supermarket", "city-laundry:site",
                    "city-dog-park:site", "city-fair:site", "city-arch-shelter:structure" })
                    Assert.That(plan.Footprints.Any(footprint => footprint.Id == owner &&
                        footprint.Kind == CityTerraceReservationKind.ProtectedPlace), Is.True, owner);
                Assert.That(plan.Footprints.Any(footprint => footprint.Kind == CityTerraceReservationKind.Terrace), Is.True);
                Assert.That(plan.Footprints.Any(footprint => footprint.Id == "player-home:forecourt" &&
                    footprint.Kind == CityTerraceReservationKind.ProtectedAccess), Is.True);
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.ProtectedAccess), Is.True,
                    "Shared entrance interfaces still need an explicit height/connection solution.");
                int massIndex = 0;
                foreach (BuildingLot lot in layout.BuildingMasses)
                {
                    if (!lot.HasBuilding && !lot.IsCafe) continue;
                    if (lot.IsOrdinaryBuilding && !layout.PrimaryLandmarkCells.Values.Contains(lot.Cell))
                    {
                        string id = "building:" + lot.Cell + ":" + massIndex;
                        CityTerraceTransportFootprint footprint = plan.Footprints.Single(item => item.Id == id);
                        Assert.That(footprint.Kind, Is.EqualTo(CityTerraceReservationKind.Building));
                        CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
                        Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, lot.BuildingVariant);
                        foreach (float x in new[] { -.5f, .5f })
                        foreach (float z in new[] { -.5f, .5f })
                        {
                            Vector3 corner = pose.TransformPoint(new Vector3(x * envelope.x, 0f, z * envelope.z));
                            Assert.That(ContainsConvex(footprint.Polygon, new Vector2(corner.x, corner.z)), Is.True,
                                "The survey must cover upper spans and every rear-house model envelope: " + id);
                        }
                    }
                    massIndex++;
                }
                foreach (float lateral in new[] { -6.5f, 6.5f })
                {
                    Vector3 roof = PlayerHomeBalconyGeometry.ToCityWorld(layout.PlayerHome,
                        new Vector3(PlayerHomeBalconyGeometry.HomeFacadeX + PlayerHomeBalconyGeometry.BalconyDepth, 0f, lateral));
                    Assert.That(plan.Footprints.Any(footprint => footprint.Id == "player-home" &&
                        ContainsConvex(footprint.Polygon, new Vector2(roof.x, roof.z))), Is.True,
                        "The Home roof overhang spans the entire facade, beyond the narrow balcony.");
                }
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.Coast), Is.False);
                Assert.That(plan.Reservations.Any(reservation => reservation.Kind == CityTerraceReservationKind.River), Is.False);
                Assert.That(plan.PilotReservations, Is.Not.Empty, "The development envelope is occupied, not an approved live insertion.");
                Assert.That(plan.PilotReservations.All(reservation => reservation.SegmentIndex == -1), Is.True);
                Assert.That(plan.Reservations.All(reservation => reservation.SegmentIndex >= 0), Is.True,
                    "The occupied historical pilot envelope is not a conflict of the new transport routes.");
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
            Assert.That(layout.BuildingMasses.Select(lot => new { lot.Cell, lot.Center, lot.Size, lot.Height,
                lot.BuildingVariant, lot.District, lot.FacadeForward, lot.DoorPosition,
                lot.ReturnPosition, lot.SidewalkArrivalPosition }), Is.EqualTo(masses));
            CityTerraceMasterPlan valid = CityTerraceMasterPlan.Create(layout);
            Assert.Throws<ArgumentNullException>(() => CityTerraceTransportPlan.Create(null, valid));
            Assert.Throws<ArgumentNullException>(() => CityTerraceTransportPlan.Create(layout, null));
            CityLayout other = CityLayoutGenerator.Generate(CityBlueprintCatalog.Default, CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed + 1);
            Assert.Throws<ArgumentException>(() => CityTerraceTransportPlan.Create(other, valid));
        }

        // Independent double-precision clipping checks actual pavement area,
        // not the production overlap flags or the sampled centreline alone.
        private static double ConvexOverlapArea(IReadOnlyList<Vector2> first, IReadOnlyList<Vector2> second)
        {
            double ax0 = double.PositiveInfinity, ay0 = double.PositiveInfinity;
            double ax1 = double.NegativeInfinity, ay1 = double.NegativeInfinity;
            double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity;
            double bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
            foreach (Vector2 point in first)
            {
                ax0 = Math.Min(ax0, point.x); ax1 = Math.Max(ax1, point.x);
                ay0 = Math.Min(ay0, point.y); ay1 = Math.Max(ay1, point.y);
            }
            foreach (Vector2 point in second)
            {
                bx0 = Math.Min(bx0, point.x); bx1 = Math.Max(bx1, point.x);
                by0 = Math.Min(by0, point.y); by1 = Math.Max(by1, point.y);
            }
            if (ax1 <= bx0 || bx1 <= ax0 || ay1 <= by0 || by1 <= ay0) return 0d;
            var polygon = first.Select(point => ((double)point.x, (double)point.y)).ToList();
            for (int i = 0; i < second.Count && polygon.Count > 0; i++)
            {
                Vector2 start = second[i], end = second[(i + 1) % second.Count];
                double dx = (double)end.x - start.x, dy = (double)end.y - start.y;
                double Side((double x, double y) point) => dx * (point.y - start.y) - dy * (point.x - start.x);
                var output = new List<(double x, double y)>();
                (double x, double y) previous = polygon[polygon.Count - 1];
                double previousSide = Side(previous);
                foreach ((double x, double y) current in polygon)
                {
                    double currentSide = Side(current);
                    if ((previousSide >= 0d) != (currentSide >= 0d))
                    {
                        double amount = previousSide / (previousSide - currentSide);
                        output.Add((previous.x + (current.x - previous.x) * amount,
                            previous.y + (current.y - previous.y) * amount));
                    }
                    if (currentSide >= 0d) output.Add(current);
                    previous = current; previousSide = currentSide;
                }
                polygon = output;
            }
            double area = 0d;
            for (int i = 1; i + 1 < polygon.Count; i++)
                area += (polygon[i].Item1 - polygon[0].Item1) * (polygon[i + 1].Item2 - polygon[0].Item2) -
                    (polygon[i].Item2 - polygon[0].Item2) * (polygon[i + 1].Item1 - polygon[0].Item1);
            return Math.Abs(area) * .5d;
        }

        private static bool ContainsConvex(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                double side = ((double)b.x - a.x) * ((double)point.y - a.y) -
                    ((double)b.y - a.y) * ((double)point.x - a.x);
                if (side < -Tolerance) return false;
            }
            return true;
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
