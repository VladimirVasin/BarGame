using System;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class CityGenerationSettings
    {
        public const float DefaultRoadWidth = 8f;
        public const float DefaultMinimumOrdinaryBuildingHeight = 36f;
        public const float DefaultMaximumOrdinaryBuildingHeight = 52f;
        public const float FogHiddenRoofReferenceCameraHeight = 4f;
        public const float MaximumFogHiddenRoofTransmittance = 0.01f;

        [Min(1)] public int BlocksX = 12;
        [Min(1)] public int BlocksZ = 12;
        [Min(0)] public int BarCount = 4;
        [Min(0)] public int ParkBlocksX = 4;
        [Min(0)] public int ParkBlocksZ = 4;
        [Min(0f)] public float MinimumBarRouteDistance = 120f;
        [Min(1f)] public float BlockWidth = 18f;
        [Min(1f)] public float BlockDepth = 18f;
        [Min(2.01f)] public float RoadWidth = DefaultRoadWidth;
        [Range(0f, 1f)] public float LoopChance = 0.28f;
        [Min(0f)] public float BuildingInset = 1.25f;
        // Bars retain this original 5-13 m range. The home and supermarket
        // also keep using its maximum as their existing authored clamp.
        [Min(0.1f)] public float MinimumBuildingHeight = 5f;
        [Min(0.1f)] public float MaximumBuildingHeight = 13f;
        // At the production City fog density, even the lowest ordinary roof
        // retains less than one percent of its colour from the highest
        // ordinary chase-camera position. The upper range may pass the fixed
        // far plane, so these masses read as continuing into the fog.
        [Min(0.1f)] public float MinimumOrdinaryBuildingHeight =
            DefaultMinimumOrdinaryBuildingHeight;
        [Min(0.1f)] public float MaximumOrdinaryBuildingHeight =
            DefaultMaximumOrdinaryBuildingHeight;

        internal CityBlueprint Blueprint { get; set; }

        public static CityGenerationSettings Default => new CityGenerationSettings();

        public Vector2 NodeSpacing =>
            new Vector2(BlockWidth + RoadWidth, BlockDepth + RoadWidth);

        public Vector2Int BlockCount => Blueprint != null
            ? new Vector2Int(
                Blueprint.CellBounds.xMax,
                Blueprint.CellBounds.yMax)
            : new Vector2Int(BlocksX, BlocksZ);

        public Vector2Int EffectiveParkBlockCount
        {
            get
            {
                if (Blueprint?.CentralPark != null)
                {
                    RectInt bounds = GetCellBounds(
                        Blueprint.CentralPark.Cells);
                    if (Blueprint.River != null)
                    {
                        int height = Mathf.Max(1, bounds.height);
                        int width = Mathf.CeilToInt(
                            Blueprint.CentralPark.Cells.Count /
                            (float)height);
                        return new Vector2Int(
                            width,
                            height);
                    }

                    return new Vector2Int(
                        bounds.width,
                        bounds.height);
                }

                if (BlocksX < 6 || BlocksZ < 6 ||
                    ParkBlocksX <= 0 || ParkBlocksZ <= 0)
                {
                    return Vector2Int.zero;
                }

                return new Vector2Int(
                    Mathf.Min(ParkBlocksX, BlocksX - 2),
                    Mathf.Min(ParkBlocksZ, BlocksZ - 2));
            }
        }

        public Vector2Int ParkCellMinimum
        {
            get
            {
                if (Blueprint?.CentralPark != null)
                {
                    return GetCellBounds(
                        Blueprint.CentralPark.Cells).position;
                }

                Vector2Int count = EffectiveParkBlockCount;
                return count == Vector2Int.zero
                    ? Vector2Int.zero
                    : new Vector2Int(
                        (BlocksX - count.x) / 2,
                        (BlocksZ - count.y) / 2);
            }
        }

        public bool IsParkCell(Vector2Int cell)
        {
            if (Blueprint != null)
            {
                return Blueprint.IsParkCell(cell);
            }

            Vector2Int count = EffectiveParkBlockCount;
            if (count == Vector2Int.zero)
            {
                return false;
            }

            Vector2Int minimum = ParkCellMinimum;
            return cell.x >= minimum.x &&
                   cell.x < minimum.x + count.x &&
                   cell.y >= minimum.y &&
                   cell.y < minimum.y + count.y;
        }

        public CityGenerationSettings Copy()
        {
            return new CityGenerationSettings
            {
                BlocksX = BlocksX,
                BlocksZ = BlocksZ,
                BarCount = BarCount,
                ParkBlocksX = ParkBlocksX,
                ParkBlocksZ = ParkBlocksZ,
                MinimumBarRouteDistance = MinimumBarRouteDistance,
                BlockWidth = BlockWidth,
                BlockDepth = BlockDepth,
                RoadWidth = RoadWidth,
                LoopChance = LoopChance,
                BuildingInset = BuildingInset,
                MinimumBuildingHeight = MinimumBuildingHeight,
                MaximumBuildingHeight = MaximumBuildingHeight,
                MinimumOrdinaryBuildingHeight =
                    MinimumOrdinaryBuildingHeight,
                MaximumOrdinaryBuildingHeight =
                    MaximumOrdinaryBuildingHeight,
                Blueprint = Blueprint
            };
        }

        public bool IsCityCell(Vector2Int cell)
        {
            return Blueprint == null
                ? cell.x >= 0 &&
                  cell.x < BlocksX &&
                  cell.y >= 0 &&
                  cell.y < BlocksZ
                : Blueprint.ContainsCell(cell);
        }

        public bool CreatesLot(Vector2Int cell)
        {
            return Blueprint == null
                ? IsCityCell(cell)
                : Blueprint.CreatesLot(cell);
        }

        public bool ParticipatesInRoadGrid(Vector2Int cell)
        {
            return Blueprint == null
                ? IsCityCell(cell)
                : Blueprint.ParticipatesInRoadGrid(cell);
        }

        public bool TryGetArea(
            Vector2Int cell,
            out CityAreaDefinition area)
        {
            if (Blueprint != null)
            {
                return Blueprint.TryGetArea(cell, out area);
            }

            area = null;
            return false;
        }

        public void Validate()
        {
            if (BlocksX < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(BlocksX));
            }

            if (BlocksZ < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(BlocksZ));
            }

            if (ParkBlocksX < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ParkBlocksX));
            }

            if (ParkBlocksZ < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ParkBlocksZ));
            }

            int lotCount = Blueprint != null
                ? Blueprint.LotCellCount
                : checked(BlocksX * BlocksZ);
            Vector2Int parkCount = EffectiveParkBlockCount;
            int buildableLotCount =
                lotCount - (Blueprint?.CentralPark != null
                    ? Blueprint.CentralPark.Cells.Count
                    : checked(parkCount.x * parkCount.y));
            if (BarCount < 0 || BarCount > buildableLotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(BarCount));
            }

            RequireNonNegativeFinite(
                MinimumBarRouteDistance,
                nameof(MinimumBarRouteDistance));
            RequirePositiveFinite(BlockWidth, nameof(BlockWidth));
            RequirePositiveFinite(BlockDepth, nameof(BlockDepth));
            RequirePositiveFinite(RoadWidth, nameof(RoadWidth));
            if (RoadWidth <=
                CityStreetSurfacePlanner.SidewalkWidth * 2f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(RoadWidth),
                    RoadWidth,
                    "Road width must leave positive carriageway space " +
                    "between both sidewalks.");
            }

            RequireNonNegativeFinite(BuildingInset, nameof(BuildingInset));
            RequirePositiveFinite(MinimumBuildingHeight, nameof(MinimumBuildingHeight));
            RequirePositiveFinite(MaximumBuildingHeight, nameof(MaximumBuildingHeight));
            RequirePositiveFinite(
                MinimumOrdinaryBuildingHeight,
                nameof(MinimumOrdinaryBuildingHeight));
            RequirePositiveFinite(
                MaximumOrdinaryBuildingHeight,
                nameof(MaximumOrdinaryBuildingHeight));

            if (!IsFinite(LoopChance) || LoopChance < 0f || LoopChance > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(LoopChance));
            }

            if (BuildingInset * 2f >= BlockWidth ||
                BuildingInset * 2f >= BlockDepth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(BuildingInset),
                    "Building inset must leave a positive building footprint.");
            }

            if (MaximumBuildingHeight < MinimumBuildingHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumBuildingHeight),
                    "Maximum height must be at least the minimum height.");
            }

            if (MaximumOrdinaryBuildingHeight <
                MinimumOrdinaryBuildingHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumOrdinaryBuildingHeight),
                    "Maximum ordinary-building height must be at least " +
                    "the minimum ordinary-building height.");
            }
        }

        private static void RequirePositiveFinite(float value, string parameterName)
        {
            if (!IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void RequireNonNegativeFinite(float value, string parameterName)
        {
            if (!IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static RectInt GetCellBounds(
            System.Collections.Generic.IReadOnlyList<Vector2Int> cells)
        {
            int xMin = cells[0].x;
            int xMax = cells[0].x;
            int zMin = cells[0].y;
            int zMax = cells[0].y;
            for (int index = 1; index < cells.Count; index++)
            {
                Vector2Int cell = cells[index];
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
    }
}
