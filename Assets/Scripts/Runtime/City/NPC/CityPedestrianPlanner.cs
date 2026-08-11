using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityPedestrianPlanner
    {
        public const int PaletteVariantCount = 4;
        public const float AgentRadius = 0.35f;
        public const float NavigationMargin = 0.15f;
        public const float MinimumSpawnSegmentLength = 4f;
        public const float MinimumSpeed = 1f;
        public const float MaximumSpeed = 1.3f;
        public const float MinimumAnimationSpeed = 0.88f;
        public const float MaximumAnimationSpeed = 0.94f;
        public const float CrosswalkChoiceProbability = 0.5f;

        private const float GeometryTolerance = 0.0001f;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.up
        };

        public static CityPedestrianPlan Create(
            CityLayout layout,
            int populationSeed)
        {
            return Create(layout, populationSeed, null);
        }

        public static CityPedestrianPlan Create(
            CityLayout layout,
            int populationSeed,
            CityStreetSurfacePlan streetSurfacePlan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            layout.ValidateOrThrow();
            if (CityStreetSurfacePlanner.SidewalkWidth < AgentRadius * 2f)
            {
                throw new InvalidOperationException(
                    "City sidewalks are too narrow for a pedestrian capsule.");
            }

            streetSurfacePlan ??= CityStreetSurfacePlanner.Create(layout);
            uint stableSeed = CityPedestrianStableHash.Combine(
                CityPedestrianStableHash.Combine(
                    unchecked((uint)layout.Seed),
                    unchecked((uint)populationSeed)),
                CityPedestrianStableHash.String(layout.BlueprintId));
            Dictionary<Vector2Int, ConnectionInfo> connections =
                CreateConnections(layout);
            var busIntersections = new HashSet<Vector2Int>(
                CityBusIntersectionSelector.Select(layout));
            List<RoadEdge> sortedEdges = new List<RoadEdge>(
                layout.RoadEdges);
            sortedEdges.Sort(RoadEdge.Compare);

            var graph = new GraphBuilder();
            var endpointsByNode =
                new Dictionary<Vector2Int, List<LaneEndpoint>>();
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                endpointsByNode.Add(
                    layout.Nodes[index],
                    new List<LaneEndpoint>());
            }

            bool supportsCrosswalkNavigation =
                streetSurfacePlan.CarriagewayWidth >
                (AgentRadius * 2f) + GeometryTolerance;
            Dictionary<RoadEdge, List<int>> crosswalksByEdge =
                supportsCrosswalkNavigation
                    ? IndexCrosswalks(streetSurfacePlan.Crosswalks)
                    : new Dictionary<RoadEdge, List<int>>();
            var crosswalkLaneNodes =
                new Dictionary<CrosswalkLaneKey, int>();
            for (int edgeIndex = 0;
                 edgeIndex < sortedEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = sortedEdges[edgeIndex];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                BuildEdgeLanes(
                    layout,
                    streetSurfacePlan,
                    connections,
                    edge,
                    graph,
                    endpointsByNode,
                    crosswalksByEdge,
                    crosswalkLaneNodes);
            }

            BuildJunctions(
                layout,
                connections,
                endpointsByNode,
                busIntersections,
                graph);
            if (supportsCrosswalkNavigation)
            {
                BuildCrosswalkLinks(
                    streetSurfacePlan,
                    crosswalkLaneNodes,
                    graph);
            }
            return graph.CreatePlan(
                layout.Seed,
                populationSeed,
                stableSeed);
        }

        public static RoadWalkableArea CreateWalkableArea(
            CityPedestrianPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return new RoadWalkableArea(plan.NavigationRectangles);
        }

        public static float GetSidewalkCenterOffset(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return (layout.RoadWidth * 0.5f) -
                   (CityStreetSurfacePlanner.SidewalkWidth * 0.5f);
        }

        private static void BuildEdgeLanes(
            CityLayout layout,
            CityStreetSurfacePlan streetSurfacePlan,
            IReadOnlyDictionary<Vector2Int, ConnectionInfo> connections,
            RoadEdge edge,
            GraphBuilder graph,
            IDictionary<Vector2Int, List<LaneEndpoint>> endpointsByNode,
            IReadOnlyDictionary<RoadEdge, List<int>> crosswalksByEdge,
            IDictionary<CrosswalkLaneKey, int> crosswalkLaneNodes)
        {
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            Vector3 tangent = end - start;
            tangent.y = 0f;
            float length = tangent.magnitude;
            if (length <= GeometryTolerance)
            {
                return;
            }

            tangent /= length;
            Vector3 left = new Vector3(-tangent.z, 0f, tangent.x);
            float halfRoad = layout.RoadWidth * 0.5f;
            float sideOffset = GetSidewalkCenterOffset(layout);
            float safeStart = ResolveSurfaceInset(
                connections[edge.A],
                halfRoad) + AgentRadius;
            float safeEnd = length - ResolveSurfaceInset(
                connections[edge.B],
                halfRoad) - AgentRadius;
            if (safeEnd - safeStart <= GeometryTolerance)
            {
                return;
            }

            crosswalksByEdge.TryGetValue(
                edge,
                out List<int> edgeCrosswalks);
            for (int side = -1; side <= 1; side += 2)
            {
                string laneId = $"{EdgeId(edge)}:side:{side}";
                var points = new List<LanePoint>();
                int firstNode = graph.AddNode(
                    $"lane:{laneId}:a",
                    SidewalkPosition(
                        start,
                        tangent,
                        left,
                        safeStart,
                        sideOffset,
                        side),
                    false);
                points.Add(new LanePoint(safeStart, firstNode));
                endpointsByNode[edge.A].Add(new LaneEndpoint(
                    edge,
                    firstNode,
                    graph.Nodes[firstNode].Position));

                if (edgeCrosswalks != null)
                {
                    for (int index = 0;
                         index < edgeCrosswalks.Count;
                         index++)
                    {
                        int crosswalkIndex = edgeCrosswalks[index];
                        CityCrosswalkDescriptor crosswalk =
                            streetSurfacePlan.Crosswalks[crosswalkIndex];
                        float distance = Vector3.Dot(
                            crosswalk.Center - start,
                            tangent);
                        if (distance <= safeStart + GeometryTolerance ||
                            distance >= safeEnd - GeometryTolerance)
                        {
                            continue;
                        }

                        int crossingNode = graph.AddNode(
                            $"lane:{laneId}:crosswalk:{crosswalkIndex}",
                            SidewalkPosition(
                                start,
                                tangent,
                                left,
                                distance,
                                sideOffset,
                                side),
                            true);
                        points.Add(new LanePoint(distance, crossingNode));
                        crosswalkLaneNodes.Add(
                            new CrosswalkLaneKey(crosswalkIndex, side),
                            crossingNode);
                    }
                }

                int secondNode = graph.AddNode(
                    $"lane:{laneId}:b",
                    SidewalkPosition(
                        start,
                        tangent,
                        left,
                        safeEnd,
                        sideOffset,
                        side),
                    false);
                points.Add(new LanePoint(safeEnd, secondNode));
                endpointsByNode[edge.B].Add(new LaneEndpoint(
                    edge,
                    secondNode,
                    graph.Nodes[secondNode].Position));
                points.Sort((first, second) =>
                    first.Distance.CompareTo(second.Distance));
                for (int index = 1; index < points.Count; index++)
                {
                    graph.AddLink(
                        $"sidewalk:{laneId}:{index - 1}",
                        points[index - 1].NodeIndex,
                        points[index].NodeIndex,
                        CityPedestrianLinkKind.Sidewalk,
                        true);
                }
            }
        }

        private static void BuildJunctions(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, ConnectionInfo> connections,
            IReadOnlyDictionary<Vector2Int, List<LaneEndpoint>>
                endpointsByNode,
            ISet<Vector2Int> busIntersections,
            GraphBuilder graph)
        {
            var nodes = new List<Vector2Int>(layout.Nodes);
            nodes.Sort(CompareNodes);
            float sideOffset = GetSidewalkCenterOffset(layout);
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                Vector2Int gridNode = nodes[nodeIndex];
                ConnectionInfo connection = connections[gridNode];
                List<LaneEndpoint> endpoints = endpointsByNode[gridNode];
                Vector3 center = layout.GetNodeWorldPosition(gridNode);
                if (connection.IsIntersectionCore &&
                    connection.StreetCount > 0)
                {
                    bool usesWideTurnSidewalk =
                        busIntersections.Contains(gridNode);
                    float cornerOffset = usesWideTurnSidewalk
                        ? CityBusIntersectionSelector
                            .GetCornerCenterOffset(layout)
                        : sideOffset;
                    var corners = new Dictionary<CornerKey, int>();
                    for (int xSign = -1; xSign <= 1; xSign += 2)
                    {
                        for (int zSign = -1; zSign <= 1; zSign += 2)
                        {
                            var key = new CornerKey(xSign, zSign);
                            corners.Add(
                                key,
                                graph.AddNode(
                                    $"junction:{gridNode.x}:{gridNode.y}:" +
                                    $"corner:{xSign}:{zSign}",
                                    new Vector3(
                                        center.x + (xSign * cornerOffset),
                                        CityStreetSurfacePlanner.SidewalkTop,
                                        center.z + (zSign * cornerOffset)),
                                    false));
                        }
                    }

                    for (int index = 0; index < endpoints.Count; index++)
                    {
                        LaneEndpoint endpoint = endpoints[index];
                        Vector3 offset = endpoint.Position - center;
                        var key = new CornerKey(
                            SignNonZero(offset.x),
                            SignNonZero(offset.z));
                        if (usesWideTurnSidewalk)
                        {
                            ConnectWideTurnEndpoint(
                                gridNode,
                                endpoint,
                                index,
                                graph.Nodes[corners[key]].Position,
                                corners[key],
                                graph);
                        }
                        else
                        {
                            graph.AddLink(
                                $"turn:{gridNode.x}:{gridNode.y}:" +
                                $"{EdgeId(endpoint.Edge)}:{index}",
                                endpoint.NodeIndex,
                                corners[key],
                                CityPedestrianLinkKind.Turn,
                                false);
                        }
                    }

                    for (int directionIndex = 0;
                         directionIndex < CardinalDirections.Length;
                         directionIndex++)
                    {
                        Vector2Int direction =
                            CardinalDirections[directionIndex];
                        if (connection.Contains(direction))
                        {
                            continue;
                        }

                        int mouth = graph.AddNode(
                            $"junction:{gridNode.x}:{gridNode.y}:" +
                            $"mouth:{direction.x}:{direction.y}",
                            new Vector3(
                                center.x + (direction.x * cornerOffset),
                                CityStreetSurfacePlanner.SidewalkTop,
                                center.z + (direction.y * cornerOffset)),
                            false);
                        GetMouthCorners(
                            direction,
                            out CornerKey firstCorner,
                            out CornerKey secondCorner);
                        graph.AddLink(
                            $"turn:{gridNode.x}:{gridNode.y}:mouth:" +
                            $"{directionIndex}:a",
                            corners[firstCorner],
                            mouth,
                            CityPedestrianLinkKind.Turn,
                            false);
                        graph.AddLink(
                            $"turn:{gridNode.x}:{gridNode.y}:mouth:" +
                            $"{directionIndex}:b",
                            mouth,
                            corners[secondCorner],
                            CityPedestrianLinkKind.Turn,
                            false);
                    }

                    continue;
                }

                ConnectStraightSeams(gridNode, endpoints, graph);
            }
        }

        private static void ConnectWideTurnEndpoint(
            Vector2Int gridNode,
            LaneEndpoint endpoint,
            int endpointIndex,
            Vector3 cornerPosition,
            int cornerNodeIndex,
            GraphBuilder graph)
        {
            Vector3 accessPosition = endpoint.Edge.IsHorizontal
                ? new Vector3(
                    cornerPosition.x,
                    cornerPosition.y,
                    endpoint.Position.z)
                : new Vector3(
                    endpoint.Position.x,
                    cornerPosition.y,
                    cornerPosition.z);
            string idPrefix =
                $"turn:{gridNode.x}:{gridNode.y}:" +
                $"{EdgeId(endpoint.Edge)}:{endpointIndex}";
            int accessNode = graph.AddNode(
                idPrefix + ":wide-access",
                accessPosition,
                false);
            graph.AddLink(
                idPrefix + ":wide-entry",
                endpoint.NodeIndex,
                accessNode,
                CityPedestrianLinkKind.Turn,
                false);
            graph.AddLink(
                idPrefix + ":wide-corner",
                accessNode,
                cornerNodeIndex,
                CityPedestrianLinkKind.Turn,
                false);
        }

        private static void ConnectStraightSeams(
            Vector2Int gridNode,
            IReadOnlyList<LaneEndpoint> endpoints,
            GraphBuilder graph)
        {
            var connected = new HashSet<int>();
            float maximumGap = (AgentRadius * 2f) +
                               (NavigationMargin * 2f) +
                               GeometryTolerance;
            for (int first = 0; first < endpoints.Count; first++)
            {
                if (connected.Contains(first))
                {
                    continue;
                }

                int best = -1;
                float bestDistance = float.PositiveInfinity;
                for (int second = first + 1;
                     second < endpoints.Count;
                     second++)
                {
                    if (connected.Contains(second) ||
                        endpoints[first].Edge.Equals(endpoints[second].Edge))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(
                        endpoints[first].Position,
                        endpoints[second].Position);
                    if (distance <= maximumGap && distance < bestDistance)
                    {
                        best = second;
                        bestDistance = distance;
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                connected.Add(first);
                connected.Add(best);
                Vector3 seamPosition =
                    (endpoints[first].Position + endpoints[best].Position) *
                    0.5f;
                int seam = graph.AddNode(
                    $"junction:{gridNode.x}:{gridNode.y}:seam:{first}",
                    seamPosition,
                    false);
                graph.AddLink(
                    $"turn:{gridNode.x}:{gridNode.y}:seam:{first}:a",
                    endpoints[first].NodeIndex,
                    seam,
                    CityPedestrianLinkKind.Turn,
                    false);
                graph.AddLink(
                    $"turn:{gridNode.x}:{gridNode.y}:seam:{first}:b",
                    seam,
                    endpoints[best].NodeIndex,
                    CityPedestrianLinkKind.Turn,
                    false);
            }
        }

        private static void BuildCrosswalkLinks(
            CityStreetSurfacePlan streetSurfacePlan,
            IReadOnlyDictionary<CrosswalkLaneKey, int> crosswalkLaneNodes,
            GraphBuilder graph)
        {
            float roadExtent =
                (streetSurfacePlan.CarriagewayWidth * 0.5f) - AgentRadius;
            for (int index = 0;
                 index < streetSurfacePlan.Crosswalks.Count;
                 index++)
            {
                if (!crosswalkLaneNodes.TryGetValue(
                        new CrosswalkLaneKey(index, -1),
                        out int negativeSide) ||
                    !crosswalkLaneNodes.TryGetValue(
                        new CrosswalkLaneKey(index, 1),
                        out int positiveSide))
                {
                    continue;
                }

                Vector3 positivePosition =
                    graph.Nodes[positiveSide].Position;
                Vector3 negativePosition =
                    graph.Nodes[negativeSide].Position;
                Vector3 across = positivePosition - negativePosition;
                across.y = 0f;
                if (across.sqrMagnitude <= GeometryTolerance)
                {
                    continue;
                }

                across.Normalize();
                Vector3 center = streetSurfacePlan.Crosswalks[index].Center;
                center.y = CityStreetSurfacePlanner.RoadTop;
                int positiveRoad = graph.AddNode(
                    $"crosswalk:{index}:road:positive",
                    center + (across * roadExtent),
                    false);
                int negativeRoad = graph.AddNode(
                    $"crosswalk:{index}:road:negative",
                    center - (across * roadExtent),
                    false);
                graph.AddLink(
                    $"crosswalk:{index}:positive-curb",
                    positiveSide,
                    positiveRoad,
                    CityPedestrianLinkKind.Crosswalk,
                    false);
                graph.AddLink(
                    $"crosswalk:{index}:carriageway",
                    positiveRoad,
                    negativeRoad,
                    CityPedestrianLinkKind.Crosswalk,
                    false);
                graph.AddLink(
                    $"crosswalk:{index}:negative-curb",
                    negativeRoad,
                    negativeSide,
                    CityPedestrianLinkKind.Crosswalk,
                    false);
            }
        }

        private static Dictionary<RoadEdge, List<int>> IndexCrosswalks(
            IReadOnlyList<CityCrosswalkDescriptor> crosswalks)
        {
            var result = new Dictionary<RoadEdge, List<int>>();
            for (int index = 0; index < crosswalks.Count; index++)
            {
                RoadEdge edge = crosswalks[index].ApproachEdge;
                if (!result.TryGetValue(edge, out List<int> indices))
                {
                    indices = new List<int>();
                    result.Add(edge, indices);
                }

                indices.Add(index);
            }

            return result;
        }

        private static Dictionary<Vector2Int, ConnectionInfo>
            CreateConnections(CityLayout layout)
        {
            var result = new Dictionary<Vector2Int, ConnectionInfo>();
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                result.Add(layout.Nodes[index], new ConnectionInfo());
            }

            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                bool street =
                    layout.GetPathKind(edge) == CityPathKind.Street;
                result[edge.A].Add(edge.B - edge.A, street);
                result[edge.B].Add(edge.A - edge.B, street);
            }

            return result;
        }

        private static Vector3 SidewalkPosition(
            Vector3 start,
            Vector3 tangent,
            Vector3 left,
            float distance,
            float sideOffset,
            int side)
        {
            Vector3 position = start +
                               (tangent * distance) +
                               (left * (sideOffset * side));
            position.y = CityStreetSurfacePlanner.SidewalkTop;
            return position;
        }

        private static float ResolveSurfaceInset(
            ConnectionInfo connection,
            float halfRoad)
        {
            if (connection.IsIntersectionCore)
            {
                return halfRoad;
            }

            return connection.Count == 1 ? -halfRoad : 0f;
        }

        private static void GetMouthCorners(
            Vector2Int direction,
            out CornerKey first,
            out CornerKey second)
        {
            if (direction.x > 0)
            {
                first = new CornerKey(1, 1);
                second = new CornerKey(1, -1);
                return;
            }

            if (direction.x < 0)
            {
                first = new CornerKey(-1, -1);
                second = new CornerKey(-1, 1);
                return;
            }

            if (direction.y > 0)
            {
                first = new CornerKey(-1, 1);
                second = new CornerKey(1, 1);
                return;
            }

            first = new CornerKey(1, -1);
            second = new CornerKey(-1, -1);
        }

        private static int SignNonZero(float value)
        {
            return value < 0f ? -1 : 1;
        }

        private static int CompareNodes(Vector2Int first, Vector2Int second)
        {
            int x = first.x.CompareTo(second.x);
            return x != 0 ? x : first.y.CompareTo(second.y);
        }

        private static string EdgeId(RoadEdge edge)
        {
            return $"{edge.A.x}:{edge.A.y}:{edge.B.x}:{edge.B.y}";
        }

        private sealed class GraphBuilder
        {
            private readonly List<CityPedestrianNode> nodes =
                new List<CityPedestrianNode>();
            private readonly List<CityPedestrianLink> links =
                new List<CityPedestrianLink>();
            private readonly List<CityPedestrianSpawnAnchor> spawnAnchors =
                new List<CityPedestrianSpawnAnchor>();
            private readonly List<Rect> navigationRectangles =
                new List<Rect>();
            private readonly Dictionary<string, int> nodeIndices =
                new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly HashSet<LinkKey> linkKeys =
                new HashSet<LinkKey>();

            public IReadOnlyList<CityPedestrianNode> Nodes => nodes;

            public int AddNode(
                string id,
                Vector3 position,
                bool isCrosswalkEntry)
            {
                if (nodeIndices.TryGetValue(id, out int existing))
                {
                    return existing;
                }

                int index = nodes.Count;
                nodeIndices.Add(id, index);
                nodes.Add(new CityPedestrianNode(
                    id,
                    position,
                    isCrosswalkEntry));
                return index;
            }

            public void AddLink(
                string id,
                int firstNode,
                int secondNode,
                CityPedestrianLinkKind kind,
                bool spawnEligible)
            {
                if (firstNode == secondNode)
                {
                    return;
                }

                var key = new LinkKey(firstNode, secondNode, kind);
                if (!linkKeys.Add(key))
                {
                    return;
                }

                links.Add(new CityPedestrianLink(
                    id,
                    firstNode,
                    secondNode,
                    kind));
                Vector3 first = nodes[firstNode].Position;
                Vector3 second = nodes[secondNode].Position;
                navigationRectangles.Add(CreateCorridor(first, second));
                Vector3 delta = second - first;
                delta.y = 0f;
                if (spawnEligible &&
                    delta.magnitude >= MinimumSpawnSegmentLength)
                {
                    spawnAnchors.Add(new CityPedestrianSpawnAnchor(
                        $"spawn:{id}",
                        (first + second) * 0.5f,
                        firstNode,
                        secondNode));
                }
            }

            public CityPedestrianPlan CreatePlan(
                int layoutSeed,
                int populationSeed,
                uint stableSeed)
            {
                FindTwoCore(
                    out bool[] retainedNodes,
                    out bool[] retainedLinks);
                int[] remappedNodeIndices = new int[nodes.Count];
                var safeNodes = new List<CityPedestrianNode>();
                for (int index = 0; index < nodes.Count; index++)
                {
                    remappedNodeIndices[index] = -1;
                    if (!retainedNodes[index])
                    {
                        continue;
                    }

                    remappedNodeIndices[index] = safeNodes.Count;
                    safeNodes.Add(nodes[index]);
                }

                var safeLinks = new List<CityPedestrianLink>();
                var safeRectangles = new List<Rect>();
                for (int index = 0; index < links.Count; index++)
                {
                    if (!retainedLinks[index])
                    {
                        continue;
                    }

                    CityPedestrianLink link = links[index];
                    safeLinks.Add(new CityPedestrianLink(
                        link.Id,
                        remappedNodeIndices[link.FirstNodeIndex],
                        remappedNodeIndices[link.SecondNodeIndex],
                        link.Kind));
                    safeRectangles.Add(navigationRectangles[index]);
                }

                var safeAnchors =
                    new List<CityPedestrianSpawnAnchor>();
                for (int index = 0; index < spawnAnchors.Count; index++)
                {
                    CityPedestrianSpawnAnchor anchor = spawnAnchors[index];
                    if (retainedNodes[anchor.FirstNodeIndex] &&
                        retainedNodes[anchor.SecondNodeIndex])
                    {
                        safeAnchors.Add(new CityPedestrianSpawnAnchor(
                            anchor.Id,
                            anchor.Position,
                            remappedNodeIndices[anchor.FirstNodeIndex],
                            remappedNodeIndices[anchor.SecondNodeIndex]));
                    }
                }

                return new CityPedestrianPlan(
                    layoutSeed,
                    populationSeed,
                    stableSeed,
                    AgentRadius,
                    safeNodes,
                    safeLinks,
                    safeAnchors,
                    safeRectangles);
            }

            private void FindTwoCore(
                out bool[] retainedNodes,
                out bool[] retainedLinks)
            {
                retainedNodes = new bool[nodes.Count];
                retainedLinks = new bool[links.Count];
                var linkIndicesByNode = new List<int>[nodes.Count];
                var degree = new int[nodes.Count];
                for (int index = 0; index < nodes.Count; index++)
                {
                    retainedNodes[index] = true;
                    linkIndicesByNode[index] = new List<int>();
                }

                for (int index = 0; index < links.Count; index++)
                {
                    retainedLinks[index] = true;
                    CityPedestrianLink link = links[index];
                    linkIndicesByNode[link.FirstNodeIndex].Add(index);
                    linkIndicesByNode[link.SecondNodeIndex].Add(index);
                    degree[link.FirstNodeIndex]++;
                    degree[link.SecondNodeIndex]++;
                }

                var pending = new Queue<int>();
                for (int index = 0; index < degree.Length; index++)
                {
                    if (degree[index] < 2)
                    {
                        pending.Enqueue(index);
                    }
                }

                while (pending.Count > 0)
                {
                    int nodeIndex = pending.Dequeue();
                    if (!retainedNodes[nodeIndex])
                    {
                        continue;
                    }

                    retainedNodes[nodeIndex] = false;
                    List<int> incidentLinks = linkIndicesByNode[nodeIndex];
                    for (int index = 0; index < incidentLinks.Count; index++)
                    {
                        int linkIndex = incidentLinks[index];
                        if (!retainedLinks[linkIndex])
                        {
                            continue;
                        }

                        retainedLinks[linkIndex] = false;
                        int other = links[linkIndex].Other(nodeIndex);
                        degree[other]--;
                        if (retainedNodes[other] && degree[other] < 2)
                        {
                            pending.Enqueue(other);
                        }
                    }
                }
            }

            private static Rect CreateCorridor(
                Vector3 first,
                Vector3 second)
            {
                if (Mathf.Abs(first.x - second.x) > GeometryTolerance &&
                    Mathf.Abs(first.z - second.z) > GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        "Pedestrian graph links must remain axis-aligned.");
                }

                float padding = AgentRadius + NavigationMargin;
                return Rect.MinMaxRect(
                    Mathf.Min(first.x, second.x) - padding,
                    Mathf.Min(first.z, second.z) - padding,
                    Mathf.Max(first.x, second.x) + padding,
                    Mathf.Max(first.z, second.z) + padding);
            }
        }

        private sealed class ConnectionInfo
        {
            private readonly List<Vector2Int> directions =
                new List<Vector2Int>(4);

            public int Count => directions.Count;
            public int StreetCount { get; private set; }
            public bool IsIntersectionCore =>
                directions.Count >= 3 ||
                (directions.Count == 2 &&
                 directions[0] + directions[1] != Vector2Int.zero);

            public void Add(Vector2Int direction, bool street)
            {
                directions.Add(direction);
                if (street)
                {
                    StreetCount++;
                }
            }

            public bool Contains(Vector2Int direction)
            {
                return directions.Contains(direction);
            }
        }

        private readonly struct LanePoint
        {
            public LanePoint(float distance, int nodeIndex)
            {
                Distance = distance;
                NodeIndex = nodeIndex;
            }

            public float Distance { get; }
            public int NodeIndex { get; }
        }

        private readonly struct LaneEndpoint
        {
            public LaneEndpoint(
                RoadEdge edge,
                int nodeIndex,
                Vector3 position)
            {
                Edge = edge;
                NodeIndex = nodeIndex;
                Position = position;
            }

            public RoadEdge Edge { get; }
            public int NodeIndex { get; }
            public Vector3 Position { get; }
        }

        private readonly struct CrosswalkLaneKey :
            IEquatable<CrosswalkLaneKey>
        {
            public CrosswalkLaneKey(int crosswalkIndex, int side)
            {
                CrosswalkIndex = crosswalkIndex;
                Side = side;
            }

            public int CrosswalkIndex { get; }
            public int Side { get; }

            public bool Equals(CrosswalkLaneKey other)
            {
                return CrosswalkIndex == other.CrosswalkIndex &&
                       Side == other.Side;
            }

            public override bool Equals(object obj)
            {
                return obj is CrosswalkLaneKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (CrosswalkIndex * 397) ^ Side;
            }
        }

        private readonly struct CornerKey : IEquatable<CornerKey>
        {
            public CornerKey(int xSign, int zSign)
            {
                XSign = xSign;
                ZSign = zSign;
            }

            public int XSign { get; }
            public int ZSign { get; }

            public bool Equals(CornerKey other)
            {
                return XSign == other.XSign && ZSign == other.ZSign;
            }

            public override bool Equals(object obj)
            {
                return obj is CornerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (XSign * 397) ^ ZSign;
            }
        }

        private readonly struct LinkKey : IEquatable<LinkKey>
        {
            public LinkKey(
                int first,
                int second,
                CityPedestrianLinkKind kind)
            {
                First = Math.Min(first, second);
                Second = Math.Max(first, second);
                Kind = kind;
            }

            public int First { get; }
            public int Second { get; }
            public CityPedestrianLinkKind Kind { get; }

            public bool Equals(LinkKey other)
            {
                return First == other.First &&
                       Second == other.Second &&
                       Kind == other.Kind;
            }

            public override bool Equals(object obj)
            {
                return obj is LinkKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = First;
                    hash = (hash * 397) ^ Second;
                    return (hash * 397) ^ (int)Kind;
                }
            }
        }
    }

    internal static class CityPedestrianStableHash
    {
        public static uint String(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string safeValue = value ?? string.Empty;
                for (int index = 0; index < safeValue.Length; index++)
                {
                    char character = safeValue[index];
                    hash ^= (byte)character;
                    hash *= 16777619u;
                    hash ^= (byte)(character >> 8);
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        public static uint Combine(uint first, uint second)
        {
            unchecked
            {
                uint hash = first ^ (second + 0x9E3779B9u +
                                     (first << 6) + (first >> 2));
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return hash;
            }
        }

        public static float ToUnitFloat(uint hash)
        {
            return (hash & 0x00FFFFFFu) / 16777216f;
        }
    }
}
