using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityStreetSurfacePlanner
    {
        public const float SidewalkWidth = 1f;
        public const float RoadTop = 0.08f;
        public const float SidewalkTop = 0.14f;
        public const float CrosswalkDepth = 2.4f;
        public const int CrosswalkStripeCount = 4;
        public const int MaximumCrosswalkIntersections =
            CityStreetIntersectionSelector.MaximumIntersectionCount;

        private const float RoadSurfaceHeight = RoadTop * 2f;
        private const float SidewalkHeight = SidewalkTop - RoadTop;
        private const float CenterDashWidth = 0.13f;
        private const float MaximumCenterDashLength = 2.1f;
        private const float MarkingHeight = 0.025f;
        private const float MarkingCenterAboveRoadBase = 0.095f;
        private const float CrosswalkStripeDepth = 0.36f;
        private const float GeometryTolerance = 0.0001f;

        public static CityStreetSurfacePlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            float carriagewayWidth =
                layout.RoadWidth - (SidewalkWidth * 2f);
            if (!IsFinite(layout.RoadWidth) ||
                carriagewayWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    layout.RoadWidth,
                    "Road width must leave positive carriageway space " +
                    "between both sidewalks.");
            }

            var streetSurfaces = new List<Bounds>();
            var parkPaths = new List<Bounds>();
            var sidewalks = new List<Bounds>();
            var centerMarkings = new List<Bounds>();
            var crosswalkMarkings = new List<Bounds>();
            var sidewalkWalkableRectangles = new List<Rect>();
            var crosswalkWalkableRectangles = new List<Rect>();
            var crosswalks = new List<CityCrosswalkDescriptor>();
            var markingExclusions = new List<Rect>();
            var edgesWithSidewalks = new HashSet<RoadEdge>();

            Dictionary<Vector2Int, NodeConnections> connections =
                CreateNodeConnections(layout);
            List<RoadEdge> sortedEdges = CreateSortedEdges(layout);
            CreateBaseSurfaces(
                layout,
                sortedEdges,
                streetSurfaces,
                parkPaths);
            CreateSidewalkStrips(
                layout,
                sortedEdges,
                connections,
                sidewalks,
                sidewalkWalkableRectangles,
                edgesWithSidewalks);
            CreateIntersectionSidewalks(
                layout,
                connections,
                sidewalks,
                sidewalkWalkableRectangles,
                markingExclusions);

            IReadOnlyList<Vector2Int> selectedNodes =
                CityStreetIntersectionSelector.Select(
                    layout,
                    MaximumCrosswalkIntersections);
            CreateCrosswalks(
                layout,
                selectedNodes,
                sortedEdges,
                edgesWithSidewalks,
                carriagewayWidth,
                crosswalkMarkings,
                crosswalkWalkableRectangles,
                crosswalks,
                markingExclusions);
            CreateCenterMarkings(
                layout,
                sortedEdges,
                markingExclusions,
                centerMarkings);

            return new CityStreetSurfacePlan(
                carriagewayWidth,
                streetSurfaces,
                parkPaths,
                sidewalks,
                centerMarkings,
                crosswalkMarkings,
                sidewalkWalkableRectangles,
                crosswalkWalkableRectangles,
                new List<Vector2Int>(selectedNodes),
                crosswalks);
        }

        private static void CreateBaseSurfaces(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            ICollection<Bounds> streetSurfaces,
            ICollection<Bounds> parkPaths)
        {
            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                Vector3 delta = end - start;
                Vector3 size = edge.IsHorizontal
                    ? new Vector3(
                        Mathf.Abs(delta.x) + layout.RoadWidth,
                        RoadSurfaceHeight,
                        layout.RoadWidth)
                    : new Vector3(
                        layout.RoadWidth,
                        RoadSurfaceHeight,
                        Mathf.Abs(delta.z) + layout.RoadWidth);
                var surface = new Bounds((start + end) * 0.5f, size);
                if (layout.GetPathKind(edge) == CityPathKind.ParkPath)
                {
                    parkPaths.Add(surface);
                }
                else
                {
                    streetSurfaces.Add(surface);
                }
            }
        }

        private static void CreateSidewalkStrips(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ICollection<Bounds> sidewalks,
            ICollection<Rect> walkableRectangles,
            ISet<RoadEdge> edgesWithSidewalks)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            float sideOffset = halfRoad - (SidewalkWidth * 0.5f);
            for (int index = 0; index < sortedEdges.Count; index++)
            {
                RoadEdge edge = sortedEdges[index];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 roadStart = layout.GetNodeWorldPosition(edge.A);
                Vector3 roadEnd = layout.GetNodeWorldPosition(edge.B);
                Vector3 tangent = (roadEnd - roadStart).normalized;
                float startInset = ResolveEndpointInset(
                    connections[edge.A],
                    halfRoad);
                float endInset = ResolveEndpointInset(
                    connections[edge.B],
                    halfRoad);
                Vector3 start = roadStart + (tangent * startInset);
                Vector3 end = roadEnd - (tangent * endInset);
                float length = Vector3.Distance(start, end);
                if (length <= GeometryTolerance)
                {
                    throw new InvalidOperationException(
                        $"Street edge {edge} is too short for sidewalks.");
                }

                Vector3 left = new Vector3(
                    -tangent.z,
                    0f,
                    tangent.x);
                Vector3 center = (start + end) * 0.5f;
                center.y = roadStart.y +
                           ((RoadTop + SidewalkTop) * 0.5f);
                Vector3 size = edge.IsHorizontal
                    ? new Vector3(length, SidewalkHeight, SidewalkWidth)
                    : new Vector3(SidewalkWidth, SidewalkHeight, length);
                AddSidewalk(
                    new Bounds(center + (left * sideOffset), size),
                    sidewalks,
                    walkableRectangles);
                AddSidewalk(
                    new Bounds(center - (left * sideOffset), size),
                    sidewalks,
                    walkableRectangles);
                edgesWithSidewalks.Add(edge);
            }
        }

        private static void CreateIntersectionSidewalks(
            CityLayout layout,
            IReadOnlyDictionary<Vector2Int, NodeConnections> connections,
            ICollection<Bounds> sidewalks,
            ICollection<Rect> walkableRectangles,
            ICollection<Rect> markingExclusions)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            float carriagewayWidth =
                layout.RoadWidth - (SidewalkWidth * 2f);
            float halfCarriageway = carriagewayWidth * 0.5f;
            float sideOffset =
                halfCarriageway + (SidewalkWidth * 0.5f);
            List<Vector2Int> nodes = CreateSortedNodes(layout);
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector2Int node = nodes[index];
                NodeConnections nodeConnections = connections[node];
                if (!nodeConnections.IsIntersectionCore)
                {
                    continue;
                }

                Vector3 nodePosition = layout.GetNodeWorldPosition(node);
                markingExclusions.Add(Rect.MinMaxRect(
                    nodePosition.x - halfRoad,
                    nodePosition.z - halfRoad,
                    nodePosition.x + halfRoad,
                    nodePosition.z + halfRoad));
                if (nodeConnections.StreetCount == 0)
                {
                    continue;
                }

                for (int xSign = -1; xSign <= 1; xSign += 2)
                {
                    for (int zSign = -1; zSign <= 1; zSign += 2)
                    {
                        Vector3 center = nodePosition + new Vector3(
                            xSign * sideOffset,
                            (RoadTop + SidewalkTop) * 0.5f,
                            zSign * sideOffset);
                        AddSidewalk(
                            new Bounds(
                                center,
                                new Vector3(
                                    SidewalkWidth,
                                    SidewalkHeight,
                                    SidewalkWidth)),
                            sidewalks,
                            walkableRectangles);
                    }
                }

                AddClosedIntersectionMouths(
                    nodePosition,
                    nodeConnections,
                    carriagewayWidth,
                    sideOffset,
                    sidewalks,
                    walkableRectangles);
            }
        }

        private static void AddClosedIntersectionMouths(
            Vector3 nodePosition,
            NodeConnections connections,
            float carriagewayWidth,
            float sideOffset,
            ICollection<Bounds> sidewalks,
            ICollection<Rect> walkableRectangles)
        {
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Vector2Int direction = directions[index];
                if (connections.Contains(direction))
                {
                    continue;
                }

                Vector3 center = nodePosition + new Vector3(
                    direction.x * sideOffset,
                    (RoadTop + SidewalkTop) * 0.5f,
                    direction.y * sideOffset);
                Vector3 size = direction.x != 0
                    ? new Vector3(
                        SidewalkWidth,
                        SidewalkHeight,
                        carriagewayWidth)
                    : new Vector3(
                        carriagewayWidth,
                        SidewalkHeight,
                        SidewalkWidth);
                AddSidewalk(
                    new Bounds(center, size),
                    sidewalks,
                    walkableRectangles);
            }
        }

        private static void CreateCrosswalks(
            CityLayout layout,
            IReadOnlyList<Vector2Int> selectedNodes,
            IReadOnlyList<RoadEdge> sortedEdges,
            ISet<RoadEdge> edgesWithSidewalks,
            float carriagewayWidth,
            ICollection<Bounds> crosswalkMarkings,
            ICollection<Rect> walkableRectangles,
            ICollection<CityCrosswalkDescriptor> crosswalks,
            ICollection<Rect> markingExclusions)
        {
            float halfRoad = layout.RoadWidth * 0.5f;
            for (int nodeIndex = 0;
                 nodeIndex < selectedNodes.Count;
                 nodeIndex++)
            {
                Vector2Int node = selectedNodes[nodeIndex];
                Vector3 nodePosition = layout.GetNodeWorldPosition(node);
                for (int edgeIndex = 0;
                     edgeIndex < sortedEdges.Count;
                     edgeIndex++)
                {
                    RoadEdge edge = sortedEdges[edgeIndex];
                    if (!edge.Contains(node) ||
                        layout.GetPathKind(edge) != CityPathKind.Street ||
                        !edgesWithSidewalks.Contains(edge))
                    {
                        continue;
                    }

                    Vector2Int other = edge.Other(node);
                    Vector3 otherPosition =
                        layout.GetNodeWorldPosition(other);
                    Vector3 outward =
                        (otherPosition - nodePosition).normalized;
                    Vector3 crosswalkCenter = nodePosition +
                        (outward *
                         (halfRoad + (CrosswalkDepth * 0.5f)));
                    Rect walkable = CreateOrientedRect(
                        crosswalkCenter,
                        outward,
                        CrosswalkDepth,
                        carriagewayWidth);
                    walkableRectangles.Add(walkable);
                    crosswalks.Add(new CityCrosswalkDescriptor(
                        node,
                        edge,
                        walkable,
                        crosswalkCenter,
                        outward,
                        new Vector3(-outward.z, 0f, outward.x)));
                    markingExclusions.Add(walkable);

                    for (int stripeIndex = 0;
                         stripeIndex < CrosswalkStripeCount;
                         stripeIndex++)
                    {
                        float distance = halfRoad +
                            (((stripeIndex + 0.5f) /
                              CrosswalkStripeCount) * CrosswalkDepth);
                        Vector3 center = nodePosition +
                            (outward * distance);
                        center.y = nodePosition.y +
                                   MarkingCenterAboveRoadBase;
                        Vector3 size = Mathf.Abs(outward.x) > 0.5f
                            ? new Vector3(
                                CrosswalkStripeDepth,
                                MarkingHeight,
                                carriagewayWidth)
                            : new Vector3(
                                carriagewayWidth,
                                MarkingHeight,
                                CrosswalkStripeDepth);
                        crosswalkMarkings.Add(new Bounds(center, size));
                    }
                }
            }
        }

        private static void CreateCenterMarkings(
            CityLayout layout,
            IReadOnlyList<RoadEdge> sortedEdges,
            IReadOnlyList<Rect> markingExclusions,
            ICollection<Bounds> centerMarkings)
        {
            for (int edgeIndex = 0;
                 edgeIndex < sortedEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = sortedEdges[edgeIndex];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                Vector3 start = layout.GetNodeWorldPosition(edge.A);
                Vector3 end = layout.GetNodeWorldPosition(edge.B);
                float length = Vector3.Distance(start, end);
                int dashCount = Mathf.Max(
                    2,
                    Mathf.FloorToInt(length / 5f));
                for (int dashIndex = 0;
                     dashIndex < dashCount;
                     dashIndex++)
                {
                    float t = (dashIndex + 0.5f) / dashCount;
                    Vector3 position = Vector3.Lerp(start, end, t);
                    position.y = start.y +
                                 MarkingCenterAboveRoadBase;
                    float dashLength = Mathf.Min(
                        MaximumCenterDashLength,
                        (length / dashCount) * 0.48f);
                    Vector3 size = edge.IsHorizontal
                        ? new Vector3(
                            dashLength,
                            MarkingHeight,
                            CenterDashWidth)
                        : new Vector3(
                            CenterDashWidth,
                            MarkingHeight,
                            dashLength);
                    var dash = new Bounds(position, size);
                    if (!OverlapsAny(
                            CreateRect(dash),
                            markingExclusions))
                    {
                        centerMarkings.Add(dash);
                    }
                }
            }
        }

        private static void AddSidewalk(
            Bounds sidewalk,
            ICollection<Bounds> sidewalks,
            ICollection<Rect> walkableRectangles)
        {
            sidewalks.Add(sidewalk);
            walkableRectangles.Add(CreateRect(sidewalk));
        }

        private static Rect CreateOrientedRect(
            Vector3 center,
            Vector3 direction,
            float alongLength,
            float acrossLength)
        {
            return Mathf.Abs(direction.x) > 0.5f
                ? new Rect(
                    center.x - (alongLength * 0.5f),
                    center.z - (acrossLength * 0.5f),
                    alongLength,
                    acrossLength)
                : new Rect(
                    center.x - (acrossLength * 0.5f),
                    center.z - (alongLength * 0.5f),
                    acrossLength,
                    alongLength);
        }

        private static Rect CreateRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static float ResolveEndpointInset(
            NodeConnections connections,
            float halfRoad)
        {
            if (connections.IsIntersectionCore)
            {
                return halfRoad;
            }

            return connections.Count == 1 ? -halfRoad : 0f;
        }

        private static bool OverlapsAny(
            Rect rectangle,
            IReadOnlyList<Rect> others)
        {
            for (int index = 0; index < others.Count; index++)
            {
                Rect other = others[index];
                if (Mathf.Min(rectangle.xMax, other.xMax) -
                        Mathf.Max(rectangle.xMin, other.xMin) >
                    GeometryTolerance &&
                    Mathf.Min(rectangle.yMax, other.yMax) -
                        Mathf.Max(rectangle.yMin, other.yMin) >
                    GeometryTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<Vector2Int, NodeConnections>
            CreateNodeConnections(CityLayout layout)
        {
            var connections =
                new Dictionary<Vector2Int, NodeConnections>(
                    layout.Nodes.Count);
            for (int index = 0; index < layout.Nodes.Count; index++)
            {
                connections.Add(
                    layout.Nodes[index],
                    new NodeConnections());
            }

            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                bool isStreet =
                    layout.GetPathKind(edge) == CityPathKind.Street;
                connections[edge.A].Add(edge.B - edge.A, isStreet);
                connections[edge.B].Add(edge.A - edge.B, isStreet);
            }

            return connections;
        }

        private static List<RoadEdge> CreateSortedEdges(CityLayout layout)
        {
            var edges = new List<RoadEdge>(layout.RoadEdges);
            edges.Sort(RoadEdge.Compare);
            return edges;
        }

        private static List<Vector2Int> CreateSortedNodes(CityLayout layout)
        {
            var nodes = new List<Vector2Int>(layout.Nodes);
            nodes.Sort(CompareNodes);
            return nodes;
        }

        private static int CompareNodes(Vector2Int left, Vector2Int right)
        {
            int xComparison = left.x.CompareTo(right.x);
            return xComparison != 0
                ? xComparison
                : left.y.CompareTo(right.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class NodeConnections
        {
            private readonly List<Vector2Int> directions =
                new List<Vector2Int>(4);

            public int Count => directions.Count;
            public int StreetCount { get; private set; }

            public bool IsIntersectionCore
            {
                get
                {
                    if (directions.Count >= 3)
                    {
                        return true;
                    }

                    return directions.Count == 2 &&
                           directions[0] + directions[1] !=
                           Vector2Int.zero;
                }
            }

            public void Add(Vector2Int direction, bool isStreet)
            {
                directions.Add(direction);
                if (isStreet)
                {
                    StreetCount++;
                }
            }

            public bool Contains(Vector2Int direction)
            {
                return directions.Contains(direction);
            }
        }
    }
}
