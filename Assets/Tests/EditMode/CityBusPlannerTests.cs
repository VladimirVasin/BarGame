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
            CityBusClearanceFailure firstLeftFailure =
                first.ClearanceFailures.FirstOrDefault(failure =>
                    failure.Kind == CityBusRouteLinkKind.LeftTurn &&
                    failure.Clearance.FailureKind ==
                    CityBusClearanceFailureKind.SidewalkOverlap);

            Assert.That(
                first.IsEmpty,
                Is.False,
                $"states={first.StreetStateCount}, " +
                $"accepted={first.ClearanceAcceptedLinkCount}, " +
                $"failures={first.ClearanceFailures.Count}, " +
                "failureKinds=" + string.Join(
                    ",",
                    first.ClearanceFailures
                        .GroupBy(failure =>
                            failure.Kind + "/" +
                            failure.Clearance.FailureKind)
                        .Select(group => group.Key + "=" + group.Count())) +
                ", " +
                "firstLeft=" + (firstLeftFailure == null
                    ? "none"
                    : firstLeftFailure.Id + "@" +
                      firstLeftFailure.Clearance.FailedSampleIndex + ":" +
                      firstLeftFailure.Clearance.FailedPosition) + ", " +
                "aprons=" + string.Join(
                    ",",
                    CityBusIntersectionSelector.Select(layout)));
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
        public void DefaultCoastal_BuildsOneCounterClockwiseParkRing()
        {
            CreateProductionContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            Vector3 parkCenter = layout.Park.Center;
            float minimumX = plan.Nodes.Min(node => node.Position.x);
            float maximumX = plan.Nodes.Max(node => node.Position.x);
            float minimumZ = plan.Nodes.Min(node => node.Position.z);
            float maximumZ = plan.Nodes.Max(node => node.Position.z);
            float signedArea = SignedArea(
                plan.OrderedLinkIndices
                    .Select(index => plan.Nodes[
                        plan.Links[index].FromNodeIndex].Position)
                    .ToArray());

            Assert.That(
                plan.OrderedLinkIndices,
                Is.EqualTo(Enumerable.Range(0, plan.Links.Count).ToArray()));
            Assert.That(plan.Nodes.Count, Is.EqualTo(plan.Links.Count));
            Assert.That(
                plan.Links.Count(link =>
                    link.Kind == CityBusRouteLinkKind.LeftTurn),
                Is.EqualTo(4));
            Assert.That(signedArea, Is.GreaterThan(0f));
            Assert.That(parkCenter.x, Is.InRange(minimumX, maximumX));
            Assert.That(parkCenter.z, Is.InRange(minimumZ, maximumZ));
            Assert.That(
                plan.LoopLength,
                Is.EqualTo(plan.Links.Sum(link => link.Length))
                    .Within(GeometryTolerance));
        }

        [Test]
        public void OrderedRing_IsStreetOnlyRightHandAndOneInOneOut()
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
        public void Turns_RetainOnlyClearSixMetreLeftArcs()
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

            Assert.That(leftTurns.Length, Is.GreaterThan(0));
            Assert.That(
                plan.Links.Any(link =>
                    link.Kind == CityBusRouteLinkKind.RightTurn),
                Is.False);
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

            CityBusClearanceFailure[] rightFailures =
                plan.ClearanceFailures
                    .Where(failure =>
                        failure.Kind ==
                        CityBusRouteLinkKind.RightTurn)
                    .ToArray();
            Assert.That(rightFailures.Length, Is.GreaterThan(0));
            Assert.That(
                rightFailures.All(failure =>
                    !failure.Clearance.IsClear &&
                    failure.Clearance.FailureKind ==
                    CityBusClearanceFailureKind.CurvatureTooTight &&
                    failure.MinimumTurnRadius <
                    plan.Vehicle.MinimumBodyCenterTurnRadius),
                Is.True);
        }

        [Test]
        public void AnchorsAndCanonicalStops_HaveSafeDistinctRoadsidePoses()
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
            CityDistrictKind[] expectedDistricts =
            {
                CityDistrictKind.Industrial,
                CityDistrictKind.Nightlife,
                CityDistrictKind.Residential,
                CityDistrictKind.OldTown
            };
            string[] expectedIds =
            {
                "bus-stop:default-coastal:ring-01:industrial",
                "bus-stop:default-coastal:ring-01:nightlife",
                "bus-stop:default-coastal:ring-01:residential",
                "bus-stop:default-coastal:ring-01:old-town"
            };
            string[] expectedKeys =
            {
                "bus.stop.default_coastal.industrial",
                "bus.stop.default_coastal.nightlife",
                "bus.stop.default_coastal.residential",
                "bus.stop.default_coastal.old_town"
            };
            var islandFrontages = new HashSet<RoadEdge>(
                layout.DistrictPointsOfInterest
                    .Where(point => point.Kind ==
                        CityDistrictPointOfInterestKind
                            .NightlifeLastRouteIsland)
                    .SelectMany(point => point.Accesses)
                    .Select(access => access.FrontageEdge));

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

            Assert.That(plan.Stops.Count, Is.EqualTo(4));
            CollectionAssert.AreEqual(
                expectedDistricts,
                plan.Stops.Select(stop => stop.District).ToArray());
            CollectionAssert.AreEqual(
                expectedIds,
                plan.Stops.Select(stop => stop.Id).ToArray());
            CollectionAssert.AreEqual(
                expectedKeys,
                plan.Stops.Select(stop => stop.NameLocalizationKey).ToArray());
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
                    islandFrontages.Contains(stop.RoadEdge),
                    Is.False,
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
        }

        [Test]
        public void CanonicalRing_CrossesIslandFrontageButDoesNotStopThere()
        {
            CreateContext(
                out CityLayout layout,
                out CityDecorationPlan decorations);
            CityBusPlan plan = CityBusPlanner.Create(
                layout,
                decorations);
            CityDistrictPointOfInterestDescriptor island =
                layout.DistrictPointsOfInterest.Single(point =>
                    point.Kind == CityDistrictPointOfInterestKind
                        .NightlifeLastRouteIsland);
            var excludedEdges = new HashSet<RoadEdge>(
                island.Accesses.Select(access => access.FrontageEdge));
            int streetEdgeCount = layout.RoadEdges.Count(edge =>
                layout.GetPathKind(edge) == CityPathKind.Street);

            Assert.That(excludedEdges, Is.Not.Empty);
            Assert.That(
                plan.StreetStateCount,
                Is.EqualTo(streetEdgeCount * 2));
            Assert.That(
                plan.SpawnAnchors.Any(anchor =>
                    excludedEdges.Contains(anchor.RoadEdge)),
                Is.True);
            Assert.That(
                plan.Stops.Any(stop =>
                    excludedEdges.Contains(stop.RoadEdge)),
                Is.False);
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

        private static void CreateProductionContext(
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

        private static float SignedArea(IReadOnlyList<Vector3> points)
        {
            float twiceArea = 0f;
            for (int index = 0; index < points.Count; index++)
            {
                Vector3 first = points[index];
                Vector3 second = points[(index + 1) % points.Count];
                twiceArea += (first.x * second.z) -
                             (second.x * first.z);
            }

            return twiceArea * 0.5f;
        }

    }
}
