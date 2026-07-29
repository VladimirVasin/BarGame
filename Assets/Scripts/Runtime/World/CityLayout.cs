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
        private readonly ReadOnlyDictionary<RoadEdge, CityPathKind>
            readOnlyPathKinds;
        private readonly Dictionary<CityDistrictKind, CityDistrictDescriptor>
            districtsByKind;
        private bool hasValidated;

        internal CityLayout(
            int seed,
            Vector2Int blockCount,
            Vector2 nodeSpacing,
            Vector3 worldOrigin,
            float roadWidth,
            float minimumBarRouteDistance,
            IList<Vector2Int> nodes,
            IList<RoadEdge> roadEdges,
            IDictionary<RoadEdge, CityPathKind> pathKinds,
            IList<BuildingLot> buildingLots,
            IList<CityDistrictDescriptor> districts,
            CityParkPlan park,
            Vector2Int spawnNode)
        {
            Seed = seed;
            BlockCount = blockCount;
            NodeSpacing = nodeSpacing;
            WorldOrigin = worldOrigin;
            RoadWidth = roadWidth;
            MinimumBarRouteDistance = minimumBarRouteDistance;
            Nodes = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(nodes));
            RoadEdges = new ReadOnlyCollection<RoadEdge>(
                new List<RoadEdge>(roadEdges));
            BuildingLots = new ReadOnlyCollection<BuildingLot>(
                new List<BuildingLot>(buildingLots));
            Districts = new ReadOnlyCollection<CityDistrictDescriptor>(
                new List<CityDistrictDescriptor>(districts));
            Park = park ?? throw new ArgumentNullException(nameof(park));
            SpawnNode = spawnNode;
            SpawnWorldPosition = GetNodeWorldPosition(spawnNode);
            nodeSet = new HashSet<Vector2Int>(Nodes);
            edgeSet = new HashSet<RoadEdge>(RoadEdges);
            var copiedPathKinds =
                new Dictionary<RoadEdge, CityPathKind>(pathKinds);
            readOnlyPathKinds =
                new ReadOnlyDictionary<RoadEdge, CityPathKind>(
                    copiedPathKinds);
            districtsByKind =
                new Dictionary<CityDistrictKind, CityDistrictDescriptor>();
            for (int index = 0; index < Districts.Count; index++)
            {
                CityDistrictDescriptor district = Districts[index];
                if (district != null &&
                    !districtsByKind.ContainsKey(district.Kind))
                {
                    districtsByKind.Add(district.Kind, district);
                }
            }
        }

        public int Seed { get; }
        public Vector2Int BlockCount { get; }
        public Vector2 NodeSpacing { get; }
        public Vector3 WorldOrigin { get; }
        public float RoadWidth { get; }
        public float MinimumBarRouteDistance { get; }
        public IReadOnlyList<Vector2Int> Nodes { get; }
        public IReadOnlyList<RoadEdge> RoadEdges { get; }
        public IReadOnlyDictionary<RoadEdge, CityPathKind> PathKinds =>
            readOnlyPathKinds;
        public IReadOnlyList<BuildingLot> BuildingLots { get; }
        public IReadOnlyList<BuildingLot> Lots => BuildingLots;
        public IReadOnlyList<CityDistrictDescriptor> Districts { get; }
        public CityParkPlan Park { get; }
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

        public CityPathKind GetPathKind(RoadEdge edge)
        {
            if (!readOnlyPathKinds.TryGetValue(
                    edge,
                    out CityPathKind kind))
            {
                throw new ArgumentException(
                    "The requested edge does not belong to this layout.",
                    nameof(edge));
            }

            return kind;
        }

        public bool TryGetDistrict(
            CityDistrictKind kind,
            out CityDistrictDescriptor district)
        {
            return districtsByKind.TryGetValue(kind, out district);
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

        public IReadOnlyList<Rect> CreateStreetRects()
        {
            var rectangles = new List<Rect>(RoadEdges.Count);
            for (int index = 0; index < RoadEdges.Count; index++)
            {
                RoadEdge edge = RoadEdges[index];
                if (GetPathKind(edge) == CityPathKind.Street)
                {
                    rectangles.Add(GetRoadRect(edge));
                }
            }

            return new ReadOnlyCollection<Rect>(rectangles);
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
            if (hasValidated)
            {
                return;
            }

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

            if (readOnlyPathKinds.Count != RoadEdges.Count)
            {
                throw new InvalidOperationException(
                    "Every travel edge must define exactly one path kind.");
            }

            for (int index = 0; index < RoadEdges.Count; index++)
            {
                RoadEdge edge = RoadEdges[index];
                if (!nodeSet.Contains(edge.A) || !nodeSet.Contains(edge.B))
                {
                    throw new InvalidOperationException(
                        $"Road edge {edge} references an unknown node.");
                }

                CityPathKind kind = GetPathKind(edge);
                if (kind != CityPathKind.Street &&
                    kind != CityPathKind.ParkPath)
                {
                    throw new InvalidOperationException(
                        $"Road edge {edge} has an unsupported path kind.");
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
                    "The layout must contain one lot descriptor per block.");
            }

            var districtKinds = new HashSet<CityDistrictKind>();
            var districtCells =
                new Dictionary<Vector2Int, CityDistrictKind>();
            for (int index = 0; index < Districts.Count; index++)
            {
                CityDistrictDescriptor district = Districts[index];
                if (district == null ||
                    !districtKinds.Add(district.Kind) ||
                    district.Cells.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Districts must be non-null, non-empty and unique.");
                }

                for (int cellIndex = 0;
                     cellIndex < district.Cells.Count;
                     cellIndex++)
                {
                    Vector2Int cell = district.Cells[cellIndex];
                    if (!IsCellInsideGrid(cell) ||
                        !districtCells.TryAdd(cell, district.Kind))
                    {
                        throw new InvalidOperationException(
                            "District cells must cover unique in-grid blocks.");
                    }
                }
            }

            if (districtCells.Count != expectedLotCount)
            {
                throw new InvalidOperationException(
                    "Districts must cover every city block exactly once.");
            }

            var parkCells = new HashSet<Vector2Int>(Park.Cells);
            if (parkCells.Count != Park.Cells.Count)
            {
                throw new InvalidOperationException(
                    "Park cells must be unique.");
            }

            var cells = new HashSet<Vector2Int>();
            var barIds = new HashSet<string>(StringComparer.Ordinal);
            var bars = new List<BuildingLot>();
            for (int index = 0; index < BuildingLots.Count; index++)
            {
                BuildingLot lot = BuildingLots[index];
                if (lot == null || !cells.Add(lot.Cell))
                {
                    throw new InvalidOperationException(
                        "Building lots must be non-null and have unique cells.");
                }

                if (!districtCells.TryGetValue(
                        lot.Cell,
                        out CityDistrictKind district) ||
                    district != lot.District)
                {
                    throw new InvalidOperationException(
                        $"Lot {lot.Cell} does not match its district plan.");
                }

                bool isPlannedPark = parkCells.Contains(lot.Cell);
                if (lot.IsPark != isPlannedPark ||
                    lot.IsPark !=
                    (lot.District == CityDistrictKind.CentralPark))
                {
                    throw new InvalidOperationException(
                        $"Lot {lot.Cell} has inconsistent park land use.");
                }

                if (!lot.IsBar)
                {
                    if (lot.BarActivity != BarActivityKind.None)
                    {
                        throw new InvalidOperationException(
                            $"Non-bar lot {lot.Cell} cannot define a bar activity.");
                    }

                    continue;
                }

                if (!lot.HasBuilding ||
                    lot.District == CityDistrictKind.CentralPark)
                {
                    throw new InvalidOperationException(
                        $"Bar {lot.BarId} cannot occupy park land.");
                }

                if (lot.BarActivity != BarActivityKind.Cocktail &&
                    lot.BarActivity != BarActivityKind.BeerPong &&
                    lot.BarActivity != BarActivityKind.SplitTheG &&
                    lot.BarActivity != BarActivityKind.TinctureMatch)
                {
                    throw new InvalidOperationException(
                        $"Bar {lot.BarId} must define a supported activity.");
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

                if (GetPathKind(frontage) != CityPathKind.Street)
                {
                    throw new InvalidOperationException(
                        $"Bar {lot.BarId} must face a city street.");
                }

                Rect road = GetRoadRect(frontage);
                if (!ContainsInclusive(road, lot.ReturnPosition))
                {
                    throw new InvalidOperationException(
                        $"Bar {lot.BarId} has a return point outside its frontage road.");
                }

                bars.Add(lot);
            }

            bars.Sort(CompareLotsRowMajor);
            var firstBarDistricts = new HashSet<CityDistrictKind>();
            for (int ordinal = 0; ordinal < bars.Count; ordinal++)
            {
                BarActivityKind expected =
                    BarActivityAssignment.Resolve(ordinal);
                if (bars[ordinal].BarActivity != expected)
                {
                    throw new InvalidOperationException(
                        $"Bar {bars[ordinal].BarId} has activity " +
                        $"{bars[ordinal].BarActivity}, but row-major ordinal " +
                        $"{ordinal} requires {expected}.");
                }

                if (bars.Count <= 4 &&
                    !firstBarDistricts.Add(bars[ordinal].District))
                {
                    throw new InvalidOperationException(
                        "The first four bars must occupy different districts.");
                }
            }

            if (MinimumBarRouteDistance > 0f)
            {
                for (int first = 0; first < bars.Count; first++)
                {
                    for (int second = first + 1;
                         second < bars.Count;
                         second++)
                    {
                        float distance = CityTravelDistance.BetweenBars(
                            this,
                            bars[first],
                            bars[second]);
                        if (distance + 0.001f <
                            MinimumBarRouteDistance)
                        {
                            throw new InvalidOperationException(
                                $"Bars {bars[first].BarId} and " +
                                $"{bars[second].BarId} are only " +
                                $"{distance:0.##} m apart.");
                        }
                    }
                }
            }

            hasValidated = true;
        }

        private static int CompareLotsRowMajor(BuildingLot left, BuildingLot right)
        {
            int rowComparison = left.Cell.y.CompareTo(right.Cell.y);
            return rowComparison != 0
                ? rowComparison
                : left.Cell.x.CompareTo(right.Cell.x);
        }

        private static bool ContainsInclusive(Rect rectangle, Vector3 position)
        {
            return position.x >= rectangle.xMin &&
                   position.x <= rectangle.xMax &&
                   position.z >= rectangle.yMin &&
                   position.z <= rectangle.yMax;
        }

        private bool IsCellInsideGrid(Vector2Int cell)
        {
            return cell.x >= 0 &&
                   cell.x < BlockCount.x &&
                   cell.y >= 0 &&
                   cell.y < BlockCount.y;
        }
    }
}
