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
            Assert.That(
                plan.LoopLength,
                Is.EqualTo(plan.Links.Sum(link => link.Length))
                    .Within(GeometryTolerance));
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
                Vector3 forward = (to - from).normalized;
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
                plan.Stops.Count,
                Is.EqualTo(layout.DistrictPointsOfInterest.Count + 1));
            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.DistrictPointOfInterest),
                Is.EqualTo(layout.DistrictPointsOfInterest.Count));
            Assert.That(
                plan.Stops.Count(stop => stop.TargetKind ==
                    CityBusStopTargetKind.PlayerHome),
                Is.EqualTo(1));
            Assert.That(
                plan.Stops.Select(stop => stop.TargetId).Distinct().Count(),
                Is.EqualTo(plan.Stops.Count));
            Assert.That(
                plan.Stops.Select(stop => stop.RoadEdge).Distinct().Count(),
                Is.EqualTo(plan.Stops.Count));
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
                float expectedLoopDistance = plan.Links
                    .Take(stop.LinkIndex)
                    .Sum(routeLink => routeLink.Length) +
                    stop.DistanceAlongLink;
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
                Assert.That(
                    stop.ShelterPosition.y,
                    Is.EqualTo(CityStreetSurfacePlanner.SidewalkTop)
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
                    Is.LessThanOrEqualTo(1),
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
            var stopEdges = new HashSet<RoadEdge>(
                plan.Stops.Select(stop => stop.RoadEdge));
            foreach (RoadEdge stopEdge in stopEdges)
            {
                int longStraightOccurrences = 0;
                for (int index = 0; index < plan.Links.Count; index++)
                {
                    CityBusRouteLink link = plan.Links[index];
                    CityBusRouteNode node =
                        plan.Nodes[link.FromNodeIndex];
                    if (node.RoadEdge == stopEdge &&
                        link.Kind == CityBusRouteLinkKind.Straight &&
                        link.Length > layout.RoadWidth)
                    {
                        longStraightOccurrences++;
                    }
                }

                Assert.That(
                    longStraightOccurrences,
                    Is.EqualTo(1),
                    stopEdge.ToString());
            }
        }

        /// <summary>
        /// Regression: the stop order was the district enum — Industrial,
        /// Nightlife, Residential, Old Town, then home — which carried no
        /// geography. On the default layout it crossed the whole city twice
        /// for a `1166 m` straight-line tour where `754 m` was available, and
        /// the road loop it forced ran `2592 m`. The order is now a shortest
        /// closed tour, which brought the same loop to `1798 m`.
        /// </summary>
        [Test]
        public void ServedOrder_IsAShortestClosedTourStartingAtHome()
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

            var positions = new List<Vector3>(plan.Stops.Count);
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                Vector3 position = plan.Stops[index].Position;
                position.y = 0f;
                positions.Add(position);
            }

            float served = MeasureCycle(positions, Identity(positions.Count));
            float best = MeasureBestCycle(positions);
            Assert.That(
                served,
                Is.LessThanOrEqualTo(best * 1.05f),
                $"The served order walks {served:F1} m between stops where " +
                $"{best:F1} m is available; a route that doubles back reads " +
                "as a mistake however well the streets between stops are " +
                "proven.");

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

        private static int[] Identity(int count)
        {
            var order = new int[count];
            for (int index = 0; index < count; index++)
            {
                order[index] = index;
            }

            return order;
        }

        private static float MeasureCycle(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<int> order)
        {
            float total = 0f;
            for (int index = 0; index < order.Count; index++)
            {
                total += Vector3.Distance(
                    points[order[index]],
                    points[order[(index + 1) % order.Count]]);
            }

            return total;
        }

        private static float MeasureBestCycle(IReadOnlyList<Vector3> points)
        {
            var order = Identity(points.Count);
            float best = MeasureCycle(points, order);
            PermuteTail(points, order, 1, ref best);
            return best;
        }

        private static void PermuteTail(
            IReadOnlyList<Vector3> points,
            int[] order,
            int start,
            ref float best)
        {
            if (start >= order.Length)
            {
                best = Mathf.Min(best, MeasureCycle(points, order));
                return;
            }

            for (int index = start; index < order.Length; index++)
            {
                (order[start], order[index]) = (order[index], order[start]);
                PermuteTail(points, order, start + 1, ref best);
                (order[start], order[index]) = (order[index], order[start]);
            }
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
                }
            }

            return result;
        }

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
