using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityRoutePathfinder
    {
        private const float DistanceEpsilon = 0.0001f;
        private const float PointEpsilonSquared = 0.000001f;

        public static CityRoutePath Build(
            CityLayout layout,
            Vector3 startWorldPosition,
            IReadOnlyList<BuildingLot> orderedStops)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (orderedStops == null)
            {
                throw new ArgumentNullException(nameof(orderedStops));
            }

            if (!IsFinite(startWorldPosition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startWorldPosition),
                    "The route start must contain only finite coordinates.");
            }

            if (orderedStops.Count == 0)
            {
                return new CityRoutePath(Array.Empty<Vector3>());
            }

            layout.ValidateOrThrow();
            Graph graph = Graph.Create(layout);
            RoadAnchor current = FindNearestAnchor(layout, startWorldPosition);
            var routePoints = new List<Vector3>();
            AppendSimplified(routePoints, startWorldPosition);
            AppendSimplified(routePoints, current.Position);

            for (int index = 0; index < orderedStops.Count; index++)
            {
                BuildingLot stop = ValidateStop(
                    layout,
                    orderedStops[index],
                    index);
                RoadAnchor target = CreateStopAnchor(layout, stop, index);
                List<Vector3> leg = BuildLeg(layout, graph, current, target);

                for (int pointIndex = 0; pointIndex < leg.Count; pointIndex++)
                {
                    AppendSimplified(routePoints, leg[pointIndex]);
                }

                current = target;
            }

            return new CityRoutePath(routePoints);
        }

        private static BuildingLot ValidateStop(
            CityLayout layout,
            BuildingLot stop,
            int index)
        {
            if (stop == null)
            {
                throw new ArgumentException(
                    $"Route stop at index {index} is null.",
                    "orderedStops");
            }

            bool belongsToLayout = false;
            for (int lotIndex = 0;
                 lotIndex < layout.BuildingLots.Count;
                 lotIndex++)
            {
                if (ReferenceEquals(layout.BuildingLots[lotIndex], stop))
                {
                    belongsToLayout = true;
                    break;
                }
            }

            if (!belongsToLayout)
            {
                throw new ArgumentException(
                    $"Route stop at index {index} does not belong to the layout.",
                    "orderedStops");
            }

            if (!stop.IsBar)
            {
                throw new ArgumentException(
                    $"Route stop at index {index} is not a bar.",
                    "orderedStops");
            }

            return stop;
        }

        private static RoadAnchor CreateStopAnchor(
            CityLayout layout,
            BuildingLot stop,
            int index)
        {
            if (!layout.TryGetFrontageEdge(stop, out RoadEdge edge))
            {
                throw new ArgumentException(
                    $"Route stop at index {index} has no frontage road.",
                    "orderedStops");
            }

            Vector3 projected = ProjectOntoEdge(
                layout,
                edge,
                stop.ReturnPosition);
            Vector3 returnPosition = stop.ReturnPosition;
            if ((projected - returnPosition).sqrMagnitude >
                PointEpsilonSquared)
            {
                throw new ArgumentException(
                    $"Route stop at index {index} is not on its frontage road.",
                    "orderedStops");
            }

            return new RoadAnchor(edge, returnPosition);
        }

        private static RoadAnchor FindNearestAnchor(
            CityLayout layout,
            Vector3 worldPosition)
        {
            if (layout.RoadEdges.Count == 0)
            {
                throw new InvalidOperationException(
                    "A route cannot be built without road edges.");
            }

            var edges = new List<RoadEdge>(layout.RoadEdges);
            edges.Sort(RoadEdge.Compare);

            RoadEdge bestEdge = edges[0];
            Vector3 bestPoint = ProjectOntoEdge(
                layout,
                bestEdge,
                worldPosition);
            float bestDistance = XzSquaredDistance(
                bestPoint,
                worldPosition);

            for (int index = 1; index < edges.Count; index++)
            {
                RoadEdge edge = edges[index];
                Vector3 candidate = ProjectOntoEdge(
                    layout,
                    edge,
                    worldPosition);
                float distance = XzSquaredDistance(
                    candidate,
                    worldPosition);
                if (distance + PointEpsilonSquared >= bestDistance)
                {
                    continue;
                }

                bestEdge = edge;
                bestPoint = candidate;
                bestDistance = distance;
            }

            return new RoadAnchor(bestEdge, bestPoint);
        }

        private static List<Vector3> BuildLeg(
            CityLayout layout,
            Graph graph,
            RoadAnchor start,
            RoadAnchor target)
        {
            PathCandidate best = default;
            bool hasBest = false;

            if (start.Edge == target.Edge)
            {
                var direct = new List<Vector3>();
                AppendSimplified(direct, start.Position);
                AppendSimplified(direct, target.Position);
                ConsiderCandidate(
                    new PathCandidate(direct),
                    ref best,
                    ref hasBest);
            }

            Vector2Int[] startNodes = { start.Edge.A, start.Edge.B };
            Vector2Int[] targetNodes = { target.Edge.A, target.Edge.B };
            for (int startIndex = 0; startIndex < startNodes.Length; startIndex++)
            {
                for (int targetIndex = 0;
                     targetIndex < targetNodes.Length;
                     targetIndex++)
                {
                    Vector2Int startNode = startNodes[startIndex];
                    Vector2Int targetNode = targetNodes[targetIndex];
                    NodePath nodePath = FindNodePath(
                        graph,
                        graph.GetIndex(startNode),
                        graph.GetIndex(targetNode));
                    var points = new List<Vector3>(
                        nodePath.NodeIndices.Count + 2);
                    AppendSimplified(points, start.Position);

                    for (int nodeIndex = 0;
                         nodeIndex < nodePath.NodeIndices.Count;
                         nodeIndex++)
                    {
                        AppendSimplified(
                            points,
                            graph.Positions[nodePath.NodeIndices[nodeIndex]]);
                    }

                    AppendSimplified(points, target.Position);
                    ConsiderCandidate(
                        new PathCandidate(points),
                        ref best,
                        ref hasBest);
                }
            }

            if (!hasBest)
            {
                throw new InvalidOperationException(
                    "The connected road graph did not produce a route leg.");
            }

            return best.Points;
        }

        private static void ConsiderCandidate(
            PathCandidate candidate,
            ref PathCandidate best,
            ref bool hasBest)
        {
            if (!hasBest ||
                candidate.Length < best.Length)
            {
                best = candidate;
                hasBest = true;
            }
        }

        private static NodePath FindNodePath(
            Graph graph,
            int startIndex,
            int targetIndex)
        {
            int count = graph.Positions.Length;
            var distances = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int index = 0; index < count; index++)
            {
                distances[index] = float.PositiveInfinity;
                previous[index] = -1;
            }

            distances[startIndex] = 0f;
            var pending = new NodeMinHeap(count);
            pending.Enqueue(startIndex, 0f);
            while (pending.TryDequeue(out int current))
            {
                if (visited[current])
                {
                    continue;
                }

                if (current == targetIndex)
                {
                    break;
                }

                visited[current] = true;
                List<Neighbour> neighbours = graph.Neighbours[current];
                for (int index = 0; index < neighbours.Count; index++)
                {
                    Neighbour neighbour = neighbours[index];
                    if (visited[neighbour.NodeIndex])
                    {
                        continue;
                    }

                    float candidate =
                        distances[current] + neighbour.Length;
                    if (candidate >= distances[neighbour.NodeIndex])
                    {
                        continue;
                    }

                    distances[neighbour.NodeIndex] = candidate;
                    previous[neighbour.NodeIndex] = current;
                    pending.Enqueue(neighbour.NodeIndex, candidate);
                }
            }

            if (float.IsPositiveInfinity(distances[targetIndex]))
            {
                throw new InvalidOperationException(
                    "The road graph is disconnected.");
            }

            var reversed = new List<int>();
            int cursor = targetIndex;
            while (cursor >= 0)
            {
                reversed.Add(cursor);
                if (cursor == startIndex)
                {
                    break;
                }

                cursor = previous[cursor];
            }

            if (reversed[reversed.Count - 1] != startIndex)
            {
                throw new InvalidOperationException(
                    "The road graph path could not be reconstructed.");
            }

            reversed.Reverse();
            return new NodePath(reversed);
        }

        private readonly struct NodeQueueEntry
        {
            public NodeQueueEntry(int nodeIndex, float distance)
            {
                NodeIndex = nodeIndex;
                Distance = distance;
            }

            public int NodeIndex { get; }
            public float Distance { get; }
        }

        private sealed class NodeMinHeap
        {
            private NodeQueueEntry[] entries;
            private int count;

            public NodeMinHeap(int capacity)
            {
                entries = new NodeQueueEntry[Mathf.Max(1, capacity)];
            }

            public void Enqueue(int nodeIndex, float distance)
            {
                EnsureCapacity();
                var entry = new NodeQueueEntry(nodeIndex, distance);
                int index = count;
                count++;

                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(entries[parent], entry) <= 0)
                    {
                        break;
                    }

                    entries[index] = entries[parent];
                    index = parent;
                }

                entries[index] = entry;
            }

            public bool TryDequeue(out int nodeIndex)
            {
                if (count == 0)
                {
                    nodeIndex = -1;
                    return false;
                }

                NodeQueueEntry result = entries[0];
                count--;
                if (count > 0)
                {
                    NodeQueueEntry replacement = entries[count];
                    int index = 0;
                    while (true)
                    {
                        int left = (index * 2) + 1;
                        if (left >= count)
                        {
                            break;
                        }

                        int right = left + 1;
                        int child =
                            right < count &&
                            Compare(entries[right], entries[left]) < 0
                                ? right
                                : left;
                        if (Compare(replacement, entries[child]) <= 0)
                        {
                            break;
                        }

                        entries[index] = entries[child];
                        index = child;
                    }

                    entries[index] = replacement;
                }

                nodeIndex = result.NodeIndex;
                return true;
            }

            private static int Compare(
                NodeQueueEntry left,
                NodeQueueEntry right)
            {
                if (left.Distance < right.Distance)
                {
                    return -1;
                }

                if (left.Distance > right.Distance)
                {
                    return 1;
                }

                // The former linear scan visited equal-distance nodes in
                // ascending graph-index order. Keep that route tie-break.
                return left.NodeIndex.CompareTo(right.NodeIndex);
            }

            private void EnsureCapacity()
            {
                if (count < entries.Length)
                {
                    return;
                }

                int capacity = checked(entries.Length * 2);
                Array.Resize(ref entries, capacity);
            }
        }

        private static Vector3 ProjectOntoEdge(
            CityLayout layout,
            RoadEdge edge,
            Vector3 point)
        {
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            Vector2 delta = new Vector2(
                end.x - start.x,
                end.z - start.z);
            Vector2 offset = new Vector2(
                point.x - start.x,
                point.z - start.z);
            float denominator = delta.sqrMagnitude;
            float amount = denominator <= PointEpsilonSquared
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(offset, delta) / denominator);
            return Vector3.Lerp(start, end, amount);
        }

        private static void AppendSimplified(
            List<Vector3> points,
            Vector3 point)
        {
            if (points.Count > 0 &&
                (points[points.Count - 1] - point).sqrMagnitude <=
                PointEpsilonSquared)
            {
                return;
            }

            points.Add(point);
            while (points.Count >= 3)
            {
                int last = points.Count - 1;
                Vector3 firstDirection =
                    points[last - 1] - points[last - 2];
                Vector3 secondDirection =
                    points[last] - points[last - 1];
                float magnitudeProduct = Mathf.Sqrt(
                    firstDirection.sqrMagnitude *
                    secondDirection.sqrMagnitude);
                float cross = Mathf.Abs(
                    (firstDirection.x * secondDirection.z) -
                    (firstDirection.z * secondDirection.x));
                bool sameDirection =
                    Vector3.Dot(firstDirection, secondDirection) > 0f;
                bool collinear =
                    magnitudeProduct > DistanceEpsilon &&
                    cross <= DistanceEpsilon * magnitudeProduct;
                if (!sameDirection || !collinear)
                {
                    break;
                }

                points.RemoveAt(last - 1);
            }
        }

        private static float XzSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z);
        }

        private static bool IsFinite(Vector3 point)
        {
            return IsFinite(point.x) &&
                   IsFinite(point.y) &&
                   IsFinite(point.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct RoadAnchor
        {
            public RoadAnchor(RoadEdge edge, Vector3 position)
            {
                Edge = edge;
                Position = position;
            }

            public RoadEdge Edge { get; }
            public Vector3 Position { get; }
        }

        private readonly struct PathCandidate
        {
            public PathCandidate(List<Vector3> points)
            {
                Points = points;
                float length = 0f;
                for (int index = 1; index < points.Count; index++)
                {
                    length += Vector3.Distance(
                        points[index - 1],
                        points[index]);
                }

                Length = length;
            }

            public List<Vector3> Points { get; }
            public float Length { get; }
        }

        private readonly struct NodePath
        {
            public NodePath(List<int> nodeIndices)
            {
                NodeIndices = nodeIndices;
            }

            public List<int> NodeIndices { get; }
        }

        private readonly struct Neighbour
        {
            public Neighbour(int nodeIndex, float length)
            {
                NodeIndex = nodeIndex;
                Length = length;
            }

            public int NodeIndex { get; }
            public float Length { get; }
        }

        private sealed class Graph
        {
            private readonly Dictionary<Vector2Int, int> indices;

            private Graph(
                Dictionary<Vector2Int, int> nodeIndices,
                Vector3[] positions,
                List<Neighbour>[] neighbours)
            {
                indices = nodeIndices;
                Positions = positions;
                Neighbours = neighbours;
            }

            public Vector3[] Positions { get; }
            public List<Neighbour>[] Neighbours { get; }

            public int GetIndex(Vector2Int node)
            {
                return indices[node];
            }

            public static Graph Create(CityLayout layout)
            {
                var nodes = new List<Vector2Int>(layout.Nodes);
                nodes.Sort(CompareNodes);
                var nodeIndices =
                    new Dictionary<Vector2Int, int>(nodes.Count);
                var positions = new Vector3[nodes.Count];
                var neighbours = new List<Neighbour>[nodes.Count];

                for (int index = 0; index < nodes.Count; index++)
                {
                    nodeIndices.Add(nodes[index], index);
                    positions[index] = layout.GetNodeWorldPosition(
                        nodes[index]);
                    neighbours[index] = new List<Neighbour>();
                }

                for (int index = 0;
                     index < layout.RoadEdges.Count;
                     index++)
                {
                    RoadEdge edge = layout.RoadEdges[index];
                    int first = nodeIndices[edge.A];
                    int second = nodeIndices[edge.B];
                    float length = Vector3.Distance(
                        positions[first],
                        positions[second]);
                    neighbours[first].Add(
                        new Neighbour(second, length));
                    neighbours[second].Add(
                        new Neighbour(first, length));
                }

                for (int index = 0; index < neighbours.Length; index++)
                {
                    neighbours[index].Sort(
                        (left, right) =>
                            left.NodeIndex.CompareTo(right.NodeIndex));
                }

                return new Graph(
                    nodeIndices,
                    positions,
                    neighbours);
            }

            private static int CompareNodes(
                Vector2Int left,
                Vector2Int right)
            {
                int xComparison = left.x.CompareTo(right.x);
                return xComparison != 0
                    ? xComparison
                    : left.y.CompareTo(right.y);
            }
        }
    }
}
