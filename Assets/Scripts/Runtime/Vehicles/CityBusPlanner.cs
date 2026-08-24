using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Builds one immutable right-hand Route 01 through every actual district
    /// point of interest and the player home. It retains only full-body-clear
    /// straights, six-metre left arcs and two-edge wide-right macros; at signal
    /// nodes the inflated vehicle envelope must also clear both static poles.
    /// </summary>
    public static partial class CityBusPlanner
    {
        public const string DefaultRouteId =
            "bus-route:default-coastal:route-01";
        public const float PathSampleSpacing = 0.1f;
        public const float TurnTangentInset = -0.5f;
        public const float LeftTurnRadius = 6f;
        public const float RightTurnRadius = 4.5f;
        public const float MaximumShelterLaneDistance = 6f;
        public const float StopJunctionClearance = 0.5f;
        public const float RoadsidePoleOutsideRoadEdge = 0.2f;

        private const uint StableSeedSalt = 0x42555331u;
        private const float DirectionTolerance = 0.5f;
        private const float GeometryTolerance = 0.001f;

        public static CityBusPlan Create(
            CityLayout layout,
            CityDecorationPlan decorationPlan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorationPlan == null)
            {
                throw new ArgumentNullException(nameof(decorationPlan));
            }

            return Create(layout, decorationPlan.Seed);
        }

        public static CityBusPlan Create(CityLayout layout)
        {
            return Create(
                layout ?? throw new ArgumentNullException(nameof(layout)),
                layout.Seed);
        }

        private static CityBusPlan Create(
            CityLayout layout,
            int decorationSeed)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            CityBusDesignVehicle vehicle = CityBusDesignVehicle.Default;
            float carriagewayWidth = layout.RoadWidth -
                (CityStreetSurfacePlanner.SidewalkWidth * 2f);
            float laneCenterOffset = Mathf.Max(0f, carriagewayWidth * 0.25f);
            uint stableSeed = CityPedestrianStableHash.Combine(
                unchecked((uint)layout.Seed),
                CityPedestrianStableHash.Combine(
                    StableSeedSalt,
                    CityPedestrianStableHash.String(layout.BlueprintId)));

            var busIntersections = new HashSet<Vector2Int>(
                CityBusIntersectionSelector.Select(layout));
            var trafficSignalIntersections = new HashSet<Vector2Int>(
                CityStreetIntersectionSelector.Select(layout));
            List<DirectedStreet> streets = CreateDirectedStreets(
                layout,
                laneCenterOffset);
            var streetByDirection = new Dictionary<
                DirectedKey,
                DirectedStreet>();
            for (int index = 0; index < streets.Count; index++)
            {
                DirectedStreet street = streets[index];
                streetByDirection.Add(
                    new DirectedKey(street.From, street.To),
                    street);
            }

            var nodes = new List<TemporaryNode>(streets.Count * 2);
            for (int index = 0; index < streets.Count; index++)
            {
                DirectedStreet street = streets[index];
                street.DepartureNodeIndex = nodes.Count;
                nodes.Add(CreateNode(street, true, layout));
                street.ArrivalNodeIndex = nodes.Count;
                nodes.Add(CreateNode(street, false, layout));
            }

            var acceptedLinks = new List<TemporaryLink>();
            var failures = new List<CityBusClearanceFailure>();
            for (int index = 0; index < streets.Count; index++)
            {
                DirectedStreet street = streets[index];
                AddRoadSegment(
                    layout,
                    vehicle,
                    street,
                    acceptedLinks,
                    failures);
            }

            for (int index = 0; index < streets.Count; index++)
            {
                AddJunctionManeuvers(
                    layout,
                    vehicle,
                    streets[index],
                    streetByDirection,
                    busIntersections,
                    trafficSignalIntersections,
                    laneCenterOffset,
                    acceptedLinks,
                    failures);
            }

            List<RouteOccurrence> route = CreateTargetRoute(
                layout,
                vehicle,
                nodes,
                acceptedLinks);
            var finalLinks = new List<CityBusRouteLink>(
                route.Count);
            var routeLinkMetadata = new List<RouteLinkMetadata>(
                route.Count);
            var finalNodes = new List<CityBusRouteNode>(route.Count);
            var orderedLinkIndices = new List<int>(route.Count);
            float loopLength = 0f;
            for (int index = 0; index < route.Count; index++)
            {
                RouteOccurrence occurrence = route[index];
                TemporaryLink source = occurrence.Source;
                int finalIndex = index;
                int toNode = (index + 1) % route.Count;
                finalLinks.Add(new CityBusRouteLink(
                    source.Id + ":route-01:" + index,
                    index,
                    toNode,
                    source.Kind,
                    source.JunctionNode,
                    source.Samples,
                    source.MinimumTurnRadius,
                    source.Clearance));
                TemporaryNode node = nodes[source.FromNodeIndex];
                finalNodes.Add(new CityBusRouteNode(
                    node.Id + ":route-01:" + index,
                    node.Position,
                    node.Forward,
                    node.RoadEdge,
                    node.FromGridNode,
                    node.ToGridNode,
                    new[] { finalIndex }));
                routeLinkMetadata.Add(new RouteLinkMetadata(
                    source,
                    finalIndex,
                    occurrence.StopCandidate));
                orderedLinkIndices.Add(finalIndex);
                loopLength += finalLinks[finalIndex].Length;
            }

            string routeId = "bus-route:" + layout.BlueprintId +
                             ":route-01";
            List<CityBusStopDescriptor> stops = CreateTargetStops(
                layout,
                routeLinkMetadata,
                finalLinks);
            stops = CoalesceCloseStops(stops, loopLength);
            stops = InsertSpacingStops(
                layout,
                vehicle,
                nodes,
                routeLinkMetadata,
                finalLinks,
                loopLength,
                stops);
            List<CityBusSpawnAnchor> anchors = CreateSpawnAnchors(
                layout,
                vehicle,
                routeLinkMetadata,
                finalLinks,
                stops);
            return new CityBusPlan(
                layout.Seed,
                decorationSeed,
                stableSeed,
                routeId,
                orderedLinkIndices,
                loopLength,
                laneCenterOffset,
                vehicle,
                finalNodes,
                finalLinks,
                anchors,
                stops,
                failures,
                streets.Count,
                acceptedLinks.Count);
        }

        private static List<DirectedStreet> CreateDirectedStreets(
            CityLayout layout,
            float laneCenterOffset)
        {
            var edges = new List<RoadEdge>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (IsBusTraversableStreet(layout, edge))
                {
                    edges.Add(edge);
                }
            }

            edges.Sort(RoadEdge.Compare);
            var result = new List<DirectedStreet>(edges.Count * 2);
            for (int index = 0; index < edges.Count; index++)
            {
                RoadEdge edge = edges[index];
                result.Add(new DirectedStreet(
                    edge,
                    edge.A,
                    edge.B,
                    layout,
                    laneCenterOffset));
                result.Add(new DirectedStreet(
                    edge,
                    edge.B,
                    edge.A,
                    layout,
                    laneCenterOffset));
            }

            return result;
        }

        private static TemporaryNode CreateNode(
            DirectedStreet street,
            bool departure,
            CityLayout layout)
        {
            Vector3 position = departure
                ? GetDeparturePosition(layout, street)
                : GetArrivalPosition(layout, street);
            return new TemporaryNode(
                $"bus:{NodeId(street.From)}:{NodeId(street.To)}:" +
                (departure ? "departure" : "arrival"),
                position,
                street.GradeForward,
                street.RoadEdge,
                street.From,
                street.To);
        }

        private static void AddRoadSegment(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            DirectedStreet street,
            ICollection<TemporaryLink> accepted,
            ICollection<CityBusClearanceFailure> failures)
        {
            Vector3 start = GetDeparturePosition(layout, street);
            Vector3 end = GetArrivalPosition(layout, street);
            List<CityBusPathSample> samples = CreateLinearSamples(
                start,
                end,
                street.GradeForward);
            Rect carriageway = GetCarriagewayRect(layout, street.RoadEdge);
            CityBusClearanceResult clearance = ValidateSamples(
                vehicle,
                samples,
                new[] { carriageway });
            string id = $"bus:road:{NodeId(street.From)}:" +
                        $"{NodeId(street.To)}";
            if (clearance.IsClear)
            {
                accepted.Add(new TemporaryLink(
                    id,
                    street.DepartureNodeIndex,
                    street.ArrivalNodeIndex,
                    CityBusRouteLinkKind.Straight,
                    street.To,
                    samples,
                    float.PositiveInfinity,
                    clearance,
                    true,
                    street.RoadEdge));
                return;
            }

            failures.Add(new CityBusClearanceFailure(
                id,
                street.RoadEdge,
                street.RoadEdge,
                street.From,
                street.To,
                street.To,
                CityBusRouteLinkKind.Straight,
                samples,
                float.PositiveInfinity,
                clearance));
        }

        private static void AddJunctionManeuvers(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            DirectedStreet incoming,
            IReadOnlyDictionary<DirectedKey, DirectedStreet>
                streetByDirection,
            ISet<Vector2Int> busIntersections,
            ISet<Vector2Int> trafficSignalIntersections,
            float laneCenterOffset,
            ICollection<TemporaryLink> accepted,
            ICollection<CityBusClearanceFailure> failures)
        {
            var nextNodes = new List<Vector2Int>();
            for (int edgeIndex = 0;
                 edgeIndex < layout.RoadEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = layout.RoadEdges[edgeIndex];
                if (!edge.Contains(incoming.To) ||
                    edge == incoming.RoadEdge ||
                    !IsBusTraversableStreet(layout, edge))
                {
                    continue;
                }

                Vector2Int next = edge.Other(incoming.To);
                if (streetByDirection.ContainsKey(
                        new DirectedKey(incoming.To, next)))
                {
                    nextNodes.Add(next);
                }
            }

            nextNodes.Sort(CompareNodes);
            for (int index = 0; index < nextNodes.Count; index++)
            {
                Vector2Int nextNode = nextNodes[index];
                DirectedStreet outgoing = streetByDirection[
                    new DirectedKey(incoming.To, nextNode)];
                float dot = Vector3.Dot(
                    incoming.Forward,
                    outgoing.Forward);
                float crossY = Vector3.Cross(
                    incoming.Forward,
                    outgoing.Forward).y;
                if (dot > DirectionTolerance)
                {
                    AddStraightJunction(
                        layout,
                        vehicle,
                        incoming,
                        outgoing,
                        trafficSignalIntersections.Contains(incoming.To),
                        accepted,
                        failures);
                }
                else if (crossY < -DirectionTolerance)
                {
                    AddLeftJunction(
                        layout,
                        vehicle,
                        incoming,
                        outgoing,
                        busIntersections.Contains(incoming.To),
                        trafficSignalIntersections.Contains(incoming.To),
                        laneCenterOffset,
                        accepted,
                        failures);
                }
                else if (crossY > DirectionTolerance)
                {
                    AddRightJunction(
                        layout,
                        vehicle,
                        incoming,
                        outgoing,
                        busIntersections.Contains(incoming.To),
                        trafficSignalIntersections.Contains(incoming.To),
                        laneCenterOffset,
                        accepted,
                        failures);
                }
            }
        }

        private static void AddStraightJunction(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            DirectedStreet incoming,
            DirectedStreet outgoing,
            bool hasTrafficSignals,
            ICollection<TemporaryLink> accepted,
            ICollection<CityBusClearanceFailure> failures)
        {
            List<CityBusPathSample> samples = CreateLinearSamples(
                GetArrivalPosition(layout, incoming),
                GetDeparturePosition(layout, outgoing),
                incoming.Forward);
            Rect[] allowed =
            {
                GetCarriagewayRect(layout, incoming.RoadEdge),
                GetCarriagewayRect(layout, outgoing.RoadEdge)
            };
            CityBusClearanceResult clearance = ValidateSamples(
                vehicle,
                samples,
                allowed);
            clearance = ValidateStaticIntersectionFixtures(
                layout,
                vehicle,
                incoming.To,
                hasTrafficSignals,
                samples,
                clearance);
            string id = ManeuverId(incoming, outgoing, "straight");
            if (clearance.IsClear)
            {
                accepted.Add(new TemporaryLink(
                    id,
                    incoming.ArrivalNodeIndex,
                    outgoing.DepartureNodeIndex,
                    CityBusRouteLinkKind.Straight,
                    incoming.To,
                    samples,
                    float.PositiveInfinity,
                    clearance,
                    false,
                    incoming.RoadEdge));
                return;
            }

            failures.Add(CreateFailure(
                id,
                incoming,
                outgoing,
                CityBusRouteLinkKind.Straight,
                samples,
                float.PositiveInfinity,
                clearance));
        }

        private static void AddLeftJunction(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            DirectedStreet incoming,
            DirectedStreet outgoing,
            bool hasFullApron,
            bool hasTrafficSignals,
            float laneCenterOffset,
            ICollection<TemporaryLink> accepted,
            ICollection<CityBusClearanceFailure> failures)
        {
            float cornerOffset =
                (layout.RoadWidth * 0.5f) - TurnTangentInset;
            float radius = cornerOffset + laneCenterOffset;
            List<CityBusPathSample> samples = CreateTurnSamples(
                layout,
                incoming,
                outgoing,
                laneCenterOffset,
                true,
                radius);
            var allowed = new List<Rect>
            {
                GetCarriagewayRect(layout, incoming.RoadEdge),
                GetCarriagewayRect(layout, outgoing.RoadEdge)
            };
            if (hasFullApron)
            {
                allowed.Add(GetIntersectionCoreRect(
                    layout,
                    incoming.To));
                allowed.Add(GetIntersectionApproachRect(
                    layout,
                    incoming.To,
                    incoming.RoadEdge));
                allowed.Add(GetIntersectionApproachRect(
                    layout,
                    incoming.To,
                    outgoing.RoadEdge));
            }

            CityBusClearanceResult clearance = ValidateSamples(
                vehicle,
                samples,
                allowed);
            clearance = ValidateStaticIntersectionFixtures(
                layout,
                vehicle,
                incoming.To,
                hasTrafficSignals,
                samples,
                clearance);
            if (!hasFullApron)
            {
                clearance = new CityBusClearanceResult(
                    false,
                    CityBusClearanceFailureKind
                        .IntersectionApronUnavailable,
                    0,
                    layout.GetNodeWorldPosition(incoming.To),
                    0f);
            }

            string id = ManeuverId(incoming, outgoing, "left");
            if (hasFullApron &&
                radius + GeometryTolerance >=
                    vehicle.MinimumBodyCenterTurnRadius &&
                clearance.IsClear)
            {
                accepted.Add(new TemporaryLink(
                    id,
                    incoming.ArrivalNodeIndex,
                    outgoing.DepartureNodeIndex,
                    CityBusRouteLinkKind.LeftTurn,
                    incoming.To,
                    samples,
                    radius,
                    clearance,
                    false,
                    incoming.RoadEdge));
                return;
            }

            failures.Add(CreateFailure(
                id,
                incoming,
                outgoing,
                CityBusRouteLinkKind.LeftTurn,
                samples,
                radius,
                clearance));
        }

        private static CityBusClearanceFailure CreateFailure(
            string id,
            DirectedStreet incoming,
            DirectedStreet outgoing,
            CityBusRouteLinkKind kind,
            IList<CityBusPathSample> samples,
            float minimumTurnRadius,
            CityBusClearanceResult clearance)
        {
            return new CityBusClearanceFailure(
                id,
                incoming.RoadEdge,
                outgoing.RoadEdge,
                incoming.From,
                incoming.To,
                outgoing.To,
                kind,
                samples,
                minimumTurnRadius,
                clearance);
        }

        private static List<CityBusPathSample> CreateLinearSamples(
            Vector3 start,
            Vector3 end,
            Vector3 forward)
        {
            var result = new List<CityBusPathSample>();
            AppendLinear(result, start, end, forward);
            return result;
        }

        private static List<CityBusPathSample> CreateTurnSamples(
            CityLayout layout,
            DirectedStreet incoming,
            DirectedStreet outgoing,
            float laneCenterOffset,
            bool leftTurn,
            float radius)
        {
            var result = new List<CityBusPathSample>();
            Vector3 junction = layout.GetNodeWorldPosition(incoming.To);
            junction.y += CityStreetSurfacePlanner.RoadTop;
            float halfRoad = layout.RoadWidth * 0.5f;
            float cornerOffset = halfRoad - TurnTangentInset;
            Vector3 start = GetArrivalPosition(layout, incoming);
            Vector3 startTangent = junction -
                (incoming.Forward * cornerOffset) +
                (incoming.Right * laneCenterOffset);
            Vector3 endTangent = junction +
                (outgoing.Forward * cornerOffset) +
                (outgoing.Right * laneCenterOffset);
            Vector3 end = GetDeparturePosition(layout, outgoing);
            AppendLinear(result, start, startTangent, incoming.Forward);

            Vector3 center = leftTurn
                ? startTangent - (incoming.Right * radius)
                : startTangent + (incoming.Right * radius);
            int arcSteps = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (Mathf.PI * 0.5f * Mathf.Max(radius, 0.01f)) /
                    PathSampleSpacing));
            if ((arcSteps & 1) != 0)
            {
                arcSteps++;
            }
            for (int step = 1; step <= arcSteps; step++)
            {
                float t = step / (float)arcSteps;
                float angle = t * Mathf.PI * 0.5f;
                Vector3 offset;
                Vector3 forward;
                if (leftTurn)
                {
                    offset = (incoming.Right * Mathf.Cos(angle) +
                              incoming.Forward * Mathf.Sin(angle)) *
                             radius;
                    forward = incoming.Forward * Mathf.Cos(angle) -
                              incoming.Right * Mathf.Sin(angle);
                }
                else
                {
                    offset = (-incoming.Right * Mathf.Cos(angle) +
                              incoming.Forward * Mathf.Sin(angle)) *
                             radius;
                    forward = incoming.Forward * Mathf.Cos(angle) +
                              incoming.Right * Mathf.Sin(angle);
                }

                AppendPoint(result, center + offset, forward.normalized);
            }

            if (result.Count == 0 ||
                Vector3.Distance(
                    result[result.Count - 1].Position,
                    endTangent) > GeometryTolerance)
            {
                AppendPoint(result, endTangent, outgoing.Forward);
            }

            AppendLinear(result, endTangent, end, outgoing.Forward);
            return result;
        }

        private static void AppendLinear(
            IList<CityBusPathSample> target,
            Vector3 start,
            Vector3 end,
            Vector3 forward)
        {
            if (target.Count == 0)
            {
                target.Add(new CityBusPathSample(start, forward, 0f));
            }
            else if (Vector3.Distance(
                         target[target.Count - 1].Position,
                         start) > GeometryTolerance)
            {
                AppendPoint(target, start, forward);
            }

            float length = Vector3.Distance(start, end);
            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(length / PathSampleSpacing));
            for (int step = 1; step <= steps; step++)
            {
                AppendPoint(
                    target,
                    Vector3.Lerp(start, end, step / (float)steps),
                    forward);
            }
        }

        private static void AppendPoint(
            IList<CityBusPathSample> target,
            Vector3 position,
            Vector3 forward)
        {
            float distance = target.Count == 0
                ? 0f
                : target[target.Count - 1].Distance +
                  Vector3.Distance(
                      target[target.Count - 1].Position,
                      position);
            target.Add(new CityBusPathSample(
                position,
                forward.sqrMagnitude > 0.0001f
                    ? forward.normalized
                    : Vector3.forward,
                distance));
        }

        private static CityBusClearanceResult ValidateSamples(
            CityBusDesignVehicle vehicle,
            IList<CityBusPathSample> samples,
            IList<Rect> allowedRectangles)
        {
            float halfLength = vehicle.InflatedLength * 0.5f;
            float halfWidth = vehicle.InflatedWidth * 0.5f;
            for (int sampleIndex = 0;
                 sampleIndex < samples.Count;
                 sampleIndex++)
            {
                CityBusPathSample sample = samples[sampleIndex];
                Vector3 forward = sample.Forward;
                forward.y = 0f;
                if (forward.sqrMagnitude <= 0.0001f)
                {
                    return new CityBusClearanceResult(
                        false,
                        CityBusClearanceFailureKind.OutsideStreetSurface,
                        sampleIndex,
                        sample.Position,
                        0f);
                }

                forward.Normalize();
                Vector3 right = new Vector3(
                    forward.z,
                    0f,
                    -forward.x);
                for (int forwardSign = -1;
                     forwardSign <= 1;
                     forwardSign++)
                {
                    for (int rightSign = -1;
                         rightSign <= 1;
                         rightSign++)
                    {
                        Vector3 point = sample.Position +
                            (forward * halfLength * forwardSign) +
                            (right * halfWidth * rightSign);
                        if (!ContainsAny(allowedRectangles, point))
                        {
                            return new CityBusClearanceResult(
                                false,
                                CityBusClearanceFailureKind.SidewalkOverlap,
                                sampleIndex,
                                point,
                                0f);
                        }
                    }
                }
            }

            return new CityBusClearanceResult(
                true,
                CityBusClearanceFailureKind.None,
                -1,
                default,
                vehicle.ClearanceMargin);
        }

        private static bool ContainsAny(
            IList<Rect> rectangles,
            Vector3 point)
        {
            Vector2 planar = new Vector2(point.x, point.z);
            for (int index = 0; index < rectangles.Count; index++)
            {
                Rect rect = rectangles[index];
                if (planar.x >= rect.xMin - GeometryTolerance &&
                    planar.x <= rect.xMax + GeometryTolerance &&
                    planar.y >= rect.yMin - GeometryTolerance &&
                    planar.y <= rect.yMax + GeometryTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Rect GetCarriagewayRect(
            CityLayout layout,
            RoadEdge edge)
        {
            Rect road = layout.GetRoadRect(edge);
            float sidewalk = CityStreetSurfacePlanner.SidewalkWidth;
            return edge.IsHorizontal
                ? Rect.MinMaxRect(
                    road.xMin,
                    road.yMin + sidewalk,
                    road.xMax,
                    road.yMax - sidewalk)
                : Rect.MinMaxRect(
                    road.xMin + sidewalk,
                    road.yMin,
                    road.xMax - sidewalk,
                    road.yMax);
        }

        private static Rect GetIntersectionCoreRect(
            CityLayout layout,
            Vector2Int node)
        {
            Vector3 center = layout.GetNodeWorldPosition(node);
            float half = layout.RoadWidth * 0.5f;
            return Rect.MinMaxRect(
                center.x - half,
                center.z - half,
                center.x + half,
                center.z + half);
        }

        private static Rect GetIntersectionApproachRect(
            CityLayout layout,
            Vector2Int node,
            RoadEdge edge)
        {
            Vector3 nodePosition = layout.GetNodeWorldPosition(node);
            Vector3 otherPosition = layout.GetNodeWorldPosition(
                edge.Other(node));
            Vector3 outward = otherPosition - nodePosition;
            outward.y = 0f;
            outward.Normalize();
            float halfRoad = layout.RoadWidth * 0.5f;
            float length = CityStreetSurfacePlanner
                .BusApproachApronLength;
            Vector3 center = nodePosition +
                (outward * (halfRoad + (length * 0.5f)));
            return edge.IsHorizontal
                ? Rect.MinMaxRect(
                    center.x - (length * 0.5f),
                    center.z - halfRoad,
                    center.x + (length * 0.5f),
                    center.z + halfRoad)
                : Rect.MinMaxRect(
                    center.x - halfRoad,
                    center.z - (length * 0.5f),
                    center.x + halfRoad,
                    center.z + (length * 0.5f));
        }

        private static Vector3 GetDeparturePosition(
            CityLayout layout,
            DirectedStreet street)
        {
            Vector3 node = layout.GetNodeWorldPosition(street.From);
            Vector3 result = node +
                (street.Forward * (layout.RoadWidth * 0.5f)) +
                (street.Right * street.LaneCenterOffset);
            result.y = node.y + CityStreetSurfacePlanner.RoadTop;
            return result;
        }

        private static Vector3 GetArrivalPosition(
            CityLayout layout,
            DirectedStreet street)
        {
            Vector3 node = layout.GetNodeWorldPosition(street.To);
            Vector3 result = node -
                (street.Forward * (layout.RoadWidth * 0.5f)) +
                (street.Right * street.LaneCenterOffset);
            result.y = node.y + CityStreetSurfacePlanner.RoadTop;
            return result;
        }

        private static bool IsBusTraversableStreet(
            CityLayout layout,
            RoadEdge edge)
        {
            if (layout.GetPathKind(edge) != CityPathKind.Street)
            {
                return false;
            }

            CityElevationTransitionDescriptor transition =
                layout.ElevationPlan.GetTransition(edge);
            return (transition.Mobility & CityTraversalMobility.Bus) != 0 &&
                   (transition.Kind == CityElevationTransitionKind.Level ||
                    transition.Kind ==
                    CityElevationTransitionKind.VehicleGrade);
        }

        private static List<CityBusSpawnAnchor> CreateSpawnAnchors(
            CityLayout layout,
            CityBusDesignVehicle vehicle,
            IList<RouteLinkMetadata> metadata,
            IReadOnlyList<CityBusRouteLink> links,
            IReadOnlyList<CityBusStopDescriptor> stops)
        {
            var result = new List<CityBusSpawnAnchor>();
            var stopLinks = new HashSet<int>();
            for (int index = 0; index < stops.Count; index++)
            {
                stopLinks.Add(stops[index].LinkIndex);
            }

            float safeEndDistance = vehicle.InflatedLength * 0.5f +
                                    StopJunctionClearance;
            for (int index = 0; index < metadata.Count; index++)
            {
                RouteLinkMetadata entry = metadata[index];
                if (!entry.Source.IsRoadSegment)
                {
                    continue;
                }

                if (layout.IsBusFurnitureExcluded(
                        entry.Source.RoadEdge))
                {
                    continue;
                }

                CityBusRouteLink link = links[entry.FinalLinkIndex];
                if (link.Length <= safeEndDistance * 2f)
                {
                    continue;
                }

                float distance = stopLinks.Contains(entry.FinalLinkIndex)
                    ? safeEndDistance
                    : link.Length * 0.5f;
                EvaluateSamples(
                    link.Samples,
                    distance,
                    out Vector3 position,
                    out Vector3 forward);
                result.Add(new CityBusSpawnAnchor(
                    entry.Source.Id + ":route-01:" +
                    entry.FinalLinkIndex + ":spawn",
                    entry.FinalLinkIndex,
                    distance,
                    position,
                    forward,
                    entry.Source.RoadEdge));
            }

            return result;
        }

        private static Vector2Int GetRightSideCell(
            Vector2Int from,
            Vector2Int to)
        {
            Vector2Int direction = to - from;
            if (direction == Vector2Int.right)
            {
                return new Vector2Int(from.x, from.y - 1);
            }

            if (direction == Vector2Int.left)
            {
                return new Vector2Int(to.x, to.y);
            }

            if (direction == Vector2Int.up)
            {
                return new Vector2Int(from.x, from.y);
            }

            return new Vector2Int(from.x - 1, to.y);
        }

        private static string GetStopSuffix(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.Industrial:
                    return "industrial";
                case CityDistrictKind.Nightlife:
                    return "nightlife";
                case CityDistrictKind.Residential:
                    return "residential";
                case CityDistrictKind.OldTown:
                    return "old-town";
                default:
                    throw new ArgumentOutOfRangeException(nameof(district));
            }
        }

        private static void EvaluateSamples(
            IReadOnlyList<CityBusPathSample> samples,
            float distance,
            out Vector3 position,
            out Vector3 forward)
        {
            if (samples.Count == 0)
            {
                position = default;
                forward = Vector3.forward;
                return;
            }

            float maximum = samples[samples.Count - 1].Distance;
            float clamped = Mathf.Clamp(distance, 0f, maximum);
            for (int index = 1; index < samples.Count; index++)
            {
                CityBusPathSample second = samples[index];
                if (clamped > second.Distance + GeometryTolerance)
                {
                    continue;
                }

                CityBusPathSample first = samples[index - 1];
                float span = second.Distance - first.Distance;
                float t = span > GeometryTolerance
                    ? (clamped - first.Distance) / span
                    : 0f;
                position = Vector3.Lerp(first.Position, second.Position, t);
                forward = Vector3.Lerp(first.Forward, second.Forward, t)
                    .normalized;
                return;
            }

            CityBusPathSample last = samples[samples.Count - 1];
            position = last.Position;
            forward = last.Forward;
        }

        private static string ManeuverId(
            DirectedStreet incoming,
            DirectedStreet outgoing,
            string kind)
        {
            return $"bus:junction:{NodeId(incoming.To)}:{kind}:" +
                   $"{NodeId(incoming.From)}:{NodeId(outgoing.To)}";
        }

        private static string NodeId(Vector2Int node)
        {
            return $"{node.x}:{node.y}";
        }

        private static int CompareNodes(Vector2Int first, Vector2Int second)
        {
            int x = first.x.CompareTo(second.x);
            return x != 0 ? x : first.y.CompareTo(second.y);
        }

        private sealed class DirectedStreet
        {
            public DirectedStreet(
                RoadEdge roadEdge,
                Vector2Int from,
                Vector2Int to,
                CityLayout layout,
                float laneCenterOffset)
            {
                RoadEdge = roadEdge;
                From = from;
                To = to;
                LaneCenterOffset = laneCenterOffset;
                Vector3 first = layout.GetNodeWorldPosition(from);
                Vector3 second = layout.GetNodeWorldPosition(to);
                Vector3 forward = second - first;
                forward.y = 0f;
                forward.Normalize();
                Forward = forward;
                Right = new Vector3(Forward.z, 0f, -Forward.x);
                float horizontalRun = Mathf.Max(
                    0f,
                    new Vector2(
                        second.x - first.x,
                        second.z - first.z).magnitude -
                    layout.RoadWidth);
                Vector3 gradeForward =
                    (Forward * horizontalRun) +
                    (Vector3.up * (second.y - first.y));
                GradeForward = gradeForward.sqrMagnitude > 0.0001f
                    ? gradeForward.normalized
                    : Forward;
            }

            public RoadEdge RoadEdge { get; }
            public Vector2Int From { get; }
            public Vector2Int To { get; }
            public Vector3 Forward { get; }
            public Vector3 GradeForward { get; }
            public Vector3 Right { get; }
            public float LaneCenterOffset { get; }
            public int DepartureNodeIndex { get; set; }
            public int ArrivalNodeIndex { get; set; }
        }

        private readonly struct DirectedKey : IEquatable<DirectedKey>
        {
            public DirectedKey(Vector2Int from, Vector2Int to)
            {
                From = from;
                To = to;
            }

            private Vector2Int From { get; }
            private Vector2Int To { get; }

            public bool Equals(DirectedKey other)
            {
                return From == other.From && To == other.To;
            }

            public override bool Equals(object obj)
            {
                return obj is DirectedKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (From.GetHashCode() * 397) ^ To.GetHashCode();
                }
            }
        }

        private sealed class TemporaryNode
        {
            public TemporaryNode(
                string id,
                Vector3 position,
                Vector3 forward,
                RoadEdge roadEdge,
                Vector2Int fromGridNode,
                Vector2Int toGridNode)
            {
                Id = id;
                Position = position;
                Forward = forward;
                RoadEdge = roadEdge;
                FromGridNode = fromGridNode;
                ToGridNode = toGridNode;
            }

            public string Id { get; }
            public Vector3 Position { get; }
            public Vector3 Forward { get; }
            public RoadEdge RoadEdge { get; }
            public Vector2Int FromGridNode { get; }
            public Vector2Int ToGridNode { get; }
        }

        private sealed class TemporaryLink
        {
            public TemporaryLink(
                string id,
                int fromNodeIndex,
                int toNodeIndex,
                CityBusRouteLinkKind kind,
                Vector2Int junctionNode,
                List<CityBusPathSample> samples,
                float minimumTurnRadius,
                CityBusClearanceResult clearance,
                bool isRoadSegment,
                RoadEdge roadEdge,
                bool occupiesPrimaryRoadEdge = false,
                bool hasSecondaryRoadEdge = false,
                RoadEdge secondaryRoadEdge = default)
            {
                Id = id;
                FromNodeIndex = fromNodeIndex;
                ToNodeIndex = toNodeIndex;
                Kind = kind;
                JunctionNode = junctionNode;
                Samples = samples;
                MinimumTurnRadius = minimumTurnRadius;
                Clearance = clearance;
                IsRoadSegment = isRoadSegment;
                RoadEdge = roadEdge;
                OccupiesPrimaryRoadEdge =
                    isRoadSegment || occupiesPrimaryRoadEdge;
                HasSecondaryRoadEdge = hasSecondaryRoadEdge;
                SecondaryRoadEdge = secondaryRoadEdge;
            }

            public string Id { get; }
            public int FromNodeIndex { get; }
            public int ToNodeIndex { get; }
            public CityBusRouteLinkKind Kind { get; }
            public Vector2Int JunctionNode { get; }
            public List<CityBusPathSample> Samples { get; }
            public float Length => Samples.Count == 0
                ? 0f
                : Samples[Samples.Count - 1].Distance;
            public float MinimumTurnRadius { get; }
            public CityBusClearanceResult Clearance { get; }
            public bool IsRoadSegment { get; }
            public RoadEdge RoadEdge { get; }
            public bool OccupiesPrimaryRoadEdge { get; }
            public bool HasSecondaryRoadEdge { get; }
            public RoadEdge SecondaryRoadEdge { get; }

        }

        private readonly struct RouteLinkMetadata
        {
            public RouteLinkMetadata(
                TemporaryLink source,
                int finalLinkIndex,
                StopCandidate stopCandidate = null)
            {
                Source = source;
                FinalLinkIndex = finalLinkIndex;
                StopCandidate = stopCandidate;
            }

            public TemporaryLink Source { get; }
            public int FinalLinkIndex { get; }
            public StopCandidate StopCandidate { get; }
        }

    }
}
