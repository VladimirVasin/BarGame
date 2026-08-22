using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityLayoutGenerator
    {
        public const float MaximumHomeBarRouteDistance = 48f;
        public const float DistrictPointApproachWidth = 5.2f;
        public const float MinimumDistrictPointLotDimension = 18f;

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

        internal static IReadOnlyList<CityDistrictKind> UrbanDistricts =>
            UrbanDistrictOrder;

        public static CityLayout Generate(CityGenerationSettings settings, int seed)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            return Generate(
                CityBlueprintCatalog.CreateLegacy(settings),
                settings,
                seed,
                false,
                false);
        }

        public static CityLayout Generate(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed)
        {
            return Generate(
                blueprint,
                settings,
                seed,
                true,
                true);
        }

        private static CityLayout Generate(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed,
            bool anchorAtBlueprintCenter,
            bool enforceAreaRequirements)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Validate();
            CityGenerationSettings snapshot = settings.Copy();
            snapshot.Blueprint = blueprint;
            snapshot.BlocksX = blueprint.CellBounds.xMax;
            snapshot.BlocksZ = blueprint.CellBounds.yMax;
            snapshot.Validate();
            if (enforceAreaRequirements)
            {
                ValidateAreaRequirements(blueprint, snapshot);
            }

            List<RoadEdge> allEdges = CreateAllEdges(snapshot);
            List<Vector2Int> nodes = CreateNodes(allEdges);
            List<RoadEdge> roads = CreateRoadGraph(
                snapshot,
                seed,
                nodes,
                allEdges);
            EnsureEveryBlockHasFrontage(snapshot, seed, roads);
            EnsureYardAccessEdges(snapshot, roads);
            EnsureDefaultOuterBoundaryStreets(
                snapshot,
                allEdges,
                roads);
            roads.Sort(RoadEdge.Compare);

            Vector3 origin = anchorAtBlueprintCenter
                ? new Vector3(
                    -blueprint.CenterNode.x * snapshot.NodeSpacing.x,
                    0f,
                    -blueprint.CenterNode.y * snapshot.NodeSpacing.y)
                : new Vector3(
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
                pathKinds,
                out List<CityDistrictPointOfInterestDescriptor>
                    districtPointsOfInterest,
                out Dictionary<CityDistrictKind, Vector2Int>
                    primaryLandmarkCells);
            CitySurfacePlanner.Create(
                blueprint,
                snapshot,
                origin,
                roads,
                pathKinds,
                out List<CitySurfaceDescriptor> surfaces,
                out List<CityOpenAreaAccessDescriptor> openAreaAccesses,
                out Rect worldXZBounds,
                out Rect mapWorldXZBounds);
            CityElevationPlan elevationPlan = CityElevationPlanner.Create(
                blueprint,
                snapshot,
                seed,
                origin,
                nodes,
                roads,
                pathKinds,
                lots,
                openAreaAccesses);
            lots = CityElevationRebaser.RebaseLots(
                elevationPlan,
                lots);
            park = CityElevationRebaser.RebasePark(
                elevationPlan,
                park);
            districtPointsOfInterest =
                CityElevationRebaser.RebaseDistrictPoints(
                    elevationPlan,
                    districtPointsOfInterest);
            surfaces = CityElevationRebaser.RebaseSurfaces(
                elevationPlan,
                surfaces);
            openAreaAccesses =
                CityElevationRebaser.RebaseOpenAreaAccesses(
                    elevationPlan,
                    openAreaAccesses);
            CityRiverPlan riverPlan = CityRiverPlanner.Create(
                blueprint,
                snapshot,
                origin,
                elevationPlan);
            List<CityDistrictDescriptor> districts =
                CreateDistricts(snapshot, lots);
            Vector2Int spawnNode =
                ResolveInitialSpawnNode(snapshot, lots);

            var layout = new CityLayout(
                seed,
                blueprint,
                snapshot.BlockCount,
                snapshot.NodeSpacing,
                origin,
                snapshot.RoadWidth,
                snapshot.MinimumBarRouteDistance,
                elevationPlan,
                riverPlan,
                nodes,
                roads,
                pathKinds,
                lots,
                districts,
                park,
                districtPointsOfInterest,
                primaryLandmarkCells,
                surfaces,
                openAreaAccesses,
                worldXZBounds,
                mapWorldXZBounds,
                spawnNode);
            layout.ValidateOrThrow();
            return layout;
        }

        private static void ValidateAreaRequirements(
            CityBlueprint blueprint,
            CityGenerationSettings settings)
        {
            int requiredBarCount = 0;
            for (int index = 0; index < blueprint.UrbanAreas.Count; index++)
            {
                if (blueprint.UrbanAreas[index].Definition.RequiresBar)
                {
                    requiredBarCount++;
                }
            }

            if (settings.BarCount < requiredBarCount)
            {
                throw new InvalidOperationException(
                    $"Blueprint '{blueprint.Id}' requires at least " +
                    $"{requiredBarCount} bars, but settings request " +
                    $"{settings.BarCount}.");
            }
        }

        private static Vector2Int ResolveInitialSpawnNode(
            CityGenerationSettings settings,
            IReadOnlyList<BuildingLot> lots)
        {
            var fallback = new Vector2Int(
                settings.Blueprint?.CenterNode.x ?? settings.BlocksX / 2,
                settings.Blueprint?.CenterNode.y ?? settings.BlocksZ / 2);

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

        private static List<Vector2Int> CreateNodes(
            IReadOnlyList<RoadEdge> edges)
        {
            var unique = new HashSet<Vector2Int>();
            for (int index = 0; index < edges.Count; index++)
            {
                unique.Add(edges[index].A);
                unique.Add(edges[index].B);
            }

            var nodes = new List<Vector2Int>(unique);
            nodes.Sort(CompareNodesRowMajor);
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
                    if (x < settings.BlocksX &&
                        (settings.ParticipatesInRoadGrid(
                             new Vector2Int(x, z - 1)) ||
                         settings.ParticipatesInRoadGrid(
                             new Vector2Int(x, z))))
                    {
                        edges.Add(new RoadEdge(node, node + Vector2Int.right));
                    }

                    if (z < settings.BlocksZ &&
                        (settings.ParticipatesInRoadGrid(
                             new Vector2Int(x - 1, z)) ||
                         settings.ParticipatesInRoadGrid(
                             new Vector2Int(x, z))))
                    {
                        edges.Add(new RoadEdge(node, node + Vector2Int.up));
                    }
                }
            }

            CityRiverDefinition river = settings.Blueprint?.River;
            if (river != null)
            {
                var unique = new HashSet<RoadEdge>(edges);
                for (int index = 0; index < river.Bridges.Count; index++)
                {
                    RoadEdge crossing = river.Bridges[index].CrossingEdge;
                    if (unique.Add(crossing))
                    {
                        edges.Add(crossing);
                    }
                }
            }

            return edges;
        }

        private static List<RoadEdge> CreateRoadGraph(
            CityGenerationSettings settings,
            int seed,
            IReadOnlyList<Vector2Int> nodes,
            List<RoadEdge> allEdges)
        {
            var shuffled = new List<RoadEdge>(allEdges);
            var random = new DeterministicRandom(
                StableHash(seed, 0x47524150u));
            Shuffle(shuffled, ref random);

            int nodeCount = nodes.Count;
            var nodeIndices = new Dictionary<Vector2Int, int>(nodeCount);
            for (int index = 0; index < nodeCount; index++)
            {
                nodeIndices.Add(nodes[index], index);
            }

            var sets = new DisjointSet(nodeCount);
            var roads = new List<RoadEdge>(Mathf.Max(0, nodeCount - 1));
            var roadSet = new HashSet<RoadEdge>();

            List<RoadEdge> requiredEdges =
                CreateRequiredEdges(
                    settings,
                    new HashSet<RoadEdge>(allEdges));
            for (int index = 0; index < requiredEdges.Count; index++)
            {
                RoadEdge edge = requiredEdges[index];
                if (roadSet.Add(edge))
                {
                    roads.Add(edge);
                }

                sets.Union(
                    nodeIndices[edge.A],
                    nodeIndices[edge.B]);
            }

            for (int index = 0; index < shuffled.Count; index++)
            {
                RoadEdge edge = shuffled[index];
                int first = nodeIndices[edge.A];
                int second = nodeIndices[edge.B];
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
            CityGenerationSettings settings,
            ISet<RoadEdge> available)
        {
            var required = new List<RoadEdge>();
            var unique = new HashSet<RoadEdge>();
            int centerX = settings.Blueprint?.CenterNode.x ??
                          settings.BlocksX / 2;
            int centerZ = settings.Blueprint?.CenterNode.y ??
                          settings.BlocksZ / 2;

            for (int x = 0; x < settings.BlocksX; x++)
            {
                AddRequiredEdge(
                    required,
                    unique,
                    available,
                    new RoadEdge(
                        new Vector2Int(x, centerZ),
                        new Vector2Int(x + 1, centerZ)));
            }

            for (int z = 0; z < settings.BlocksZ; z++)
            {
                AddRequiredEdge(
                    required,
                    unique,
                    available,
                    new RoadEdge(
                        new Vector2Int(centerX, z),
                        new Vector2Int(centerX, z + 1)));
            }

            CityRiverDefinition river = settings.Blueprint?.River;
            if (river != null)
            {
                for (int index = 0; index < river.Bridges.Count; index++)
                {
                    AddRequiredEdge(
                        required,
                        unique,
                        available,
                        river.Bridges[index].CrossingEdge);
                }

                for (int z = river.CoreMinimumZ;
                     z < river.CoreMaximumZExclusive;
                     z++)
                {
                    AddRequiredEdge(
                        required,
                        unique,
                        available,
                        new RoadEdge(
                            new Vector2Int(river.CorridorCellX, z),
                            new Vector2Int(river.CorridorCellX, z + 1)));
                    AddRequiredEdge(
                        required,
                        unique,
                        available,
                        new RoadEdge(
                            new Vector2Int(river.CorridorCellX + 1, z),
                            new Vector2Int(river.CorridorCellX + 1, z + 1)));
                }
            }

            CityAreaPlacement park = settings.Blueprint?.CentralPark;
            if (park == null)
            {
                AddRequiredOpenAreaAccessEdges(
                    settings,
                    required,
                    unique,
                    available);
                return required;
            }

            var parkCells = new HashSet<Vector2Int>(park.Cells);
            for (int cellIndex = 0;
                 cellIndex < park.Cells.Count;
                 cellIndex++)
            {
                Vector2Int cell = park.Cells[cellIndex];
                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    if (parkCells.Contains(cell + direction))
                    {
                        continue;
                    }

                    AddRequiredEdge(
                        required,
                        unique,
                        available,
                        RoadEdge.ForCellFrontage(cell, direction));
                }
            }

            AddRequiredOpenAreaAccessEdges(
                settings,
                required,
                unique,
                available);

            return required;
        }

        private static void AddRequiredOpenAreaAccessEdges(
            CityGenerationSettings settings,
            ICollection<RoadEdge> target,
            ISet<RoadEdge> unique,
            ISet<RoadEdge> available)
        {
            CityBlueprint blueprint = settings.Blueprint;
            if (blueprint == null)
            {
                return;
            }

            for (int areaIndex = 0;
                 areaIndex < blueprint.Areas.Count;
                 areaIndex++)
            {
                CityAreaPlacement area = blueprint.Areas[areaIndex];
                if (area.Definition.Feature !=
                        CityAreaFeatureKind.NorthWaterfront &&
                    area.Definition.Feature != CityAreaFeatureKind.Cemetery)
                {
                    continue;
                }

                bool found = false;
                int bestDistance = int.MaxValue;
                RoadEdge bestEdge = default;
                for (int cellIndex = 0;
                     cellIndex < area.Cells.Count;
                     cellIndex++)
                {
                    Vector2Int openCell = area.Cells[cellIndex];
                    if (area.GetTopology(openCell) !=
                        CityCellTopologyKind.OpenLand)
                    {
                        continue;
                    }

                    for (int directionIndex = 0;
                         directionIndex < CardinalDirections.Length;
                         directionIndex++)
                    {
                        Vector2Int direction =
                            CardinalDirections[directionIndex];
                        Vector2Int roadCell = openCell - direction;
                        if (!blueprint.ParticipatesInRoadGrid(roadCell))
                        {
                            continue;
                        }

                        RoadEdge edge = RoadEdge.ForCellFrontage(
                            roadCell,
                            direction);
                        if (!available.Contains(edge))
                        {
                            continue;
                        }

                        int midpointX = edge.A.x + edge.B.x;
                        int midpointZ = edge.A.y + edge.B.y;
                        int distance = Mathf.Abs(
                                           midpointX -
                                           blueprint.CenterNode.x * 2) +
                                       Mathf.Abs(
                                           midpointZ -
                                           blueprint.CenterNode.y * 2);
                        if (!found ||
                            distance < bestDistance ||
                            (distance == bestDistance &&
                             RoadEdge.Compare(edge, bestEdge) < 0))
                        {
                            found = true;
                            bestDistance = distance;
                            bestEdge = edge;
                        }
                    }
                }

                if (found)
                {
                    AddRequiredEdge(
                        target,
                        unique,
                        available,
                        bestEdge);
                }
            }
        }

        private static void AddRequiredEdge(
            ICollection<RoadEdge> target,
            ISet<RoadEdge> unique,
            ISet<RoadEdge> available,
            RoadEdge edge)
        {
            if (available.Contains(edge) && unique.Add(edge))
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
                    if (!settings.CreatesLot(cell) ||
                        settings.IsParkCell(cell) ||
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

        private static void EnsureYardAccessEdges(
            CityGenerationSettings settings,
            List<RoadEdge> roads)
        {
            // Yards must be reachable from a street for the open-area
            // access contract. This runs after every random-driven road
            // pass, so on any blueprint where a yard already borders a
            // built street (the canonical city) it is a strict no-op and
            // the road RNG stream is untouched.
            CityBlueprint blueprint = settings.Blueprint;
            if (blueprint == null)
            {
                return;
            }

            var roadSet = new HashSet<RoadEdge>(roads);
            for (int areaIndex = 0;
                 areaIndex < blueprint.Areas.Count;
                 areaIndex++)
            {
                CityAreaPlacement area = blueprint.Areas[areaIndex];
                if (area.Definition.Feature != CityAreaFeatureKind.Yard)
                {
                    continue;
                }

                bool hasAccess = false;
                bool hasBest = false;
                RoadEdge best = default;
                long bestDistance = long.MaxValue;
                for (int cellIndex = 0;
                     cellIndex < area.Cells.Count && !hasAccess;
                     cellIndex++)
                {
                    Vector2Int cell = area.Cells[cellIndex];
                    for (int directionIndex = 0;
                         directionIndex < CardinalDirections.Length;
                         directionIndex++)
                    {
                        Vector2Int direction =
                            CardinalDirections[directionIndex];
                        if (!settings.ParticipatesInRoadGrid(
                                cell + direction))
                        {
                            continue;
                        }

                        RoadEdge candidate = RoadEdge.ForCellFrontage(
                            cell,
                            direction);
                        if (roadSet.Contains(candidate))
                        {
                            hasAccess = true;
                            break;
                        }

                        Vector2Int doubledMidpoint =
                            candidate.A + candidate.B;
                        Vector2Int doubledCenter =
                            blueprint.CenterNode * 2;
                        long deltaX =
                            doubledMidpoint.x - doubledCenter.x;
                        long deltaZ =
                            doubledMidpoint.y - doubledCenter.y;
                        long distance = deltaX * deltaX + deltaZ * deltaZ;
                        if (!hasBest ||
                            distance < bestDistance ||
                            (distance == bestDistance &&
                             RoadEdge.Compare(candidate, best) < 0))
                        {
                            best = candidate;
                            hasBest = true;
                            bestDistance = distance;
                        }
                    }
                }

                if (!hasAccess && hasBest)
                {
                    roads.Add(best);
                    roadSet.Add(best);
                }
            }
        }

        private static void EnsureDefaultOuterBoundaryStreets(
            CityGenerationSettings settings,
            IReadOnlyList<RoadEdge> availableEdges,
            ICollection<RoadEdge> roads)
        {
            CityBlueprint blueprint = settings.Blueprint;
            if (blueprint == null ||
                !string.Equals(
                    blueprint.Id,
                    CityBlueprintCatalog.DefaultBlueprintId,
                    StringComparison.Ordinal))
            {
                return;
            }

            // The exterior circuit is authored city structure, not an
            // optional random loop. Append it after all seeded graph and
            // access passes so the existing interior road selection stays
            // intact. The river-bank edges are part of this boundary and
            // its two required road bridges join the west and east sides.
            var available = new HashSet<RoadEdge>(availableEdges);
            var roadSet = new HashSet<RoadEdge>(roads);
            for (int cellIndex = 0;
                 cellIndex < blueprint.Cells.Count;
                 cellIndex++)
            {
                CityBlueprintCell descriptor = blueprint.Cells[cellIndex];
                if (!descriptor.ParticipatesInRoadGrid)
                {
                    continue;
                }

                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    if (blueprint.ParticipatesInRoadGrid(
                            descriptor.Cell + direction))
                    {
                        continue;
                    }

                    AddRequiredEdge(
                        roads,
                        roadSet,
                        available,
                        RoadEdge.ForCellFrontage(
                            descriptor.Cell,
                            direction));
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
                if (settings.Blueprint?.River != null &&
                    settings.Blueprint.River.TryGetBridge(
                        edge,
                        out CityBridgeDefinition bridge))
                {
                    result.Add(
                        edge,
                        bridge.Role == CityBridgeRole.ParkFootbridge
                            ? CityPathKind.ParkPath
                            : CityPathKind.Street);
                    continue;
                }

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
            if (edge.IsHorizontal)
            {
                return settings.IsParkCell(
                           new Vector2Int(edge.A.x, edge.A.y - 1)) &&
                       settings.IsParkCell(
                           new Vector2Int(edge.A.x, edge.A.y));
            }

            return settings.IsParkCell(
                       new Vector2Int(edge.A.x - 1, edge.A.y)) &&
                   settings.IsParkCell(
                       new Vector2Int(edge.A.x, edge.A.y));
        }

        private static List<BuildingLot> CreateBuildingLots(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyList<Vector2Int> nodes,
            List<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            out List<CityDistrictPointOfInterestDescriptor>
                districtPointsOfInterest,
            out Dictionary<CityDistrictKind, Vector2Int>
                primaryLandmarkCells)
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
                    if (!settings.CreatesLot(cell) ||
                        settings.IsParkCell(cell))
                    {
                        frontages[lotIndex] = Vector2Int.zero;
                        continue;
                    }

                    Vector2Int frontage = ChooseFrontage(
                        cell,
                        seed,
                        roadSet,
                        pathKinds);
                    if (TryGetCanonicalRiverHomePair(
                            settings,
                            seed,
                            out Vector2Int canonicalBar,
                            out _,
                            out Vector2Int barFrontage) &&
                        cell == canonicalBar)
                    {
                        RoadEdge authoredFrontage =
                            RoadEdge.ForCellFrontage(cell, barFrontage);
                        if (roadSet.Contains(authoredFrontage) &&
                            pathKinds[authoredFrontage] ==
                            CityPathKind.Street)
                        {
                            frontage = barFrontage;
                        }
                    }

                    frontages[lotIndex] = frontage;
                    if (frontage != Vector2Int.zero)
                    {
                        barCandidates.Add(CreateBarCandidate(
                            settings,
                            seed,
                            origin,
                            lotIndex,
                            cell,
                            frontage));
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
                seed,
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

            CityDistrictPointOfInterestPlanner.Create(
                settings,
                seed,
                origin,
                roads,
                pathKinds,
                barLots,
                homeLotIndex,
                out primaryLandmarkCells,
                out Dictionary<CityDistrictKind, int> districtPointLots,
                out districtPointsOfInterest);
            var districtPointLotIndices = new HashSet<int>(
                districtPointLots.Values);
            int supermarketLotIndex = SelectSupermarketLot(
                settings,
                seed,
                origin,
                nodes,
                roads,
                frontages,
                barLots,
                homeLotIndex,
                districtPointLotIndices,
                primaryLandmarkCells);

            var lots = new List<BuildingLot>(lotCount);
            int barOrdinal = 0;
            for (int z = 0; z < settings.BlocksZ; z++)
            {
                for (int x = 0; x < settings.BlocksX; x++)
                {
                    int lotIndex = ToLotIndex(x, z, settings.BlocksX);
                    var cell = new Vector2Int(x, z);
                    if (!settings.CreatesLot(cell))
                    {
                        continue;
                    }

                    bool isBar = barLots.Contains(lotIndex);
                    bool isPlayerHome = lotIndex == homeLotIndex;
                    bool isSupermarket =
                        lotIndex == supermarketLotIndex;
                    bool isDistrictPointOfInterest =
                        districtPointLotIndices.Contains(lotIndex);
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
                        cell,
                        frontages[lotIndex],
                        isBar,
                        isPlayerHome,
                        isSupermarket,
                        isDistrictPointOfInterest,
                        barActivity));
                }
            }

            return lots;
        }

        private static int SelectSupermarketLot(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyList<Vector2Int> frontages,
            ISet<int> barLots,
            int homeLotIndex,
            ISet<int> districtPointLots,
            IReadOnlyDictionary<CityDistrictKind, Vector2Int>
                primaryLandmarkCells)
        {
            var primaryLandmarkLots = new HashSet<int>();
            foreach (Vector2Int cell in primaryLandmarkCells.Values)
            {
                primaryLandmarkLots.Add(
                    ToLotIndex(
                        cell.x,
                        cell.y,
                        settings.BlocksX));
            }

            bool hasHome = homeLotIndex >= 0 &&
                           homeLotIndex < frontages.Count &&
                           frontages[homeLotIndex] != Vector2Int.zero;
            RoadEdge homeFrontage = default;
            Vector3 homeReturn = default;
            if (hasHome)
            {
                Vector2Int homeCell = new Vector2Int(
                    homeLotIndex % settings.BlocksX,
                    homeLotIndex / settings.BlocksX);
                homeFrontage = RoadEdge.ForCellFrontage(
                    homeCell,
                    frontages[homeLotIndex]);
                homeReturn = GetReturnPosition(
                    settings,
                    origin,
                    homeCell,
                    frontages[homeLotIndex]);
            }

            bool found = false;
            int bestLotIndex = -1;
            int bestDistrictPenalty = int.MaxValue;
            float bestDistance = float.PositiveInfinity;
            uint bestRank = uint.MaxValue;
            for (int lotIndex = 0;
                 lotIndex < frontages.Count;
                 lotIndex++)
            {
                Vector2Int frontage = frontages[lotIndex];
                if (frontage == Vector2Int.zero ||
                    lotIndex == homeLotIndex ||
                    barLots.Contains(lotIndex) ||
                    districtPointLots.Contains(lotIndex) ||
                    primaryLandmarkLots.Contains(lotIndex))
                {
                    continue;
                }

                Vector2Int cell = new Vector2Int(
                    lotIndex % settings.BlocksX,
                    lotIndex / settings.BlocksX);
                if (!settings.CreatesLot(cell) ||
                    settings.IsParkCell(cell))
                {
                    continue;
                }

                float facadeWidth = frontage.x != 0
                    ? settings.BlockDepth -
                      settings.BuildingInset * 2f
                    : settings.BlockWidth -
                      settings.BuildingInset * 2f;
                if (facadeWidth <
                    SupermarketEntranceGeometry.CanopyWidth + 0.20f)
                {
                    continue;
                }

                CityDistrictKind district =
                    ResolveDistrict(settings, cell);
                int districtPenalty =
                    district == CityDistrictKind.Residential
                        ? 0
                        : 1;
                Vector3 candidateReturn = GetReturnPosition(
                    settings,
                    origin,
                    cell,
                    frontage);
                float distance = 0f;
                if (hasHome)
                {
                    distance = CityTravelDistance.BetweenAnchors(
                        nodes,
                        roads,
                        node => GetNodeWorldPosition(
                            settings,
                            origin,
                            node),
                        homeFrontage,
                        homeReturn,
                        RoadEdge.ForCellFrontage(cell, frontage),
                        candidateReturn);
                }

                uint rank = StableHash(
                    seed,
                    cell.x,
                    cell.y,
                    0x53555052u);
                if (!found ||
                    districtPenalty < bestDistrictPenalty ||
                    (districtPenalty == bestDistrictPenalty &&
                     distance < bestDistance - 0.001f) ||
                    (districtPenalty == bestDistrictPenalty &&
                     Mathf.Abs(distance - bestDistance) <= 0.001f &&
                     rank < bestRank))
                {
                    found = true;
                    bestLotIndex = lotIndex;
                    bestDistrictPenalty = districtPenalty;
                    bestDistance = distance;
                    bestRank = rank;
                }
            }

            return bestLotIndex;
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

            if (TryGetCanonicalRiverHomePair(
                    settings,
                    seed,
                    out Vector2Int canonicalBar,
                    out Vector2Int canonicalHome,
                    out Vector2Int barFrontage))
            {
                int barLotIndex = ToLotIndex(
                    canonicalBar.x,
                    canonicalBar.y,
                    settings.BlocksX);
                RoadEdge sharedRoad = RoadEdge.ForCellFrontage(
                    canonicalBar,
                    barFrontage);
                if (barLots.Contains(barLotIndex) &&
                    settings.CreatesLot(canonicalHome) &&
                    roadSet.Contains(sharedRoad) &&
                    pathKinds[sharedRoad] == CityPathKind.Street)
                {
                    homeFrontage = -barFrontage;
                    return ToLotIndex(
                        canonicalHome.x,
                        canonicalHome.y,
                        settings.BlocksX);
                }
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
                    if (sharedRoad != bar.Frontage ||
                        !roadSet.Contains(sharedRoad) ||
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

                    Vector2Int frontage = -direction;
                    Vector3 homeReturn = GetReturnPosition(
                        settings,
                        origin,
                        homeCell,
                        frontage);
                    float distance =
                        CityTravelDistance.BetweenAnchors(
                            nodes,
                            roads,
                            node => GetNodeWorldPosition(
                                settings,
                                origin,
                                node),
                            sharedRoad,
                            homeReturn,
                            bar.Frontage,
                            bar.ReturnPosition);
                    if (distance >
                        MaximumHomeBarRouteDistance + 0.001f)
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
                        bestFrontage = frontage;
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
                if (!settings.CreatesLot(cell) ||
                    settings.IsParkCell(cell))
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

        private static bool TryGetCanonicalRiverHomePair(
            CityGenerationSettings settings,
            int seed,
            out Vector2Int barCell,
            out Vector2Int homeCell,
            out Vector2Int barFrontage)
        {
            barCell = new Vector2Int(12, 6);
            homeCell = new Vector2Int(12, 5);
            barFrontage = Vector2Int.down;
            return seed == GameSessionState.DefaultCitySeed &&
                   settings.Blueprint?.River != null &&
                   string.Equals(
                       settings.Blueprint.Id,
                       CityBlueprintCatalog.DefaultBlueprintId,
                       StringComparison.Ordinal) &&
                   settings.CreatesLot(barCell) &&
                   settings.CreatesLot(homeCell);
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
                ResolveAreaId(settings, cell),
                ResolveDistrict(settings, cell),
                RoadEdge.ForCellFrontage(cell, frontage),
                center + (direction * roadDistance),
                StableHash(seed, cell.x, cell.y, 0x42415253u));
        }

        private static HashSet<int> SelectBarLots(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyList<BarCandidate> candidates)
        {
            var selected = new List<BarCandidate>(settings.BarCount);
            var selectedLots = new HashSet<int>();
            var selectedAreas = new HashSet<string>(StringComparer.Ordinal);
            Vector2Int centerNode = settings.Blueprint?.CenterNode ??
                                    new Vector2Int(
                                        settings.BlocksX / 2,
                                        settings.BlocksZ / 2);
            Vector3 spawn = GetNodeWorldPosition(
                settings,
                origin,
                centerNode);
            bool hasAuthoredBar = TryGetCanonicalRiverHomePair(
                settings,
                seed,
                out Vector2Int canonicalBar,
                out _,
                out _);
            string canonicalArea = null;
            if (hasAuthoredBar)
            {
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (candidates[index].Cell == canonicalBar)
                    {
                        canonicalArea = candidates[index].AreaId;
                        break;
                    }
                }
            }

            for (int ordinal = 0;
                 ordinal < settings.BarCount;
                 ordinal++)
            {
                string requiredArea = FindUnrepresentedArea(
                    settings,
                    candidates,
                    selectedLots,
                    selectedAreas);
                BarCandidate best = default;
                bool found = false;
                float bestScore = float.NegativeInfinity;

                for (int index = 0; index < candidates.Count; index++)
                {
                    BarCandidate candidate = candidates[index];
                    if (selectedLots.Contains(candidate.LotIndex) ||
                        (requiredArea != null &&
                         !string.Equals(
                             candidate.AreaId,
                             requiredArea,
                             StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    if (canonicalArea != null &&
                        string.Equals(
                            requiredArea,
                            canonicalArea,
                            StringComparison.Ordinal) &&
                        candidate.Cell != canonicalBar)
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
                selectedAreas.Add(best.AreaId);
            }

            return selectedLots;
        }

        private static string FindUnrepresentedArea(
            CityGenerationSettings settings,
            IReadOnlyList<BarCandidate> candidates,
            ISet<int> selectedLots,
            ISet<string> selectedAreas)
        {
            if (settings.Blueprint == null)
            {
                return null;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int areaIndex = 0;
                     areaIndex < settings.Blueprint.UrbanAreas.Count;
                     areaIndex++)
                {
                    CityAreaPlacement area =
                        settings.Blueprint.UrbanAreas[areaIndex];
                    if ((pass == 0) != area.Definition.RequiresBar ||
                        selectedAreas.Contains(area.Id))
                    {
                        continue;
                    }

                    for (int candidateIndex = 0;
                         candidateIndex < candidates.Count;
                         candidateIndex++)
                    {
                        BarCandidate candidate = candidates[candidateIndex];
                        if (string.Equals(
                                candidate.AreaId,
                                area.Id,
                                StringComparison.Ordinal) &&
                            !selectedLots.Contains(candidate.LotIndex))
                        {
                            return area.Id;
                        }
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
            bool isSupermarket,
            bool isDistrictPointOfInterest,
            BarActivityKind barActivity)
        {
            var random = new DeterministicRandom(
                StableHash(seed, cell.x, cell.y, 0x4C4F5453u));
            CityDistrictKind district = ResolveDistrict(settings, cell);
            CityLandUseKind landUse = settings.IsParkCell(cell)
                ? CityLandUseKind.Park
                : isDistrictPointOfInterest
                    ? CityLandUseKind.DistrictPointOfInterest
                    : CityLandUseKind.Building;
            float maximumWidth = settings.BlockWidth - (settings.BuildingInset * 2f);
            float maximumDepth = settings.BlockDepth - (settings.BuildingInset * 2f);
            Vector2 size = landUse != CityLandUseKind.Building
                ? new Vector2(settings.BlockWidth, settings.BlockDepth)
                : isPlayerHome
                    ? new Vector2(
                        Mathf.Min(13f, maximumWidth),
                        Mathf.Min(12f, maximumDepth))
                : isSupermarket
                    ? new Vector2(
                        maximumWidth,
                        maximumDepth)
                : CreateBuildingSize(
                    district,
                    maximumWidth,
                    maximumDepth,
                    ref random);
            float height = landUse != CityLandUseKind.Building
                ? 0.1f
                : isPlayerHome
                    ? PlayerHomeBalconyGeometry.ResolveBuildingHeight(
                        settings)
                : isSupermarket
                    ? Mathf.Clamp(
                        6.4f,
                        settings.MinimumBuildingHeight,
                        settings.MaximumBuildingHeight)
                : isBar
                    ? CreateBuildingHeight(
                        settings.MinimumBuildingHeight,
                        settings.MaximumBuildingHeight,
                        district,
                        ref random)
                    : CreateBuildingHeight(
                        settings.MinimumOrdinaryBuildingHeight,
                        settings.MaximumOrdinaryBuildingHeight,
                        district,
                        ref random);
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
            float sidewalkCenterOffset =
                (settings.RoadWidth * 0.5f) -
                (CityStreetSurfacePlanner.SidewalkWidth * 0.5f);
            Vector3 sidewalkArrivalPosition =
                returnPosition -
                (direction * Mathf.Max(0f, sidewalkCenterOffset));
            Color color = CreateBuildingColor(
                ref random,
                isBar,
                isPlayerHome,
                isSupermarket,
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
                ResolveAreaId(settings, cell),
                district,
                landUse,
                isBar,
                isPlayerHome,
                isSupermarket,
                barId,
                barActivity,
                frontage,
                doorPosition,
                returnPosition,
                sidewalkArrivalPosition);
        }

        /// <summary>
        /// Draws one building colour for a given seed.
        /// <para>
        /// Exists so the facade tests can sweep the live colour ranges rather
        /// than restate them. The facade albedos are brightened by a fixed
        /// factor chosen from the brightest channel these ranges can produce,
        /// so a widened palette has to fail a test instead of quietly clamping
        /// every bar in the city.
        /// </para>
        /// </summary>
        internal static Color CreateBuildingColorForSeed(
            uint seed,
            bool isBar,
            bool isPlayerHome,
            bool isSupermarket,
            CityDistrictKind district)
        {
            var random = new DeterministicRandom(seed);
            return CreateBuildingColor(
                ref random,
                isBar,
                isPlayerHome,
                isSupermarket,
                district);
        }

        private static Color CreateBuildingColor(
            ref DeterministicRandom random,
            bool isBar,
            bool isPlayerHome,
            bool isSupermarket,
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

            if (isSupermarket)
            {
                return new Color(
                    random.Range(0.30f, 0.38f),
                    random.Range(0.34f, 0.42f),
                    random.Range(0.27f, 0.34f),
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
            return settings.CreatesLot(cell);
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
            float minimumHeight,
            float maximumHeight,
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
                minimumHeight,
                maximumHeight,
                random.Range(minimumT, maximumT));
        }

        internal static Vector3 GetLotCenter(
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

        internal static CityDistrictKind ResolveDistrict(
            CityGenerationSettings settings,
            Vector2Int cell)
        {
            if (settings.TryGetArea(
                    cell,
                    out CityAreaDefinition area))
            {
                return area.Archetype;
            }

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

        internal static string ResolveAreaId(
            CityGenerationSettings settings,
            Vector2Int cell)
        {
            if (settings.TryGetArea(
                    cell,
                    out CityAreaDefinition area))
            {
                return area.Id;
            }

            return ResolveDistrict(settings, cell)
                .ToString()
                .ToLowerInvariant();
        }

        private static List<CityDistrictDescriptor> CreateDistricts(
            CityGenerationSettings settings,
            IReadOnlyList<BuildingLot> lots)
        {
            var cellsByArea =
                new Dictionary<string, List<Vector2Int>>(
                    StringComparer.Ordinal);
            var boundsByArea =
                new Dictionary<string, Bounds>(StringComparer.Ordinal);
            var kindsByArea =
                new Dictionary<string, CityDistrictKind>(
                    StringComparer.Ordinal);
            var orderedAreas = new List<string>();

            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                if (!cellsByArea.TryGetValue(
                        lot.AreaId,
                        out List<Vector2Int> cells))
                {
                    cells = new List<Vector2Int>();
                    cellsByArea.Add(lot.AreaId, cells);
                    boundsByArea.Add(lot.AreaId, default);
                    kindsByArea.Add(lot.AreaId, lot.District);
                    orderedAreas.Add(lot.AreaId);
                }

                cells.Add(lot.Cell);
                var cellBounds = new Bounds(
                    lot.Center,
                    new Vector3(
                        settings.NodeSpacing.x,
                        1f,
                        settings.NodeSpacing.y));
                Bounds districtBounds = boundsByArea[lot.AreaId];
                if (districtBounds.size != Vector3.zero)
                {
                    districtBounds.Encapsulate(cellBounds);
                    boundsByArea[lot.AreaId] = districtBounds;
                }
                else
                {
                    boundsByArea[lot.AreaId] = cellBounds;
                }
            }

            var result = new List<CityDistrictDescriptor>(
                cellsByArea.Count);
            if (settings.Blueprint != null)
            {
                orderedAreas.Sort((left, right) =>
                {
                    int leftIndex = GetBlueprintAreaIndex(
                        settings.Blueprint,
                        left);
                    int rightIndex = GetBlueprintAreaIndex(
                        settings.Blueprint,
                        right);
                    return leftIndex.CompareTo(rightIndex);
                });
            }

            for (int index = 0; index < orderedAreas.Count; index++)
            {
                string areaId = orderedAreas[index];
                result.Add(new CityDistrictDescriptor(
                    areaId,
                    kindsByArea[areaId],
                    cellsByArea[areaId],
                    boundsByArea[areaId]));
            }

            return result;
        }

        private static int GetBlueprintAreaIndex(
            CityBlueprint blueprint,
            string areaId)
        {
            for (int index = 0; index < blueprint.Areas.Count; index++)
            {
                if (string.Equals(
                        blueprint.Areas[index].Id,
                        areaId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        private static CityParkPlan CreateParkPlan(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin)
        {
            if (settings.Blueprint?.River != null)
            {
                return CreateRiverParkPlan(settings, seed, origin);
            }

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

        private static CityParkPlan CreateRiverParkPlan(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin)
        {
            CityAreaPlacement park = settings.Blueprint.CentralPark;
            CityRiverDefinition river = settings.Blueprint.River;
            var westCells = new List<Vector2Int>();
            var eastCells = new List<Vector2Int>();
            for (int index = 0; index < park.Cells.Count; index++)
            {
                Vector2Int cell = park.Cells[index];
                (cell.x < river.CorridorCellX
                    ? westCells
                    : eastCells).Add(cell);
            }

            CityParkRegionPlan west = CreateParkRegion(
                "central-park-west",
                westCells,
                settings,
                origin);
            CityParkRegionPlan east = CreateParkRegion(
                "central-park-east",
                eastCells,
                settings,
                origin);
            var regions = new[] { west, east };
            Rect aggregate = Rect.MinMaxRect(
                Mathf.Min(
                    west.WalkableBounds.xMin,
                    east.WalkableBounds.xMin),
                Mathf.Min(
                    west.WalkableBounds.yMin,
                    east.WalkableBounds.yMin),
                Mathf.Max(
                    west.WalkableBounds.xMax,
                    east.WalkableBounds.xMax),
                Mathf.Max(
                    west.WalkableBounds.yMax,
                    east.WalkableBounds.yMax));
            float bridgeX = origin.x +
                            (river.CorridorCellX + 0.5f) *
                            settings.NodeSpacing.x;
            var center = new Vector3(
                bridgeX,
                0f,
                aggregate.center.y);
            var gates = new List<CityParkGateDescriptor>();
            gates.AddRange(west.Gates);
            gates.AddRange(east.Gates);
            var trees = new List<Vector3>();
            trees.AddRange(CreateParkTreePositions(
                seed,
                west.WalkableBounds,
                west.PlazaPosition));
            trees.AddRange(CreateParkTreePositions(
                seed ^ 0x51A7,
                east.WalkableBounds,
                east.PlazaPosition));
            var benches = new List<Vector3>();
            AddRiverParkBenches(benches, west, true);
            AddRiverParkBenches(benches, east, false);
            return new CityParkPlan(
                new List<Vector2Int>(park.Cells),
                aggregate,
                center,
                gates,
                trees,
                benches,
                regions);
        }

        private static CityParkRegionPlan CreateParkRegion(
            string id,
            IList<Vector2Int> cells,
            CityGenerationSettings settings,
            Vector3 origin)
        {
            int minimumX = int.MaxValue;
            int minimumZ = int.MaxValue;
            int maximumX = int.MinValue;
            int maximumZ = int.MinValue;
            for (int index = 0; index < cells.Count; index++)
            {
                minimumX = Mathf.Min(minimumX, cells[index].x);
                minimumZ = Mathf.Min(minimumZ, cells[index].y);
                maximumX = Mathf.Max(maximumX, cells[index].x + 1);
                maximumZ = Mathf.Max(maximumZ, cells[index].y + 1);
            }

            Vector3 worldMinimum = GetNodeWorldPosition(
                settings,
                origin,
                new Vector2Int(minimumX, minimumZ));
            Vector3 worldMaximum = GetNodeWorldPosition(
                settings,
                origin,
                new Vector2Int(maximumX, maximumZ));
            float inset = settings.RoadWidth * 0.5f + 1.2f;
            Rect bounds = Rect.MinMaxRect(
                worldMinimum.x + inset,
                worldMinimum.z + inset,
                worldMaximum.x - inset,
                worldMaximum.z - inset);
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            bool west = id.EndsWith("west", StringComparison.Ordinal);
            Vector3 plaza = center + new Vector3(
                west ? bounds.width * 0.24f : -bounds.width * 0.24f,
                0f,
                0f);
            float halfRoad = settings.RoadWidth * 0.5f;
            float gateWidth = settings.RoadWidth + 0.8f;
            int lowerMiddleX =
                minimumX + ((maximumX - minimumX - 1) / 2);
            int upperMiddleX =
                minimumX + ((maximumX - minimumX) / 2);
            int lowerMiddleZ =
                minimumZ + ((maximumZ - minimumZ - 1) / 2);
            int upperMiddleZ =
                minimumZ + ((maximumZ - minimumZ) / 2);
            float southGateX = GetLotCenter(
                settings,
                origin,
                new Vector2Int(lowerMiddleX, minimumZ)).x;
            float northGateX = GetLotCenter(
                settings,
                origin,
                new Vector2Int(upperMiddleX, maximumZ - 1)).x;
            float westGateZ = GetLotCenter(
                settings,
                origin,
                new Vector2Int(minimumX, lowerMiddleZ)).z;
            float eastGateZ = GetLotCenter(
                settings,
                origin,
                new Vector2Int(maximumX - 1, upperMiddleZ)).z;
            var gates = new[]
            {
                new CityParkGateDescriptor(
                    $"{id}-gate-south",
                    new Vector3(
                        southGateX,
                        0f,
                        worldMinimum.z + halfRoad),
                    Vector3.forward,
                    gateWidth),
                new CityParkGateDescriptor(
                    $"{id}-gate-east",
                    new Vector3(
                        worldMaximum.x - halfRoad,
                        0f,
                        eastGateZ),
                    Vector3.left,
                    gateWidth),
                new CityParkGateDescriptor(
                    $"{id}-gate-north",
                    new Vector3(
                        northGateX,
                        0f,
                        worldMaximum.z - halfRoad),
                    Vector3.back,
                    gateWidth),
                new CityParkGateDescriptor(
                    $"{id}-gate-west",
                    new Vector3(
                        worldMinimum.x + halfRoad,
                        0f,
                        westGateZ),
                    Vector3.right,
                    gateWidth)
            };
            return new CityParkRegionPlan(
                id,
                cells,
                bounds,
                center,
                gates,
                plaza);
        }

        private static void AddRiverParkBenches(
            ICollection<Vector3> target,
            CityParkRegionPlan region,
            bool west)
        {
            float inward = west ? 1f : -1f;
            target.Add(region.PlazaPosition + new Vector3(
                -inward * 5.5f,
                0f,
                -7f));
            target.Add(region.PlazaPosition + new Vector3(
                -inward * 5.5f,
                0f,
                7f));
            target.Add(region.Center + new Vector3(0f, 0f, -15f));
            target.Add(region.Center + new Vector3(0f, 0f, 15f));
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

        internal static int ToLotIndex(int x, int z, int blockWidth)
        {
            return (z * blockWidth) + x;
        }

        private static int CompareNodesRowMajor(
            Vector2Int left,
            Vector2Int right)
        {
            int zComparison = left.y.CompareTo(right.y);
            return zComparison != 0
                ? zComparison
                : left.x.CompareTo(right.x);
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

        internal static uint StableHash(
            int seed,
            int x,
            int z,
            uint category,
            uint salt)
        {
            uint hash = StableHash(seed, x, z, salt);
            return StableHash(hash, category);
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
                string areaId,
                CityDistrictKind district,
                RoadEdge frontage,
                Vector3 returnPosition,
                uint rank)
            {
                LotIndex = lotIndex;
                Cell = cell;
                AreaId = areaId ?? string.Empty;
                District = district;
                Frontage = frontage;
                ReturnPosition = returnPosition;
                Rank = rank;
            }

            public int LotIndex { get; }
            public Vector2Int Cell { get; }
            public string AreaId { get; }
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
