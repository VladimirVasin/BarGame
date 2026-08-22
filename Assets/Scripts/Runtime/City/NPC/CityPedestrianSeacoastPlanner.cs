using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static partial class CityPedestrianPlanner
    {
        /// <summary>
        /// The shore promenade: one east-west lane along the esplanade
        /// axis, from the waterfront's street access to the wild east
        /// shore, crossing the mouth on the coast's own footbridge.
        ///
        /// Every piece of it is anchored so the two-core prune keeps
        /// the whole walk: the west end hangs off the boundary-street
        /// sidewalk through the access opening, both river promenades'
        /// north stubs — dead ends the prune used to swallow — hook
        /// into it down the new quay stairs, and the east end closes
        /// into its own small ring around the driftwood. The result is
        /// a grand loop (street, esplanade, footbridge, wild shore,
        /// promenade, mouth bridge, street) with no node under degree
        /// two by construction.
        ///
        /// The seacoast plan is recomputed here rather than passed in:
        /// it is a pure function of the layout, so the lane and the
        /// decks it walks cannot disagree.
        /// </summary>
        private static void BuildSeacoastPaths(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, List<LaneEndpoint>>
                endpointsByNode,
            GraphBuilder graph)
        {
            CitySeacoastPlan seacoast = CitySeacoastPlanner.Create(
                layout);
            if (seacoast == null)
            {
                return;
            }

            if (!seacoast.TryGetPart(
                    CitySeacoastPlanner.FootbridgeDeckWestId,
                    out CitySeacoastPartDescriptor bridgeWest) ||
                !seacoast.TryGetPart(
                    CitySeacoastPlanner.FootbridgeDeckEastId,
                    out CitySeacoastPartDescriptor bridgeEast) ||
                !seacoast.TryGetPart(
                    CitySeacoastPlanner.PierDeckRootId,
                    out CitySeacoastPartDescriptor pierRoot))
            {
                return;
            }

            CitySeacoastFrame frame = seacoast.Frame;
            CityOpenAreaAccessDescriptor access = default;
            bool hasAccess = false;
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                if (layout.OpenAreaAccesses[index].Feature ==
                    CityAreaFeatureKind.NorthWaterfront)
                {
                    access = layout.OpenAreaAccesses[index];
                    hasAccess = true;
                    break;
                }
            }

            if (!hasAccess)
            {
                return;
            }

            // The lane rides the footbridge's own axis, so the deck
            // and the crossing link are the same line.
            float laneZ = bridgeWest.Center.z;

            int LaneNode(string id, float x)
            {
                float y = CitySeacoastPlanner.SampleShoreWalkTop(
                    layout,
                    frame,
                    x,
                    laneZ);
                return graph.AddNode(
                    id,
                    new Vector3(x, y, laneZ),
                    false);
            }

            // ----------------------------------------------------------
            // the access spur: the lane's hold on the street sidewalks
            // ----------------------------------------------------------
            int accessNode = LaneNode(
                "coast:access",
                access.Center.x);
            ConnectSpurToSidewalk(
                layout,
                endpointsByNode,
                graph,
                access,
                accessNode);

            // ----------------------------------------------------------
            // the west chain: access, west quay, footbridge
            // ----------------------------------------------------------
            var west = new List<CoastLanePoint>
            {
                new CoastLanePoint(access.Center.x, accessNode),
            };
            int bridgeWestNode = graph.AddNode(
                "coast:bridge:west",
                new Vector3(
                    bridgeWest.Center.x,
                    bridgeWest.Center.y + bridgeWest.Size.y * 0.5f,
                    laneZ),
                false);
            west.Add(new CoastLanePoint(
                bridgeWest.Center.x,
                bridgeWestNode));
            AddQuayJunction(
                layout,
                graph,
                frame,
                laneZ,
                true,
                west);
            ChainLanePoints(graph, west, "coast-lane:west");

            // ----------------------------------------------------------
            // the crossing
            // ----------------------------------------------------------
            int bridgeEastNode = graph.AddNode(
                "coast:bridge:east",
                new Vector3(
                    bridgeEast.Center.x,
                    bridgeEast.Center.y + bridgeEast.Size.y * 0.5f,
                    laneZ),
                false);
            graph.AddLink(
                "coast-bridge",
                bridgeWestNode,
                bridgeEastNode,
                CityPedestrianLinkKind.Sidewalk,
                false);

            // ----------------------------------------------------------
            // the east chain: east quay, boat station, the wild shore
            // ----------------------------------------------------------
            var east = new List<CoastLanePoint>
            {
                new CoastLanePoint(bridgeEast.Center.x, bridgeEastNode),
                new CoastLanePoint(
                    pierRoot.Center.x,
                    LaneNode("coast:pier", pierRoot.Center.x)),
            };
            AddQuayJunction(
                layout,
                graph,
                frame,
                laneZ,
                false,
                east);

            // Intermediate posts every block-ish keep node heights on
            // the sand as the kerb climbs eastward, and keep every
            // link short enough to read as one straight walk.
            float ringX = frame.EastZone.xMin +
                          frame.EastZone.width * 0.35f;
            int intermediate = 0;
            for (float x = pierRoot.Center.x + 26f;
                 x < ringX - 13f;
                 x += 26f)
            {
                east.Add(new CoastLanePoint(
                    x,
                    LaneNode(
                        $"coast:lane:{intermediate:D2}",
                        x)));
                intermediate++;
            }

            int shoreNode = LaneNode("coast:shore", ringX);
            east.Add(new CoastLanePoint(ringX, shoreNode));
            ChainLanePoints(graph, east, "coast-lane:east");

            // ----------------------------------------------------------
            // the terminal ring: the walk's turn-around on the wild
            // shore, and its own local two-core anchor
            // ----------------------------------------------------------
            float ringFarX = ringX + 14f;
            float ringNorthZ = laneZ + 3.2f;
            int ringEast = LaneNode("coast:ring:1", ringFarX);
            int ringNorthEast = graph.AddNode(
                "coast:ring:2",
                new Vector3(
                    ringFarX,
                    CitySeacoastPlanner.SampleShoreWalkTop(
                        layout,
                        frame,
                        ringFarX,
                        ringNorthZ),
                    ringNorthZ),
                false);
            int ringNorthWest = graph.AddNode(
                "coast:ring:3",
                new Vector3(
                    ringX,
                    CitySeacoastPlanner.SampleShoreWalkTop(
                        layout,
                        frame,
                        ringX,
                        ringNorthZ),
                    ringNorthZ),
                false);
            graph.AddLink(
                "coast-ring:0",
                shoreNode,
                ringEast,
                CityPedestrianLinkKind.Sidewalk,
                false);
            graph.AddLink(
                "coast-ring:1",
                ringEast,
                ringNorthEast,
                CityPedestrianLinkKind.Sidewalk,
                false);
            graph.AddLink(
                "coast-ring:2",
                ringNorthEast,
                ringNorthWest,
                CityPedestrianLinkKind.Sidewalk,
                false);
            graph.AddLink(
                "coast-ring:3",
                ringNorthWest,
                shoreNode,
                CityPedestrianLinkKind.Sidewalk,
                false);
        }

        /// <summary>
        /// Ties the lane's access node to the boundary street's
        /// sidewalk: a T-node on the sidewalk lane at the access's own
        /// x, linked to both of the lane's endpoints and up through
        /// the rail opening. Three links, so the spur is core on its
        /// own.
        /// </summary>
        private static void ConnectSpurToSidewalk(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, List<LaneEndpoint>>
                endpointsByNode,
            GraphBuilder graph,
            in CityOpenAreaAccessDescriptor access,
            int accessNode)
        {
            RoadEdge frontage = access.FrontageEdge;
            LaneEndpoint first = default;
            LaneEndpoint second = default;
            bool hasFirst = FindShoreSideEndpoint(
                endpointsByNode,
                frontage,
                frontage.A,
                ref first);
            bool hasSecond = FindShoreSideEndpoint(
                endpointsByNode,
                frontage,
                frontage.B,
                ref second);
            if (!hasFirst || !hasSecond)
            {
                throw new InvalidOperationException(
                    "The waterfront access frontage must expose a " +
                    "sidewalk endpoint at both of its grid nodes.");
            }

            float spurY = (first.Position.y + second.Position.y) * 0.5f;
            int spurNode = graph.AddNode(
                "coast:spur",
                new Vector3(
                    access.Center.x,
                    spurY,
                    first.Position.z),
                false);
            graph.AddLink(
                "coast-spur:west",
                spurNode,
                first.NodeIndex,
                CityPedestrianLinkKind.Sidewalk,
                false);
            graph.AddLink(
                "coast-spur:east",
                spurNode,
                second.NodeIndex,
                CityPedestrianLinkKind.Sidewalk,
                false);
            graph.AddLink(
                "coast-spur",
                spurNode,
                accessNode,
                CityPedestrianLinkKind.Sidewalk,
                false);
        }

        /// <summary>
        /// The sidewalk endpoint on the shore side of the frontage
        /// street at one of its grid nodes: of the edge's endpoints
        /// registered there, the one furthest north — the boundary
        /// street's far pavement, the one the rail opening breaks
        /// onto.
        /// </summary>
        private static bool FindShoreSideEndpoint(
            IReadOnlyDictionary<Vector2Int, List<LaneEndpoint>>
                endpointsByNode,
            RoadEdge frontage,
            Vector2Int gridNode,
            ref LaneEndpoint result)
        {
            if (!endpointsByNode.TryGetValue(
                    gridNode,
                    out List<LaneEndpoint> endpoints))
            {
                return false;
            }

            bool found = false;
            for (int index = 0; index < endpoints.Count; index++)
            {
                LaneEndpoint endpoint = endpoints[index];
                if (endpoint.Edge != frontage)
                {
                    continue;
                }

                if (!found ||
                    endpoint.Position.z > result.Position.z)
                {
                    result = endpoint;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Hooks one river promenade's north stub into the shore lane:
        /// a junction node on the lane at the promenade's own lane x,
        /// and a straight link down the quay stair to the
        /// `river:{bank}:north` node the two-core used to prune away.
        /// </summary>
        private static void AddQuayJunction(
            CityLayout layout,
            GraphBuilder graph,
            in CitySeacoastFrame frame,
            float laneZ,
            bool westBank,
            ICollection<CoastLanePoint> points)
        {
            CityRiverPromenadeDescriptor promenade = default;
            bool found = false;
            for (int index = 0;
                 index < layout.River.Promenades.Count;
                 index++)
            {
                if (layout.River.Promenades[index].WestBank == westBank)
                {
                    promenade = layout.River.Promenades[index];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return;
            }

            string bankId = westBank ? "west" : "east";
            float laneX = westBank
                ? promenade.Bounds.xMin +
                  CitySeacoastPlanner.PromenadeLaneInset
                : promenade.Bounds.xMax -
                  CitySeacoastPlanner.PromenadeLaneInset;
            int junction = graph.AddNode(
                $"coast:quay:{bankId}",
                new Vector3(
                    laneX,
                    CitySeacoastPlanner.SampleShoreWalkTop(
                        layout,
                        frame,
                        laneX,
                        laneZ),
                    laneZ),
                false);
            points.Add(new CoastLanePoint(laneX, junction));

            // The stub node already exists under this exact id and
            // position; AddNode returns it rather than making another.
            int stub = graph.AddNode(
                $"river:{bankId}:north",
                new Vector3(
                    laneX,
                    promenade.NorthY,
                    promenade.Bounds.yMax - AgentRadius),
                false);
            graph.AddLink(
                $"coast-quay:{bankId}",
                junction,
                stub,
                CityPedestrianLinkKind.Sidewalk,
                false);
        }

        private static void ChainLanePoints(
            GraphBuilder graph,
            List<CoastLanePoint> points,
            string linkPrefix)
        {
            points.Sort(CoastLanePoint.Compare);
            for (int index = 1; index < points.Count; index++)
            {
                graph.AddLink(
                    $"{linkPrefix}:{index - 1}",
                    points[index - 1].NodeIndex,
                    points[index].NodeIndex,
                    CityPedestrianLinkKind.Sidewalk,
                    false);
            }
        }

        private readonly struct CoastLanePoint
        {
            public CoastLanePoint(float x, int nodeIndex)
            {
                X = x;
                NodeIndex = nodeIndex;
            }

            public float X { get; }
            public int NodeIndex { get; }

            public static int Compare(
                CoastLanePoint left,
                CoastLanePoint right)
            {
                int xComparison = left.X.CompareTo(right.X);
                return xComparison != 0
                    ? xComparison
                    : left.NodeIndex.CompareTo(right.NodeIndex);
            }
        }
    }
}
