using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBusPlannerTests
    {
        private const float GeometryTolerance = 0.001f;

        [Test]
        public void DefaultRoadV21_CreatesDeterministicNonEmptyPlan()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);

            CityBusPlan first = CityBusPlanner.Create(
                layout,
                decorations);
            CityBusPlan second = CityBusPlanner.Create(layout);

            Assert.That(
                first.IsEmpty,
                Is.False,
                "Route 01 should connect all semantic targets.");
            Assert.That(first.Nodes.Count, Is.GreaterThan(0));
            Assert.That(first.Links.Count, Is.GreaterThan(0));
            Assert.That(
                first.RouteId,
                Is.EqualTo(CityBusPlanner.DefaultRouteId));
            Assert.That(first.StableSeed, Is.EqualTo(second.StableSeed));
            CollectionAssert.AreEqual(
                first.Nodes.Select(node => node.Id).ToArray(),
                second.Nodes.Select(node => node.Id).ToArray());
            CollectionAssert.AreEqual(
                first.Links.Select(link => link.Id).ToArray(),
                second.Links.Select(link => link.Id).ToArray());
            CollectionAssert.AreEqual(
                first.SpawnAnchors.Select(anchor => anchor.Id).ToArray(),
                second.SpawnAnchors.Select(anchor => anchor.Id).ToArray());
            CollectionAssert.AreEqual(
                first.Stops.Select(stop => stop.Id).ToArray(),
                second.Stops.Select(stop => stop.Id).ToArray());
            CollectionAssert.AreEqual(
                first.OrderedLinkIndices,
                second.OrderedLinkIndices);
            CollectionAssert.AreEqual(
                first.ClearanceFailures
                    .Select(failure => failure.Id)
                    .ToArray(),
                second.ClearanceFailures
                    .Select(failure => failure.Id)
                    .ToArray());
        }

        [Test]
        public void DefaultCoastal_BuildsClosedWindingTargetRoute()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            Assert.That(
                plan.OrderedLinkIndices,
                Is.EqualTo(Enumerable.Range(0, plan.Links.Count).ToArray()));
            Assert.That(plan.Nodes.Count, Is.EqualTo(plan.Links.Count));
            Assert.That(
                plan.Links.Count(link =>
                    link.Kind != CityBusRouteLinkKind.Straight),
                Is.GreaterThan(4));
            Assert.That(
                plan.Links.Any(link =>
                    link.Kind == CityBusRouteLinkKind.RightTurn &&
                    link.Clearance.IsClear &&
                    link.MinimumTurnRadius >=
                        plan.Vehicle.MinimumBodyCenterTurnRadius),
                Is.True);
            // Accumulate in float exactly the way the planner does — the
            // LINQ Sum runs in double, and over ~380 links the two drift
            // past the geometry tolerance.
            float summedLength = 0f;
            for (int index = 0; index < plan.Links.Count; index++)
            {
                summedLength += plan.Links[index].Length;
            }

            Assert.That(
                plan.LoopLength,
                Is.EqualTo(summedLength).Within(GeometryTolerance));
            for (int index = 0; index < plan.Links.Count; index++)
            {
                CityBusRouteLink current = plan.Links[index];
                CityBusRouteLink next = plan.Links[
                    (index + 1) % plan.Links.Count];
                Assert.That(
                    Vector3.Distance(
                        current.Samples[current.Samples.Count - 1].Position,
                        next.Samples[0].Position),
                    Is.LessThanOrEqualTo(GeometryTolerance),
                    current.Id);
            }
        }

        [Test]
        [Category("CityRiver")]
        public void DefaultRiver_UsesBothRoadBridgesAndNoReservedFurniture()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(layout, decorations);
            CityRiverBridgeDescriptor[] roadBridges = layout.River.Bridges
                .Where(bridge => bridge.Definition.Role ==
                    CityBridgeRole.Road)
                .ToArray();
            CityRiverBridgeDescriptor footbridge = layout.River.Bridges
                .Single(bridge => bridge.Definition.Role ==
                    CityBridgeRole.ParkFootbridge);

            Assert.That(roadBridges, Has.Length.EqualTo(2));
            Assert.That(
                plan.Links,
                Is.Not.Empty,
                "Route 01 must remain available after inserting the river. " +
                string.Join(" | ", plan.ClearanceFailures
                    .Where(failure => roadBridges.Any(bridge =>
                        failure.FromRoadEdge ==
                            bridge.Definition.CrossingEdge ||
                        failure.ToRoadEdge ==
                            bridge.Definition.CrossingEdge))
                    .Select(failure => failure.Id + ":" +
                        failure.Clearance.FailureKind)));
            for (int bridgeIndex = 0;
                 bridgeIndex < roadBridges.Length;
                 bridgeIndex++)
            {
                RoadEdge edge =
                    roadBridges[bridgeIndex].Definition.CrossingEdge;
                int traversals = plan.Links.Count(link =>
                    link.Id.StartsWith("bus:road:") &&
                    plan.Nodes[link.FromNodeIndex].RoadEdge == edge);
                Assert.That(
                    traversals,
                    Is.EqualTo(1),
                    roadBridges[bridgeIndex].Definition.Id);
            }

            Assert.That(
                plan.Nodes.Any(node => node.RoadEdge ==
                    footbridge.Definition.CrossingEdge),
                Is.False);
            Assert.That(
                plan.Stops.Any(stop =>
                    layout.IsBusFurnitureExcluded(stop.RoadEdge)),
                Is.False);
            Assert.That(
                plan.SpawnAnchors.Any(anchor =>
                    layout.IsBusFurnitureExcluded(anchor.RoadEdge)),
                Is.False);
            foreach (CityBusStopDescriptor stop in plan.Stops)
            {
                if (stop.TargetKind == CityBusStopTargetKind.PlayerHome)
                {
                    Assert.That(
                        layout.TryGetFrontageEdge(
                            layout.PlayerHome,
                            out RoadEdge homeFrontage),
                        Is.True);
                    Assert.That(
                        RoadEdgeHop(
                            stop.RoadEdge,
                            new[] { homeFrontage }),
                        Is.LessThanOrEqualTo(1),
                        stop.TargetId);
                }
                else if (stop.TargetKind ==
                         CityBusStopTargetKind.DistrictPointOfInterest)
                {
                    CityDistrictPointOfInterestDescriptor point =
                        layout.DistrictPointsOfInterest.Single(candidate =>
                            candidate.Id == stop.TargetId);
                    Assert.That(
                        RoadEdgeHop(
                            stop.RoadEdge,
                            point.Accesses.Select(access =>
                                access.FrontageEdge)),
                        Is.LessThanOrEqualTo(5),
                        stop.TargetId);
                    float referenceDistance = point.Accesses
                        .Select(access => XzDistance(
                            stop.ShelterPosition,
                            access.Center))
                        .Append(XzDistance(
                            stop.ShelterPosition,
                            point.Center))
                        .Min();
                    Assert.That(
                        referenceDistance,
                        Is.LessThanOrEqualTo(120f),
                        stop.TargetId);
                }
                else if (stop.TargetKind ==
                         CityBusStopTargetKind.OpenAreaAccess)
                {
                    CityOpenAreaAccessDescriptor access = layout
                        .OpenAreaAccesses
                        .SingleOrDefault(candidate =>
                            candidate.Id == stop.TargetId);
                    if (string.IsNullOrEmpty(access.Id))
                    {
                        // A synthetic waterfront spread stop has no
                        // matching descriptor; the coverage tests bound
                        // it instead.
                        continue;
                    }

                    Assert.That(
                        RoadEdgeHop(
                            stop.RoadEdge,
                            new[] { access.FrontageEdge }),
                        Is.LessThanOrEqualTo(
                            CityBusPlanner.MaximumCoverageStopRoadEdgeHop),
                        stop.TargetId);
                    Assert.That(
                        XzDistance(
                            stop.ShelterPosition,
                            access.Center),
                        Is.LessThanOrEqualTo(
                            CityBusPlanner.MaximumCoverageStopDistance),
                        stop.TargetId);
                }
            }
        }

        [Test]
        public void OrderedRoute_IsStreetOnlyRightHandAndOneInOneOut()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            var incomingCounts = new int[plan.Nodes.Count];
            for (int index = 0; index < plan.Links.Count; index++)
            {
                incomingCounts[plan.Links[index].ToNodeIndex]++;
            }

            for (int nodeIndex = 0;
                 nodeIndex < plan.Nodes.Count;
                 nodeIndex++)
            {
                CityBusRouteNode node = plan.Nodes[nodeIndex];
                Assert.That(
                    layout.GetPathKind(node.RoadEdge),
                    Is.EqualTo(CityPathKind.Street),
                    node.Id);
                Assert.That(
                    plan.GetOutgoingLinkIndices(nodeIndex).Count,
                    Is.EqualTo(1),
                    node.Id);
                Assert.That(incomingCounts[nodeIndex], Is.EqualTo(1), node.Id);

                Vector3 from = layout.GetNodeWorldPosition(
                    node.FromGridNode);
                Vector3 to = layout.GetNodeWorldPosition(
                    node.ToGridNode);
                // The lane offset is measured on the ground. On a
                // graded street the 3D tangent tilts, and a right
                // vector built out of it comes up short, reading the
                // planner's 1.5 m as 1.4989.
                Vector3 forward = to - from;
                forward.y = 0f;
                forward.Normalize();
                Vector3 right = new Vector3(
                    forward.z,
                    0f,
                    -forward.x);
                float lateralOffset = Vector3.Dot(
                    node.Position - from,
                    right);
                Assert.That(
                    lateralOffset,
                    Is.EqualTo(plan.LaneCenterOffset)
                        .Within(GeometryTolerance),
                    node.Id);
            }

            for (int linkIndex = 0;
                 linkIndex < plan.Links.Count;
                 linkIndex++)
            {
                CityBusRouteLink link = plan.Links[linkIndex];
                Assert.That(link.Clearance.IsClear, Is.True, link.Id);
                Assert.That(
                    plan.GetOutgoingLinkIndices(link.FromNodeIndex),
                    Does.Contain(linkIndex),
                    link.Id);
                Assert.That(
                    link.ToNodeIndex,
                    Is.InRange(0, plan.Nodes.Count - 1),
                    link.Id);
                Assert.That(link.Samples.Count, Is.GreaterThan(1), link.Id);
                Assert.That(
                    plan.GetNextOrderedLinkIndex(linkIndex),
                    Is.EqualTo((linkIndex + 1) % plan.Links.Count),
                    link.Id);
            }
        }

        [Test]
        public void Turns_RetainClearWideArcsAndRejectTightRights()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            var selectedIntersections = new HashSet<Vector2Int>(
                CityBusIntersectionSelector.Select(layout));
            CityBusRouteLink[] leftTurns = plan.Links
                .Where(link =>
                    link.Kind == CityBusRouteLinkKind.LeftTurn)
                .ToArray();
            CityBusRouteLink[] rightTurns = plan.Links
                .Where(link =>
                    link.Kind == CityBusRouteLinkKind.RightTurn)
                .ToArray();

            Assert.That(leftTurns.Length, Is.GreaterThan(0));
            Assert.That(rightTurns.Length, Is.GreaterThan(0));
            for (int index = 0; index < leftTurns.Length; index++)
            {
                CityBusRouteLink turn = leftTurns[index];
                Assert.That(turn.Clearance.IsClear, Is.True, turn.Id);
                Assert.That(
                    turn.MinimumTurnRadius,
                    Is.EqualTo(CityBusPlanner.LeftTurnRadius)
                        .Within(GeometryTolerance),
                    turn.Id);
                Assert.That(
                    turn.MinimumTurnRadius,
                    Is.GreaterThanOrEqualTo(
                        plan.Vehicle.MinimumBodyCenterTurnRadius),
                    turn.Id);
                Assert.That(
                    selectedIntersections,
                    Does.Contain(turn.JunctionNode),
                    turn.Id);
            }

            for (int index = 0; index < rightTurns.Length; index++)
            {
                CityBusRouteLink turn = rightTurns[index];
                Assert.That(turn.Clearance.IsClear, Is.True, turn.Id);
                Assert.That(
                    turn.MinimumTurnRadius,
                    Is.GreaterThanOrEqualTo(
                        plan.Vehicle.MinimumBodyCenterTurnRadius),
                    turn.Id);
                Assert.That(
                    turn.MinimumTurnRadius,
                    Is.EqualTo(CityBusPlanner.RightTurnRadius)
                        .Within(GeometryTolerance),
                    turn.Id);
                Assert.That(
                    selectedIntersections,
                    Does.Contain(turn.JunctionNode),
                    turn.Id);
            }

            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            var signalNodes = new HashSet<Vector2Int>(
                night.TrafficSignals.Select(signal =>
                    signal.IntersectionNode));
            Assert.That(
                plan.Links.Any(link =>
                    signalNodes.Contains(link.JunctionNode)),
                Is.True,
                "The retained route must prove the signal intersections " +
                "that connect its closed component.");
            for (int linkIndex = 0;
                 linkIndex < plan.Links.Count;
                 linkIndex++)
            {
                CityBusRouteLink link = plan.Links[linkIndex];
                for (int sampleIndex = 0;
                     sampleIndex < link.Samples.Count;
                     sampleIndex++)
                {
                    CityBusPathSample sample = link.Samples[sampleIndex];
                    for (int signalIndex = 0;
                         signalIndex < night.TrafficSignals.Count;
                         signalIndex++)
                    {
                        TrafficSignalDescriptor signal =
                            night.TrafficSignals[signalIndex];
                        Assert.That(
                            FixtureOverlapsInflatedBus(
                                plan.Vehicle,
                                sample,
                                signal.Position),
                            Is.False,
                            $"{link.Id} sample {sampleIndex} overlaps " +
                            $"signal {signal.IntersectionNode}/" +
                            signal.PairIndex);
                    }
                }
            }

            CityBusClearanceFailure[] rightFailures =
                plan.ClearanceFailures
                    .Where(failure =>
                        failure.Kind ==
                        CityBusRouteLinkKind.RightTurn)
                    .ToArray();
            Assert.That(rightFailures.Length, Is.GreaterThan(0));
            Assert.That(
                rightFailures
                    .Where(failure => failure.Clearance.FailureKind ==
                        CityBusClearanceFailureKind.CurvatureTooTight)
                    .All(failure =>
                    !failure.Clearance.IsClear &&
                    failure.MinimumTurnRadius <
                        plan.Vehicle.MinimumBodyCenterTurnRadius),
                Is.True);
            Assert.That(
                rightFailures.Any(failure =>
                    failure.Clearance.FailureKind ==
                        CityBusClearanceFailureKind.CurvatureTooTight),
                Is.True);
        }

        [Test]
        public void SemanticStops_CoverEveryPointOfInterestAndPlayerHome()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            float halfBody = plan.Vehicle.InflatedLength * 0.5f;
            float safeEnd = halfBody +
                CityBusPlanner.StopJunctionClearance;
            Assert.That(plan.SpawnAnchors.Count, Is.GreaterThan(0));
            for (int index = 0;
                 index < plan.SpawnAnchors.Count;
                 index++)
            {
                CityBusSpawnAnchor anchor = plan.SpawnAnchors[index];
                CityBusRouteLink link = plan.Links[anchor.LinkIndex];
                Assert.That(
                    link.Kind,
                    Is.EqualTo(CityBusRouteLinkKind.Straight),
                    anchor.Id);
                Assert.That(
                    anchor.DistanceAlongLink,
                    Is.GreaterThanOrEqualTo(safeEnd),
                    anchor.Id);
                Assert.That(
                    anchor.DistanceAlongLink,
                    Is.LessThanOrEqualTo(link.Length - safeEnd),
                    anchor.Id);
                Assert.That(
                    layout.GetPathKind(anchor.RoadEdge),
                    Is.EqualTo(CityPathKind.Street),
                    anchor.Id);
            }

            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.DistrictPointOfInterest),
                Is.EqualTo(layout.DistrictPointsOfInterest.Count));
            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.PlayerHome),
                Is.EqualTo(1));
            // Coverage stop counts are bounded, not pinned: stops that
            // land closer than the minimum spacing to a neighbour are
            // coalesced into it, and the coverage tests prove the
            // surviving poles still serve every gate.
            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.OpenAreaAccess),
                Is.GreaterThanOrEqualTo(
                    (layout.OpenAreaAccesses.Count / 2) + 1),
                "Most open-area gates keep their own stop.");
            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.ParkGate),
                Is.LessThanOrEqualTo(2));
            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.Supermarket),
                Is.LessThanOrEqualTo(1));
            Assert.That(
                plan.Stops
                    .Where(stop => stop.TargetKind !=
                        CityBusStopTargetKind.LoopSpacing)
                    .Select(stop => stop.TargetId)
                    .Distinct()
                    .Count(),
                Is.EqualTo(plan.Stops.Count(stop => stop.TargetKind !=
                    CityBusStopTargetKind.LoopSpacing)));
            // A physical edge may carry two stops — one per driving
            // direction, each with its own kerbside pole — but never two
            // stops on the same directed link.
            Assert.That(
                plan.Stops.Select(stop => stop.LinkIndex)
                    .Distinct()
                    .Count(),
                Is.EqualTo(plan.Stops.Count));
            CityStreetSurfacePlan streetSurfaces =
                CityStreetSurfacePlanner.Create(layout);
            for (int stopIndex = 0;
                 stopIndex < plan.Stops.Count;
                 stopIndex++)
            {
                CityBusStopDescriptor stop = plan.Stops[stopIndex];
                CityBusRouteLink link = plan.Links[stop.LinkIndex];
                Assert.That(
                    link.Kind,
                    Is.EqualTo(CityBusRouteLinkKind.Straight),
                    stop.Id);
                Assert.That(link.Clearance.IsClear, Is.True, stop.Id);
                Assert.That(
                    layout.GetPathKind(stop.RoadEdge),
                    Is.EqualTo(CityPathKind.Street),
                    stop.Id);
                Assert.That(
                    stop.DistanceAlongLink,
                    Is.InRange(safeEnd, link.Length - safeEnd),
                    stop.Id);
                Assert.That(stop.SequenceIndex, Is.EqualTo(stopIndex));
                Assert.That(
                    stop.Origin,
                    Is.EqualTo(CityBusStopOrigin.RouteNative),
                    stop.Id);
                Assert.That(stop.SourceDecorationId, Is.Empty, stop.Id);
                Assert.That(
                    stop.DistanceAlongLoop,
                    Is.GreaterThan(stopIndex == 0
                        ? 0f
                        : plan.Stops[stopIndex - 1].DistanceAlongLoop),
                    stop.Id);
                Assert.That(
                    stop.DistanceAlongLoop,
                    Is.LessThan(plan.LoopLength),
                    stop.Id);
                float expectedLoopDistance = stop.DistanceAlongLink;
                for (int linkIndex = 0;
                     linkIndex < stop.LinkIndex;
                     linkIndex++)
                {
                    expectedLoopDistance += plan.Links[linkIndex].Length;
                }
                Assert.That(
                    stop.DistanceAlongLoop,
                    Is.EqualTo(expectedLoopDistance)
                        .Within(GeometryTolerance),
                    stop.Id);
                Vector3 right = new Vector3(
                    stop.Forward.z,
                    0f,
                    -stop.Forward.x).normalized;
                Vector3 roadsideOffset =
                    stop.ShelterPosition - stop.Position;
                roadsideOffset.y = 0f;
                float expectedOffset =
                    (layout.RoadWidth * 0.5f) +
                    CityBusPlanner.RoadsidePoleOutsideRoadEdge -
                    plan.LaneCenterOffset;
                Assert.That(
                    Vector3.Dot(roadsideOffset, right),
                    Is.EqualTo(expectedOffset).Within(GeometryTolerance),
                    stop.Id);
                Assert.That(
                    Vector3.Dot(roadsideOffset, stop.Forward),
                    Is.EqualTo(0f).Within(GeometryTolerance),
                    stop.Id);
                // The shelter stands on the physical ground the walker's
                // CharacterController lands on; the analytic road-line
                // height is only the fallback where nothing samples.
                float expectedShelterY =
                    CityBusPlanner.TryResolveShelterGroundTop(
                        layout,
                        streetSurfaces,
                        stop.ShelterPosition,
                        out float shelterGroundTop)
                        ? shelterGroundTop
                        : stop.Position.y +
                          CityStreetSurfacePlanner.SidewalkTop -
                          CityStreetSurfacePlanner.RoadTop;
                Assert.That(
                    stop.ShelterPosition.y,
                    Is.EqualTo(expectedShelterY)
                        .Within(GeometryTolerance),
                    stop.Id);
                Assert.That(
                    Vector3.Distance(stop.RoadsideForward, -right),
                    Is.LessThanOrEqualTo(GeometryTolerance),
                    stop.Id);
                Assert.That(
                    plan.GetStopIndices(stop.LinkIndex),
                    Does.Contain(stopIndex),
                    stop.Id);
                CityBusSpawnAnchor anchor = plan.SpawnAnchors.Single(
                    candidate => candidate.LinkIndex == stop.LinkIndex);
                Assert.That(
                    Mathf.Abs(
                        anchor.DistanceAlongLink -
                        stop.DistanceAlongLink),
                    Is.GreaterThan(GeometryTolerance),
                    stop.Id);
            }

            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[index];
                CityBusStopDescriptor stop = plan.Stops.Single(candidate =>
                    candidate.TargetKind ==
                        CityBusStopTargetKind.DistrictPointOfInterest &&
                    candidate.TargetId == point.Id);
                Assert.That(stop.TargetCell, Is.EqualTo(point.Cell));
                Assert.That(stop.District, Is.EqualTo(point.District));
                Assert.That(
                    RoadEdgeHop(
                        stop.RoadEdge,
                        point.Accesses.Select(access =>
                            access.FrontageEdge)),
                    Is.LessThanOrEqualTo(
                        layout.River.IsEnabled ? 5 : 1),
                    stop.Id);
                Vector2Int roadsideCell = GetRoadsideCell(plan, stop);
                Assert.That(roadsideCell, Is.Not.EqualTo(point.Cell), stop.Id);
                Assert.That(
                    layout.TryGetArea(
                        roadsideCell,
                        out CityAreaDefinition roadsideArea),
                    Is.True,
                    stop.Id);
                Assert.That(
                    roadsideArea.Archetype,
                    Is.EqualTo(point.District),
                    stop.Id);
                Assert.That(
                    Contains(point.PublicBounds, stop.ShelterPosition),
                    Is.False,
                    stop.Id);
                Assert.That(
                    point.Accesses.Any(access => Contains(
                        access.ApproachBounds,
                        stop.ShelterPosition)),
                    Is.False,
                    stop.Id);
            }

            BuildingLot home = layout.PlayerHome;
            CityBusStopDescriptor homeStop = plan.Stops.Single(stop =>
                stop.TargetKind == CityBusStopTargetKind.PlayerHome);
            Assert.That(homeStop.TargetCell, Is.EqualTo(home.Cell));
            Assert.That(
                homeStop.NameLocalizationKey,
                Is.EqualTo("bus.stop.default_coastal.home"));
            Assert.That(
                layout.TryGetFrontageEdge(home, out RoadEdge homeFrontage),
                Is.True);
            Assert.That(
                RoadEdgeHop(homeStop.RoadEdge, new[] { homeFrontage }),
                Is.LessThanOrEqualTo(1));
            Assert.That(
                GetRoadsideCell(plan, homeStop),
                Is.Not.EqualTo(home.Cell));
            Assert.That(
                Contains(home.WorldBounds, homeStop.ShelterPosition),
                Is.False);
            float homeDistance = Mathf.Min(
                XzDistance(
                    homeStop.ShelterPosition,
                    home.ReturnPosition),
                XzDistance(
                    homeStop.ShelterPosition,
                    home.SidewalkArrivalPosition));
            Assert.That(
                homeDistance,
                Is.LessThanOrEqualTo(
                    Mathf.Max(layout.NodeSpacing.x, layout.NodeSpacing.y) +
                    layout.RoadWidth));
        }

        [Test]
        public void StopBearingEdges_AreVisitedOnlyAtTheirStopOccurrence()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            int streetEdgeCount = layout.RoadEdges.Count(edge =>
                layout.GetPathKind(edge) == CityPathKind.Street);

            Assert.That(
                plan.StreetStateCount,
                Is.EqualTo(streetEdgeCount * 2));
            // A stop belongs to one DRIVING DIRECTION of its street: the
            // loop may drive the opposite direction as often as it needs
            // (that pass runs on the far side of the carriageway), but
            // the stop's own direction must be driven exactly once so
            // the bus never cruises past its own pole.
            foreach (CityBusStopDescriptor stop in plan.Stops)
            {
                CityBusRouteNode stopNode =
                    plan.Nodes[plan.Links[stop.LinkIndex].FromNodeIndex];
                int longStraightOccurrences = 0;
                for (int index = 0; index < plan.Links.Count; index++)
                {
                    CityBusRouteLink link = plan.Links[index];
                    CityBusRouteNode node =
                        plan.Nodes[link.FromNodeIndex];
                    if (node.FromGridNode == stopNode.FromGridNode &&
                        node.ToGridNode == stopNode.ToGridNode &&
                        link.Kind == CityBusRouteLinkKind.Straight &&
                        link.Length > layout.RoadWidth)
                    {
                        longStraightOccurrences++;
                    }
                }

                Assert.That(
                    longStraightOccurrences,
                    Is.EqualTo(1),
                    stop.Id);
            }
        }

        /// <summary>
        /// Regression: the stop order was once the district enum, then a
        /// shortest closed tour over five semantic targets. The grand loop
        /// replaces both — targets are ordered by their station along the
        /// road-grid perimeter, counter-clockwise so the right-hand doors
        /// face the outer precincts, and the river is crossed exactly
        /// twice, once per road bridge.
        /// </summary>
        [Test]
        public void ServedOrder_IsABankContiguousLoopStartingAtHome()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(layout, decorations);
            Assert.That(plan.Stops.Count, Is.GreaterThan(2));

            Assert.That(
                plan.Stops[0].TargetKind,
                Is.EqualTo(CityBusStopTargetKind.PlayerHome),
                "A closed loop has no last stop, so numbering starts at the " +
                "one place the hero can name.");

            // Stops follow the loop, and the loop may cross the river only
            // over the two road bridges, each exactly once — so the served
            // sequence changes banks exactly twice per lap.
            int corridorCellX = layout.River.Definition.CorridorCellX;
            int bankTransitions = 0;
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                bool currentEast = IsEastBank(
                    plan.Stops[index].RoadEdge,
                    corridorCellX);
                bool nextEast = IsEastBank(
                    plan.Stops[(index + 1) % plan.Stops.Count].RoadEdge,
                    corridorCellX);
                if (currentEast != nextEast)
                {
                    bankTransitions++;
                }
            }

            Assert.That(bankTransitions, Is.EqualTo(2));

            // Same layout and seed must always produce the same loop, so the
            // tie-break cannot depend on the order the search reaches
            // equal-length tours in.
            CityBusPlan repeat = CityBusPlanner.Create(layout, decorations);
            Assert.That(repeat.Stops.Count, Is.EqualTo(plan.Stops.Count));
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                Assert.That(
                    repeat.Stops[index].Id,
                    Is.EqualTo(plan.Stops[index].Id));
            }
        }

        private static bool IsEastBank(RoadEdge edge, int corridorCellX)
        {
            return edge.A.x >= corridorCellX + 1;
        }

        private static void CreateContext(
            out CityLayout layout,
            out CityDecorationPlan decorations)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Resolve(
                    GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            RoadFencePlan fences = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            decorations = CityDecorationPlanner.CreatePlan(
                layout,
                fences,
                night);
        }

        private static Vector2Int GetRoadsideCell(
            CityBusPlan plan,
            CityBusStopDescriptor stop)
        {
            CityBusRouteNode node = plan.Nodes[
                plan.Links[stop.LinkIndex].FromNodeIndex];
            Vector2Int direction = node.ToGridNode - node.FromGridNode;
            if (direction == Vector2Int.right)
            {
                return new Vector2Int(
                    node.FromGridNode.x,
                    node.FromGridNode.y - 1);
            }

            if (direction == Vector2Int.left)
            {
                return new Vector2Int(
                    node.ToGridNode.x,
                    node.ToGridNode.y);
            }

            if (direction == Vector2Int.up)
            {
                return node.FromGridNode;
            }

            return new Vector2Int(
                node.FromGridNode.x - 1,
                node.ToGridNode.y);
        }

        private static int RoadEdgeHop(
            RoadEdge edge,
            IEnumerable<RoadEdge> frontages)
        {
            int result = int.MaxValue;
            foreach (RoadEdge frontage in frontages)
            {
                if (edge == frontage)
                {
                    return 0;
                }

                if (edge.Contains(frontage.A) ||
                    edge.Contains(frontage.B))
                {
                    result = 1;
                    continue;
                }

                int nodeDistance = Mathf.Min(
                    ManhattanDistance(edge.A, frontage.A),
                    ManhattanDistance(edge.A, frontage.B),
                    ManhattanDistance(edge.B, frontage.A),
                    ManhattanDistance(edge.B, frontage.B));
                result = Mathf.Min(result, nodeDistance + 1);
            }

            return result;
        }

        private static int ManhattanDistance(
            Vector2Int left,
            Vector2Int right) =>
            Mathf.Abs(left.x - right.x) + Mathf.Abs(left.y - right.y);

        private static bool Contains(Rect bounds, Vector3 point)
        {
            return point.x >= bounds.xMin - GeometryTolerance &&
                   point.x <= bounds.xMax + GeometryTolerance &&
                   point.z >= bounds.yMin - GeometryTolerance &&
                   point.z <= bounds.yMax + GeometryTolerance;
        }

        private static bool Contains(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x - GeometryTolerance &&
                   point.x <= bounds.max.x + GeometryTolerance &&
                   point.z >= bounds.min.z - GeometryTolerance &&
                   point.z <= bounds.max.z + GeometryTolerance;
        }

        private static float XzDistance(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }

        private static bool FixtureOverlapsInflatedBus(
            CityBusDesignVehicle vehicle,
            CityBusPathSample sample,
            Vector3 fixturePosition)
        {
            Vector3 forward = sample.Forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = new Vector3(
                forward.z,
                0f,
                -forward.x);
            Vector3 delta = fixturePosition - sample.Position;
            delta.y = 0f;
            float outsideForward = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(delta, forward)) -
                (vehicle.InflatedLength * 0.5f));
            float outsideRight = Mathf.Max(
                0f,
                Mathf.Abs(Vector3.Dot(delta, right)) -
                (vehicle.InflatedWidth * 0.5f));
            float distanceSquared =
                outsideForward * outsideForward +
                outsideRight * outsideRight;
            float fixtureRadius =
                CityBusPlanner.TrafficSignalFixtureRadius;
            return distanceSquared <=
                   fixtureRadius * fixtureRadius +
                   (GeometryTolerance * GeometryTolerance);
        }

    }
}
