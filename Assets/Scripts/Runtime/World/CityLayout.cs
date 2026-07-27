using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityLayout
    {
        private readonly HashSet<Vector2Int> nodeSet;
        private readonly HashSet<RoadEdge> edgeSet;

        internal CityLayout(
            int seed,
            Vector2Int blockCount,
            Vector2 nodeSpacing,
            Vector3 worldOrigin,
            float roadWidth,
            IList<Vector2Int> nodes,
            IList<RoadEdge> roadEdges,
            IList<BuildingLot> buildingLots,
            Vector2Int spawnNode)
        {
            Seed = seed;
            BlockCount = blockCount;
            NodeSpacing = nodeSpacing;
            WorldOrigin = worldOrigin;
            RoadWidth = roadWidth;
            Nodes = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(nodes));
            RoadEdges = new ReadOnlyCollection<RoadEdge>(
                new List<RoadEdge>(roadEdges));
            BuildingLots = new ReadOnlyCollection<BuildingLot>(
                new List<BuildingLot>(buildingLots));
            SpawnNode = spawnNode;
            SpawnWorldPosition = GetNodeWorldPosition(spawnNode);
            nodeSet = new HashSet<Vector2Int>(Nodes);
            edgeSet = new HashSet<RoadEdge>(RoadEdges);
        }

        public int Seed { get; }
        public Vector2Int BlockCount { get; }
        public Vector2 NodeSpacing { get; }
        public Vector3 WorldOrigin { get; }
        public float RoadWidth { get; }
        public IReadOnlyList<Vector2Int> Nodes { get; }
        public IReadOnlyList<RoadEdge> RoadEdges { get; }
        public IReadOnlyList<BuildingLot> BuildingLots { get; }
        public IReadOnlyList<BuildingLot> Lots => BuildingLots;
        public Vector2Int SpawnNode { get; }
        public Vector3 SpawnWorldPosition { get; }

        public Vector3 GetNodeWorldPosition(Vector2Int node)
        {
            if (node.x < 0 || node.x > BlockCount.x ||
                node.y < 0 || node.y > BlockCount.y)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(node),
                    $"Node {node} lies outside the city grid.");
            }

            return WorldOrigin + new Vector3(
                node.x * NodeSpacing.x,
                0f,
                node.y * NodeSpacing.y);
        }

        public bool HasRoad(Vector2Int first, Vector2Int second)
        {
            int distance =
                Mathf.Abs(first.x - second.x) +
                Mathf.Abs(first.y - second.y);
            return distance == 1 && edgeSet.Contains(new RoadEdge(first, second));
        }

        public bool HasRoad(RoadEdge edge)
        {
            return edgeSet.Contains(edge);
        }

        public Rect GetRoadRect(RoadEdge edge)
        {
            if (!edgeSet.Contains(edge))
            {
                throw new ArgumentException(
                    "The requested edge does not belong to this layout.",
                    nameof(edge));
            }

            Vector3 first = GetNodeWorldPosition(edge.A);
            Vector3 second = GetNodeWorldPosition(edge.B);
            float halfWidth = RoadWidth * 0.5f;
            return Rect.MinMaxRect(
                Mathf.Min(first.x, second.x) - halfWidth,
                Mathf.Min(first.z, second.z) - halfWidth,
                Mathf.Max(first.x, second.x) + halfWidth,
                Mathf.Max(first.z, second.z) + halfWidth);
        }

        public IReadOnlyList<Rect> CreateRoadRects()
        {
            var rectangles = new Rect[RoadEdges.Count];
            for (int index = 0; index < RoadEdges.Count; index++)
            {
                rectangles[index] = GetRoadRect(RoadEdges[index]);
            }

            return Array.AsReadOnly(rectangles);
        }

        public bool TryGetFrontageEdge(BuildingLot lot, out RoadEdge edge)
        {
            if (lot != null && lot.HasRoadFrontage)
            {
                RoadEdge candidate = RoadEdge.ForCellFrontage(
                    lot.Cell,
                    lot.FrontageDirection);
                if (edgeSet.Contains(candidate))
                {
                    edge = candidate;
                    return true;
                }
            }

            edge = default;
            return false;
        }

        public bool IsRoadGraphConnected()
        {
            if (Nodes.Count == 0)
            {
                return false;
            }

            var visited = new HashSet<Vector2Int>();
            var pending = new Queue<Vector2Int>();
            pending.Enqueue(Nodes[0]);
            visited.Add(Nodes[0]);

            while (pending.Count > 0)
            {
                Vector2Int current = pending.Dequeue();
                for (int index = 0; index < RoadEdges.Count; index++)
                {
                    RoadEdge edge = RoadEdges[index];
                    if (!edge.Contains(current))
                    {
                        continue;
                    }

                    Vector2Int neighbour = edge.Other(current);
                    if (visited.Add(neighbour))
                    {
                        pending.Enqueue(neighbour);
                    }
                }
            }

            return visited.Count == Nodes.Count;
        }

        public void ValidateOrThrow()
        {
            int expectedNodeCount =
                checked((BlockCount.x + 1) * (BlockCount.y + 1));
            if (Nodes.Count != expectedNodeCount || nodeSet.Count != Nodes.Count)
            {
                throw new InvalidOperationException(
                    "The layout does not contain exactly one copy of every grid node.");
            }

            if (edgeSet.Count != RoadEdges.Count)
            {
                throw new InvalidOperationException("The layout contains duplicate road edges.");
            }

            for (int index = 0; index < RoadEdges.Count; index++)
            {
                RoadEdge edge = RoadEdges[index];
                if (!nodeSet.Contains(edge.A) || !nodeSet.Contains(edge.B))
                {
                    throw new InvalidOperationException(
                        $"Road edge {edge} references an unknown node.");
                }
            }

            if (!nodeSet.Contains(SpawnNode) || !IsRoadGraphConnected())
            {
                throw new InvalidOperationException(
                    "The spawn node must belong to one connected road graph.");
            }

            int expectedLotCount = checked(BlockCount.x * BlockCount.y);
            if (BuildingLots.Count != expectedLotCount)
            {
                throw new InvalidOperationException(
                    "The layout must contain one building lot per block.");
            }

            var cells = new HashSet<Vector2Int>();
            var barIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < BuildingLots.Count; index++)
            {
                BuildingLot lot = BuildingLots[index];
                if (lot == null || !cells.Add(lot.Cell))
                {
                    throw new InvalidOperationException(
                        "Building lots must be non-null and have unique cells.");
                }

                if (!lot.IsBar)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(lot.BarId) || !barIds.Add(lot.BarId))
                {
                    throw new InvalidOperationException(
                        "Every bar must have a unique stable ID.");
                }

                if (!TryGetFrontageEdge(lot, out RoadEdge frontage))
                {
                    throw new InvalidOperationException(
                        $"Bar {lot.BarId} has no matching road frontage.");
                }

                Rect road = GetRoadRect(frontage);
                if (!ContainsInclusive(road, lot.ReturnPosition))
                {
                    throw new InvalidOperationException(
                        $"Bar {lot.BarId} has a return point outside its frontage road.");
                }
            }
        }

        private static bool ContainsInclusive(Rect rectangle, Vector3 position)
        {
            return position.x >= rectangle.xMin &&
                   position.x <= rectangle.xMax &&
                   position.z >= rectangle.yMin &&
                   position.z <= rectangle.yMax;
        }
    }
}
