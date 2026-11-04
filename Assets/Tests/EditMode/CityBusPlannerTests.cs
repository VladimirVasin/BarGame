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
        public void DistrictNetwork_ClosesEachLineAndServesItsTransfers()
        {
            CityLayout layout = CityLayoutGenerator.Generate(CityBlueprintCatalog.Resolve(
                GameSessionState.DefaultCityBlueprintId), CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var lines = CityDistrictBusPlanner.Create(layout);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, lines.Select(line => line.Number));
            var surfaces = CityStreetSurfacePlanner.Create(layout);
            Assert.That(lines.Sum(line => line.Stops.Count), Is.EqualTo(20));
            foreach (var line in lines)
            {
                var intervals = line.Stops.Select((stop, i) =>
                    (i + 1 == line.Stops.Count ? line.LoopLength + line.Stops[0].DistanceAlongLoop :
                    line.Stops[i + 1].DistanceAlongLoop) - stop.DistanceAlongLoop).ToArray();
                TestContext.Out.WriteLine($"{line.RouteId}: {line.LoopLength:F0} m, {line.Stops.Count} stops, " +
                    $"{intervals.Min():F0}–{intervals.Max():F0} m apart. " + string.Join(", ",
                    line.Stops.Select(s => $"{s.TargetId} {s.ShelterPosition:F0}")));
            }
            var transferPlatforms = lines.SelectMany(line => line.Stops.Where(stop => stop.TransferNumber > 0)
                .Select(stop => (line.Number, stop))).GroupBy(entry => entry.stop.TargetId).ToArray();
            CollectionAssert.AreEquivalent(new[] { "transfer:west-slope", "transfer:east-slope", "transfer:mouth" },
                transferPlatforms.Select(group => group.Key));
            foreach (var group in transferPlatforms)
            {
                var pair = group.ToArray();
                Assert.That(pair.Length, Is.EqualTo(2), group.Key);
                Assert.That(pair[0].stop.TransferNumber, Is.EqualTo(pair[1].Number), group.Key);
                Assert.That(pair[1].stop.TransferNumber, Is.EqualTo(pair[0].Number), group.Key);
                Assert.That(Vector3.Distance(pair[0].stop.ShelterPosition, pair[1].stop.ShelterPosition),
                    Is.LessThan(85f), group.Key + " requires a short walk between " + pair[0].stop.ShelterPosition + " and " + pair[1].stop.ShelterPosition);
            }
            float westEdge = layout.RoadGeometry.GetNodePosition(new Vector2Int(layout.River.Definition.CorridorCellX, 1)).x;
            float eastEdge = layout.RoadGeometry.GetNodePosition(new Vector2Int(layout.River.Definition.CorridorCellX + 1, 1)).x;
            foreach (var line in lines)
            {
                Assert.That(line.Stops.Count(s => s.TransferNumber == 0), Is.GreaterThan(2), line.RouteId);
                Assert.That(line.Stops.Select(s => s.Id).Distinct().Count(), Is.EqualTo(line.Stops.Count));
                Assert.That(line.Stops.Select(s => s.DistanceAlongLoop), Is.Ordered);
                foreach (var stop in line.Stops)
                {
                    Assert.That(CityBusPlatformClearance.ForLayout(layout).Allows(stop), Is.True, stop.Id);
                    Assert.That(surfaces.TryGetStopCrossing(stop.Id, out var zebra), Is.True, stop.Id);
                    Assert.That(Vector3.ProjectOnPlane(stop.Position - zebra.Center, Vector3.up).magnitude,
                        Is.LessThan(26.2f), stop.Id);
                    Assert.That(zebra.Bounds.Overlaps(CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop)), Is.False, stop.Id);
                    AssertStopPaint(zebra, surfaces);
                }
                foreach (int index in line.OrderedLinkIndices)
                {
                    var link = line.Links[index];
                    var next = line.Links[line.GetNextOrderedLinkIndex(index)];
                    Assert.That(Vector3.Distance(link.Samples[link.Samples.Count - 1].Position,
                        next.Samples[0].Position), Is.LessThan(.15f), link.Id);
                    Assert.That(link.Clearance.IsClear, Is.True, link.Id);
                    if (line.Number == 1)
                        Assert.That(link.Samples.Max(p => p.Position.x), Is.LessThanOrEqualTo(westEdge + layout.RoadWidth));
                    if (line.Number == 2)
                        Assert.That(link.Samples.Min(p => p.Position.x), Is.GreaterThanOrEqualTo(eastEdge - layout.RoadWidth));
                }
            }
            Assert.That(lines[2].Stops.Any(s => s.ShelterPosition.y > 39f), Is.True);
            Assert.That(lines[2].Stops.Count(s => s.Id.Contains(":west-terrace-")), Is.InRange(3, 4));
            Assert.That(lines[2].Stops.Count(s => s.TargetId.StartsWith("east-terrace-")), Is.InRange(2, 3));
            var home = lines.SelectMany(line => line.Stops).Single(stop => stop.TargetKind == CityBusStopTargetKind.PlayerHome);
            Assert.That(home, Is.SameAs(lines[2].Stops[3]));
            Assert.That(home.TargetCell, Is.EqualTo(layout.PlayerHome.Cell));
            Assert.That(home.TargetId, Is.EqualTo("player-home:" + layout.BlueprintId + ":" + home.TargetCell.x + ":" + home.TargetCell.y));
            Assert.That(home.NameLocalizationKey, Is.EqualTo("bus.stop.default_coastal.home"));
            Assert.That(Vector3.Distance(home.ShelterPosition, layout.PlayerHome.SidewalkArrivalPosition), Is.LessThan(55f));
            var lower = lines[1].Stops[1];
            Assert.That(lower.TargetKind, Is.EqualTo(CityBusStopTargetKind.ResidentialBlock));
            Assert.That(lower.TargetCell, Is.EqualTo(layout.LowerResidentialLot.Cell));
            Assert.That(lower.NameLocalizationKey, Is.EqualTo("bus.stop.default_coastal.residential_block"));
        }

        private static void AssertStopPaint(CityBusStopCrossing site, CityStreetSurfacePlan surfaces)
        {
            var path = site.WalkPath;
            foreach (Vector3 kerb in new[] { path[0], path[3] })
            {
                Assert.That(surfaces.SupportPlan.TrySampleTop(new Vector2(kerb.x, kerb.z),
                    FootstepGroundKind.Stone, out float top), Is.True, site.Id);
                Assert.That(kerb.y, Is.EqualTo(top).Within(.015f), site.Id);
            }
            Vector3 across = Vector3.ProjectOnPlane(path[3] - path[0], Vector3.up).normalized;
            float previous = float.NegativeInfinity;
            Assert.That(site.Stripes, Has.Count.EqualTo(6), site.Id);
            foreach (var stripe in site.Stripes)
            {
                float position = Vector3.Dot(stripe.Center - site.Center, across);
                Assert.That(position, Is.GreaterThan(previous + stripe.Size.x), site.Id + " asphalt between stripes");
                previous = position;
                Vector3 longSide = Vector3.ProjectOnPlane(stripe.Rotation * Vector3.forward, Vector3.up).normalized;
                Assert.That(Mathf.Abs(Vector3.Dot(longSide, across)), Is.LessThan(.02f), site.Id + " long edge follows traffic");
                Assert.That(stripe.Size.z, Is.InRange(2.38f, 2.55f), site.Id);
                Assert.That(stripe.Size.x, Is.InRange(.45f, .62f), site.Id);
                foreach (int x in new[] { -1, 1 })
                foreach (int z in new[] { -1, 1 })
                {
                    Vector3 corner = stripe.Center + stripe.Rotation * new Vector3(
                        stripe.Size.x * .5f * x, .5f * stripe.Size.y, stripe.Size.z * .5f * z);
                    Assert.That(surfaces.SupportPlan.TrySampleTop(new Vector2(corner.x, corner.z),
                        FootstepGroundKind.Concrete, out float asphalt), Is.True, site.Id);
                    Assert.That(corner.y - asphalt, Is.InRange(-.003f, .035f), site.Id + " paint follows road");
                }
            }
            Assert.That(Vector3.Dot(site.Stripes[0].Center - site.Center, across), Is.LessThan(-2f), site.Id);
            Assert.That(previous, Is.GreaterThan(2f), site.Id);
        }

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
            CityStreetSupportPlan support = CityStreetSupportPlan.ForLayout(layout);
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
                if (!current.Id.StartsWith("bus:road:")) continue;
                foreach (CityBusPathSample sample in current.Samples)
                {
                    Assert.That(support.TrySampleTop(
                            new Vector2(sample.Position.x, sample.Position.z),
                            FootstepGroundKind.Concrete, out float physicalTop),
                        Is.True, current.Id);
                    Assert.That(sample.Position.y,
                        Is.EqualTo(physicalTop).Within(GeometryTolerance),
                        current.Id);
                }
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
                if (stop.TargetKind == CityBusStopTargetKind.ResidentialBlock)
                {
                    Assert.That(
                        layout.TryGetFrontageEdge(
                            layout.LowerResidentialLot,
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
                    CityBusStopTargetKind.ResidentialBlock),
                Is.EqualTo(1));
            Assert.That(CityDistrictBusPlanner.Create(layout)[2].Stops.Count(stop => stop.TargetKind ==
                CityBusStopTargetKind.PlayerHome && stop.TargetCell == layout.PlayerHome.Cell), Is.EqualTo(1));
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
                    CityBusPlanner.GetRoadsidePoleOutsideRoadEdge(layout) -
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

            BuildingLot home = layout.LowerResidentialLot;
            CityBusStopDescriptor homeStop = plan.Stops.Single(stop =>
                stop.TargetKind == CityBusStopTargetKind.ResidentialBlock);
            Assert.That(homeStop.TargetCell, Is.EqualTo(home.Cell));
            Assert.That(
                homeStop.NameLocalizationKey,
                Is.EqualTo("bus.stop.default_coastal.residential_block"));
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
        public void ServedOrder_IsABankContiguousLoopStartingAtTheLowerResidentialBlock()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(layout, decorations);
            Assert.That(plan.Stops.Count, Is.GreaterThan(2));

            Assert.That(
                plan.Stops[0].TargetKind,
                Is.EqualTo(CityBusStopTargetKind.ResidentialBlock),
                "The grid service keeps its established first residential platform.");

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
