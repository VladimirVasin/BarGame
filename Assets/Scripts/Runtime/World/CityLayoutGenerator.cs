using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityLayoutGenerator
    {
        public const float MaximumHomeBarRouteDistance = 48f;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.down,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left
        };

        private static readonly CityDistrictKind[] UrbanDistrictOrder =
        {
            CityDistrictKind.OldTown,
            CityDistrictKind.Residential,
            CityDistrictKind.Industrial,
            CityDistrictKind.Nightlife
        };

        public static CityLayout Generate(CityGenerationSettings settings, int seed)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            CityGenerationSettings snapshot = settings.Copy();
            List<Vector2Int> nodes = CreateNodes(snapshot);
            List<RoadEdge> allEdges = CreateAllEdges(snapshot);
            List<RoadEdge> roads = CreateRoadGraph(snapshot, seed, allEdges);
            EnsureEveryBlockHasFrontage(snapshot, seed, roads);
            roads.Sort(RoadEdge.Compare);

            Vector3 origin = new Vector3(
                -(snapshot.BlocksX * snapshot.NodeSpacing.x) * 0.5f,
                0f,
                -(snapshot.BlocksZ * snapshot.NodeSpacing.y) * 0.5f);
            Dictionary<RoadEdge, CityPathKind> pathKinds =
                CreatePathKinds(snapshot, roads);
            CityParkPlan park =
                CreateParkPlan(snapshot, seed, origin);
            List<BuildingLot> lots = CreateBuildingLots(
                snapshot,
                seed,
                origin,
                nodes,
                roads,
                pathKinds);
            List<CityDistrictDescriptor> districts =
                CreateDistricts(snapshot, lots);
            Vector2Int spawnNode =
                ResolveInitialSpawnNode(snapshot, lots);

            var layout = new CityLayout(
                seed,
                snapshot.BlockCount,
                snapshot.NodeSpacing,
                origin,
                snapshot.RoadWidth,
                snapshot.MinimumBarRouteDistance,
                nodes,
                roads,
                pathKinds,
                lots,
                districts,
                park,
                spawnNode);
            layout.ValidateOrThrow();
            return layout;
        }

        private static Vector2Int ResolveInitialSpawnNode(
            CityGenerationSettings settings,
            IReadOnlyList<BuildingLot> lots)
        {
            var fallback = new Vector2Int(
                settings.BlocksX / 2,
                settings.BlocksZ / 2);

            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                if (!lot.IsPlayerHome)
                {
                    continue;
                }

                RoadEdge frontage = RoadEdge.ForCellFrontage(
                    lot.Cell,
                    lot.FrontageDirection);
                return frontage.A;
            }

            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                if (!lot.IsBar)
                {
                    continue;
                }

                RoadEdge frontage = RoadEdge.ForCellFrontage(
                    lot.Cell,
                    lot.FrontageDirection);
                return frontage.A;
            }

            return fallback;
        }

        private static List<Vector2Int> CreateNodes(CityGenerationSettings settings)
        {
            var nodes = new List<Vector2Int>(
                checked((settings.BlocksX + 1) * (settings.BlocksZ + 1)));
            for (int z = 0; z <= settings.BlocksZ; z++)
            {
                for (int x = 0; x <= settings.BlocksX; x++)
                {
                    nodes.Add(new Vector2Int(x, z));
                }
            }

            return nodes;
        }

        private static List<RoadEdge> CreateAllEdges(CityGenerationSettings settings)
        {
            int horizontalCount = settings.BlocksX * (settings.BlocksZ + 1);
            int verticalCount = (settings.BlocksX + 1) * settings.BlocksZ;
            var edges = new List<RoadEdge>(horizontalCount + verticalCount);

            for (int z = 0; z <= settings.BlocksZ; z++)
            {
                for (int x = 0; x <= settings.BlocksX; x++)
                {
                    Vector2Int node = new Vector2Int(x, z);
                    if (x < settings.BlocksX)
                    {
                        edges.Add(new RoadEdge(node, node + Vector2Int.right));
                    }

                    if (z < settings.BlocksZ)
                    {
                        edges.Add(new RoadEdge(node, node + Vector2Int.up));
                    }
                }
            }

            return edges;
        }

        private static List<RoadEdge> CreateRoadGraph(
            CityGenerationSettings settings,
            int seed,
            List<RoadEdge> allEdges)
        {
            var shuffled = new List<RoadEdge>(allEdges);
            var random = new DeterministicRandom(
                StableHash(seed, 0x47524150u));
            Shuffle(shuffled, ref random);

            int nodeWidth = settings.BlocksX + 1;
            int nodeCount = checked(nodeWidth * (settings.BlocksZ + 1));
            var sets = new DisjointSet(nodeCount);
            var roads = new List<RoadEdge>(nodeCount - 1);
            var roadSet = new HashSet<RoadEdge>();

            List<RoadEdge> requiredEdges =
                CreateRequiredEdges(settings);
            for (int index = 0; index < requiredEdges.Count; index++)
            {
                RoadEdge edge = requiredEdges[index];
                if (roadSet.Add(edge))
                {
                    roads.Add(edge);
                }

                sets.Union(
                    ToNodeIndex(edge.A, nodeWidth),
                    ToNodeIndex(edge.B, nodeWidth));
            }

            for (int index = 0; index < shuffled.Count; index++)
            {
                RoadEdge edge = shuffled[index];
                int first = ToNodeIndex(edge.A, nodeWidth);
                int second = ToNodeIndex(edge.B, nodeWidth);
                if (!sets.Union(first, second))
                {
                    continue;
                }

                roads.Add(edge);
                roadSet.Add(edge);
            }

            var loopRandom = new DeterministicRandom(
                StableHash(seed, 0x4C4F4F50u));
            for (int index = 0; index < allEdges.Count; index++)
            {
                RoadEdge edge = allEdges[index];
                if (roadSet.Contains(edge) ||
                    loopRandom.NextFloat() >= settings.LoopChance)
                {
                    continue;
                }

                roads.Add(edge);
                roadSet.Add(edge);
            }

            return roads;
        }

        private static List<RoadEdge> CreateRequiredEdges(
            CityGenerationSettings settings)
        {
            var required = new List<RoadEdge>();
            var unique = new HashSet<RoadEdge>();
            int centerX = settings.BlocksX / 2;
            int centerZ = settings.BlocksZ / 2;

            for (int x = 0; x < settings.BlocksX; x++)
            {
                AddRequiredEdge(
                    required,
                    unique,
                    new RoadEdge(
                        new Vector2Int(x, centerZ),
                        new Vector2Int(x + 1, centerZ)));
            }

            for (int z = 0; z < settings.BlocksZ; z++)
            {
                AddRequiredEdge(
                    required,
                    unique,
                    new RoadEdge(
                        new Vector2Int(centerX, z),
                        new Vector2Int(centerX, z + 1)));
            }

            Vector2Int parkCount = settings.EffectiveParkBlockCount;
            if (parkCount == Vector2Int.zero)
            {
                return required;
            }

            Vector2Int minimum = settings.ParkCellMinimum;
            Vector2Int maximum = minimum + parkCount;
            for (int x = minimum.x; x < maximum.x; x++)
            {
                AddRequiredEdge(
                    required,
                    unique,
                    new RoadEdge(
                        new Vector2Int(x, minimum.y),
                        new Vector2Int(x + 1, minimum.y)));
                AddRequiredEdge(
                    required,
                    unique,
                    new RoadEdge(
                        new Vector2Int(x, maximum.y),
                        new Vector2Int(x + 1, maximum.y)));
            }

            for (int z = minimum.y; z < maximum.y; z++)
            {
                AddRequiredEdge(
                    required,
                    unique,
                    new RoadEdge(
                        new Vector2Int(minimum.x, z),
                        new Vector2Int(minimum.x, z + 1)));
                AddRequiredEdge(
                    required,
                    unique,
                    new RoadEdge(
                        new Vector2Int(maximum.x, z),
                        new Vector2Int(maximum.x, z + 1)));
            }

            return required;
        }

        private static void AddRequiredEdge(
            ICollection<RoadEdge> target,
            ISet<RoadEdge> unique,
            RoadEdge edge)
        {
            if (unique.Add(edge))
            {
                target.Add(edge);
            }
        }

        private static void EnsureEveryBlockHasFrontage(
            CityGenerationSettings settings,
            int seed,
            List<RoadEdge> roads)
        {
            var roadSet = new HashSet<RoadEdge>(roads);
            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (settings.IsParkCell(cell) ||
                        HasAnyFrontage(cell, roadSet))
                    {
                        continue;
                    }

                    uint hash = StableHash(seed, x, z, 0x46524F4Eu);
                    int start = (int)(hash % (uint)CardinalDirections.Length);
                    RoadEdge added = RoadEdge.ForCellFrontage(
                        cell,
                        CardinalDirections[start]);
                    roads.Add(added);
                    roadSet.Add(added);
                }
            }
        }

        private static Dictionary<RoadEdge, CityPathKind> CreatePathKinds(
            CityGenerationSettings settings,
            IReadOnlyList<RoadEdge> roads)
        {
            var result =
                new Dictionary<RoadEdge, CityPathKind>(roads.Count);
            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                result.Add(
                    edge,
                    IsInteriorParkEdge(settings, edge)
                        ? CityPathKind.ParkPath
                        : CityPathKind.Street);
            }

            return result;
        }

        private static bool IsInteriorParkEdge(
            CityGenerationSettings settings,
            RoadEdge edge)
        {
            Vector2Int count = settings.EffectiveParkBlockCount;
            if (count == Vector2Int.zero)
            {
                return false;
            }

            Vector2Int minimum = settings.ParkCellMinimum;
            Vector2Int maximum = minimum + count;
            if (edge.IsHorizontal)
            {
                int z = edge.A.y;
                return z > minimum.y &&
                       z < maximum.y &&
                       edge.A.x >= minimum.x &&
                       edge.B.x <= maximum.x;
            }

            int x = edge.A.x;
            return x > minimum.x &&
                   x < maximum.x &&
                   edge.A.y >= minimum.y &&
                   edge.B.y <= maximum.y;
        }

        private static List<BuildingLot> CreateBuildingLots(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyList<Vector2Int> nodes,
            List<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            int lotCount = checked(settings.BlocksX * settings.BlocksZ);
            var roadSet = new HashSet<RoadEdge>(roads);
            var frontages = new Vector2Int[lotCount];
            var barCandidates = new List<BarCandidate>(lotCount);

            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    int lotIndex = ToLotIndex(x, z, settings.BlocksX);
                    Vector2Int cell = new Vector2Int(x, z);
                    if (settings.IsParkCell(cell))
                    {
                        frontages[lotIndex] = Vector2Int.zero;
                        continue;
                    }

                    frontages[lotIndex] = ChooseFrontage(
                        cell,
                        seed,
                        roadSet,
                        pathKinds);
                    if (frontages[lotIndex] != Vector2Int.zero)
                    {
                        barCandidates.Add(CreateBarCandidate(
                            settings,
                            seed,
                            origin,
                            lotIndex,
                            cell,
                            frontages[lotIndex]));
                    }
                }
            }

            if (barCandidates.Count < settings.BarCount)
            {
                throw new InvalidOperationException(
                    "The generated road graph has too few accessible bar lots.");
            }

            HashSet<int> barLots = SelectBarLots(
                settings,
                origin,
                nodes,
                roads,
                barCandidates);
            int homeLotIndex = SelectHomeLot(
                settings,
                seed,
                origin,
                nodes,
                roads,
                pathKinds,
                roadSet,
                frontages,
                barCandidates,
                barLots,
                out Vector2Int homeFrontage);
            if (homeLotIndex >= 0)
            {
                frontages[homeLotIndex] = homeFrontage;
            }

            var lots = new List<BuildingLot>(lotCount);
            int barOrdinal = 0;
            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    int lotIndex = ToLotIndex(x, z, settings.BlocksX);
                    bool isBar = barLots.Contains(lotIndex);
                    bool isPlayerHome = lotIndex == homeLotIndex;
                    BarActivityKind barActivity = BarActivityKind.None;
                    if (isBar)
                    {
                        barActivity =
                            BarActivityAssignment.Resolve(barOrdinal);
                        barOrdinal++;
                    }

                    lots.Add(CreateBuildingLot(
                        settings,
                        seed,
                        origin,
                        new Vector2Int(x, z),
                        frontages[lotIndex],
                        isBar,
                        isPlayerHome,
                        barActivity));
                }
            }

            return lots;
        }

        private static int SelectHomeLot(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            ISet<RoadEdge> roadSet,
            IReadOnlyList<Vector2Int> frontages,
            IReadOnlyList<BarCandidate> barCandidates,
            ISet<int> barLots,
            out Vector2Int homeFrontage)
        {
            homeFrontage = Vector2Int.zero;
            if (barLots.Count == 0)
            {
                return -1;
            }

            bool found = false;
            int bestLotIndex = -1;
            int bestDistrictPenalty = int.MaxValue;
            uint bestRank = uint.MaxValue;
            Vector2Int bestFrontage = Vector2Int.zero;

            for (int barIndex = 0;
                 barIndex < barCandidates.Count;
                 barIndex++)
            {
                BarCandidate bar = barCandidates[barIndex];
                if (!barLots.Contains(bar.LotIndex))
                {
                    continue;
                }

                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    RoadEdge sharedRoad =
                        RoadEdge.ForCellFrontage(
                            bar.Cell,
                            direction);
                    Vector2Int homeCell = bar.Cell + direction;
                    if (!roadSet.Contains(sharedRoad) ||
                        pathKinds[sharedRoad] !=
                        CityPathKind.Street ||
                        !IsCellInsideGrid(settings, homeCell) ||
                        settings.IsParkCell(homeCell))
                    {
                        continue;
                    }

                    int lotIndex = ToLotIndex(
                        homeCell.x,
                        homeCell.y,
                        settings.BlocksX);
                    if (barLots.Contains(lotIndex))
                    {
                        continue;
                    }

                    int districtPenalty =
                        ResolveDistrict(settings, homeCell) ==
                        CityDistrictKind.Residential
                            ? 0
                            : 1;
                    uint rank = StableHash(
                        seed,
                        homeCell.x,
                        homeCell.y,
                        0x484F4D45u);
                    if (!found ||
                        districtPenalty < bestDistrictPenalty ||
                        (districtPenalty == bestDistrictPenalty &&
                         rank < bestRank))
                    {
                        found = true;
                        bestLotIndex = lotIndex;
                        bestDistrictPenalty = districtPenalty;
                        bestRank = rank;
                        bestFrontage = -direction;
                    }
                }
            }

            if (found)
            {
                homeFrontage = bestFrontage;
                return bestLotIndex;
            }

            float bestDistance = float.PositiveInfinity;
            for (int lotIndex = 0;
                 lotIndex < frontages.Count;
                 lotIndex++)
            {
                Vector2Int frontage = frontages[lotIndex];
                if (frontage == Vector2Int.zero ||
                    barLots.Contains(lotIndex))
                {
                    continue;
                }

                Vector2Int cell = new Vector2Int(
                    lotIndex % settings.BlocksX,
                    lotIndex / settings.BlocksX);
                if (settings.IsParkCell(cell))
                {
                    continue;
                }

                RoadEdge homeRoad =
                    RoadEdge.ForCellFrontage(cell, frontage);
                Vector3 homeReturn = GetReturnPosition(
                    settings,
                    origin,
                    cell,
                    frontage);
                for (int barIndex = 0;
                     barIndex < barCandidates.Count;
                     barIndex++)
                {
                    BarCandidate bar = barCandidates[barIndex];
                    if (!barLots.Contains(bar.LotIndex))
                    {
                        continue;
                    }

                    float distance =
                        CityTravelDistance.BetweenAnchors(
                            nodes,
                            roads,
                            node => GetNodeWorldPosition(
                                settings,
                                origin,
                                node),
                            homeRoad,
                            homeReturn,
                            bar.Frontage,
                            bar.ReturnPosition);
                    uint rank = StableHash(
                        seed,
                        cell.x,
                        cell.y,
                        0x484F4D45u);
                    if (distance < bestDistance - 0.001f ||
                        (Mathf.Abs(distance - bestDistance) <=
                         0.001f &&
                         rank < bestRank))
                    {
                        bestDistance = distance;
                        bestLotIndex = lotIndex;
                        bestRank = rank;
                        bestFrontage = frontage;
                    }
                }
            }

            if (bestDistance >
                MaximumHomeBarRouteDistance + 0.001f)
            {
                return -1;
            }

            homeFrontage = bestFrontage;
            return bestLotIndex;
        }

        private static BarCandidate CreateBarCandidate(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            int lotIndex,
            Vector2Int cell,
            Vector2Int frontage)
        {
            Vector3 center = GetLotCenter(settings, origin, cell);
            Vector3 direction =
                new Vector3(frontage.x, 0f, frontage.y);
            float roadDistance =
                frontage.x != 0
                    ? settings.NodeSpacing.x * 0.5f
                    : settings.NodeSpacing.y * 0.5f;
            return new BarCandidate(
                lotIndex,
                cell,
                ResolveDistrict(settings, cell),
                RoadEdge.ForCellFrontage(cell, frontage),
                center + (direction * roadDistance),
                StableHash(seed, cell.x, cell.y, 0x42415253u));
        }

        private static HashSet<int> SelectBarLots(
            CityGenerationSettings settings,
            Vector3 origin,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyList<BarCandidate> candidates)
        {
            var selected = new List<BarCandidate>(settings.BarCount);
            var selectedLots = new HashSet<int>();
            var selectedDistricts = new HashSet<CityDistrictKind>();
            Vector3 spawn = GetNodeWorldPosition(
                settings,
                origin,
                new Vector2Int(
                    settings.BlocksX / 2,
                    settings.BlocksZ / 2));

            for (int ordinal = 0;
                 ordinal < settings.BarCount;
                 ordinal++)
            {
                CityDistrictKind? requiredDistrict =
                    ordinal < UrbanDistrictOrder.Length
                        ? FindUnrepresentedDistrict(
                            candidates,
                            selectedLots,
                            selectedDistricts)
                        : null;
                BarCandidate best = default;
                bool found = false;
                float bestScore = float.NegativeInfinity;

                for (int index = 0; index < candidates.Count; index++)
                {
                    BarCandidate candidate = candidates[index];
                    if (selectedLots.Contains(candidate.LotIndex) ||
                        (requiredDistrict.HasValue &&
                         candidate.District != requiredDistrict.Value))
                    {
                        continue;
                    }

                    float score = selected.Count == 0
                        ? XzSquaredDistance(candidate.ReturnPosition, spawn)
                        : MinimumDistanceToSelected(
                            nodes,
                            roads,
                            settings,
                            origin,
                            candidate,
                            selected);
                    if (!found ||
                        score > bestScore + 0.001f ||
                        (Mathf.Abs(score - bestScore) <= 0.001f &&
                         candidate.Rank < best.Rank))
                    {
                        found = true;
                        best = candidate;
                        bestScore = score;
                    }
                }

                if (!found)
                {
                    throw new InvalidOperationException(
                        "No accessible lot satisfies the district bar plan.");
                }

                if (selected.Count > 0 &&
                    bestScore + 0.001f <
                    settings.MinimumBarRouteDistance)
                {
                    throw new InvalidOperationException(
                        $"Cannot place {settings.BarCount} bars at least " +
                        $"{settings.MinimumBarRouteDistance:0.##} m apart.");
                }

                selected.Add(best);
                selectedLots.Add(best.LotIndex);
                selectedDistricts.Add(best.District);
            }

            return selectedLots;
        }

        private static CityDistrictKind? FindUnrepresentedDistrict(
            IReadOnlyList<BarCandidate> candidates,
            ISet<int> selectedLots,
            ISet<CityDistrictKind> selectedDistricts)
        {
            for (int districtIndex = 0;
                 districtIndex < UrbanDistrictOrder.Length;
                 districtIndex++)
            {
                CityDistrictKind district =
                    UrbanDistrictOrder[districtIndex];
                if (selectedDistricts.Contains(district))
                {
                    continue;
                }

                for (int candidateIndex = 0;
                     candidateIndex < candidates.Count;
                     candidateIndex++)
                {
                    BarCandidate candidate = candidates[candidateIndex];
                    if (candidate.District == district &&
                        !selectedLots.Contains(candidate.LotIndex))
                    {
                        return district;
                    }
                }
            }

            return null;
        }

        private static float MinimumDistanceToSelected(
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            CityGenerationSettings settings,
            Vector3 origin,
            BarCandidate candidate,
            IReadOnlyList<BarCandidate> selected)
        {
            float minimum = float.PositiveInfinity;
            for (int index = 0; index < selected.Count; index++)
            {
                BarCandidate other = selected[index];
                float distance = CityTravelDistance.BetweenAnchors(
                    nodes,
                    roads,
                    node => GetNodeWorldPosition(settings, origin, node),
                    candidate.Frontage,
                    candidate.ReturnPosition,
                    other.Frontage,
                    other.ReturnPosition);
                minimum = Mathf.Min(minimum, distance);
            }

            return minimum;
        }

        private static BuildingLot CreateBuildingLot(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            Vector2Int cell,
            Vector2Int frontage,
            bool isBar,
            bool isPlayerHome,
            BarActivityKind barActivity)
        {
            var random = new DeterministicRandom(
                StableHash(seed, cell.x, cell.y, 0x4C4F5453u));
            CityDistrictKind district = ResolveDistrict(settings, cell);
            CityLandUseKind landUse = settings.IsParkCell(cell)
                ? CityLandUseKind.Park
                : CityLandUseKind.Building;
            float maximumWidth = settings.BlockWidth - (settings.BuildingInset * 2f);
            float maximumDepth = settings.BlockDepth - (settings.BuildingInset * 2f);
            Vector2 size = landUse == CityLandUseKind.Park
                ? new Vector2(settings.BlockWidth, settings.BlockDepth)
                : isPlayerHome
                    ? new Vector2(
                        Mathf.Min(13f, maximumWidth),
                        Mathf.Min(12f, maximumDepth))
                : CreateBuildingSize(
                    district,
                    maximumWidth,
                    maximumDepth,
                    ref random);
            float height = landUse == CityLandUseKind.Park
                ? 0.1f
                : isPlayerHome
                    ? PlayerHomeBalconyGeometry.ResolveBuildingHeight(
                        settings)
                : CreateBuildingHeight(settings, district, ref random);
            Vector3 center = GetLotCenter(settings, origin, cell);

            Vector3 direction = new Vector3(frontage.x, 0f, frontage.y);
            float buildingHalfDistance =
                frontage.x != 0 ? size.x * 0.5f : size.y * 0.5f;
            float roadDistance =
                frontage.x != 0
                    ? settings.NodeSpacing.x * 0.5f
                    : settings.NodeSpacing.y * 0.5f;
            Vector3 doorPosition = center + (direction * buildingHalfDistance);
            Vector3 returnPosition = center + (direction * roadDistance);
            Color color = CreateBuildingColor(
                ref random,
                isBar,
                isPlayerHome,
                district);
            string barId = isBar
                ? $"bar-{unchecked((uint)seed):x8}-{cell.x:D2}-{cell.y:D2}"
                : string.Empty;

            return new BuildingLot(
                cell,
                center,
                size,
                height,
                color,
                district,
                landUse,
                isBar,
                isPlayerHome,
                barId,
                barActivity,
                frontage,
                doorPosition,
                returnPosition);
        }

        private static Color CreateBuildingColor(
            ref DeterministicRandom random,
            bool isBar,
            bool isPlayerHome,
            CityDistrictKind district)
        {
            if (isBar)
            {
                return new Color(
                    random.Range(0.62f, 0.88f),
                    random.Range(0.18f, 0.34f),
                    random.Range(0.12f, 0.26f),
                    1f);
            }

            if (isPlayerHome)
            {
                return new Color(
                    random.Range(0.28f, 0.36f),
                    random.Range(0.48f, 0.58f),
                    random.Range(0.52f, 0.64f),
                    1f);
            }

            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return new Color(
                        random.Range(0.42f, 0.58f),
                        random.Range(0.34f, 0.46f),
                        random.Range(0.26f, 0.36f),
                        1f);
                case CityDistrictKind.Residential:
                    return new Color(
                        random.Range(0.34f, 0.48f),
                        random.Range(0.43f, 0.56f),
                        random.Range(0.48f, 0.62f),
                        1f);
                case CityDistrictKind.Industrial:
                    return new Color(
                        random.Range(0.30f, 0.42f),
                        random.Range(0.34f, 0.43f),
                        random.Range(0.32f, 0.39f),
                        1f);
                case CityDistrictKind.Nightlife:
                    return new Color(
                        random.Range(0.34f, 0.52f),
                        random.Range(0.22f, 0.34f),
                        random.Range(0.42f, 0.60f),
                        1f);
                default:
                    return new Color(0.20f, 0.34f, 0.22f, 1f);
            }
        }

        private static Vector3 GetReturnPosition(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int cell,
            Vector2Int frontage)
        {
            Vector3 center = GetLotCenter(settings, origin, cell);
            Vector3 direction =
                new Vector3(frontage.x, 0f, frontage.y);
            float roadDistance =
                frontage.x != 0
                    ? settings.NodeSpacing.x * 0.5f
                    : settings.NodeSpacing.y * 0.5f;
            return center + (direction * roadDistance);
        }

        private static bool IsCellInsideGrid(
            CityGenerationSettings settings,
            Vector2Int cell)
        {
            return cell.x >= 0 &&
                   cell.x < settings.BlocksX &&
                   cell.y >= 0 &&
                   cell.y < settings.BlocksZ;
        }

        private static Vector2 CreateBuildingSize(
            CityDistrictKind district,
            float maximumWidth,
            float maximumDepth,
            ref DeterministicRandom random)
        {
            float minimumScale;
            float maximumScale;
            switch (district)
            {
                case CityDistrictKind.Residential:
                    minimumScale = 0.76f;
                    maximumScale = 0.90f;
                    break;
                case CityDistrictKind.Nightlife:
                    minimumScale = 0.84f;
                    maximumScale = 0.96f;
                    break;
                default:
                    minimumScale = 0.92f;
                    maximumScale = 0.99f;
                    break;
            }

            return new Vector2(
                maximumWidth *
                random.Range(minimumScale, maximumScale),
                maximumDepth *
                random.Range(minimumScale, maximumScale));
        }

        private static float CreateBuildingHeight(
            CityGenerationSettings settings,
            CityDistrictKind district,
            ref DeterministicRandom random)
        {
            float minimumT;
            float maximumT;
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    minimumT = 0.28f;
                    maximumT = 0.72f;
                    break;
                case CityDistrictKind.Residential:
                    minimumT = 0.18f;
                    maximumT = 0.58f;
                    break;
                case CityDistrictKind.Industrial:
                    minimumT = 0f;
                    maximumT = 0.32f;
                    break;
                case CityDistrictKind.Nightlife:
                    minimumT = 0.56f;
                    maximumT = 1f;
                    break;
                default:
                    minimumT = 0f;
                    maximumT = 1f;
                    break;
            }

            return Mathf.Lerp(
                settings.MinimumBuildingHeight,
                settings.MaximumBuildingHeight,
                random.Range(minimumT, maximumT));
        }

        private static Vector3 GetLotCenter(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int cell)
        {
            return origin + new Vector3(
                (cell.x + 0.5f) * settings.NodeSpacing.x,
                0f,
                (cell.y + 0.5f) * settings.NodeSpacing.y);
        }

        private static Vector3 GetNodeWorldPosition(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int node)
        {
            return origin + new Vector3(
                node.x * settings.NodeSpacing.x,
                0f,
                node.y * settings.NodeSpacing.y);
        }

        private static CityDistrictKind ResolveDistrict(
            CityGenerationSettings settings,
            Vector2Int cell)
        {
            if (settings.IsParkCell(cell))
            {
                return CityDistrictKind.CentralPark;
            }

            bool east = cell.x >= settings.BlocksX / 2;
            bool north = cell.y >= settings.BlocksZ / 2;
            if (north)
            {
                return east
                    ? CityDistrictKind.Residential
                    : CityDistrictKind.OldTown;
            }

            return east
                ? CityDistrictKind.Nightlife
                : CityDistrictKind.Industrial;
        }

        private static List<CityDistrictDescriptor> CreateDistricts(
            CityGenerationSettings settings,
            IReadOnlyList<BuildingLot> lots)
        {
            var cellsByDistrict =
                new Dictionary<CityDistrictKind, List<Vector2Int>>();
            var boundsByDistrict =
                new Dictionary<CityDistrictKind, Bounds>();

            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                if (!cellsByDistrict.TryGetValue(
                        lot.District,
                        out List<Vector2Int> cells))
                {
                    cells = new List<Vector2Int>();
                    cellsByDistrict.Add(lot.District, cells);
                }

                cells.Add(lot.Cell);
                var cellBounds = new Bounds(
                    lot.Center,
                    new Vector3(
                        settings.NodeSpacing.x,
                        1f,
                        settings.NodeSpacing.y));
                if (boundsByDistrict.TryGetValue(
                        lot.District,
                        out Bounds districtBounds))
                {
                    districtBounds.Encapsulate(cellBounds);
                    boundsByDistrict[lot.District] = districtBounds;
                }
                else
                {
                    boundsByDistrict.Add(lot.District, cellBounds);
                }
            }

            var result = new List<CityDistrictDescriptor>(
                cellsByDistrict.Count);
            for (int kindValue = 0;
                 kindValue <= (int)CityDistrictKind.CentralPark;
                 kindValue++)
            {
                var kind = (CityDistrictKind)kindValue;
                if (!cellsByDistrict.TryGetValue(
                        kind,
                        out List<Vector2Int> cells))
                {
                    continue;
                }

                result.Add(new CityDistrictDescriptor(
                    kind,
                    cells,
                    boundsByDistrict[kind]));
            }

            return result;
        }

        private static CityParkPlan CreateParkPlan(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin)
        {
            Vector2Int count = settings.EffectiveParkBlockCount;
            if (count == Vector2Int.zero)
            {
                return new CityParkPlan(
                    Array.Empty<Vector2Int>(),
                    new Rect(),
                    Vector3.zero,
                    Array.Empty<CityParkGateDescriptor>(),
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>());
            }

            Vector2Int minimum = settings.ParkCellMinimum;
            Vector2Int maximum = minimum + count;
            var cells = new List<Vector2Int>(
                checked(count.x * count.y));
            for (int z = minimum.y; z < maximum.y; z++)
            {
                for (int x = minimum.x; x < maximum.x; x++)
                {
                    cells.Add(new Vector2Int(x, z));
                }
            }

            Vector3 worldMinimum =
                GetNodeWorldPosition(settings, origin, minimum);
            Vector3 worldMaximum =
                GetNodeWorldPosition(settings, origin, maximum);
            Vector3 center = (worldMinimum + worldMaximum) * 0.5f;
            float inset = settings.RoadWidth * 0.5f + 1.2f;
            Rect walkable = Rect.MinMaxRect(
                worldMinimum.x + inset,
                worldMinimum.z + inset,
                worldMaximum.x - inset,
                worldMaximum.z - inset);
            float gateWidth = settings.RoadWidth + 0.8f;
            float halfRoad = settings.RoadWidth * 0.5f;
            var gates = new[]
            {
                new CityParkGateDescriptor(
                    "park-gate-south",
                    new Vector3(
                        center.x,
                        0f,
                        worldMinimum.z + halfRoad),
                    Vector3.forward,
                    gateWidth),
                new CityParkGateDescriptor(
                    "park-gate-east",
                    new Vector3(
                        worldMaximum.x - halfRoad,
                        0f,
                        center.z),
                    Vector3.left,
                    gateWidth),
                new CityParkGateDescriptor(
                    "park-gate-north",
                    new Vector3(
                        center.x,
                        0f,
                        worldMaximum.z - halfRoad),
                    Vector3.back,
                    gateWidth),
                new CityParkGateDescriptor(
                    "park-gate-west",
                    new Vector3(
                        worldMinimum.x + halfRoad,
                        0f,
                        center.z),
                    Vector3.right,
                    gateWidth)
            };

            List<Vector3> trees =
                CreateParkTreePositions(seed, walkable, center);
            List<Vector3> benches =
                CreateParkBenchPositions(center);
            return new CityParkPlan(
                cells,
                walkable,
                center,
                gates,
                trees,
                benches);
        }

        private static List<Vector3> CreateParkTreePositions(
            int seed,
            Rect bounds,
            Vector3 center)
        {
            const int gridSize = 8;
            const float pathClearance = 5.4f;
            const float plazaRadiusSquared = 10.5f * 10.5f;
            var result = new List<Vector3>(40);

            for (int z = 0; z < gridSize; z++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    var random = new DeterministicRandom(
                        StableHash(seed, x, z, 0x54524545u));
                    if (random.NextFloat() < 0.25f)
                    {
                        continue;
                    }

                    float xAmount = (x + 0.5f) / gridSize;
                    float zAmount = (z + 0.5f) / gridSize;
                    Vector3 position = new Vector3(
                        Mathf.Lerp(bounds.xMin, bounds.xMax, xAmount) +
                        random.Range(-1.8f, 1.8f),
                        0f,
                        Mathf.Lerp(bounds.yMin, bounds.yMax, zAmount) +
                        random.Range(-1.8f, 1.8f));
                    Vector3 offset = position - center;
                    if (Mathf.Abs(offset.x) < pathClearance ||
                        Mathf.Abs(offset.z) < pathClearance ||
                        offset.sqrMagnitude < plazaRadiusSquared)
                    {
                        continue;
                    }

                    position.x = Mathf.Clamp(
                        position.x,
                        bounds.xMin + 1f,
                        bounds.xMax - 1f);
                    position.z = Mathf.Clamp(
                        position.z,
                        bounds.yMin + 1f,
                        bounds.yMax - 1f);
                    result.Add(position);
                }
            }

            return result;
        }

        private static List<Vector3> CreateParkBenchPositions(
            Vector3 center)
        {
            return new List<Vector3>
            {
                center + new Vector3(-12f, 0f, -5.5f),
                center + new Vector3(-12f, 0f, 5.5f),
                center + new Vector3(12f, 0f, -5.5f),
                center + new Vector3(12f, 0f, 5.5f),
                center + new Vector3(-5.5f, 0f, -12f),
                center + new Vector3(5.5f, 0f, -12f),
                center + new Vector3(-5.5f, 0f, 12f),
                center + new Vector3(5.5f, 0f, 12f)
            };
        }

        private static Vector2Int ChooseFrontage(
            Vector2Int cell,
            int seed,
            HashSet<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            var available = new List<Vector2Int>(4);
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                Vector2Int direction = CardinalDirections[index];
                RoadEdge edge =
                    RoadEdge.ForCellFrontage(cell, direction);
                if (roads.Contains(edge) &&
                    pathKinds.TryGetValue(
                        edge,
                        out CityPathKind kind) &&
                    kind == CityPathKind.Street)
                {
                    available.Add(direction);
                }
            }

            if (available.Count == 0)
            {
                return Vector2Int.zero;
            }

            uint hash = StableHash(seed, cell.x, cell.y, 0x444F4F52u);
            return available[(int)(hash % (uint)available.Count)];
        }

        private static float XzSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return x * x + z * z;
        }

        private static bool HasAnyFrontage(
            Vector2Int cell,
            HashSet<RoadEdge> roads)
        {
            for (int index = 0; index < CardinalDirections.Length; index++)
            {
                if (roads.Contains(
                    RoadEdge.ForCellFrontage(cell, CardinalDirections[index])))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ToNodeIndex(Vector2Int node, int nodeWidth)
        {
            return (node.y * nodeWidth) + node.x;
        }

        private static int ToLotIndex(int x, int z, int blockWidth)
        {
            return (z * blockWidth) + x;
        }

        private static void Shuffle<T>(
            IList<T> items,
            ref DeterministicRandom random)
        {
            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = random.NextInt(index + 1);
                T temporary = items[index];
                items[index] = items[other];
                items[other] = temporary;
            }
        }

        private static uint StableHash(int seed, uint salt)
        {
            return StableHash(unchecked((uint)seed), salt);
        }

        private static uint StableHash(int seed, int x, int z, uint salt)
        {
            uint hash = StableHash(unchecked((uint)seed), unchecked((uint)x));
            hash = StableHash(hash, unchecked((uint)z));
            return StableHash(hash, salt);
        }

        private static uint StableHash(uint first, uint second)
        {
            uint hash = first ^ 0x9E3779B9u;
            hash ^= second + 0x85EBCA6Bu + (hash << 6) + (hash >> 2);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            hash ^= hash >> 16;
            return hash == 0u ? 0xA341316Cu : hash;
        }

        private readonly struct BarCandidate
        {
            public BarCandidate(
                int lotIndex,
                Vector2Int cell,
                CityDistrictKind district,
                RoadEdge frontage,
                Vector3 returnPosition,
                uint rank)
            {
                LotIndex = lotIndex;
                Cell = cell;
                District = district;
                Frontage = frontage;
                ReturnPosition = returnPosition;
                Rank = rank;
            }

            public int LotIndex { get; }
            public Vector2Int Cell { get; }
            public CityDistrictKind District { get; }
            public RoadEdge Frontage { get; }
            public Vector3 ReturnPosition { get; }
            public uint Rank { get; }
        }

        private struct DeterministicRandom
        {
            private uint state;

            public DeterministicRandom(uint seed)
            {
                state = seed == 0u ? 0xA341316Cu : seed;
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
                }

                return (int)(((ulong)NextUInt() * (uint)exclusiveMaximum) >> 32);
            }

            public float NextFloat()
            {
                return (NextUInt() >> 8) * (1f / 16777216f);
            }

            public float Range(float minimum, float maximum)
            {
                return minimum + ((maximum - minimum) * NextFloat());
            }

            private uint NextUInt()
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }
        }

        private sealed class DisjointSet
        {
            private readonly int[] parent;
            private readonly byte[] rank;

            public DisjointSet(int count)
            {
                parent = new int[count];
                rank = new byte[count];
                for (int index = 0; index < count; index++)
                {
                    parent[index] = index;
                }
            }

            public bool Union(int first, int second)
            {
                int firstRoot = Find(first);
                int secondRoot = Find(second);
                if (firstRoot == secondRoot)
                {
                    return false;
                }

                if (rank[firstRoot] < rank[secondRoot])
                {
                    parent[firstRoot] = secondRoot;
                }
                else if (rank[firstRoot] > rank[secondRoot])
                {
                    parent[secondRoot] = firstRoot;
                }
                else
                {
                    parent[secondRoot] = firstRoot;
                    rank[firstRoot]++;
                }

                return true;
            }

            private int Find(int item)
            {
                int root = item;
                while (parent[root] != root)
                {
                    root = parent[root];
                }

                while (parent[item] != item)
                {
                    int next = parent[item];
                    parent[item] = root;
                    item = next;
                }

                return root;
            }
        }
    }
}
