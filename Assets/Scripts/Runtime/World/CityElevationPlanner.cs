using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityElevationPlanner
    {
        private const float LevelTolerance = 0.02f;
        private const float SignatureStairMinimumRise = 0.90f;
        private const float SignatureStairWidth = 1.8f;
        private const float SignatureStairTread = 0.32f;
        private const float SignatureStairLanding = 1.5f;
        private const float RiverBankHeightAboveWater = 1.8f;
        private const float RiverValleyRisePerNode = 0.98f;
        private const float LakeWaterDatumBelowAccess = 0.36f;

        private static readonly float[] DefaultNorthProfile =
        {
            1.0f,
            1.5f,
            2.5f,
            3.0f,
            4.0f,
            5.0f,
            6.0f,
            7.0f,
            8.0f,
            9.0f,
            10.0f,
            9.0f,
            8.0f,
            7.0f,
            6.0f
        };

        public static CityElevationPlan Create(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed,
            Vector3 worldOrigin,
            IReadOnlyList<Vector2Int> nodes,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            IReadOnlyList<BuildingLot> lots = null,
            IReadOnlyList<CityOpenAreaAccessDescriptor>
                openAreaAccesses = null)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (roads == null)
            {
                throw new ArgumentNullException(nameof(roads));
            }

            if (pathKinds == null)
            {
                throw new ArgumentNullException(nameof(pathKinds));
            }

            bool elevated = string.Equals(
                blueprint.Id,
                CityBlueprintCatalog.DefaultBlueprintId,
                StringComparison.Ordinal);
            var nodeElevations = new Dictionary<Vector2Int, float>();
            for (int index = 0; index < nodes.Count; index++)
            {
                Vector2Int node = nodes[index];
                nodeElevations.Add(
                    node,
                    elevated
                        ? ResolveDefaultNodeElevation(
                            node,
                            settings.BlockCount,
                            blueprint.River)
                        : 0f);
            }

            var cellElevations = new Dictionary<Vector2Int, float>();
            for (int index = 0; index < blueprint.Cells.Count; index++)
            {
                CityBlueprintCell cell = blueprint.Cells[index];
                float elevation = elevated
                    ? ResolveCellElevation(
                        cell,
                        nodeElevations,
                        seed)
                    : 0f;
                cellElevations.Add(cell.Cell, elevation);
            }

            if (elevated && lots != null)
            {
                AlignLotTerracesToFrontages(
                    lots,
                    nodeElevations,
                    cellElevations);
            }

            if (elevated && openAreaAccesses != null)
            {
                AlignOpenAreasToAccesses(
                    blueprint,
                    openAreaAccesses,
                    nodeElevations,
                    cellElevations);
            }

            var transitions = CreateTransitions(
                settings,
                roads,
                pathKinds,
                nodeElevations);
            Dictionary<CityDistrictKind, DistrictElevationProfile>
                profiles = CreateProfiles(
                    blueprint,
                    cellElevations,
                    elevated);
            List<CityElevationStairDescriptor> stairs = elevated
                ? CreateSignatureStairs(
                    blueprint,
                    seed,
                    roads,
                    pathKinds,
                    nodeElevations)
                : new List<CityElevationStairDescriptor>();
            var plan = new CityElevationPlan(
                blueprint.Id,
                seed,
                worldOrigin,
                settings.NodeSpacing,
                settings.RoadWidth,
                elevated,
                nodeElevations,
                cellElevations,
                transitions,
                profiles,
                stairs);
            CityElevationValidator.ValidateOrThrow(
                plan,
                blueprint,
                nodes,
                roads,
                pathKinds);
            return plan;
        }

        private static float ResolveDefaultNodeElevation(
            Vector2Int node,
            Vector2Int blockCount,
            CityRiverDefinition river)
        {
            if (river != null)
            {
                float waterY = CityRiverPlanner.ResolveWaterY(
                    river,
                    node.y);
                float distanceToBank = Mathf.Min(
                    Mathf.Abs(node.x - river.CorridorCellX),
                    Mathf.Abs(node.x - (river.CorridorCellX + 1)));
                return waterY +
                       RiverBankHeightAboveWater +
                       distanceToBank * RiverValleyRisePerNode;
            }

            float northAmount = blockCount.y > 0
                ? Mathf.Clamp01(node.y / (float)blockCount.y)
                : 0f;
            float scaledNorth = northAmount *
                                (DefaultNorthProfile.Length - 1);
            int first = Mathf.Clamp(
                Mathf.FloorToInt(scaledNorth),
                0,
                DefaultNorthProfile.Length - 1);
            int second = Mathf.Min(
                first + 1,
                DefaultNorthProfile.Length - 1);
            float northElevation = Mathf.Lerp(
                DefaultNorthProfile[first],
                DefaultNorthProfile[second],
                scaledNorth - first);
            float eastAmount = blockCount.x > 0
                ? Mathf.Clamp01(node.x / (float)blockCount.x)
                : 0f;
            return northElevation + eastAmount * 2f;
        }

        private static float ResolveCellElevation(
            CityBlueprintCell cell,
            IReadOnlyDictionary<Vector2Int, float> nodeElevations,
            int seed)
        {
            if (cell.IsWater)
            {
                return cell.Area.Feature == CityAreaFeatureKind.Lake
                    ? 1f
                    : 0f;
            }

            if (cell.Area.Feature == CityAreaFeatureKind.NorthWaterfront)
            {
                return 0.6f;
            }

            if (cell.Area.Feature == CityAreaFeatureKind.Lake)
            {
                return 1.7f + StableSignedStep(seed, cell.Cell, 0.15f);
            }

            if (cell.Area.Feature == CityAreaFeatureKind.Cemetery)
            {
                // The cemetery is one authored precinct with a continuous
                // fence and processional path, so it deliberately keeps a
                // shared terrace datum.
                return 2.3f;
            }

            if (cell.Area.Feature == CityAreaFeatureKind.Yard)
            {
                // Yards are required to declare a street access, so this
                // placeholder is always overwritten by
                // AlignOpenAreasToAccesses with the access frontage datum.
                return 0f;
            }

            Vector2Int minimum = cell.Cell;
            float average =
                GetNode(nodeElevations, minimum) +
                GetNode(nodeElevations, minimum + Vector2Int.right) +
                GetNode(nodeElevations, minimum + Vector2Int.up) +
                GetNode(nodeElevations, minimum + Vector2Int.one);
            return Mathf.Round((average * 0.25f) / 0.15f) * 0.15f;
        }

        private static void AlignLotTerracesToFrontages(
            IReadOnlyList<BuildingLot> lots,
            IReadOnlyDictionary<Vector2Int, float> nodeElevations,
            IDictionary<Vector2Int, float> cellElevations)
        {
            for (int index = 0; index < lots.Count; index++)
            {
                BuildingLot lot = lots[index];
                if (lot == null ||
                    !lot.HasRoadFrontage ||
                    lot.IsPark)
                {
                    continue;
                }

                RoadEdge frontage = RoadEdge.ForCellFrontage(
                    lot.Cell,
                    lot.FrontageDirection);
                cellElevations[lot.Cell] =
                    (GetNode(nodeElevations, frontage.A) +
                     GetNode(nodeElevations, frontage.B)) * 0.5f;
            }
        }

        private static void AlignOpenAreasToAccesses(
            CityBlueprint blueprint,
            IReadOnlyList<CityOpenAreaAccessDescriptor> accesses,
            IReadOnlyDictionary<Vector2Int, float> nodeElevations,
            IDictionary<Vector2Int, float> cellElevations)
        {
            for (int accessIndex = 0;
                 accessIndex < accesses.Count;
                 accessIndex++)
            {
                CityOpenAreaAccessDescriptor access =
                    accesses[accessIndex];
                float accessDatum =
                    (GetNode(nodeElevations, access.FrontageEdge.A) +
                     GetNode(nodeElevations, access.FrontageEdge.B)) * 0.5f;
                for (int cellIndex = 0;
                     cellIndex < blueprint.Cells.Count;
                     cellIndex++)
                {
                    CityBlueprintCell cell = blueprint.Cells[cellIndex];
                    if (!string.Equals(
                            cell.Area.Id,
                            access.AreaId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (cell.IsWater)
                    {
                        // The lake is an elevated local basin. Leaving its
                        // water at a global Y=1 while the only approach is
                        // eight metres higher creates a sheer artificial pit.
                        // Keep a small blocked shoreline drop instead; sea
                        // water remains on the global coastal datum.
                        if (access.Feature == CityAreaFeatureKind.Lake)
                        {
                            cellElevations[cell.Cell] =
                                accessDatum - LakeWaterDatumBelowAccess;
                        }

                        continue;
                    }

                    cellElevations[cell.Cell] = accessDatum;
                }
            }
        }

        private static Dictionary<RoadEdge,
            CityElevationTransitionDescriptor> CreateTransitions(
            CityGenerationSettings settings,
            IReadOnlyList<RoadEdge> roads,
            IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
            IReadOnlyDictionary<Vector2Int, float> nodeElevations)
        {
            var result = new Dictionary<RoadEdge,
                CityElevationTransitionDescriptor>();
            float horizontalRun = Mathf.Max(
                0.01f,
                Mathf.Min(
                    settings.NodeSpacing.x,
                    settings.NodeSpacing.y) - settings.RoadWidth);
            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                float start = GetNode(nodeElevations, edge.A);
                float end = GetNode(nodeElevations, edge.B);
                float grade = Mathf.Abs(end - start) /
                              horizontalRun * 100f;
                CityPathKind pathKind = pathKinds[edge];
                CityTraversalMobility mobility =
                    CityTraversalMobility.Player |
                    CityTraversalMobility.Pedestrian;
                if (pathKind == CityPathKind.Street)
                {
                    mobility |= CityTraversalMobility.Vehicle |
                                CityTraversalMobility.Bus;
                }

                result.Add(
                    edge,
                    new CityElevationTransitionDescriptor(
                        edge,
                        pathKind,
                        Mathf.Abs(end - start) <= LevelTolerance
                            ? CityElevationTransitionKind.Level
                            : CityElevationTransitionKind.VehicleGrade,
                        mobility,
                        start,
                        end,
                        horizontalRun,
                        grade));
            }

            return result;
        }

        private static List<CityElevationStairDescriptor>
            CreateSignatureStairs(
                CityBlueprint blueprint,
                int seed,
                IReadOnlyList<RoadEdge> roads,
                IReadOnlyDictionary<RoadEdge, CityPathKind> pathKinds,
                IReadOnlyDictionary<Vector2Int, float> nodeElevations)
        {
            var candidates = new Dictionary<CityDistrictKind,
                List<StairCandidate>>();
            for (int index = 0; index < roads.Count; index++)
            {
                RoadEdge edge = roads[index];
                if (pathKinds[edge] != CityPathKind.Street ||
                    !TryResolveSignatureStairOwner(
                        blueprint,
                        edge,
                        out CityDistrictKind district,
                        out CityElevationStairSide stairSide))
                {
                    continue;
                }

                float first = GetNode(nodeElevations, edge.A);
                float second = GetNode(nodeElevations, edge.B);
                float rise = Mathf.Abs(second - first);
                if (rise < SignatureStairMinimumRise)
                {
                    continue;
                }

                Vector2Int lower = first <= second ? edge.A : edge.B;
                Vector2Int upper = first <= second ? edge.B : edge.A;
                if (!candidates.TryGetValue(
                        district,
                        out List<StairCandidate> districtCandidates))
                {
                    districtCandidates = new List<StairCandidate>();
                    candidates.Add(district, districtCandidates);
                }

                districtCandidates.Add(new StairCandidate(
                    edge,
                    district,
                    lower,
                    upper,
                    stairSide,
                    rise,
                    StableRank(seed, edge, district)));
            }

            var result = new List<CityElevationStairDescriptor>();
            CityDistrictKind[] districts =
            {
                CityDistrictKind.OldTown,
                CityDistrictKind.Residential,
                CityDistrictKind.Industrial,
                CityDistrictKind.Nightlife
            };
            for (int index = 0; index < districts.Length; index++)
            {
                CityDistrictKind district = districts[index];
                if (!candidates.TryGetValue(
                        district,
                        out List<StairCandidate> districtCandidates) ||
                    districtCandidates.Count == 0)
                {
                    continue;
                }

                districtCandidates.Sort(StairCandidate.Compare);
                StairCandidate selected = districtCandidates[0];
                int stepCount = Mathf.Clamp(
                    Mathf.RoundToInt(selected.Rise / 0.16f),
                    6,
                    12);
                float stepRise = selected.Rise / stepCount;
                result.Add(new CityElevationStairDescriptor(
                    $"city-stair-{district.ToString().ToLowerInvariant()}",
                    district,
                    selected.Edge,
                    selected.LowerNode,
                    selected.UpperNode,
                    selected.Side,
                    stepCount,
                    stepRise,
                    SignatureStairTread,
                    SignatureStairWidth,
                    SignatureStairLanding));
            }

            return result;
        }

        private static bool TryResolveSignatureStairOwner(
            CityBlueprint blueprint,
            RoadEdge edge,
            out CityDistrictKind district,
            out CityElevationStairSide stairSide)
        {
            GetAdjacentCells(edge, out Vector2Int first, out Vector2Int second);
            bool firstExists = blueprint.TryGetCell(
                first,
                out CityBlueprintCell firstCell);
            bool secondExists = blueprint.TryGetCell(
                second,
                out CityBlueprintCell secondCell);
            if (!firstExists || !secondExists)
            {
                district = default;
                stairSide = default;
                return false;
            }

            // Yards host no signature stairs in v1, and excluding them
            // keeps the pre-yard stair selection bit-identical: before the
            // yards existed these edges had no outward cell and were
            // rejected by the existence check above.
            if (firstCell.Area.Feature == CityAreaFeatureKind.Yard ||
                secondCell.Area.Feature == CityAreaFeatureKind.Yard)
            {
                district = default;
                stairSide = default;
                return false;
            }

            bool firstUrban = firstCell.Area.Feature ==
                              CityAreaFeatureKind.UrbanDistrict;
            bool secondUrban = secondCell.Area.Feature ==
                               CityAreaFeatureKind.UrbanDistrict;
            if (!firstUrban && !secondUrban)
            {
                district = default;
                stairSide = default;
                return false;
            }

            CityBlueprintCell owner;
            if (firstUrban && secondUrban)
            {
                if (firstCell.Area.Archetype != secondCell.Area.Archetype)
                {
                    district = default;
                    stairSide = default;
                    return false;
                }

                owner = firstCell;
                stairSide = CityElevationStairSide.Right;
            }
            else
            {
                owner = firstUrban ? firstCell : secondCell;
                stairSide = firstUrban
                    ? CityElevationStairSide.Left
                    : CityElevationStairSide.Right;
            }

            district = owner.Area.Archetype;
            return true;
        }

        private static void GetAdjacentCells(
            RoadEdge edge,
            out Vector2Int first,
            out Vector2Int second)
        {
            if (edge.IsHorizontal)
            {
                first = new Vector2Int(edge.A.x, edge.A.y - 1);
                second = new Vector2Int(edge.A.x, edge.A.y);
                return;
            }

            first = new Vector2Int(edge.A.x - 1, edge.A.y);
            second = new Vector2Int(edge.A.x, edge.A.y);
        }

        private static Dictionary<CityDistrictKind,
            DistrictElevationProfile> CreateProfiles(
                CityBlueprint blueprint,
                IReadOnlyDictionary<Vector2Int, float> cellElevations,
                bool elevated)
        {
            var ranges = new Dictionary<CityDistrictKind, Vector2>();
            for (int index = 0; index < blueprint.Cells.Count; index++)
            {
                CityBlueprintCell cell = blueprint.Cells[index];
                CityDistrictKind district = cell.Area.Archetype;
                float elevation = cellElevations[cell.Cell];
                if (!ranges.TryGetValue(district, out Vector2 range))
                {
                    ranges.Add(
                        district,
                        new Vector2(elevation, elevation));
                    continue;
                }

                ranges[district] = new Vector2(
                    Mathf.Min(range.x, elevation),
                    Mathf.Max(range.y, elevation));
            }

            var result = new Dictionary<CityDistrictKind,
                DistrictElevationProfile>();
            foreach (KeyValuePair<CityDistrictKind, Vector2> pair in ranges)
            {
                float minimum = elevated ? pair.Value.x : 0f;
                float maximum = elevated ? pair.Value.y : 0f;
                result.Add(
                    pair.Key,
                    new DistrictElevationProfile(
                        pair.Key,
                        minimum,
                        maximum,
                        ResolvePreferredStairConnections(pair.Key)));
            }

            return result;
        }

        private static int ResolvePreferredStairConnections(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                case CityDistrictKind.Residential:
                case CityDistrictKind.Industrial:
                case CityDistrictKind.Nightlife:
                case CityDistrictKind.Lake:
                    return 1;
                case CityDistrictKind.CentralPark:
                    return 4;
                case CityDistrictKind.NorthWaterfront:
                    return 2;
                case CityDistrictKind.Yard:
                    return 0;
                default:
                    return 0;
            }
        }

        private static float GetNode(
            IReadOnlyDictionary<Vector2Int, float> elevations,
            Vector2Int node)
        {
            return elevations.TryGetValue(node, out float elevation)
                ? elevation
                : 0f;
        }

        private static float StableSignedStep(
            int seed,
            Vector2Int cell,
            float step)
        {
            uint value = unchecked((uint)seed);
            value ^= unchecked((uint)cell.x) * 0x9E3779B9u;
            value ^= unchecked((uint)cell.y) * 0x85EBCA6Bu;
            value ^= value >> 16;
            return ((int)(value % 3u) - 1) * step;
        }

        private static uint StableRank(
            int seed,
            RoadEdge edge,
            CityDistrictKind district)
        {
            uint value = unchecked((uint)seed) ^
                         unchecked((uint)district) * 0x9E3779B9u;
            value ^= unchecked((uint)edge.A.x) * 0x85EBCA6Bu;
            value ^= unchecked((uint)edge.A.y) * 0xC2B2AE35u;
            value ^= unchecked((uint)edge.B.x) * 0x27D4EB2Fu;
            value ^= unchecked((uint)edge.B.y) * 0x165667B1u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return value;
        }

        private readonly struct StairCandidate
        {
            public StairCandidate(
                RoadEdge edge,
                CityDistrictKind district,
                Vector2Int lowerNode,
                Vector2Int upperNode,
                CityElevationStairSide side,
                float rise,
                uint rank)
            {
                Edge = edge;
                District = district;
                LowerNode = lowerNode;
                UpperNode = upperNode;
                Side = side;
                Rise = rise;
                Rank = rank;
            }

            public RoadEdge Edge { get; }
            public CityDistrictKind District { get; }
            public Vector2Int LowerNode { get; }
            public Vector2Int UpperNode { get; }
            public CityElevationStairSide Side { get; }
            public float Rise { get; }
            public uint Rank { get; }

            public static int Compare(
                StairCandidate left,
                StairCandidate right)
            {
                int rank = left.Rank.CompareTo(right.Rank);
                return rank != 0
                    ? rank
                    : RoadEdge.Compare(left.Edge, right.Edge);
            }
        }
    }
}
