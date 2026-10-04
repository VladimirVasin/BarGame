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
            snapshot.SpatialPlan = CitySpatialPlan.Create(blueprint, snapshot);
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
            roads = CityStreetHierarchyPlanner.Replan(snapshot, seed,
                nodes, allEdges, roads,
                CreateRequiredEdges(snapshot, new HashSet<RoadEdge>(allEdges)));
            EnsureEveryBlockHasFrontage(snapshot, seed, roads);
            EnsureYardAccessEdges(snapshot, roads);
            EnsureDefaultOuterBoundaryStreets(
                snapshot,
                allEdges,
                roads);
            EnsureAuthoredStreets(snapshot, allEdges, roads);
            CityRoadGeometryPlan.EnsurePilotRoads(snapshot, roads);
            roads.Sort(RoadEdge.Compare);

            Vector2 originOffset = anchorAtBlueprintCenter
                ? -snapshot.GetCoordinateOffset(blueprint.CenterNode)
                : -snapshot.GetCoordinateOffset(new Vector2(
                    snapshot.BlocksX * 0.5f, snapshot.BlocksZ * 0.5f));
            Vector3 origin = new Vector3(originOffset.x, 0f, originOffset.y);
            Dictionary<RoadEdge, CityPathKind> pathKinds =
                CreatePathKinds(snapshot, roads);
            snapshot.RoadGeometry = CityRoadGeometryPlan.Create(snapshot, origin, roads);
            CityParkPlan park =
                CreateParkPlan(
                    snapshot,
                    seed,
                    origin,
                    roads,
                    pathKinds);
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

        private static void EnsureAuthoredStreets(
            CityGenerationSettings settings,
            IReadOnlyList<RoadEdge> availableEdges,
            ICollection<RoadEdge> roads)
        {
            IReadOnlyList<RoadEdge> authored =
                settings.Blueprint?.AuthoredStreets;
            if (authored == null || authored.Count == 0)
            {
                return;
            }

            // Authored structure, appended like the outer ring: after every
            // seeded pass, so no earlier random draw shifts.
            var available = new HashSet<RoadEdge>(availableEdges);
            var roadSet = new HashSet<RoadEdge>(roads);
            for (int index = 0; index < authored.Count; index++)
            {
                AddRequiredEdge(roads, roadSet, available, authored[index]);
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
                    if (frontage != Vector2Int.zero && settings.RoadGeometry?.IsAffectedCell(cell) != true)
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
                            BarActivityAssignment.Resolve(
                                settings.Blueprint?.Id,
                                seed,
                                cell,
                                barOrdinal);
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
                        barActivity,
                        primaryLandmarkCells.TryGetValue(
                            ResolveDistrict(settings, cell),
                            out Vector2Int landmarkCell) && landmarkCell == cell,
                        pathKinds.TryGetValue(RoadEdge.ForCellFrontage(cell, Vector2Int.right),
                            out CityPathKind eastKind) && eastKind == CityPathKind.Street,
                        pathKinds.TryGetValue(RoadEdge.ForCellFrontage(cell, Vector2Int.left),
                            out CityPathKind westKind) && westKind == CityPathKind.Street));
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
            if (!CanFitAuthoredSupermarket(settings))
            {
                return -1;
            }

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
                    settings.IsParkCell(cell) || settings.RoadGeometry?.IsAffectedCell(cell) == true)
                {
                    continue;
                }

                float facadeWidth = frontage.x != 0
                    ? settings.GetBlockSize(cell).y -
                      settings.BuildingInset * 2f
                    : settings.GetBlockSize(cell).x -
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

        private static bool CanFitAuthoredSupermarket(
            CityGenerationSettings settings)
        {
            const float tolerance = 0.001f;
            float availableWidth =
                settings.BlockWidth - settings.BuildingInset * 2f;
            float availableDepth =
                settings.BlockDepth - settings.BuildingInset * 2f;
            return availableWidth + tolerance >=
                       SupermarketEntranceGeometry.ExteriorWidth &&
                   availableDepth + tolerance >=
                       SupermarketEntranceGeometry.ExteriorDepth;
        }

        private static bool CanFitAuthoredPlayerHome(
            CityGenerationSettings settings)
        {
            const float tolerance = 0.001f;
            float availableX =
                settings.BlockWidth - settings.BuildingInset * 2f;
            float availableZ =
                settings.BlockDepth - settings.BuildingInset * 2f;
            bool fitsUnrotated =
                availableX + tolerance >= 13f &&
                availableZ + tolerance >= 12f;
            bool fitsRotated =
                availableX + tolerance >= 12f &&
                availableZ + tolerance >= 13f;
            return (fitsUnrotated || fitsRotated) &&
                   settings.MaximumBuildingHeight + tolerance >=
                   PlayerHomeBalconyGeometry.PreferredBuildingHeight;
        }

        private static bool CanFitAuthoredPlayerHome(
            CityGenerationSettings settings,
            Vector2Int frontage)
        {
            if (!CanFitAuthoredPlayerHome(settings) ||
                frontage == Vector2Int.zero)
            {
                return false;
            }

            const float tolerance = 0.001f;
            float requiredX = frontage.x != 0 ? 12f : 13f;
            float requiredZ = frontage.x != 0 ? 13f : 12f;
            float availableX =
                settings.BlockWidth - settings.BuildingInset * 2f;
            float availableZ =
                settings.BlockDepth - settings.BuildingInset * 2f;
            return availableX + tolerance >= requiredX &&
                   availableZ + tolerance >= requiredZ;
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
            if (barLots.Count == 0 ||
                !CanFitAuthoredPlayerHome(settings))
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
                Vector2Int canonicalHomeFrontage = -barFrontage;
                if (barLots.Contains(barLotIndex) &&
                    settings.CreatesLot(canonicalHome) && settings.RoadGeometry?.IsAffectedCell(canonicalHome) != true &&
                    roadSet.Contains(sharedRoad) &&
                    pathKinds[sharedRoad] == CityPathKind.Street &&
                    CanFitAuthoredPlayerHome(
                        settings,
                        canonicalHomeFrontage))
                {
                    homeFrontage = canonicalHomeFrontage;
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
                        settings.IsParkCell(homeCell) || settings.RoadGeometry?.IsAffectedCell(homeCell) == true)
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
                    if (!CanFitAuthoredPlayerHome(
                            settings,
                            frontage))
                    {
                        continue;
                    }

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
                    settings.IsParkCell(cell) || settings.RoadGeometry?.IsAffectedCell(cell) == true ||
                    !CanFitAuthoredPlayerHome(
                        settings,
                        frontage))
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
            barCell = BarActivityAssignment.DefaultHomeBarCell;
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
                    ? settings.GetCellSpan(cell).x * 0.5f
                    : settings.GetCellSpan(cell).y * 0.5f;
            return new BarCandidate(
                lotIndex,
                cell,
                ResolveAreaId(settings, cell),
                ResolveDistrict(settings, cell),
                frontage,
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
                bool preferHomePair = settings.BarCount == 1 &&
                                      HasHomePairCandidate(
                                          settings,
                                          candidates,
                                          selectedLots,
                                          requiredArea);
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
                             StringComparison.Ordinal)) ||
                        (preferHomePair &&
                         !HasHomeAcrossFrontage(settings, candidate)))
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

        private static bool HasHomePairCandidate(
            CityGenerationSettings settings,
            IReadOnlyList<BarCandidate> candidates,
            ISet<int> selectedLots,
            string requiredArea)
        {
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

                if (HasHomeAcrossFrontage(settings, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasHomeAcrossFrontage(
            CityGenerationSettings settings,
            BarCandidate candidate)
        {
            Vector2Int homeCell =
                candidate.Cell + candidate.FrontageDirection;
            return IsCellInsideGrid(settings, homeCell) &&
                   settings.CreatesLot(homeCell) &&
                   !settings.IsParkCell(homeCell);
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
            BarActivityKind barActivity,
            bool isPrimaryLandmark,
            bool hasEastStreet,
            bool hasWestStreet)
        {
            var random = new DeterministicRandom(
                StableHash(seed, cell.x, cell.y, 0x4C4F5453u));
            CityDistrictKind district = ResolveDistrict(settings, cell);
            CityLandUseKind landUse = settings.IsParkCell(cell)
                ? CityLandUseKind.Park
                : isDistrictPointOfInterest
                    ? CityLandUseKind.DistrictPointOfInterest
                    : CityLandUseKind.Building;
            Vector2 blockSize = settings.GetBlockSize(cell);
            Vector2 cellSpan = settings.GetCellSpan(cell);
            float maximumWidth = blockSize.x - settings.BuildingInset * 2f;
            float maximumDepth = blockSize.y - settings.BuildingInset * 2f;
            bool authoredOrdinary = landUse == CityLandUseKind.Building &&
                !isBar && !isPlayerHome && !isSupermarket &&
                settings.SpatialPlan != null && !settings.SpatialPlan.IsUniform;
            bool offsetPair = authoredOrdinary && !isPrimaryLandmark && (hasEastStreet || hasWestStreet) && district == CityDistrictKind.OldTown &&
                CityCourtyardBlockPlanner.SupportsOffsetPair(settings, cell);
            if (offsetPair) frontage = hasEastStreet ? Vector2Int.right : Vector2Int.left;
            if (authoredOrdinary && settings.RoadGeometry?.IsAffectedCell(cell) == true)
                frontage = ResolvePilotFrontage(settings.RoadGeometry, cell, frontage);
            bool isAuthoredPrecinct = (cell.x == 10 && cell.y == 5) ||
                (cell.x == 11 && (cell.y == 3 || cell.y == 4 || cell.y == 5));
            bool curvedBlock = settings.RoadGeometry?.IsAffectedCell(cell) == true;
            int buildingVariant = authoredOrdinary && curvedBlock
                ? 2
                : offsetPair
                    ? 1
                : authoredOrdinary && !isPrimaryLandmark && !isAuthoredPrecinct
                    ? ResolveBuildingVariant(seed, cell, frontage, district,
                        new Vector2(maximumWidth, maximumDepth))
                    : 0;
            Vector3 envelope = authoredOrdinary
                ? CityBuildingAssetProvider.GetExpectedEnvelope(district, buildingVariant)
                : Vector3.zero;
            Vector2 size = landUse != CityLandUseKind.Building
                ? blockSize
                : isPlayerHome
                    ? frontage.x != 0
                        ? new Vector2(12f, 13f)
                        : new Vector2(13f, 12f)
                : isSupermarket
                    ? new Vector2(
                        SupermarketEntranceGeometry.ExteriorWidth,
                        SupermarketEntranceGeometry.ExteriorDepth)
                : authoredOrdinary
                    ? frontage.x != 0
                        ? new Vector2(envelope.z, envelope.x)
                        : new Vector2(envelope.x, envelope.z)
                : CreateBuildingSize(
                    district,
                    maximumWidth,
                    maximumDepth,
                    ref random);
            float height = landUse != CityLandUseKind.Building
                ? 0.1f
                : isPlayerHome
                    ? PlayerHomeBalconyGeometry.PreferredBuildingHeight
                : isSupermarket
                    ? SupermarketEntranceGeometry.ExteriorHeight
                : isBar
                    ? CreateBuildingHeight(
                        settings.MinimumBuildingHeight,
                        settings.MaximumBuildingHeight,
                        district,
                        ref random)
                    : authoredOrdinary
                        ? envelope.y
                    : CreateBuildingHeight(
                        settings.MinimumOrdinaryBuildingHeight,
                        settings.MaximumOrdinaryBuildingHeight,
                        district,
                        ref random);
            Vector3 center = GetLotCenter(settings, origin, cell);

            Vector3 direction = new Vector3(frontage.x, 0f, frontage.y);
            Vector3 facadeForward = direction;
            if (authoredOrdinary && curvedBlock && frontage != Vector2Int.zero)
                ResolvePilotBuildingPose(settings, cell, envelope, frontage,
                    ref center, out facadeForward);
            if (offsetPair)
                center = ResolveOffsetCourtyardCenter(settings, cell, envelope, center, facadeForward);
            // The street wall belongs to the public frontage; excess land
            // stays behind the building as a yard instead of a moat on all
            // four sides. Residential setbacks deliberately remain deeper.
            if (authoredOrdinary && !curvedBlock && !offsetPair && frontage != Vector2Int.zero)
            {
                float halfSpan = frontage.x != 0 ? cellSpan.x * 0.5f : cellSpan.y * 0.5f;
                float halfBuilding = frontage.x != 0 ? size.x * 0.5f : size.y * 0.5f;
                float setback = district == CityDistrictKind.Residential ? 2.8f : 1.5f;
                float shift = Mathf.Max(0f, halfSpan - settings.RoadWidth * 0.5f - halfBuilding - setback);
                center += direction * shift;
            }
            float buildingHalfDistance =
                frontage.x != 0 ? size.x * 0.5f : size.y * 0.5f;
            float roadDistance =
                frontage.x != 0
                    ? cellSpan.x * 0.5f
                    : cellSpan.y * 0.5f;
            Vector3 doorPosition = center + (facadeForward * buildingHalfDistance);
            Vector3 returnPosition = GetLotCenter(settings, origin, cell) + (direction * roadDistance);
            if (settings.RoadGeometry != null && frontage != Vector2Int.zero)
            {
                RoadEdge frontageEdge = RoadEdge.ForCellFrontage(cell, frontage);
                if (settings.RoadGeometry.IsCurved(frontageEdge))
                {
                    CityRoadProjection projection = settings.RoadGeometry.Get(frontageEdge).Project(
                        new Vector2(returnPosition.x, returnPosition.z));
                    returnPosition.x = projection.Position.x;
                    returnPosition.z = projection.Position.y;
                    if (authoredOrdinary && curvedBlock)
                    {
                        // Docks meet the pavement normal at the facade anchor;
                        // graph frontage keeps its original cardinal identity.
                        CityRoadPath path = settings.RoadGeometry.Get(frontageEdge);
                        projection = path.Project(new Vector2(doorPosition.x, doorPosition.z));
                        returnPosition.x = projection.Position.x;
                        returnPosition.z = projection.Position.y;
                        Vector2 normal = path.SampleDistance(projection.DistanceAlong).Right;
                        if (Vector2.Dot(normal, new Vector2(direction.x, direction.z)) < 0f) normal = -normal;
                        direction = new Vector3(normal.x, 0f, normal.y);
                    }
                }
            }
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
                sidewalkArrivalPosition,
                buildingVariant,
                facadeForward);
        }

        private static Vector2Int ResolvePilotFrontage(CityRoadGeometryPlan roads,
            Vector2Int cell, Vector2Int fallback)
        {
            // East-side blocks face the oblique branch. West-side blocks face
            // its north/south approaches, retaining the authored street wall.
            Vector2Int[] candidates = cell.x == 1
                ? new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }
                : new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            foreach (Vector2Int direction in candidates)
                if (roads.IsCurved(RoadEdge.ForCellFrontage(cell, direction))) return direction;
            return fallback;
        }

        private static Vector3 ResolveOffsetCourtyardCenter(CityGenerationSettings settings,
            Vector2Int cell, Vector3 envelope, Vector3 cellCenter, Vector3 facing)
        {
            Vector2 span = settings.GetCellSpan(cell);
            var cellBounds = new Rect(cellCenter.x - span.x * .5f,
                cellCenter.z - span.y * .5f, span.x, span.y);
            float edge = facing.x > 0f ? cellBounds.xMax : cellBounds.xMin;
            Vector3 primary = new Vector3(edge - facing.x * (settings.RoadWidth * .5f + envelope.z * .5f + 1.4f),
                cellCenter.y, cellCenter.z + 1f);
            Quaternion rotation = Quaternion.LookRotation(facing);
            Vector3 rear = CityCourtyardBlockPlanner.ResolveOffsetRearCenter(primary, facing);
            Vector3 compact = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.OldTown, 0);
            List<Vector2[]> cuts = CreatePilotRoadCuts(settings, cellBounds);
            if (!FitsPilotGround(PilotEnvelope(primary, rotation, envelope.x + 1.3f, envelope.z + 1.6f), cellBounds, cuts) ||
                !FitsPilotGround(PilotEnvelope(rear, rotation, compact.x + 1.3f, compact.z + 1.6f), cellBounds, cuts))
                throw new InvalidOperationException($"Offset courtyard {cell} cannot fit its fixed metre houses on actual ground.");
            return primary;
        }

        private static void ResolvePilotBuildingPose(CityGenerationSettings settings,
            Vector2Int cell, Vector3 envelope, Vector2Int frontage,
            ref Vector3 center, out Vector3 forward)
        {
            Vector3 cardinal = new Vector3(frontage.x, 0f, frontage.y);
            forward = cardinal;
            CityRoadPath path = settings.RoadGeometry.Get(RoadEdge.ForCellFrontage(cell, frontage));
            // One authored station avoids a coincidentally cardinal midpoint
            // on a symmetric bend, and stays deterministic across city seeds.
            CityRoadSample sample = path.SampleDistance(path.Length * .35f);
            Vector2 normal = sample.Right;
            if (Vector2.Dot(normal, new Vector2(cardinal.x, cardinal.z)) < 0f) normal = -normal;
            Vector3 desired = new Vector3(normal.x, 0f, normal.y);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(cardinal, desired, Vector3.up), -12f, 12f);
            Vector3 cellCenter = center;
            Vector2 span = settings.GetCellSpan(cell);
            Rect cellBounds = new Rect(cellCenter.x - span.x * .5f,
                cellCenter.z - span.y * .5f, span.x, span.y);
            float halfSpan = frontage.x != 0 ? span.x * .5f : span.y * .5f;
            Vector3 original = cellCenter + cardinal * Mathf.Max(0f,
                halfSpan - settings.RoadWidth * .5f - envelope.z * .5f - 1.4f);
            List<Vector2[]> roadCuts = CreatePilotRoadCuts(settings, cellBounds);
            Vector3 lateral = new Vector3(cardinal.z, 0f, -cardinal.x);
            var shifts = new List<Vector2>();
            for (int inwardStep = 0; inwardStep <= 12; inwardStep++)
                for (int lateralStep = -8; lateralStep <= 8; lateralStep++)
                    shifts.Add(new Vector2(lateralStep * .25f, inwardStep * .25f));
            shifts.Sort((a, b) => {
                int distance = a.sqrMagnitude.CompareTo(b.sqrMagnitude);
                if (distance != 0) return distance;
                int inward = a.y.CompareTo(b.y);
                return inward != 0 ? inward : a.x.CompareTo(b.x);
            });
            // Keep the fixed metre mass and leave a walking clearance outside
            // it. First try its actual tangent, then smaller bounded yaw if a
            // narrow block cannot admit that rigid rectangle.
            for (int angleStep = 0; angleStep <= 6; angleStep++)
            {
                float angle = yaw * (1f - angleStep / 6f);
                Vector3 candidateForward = Quaternion.AngleAxis(angle, Vector3.up) * cardinal;
                Quaternion rotation = Quaternion.LookRotation(candidateForward, Vector3.up);
                foreach (Vector2 shift in shifts)
                {
                    Vector3 candidate = original - cardinal * shift.y + lateral * shift.x;
                    Vector2[] footprint = PilotEnvelope(candidate + candidateForward * .15f, rotation,
                        envelope.x + 1.3f, envelope.z + 1.6f);
                    if (!FitsPilotGround(footprint, cellBounds, roadCuts)) continue;
                    if (cell.x == 0)
                    {
                        // A second fixed-metre house closes the rear of the
                        // western court. Reserve its real ground now, rather
                        // than squeezing it into the gap after placement.
                        Vector2[] mass = PilotEnvelope(candidate, rotation, envelope.x, envelope.z);
                        Vector3 rear = CityCourtyardBlockPlanner.ResolveRearCenter(candidate, mass);
                        Vector3 rearModel = CityBuildingAssetProvider.GetExpectedEnvelope(district: CityDistrictKind.OldTown, variantIndex: 0);
                        Vector2[] rearEnvelope = PilotEnvelope(rear,
                            Quaternion.LookRotation(Vector3.right), rearModel.x + .7f, rearModel.z + .7f);
                        if (!FitsPilotGround(rearEnvelope, cellBounds, roadCuts)) continue;
                    }
                    center = candidate;
                    forward = candidateForward;
                    return;
                }
            }
            throw new InvalidOperationException($"Pilot building {cell} cannot fit its fixed metre envelope on actual ground.");
        }

        private static Vector2[] PilotEnvelope(Vector3 center, Quaternion rotation,
            float width, float depth)
        {
            var local = new[] { new Vector3(-width * .5f, 0f, -depth * .5f),
                new Vector3(width * .5f, 0f, -depth * .5f),
                new Vector3(width * .5f, 0f, depth * .5f),
                new Vector3(-width * .5f, 0f, depth * .5f) };
            var polygon = new Vector2[local.Length];
            for (int index = 0; index < local.Length; index++)
            {
                Vector3 world = center + rotation * local[index];
                polygon[index] = new Vector2(world.x, world.z);
            }
            return CityRoadPolygon.CounterClockwise(polygon);
        }

        private static List<Vector2[]> CreatePilotRoadCuts(CityGenerationSettings settings,
            Rect cellBounds)
        {
            var cuts = new List<Vector2[]>();
            var nodes = new HashSet<Vector2Int>();
            CityRoadJunction junction = settings.RoadGeometry.ObliqueJunction;
            foreach (RoadEdge edge in settings.RoadGeometry.Edges)
            {
                CityRoadPath path = settings.RoadGeometry.Get(edge);
                foreach (Vector2[] ribbon in settings.RoadGeometry.GetCorridor(edge))
                    if (cellBounds.Overlaps(CityRoadPolygon.Bounds(ribbon))) cuts.Add(ribbon);
                foreach (Vector2Int node in new[] { edge.A, edge.B })
                {
                    if (!nodes.Add(node) || (junction != null && node == junction.Node)) continue;
                    Vector2 point = node == edge.A ? path.Vertices[0] : path.Vertices[path.Vertices.Count - 1];
                    Rect cap = new Rect(point - Vector2.one * settings.RoadWidth * .5f,
                        Vector2.one * settings.RoadWidth);
                    if (cellBounds.Overlaps(cap)) cuts.Add(CityRoadPolygon.Rectangle(cap));
                }
            }
            if (junction != null)
                foreach (Vector2[] polygon in junction.RoadPolygons)
                    if (cellBounds.Overlaps(CityRoadPolygon.Bounds(polygon))) cuts.Add(polygon);
            return cuts;
        }

        private static bool FitsPilotGround(Vector2[] footprint, Rect cellBounds,
            IReadOnlyList<Vector2[]> roadCuts)
        {
            // A padded mass fits the ground complement exactly when it stays
            // inside its full cell and intersects no authored street/cap/core.
            // Test each real convex cut independently, avoiding fragmentation
            // and accumulated area errors in a triangulated ground union.
            foreach (Vector2 point in footprint)
                if (point.x < cellBounds.xMin || point.x > cellBounds.xMax ||
                    point.y < cellBounds.yMin || point.y > cellBounds.yMax) return false;
            Rect broad = CityRoadPolygon.Bounds(footprint);
            foreach (Vector2[] cut in roadCuts)
            {
                if (!broad.Overlaps(CityRoadPolygon.Bounds(cut))) continue;
                var intersection = new List<Vector2>(footprint);
                for (int index = 0; index < cut.Length && intersection.Count >= 3; index++)
                    intersection = CityRoadPolygon.Clip(intersection, cut[index], cut[(index + 1) % cut.Length]);
                if (CityRoadPolygon.Area(intersection) > .001f) return false;
            }
            return true;
        }

        private static int ResolveBuildingVariant(int seed, Vector2Int cell,
            Vector2Int frontage, CityDistrictKind district, Vector2 available)
        {
            uint rank = StableHash(seed, cell.x, cell.y, 0x4D415353u);
            // Longer frontage is preferred on the expanded outer blocks;
            // corner bodies introduce real rear pockets on the smaller ones.
            int preferred = (rank % 3u) == 0u ? 2 : 1;
            for (int pass = 0; pass < 2; pass++)
            {
                int variant = pass == 0 ? preferred : 3 - preferred;
                Vector3 envelope = CityBuildingAssetProvider.GetExpectedEnvelope(district, variant);
                Vector2 footprint = frontage.x != 0
                    ? new Vector2(envelope.z, envelope.x)
                    : new Vector2(envelope.x, envelope.z);
                if (footprint.x <= available.x + 0.001f &&
                    footprint.y <= available.y + 0.001f)
                    return variant;
            }
            return 0;
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
                    ? settings.GetCellSpan(cell).x * 0.5f
                    : settings.GetCellSpan(cell).y * 0.5f;
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
            Vector2 offset = settings.GetCoordinateOffset((Vector2)cell + Vector2.one * 0.5f);
            return origin + new Vector3(offset.x, 0f, offset.y);
        }

        private static Vector3 GetNodeWorldPosition(
            CityGenerationSettings settings,
            Vector3 origin,
            Vector2Int node)
        {
            Vector2 offset = settings.GetCoordinateOffset(node);
            return origin + new Vector3(offset.x, 0f, offset.y);
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
                        settings.GetCellSpan(lot.Cell).x,
                        1f,
                        settings.GetCellSpan(lot.Cell).y));
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
            Vector3 origin,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
        {
            if (settings.Blueprint?.River != null)
            {
                return CreateRiverParkPlan(
                    settings,
                    seed,
                    origin,
                    roads,
                    pathKinds);
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
                    Array.Empty<CityParkBenchDescriptor>());
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

            var region = new CityParkRegionPlan(
                "central-park",
                cells,
                walkable,
                center,
                gates,
                center);
            var regions = new[] { region };
            List<CityParkBenchDescriptor> benches =
                CityParkBenchPlanner.Create(
                    regions,
                    settings,
                    origin,
                    roads,
                    pathKinds);
            List<Vector3> trees = CreateParkTreePositions(
                seed,
                walkable,
                center,
                benches);
            return new CityParkPlan(
                cells,
                walkable,
                center,
                gates,
                trees,
                benches,
                regions);
        }

        private static CityParkPlan CreateRiverParkPlan(
            CityGenerationSettings settings,
            int seed,
            Vector3 origin,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds)
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
            float bridgeX = origin.x + settings.GetCoordinateOffset(
                new Vector2(river.CorridorCellX + 0.5f, 0f)).x;
            var center = new Vector3(
                bridgeX,
                0f,
                aggregate.center.y);
            var gates = new List<CityParkGateDescriptor>();
            gates.AddRange(west.Gates);
            gates.AddRange(east.Gates);
            List<CityParkBenchDescriptor> benches =
                CityParkBenchPlanner.Create(
                    regions,
                    settings,
                    origin,
                    roads,
                    pathKinds);
            var trees = new List<Vector3>();
            trees.AddRange(CreateParkTreePositions(
                seed,
                west.WalkableBounds,
                west.PlazaPosition,
                benches));
            trees.AddRange(CreateParkTreePositions(
                seed ^ 0x51A7,
                east.WalkableBounds,
                east.PlazaPosition,
                benches));
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

        private static List<Vector3> CreateParkTreePositions(
            int seed,
            Rect bounds,
            Vector3 center,
            IReadOnlyList<CityParkBenchDescriptor> benches)
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
                    if (IsNearParkBench(position, benches))
                    {
                        continue;
                    }

                    result.Add(position);
                }
            }

            return result;
        }

        private static bool IsNearParkBench(
            Vector3 position,
            IReadOnlyList<CityParkBenchDescriptor> benches)
        {
            const float clearance = 2.4f;
            for (int index = 0; index < benches.Count; index++)
            {
                if (XzSquaredDistance(
                        position,
                        benches[index].Position) < clearance * clearance)
                {
                    return true;
                }
            }

            return false;
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
                Vector2Int frontageDirection,
                RoadEdge frontage,
                Vector3 returnPosition,
                uint rank)
            {
                LotIndex = lotIndex;
                Cell = cell;
                AreaId = areaId ?? string.Empty;
                District = district;
                FrontageDirection = frontageDirection;
                Frontage = frontage;
                ReturnPosition = returnPosition;
                Rank = rank;
            }

            public int LotIndex { get; }
            public Vector2Int Cell { get; }
            public string AreaId { get; }
            public CityDistrictKind District { get; }
            public Vector2Int FrontageDirection { get; }
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
