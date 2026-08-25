using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The first half of the journey: off the last route island, through the
    /// city's own streets, across the tunnel forecourt and into the dark.
    ///
    /// The middle of it is a Dijkstra over <see cref="CityLayout.RoadEdges"/> -
    /// the city's own undirected street grid - and NOT over
    /// <see cref="CityBusPlan"/>, which was the obvious first choice and is
    /// wrong. That plan's `Nodes` and `Links` are Route 01 itself, one closed
    /// right-hand circuit; routing on it meant only ever going the way the bus
    /// goes, and the first measured departure ran `4.8 km` - eighty-four per
    /// cent of the way round a `5.6 km` loop to reach a portal `170 m` away.
    /// The layout's edges have no direction and no timetable.
    ///
    /// The two ends are the pieces no street graph knows about: the run off the
    /// island's own lot, and the forecourt corridor that the fringe yard
    /// planner already keeps clear for exactly this
    /// (<see cref="CityTunnelForecourtDescriptor.DriveClearBounds"/>).
    ///
    /// It never answers "no path". A ride has been promised by the time this
    /// is called - the man has already got off his bonnet - so a seed whose
    /// graph cannot be walked falls back to driving straight at the portal
    /// rather than leaving the hero sitting in a car that will not start.
    /// </summary>
    public static class LastRouteCityDeparturePlanner
    {
        /// <summary>
        /// How far past the portal the car is driven before the screen is
        /// black and the load is asked for.
        ///
        /// Two constraints meet here. `TunnelPhysicalDepth` is `12 m`, so this
        /// has to be past that for the car to be genuinely swallowed; and the
        /// throat starts bending `4°` per `6 m` segment after
        /// `TunnelStraightDepth`, so it has to be near enough to the mouth
        /// that a straight run does not walk into the wall. At `15 m` the bend
        /// has carried the centreline about six centimetres.
        /// </summary>
        public const float TunnelBlackoutDepth = 15f;

        /// <summary>
        /// How far off the carriageway's middle the car drives. A quarter of
        /// the `6 m` between kerbs, which is the bus's own rule
        /// (`CityBusPlanner`: `carriagewayWidth * 0.25`) and puts him on his
        /// own side of the road rather than down the crown of it.
        /// </summary>
        public const float LaneCenterOffsetMeters = 1.5f;

        /// <summary>How long the pull-away off the lot is, before the nose is
        /// pointed at the street.</summary>
        public const float LotExitLeadMeters = 5f;

        /// <summary>Straight runs are cut this fine, so a long approach still
        /// has vertices for the corner rounding to work with.</summary>
        public const float StraightStepMeters = 1.5f;

        /// <summary>
        /// How hard the corners are rounded off. The street grid is square, so
        /// every junction the route turns at is a right angle, and a raw
        /// polyline would ask the car to change heading in a single vertex.
        /// </summary>
        public const float CornerRadiusMeters = 4.5f;

        /// <summary>Below this a vertex is a straight, not a corner.</summary>
        public const float CornerAngleThresholdDegrees = 4f;

        /// <summary>
        /// Steeper than this is not a road. On this terrain nothing should
        /// come close - the whole city has about eight metres of range across
        /// its width - but a router that would happily send a saloon up
        /// something impossible is worth three lines to rule out.
        /// </summary>
        public const float MaximumDrivableGrade = 0.25f;

        public static LastRouteCarDrivePath Create(
            LastRouteCarPlan carPlan,
            CityLayout layout,
            CityTunnelForecourtDescriptor forecourt,
            float tunnelFloorSurfaceY)
        {
            if (carPlan == null || !carPlan.IsPresent)
            {
                throw new ArgumentException(
                    "There is no car to drive away.",
                    nameof(carPlan));
            }

            var points = new List<Vector3> { carPlan.Position };
            Vector3 streetAnchor = forecourt.StreetAnchor;
            if (!TryAppendStreets(points, carPlan, layout, streetAnchor))
            {
                // No layout, or nothing on it reachable. He still leaves.
                GameLog.Warning("lastroute", "departure_streets_unavailable");
                AppendStraight(points, streetAnchor);
            }

            AppendStraight(points, forecourt.PortalAnchor);

            Vector3 axis = forecourt.Axis.normalized;
            Vector3 mouth = forecourt.PortalAnchor;
            mouth.y = tunnelFloorSurfaceY;
            AppendStraight(points, mouth);
            AppendStraight(points, mouth + (axis * TunnelBlackoutDepth));

            // Rounded first, then cut fine. The other way round would cap
            // every corner's cut at half a short segment and leave the arcs
            // barely bent, because a junction on this grid is a right angle
            // with twenty-five metres of straight either side of it.
            return new LastRouteCarDrivePath(
                Subdivide(RoundCorners(points)));
        }

        /// <summary>
        /// The whole street middle: joins the grid at the junction nearest the
        /// lot, walks it to the junction nearest the forecourt's street
        /// anchor, and lays the result out in the right-hand lane.
        /// </summary>
        private static bool TryAppendStreets(
            List<Vector3> points,
            LastRouteCarPlan carPlan,
            CityLayout layout,
            Vector3 streetAnchor)
        {
            if (layout == null)
            {
                return false;
            }

            Dictionary<Vector2Int, List<Vector2Int>> graph =
                BuildStreetGraph(layout);
            if (graph.Count == 0)
            {
                return false;
            }

            // The car is parked nose out at a way in, so the street it wants
            // is the one that way rather than the nearest one behind it.
            Vector3 probe = carPlan.Position +
                            (carPlan.Facing * LotExitLeadMeters);
            if (!TryFindNearestNode(layout, graph, probe, out Vector2Int from) ||
                !TryFindNearestNode(
                    layout,
                    graph,
                    streetAnchor,
                    out Vector2Int to))
            {
                return false;
            }

            if (!TryFindRoute(
                    layout,
                    graph,
                    from,
                    to,
                    out List<Vector2Int> route))
            {
                return false;
            }

            IReadOnlyList<Vector3> lane = BuildLane(layout, route);
            if (lane.Count == 0)
            {
                return false;
            }

            // Pulling away off the lot: a short bend from where it is parked
            // onto the road, rather than a corner at the kerb.
            AppendLotExit(points, carPlan, lane[0]);
            for (int index = 0; index < lane.Count; index++)
            {
                Append(points, lane[index]);
            }

            AppendStraight(points, streetAnchor);
            return true;
        }

        /// <summary>
        /// The city's drivable streets as an undirected adjacency map. Park
        /// paths are left out - they are gravel between benches - and so is
        /// anything too steep to be a road.
        /// </summary>
        private static Dictionary<Vector2Int, List<Vector2Int>>
            BuildStreetGraph(CityLayout layout)
        {
            var graph = new Dictionary<Vector2Int, List<Vector2Int>>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 a = layout.GetNodeWorldPosition(edge.A);
                Vector3 b = layout.GetNodeWorldPosition(edge.B);
                float run = Vector3.ProjectOnPlane(b - a, Vector3.up).magnitude;
                if (run > 0.01f &&
                    Mathf.Abs(b.y - a.y) / run > MaximumDrivableGrade)
                {
                    continue;
                }

                Link(graph, edge.A, edge.B);
                Link(graph, edge.B, edge.A);
            }

            return graph;
        }

        private static void Link(
            Dictionary<Vector2Int, List<Vector2Int>> graph,
            Vector2Int from,
            Vector2Int to)
        {
            if (!graph.TryGetValue(from, out List<Vector2Int> neighbours))
            {
                neighbours = new List<Vector2Int>(4);
                graph[from] = neighbours;
            }

            neighbours.Add(to);
        }

        /// <summary>
        /// Dijkstra over the street grid, weighted by the real world distance
        /// between junctions so the river valley's climbs cost what they
        /// actually cost. A linear scan for the cheapest open node is plenty
        /// here - the default city is a few hundred junctions - and it is much
        /// easier to read than a heap.
        /// </summary>
        private static bool TryFindRoute(
            CityLayout layout,
            Dictionary<Vector2Int, List<Vector2Int>> graph,
            Vector2Int from,
            Vector2Int to,
            out List<Vector2Int> route)
        {
            route = new List<Vector2Int> { from };
            if (from == to)
            {
                return true;
            }

            var best = new Dictionary<Vector2Int, float> { [from] = 0f };
            var arrivedFrom = new Dictionary<Vector2Int, Vector2Int>();
            var settled = new HashSet<Vector2Int>();
            while (true)
            {
                Vector2Int current = default;
                float cheapest = float.PositiveInfinity;
                bool found = false;
                foreach (KeyValuePair<Vector2Int, float> open in best)
                {
                    if (settled.Contains(open.Key) || open.Value >= cheapest)
                    {
                        continue;
                    }

                    cheapest = open.Value;
                    current = open.Key;
                    found = true;
                }

                if (!found)
                {
                    return false;
                }

                if (current == to)
                {
                    break;
                }

                settled.Add(current);
                if (!graph.TryGetValue(
                        current,
                        out List<Vector2Int> neighbours))
                {
                    continue;
                }

                Vector3 here = layout.GetNodeWorldPosition(current);
                for (int index = 0; index < neighbours.Count; index++)
                {
                    Vector2Int next = neighbours[index];
                    float candidate = cheapest + Vector3.Distance(
                        here,
                        layout.GetNodeWorldPosition(next));
                    if (best.TryGetValue(next, out float existing) &&
                        candidate >= existing)
                    {
                        continue;
                    }

                    best[next] = candidate;
                    arrivedFrom[next] = current;
                }
            }

            var reversed = new List<Vector2Int> { to };
            Vector2Int walk = to;
            while (walk != from)
            {
                if (!arrivedFrom.TryGetValue(walk, out Vector2Int previous))
                {
                    return false;
                }

                walk = previous;
                reversed.Add(walk);
            }

            reversed.Reverse();
            route = reversed;
            return true;
        }

        /// <summary>
        /// The junction centres carried over into the right-hand lane.
        ///
        /// Offsetting each segment on its own would leave the two halves of
        /// every corner not meeting, so each vertex is pushed along the
        /// bisector of its two segments by the miter length - which for the
        /// square corners this grid is made of works out at about one and a
        /// half lane offsets. The miter is capped so a route that doubles back
        /// on itself never throws a vertex across the city.
        /// </summary>
        private static IReadOnlyList<Vector3> BuildLane(
            CityLayout layout,
            IReadOnlyList<Vector2Int> route)
        {
            var centres = new List<Vector3>(route.Count);
            for (int index = 0; index < route.Count; index++)
            {
                centres.Add(layout.GetNodeWorldPosition(route[index]));
            }

            var lane = new List<Vector3>(centres.Count);
            for (int index = 0; index < centres.Count; index++)
            {
                Vector3 incoming = index > 0
                    ? Flatten(centres[index] - centres[index - 1])
                    : Vector3.zero;
                Vector3 outgoing = index < centres.Count - 1
                    ? Flatten(centres[index + 1] - centres[index])
                    : Vector3.zero;
                if (incoming.sqrMagnitude < 0.000001f)
                {
                    incoming = outgoing;
                }

                if (outgoing.sqrMagnitude < 0.000001f)
                {
                    outgoing = incoming;
                }

                if (incoming.sqrMagnitude < 0.000001f)
                {
                    lane.Add(centres[index]);
                    continue;
                }

                Vector3 rightIn = Vector3.Cross(
                    Vector3.up,
                    incoming.normalized);
                Vector3 rightOut = Vector3.Cross(
                    Vector3.up,
                    outgoing.normalized);
                Vector3 bisector = rightIn + rightOut;
                if (bisector.sqrMagnitude < 0.000001f)
                {
                    bisector = rightIn;
                }

                bisector = bisector.normalized;
                float scale = LaneCenterOffsetMeters /
                              Mathf.Max(0.35f, Vector3.Dot(bisector, rightIn));
                lane.Add(centres[index] + (bisector * scale));
            }

            return lane;
        }

        private static bool TryFindNearestNode(
            CityLayout layout,
            Dictionary<Vector2Int, List<Vector2Int>> graph,
            Vector3 target,
            out Vector2Int nearest)
        {
            nearest = default;
            float best = float.PositiveInfinity;
            bool found = false;
            foreach (Vector2Int node in graph.Keys)
            {
                float distance = PlanarDistanceSquared(
                    layout.GetNodeWorldPosition(node),
                    target);
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                nearest = node;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// The pull-away. A quadratic through a control point out along the
        /// car's own nose, so it leaves the way it is pointing and arrives on
        /// the road pointing down it, instead of sliding sideways off the lot.
        /// </summary>
        private static void AppendLotExit(
            List<Vector3> points,
            LastRouteCarPlan carPlan,
            Vector3 join)
        {
            Vector3 start = carPlan.Position;
            float reach = Mathf.Min(
                LotExitLeadMeters,
                Vector3.Distance(start, join) * 0.5f);
            Vector3 control = start + (carPlan.Facing * reach);
            const int divisions = 10;
            for (int index = 1; index <= divisions; index++)
            {
                float t = index / (float)divisions;
                float inverse = 1f - t;
                Append(
                    points,
                    (inverse * inverse * start) +
                    (2f * inverse * t * control) +
                    (t * t * join));
            }
        }

        private static void AppendStraight(List<Vector3> points, Vector3 to)
        {
            Vector3 from = points[points.Count - 1];
            float span = Vector3.Distance(from, to);
            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(span / StraightStepMeters));
            for (int index = 1; index <= steps; index++)
            {
                Append(points, Vector3.Lerp(from, to, index / (float)steps));
            }
        }

        private static void Append(List<Vector3> points, Vector3 point)
        {
            if (points.Count > 0 &&
                Vector3.Distance(points[points.Count - 1], point) <
                LastRouteCarDrivePath.MinimumSegmentLength)
            {
                return;
            }

            points.Add(point);
        }

        /// <summary>
        /// Cuts every genuine corner into a small arc. Endpoints are never
        /// moved - the first point is where the car is actually parked, and
        /// the last is the point the blackout is measured at.
        /// </summary>
        private static List<Vector3> RoundCorners(List<Vector3> points)
        {
            if (points.Count < 3)
            {
                return points;
            }

            var rounded = new List<Vector3> { points[0] };
            for (int index = 1; index < points.Count - 1; index++)
            {
                Vector3 previous = points[index - 1];
                Vector3 corner = points[index];
                Vector3 next = points[index + 1];
                Vector3 incoming = corner - previous;
                Vector3 outgoing = next - corner;
                if (incoming.sqrMagnitude < 0.000001f ||
                    outgoing.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                if (Vector3.Angle(incoming, outgoing) <
                    CornerAngleThresholdDegrees)
                {
                    Append(rounded, corner);
                    continue;
                }

                float cut = Mathf.Min(
                    CornerRadiusMeters,
                    incoming.magnitude * 0.5f,
                    outgoing.magnitude * 0.5f);
                Vector3 entry = corner - (incoming.normalized * cut);
                Vector3 exit = corner + (outgoing.normalized * cut);
                Append(rounded, entry);
                const int divisions = 8;
                for (int step = 1; step < divisions; step++)
                {
                    float t = step / (float)divisions;
                    float inverse = 1f - t;
                    Append(
                        rounded,
                        (inverse * inverse * entry) +
                        (2f * inverse * t * corner) +
                        (t * t * exit));
                }

                Append(rounded, exit);
            }

            Append(rounded, points[points.Count - 1]);
            return rounded;
        }

        /// <summary>
        /// Cuts every long straight into steps.
        ///
        /// A lane laid out junction to junction carries one vertex every
        /// twenty-five metres, and the drive model reads the road ahead by
        /// walking VERTICES - so a coarse straight is a road it can barely
        /// see. It also leaves the steering look-ahead interpolating across
        /// half a block. The mountain route arrives sampled at a metre; this
        /// brings the city up to the same footing.
        /// </summary>
        private static List<Vector3> Subdivide(List<Vector3> points)
        {
            if (points.Count < 2)
            {
                return points;
            }

            var fine = new List<Vector3> { points[0] };
            for (int index = 1; index < points.Count; index++)
            {
                Vector3 from = points[index - 1];
                Vector3 to = points[index];
                int steps = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Vector3.Distance(from, to) / StraightStepMeters));
                for (int step = 1; step <= steps; step++)
                {
                    Append(fine, Vector3.Lerp(from, to, step / (float)steps));
                }
            }

            return fine;
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        private static float PlanarDistanceSquared(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z);
        }
    }
}
