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
        private readonly Dictionary<
            Vector2Int,
            CityDistrictPointOfInterestDescriptor> districtPointsByCell;
        private readonly ReadOnlyDictionary<CityDistrictKind, Vector2Int>
            readOnlyPrimaryLandmarkCells;
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
            IList<CityDistrictPointOfInterestDescriptor>
                districtPointsOfInterest,
            IDictionary<CityDistrictKind, Vector2Int>
                primaryLandmarkCells,
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
            for (int index = 0; index < BuildingLots.Count; index++)
            {
                BuildingLot lot = BuildingLots[index];
                if (lot?.IsPlayerHome == true)
                {
                    PlayerHome = lot;
                }

                if (lot?.IsSupermarket == true)
                {
                    Supermarket = lot;
                }
            }

            Districts = new ReadOnlyCollection<CityDistrictDescriptor>(
                new List<CityDistrictDescriptor>(districts));
            Park = park ?? throw new ArgumentNullException(nameof(park));
            DistrictPointsOfInterest =
                new ReadOnlyCollection<
                    CityDistrictPointOfInterestDescriptor>(
                    new List<
                        CityDistrictPointOfInterestDescriptor>(
                        districtPointsOfInterest ??
                        throw new ArgumentNullException(
                            nameof(districtPointsOfInterest))));
            districtPointsByCell =
                new Dictionary<
                    Vector2Int,
                    CityDistrictPointOfInterestDescriptor>();
            for (int index = 0;
                 index < DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    DistrictPointsOfInterest[index];
                if (point != null &&
                    !districtPointsByCell.ContainsKey(point.Cell))
                {
                    districtPointsByCell.Add(point.Cell, point);
                }
            }

            readOnlyPrimaryLandmarkCells =
                new ReadOnlyDictionary<CityDistrictKind, Vector2Int>(
                    new Dictionary<CityDistrictKind, Vector2Int>(
                        primaryLandmarkCells ??
                        throw new ArgumentNullException(
                            nameof(primaryLandmarkCells))));
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
        public BuildingLot PlayerHome { get; }
        public BuildingLot Supermarket { get; }
        public IReadOnlyList<CityDistrictDescriptor> Districts { get; }
        public CityParkPlan Park { get; }
        public IReadOnlyList<CityDistrictPointOfInterestDescriptor>
            DistrictPointsOfInterest { get; }
        public IReadOnlyDictionary<CityDistrictKind, Vector2Int>
            PrimaryLandmarkCells => readOnlyPrimaryLandmarkCells;
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

        public bool TryGetDistrictPointOfInterest(
            Vector2Int cell,
            out CityDistrictPointOfInterestDescriptor point)
        {
            return districtPointsByCell.TryGetValue(cell, out point);
        }

        public bool TryGetPrimaryLandmarkCell(
            CityDistrictKind district,
            out Vector2Int cell)
        {
            return readOnlyPrimaryLandmarkCells.TryGetValue(
                district,
                out cell);
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
            var lotsByCell = new Dictionary<Vector2Int, BuildingLot>();
            var barIds = new HashSet<string>(StringComparer.Ordinal);
            var bars = new List<BuildingLot>();
            BuildingLot playerHome = null;
            BuildingLot supermarket = null;
            for (int index = 0; index < BuildingLots.Count; index++)
            {
                BuildingLot lot = BuildingLots[index];
                if (lot == null || !cells.Add(lot.Cell))
                {
                    throw new InvalidOperationException(
                        "Building lots must be non-null and have unique cells.");
                }

                lotsByCell.Add(lot.Cell, lot);

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

                if (!lot.HasBuilding &&
                    !lot.IsPark &&
                    !lot.IsDistrictPointOfInterest)
                {
                    throw new InvalidOperationException(
                        $"Lot {lot.Cell} has unsupported land use.");
                }

                if (lot.IsDistrictPointOfInterest &&
                    lot.District == CityDistrictKind.CentralPark)
                {
                    throw new InvalidOperationException(
                        "District points of interest must occupy urban land.");
                }

                if (!lot.IsBar)
                {
                    if (lot.BarActivity != BarActivityKind.None)
                    {
                        throw new InvalidOperationException(
                            $"Non-bar lot {lot.Cell} cannot define a bar activity.");
                    }

                    if (lot.IsSupermarket)
                    {
                        if (lot.IsPlayerHome ||
                            supermarket != null ||
                            !lot.HasBuilding ||
                            lot.District ==
                            CityDistrictKind.CentralPark)
                        {
                            throw new InvalidOperationException(
                                "The city can contain at most one " +
                                "supermarket on ordinary urban land.");
                        }

                        if (!TryGetFrontageEdge(
                                lot,
                                out RoadEdge supermarketFrontage) ||
                            GetPathKind(supermarketFrontage) !=
                            CityPathKind.Street)
                        {
                            throw new InvalidOperationException(
                                "The supermarket must face a city street.");
                        }

                        Rect supermarketRoad =
                            GetRoadRect(supermarketFrontage);
                        if (!ContainsInclusive(
                                supermarketRoad,
                                lot.ReturnPosition))
                        {
                            throw new InvalidOperationException(
                                "The supermarket return point must lie on " +
                                "its frontage road.");
                        }

                        supermarket = lot;
                        continue;
                    }

                    if (!lot.IsPlayerHome)
                    {
                        continue;
                    }

                    if (playerHome != null ||
                        !lot.HasBuilding ||
                        lot.District ==
                        CityDistrictKind.CentralPark)
                    {
                        throw new InvalidOperationException(
                            "The city can contain at most one player home " +
                            "on buildable urban land.");
                    }

                    if (!TryGetFrontageEdge(
                            lot,
                            out RoadEdge homeFrontage) ||
                        GetPathKind(homeFrontage) !=
                        CityPathKind.Street)
                    {
                        throw new InvalidOperationException(
                            "The player home must face a city street.");
                    }

                    Rect homeRoad = GetRoadRect(homeFrontage);
                    if (!ContainsInclusive(
                            homeRoad,
                            lot.ReturnPosition))
                    {
                        throw new InvalidOperationException(
                            "The player home return point must lie on " +
                            "its frontage road.");
                    }

                    playerHome = lot;
                    continue;
                }

                if (lot.IsPlayerHome || lot.IsSupermarket)
                {
                    throw new InvalidOperationException(
                        "A building lot cannot combine a bar with " +
                        "another special building role.");
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

            ValidatePrimaryLandmarkCells(lotsByCell);
            ValidateDistrictPointsOfInterest(lotsByCell);

            if (!ReferenceEquals(PlayerHome, playerHome))
            {
                throw new InvalidOperationException(
                    "The player home descriptor is inconsistent.");
            }

            if (!ReferenceEquals(Supermarket, supermarket))
            {
                throw new InvalidOperationException(
                    "The supermarket descriptor is inconsistent.");
            }

            if (playerHome != null)
            {
                if (bars.Count == 0)
                {
                    throw new InvalidOperationException(
                        "The player home requires at least one bar.");
                }

                TryGetFrontageEdge(
                    playerHome,
                    out RoadEdge homeFrontage);
                if (!homeFrontage.Contains(SpawnNode))
                {
                    throw new InvalidOperationException(
                        "A fresh session must spawn on the road beside " +
                        "the player home.");
                }

                float nearestBarDistance = float.PositiveInfinity;
                for (int index = 0; index < bars.Count; index++)
                {
                    TryGetFrontageEdge(
                        bars[index],
                        out RoadEdge barFrontage);
                    nearestBarDistance = Mathf.Min(
                        nearestBarDistance,
                        CityTravelDistance.BetweenAnchors(
                            Nodes,
                            RoadEdges,
                            GetNodeWorldPosition,
                            homeFrontage,
                            playerHome.ReturnPosition,
                            barFrontage,
                            bars[index].ReturnPosition));
                }

                if (nearestBarDistance >
                    CityLayoutGenerator
                        .MaximumHomeBarRouteDistance +
                    0.001f)
                {
                    throw new InvalidOperationException(
                        "The player home must be within " +
                        $"{CityLayoutGenerator.MaximumHomeBarRouteDistance:0.##} m " +
                        "of a bar by traversable street distance.");
                }
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

        private void ValidatePrimaryLandmarkCells(
            IReadOnlyDictionary<Vector2Int, BuildingLot> lotsByCell)
        {
            for (int index = 0; index < Districts.Count; index++)
            {
                CityDistrictKind district = Districts[index].Kind;
                if (district == CityDistrictKind.CentralPark)
                {
                    continue;
                }

                if (!readOnlyPrimaryLandmarkCells.TryGetValue(
                        district,
                        out Vector2Int cell) ||
                    !lotsByCell.TryGetValue(
                        cell,
                        out BuildingLot lot) ||
                    !lot.HasBuilding ||
                    lot.District != district ||
                    lot.IsSupermarket)
                {
                    throw new InvalidOperationException(
                        $"District '{district}' must reserve one " +
                        "buildable primary landmark cell.");
                }
            }

            foreach (KeyValuePair<CityDistrictKind, Vector2Int> pair
                     in readOnlyPrimaryLandmarkCells)
            {
                if (pair.Key == CityDistrictKind.CentralPark ||
                    !lotsByCell.TryGetValue(
                        pair.Value,
                        out BuildingLot lot) ||
                    lot.District != pair.Key ||
                    !lot.HasBuilding ||
                    lot.IsSupermarket)
                {
                    throw new InvalidOperationException(
                        "Primary landmark reservations must reference " +
                        "buildable cells in their urban district.");
                }
            }
        }

        private void ValidateDistrictPointsOfInterest(
            IReadOnlyDictionary<Vector2Int, BuildingLot> lotsByCell)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var cells = new HashSet<Vector2Int>();
            var districts = new HashSet<CityDistrictKind>();
            var accessIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                 index < DistrictPointsOfInterest.Count;
                 index++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    DistrictPointsOfInterest[index];
                if (point == null ||
                    string.IsNullOrEmpty(point.Id) ||
                    !ids.Add(point.Id) ||
                    !cells.Add(point.Cell) ||
                    !districts.Add(point.District))
                {
                    throw new InvalidOperationException(
                        "District points of interest must be non-null and " +
                        "have unique IDs, cells and districts.");
                }

                if (!lotsByCell.TryGetValue(
                        point.Cell,
                        out BuildingLot lot) ||
                    !lot.IsDistrictPointOfInterest ||
                    lot.HasBuilding ||
                    lot.IsPark ||
                    lot.IsBar ||
                    lot.IsPlayerHome ||
                    lot.IsSupermarket ||
                    lot.District != point.District ||
                    ResolvePointOfInterestKind(point.District) != point.Kind)
                {
                    throw new InvalidOperationException(
                        $"District point '{point.Id}' does not match its " +
                        "reserved urban lot.");
                }

                if (point.Center != lot.Center ||
                    !IsFinitePositive(point.PublicBounds) ||
                    point.PublicBounds.width <
                        CityLayoutGenerator
                            .MinimumDistrictPointLotDimension ||
                    point.PublicBounds.height <
                        CityLayoutGenerator
                            .MinimumDistrictPointLotDimension ||
                    !ContainsInclusive(point.PublicBounds, point.Center) ||
                    point.Accesses.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"District point '{point.Id}' must define usable " +
                        "finite public ground and at least one street " +
                        "access.");
                }

                var accessDirections = new HashSet<Vector2Int>();
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    CityDistrictPointOfInterestAccessDescriptor access =
                        point.Accesses[accessIndex];
                    Vector2Int direction = access.StreetSideDirection;
                    Vector3 expectedOutward = new Vector3(
                        -direction.x,
                        0f,
                        -direction.y);
                    RoadEdge expectedEdge =
                        RoadEdge.ForCellFrontage(
                            point.Cell,
                            direction);
                    if (string.IsNullOrEmpty(access.Id) ||
                        !accessIds.Add(access.Id) ||
                        Mathf.Abs(direction.x) + Mathf.Abs(direction.y) != 1 ||
                        !accessDirections.Add(direction) ||
                        access.FrontageEdge != expectedEdge ||
                        !HasRoad(expectedEdge) ||
                        GetPathKind(expectedEdge) != CityPathKind.Street ||
                        (access.OutwardNormal - expectedOutward)
                            .sqrMagnitude > 0.0001f ||
                        !IsFinite(access.Width) ||
                        access.Width <= 0f ||
                        !IsFinitePositive(access.ApproachBounds) ||
                        !ContainsInclusive(
                            access.ApproachBounds,
                            access.Center) ||
                        !ContainsInclusive(
                            point.PublicBounds,
                            access.Center))
                    {
                        throw new InvalidOperationException(
                            $"District point access '{access.Id}' is not " +
                            "a valid open street approach.");
                    }

                    Rect street = GetRoadRect(expectedEdge);
                    float approachDepth = direction.x != 0
                        ? access.ApproachBounds.width
                        : access.ApproachBounds.height;
                    float sampleDistance = Mathf.Min(
                        0.5f,
                        Mathf.Min(
                            RoadWidth * 0.25f,
                            approachDepth * 0.25f));
                    Vector3 roadSample = access.Center -
                                         (access.OutwardNormal *
                                          sampleDistance);
                    Vector3 publicSample = access.Center +
                                           (access.OutwardNormal *
                                            sampleDistance);
                    if (!ContainsInclusive(street, roadSample) ||
                        !ContainsInclusive(
                            access.ApproachBounds,
                            roadSample) ||
                        !ContainsInclusive(
                            point.PublicBounds,
                            publicSample) ||
                        !ContainsInclusive(
                            access.ApproachBounds,
                            publicSample))
                    {
                        throw new InvalidOperationException(
                            $"District point access '{access.Id}' must " +
                            "overlap both its street and public ground.");
                    }
                }
            }

            int publicLotCount = 0;
            foreach (BuildingLot lot in BuildingLots)
            {
                if (lot.IsDistrictPointOfInterest)
                {
                    publicLotCount++;
                    if (!cells.Contains(lot.Cell))
                    {
                        throw new InvalidOperationException(
                            $"Public lot {lot.Cell} has no district-point " +
                            "descriptor.");
                    }
                }
            }

            if (publicLotCount != DistrictPointsOfInterest.Count ||
                districtPointsByCell.Count !=
                DistrictPointsOfInterest.Count)
            {
                throw new InvalidOperationException(
                    "District-point lots and descriptors must match " +
                    "one-to-one.");
            }
        }

        private static CityDistrictPointOfInterestKind
            ResolvePointOfInterestKind(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return CityDistrictPointOfInterestKind
                        .OldTownWaterworksCourt;
                case CityDistrictKind.Residential:
                    return CityDistrictPointOfInterestKind
                        .ResidentialDryingYard;
                case CityDistrictKind.Industrial:
                    return CityDistrictPointOfInterestKind
                        .IndustrialWeighbridge;
                case CityDistrictKind.Nightlife:
                    return CityDistrictPointOfInterestKind
                        .NightlifeLastRouteIsland;
                default:
                    throw new InvalidOperationException(
                        $"District '{district}' cannot own a district point " +
                        "of interest.");
            }
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

        private static bool IsFinitePositive(Rect rectangle)
        {
            return IsFinite(rectangle.xMin) &&
                   IsFinite(rectangle.xMax) &&
                   IsFinite(rectangle.yMin) &&
                   IsFinite(rectangle.yMax) &&
                   rectangle.width > 0f &&
                   rectangle.height > 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
