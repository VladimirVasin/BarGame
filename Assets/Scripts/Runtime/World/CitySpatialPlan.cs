using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Maps stable city grid identities to metres. Street topology remains
    /// orthogonal; callers use this plan rather than assuming a uniform step.
    /// Offsets exclude the world origin and elevation.
    /// </summary>
    public sealed class CitySpatialPlan
    {
        // Entries include coordinate -1, the service fringe. Urban blocks keep
        // their real 26 m street pitch; compactness comes from removing the
        // stretched approaches and narrowing the outlying service ground.
        private static readonly float[] CoastalXOffsets =
        {
            14f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, -8.5f, -17f, -25.5f, -34f
        };

        private static readonly float[] CoastalZOffsets =
        {
            14f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            0f, 0f, 0f, 0f, 0f, 0f, 2f, 2f
        };

        private readonly Axis xAxis;
        private readonly Axis zAxis;

        private CitySpatialPlan(Vector2 nominalNodeSpacing,
            float[] xOffsets, float[] zOffsets, bool compactCoastal = false)
        {
            ValidateSpacing(nominalNodeSpacing.x, nameof(nominalNodeSpacing));
            ValidateSpacing(nominalNodeSpacing.y, nameof(nominalNodeSpacing));
            NominalNodeSpacing = nominalNodeSpacing;
            xAxis = new Axis(nominalNodeSpacing.x, xOffsets, compactCoastal ? -1 : 0);
            zAxis = new Axis(nominalNodeSpacing.y, zOffsets, compactCoastal ? -1 : 0);
            IsUniform = xOffsets == null && zOffsets == null;
            IsCompactCoastal = compactCoastal;
        }

        public Vector2 NominalNodeSpacing { get; }
        public bool IsUniform { get; }
        public bool IsCompactCoastal { get; }

        public static CitySpatialPlan Uniform(Vector2 nodeSpacing)
        {
            return new CitySpatialPlan(nodeSpacing, null, null);
        }

        public static CitySpatialPlan Create(
            CityBlueprint blueprint, CityGenerationSettings settings)
        {
            if (blueprint == null)
                throw new ArgumentNullException(nameof(blueprint));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            // Authored extensions belong to the production coastal footprint.
            // Other blueprints and custom-metre authoring retain their old map.
            bool coastal = blueprint.Id == CityBlueprintCatalog.DefaultBlueprintId &&
                blueprint.CenterNode == new Vector2Int(6, 6) &&
                blueprint.CellBounds == new RectInt(-1, -1, 18, 15) &&
                blueprint.LotCellCount == 144 && blueprint.River != null &&
                settings.BlockWidth == 18f && settings.BlockDepth == 18f &&
                settings.RoadWidth == CityGenerationSettings.DefaultRoadWidth;
            return coastal
                ? new CitySpatialPlan(settings.NodeSpacing,
                    CoastalXOffsets, CoastalZOffsets, compactCoastal: true)
                : Uniform(settings.NodeSpacing);
        }

        public Vector2 GetCoordinateWorldOffset(Vector2Int coordinate)
        {
            return GetCoordinateWorldOffset((Vector2)coordinate);
        }

        public Vector2 GetCoordinateWorldOffset(Vector2 coordinate)
        {
            return new Vector2(xAxis.Forward(coordinate.x),
                zAxis.Forward(coordinate.y));
        }

        /// <summary>
        /// Inverse continuous coordinate map. Subtract the world origin before
        /// querying; floor its result to identify a ground cell. Outside the
        /// authored axes both maps extrapolate at the nominal step, preserving
        /// the depth of the one-cell service fringe.
        /// </summary>
        public Vector2 WorldToGrid(Vector2 worldOffset)
        {
            return new Vector2(xAxis.Inverse(worldOffset.x),
                zAxis.Inverse(worldOffset.y));
        }

        public Rect GetCellBounds(Vector2Int cell)
        {
            Vector2 minimum = GetCoordinateWorldOffset(cell);
            Vector2 maximum = GetCoordinateWorldOffset(cell + Vector2Int.one);
            return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        }

        public Vector2 GetCellSize(Vector2Int cell)
        {
            return GetCellBounds(cell).size;
        }

        public float GetNodeSpan(RoadEdge edge)
        {
            return Vector2.Distance(GetCoordinateWorldOffset(edge.A),
                GetCoordinateWorldOffset(edge.B));
        }

        private static void ValidateSpacing(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name,
                    "Spatial steps must be positive finite metres.");
        }

        private sealed class Axis
        {
            private readonly float nominalStep;
            private readonly float[] coordinates;
            private readonly int firstCoordinate;

            internal Axis(float nominalStep, float[] offsets, int firstCoordinate = 0)
            {
                this.nominalStep = nominalStep;
                this.firstCoordinate = firstCoordinate;
                if (offsets == null) return;
                coordinates = new float[offsets.Length];
                for (int index = 0; index < offsets.Length; index++)
                {
                    coordinates[index] = (index + firstCoordinate) * nominalStep + offsets[index];
                    if (index > 0 && coordinates[index] <= coordinates[index - 1])
                        throw new ArgumentException(
                            "Spatial axis coordinates must increase.", nameof(offsets));
                }
            }

            internal float Forward(float coordinate)
            {
                if (coordinates == null) return coordinate * nominalStep;
                if (float.IsNaN(coordinate) || float.IsInfinity(coordinate))
                    return coordinate;
                coordinate -= firstCoordinate;
                int last = coordinates.Length - 1;
                if (coordinate <= 0f) return coordinates[0] + coordinate * nominalStep;
                if (coordinate >= last)
                    return coordinates[last] + (coordinate - last) * nominalStep;
                int first = Mathf.FloorToInt(coordinate);
                return Mathf.LerpUnclamped(coordinates[first], coordinates[first + 1],
                    coordinate - first);
            }

            internal float Inverse(float offset)
            {
                if (coordinates == null) return offset / nominalStep;
                if (float.IsNaN(offset) || float.IsInfinity(offset)) return offset;
                int last = coordinates.Length - 1;
                if (offset <= coordinates[0]) return firstCoordinate + (offset - coordinates[0]) / nominalStep;
                if (offset >= coordinates[last])
                    return firstCoordinate + last + (offset - coordinates[last]) / nominalStep;
                int lower = 0;
                int upper = last;
                while (upper - lower > 1)
                {
                    int middle = (lower + upper) / 2;
                    if (coordinates[middle] <= offset) lower = middle;
                    else upper = middle;
                }
                return firstCoordinate + lower + (offset - coordinates[lower]) /
                    (coordinates[upper] - coordinates[lower]);
            }
        }
    }
}
