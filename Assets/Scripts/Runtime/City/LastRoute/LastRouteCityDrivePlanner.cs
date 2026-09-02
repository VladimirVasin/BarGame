using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The city's half of the journey, in both directions: off the last route
    /// island, through the city's own streets, across the tunnel forecourt and
    /// into the dark - and, on the way home, exactly that road read backwards.
    ///
    /// The return is not a second road. It is this one with the lane put on
    /// the other side of the crown and the points handed over end for end,
    /// which is the whole of what changes when a car drives a street the other
    /// way: same junctions, same turning, other half of the carriageway. See
    /// <see cref="CreateReturn"/> for the one thing that is genuinely
    /// different, which is where it stops.
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
    public static class LastRouteCityDrivePlanner
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
        ///
        /// It is applied to the RIGHT of the direction the points are laid in,
        /// so the return leg - which is laid outbound and then handed over end
        /// for end - has to negate it. Reversing a right-hand lane without
        /// negating it is a car driving home down the oncoming side.
        /// </summary>
        public const float LaneCenterOffsetMeters = 1.5f;

        /// <summary>How long the pull-away off the lot is, before the nose is
        /// pointed at the street.</summary>
        public const float LotExitLeadMeters = 5f;

        /// <summary>
        /// How fine the finished road is cut, once every corner in it has
        /// been rounded. The drive model reads the road ahead by walking
        /// VERTICES, so a road sampled block by block is one it can barely
        /// see; this brings the city up to the metre the mountain route
        /// already arrives at.
        /// </summary>
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
        /// How far back from the turn the car stops when it has to give way.
        ///
        /// Measured to the car's MIDDLE, which is what the drive model moves,
        /// so it is the corner's own cut plus about half a car: the nose then
        /// sits where the turn begins, square in its own lane, and nothing of
        /// it is over the crown of the road while it waits.
        /// </summary>
        public const float GiveWayStandoffMeters = CornerRadiusMeters + 2f;

        /// <summary>
        /// Steeper than this is not a road. On this terrain nothing should
        /// come close - the whole city has about eight metres of range across
        /// its width - but a router that would happily send a saloon up
        /// something impossible is worth three lines to rule out.
        /// </summary>
        public const float MaximumDrivableGrade = 0.25f;

        /// <summary>
        /// The way out: the island, the streets, the forecourt, the dark.
        /// </summary>
        public static LastRouteCarDrivePath CreateDeparture(
            LastRouteCarPlan carPlan,
            CityLayout layout,
            CityTunnelForecourtDescriptor forecourt,
            float tunnelFloorSurfaceY)
        {
            return Create(
                carPlan,
                layout,
                forecourt,
                tunnelFloorSurfaceY,
                true);
        }

        /// <summary>
        /// The way home: out of the same portal, back across the forecourt and
        /// down the same streets to the island he left from.
        ///
        /// <paramref name="carPlan"/> is still the ISLAND's stance rather than
        /// wherever the car happens to be sitting when this is called, because
        /// what it names here is the destination: the road is laid outbound
        /// from the bay exactly as the departure lays it, and then turned
        /// round.
        ///
        /// **It ends nose-in, and that is a deliberate answer rather than an
        /// oversight.** The bay is a slot on open paving whose nose points at
        /// the way in; a car can only be put back into it pointing that way by
        /// reversing in from behind it, and behind it is the island's own
        /// paving circle and its route mast. So the car comes in off the
        /// street the way it went out and stops in its own place turned round,
        /// which is what the bay's clearance test measures anyway - the box it
        /// checks is the same box either way about. The canonical stance comes
        /// back with the next city build, because the city always raises him
        /// from the layout rather than from where he was left.
        /// </summary>
        public static LastRouteCarDrivePath CreateReturn(
            LastRouteCarPlan carPlan,
            CityLayout layout,
            CityTunnelForecourtDescriptor forecourt,
            float tunnelFloorSurfaceY)
        {
            return Create(
                carPlan,
                layout,
                forecourt,
                tunnelFloorSurfaceY,
                false);
        }

        /// <summary>
        /// Where the car is standing the moment the City finishes loading a
        /// homecoming - inside its own south portal, pointing out of it.
        ///
        /// It is the departure's last point read back, which is the whole
        /// contract between the two halves: the car goes into the dark at one
        /// place and comes out of it at the same one, so nothing can drift
        /// between them. <see cref="LastRouteMountainDrivePlanner"/> keeps the
        /// same promise at the other end.
        /// </summary>
        public static void ResolveReturnEntryPose(
            CityTunnelForecourtDescriptor forecourt,
            float tunnelFloorSurfaceY,
            out Vector3 position,
            out Vector3 facing)
        {
            Vector3 axis = forecourt.Axis.normalized;
            Vector3 mouth = forecourt.PortalAnchor;
            mouth.y = tunnelFloorSurfaceY;
            position = mouth + (axis * TunnelBlackoutDepth);
            facing = -axis;
        }

        private static LastRouteCarDrivePath Create(
            LastRouteCarPlan carPlan,
            CityLayout layout,
            CityTunnelForecourtDescriptor forecourt,
            float tunnelFloorSurfaceY,
            bool outbound)
        {
            if (carPlan == null || !carPlan.IsPresent)
            {
                throw new ArgumentException(
                    "There is no car to drive away.",
                    nameof(carPlan));
            }

            float laneSign = outbound ? 1f : -1f;
            var points = new List<Vector3> { carPlan.Position };
            Vector3 streetAnchor = forecourt.StreetAnchor;
            bool hasTurn = TryAppendStreets(
                points,
                carPlan,
                layout,
                streetAnchor,
                laneSign,
                out Vector3 turnFrom,
                out Vector3 laneDirection,
                out float laneRun);
            if (!hasTurn)
            {
                // No layout, or nothing on it reachable. He still leaves.
                GameLog.Warning("lastroute", "departure_streets_unavailable");
            }

            Append(points, streetAnchor);

            // The portal is ONE vertex, carrying the tunnel floor's own
            // height, and not the forecourt ground followed by a step up onto
            // that floor.
            //
            // The two differ by the `3 cm` throat lift and by nothing else -
            // same X, same Z - so a pair of them is a segment pointing
            // straight up. `BuildVertexForwards` averages the segments meeting
            // at a vertex, which turns a riser of any height into a forward
            // pitched forty-five degrees, and `Sample` then slerps the car
            // into and out of it across whatever its neighbours are. That was
            // survivable while the neighbours were the centimetre-and-a-half
            // ends of a rounded corner; once every leg reaches the rounder at
            // full length it is a metre and a half either side, and the car
            // rears up in the hero's face at the mouth. The lift now rides the
            // approach as a `0.2%` grade instead.
            Vector3 axis = forecourt.Axis.normalized;
            Vector3 mouth = forecourt.PortalAnchor;
            mouth.y = tunnelFloorSurfaceY;
            Append(points, mouth);
            Append(points, mouth + (axis * TunnelBlackoutDepth));

            // End for end. Everything above is laid in the outbound order
            // because that is the order the two ends are known in - the lot
            // exit needs the car's own nose, the forecourt run needs the
            // portal - and none of it is direction-dependent once the lane has
            // been put on the correct side of the crown.
            if (!outbound)
            {
                points.Reverse();
            }

            // Straightened, then rounded, then cut fine, and the order is the
            // whole reason the turn into the forecourt reads as a turn.
            //
            // A corner's cut is capped at half of the shorter leg meeting it,
            // so a corner can only be rounded as hard as its neighbours are
            // long. Every leg therefore has to reach `RoundCorners` at its
            // FULL length - which is why nothing before this point subdivides
            // anything, and why the collinear vertices the forecourt run
            // carries are dropped first. The fine sampling the drive model
            // reads the road by is put back at the end, on a road that has
            // already been bent.
            var road = new LastRouteCarDrivePath(
                Subdivide(RoundCorners(Straighten(points))));
            if (!hasTurn)
            {
                return road;
            }

            if (outbound)
            {
                road.DeclareGiveWay(
                    ResolveGiveWay(
                        road,
                        turnFrom,
                        laneDirection,
                        laneRun,
                        streetAnchor));
                return road;
            }

            // Homebound the same junction is taken the other way: he is the
            // one coming OUT of the forecourt, so he waits on the forecourt's
            // own run rather than in the street's lane, and the crossing he is
            // waiting to take is the same segment read the other way. The
            // stand-off is measured back up the run he is on, and never
            // further than that run is long, for the reason
            // <see cref="ResolveGiveWay"/> gives.
            Vector3 approach = Flatten(turnFrom - streetAnchor);
            float approachRun = approach.magnitude;
            if (approachRun < 0.001f)
            {
                return road;
            }

            Vector3 stopLine = turnFrom -
                               (approach / approachRun *
                                Mathf.Min(GiveWayStandoffMeters, approachRun));
            road.DeclareGiveWay(
                new LastRouteCarGiveWayPoint(
                    road.FindNearestDistance(stopLine),
                    streetAnchor,
                    turnFrom));
            return road;
        }

        /// <summary>
        /// Where the car stops if it has to let something past, and what it
        /// is letting past.
        ///
        /// The stop line is measured back up the LANE from the turn, not back
        /// along the finished road, because the finished road has an arc
        /// where the turn is and a point on an arc is already committed to
        /// it. Backing up the straight the arc leaves from puts the line on
        /// the one stretch where a stopped car is still just a car in a lane.
        ///
        /// And never further back than that lane is long. On a short enough
        /// block the standoff would walk the line off the end of it, and
        /// <see cref="LastRouteCarDrivePath.FindNearestDistance"/> would then
        /// answer with whatever part of the road happened to pass nearest -
        /// which on a grid is a street at right angles to this one.
        /// </summary>
        private static LastRouteCarGiveWayPoint ResolveGiveWay(
            LastRouteCarDrivePath road,
            Vector3 turnFrom,
            Vector3 laneDirection,
            float laneRun,
            Vector3 streetAnchor)
        {
            Vector3 stopLine = turnFrom -
                               (laneDirection *
                                Mathf.Min(GiveWayStandoffMeters, laneRun));
            return new LastRouteCarGiveWayPoint(
                road.FindNearestDistance(stopLine),
                turnFrom,
                streetAnchor);
        }

        /// <summary>
        /// The whole street middle: joins the grid at the junction nearest the
        /// lot, walks it to the block the forecourt opens onto, and lays the
        /// result out in the right-hand lane as far as the turning itself.
        ///
        /// It stops AT THE TURNING and not at a junction, and that is the
        /// difference between a car that turns off where the tunnel is and
        /// one that drives past it. The forecourt opening sits halfway along
        /// its block - `CitySurfacePlan` puts an access centre at the middle
        /// of its frontage edge - so both ends of that block are the same
        /// distance from it, to the centimetre. Routing to "the nearest
        /// junction" was therefore a coin toss between them, it came down on
        /// the far one, and the car drove thirteen metres past its own
        /// turning and swung back through a hundred and thirty-five degrees
        /// to reach it.
        /// </summary>
        private static bool TryAppendStreets(
            List<Vector3> points,
            LastRouteCarPlan carPlan,
            CityLayout layout,
            Vector3 streetAnchor,
            float laneSign,
            out Vector3 turnFrom,
            out Vector3 laneDirection,
            out float laneRun)
        {
            turnFrom = streetAnchor;
            laneDirection = Vector3.forward;
            laneRun = 0f;
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
            if (!TryFindNearestNode(layout, graph, probe, out Vector2Int from))
            {
                return false;
            }

            if (!TryFindTurnOff(
                    layout,
                    streetAnchor,
                    out Vector2Int firstEnd,
                    out Vector2Int secondEnd,
                    out Vector3 foot))
            {
                return false;
            }

            if (!TryRouteToBlock(
                    layout,
                    graph,
                    from,
                    firstEnd,
                    secondEnd,
                    foot,
                    out List<Vector2Int> route,
                    out Vector2Int entry,
                    out Vector2Int exit))
            {
                return false;
            }

            laneDirection = Flatten(
                layout.GetNodeWorldPosition(exit) -
                layout.GetNodeWorldPosition(entry));
            if (laneDirection.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            laneDirection = laneDirection.normalized;

            // The far end of the block is carried into the lane solve and
            // then dropped. It is there only so the junction the car turns AT
            // is mitered as a corner between two streets rather than as the
            // end of the road; the car never reaches it.
            var laneRoute = new List<Vector2Int>(route.Count + 1);
            laneRoute.AddRange(route);
            laneRoute.Add(exit);
            IReadOnlyList<Vector3> lane = BuildLane(layout, laneRoute, laneSign);
            if (lane.Count < 2)
            {
                return false;
            }

            turnFrom = foot +
                       (Vector3.Cross(Vector3.up, laneDirection) *
                        (LaneCenterOffsetMeters * laneSign));

            // How much lane there is behind the turning, which is all the
            // room a car waiting to take it has.
            laneRun = Vector3.Distance(
                Flatten(lane[lane.Count - 2]),
                Flatten(turnFrom));

            // Pulling away off the lot: a short bend from where it is parked
            // onto the road, rather than a corner at the kerb.
            AppendLotExit(points, carPlan, lane[0]);
            for (int index = 0; index < lane.Count - 1; index++)
            {
                Append(points, lane[index]);
            }

            Append(points, turnFrom);
            return true;
        }

        /// <summary>
        /// The block of street the forecourt opens onto, and the point on its
        /// middle square with that opening.
        ///
        /// Nearest SEGMENT rather than nearest node, because that is the
        /// question actually being asked: which stretch of road does this
        /// driveway come off. The foot is then held back from both ends by a
        /// corner radius so that the turn out of the block and the turn into
        /// the forecourt are two corners with room between them instead of
        /// one unroundable kink - and on a block too short to hold both, the
        /// middle is the least bad place to put it.
        /// </summary>
        private static bool TryFindTurnOff(
            CityLayout layout,
            Vector3 streetAnchor,
            out Vector2Int first,
            out Vector2Int second,
            out Vector3 foot)
        {
            first = default;
            second = default;
            foot = streetAnchor;
            float best = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (!IsDrivable(layout, edge))
                {
                    continue;
                }

                Vector3 a = layout.GetNodeWorldPosition(edge.A);
                Vector3 b = layout.GetNodeWorldPosition(edge.B);
                Vector3 candidate = ClosestPointOnSegment(a, b, streetAnchor);
                float distance = PlanarDistanceSquared(candidate, streetAnchor);
                if (distance >= best)
                {
                    continue;
                }

                best = distance;
                first = edge.A;
                second = edge.B;
                foot = candidate;
                found = true;
            }

            if (!found)
            {
                return false;
            }

            Vector3 start = layout.GetNodeWorldPosition(first);
            Vector3 end = layout.GetNodeWorldPosition(second);
            float block = Vector3.Distance(
                Flatten(start),
                Flatten(end));
            float along = Vector3.Distance(Flatten(start), Flatten(foot));
            float held = block > CornerRadiusMeters * 2f
                ? Mathf.Clamp(
                    along,
                    CornerRadiusMeters,
                    block - CornerRadiusMeters)
                : block * 0.5f;
            foot = Vector3.Lerp(
                start,
                end,
                block > 0.001f ? held / block : 0.5f);
            return true;
        }

        /// <summary>
        /// Which end of the turning's own block the car arrives at, and the
        /// route there.
        ///
        /// Both ends are costed, and the cost includes the run back along the
        /// block to the turning - otherwise the far end wins on a technicality
        /// whenever the route to it happens to be shorter by less than the
        /// block. Which end wins also decides which LANE the car is in when it
        /// gets there, which is the whole reason the turn is a give-way at
        /// all: arriving from one side it crosses the oncoming carriageway,
        /// and from the other it does not.
        /// </summary>
        private static bool TryRouteToBlock(
            CityLayout layout,
            Dictionary<Vector2Int, List<Vector2Int>> graph,
            Vector2Int from,
            Vector2Int first,
            Vector2Int second,
            Vector3 foot,
            out List<Vector2Int> route,
            out Vector2Int entry,
            out Vector2Int exit)
        {
            bool hasFirst = TryFindRoute(
                layout,
                graph,
                from,
                first,
                out List<Vector2Int> toFirst);
            bool hasSecond = TryFindRoute(
                layout,
                graph,
                from,
                second,
                out List<Vector2Int> toSecond);
            float firstCost = hasFirst
                ? MeasureRoute(layout, toFirst) +
                  Vector3.Distance(
                      Flatten(layout.GetNodeWorldPosition(first)),
                      Flatten(foot))
                : float.PositiveInfinity;
            float secondCost = hasSecond
                ? MeasureRoute(layout, toSecond) +
                  Vector3.Distance(
                      Flatten(layout.GetNodeWorldPosition(second)),
                      Flatten(foot))
                : float.PositiveInfinity;
            if (float.IsPositiveInfinity(firstCost) &&
                float.IsPositiveInfinity(secondCost))
            {
                route = null;
                entry = default;
                exit = default;
                return false;
            }

            if (firstCost <= secondCost)
            {
                route = toFirst;
                entry = first;
                exit = second;
            }
            else
            {
                route = toSecond;
                entry = second;
                exit = first;
            }

            return route != null && route.Count > 0;
        }

        private static float MeasureRoute(
            CityLayout layout,
            IReadOnlyList<Vector2Int> route)
        {
            float length = 0f;
            for (int index = 1; index < route.Count; index++)
            {
                length += Vector3.Distance(
                    layout.GetNodeWorldPosition(route[index - 1]),
                    layout.GetNodeWorldPosition(route[index]));
            }

            return length;
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
                if (!IsDrivable(layout, edge))
                {
                    continue;
                }

                Link(graph, edge.A, edge.B);
                Link(graph, edge.B, edge.A);
            }

            return graph;
        }

        /// <summary>
        /// A street a saloon could take. Park paths are gravel between
        /// benches, and nothing this steep is a road.
        /// </summary>
        private static bool IsDrivable(CityLayout layout, RoadEdge edge)
        {
            if (layout.GetPathKind(edge) != CityPathKind.Street)
            {
                return false;
            }

            Vector3 a = layout.GetNodeWorldPosition(edge.A);
            Vector3 b = layout.GetNodeWorldPosition(edge.B);
            float run = Vector3.ProjectOnPlane(b - a, Vector3.up).magnitude;
            return run <= 0.01f ||
                   Mathf.Abs(b.y - a.y) / run <= MaximumDrivableGrade;
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
            IReadOnlyList<Vector2Int> route,
            float laneSign)
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
                lane.Add(centres[index] + (bisector * (scale * laneSign)));
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
        /// Drops every vertex that is not a corner.
        ///
        /// A corner can only be cut as deep as half its shorter leg, so a
        /// vertex sitting in the middle of a straight is not free: it halves
        /// what the corners either side of it are allowed to be. The forecourt
        /// run carries three such - the street anchor and the two the tunnel
        /// mouth's own floor height puts in - all of them dead in line with
        /// the axis, and between them they held the turn into the forecourt
        /// to a cut of under three metres.
        ///
        /// Endpoints are never dropped, and neither is a vertex whose
        /// neighbours are directly above or below it: those are the tunnel
        /// floor step, they turn the car through nothing, and flattening them
        /// leaves no direction to judge.
        /// </summary>
        private static List<Vector3> Straighten(List<Vector3> points)
        {
            if (points.Count < 3)
            {
                return points;
            }

            var kept = new List<Vector3> { points[0] };
            for (int index = 1; index < points.Count - 1; index++)
            {
                Vector3 incoming = Flatten(points[index] - kept[kept.Count - 1]);
                Vector3 outgoing = Flatten(points[index + 1] - points[index]);
                if (incoming.sqrMagnitude < 0.000001f ||
                    outgoing.sqrMagnitude < 0.000001f ||
                    Vector3.Angle(incoming, outgoing) >=
                    CornerAngleThresholdDegrees)
                {
                    Append(kept, points[index]);
                }
            }

            Append(kept, points[points.Count - 1]);
            return kept;
        }

        /// <summary>
        /// Cuts every genuine corner into a small arc. Endpoints are never
        /// moved - the first point is where the car is actually parked, and
        /// the last is the point the blackout is measured at.
        ///
        /// The turn is measured on the GROUND PLANE, matching
        /// <see cref="LastRouteCarDrivePath"/>'s own curvature: what a corner
        /// costs the car is how hard the road turns, not how steeply it
        /// climbs, and the run into the tunnel steps down onto the floor
        /// without turning at all.
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
                if (Flatten(incoming).sqrMagnitude < 0.000001f ||
                    Flatten(outgoing).sqrMagnitude < 0.000001f)
                {
                    Append(rounded, corner);
                    continue;
                }

                if (Vector3.Angle(Flatten(incoming), Flatten(outgoing)) <
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

        /// <summary>
        /// The point on a street segment nearest a place beside it, chosen on
        /// the ground plane and carrying the street's own height at that
        /// point rather than the caller's.
        /// </summary>
        private static Vector3 ClosestPointOnSegment(
            Vector3 from,
            Vector3 to,
            Vector3 point)
        {
            Vector3 run = Flatten(to - from);
            float lengthSquared = run.sqrMagnitude;
            if (lengthSquared < 0.000001f)
            {
                return from;
            }

            float t = Mathf.Clamp01(
                Vector3.Dot(Flatten(point - from), run) / lengthSquared);
            return Vector3.Lerp(from, to, t);
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
