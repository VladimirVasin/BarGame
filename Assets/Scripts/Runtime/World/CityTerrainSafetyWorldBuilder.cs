using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class CityTerrainSafetyWorldBuilder
    {
        private const float RoadRailSegmentLength = 3f;
        private const float RailHeight = 1.05f;
        private const float RailThickness = 0.16f;

        private static readonly Color RailColor =
            new Color(0.16f, 0.19f, 0.18f);

        internal static GameObject Build(
            Transform parent,
            CityLayout layout)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var byCell = new Dictionary<Vector2Int,
                CitySurfaceDescriptor>();
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                byCell[surface.Cell] = surface;
            }

            var rails = new List<Bounds>();
            CityRoadGroundBoundaryPlan roadGroundBoundaries =
                CityRoadGroundBoundaryPlanner.Create(layout);
            for (int index = 0;
                 index < roadGroundBoundaries.ProtectedDrops.Count;
                 index++)
            {
                CityRoadGroundBoundarySpan span =
                    roadGroundBoundaries.ProtectedDrops[index];
                if (span.Surface.Kind == CitySurfaceKind.RiverWater)
                {
                    continue;
                }

                AddRoadBoundaryRailSegments(
                    span,
                    rails);
            }

            foreach (KeyValuePair<Vector2Int, CitySurfaceDescriptor> pair
                     in byCell)
            {
                AddBoundaryIfRequired(
                    layout,
                    pair.Value,
                    pair.Key + Vector2Int.right,
                    Vector2Int.right,
                    byCell,
                    rails);
                AddBoundaryIfRequired(
                    layout,
                    pair.Value,
                    pair.Key + Vector2Int.up,
                    Vector2Int.up,
                    byCell,
                    rails);
            }

            if (rails.Count == 0)
            {
                return null;
            }

            Transform root = new GameObject(
                "Protected Terrain Drops").transform;
            root.SetParent(parent, false);
            return RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Terrain Guard Rails",
                root,
                rails,
                RailColor,
                true);
        }

        private static void AddRoadBoundaryRailSegments(
            CityRoadGroundBoundarySpan span,
            ICollection<Bounds> destination)
        {
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    span.Length /
                    RoadRailSegmentLength));
            for (int index = 0; index < segmentCount; index++)
            {
                float first = Mathf.Lerp(
                    span.MinimumCoordinate,
                    span.MaximumCoordinate,
                    index / (float)segmentCount);
                float second = Mathf.Lerp(
                    span.MinimumCoordinate,
                    span.MaximumCoordinate,
                    (index + 1f) / segmentCount);
                float middle = (first + second) * 0.5f;
                float firstAmount = index / (float)segmentCount;
                float secondAmount = (index + 1f) / segmentCount;
                float firstRoadTop = Mathf.Lerp(
                    span.FirstTravelTopY,
                    span.SecondTravelTopY,
                    firstAmount);
                float secondRoadTop = Mathf.Lerp(
                    span.FirstTravelTopY,
                    span.SecondTravelTopY,
                    secondAmount);

                float minimumBase = Mathf.Min(
                    span.GroundTopY,
                    firstRoadTop,
                    secondRoadTop);
                float maximumBase = Mathf.Max(
                    span.GroundTopY,
                    firstRoadTop,
                    secondRoadTop);
                float height = RailHeight +
                               maximumBase - minimumBase;
                Vector3 center = span.IsHorizontal
                    ? new Vector3(
                        middle,
                        minimumBase + height * 0.5f,
                        span.FixedCoordinate)
                    : new Vector3(
                        span.FixedCoordinate,
                        minimumBase + height * 0.5f,
                        middle);
                Vector3 size = span.IsHorizontal
                    ? new Vector3(
                        second - first,
                        height,
                        RailThickness)
                    : new Vector3(
                        RailThickness,
                        height,
                        second - first);
                destination.Add(new Bounds(center, size));
            }
        }

        private static void AddBoundaryIfRequired(
            CityLayout layout,
            CitySurfaceDescriptor first,
            Vector2Int neighbourCell,
            Vector2Int direction,
            IReadOnlyDictionary<Vector2Int, CitySurfaceDescriptor> byCell,
            ICollection<Bounds> destination)
        {
            if (!byCell.TryGetValue(
                    neighbourCell,
                    out CitySurfaceDescriptor second) ||
                first.Kind == CitySurfaceKind.RiverWater ||
                second.Kind == CitySurfaceKind.RiverWater ||
                layout.HasRoad(
                    RoadEdge.ForCellFrontage(first.Cell, direction)) ||
                Mathf.Abs(
                    first.PhysicalTopY - second.PhysicalTopY) <=
                CityRoadGroundBoundaryPlanner.MaximumSafeStep + 0.001f)
            {
                return;
            }

            float highTop = Mathf.Max(
                first.PhysicalTopY,
                second.PhysicalTopY);
            if (direction == Vector2Int.right)
            {
                float zMin = Mathf.Max(
                    first.WorldBounds.yMin,
                    second.WorldBounds.yMin);
                float zMax = Mathf.Min(
                    first.WorldBounds.yMax,
                    second.WorldBounds.yMax);
                if (zMax <= zMin)
                {
                    return;
                }

                destination.Add(new Bounds(
                    new Vector3(
                        (first.WorldBounds.xMax +
                         second.WorldBounds.xMin) * 0.5f,
                        highTop + RailHeight * 0.5f,
                        (zMin + zMax) * 0.5f),
                    new Vector3(
                        RailThickness,
                        RailHeight,
                        zMax - zMin)));
                return;
            }

            float xMin = Mathf.Max(
                first.WorldBounds.xMin,
                second.WorldBounds.xMin);
            float xMax = Mathf.Min(
                first.WorldBounds.xMax,
                second.WorldBounds.xMax);
            if (xMax <= xMin)
            {
                return;
            }

            destination.Add(new Bounds(
                new Vector3(
                    (xMin + xMax) * 0.5f,
                    highTop + RailHeight * 0.5f,
                    (first.WorldBounds.yMax +
                     second.WorldBounds.yMin) * 0.5f),
                new Vector3(
                    xMax - xMin,
                    RailHeight,
                    RailThickness)));
        }

    }
}
