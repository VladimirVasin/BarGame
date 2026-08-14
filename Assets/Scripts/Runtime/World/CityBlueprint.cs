using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum CityAreaCategory
    {
        UrbanBuilt = 0,
        NonUrbanOpen = 1
    }

    public enum CityAreaFeatureKind
    {
        UrbanDistrict = 0,
        CentralPark = 1,
        NorthWaterfront = 2,
        Lake = 3,
        Cemetery = 4,
        Yard = 5
    }

    public enum CityAreaPlacementPolicy
    {
        Movable = 0,
        CenterAnchor = 1,
        NorthEdge = 2
    }

    public enum CityCellTopologyKind
    {
        BuildableLand = 0,
        ParkLand = 1,
        OpenLand = 2,
        Water = 3
    }

    public sealed class CityAreaDefinition
    {
        public CityAreaDefinition(
            string id,
            CityDistrictKind archetype,
            CityAreaCategory category,
            CityAreaFeatureKind feature,
            CityAreaPlacementPolicy placementPolicy,
            string localizationKey,
            Color mapColor,
            bool requiresBar = false)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A city area requires a stable ID.",
                    nameof(id));
            }

            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                throw new ArgumentException(
                    "A city area requires a localization key.",
                    nameof(localizationKey));
            }

            Id = id.Trim();
            Archetype = archetype;
            Category = category;
            Feature = feature;
            PlacementPolicy = placementPolicy;
            LocalizationKey = localizationKey.Trim();
            MapColor = mapColor;
            RequiresBar = requiresBar;
            if (!IsFinite(MapColor.r) ||
                !IsFinite(MapColor.g) ||
                !IsFinite(MapColor.b) ||
                !IsFinite(MapColor.a))
            {
                throw new ArgumentOutOfRangeException(nameof(mapColor));
            }

            ValidateCombination();
        }

        public string Id { get; }
        public CityDistrictKind Archetype { get; }
        public CityAreaCategory Category { get; }
        public CityAreaFeatureKind Feature { get; }
        public CityAreaPlacementPolicy PlacementPolicy { get; }
        public string LocalizationKey { get; }
        public Color MapColor { get; }
        public bool RequiresBar { get; }
        public bool SupportsOrdinaryBuildings =>
            Category == CityAreaCategory.UrbanBuilt &&
            Feature == CityAreaFeatureKind.UrbanDistrict;

        private void ValidateCombination()
        {
            if (!Enum.IsDefined(typeof(CityDistrictKind), Archetype))
            {
                throw new ArgumentOutOfRangeException(nameof(Archetype));
            }

            if (!Enum.IsDefined(typeof(CityAreaCategory), Category))
            {
                throw new ArgumentOutOfRangeException(nameof(Category));
            }

            if (!Enum.IsDefined(typeof(CityAreaFeatureKind), Feature))
            {
                throw new ArgumentOutOfRangeException(nameof(Feature));
            }

            if (!Enum.IsDefined(
                    typeof(CityAreaPlacementPolicy),
                    PlacementPolicy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PlacementPolicy));
            }

            switch (Feature)
            {
                case CityAreaFeatureKind.UrbanDistrict:
                    if (Category != CityAreaCategory.UrbanBuilt ||
                        PlacementPolicy != CityAreaPlacementPolicy.Movable ||
                        IsSpecialArchetype(Archetype))
                    {
                        throw new ArgumentException(
                            "Urban districts require an urban archetype " +
                            "and the movable built-area policy.");
                    }

                    break;
                case CityAreaFeatureKind.CentralPark:
                    ValidateSpecialCombination(
                        CityDistrictKind.CentralPark,
                        CityAreaPlacementPolicy.CenterAnchor,
                        "central park");
                    break;
                case CityAreaFeatureKind.NorthWaterfront:
                    ValidateSpecialCombination(
                        CityDistrictKind.NorthWaterfront,
                        CityAreaPlacementPolicy.NorthEdge,
                        "north waterfront");
                    break;
                case CityAreaFeatureKind.Lake:
                    ValidateSpecialCombination(
                        CityDistrictKind.Lake,
                        CityAreaPlacementPolicy.Movable,
                        "lake");
                    break;
                case CityAreaFeatureKind.Cemetery:
                    ValidateSpecialCombination(
                        CityDistrictKind.Cemetery,
                        CityAreaPlacementPolicy.Movable,
                        "cemetery");
                    break;
                case CityAreaFeatureKind.Yard:
                    ValidateSpecialCombination(
                        CityDistrictKind.Yard,
                        CityAreaPlacementPolicy.Movable,
                        "yard");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Feature));
            }
        }

        private void ValidateSpecialCombination(
            CityDistrictKind expectedArchetype,
            CityAreaPlacementPolicy expectedPolicy,
            string label)
        {
            if (Archetype != expectedArchetype ||
                Category != CityAreaCategory.NonUrbanOpen ||
                PlacementPolicy != expectedPolicy ||
                RequiresBar)
            {
                throw new ArgumentException(
                    $"The {label} requires its matching archetype, " +
                    "non-urban category, expected placement policy and " +
                    "cannot require a bar.");
            }
        }

        private static bool IsSpecialArchetype(CityDistrictKind archetype)
        {
            switch (archetype)
            {
                case CityDistrictKind.CentralPark:
                case CityDistrictKind.NorthWaterfront:
                case CityDistrictKind.Lake:
                case CityDistrictKind.Cemetery:
                case CityDistrictKind.Yard:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public readonly struct CityBlueprintCell
    {
        internal CityBlueprintCell(
            Vector2Int cell,
            CityAreaDefinition area,
            CityCellTopologyKind topology)
        {
            Cell = cell;
            Area = area;
            Topology = topology;
        }

        public Vector2Int Cell { get; }
        public CityAreaDefinition Area { get; }
        public CityCellTopologyKind Topology { get; }
        public bool CreatesLot =>
            Topology == CityCellTopologyKind.BuildableLand ||
            Topology == CityCellTopologyKind.ParkLand;
        public bool ParticipatesInRoadGrid => CreatesLot;
        public bool IsWalkableOpenLand =>
            Topology == CityCellTopologyKind.OpenLand;
        public bool IsWater => Topology == CityCellTopologyKind.Water;
    }

    public sealed class CityAreaPlacement
    {
        private readonly ReadOnlyCollection<Vector2Int> cells;
        private readonly ReadOnlyDictionary<
            Vector2Int,
            CityCellTopologyKind> topologyByCell;

        internal CityAreaPlacement(
            CityAreaDefinition definition,
            IDictionary<Vector2Int, CityCellTopologyKind> source)
        {
            Definition = definition ??
                throw new ArgumentNullException(nameof(definition));
            if (source == null || source.Count == 0)
            {
                throw new ArgumentException(
                    $"Area '{definition.Id}' must contain at least one cell.",
                    nameof(source));
            }

            var orderedCells = new List<Vector2Int>(source.Keys);
            orderedCells.Sort(CompareCellsRowMajor);
            cells = new ReadOnlyCollection<Vector2Int>(orderedCells);
            topologyByCell =
                new ReadOnlyDictionary<Vector2Int, CityCellTopologyKind>(
                    new Dictionary<Vector2Int, CityCellTopologyKind>(source));
        }

        public CityAreaDefinition Definition { get; }
        public string Id => Definition.Id;
        public IReadOnlyList<Vector2Int> Cells => cells;
        public IReadOnlyDictionary<Vector2Int, CityCellTopologyKind>
            TopologyByCell => topologyByCell;

        public CityCellTopologyKind GetTopology(Vector2Int cell)
        {
            if (!topologyByCell.TryGetValue(
                    cell,
                    out CityCellTopologyKind topology))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cell),
                    $"Cell {cell} does not belong to area '{Id}'.");
            }

            return topology;
        }

        internal static int CompareCellsRowMajor(
            Vector2Int left,
            Vector2Int right)
        {
            int zComparison = left.y.CompareTo(right.y);
            return zComparison != 0
                ? zComparison
                : left.x.CompareTo(right.x);
        }
    }

    public sealed class CityBlueprint
    {
        // How far the OpenLand/Water fringe may extend below the
        // normalized lot footprint. Lot cells themselves must stay
        // non-negative because per-cell random streams hash raw
        // coordinates.
        private const int OpenFringeMinimumCell = -1;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.down,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left
        };

        private readonly ReadOnlyCollection<CityAreaPlacement> areas;
        private readonly ReadOnlyCollection<CityBlueprintCell> cells;
        private readonly ReadOnlyCollection<CityAreaPlacement> urbanAreas;
        private readonly Dictionary<string, CityAreaPlacement> areasById;
        private readonly Dictionary<Vector2Int, CityBlueprintCell> cellsByCell;

        internal CityBlueprint(
            string id,
            Vector2Int centerNode,
            IList<CityAreaPlacement> sourceAreas,
            CityRiverDefinition river,
            bool requireCentralPark,
            bool requireNorthWaterfront)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "A city blueprint requires a stable ID.",
                    nameof(id));
            }

            if (centerNode.x < 0 || centerNode.y < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(centerNode));
            }

            if (sourceAreas == null)
            {
                throw new ArgumentNullException(nameof(sourceAreas));
            }

            Id = id.Trim();
            CenterNode = centerNode;
            River = river;
            RequiresCentralPark = requireCentralPark;
            RequiresNorthWaterfront = requireNorthWaterfront;
            areasById = new Dictionary<string, CityAreaPlacement>(
                StringComparer.Ordinal);
            cellsByCell = new Dictionary<Vector2Int, CityBlueprintCell>();
            var copiedAreas = new List<CityAreaPlacement>(sourceAreas.Count);
            var copiedCells = new List<CityBlueprintCell>();
            var copiedUrbanAreas = new List<CityAreaPlacement>();

            for (int areaIndex = 0;
                 areaIndex < sourceAreas.Count;
                 areaIndex++)
            {
                CityAreaPlacement area = sourceAreas[areaIndex] ??
                    throw new ArgumentException(
                        "Blueprint areas cannot contain null entries.",
                        nameof(sourceAreas));
                if (!areasById.TryAdd(area.Id, area))
                {
                    throw new InvalidOperationException(
                        $"City area ID '{area.Id}' is duplicated.");
                }

                ValidateAreaCells(area);
                copiedAreas.Add(area);
                if (area.Definition.Category == CityAreaCategory.UrbanBuilt)
                {
                    copiedUrbanAreas.Add(area);
                }

                for (int cellIndex = 0;
                     cellIndex < area.Cells.Count;
                     cellIndex++)
                {
                    Vector2Int cell = area.Cells[cellIndex];
                    var descriptor = new CityBlueprintCell(
                        cell,
                        area.Definition,
                        area.GetTopology(cell));
                    if (!cellsByCell.TryAdd(cell, descriptor))
                    {
                        throw new InvalidOperationException(
                            $"City cell {cell} belongs to more than one area.");
                    }

                    copiedCells.Add(descriptor);
                }
            }

            if (copiedCells.Count == 0)
            {
                throw new InvalidOperationException(
                    "A city blueprint must contain at least one mapped cell.");
            }

            copiedCells.Sort((left, right) =>
                CityAreaPlacement.CompareCellsRowMajor(
                    left.Cell,
                    right.Cell));
            areas = new ReadOnlyCollection<CityAreaPlacement>(copiedAreas);
            cells = new ReadOnlyCollection<CityBlueprintCell>(copiedCells);
            urbanAreas =
                new ReadOnlyCollection<CityAreaPlacement>(copiedUrbanAreas);
            CellBounds = CalculateCellBounds(copiedCells);
            // The lot/road-grid footprint stays normalized to (0,0) so
            // every coordinate-seeded random stream keeps its meaning; an
            // OpenLand/Water fringe (yards) may extend one cell into the
            // negatives around it.
            var roadGridCells = new List<Vector2Int>();
            for (int index = 0; index < copiedCells.Count; index++)
            {
                if (copiedCells[index].ParticipatesInRoadGrid)
                {
                    roadGridCells.Add(copiedCells[index].Cell);
                }
            }

            if (roadGridCells.Count == 0 ||
                CalculateCellBounds(roadGridCells).position !=
                Vector2Int.zero)
            {
                throw new InvalidOperationException(
                    "City blueprint lot cells must be normalized so their " +
                    "minimum X and Z coordinates are zero.");
            }

            if (CellBounds.xMin < OpenFringeMinimumCell ||
                CellBounds.yMin < OpenFringeMinimumCell)
            {
                throw new InvalidOperationException(
                    "The open fringe may extend at most one cell below " +
                    "the normalized lot footprint.");
            }

            MapNodeBounds = CalculateCenteredMapNodeBounds(
                CellBounds,
                CenterNode);
            ValidateConnectedMappedFootprint();
            ValidateConnectedRoadFootprint();
            ValidateStructuralAreas(
                requireCentralPark,
                requireNorthWaterfront);
        }

        public string Id { get; }
        public Vector2Int CenterNode { get; }
        public RectInt CellBounds { get; }
        public RectInt MapNodeBounds { get; }
        public IReadOnlyList<CityAreaPlacement> Areas => areas;
        public IReadOnlyList<CityAreaPlacement> UrbanAreas => urbanAreas;
        public IReadOnlyList<CityBlueprintCell> Cells => cells;
        public CityAreaPlacement CentralPark { get; private set; }
        public CityAreaPlacement NorthWaterfront { get; private set; }
        public CityRiverDefinition River { get; }
        public int LotCellCount { get; private set; }
        internal bool RequiresCentralPark { get; }
        internal bool RequiresNorthWaterfront { get; }

        public bool ContainsCell(Vector2Int cell)
        {
            return cellsByCell.ContainsKey(cell);
        }

        public bool TryGetCell(
            Vector2Int cell,
            out CityBlueprintCell descriptor)
        {
            return cellsByCell.TryGetValue(cell, out descriptor);
        }

        public bool TryGetArea(
            Vector2Int cell,
            out CityAreaDefinition area)
        {
            if (cellsByCell.TryGetValue(
                    cell,
                    out CityBlueprintCell descriptor))
            {
                area = descriptor.Area;
                return true;
            }

            area = null;
            return false;
        }

        public bool TryGetArea(
            string areaId,
            out CityAreaPlacement area)
        {
            return areasById.TryGetValue(
                areaId ?? string.Empty,
                out area);
        }

        public bool CreatesLot(Vector2Int cell)
        {
            return cellsByCell.TryGetValue(
                       cell,
                       out CityBlueprintCell descriptor) &&
                   descriptor.CreatesLot;
        }

        public bool ParticipatesInRoadGrid(Vector2Int cell)
        {
            return cellsByCell.TryGetValue(
                       cell,
                       out CityBlueprintCell descriptor) &&
                   descriptor.ParticipatesInRoadGrid;
        }

        public bool IsParkCell(Vector2Int cell)
        {
            return cellsByCell.TryGetValue(
                       cell,
                       out CityBlueprintCell descriptor) &&
                   descriptor.Area.Feature ==
                   CityAreaFeatureKind.CentralPark;
        }

        public IReadOnlyList<Vector2Int> GetCells(
            CityCellTopologyKind topology)
        {
            var result = new List<Vector2Int>();
            for (int index = 0; index < cells.Count; index++)
            {
                if (cells[index].Topology == topology)
                {
                    result.Add(cells[index].Cell);
                }
            }

            return result;
        }

        private void ValidateAreaCells(CityAreaPlacement area)
        {
            for (int index = 0; index < area.Cells.Count; index++)
            {
                Vector2Int cell = area.Cells[index];
                CityCellTopologyKind topology = area.GetTopology(cell);
                if (cell.x < 0 || cell.y < 0)
                {
                    // Lot-creating cells must stay normalized because the
                    // per-cell random streams are seeded by raw cell
                    // coordinates; only the open fringe may dip below zero,
                    // and only by one bounded row/column.
                    bool lotTopology =
                        topology == CityCellTopologyKind.BuildableLand ||
                        topology == CityCellTopologyKind.ParkLand;
                    if (lotTopology ||
                        cell.x < OpenFringeMinimumCell ||
                        cell.y < OpenFringeMinimumCell)
                    {
                        throw new InvalidOperationException(
                            $"Area '{area.Id}' uses negative cell {cell}.");
                    }
                }

                ValidateTopology(area.Definition, topology);
            }

            bool splitRiverPark =
                River != null &&
                area.Definition.Feature == CityAreaFeatureKind.CentralPark &&
                CountConnectedComponents(area.Cells) == 2;
            if (!IsConnected(area.Cells) && !splitRiverPark)
            {
                throw new InvalidOperationException(
                    $"Area '{area.Id}' must be four-neighbour connected.");
            }
        }

        private static void ValidateTopology(
            CityAreaDefinition definition,
            CityCellTopologyKind topology)
        {
            if (!Enum.IsDefined(typeof(CityCellTopologyKind), topology))
            {
                throw new ArgumentOutOfRangeException(nameof(topology));
            }

            switch (definition.Feature)
            {
                case CityAreaFeatureKind.UrbanDistrict:
                    if (topology != CityCellTopologyKind.BuildableLand)
                    {
                        throw new InvalidOperationException(
                            $"Urban area '{definition.Id}' can contain " +
                            "only buildable land.");
                    }

                    break;
                case CityAreaFeatureKind.CentralPark:
                    if (topology != CityCellTopologyKind.ParkLand)
                    {
                        throw new InvalidOperationException(
                            "The central park can contain only park land.");
                    }

                    break;
                case CityAreaFeatureKind.Cemetery:
                    if (topology != CityCellTopologyKind.OpenLand)
                    {
                        throw new InvalidOperationException(
                            "A cemetery can contain only open land in the MVP.");
                    }

                    break;
                case CityAreaFeatureKind.Yard:
                    if (topology != CityCellTopologyKind.OpenLand)
                    {
                        throw new InvalidOperationException(
                            "A yard can contain only open land in v1.");
                    }

                    break;
                case CityAreaFeatureKind.Lake:
                    if (topology != CityCellTopologyKind.OpenLand &&
                        topology != CityCellTopologyKind.Water)
                    {
                        throw new InvalidOperationException(
                            "A lake area can contain only shoreline and " +
                            "water cells.");
                    }

                    break;
                case CityAreaFeatureKind.NorthWaterfront:
                    if (topology != CityCellTopologyKind.OpenLand &&
                        topology != CityCellTopologyKind.Water)
                    {
                        throw new InvalidOperationException(
                            "The waterfront can contain only beach and " +
                            "water cells.");
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(definition.Feature));
            }
        }

        private void ValidateConnectedMappedFootprint()
        {
            var all = new List<Vector2Int>(cells.Count);
            for (int index = 0; index < cells.Count; index++)
            {
                all.Add(cells[index].Cell);
            }

            if (!IsConnected(all))
            {
                throw new InvalidOperationException(
                    "The complete city footprint must be four-neighbour connected.");
            }
        }

        private void ValidateConnectedRoadFootprint()
        {
            var roadCells = new List<Vector2Int>();
            for (int index = 0; index < cells.Count; index++)
            {
                if (cells[index].ParticipatesInRoadGrid)
                {
                    roadCells.Add(cells[index].Cell);
                    LotCellCount++;
                }
            }

            int componentCount = CountConnectedComponents(roadCells);
            int expectedComponents = River == null ? 1 : 2;
            if (roadCells.Count == 0 || componentCount != expectedComponents)
            {
                throw new InvalidOperationException(
                    River == null
                        ? "Buildable and park cells must form one connected road footprint."
                        : "River cities require exactly two road-grid banks.");
            }

            if (River != null)
            {
                ValidateRiverCorridor();
            }
        }

        private void ValidateRiverCorridor()
        {
            for (int z = River.CoreMinimumZ;
                 z < River.CoreMaximumZExclusive;
                 z++)
            {
                var corridor = new Vector2Int(River.CorridorCellX, z);
                if (ContainsCell(corridor) ||
                    !ParticipatesInRoadGrid(corridor + Vector2Int.left) ||
                    !ParticipatesInRoadGrid(corridor + Vector2Int.right))
                {
                    throw new InvalidOperationException(
                        "The river corridor must be an unmapped column " +
                        "between two complete road-grid banks.");
                }
            }
        }

        private void ValidateStructuralAreas(
            bool requireCentralPark,
            bool requireNorthWaterfront)
        {
            for (int index = 0; index < areas.Count; index++)
            {
                CityAreaPlacement area = areas[index];
                switch (area.Definition.Feature)
                {
                    case CityAreaFeatureKind.UrbanDistrict:
                        break;
                    case CityAreaFeatureKind.CentralPark:
                        if (CentralPark != null)
                        {
                            throw new InvalidOperationException(
                                "A blueprint can contain only one central park.");
                        }

                        CentralPark = area;
                        break;
                    case CityAreaFeatureKind.NorthWaterfront:
                        if (NorthWaterfront != null)
                        {
                            throw new InvalidOperationException(
                                "A blueprint can contain only one north waterfront.");
                        }

                        NorthWaterfront = area;
                        break;
                    case CityAreaFeatureKind.Lake:
                        ValidateMixedOpenWaterArea(area, "lake");
                        break;
                    case CityAreaFeatureKind.Cemetery:
                        break;
                    case CityAreaFeatureKind.Yard:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(area.Definition.Feature));
                }

                if (area.Definition.Feature !=
                    CityAreaFeatureKind.UrbanDistrict)
                {
                    ValidateSpecialAreaRoadAccess(area);
                }
            }

            if (requireCentralPark && CentralPark == null)
            {
                throw new InvalidOperationException(
                    "The blueprint requires one central park.");
            }

            if (CentralPark != null)
            {
                RectInt parkBounds = CalculateCellBounds(CentralPark.Cells);
                if (River == null &&
                    parkBounds.width * parkBounds.height !=
                        CentralPark.Cells.Count)
                {
                    throw new InvalidOperationException(
                        "The central park must be one solid rectangle.");
                }

                if (River == null)
                {
                    var parkCenterNode = new Vector2Int(
                        parkBounds.xMin + parkBounds.width / 2,
                        parkBounds.yMin + parkBounds.height / 2);
                    if (parkBounds.width % 2 != 0 ||
                        parkBounds.height % 2 != 0 ||
                        parkCenterNode != CenterNode)
                    {
                        throw new InvalidOperationException(
                            "The central park must be evenly centered on CityCenterAnchor.");
                    }
                }
                else
                {
                    ValidateSplitRiverPark(CentralPark);
                }
            }

            if (requireNorthWaterfront && NorthWaterfront == null)
            {
                throw new InvalidOperationException(
                    "The blueprint requires one north waterfront.");
            }

            if (NorthWaterfront != null)
            {
                ValidateMixedOpenWaterArea(
                    NorthWaterfront,
                    "north waterfront");
                ValidateNorthWaterfront(NorthWaterfront);
            }
        }

        private void ValidateSplitRiverPark(CityAreaPlacement park)
        {
            if (park.Cells.Count != 16 ||
                CountConnectedComponents(park.Cells) != 2)
            {
                throw new InvalidOperationException(
                    "The river park requires two connected eight-cell banks.");
            }

            int westCount = 0;
            int eastCount = 0;
            for (int index = 0; index < park.Cells.Count; index++)
            {
                int x = park.Cells[index].x;
                if (x < River.CorridorCellX)
                {
                    westCount++;
                }
                else if (x > River.CorridorCellX)
                {
                    eastCount++;
                }
                else
                {
                    throw new InvalidOperationException(
                        "Park land cannot occupy the river corridor.");
                }
            }

            if (westCount != 8 || eastCount != 8)
            {
                throw new InvalidOperationException(
                    "The river park must keep balanced 2x4 sections.");
            }
        }

        private void ValidateMixedOpenWaterArea(
            CityAreaPlacement area,
            string label)
        {
            var openLandCells = new List<Vector2Int>();
            var waterCells = new List<Vector2Int>();
            bool touches = false;
            for (int index = 0; index < area.Cells.Count; index++)
            {
                Vector2Int cell = area.Cells[index];
                CityCellTopologyKind topology = area.GetTopology(cell);
                if (topology == CityCellTopologyKind.OpenLand)
                {
                    openLandCells.Add(cell);
                }
                else if (topology == CityCellTopologyKind.Water)
                {
                    waterCells.Add(cell);
                }

                if (topology != CityCellTopologyKind.OpenLand)
                {
                    continue;
                }

                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int neighbour =
                        cell + CardinalDirections[directionIndex];
                    if (area.TopologyByCell.TryGetValue(
                            neighbour,
                            out CityCellTopologyKind neighbourTopology) &&
                        neighbourTopology == CityCellTopologyKind.Water)
                    {
                        touches = true;
                        break;
                    }
                }
            }

            if (!IsConnected(openLandCells) ||
                !IsConnected(waterCells) ||
                !touches)
            {
                throw new InvalidOperationException(
                    $"The {label} requires separately connected open shore " +
                    "and water cells that touch each other.");
            }

            for (int waterIndex = 0;
                 waterIndex < waterCells.Count;
                 waterIndex++)
            {
                Vector2Int waterCell = waterCells[waterIndex];
                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int neighbour =
                        waterCell + CardinalDirections[directionIndex];
                    if (cellsByCell.TryGetValue(
                            neighbour,
                            out CityBlueprintCell neighbourCell) &&
                        neighbourCell.ParticipatesInRoadGrid)
                    {
                        throw new InvalidOperationException(
                            $"The {label} water must be separated from " +
                            "the road grid by open shoreline cells.");
                    }
                }
            }
        }

        private void ValidateSpecialAreaRoadAccess(CityAreaPlacement area)
        {
            CityCellTopologyKind accessTopology =
                area.Definition.Feature == CityAreaFeatureKind.CentralPark
                    ? CityCellTopologyKind.ParkLand
                    : CityCellTopologyKind.OpenLand;
            for (int cellIndex = 0;
                 cellIndex < area.Cells.Count;
                 cellIndex++)
            {
                Vector2Int cell = area.Cells[cellIndex];
                CityCellTopologyKind topology = area.GetTopology(cell);
                if (topology != accessTopology)
                {
                    continue;
                }

                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int neighbour =
                        cell + CardinalDirections[directionIndex];
                    if (cellsByCell.TryGetValue(
                            neighbour,
                            out CityBlueprintCell neighbourCell) &&
                        !string.Equals(
                            neighbourCell.Area.Id,
                            area.Id,
                            StringComparison.Ordinal) &&
                        neighbourCell.ParticipatesInRoadGrid)
                    {
                        return;
                    }
                }
            }

            throw new InvalidOperationException(
                $"Special area '{area.Id}' must have a {accessTopology} " +
                "edge adjacent to the connected road-grid footprint.");
        }

        private void ValidateNorthWaterfront(CityAreaPlacement waterfront)
        {
            int northernCellZ = CellBounds.yMax - 1;
            // The water row spans the normalized (non-negative) footprint;
            // a negative-fringe yard column carries no waterfront duty.
            for (int x = 0; x < CellBounds.xMax; x++)
            {
                var northernCell = new Vector2Int(x, northernCellZ);
                if (!cellsByCell.TryGetValue(
                        northernCell,
                        out CityBlueprintCell cell) ||
                    !string.Equals(
                        cell.Area.Id,
                        waterfront.Id,
                        StringComparison.Ordinal) ||
                    !cell.IsWater)
                {
                    throw new InvalidOperationException(
                        "The complete north edge of the city bounds must " +
                        "be a continuous row of waterfront water.");
                }
            }
        }

        private static bool IsConnected(IReadOnlyList<Vector2Int> source)
        {
            if (source.Count == 0)
            {
                return false;
            }

            var all = new HashSet<Vector2Int>();
            for (int index = 0; index < source.Count; index++)
            {
                all.Add(source[index]);
            }

            var visited = new HashSet<Vector2Int>();
            var pending = new Queue<Vector2Int>();
            pending.Enqueue(source[0]);
            visited.Add(source[0]);
            while (pending.Count > 0)
            {
                Vector2Int current = pending.Dequeue();
                for (int index = 0;
                     index < CardinalDirections.Length;
                     index++)
                {
                    Vector2Int neighbour =
                        current + CardinalDirections[index];
                    if (all.Contains(neighbour) && visited.Add(neighbour))
                    {
                        pending.Enqueue(neighbour);
                    }
                }
            }

            return visited.Count == all.Count;
        }

        private static int CountConnectedComponents(
            IReadOnlyList<Vector2Int> source)
        {
            var remaining = new HashSet<Vector2Int>();
            for (int index = 0; index < source.Count; index++)
            {
                remaining.Add(source[index]);
            }

            int count = 0;
            var pending = new Queue<Vector2Int>();
            while (remaining.Count > 0)
            {
                Vector2Int start = default;
                foreach (Vector2Int cell in remaining)
                {
                    start = cell;
                    break;
                }

                count++;
                remaining.Remove(start);
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    Vector2Int current = pending.Dequeue();
                    for (int directionIndex = 0;
                         directionIndex < CardinalDirections.Length;
                         directionIndex++)
                    {
                        Vector2Int neighbour =
                            current + CardinalDirections[directionIndex];
                        if (remaining.Remove(neighbour))
                        {
                            pending.Enqueue(neighbour);
                        }
                    }
                }
            }

            return count;
        }

        private static RectInt CalculateCellBounds(
            IReadOnlyList<CityBlueprintCell> source)
        {
            var coordinates = new List<Vector2Int>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                coordinates.Add(source[index].Cell);
            }

            return CalculateCellBounds(coordinates);
        }

        private static RectInt CalculateCellBounds(
            IReadOnlyList<Vector2Int> source)
        {
            int xMin = source[0].x;
            int xMax = source[0].x;
            int zMin = source[0].y;
            int zMax = source[0].y;
            for (int index = 1; index < source.Count; index++)
            {
                Vector2Int cell = source[index];
                xMin = Mathf.Min(xMin, cell.x);
                xMax = Mathf.Max(xMax, cell.x);
                zMin = Mathf.Min(zMin, cell.y);
                zMax = Mathf.Max(zMax, cell.y);
            }

            return new RectInt(
                xMin,
                zMin,
                xMax - xMin + 1,
                zMax - zMin + 1);
        }

        private static RectInt CalculateCenteredMapNodeBounds(
            RectInt cellBounds,
            Vector2Int centerNode)
        {
            int halfWidth = Mathf.Max(
                centerNode.x - cellBounds.xMin,
                cellBounds.xMax - centerNode.x);
            int halfDepth = Mathf.Max(
                centerNode.y - cellBounds.yMin,
                cellBounds.yMax - centerNode.y);
            return new RectInt(
                centerNode.x - halfWidth,
                centerNode.y - halfDepth,
                halfWidth * 2,
                halfDepth * 2);
        }
    }

    public sealed class CityBlueprintBuilder
    {
        private sealed class MutableArea
        {
            public MutableArea(CityAreaDefinition definition)
            {
                Definition = definition;
            }

            public CityAreaDefinition Definition { get; }
            public Dictionary<Vector2Int, CityCellTopologyKind> Cells { get; } =
                new Dictionary<Vector2Int, CityCellTopologyKind>();
        }

        private readonly string id;
        private readonly Vector2Int centerNode;
        private readonly bool requireCentralPark;
        private readonly bool requireNorthWaterfront;
        private CityRiverDefinition river;
        private readonly List<MutableArea> orderedAreas =
            new List<MutableArea>();
        private readonly Dictionary<string, MutableArea> areasById =
            new Dictionary<string, MutableArea>(StringComparer.Ordinal);

        public CityBlueprintBuilder(string id, Vector2Int centerNode)
            : this(id, centerNode, true, true)
        {
        }

        private CityBlueprintBuilder(
            string id,
            Vector2Int centerNode,
            bool requireCentralPark,
            bool requireNorthWaterfront,
            CityRiverDefinition river = null)
        {
            this.id = id;
            this.centerNode = centerNode;
            this.requireCentralPark = requireCentralPark;
            this.requireNorthWaterfront = requireNorthWaterfront;
            this.river = river;
        }

        public static CityBlueprintBuilder From(CityBlueprint source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var builder = new CityBlueprintBuilder(
                source.Id,
                source.CenterNode,
                source.RequiresCentralPark,
                source.RequiresNorthWaterfront,
                source.River);
            for (int areaIndex = 0;
                 areaIndex < source.Areas.Count;
                 areaIndex++)
            {
                CityAreaPlacement area = source.Areas[areaIndex];
                MutableArea mutable = builder.GetOrCreateArea(
                    area.Definition);
                for (int cellIndex = 0;
                     cellIndex < area.Cells.Count;
                     cellIndex++)
                {
                    Vector2Int cell = area.Cells[cellIndex];
                    mutable.Cells.Add(cell, area.GetTopology(cell));
                }
            }

            return builder;
        }

        public CityBlueprintBuilder WithRiver(CityRiverDefinition definition)
        {
            river = definition ??
                throw new ArgumentNullException(nameof(definition));
            return this;
        }

        public CityBlueprintBuilder AddRectangle(
            CityAreaDefinition definition,
            RectInt rectangle,
            CityCellTopologyKind topology)
        {
            if (rectangle.width <= 0 || rectangle.height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rectangle));
            }

            var cells = new List<Vector2Int>(
                checked(rectangle.width * rectangle.height));
            for (int z = rectangle.yMin; z < rectangle.yMax; z++)
            {
                for (int x = rectangle.xMin; x < rectangle.xMax; x++)
                {
                    cells.Add(new Vector2Int(x, z));
                }
            }

            return AddCells(definition, cells, topology);
        }

        public CityBlueprintBuilder AddCells(
            CityAreaDefinition definition,
            IEnumerable<Vector2Int> source,
            CityCellTopologyKind topology)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            MutableArea area = GetOrCreateArea(definition);
            foreach (Vector2Int cell in source)
            {
                if (!area.Cells.TryAdd(cell, topology))
                {
                    throw new InvalidOperationException(
                        $"Area '{definition.Id}' repeats cell {cell}.");
                }
            }

            return this;
        }

        public CityBlueprintBuilder RemoveArea(string areaId)
        {
            if (string.IsNullOrWhiteSpace(areaId))
            {
                throw new ArgumentException(
                    "A city area requires a stable ID.",
                    nameof(areaId));
            }

            string normalized = areaId.Trim();
            if (!areasById.TryGetValue(
                    normalized,
                    out MutableArea area))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(areaId),
                    areaId,
                    "The requested city area does not exist.");
            }

            areasById.Remove(normalized);
            orderedAreas.Remove(area);
            return this;
        }

        public CityBlueprintBuilder SwapUrbanAreas(
            string firstAreaId,
            string secondAreaId)
        {
            MutableArea first = GetExistingArea(
                firstAreaId,
                nameof(firstAreaId));
            MutableArea second = GetExistingArea(
                secondAreaId,
                nameof(secondAreaId));
            if (ReferenceEquals(first, second))
            {
                return this;
            }

            if (first.Definition.Feature !=
                    CityAreaFeatureKind.UrbanDistrict ||
                second.Definition.Feature !=
                    CityAreaFeatureKind.UrbanDistrict)
            {
                throw new InvalidOperationException(
                    "Only movable urban areas can be swapped. Structural " +
                    "park and waterfront anchors remain fixed.");
            }

            var firstCells = new List<Vector2Int>(first.Cells.Keys);
            var secondCells = new List<Vector2Int>(second.Cells.Keys);
            first.Cells.Clear();
            second.Cells.Clear();
            for (int index = 0; index < secondCells.Count; index++)
            {
                first.Cells.Add(
                    secondCells[index],
                    CityCellTopologyKind.BuildableLand);
            }

            for (int index = 0; index < firstCells.Count; index++)
            {
                second.Cells.Add(
                    firstCells[index],
                    CityCellTopologyKind.BuildableLand);
            }

            return this;
        }

        public CityBlueprintBuilder SetAreaCells(
            CityAreaDefinition definition,
            IEnumerable<Vector2Int> source,
            CityCellTopologyKind topology)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var replacement = new MutableArea(definition);
            foreach (Vector2Int cell in source)
            {
                if (!replacement.Cells.TryAdd(cell, topology))
                {
                    throw new InvalidOperationException(
                        $"Area '{definition.Id}' repeats cell {cell}.");
                }
            }

            if (replacement.Cells.Count == 0)
            {
                throw new ArgumentException(
                    $"Area '{definition.Id}' must contain at least one cell.",
                    nameof(source));
            }

            if (areasById.TryGetValue(
                    definition.Id,
                    out MutableArea existing))
            {
                int areaIndex = orderedAreas.IndexOf(existing);
                orderedAreas[areaIndex] = replacement;
                areasById[definition.Id] = replacement;
            }
            else
            {
                areasById.Add(definition.Id, replacement);
                orderedAreas.Add(replacement);
            }

            return this;
        }

        public CityBlueprint Build()
        {
            return Build(
                requireCentralPark,
                requireNorthWaterfront);
        }

        internal CityBlueprint Build(
            bool requireCentralPark,
            bool requireNorthWaterfront)
        {
            var placements = new List<CityAreaPlacement>(
                orderedAreas.Count);
            for (int index = 0; index < orderedAreas.Count; index++)
            {
                MutableArea area = orderedAreas[index];
                placements.Add(new CityAreaPlacement(
                    area.Definition,
                    area.Cells));
            }

            return new CityBlueprint(
                id,
                centerNode,
                placements,
                river,
                requireCentralPark,
                requireNorthWaterfront);
        }

        private MutableArea GetOrCreateArea(CityAreaDefinition definition)
        {
            if (areasById.TryGetValue(
                    definition.Id,
                    out MutableArea existing))
            {
                if (!ReferenceEquals(existing.Definition, definition))
                {
                    throw new InvalidOperationException(
                        $"Area ID '{definition.Id}' uses multiple definitions.");
                }

                return existing;
            }

            var created = new MutableArea(definition);
            areasById.Add(definition.Id, created);
            orderedAreas.Add(created);
            return created;
        }

        private MutableArea GetExistingArea(
            string areaId,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(areaId))
            {
                throw new ArgumentException(
                    "A city area requires a stable ID.",
                    parameterName);
            }

            string normalized = areaId.Trim();
            if (!areasById.TryGetValue(
                    normalized,
                    out MutableArea area))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    areaId,
                    "The requested city area does not exist.");
            }

            return area;
        }
    }
}
