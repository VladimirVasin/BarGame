using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityTravelDistance
    {
        private const float PointEpsilonSquared = 0.000001f;

        public static float BetweenBars(
            CityLayout layout,
            BuildingLot first,
            BuildingLot second)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            RoadEdge firstEdge = ValidateBar(
                layout,
                first,
                nameof(first));
            RoadEdge secondEdge = ValidateBar(
                layout,
                second,
                nameof(second));

            return BetweenAnchors(
                layout.Nodes,
                layout.RoadEdges,
                layout.GetNodeWorldPosition,
                firstEdge,
                first.ReturnPosition,
                secondEdge,
                second.ReturnPosition);
        }

        internal static float BetweenAnchors(
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> edges,
            Func<Vector2Int, Vector3> getNodeWorldPosition,
            RoadEdge firstEdge,
            Vector3 firstAnchor,
            RoadEdge secondEdge,
            Vector3 secondAnchor)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (edges == null)
            {
                throw new ArgumentNullException(nameof(edges));
            }

            if (getNodeWorldPosition == null)
            {
                throw new ArgumentNullException(
                    nameof(getNodeWorldPosition));
            }

            WeightedGraph graph = WeightedGraph.Create(
                nodes,
                edges,
                getNodeWorldPosition);
            Vector3 validatedFirstAnchor = ValidateAnchor(
                graph,
                firstEdge,
                firstAnchor,
                nameof(firstEdge),
                nameof(firstAnchor));
            Vector3 validatedSecondAnchor = ValidateAnchor(
                graph,
                secondEdge,
                secondAnchor,
                nameof(secondEdge),
                nameof(secondAnchor));

            float[] distances = CalculateDistances(
                graph,
                firstEdge,
                validatedFirstAnchor);
            float best = float.PositiveInfinity;
            if (firstEdge == secondEdge)
            {
                best = Vector3.Distance(
                    validatedFirstAnchor,
                    validatedSecondAnchor);
            }

            ConsiderEndpoint(
                graph,
                distances,
                secondEdge.A,
                validatedSecondAnchor,
                ref best);
            ConsiderEndpoint(
                graph,
                distances,
                secondEdge.B,
                validatedSecondAnchor,
                ref best);

            if (float.IsPositiveInfinity(best))
            {
                throw new InvalidOperationException(
                    "The road graph does not connect the two anchors.");
            }

            return best;
        }

        private static RoadEdge ValidateBar(
            CityLayout layout,
            BuildingLot lot,
            string parameterName)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            bool belongsToLayout = false;
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                if (ReferenceEquals(layout.BuildingLots[index], lot))
                {
                    belongsToLayout = true;
                    break;
                }
            }

            if (!belongsToLayout)
            {
                throw new ArgumentException(
                    "The bar does not belong to the supplied layout.",
                    parameterName);
            }

            if (!lot.IsBar)
            {
                throw new ArgumentException(
                    "The supplied building lot is not a bar.",
                    parameterName);
            }

            if (!layout.TryGetFrontageEdge(lot, out RoadEdge edge))
            {
                throw new ArgumentException(
                    "The supplied bar has no frontage road in the layout.",
                    parameterName);
            }

            return edge;
        }

        private static Vector3 ValidateAnchor(
            WeightedGraph graph,
            RoadEdge edge,
            Vector3 anchor,
            string edgeParameterName,
            string anchorParameterName)
        {
            if (!graph.Contains(edge))
            {
                throw new ArgumentException(
                    "The anchor edge does not belong to the road graph.",
                    edgeParameterName);
            }

            if (!IsFinite(anchor))
            {
                throw new ArgumentOutOfRangeException(
                    anchorParameterName,
                    "A road anchor must contain only finite coordinates.");
            }

            anchor = Flatten(anchor);
            Vector3 start = graph.GetPosition(edge.A);
            Vector3 end = graph.GetPosition(edge.B);
            Vector3 projected = ProjectOntoSegment(anchor, start, end);
            if ((projected - anchor).sqrMagnitude >
                PointEpsilonSquared)
            {
                throw new ArgumentException(
                    "A road anchor must lie on its supplied edge.",
                    anchorParameterName);
            }

            return anchor;
        }

        private static float[] CalculateDistances(
            WeightedGraph graph,
            RoadEdge firstEdge,
            Vector3 firstAnchor)
        {
            int count = graph.NodeCount;
            var distances = new float[count];
            var visited = new bool[count];
            for (int index = 0; index < count; index++)
            {
                distances[index] = float.PositiveInfinity;
            }

            var pending = new NodeMinHeap(count);
            SetMinimum(
                distances,
                graph.GetIndex(firstEdge.A),
                Vector3.Distance(
                    firstAnchor,
                    graph.GetPosition(firstEdge.A)),
                pending);
            SetMinimum(
                distances,
                graph.GetIndex(firstEdge.B),
                Vector3.Distance(
                    firstAnchor,
                    graph.GetPosition(firstEdge.B)),
                pending);

            while (pending.TryDequeue(out int current))
            {
                if (visited[current])
                {
                    continue;
                }

                visited[current] = true;
                IReadOnlyList<Neighbour> neighbours =
                    graph.GetNeighbours(current);
                for (int index = 0; index < neighbours.Count; index++)
                {
                    Neighbour neighbour = neighbours[index];
                    if (visited[neighbour.NodeIndex])
                    {
                        continue;
                    }

                    float candidate =
                        distances[current] + neighbour.Length;
                    if (candidate < distances[neighbour.NodeIndex])
                    {
                        distances[neighbour.NodeIndex] = candidate;
                        pending.Enqueue(
                            neighbour.NodeIndex,
                            candidate);
                    }
                }
            }

            return distances;
        }

        private static void ConsiderEndpoint(
            WeightedGraph graph,
            float[] distances,
            Vector2Int node,
            Vector3 targetAnchor,
            ref float best)
        {
            int index = graph.GetIndex(node);
            if (float.IsPositiveInfinity(distances[index]))
            {
                return;
            }

            float candidate =
                distances[index] +
                Vector3.Distance(
                    graph.GetPosition(node),
                    targetAnchor);
            if (candidate < best)
            {
                best = candidate;
            }
        }

        private static void SetMinimum(
            float[] distances,
            int index,
            float value,
            NodeMinHeap pending)
        {
            if (value < distances[index])
            {
                distances[index] = value;
                pending.Enqueue(index, value);
            }
        }

        private static Vector3 ProjectOntoSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 delta = end - start;
            float denominator = delta.sqrMagnitude;
            float amount = denominator <= PointEpsilonSquared
                ? 0f
                : Mathf.Clamp01(
                    Vector3.Dot(point - start, delta) / denominator);
            return start + (delta * amount);
        }

        private static Vector3 Flatten(Vector3 point)
        {
            point.y = 0f;
            return point;
        }

        private static bool IsFinite(Vector3 point)
        {
            return IsFinite(point.x) &&
                   IsFinite(point.y) &&
                   IsFinite(point.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
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

                return left.NodeIndex.CompareTo(right.NodeIndex);
            }

            private void EnsureCapacity()
            {
                if (count < entries.Length)
                {
                    return;
                }

                Array.Resize(
                    ref entries,
                    checked(entries.Length * 2));
            }
        }

        private sealed class WeightedGraph
        {
            private readonly Dictionary<Vector2Int, int> indices;
            private readonly HashSet<RoadEdge> edgeSet;
            private readonly Vector3[] positions;
            private readonly List<Neighbour>[] neighbours;

            private WeightedGraph(
                Dictionary<Vector2Int, int> indices,
                HashSet<RoadEdge> edgeSet,
                Vector3[] positions,
                List<Neighbour>[] neighbours)
            {
                this.indices = indices;
                this.edgeSet = edgeSet;
                this.positions = positions;
                this.neighbours = neighbours;
            }

            public int NodeCount => positions.Length;

            public bool Contains(RoadEdge edge)
            {
                return edgeSet.Contains(edge);
            }

            public int GetIndex(Vector2Int node)
            {
                return indices[node];
            }

            public Vector3 GetPosition(Vector2Int node)
            {
                return positions[GetIndex(node)];
            }

            public IReadOnlyList<Neighbour> GetNeighbours(int index)
            {
                return neighbours[index];
            }

            public static WeightedGraph Create(
                IReadOnlyList<Vector2Int> nodes,
                IReadOnlyList<RoadEdge> edges,
                Func<Vector2Int, Vector3> getNodeWorldPosition)
            {
                if (nodes.Count == 0)
                {
                    throw new ArgumentException(
                        "A road graph must contain at least one node.",
                        nameof(nodes));
                }

                var orderedNodes = new List<Vector2Int>(nodes.Count);
                for (int index = 0; index < nodes.Count; index++)
                {
                    orderedNodes.Add(nodes[index]);
                }

                orderedNodes.Sort(CompareNodes);
                var indices = new Dictionary<Vector2Int, int>(
                    orderedNodes.Count);
                var positions = new Vector3[orderedNodes.Count];
                var neighbours =
                    new List<Neighbour>[orderedNodes.Count];
                for (int index = 0;
                     index < orderedNodes.Count;
                     index++)
                {
                    Vector2Int node = orderedNodes[index];
                    if (indices.ContainsKey(node))
                    {
                        throw new ArgumentException(
                            "Road graph nodes must be unique.",
                            nameof(nodes));
                    }

                    Vector3 position = getNodeWorldPosition(node);
                    if (!IsFinite(position))
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(getNodeWorldPosition),
                            "Node world positions must be finite.");
                    }

                    indices.Add(node, index);
                    positions[index] = Flatten(position);
                    neighbours[index] = new List<Neighbour>();
                }

                var edgeSet = new HashSet<RoadEdge>();
                for (int index = 0; index < edges.Count; index++)
                {
                    RoadEdge edge = edges[index];
                    if (!edgeSet.Add(edge))
                    {
                        throw new ArgumentException(
                            "Road graph edges must be unique.",
                            nameof(edges));
                    }

                    if (!indices.TryGetValue(
                            edge.A,
                            out int firstIndex) ||
                        !indices.TryGetValue(
                            edge.B,
                            out int secondIndex))
                    {
                        throw new ArgumentException(
                            "Every road edge must reference known nodes.",
                            nameof(edges));
                    }

                    float length = Vector3.Distance(
                        positions[firstIndex],
                        positions[secondIndex]);
                    if (!IsFinite(length) || length <= 0f)
                    {
                        throw new ArgumentException(
                            "Every road edge must have a positive finite " +
                            "world length.",
                            nameof(edges));
                    }

                    neighbours[firstIndex].Add(
                        new Neighbour(secondIndex, length));
                    neighbours[secondIndex].Add(
                        new Neighbour(firstIndex, length));
                }

                for (int index = 0; index < neighbours.Length; index++)
                {
                    neighbours[index].Sort(
                        (left, right) =>
                            left.NodeIndex.CompareTo(right.NodeIndex));
                }

                return new WeightedGraph(
                    indices,
                    edgeSet,
                    positions,
                    neighbours);
            }
        }
    }
}
