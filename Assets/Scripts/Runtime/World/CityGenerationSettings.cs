using System;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class CityGenerationSettings
    {
        [Min(1)] public int BlocksX = 4;
        [Min(1)] public int BlocksZ = 4;
        [Min(0)] public int BarCount = 4;
        [Min(1f)] public float BlockWidth = 18f;
        [Min(1f)] public float BlockDepth = 18f;
        [Min(0.1f)] public float RoadWidth = 6f;
        [Range(0f, 1f)] public float LoopChance = 0.28f;
        [Min(0f)] public float BuildingInset = 1.25f;
        [Min(0.1f)] public float MinimumBuildingHeight = 5f;
        [Min(0.1f)] public float MaximumBuildingHeight = 13f;

        public static CityGenerationSettings Default => new CityGenerationSettings();

        public Vector2 NodeSpacing =>
            new Vector2(BlockWidth + RoadWidth, BlockDepth + RoadWidth);

        public Vector2Int BlockCount => new Vector2Int(BlocksX, BlocksZ);

        public CityGenerationSettings Copy()
        {
            return new CityGenerationSettings
            {
                BlocksX = BlocksX,
                BlocksZ = BlocksZ,
                BarCount = BarCount,
                BlockWidth = BlockWidth,
                BlockDepth = BlockDepth,
                RoadWidth = RoadWidth,
                LoopChance = LoopChance,
                BuildingInset = BuildingInset,
                MinimumBuildingHeight = MinimumBuildingHeight,
                MaximumBuildingHeight = MaximumBuildingHeight
            };
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

            int lotCount = checked(BlocksX * BlocksZ);
            if (BarCount < 0 || BarCount > lotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(BarCount));
            }

            RequirePositiveFinite(BlockWidth, nameof(BlockWidth));
            RequirePositiveFinite(BlockDepth, nameof(BlockDepth));
            RequirePositiveFinite(RoadWidth, nameof(RoadWidth));
            RequireNonNegativeFinite(BuildingInset, nameof(BuildingInset));
            RequirePositiveFinite(MinimumBuildingHeight, nameof(MinimumBuildingHeight));
            RequirePositiveFinite(MaximumBuildingHeight, nameof(MaximumBuildingHeight));

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
    }
}
