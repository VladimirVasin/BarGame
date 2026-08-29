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
            CityLayout layout,
            Rect? railSuppressionFootprint = null)
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
                    rails,
                    railSuppressionFootprint);
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
                    rails,
                    railSuppressionFootprint);
                AddBoundaryIfRequired(
                    layout,
                    pair.Value,
                    pair.Key + Vector2Int.up,
                    Vector2Int.up,
                    byCell,
                    rails,
                    railSuppressionFootprint);
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
            ICollection<Bounds> destination,
            Rect? railSuppressionFootprint)
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
                float firstGroundTop = Mathf.Lerp(
                    span.FirstGroundTopY,
                    span.SecondGroundTopY,
                    firstAmount);
                float secondGroundTop = Mathf.Lerp(
                    span.FirstGroundTopY,
                    span.SecondGroundTopY,
                    secondAmount);

                float minimumBase = Mathf.Min(
                    firstGroundTop,
                    secondGroundTop,
                    firstRoadTop,
                    secondRoadTop);
                float maximumBase = Mathf.Max(
                    firstGroundTop,
                    secondGroundTop,
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
                AddWithSuppression(
                    new Bounds(center, size),
                    span.IsHorizontal,
                    destination,
                    railSuppressionFootprint);
            }
        }

        /// <summary>
        /// Water whose edge a precinct builder authors physically:
        /// the river's quay walls.
        ///
        /// A generic rail on one of these boundaries stands on ground
        /// that visibly continues past it, which is the invisible
        /// perimeter this project does not build. The precinct owes a
        /// visible barrier in its place, and the river pays it.
        ///
        /// The sea's waterfront row is deliberately not included: the
        /// beach-to-sea step sits inside the safe-step budget, so the
        /// generic pass never rails the waterline, and the seacoast's
        /// own raised decks carry their parapets under the coast
        /// planner's validated continuity contracts.
        /// </summary>
        private static bool IsAuthoredWaterEdge(
            CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.RiverWater;
        }

        private static void AddBoundaryIfRequired(
            CityLayout layout,
            CitySurfaceDescriptor first,
            Vector2Int neighbourCell,
            Vector2Int direction,
            IReadOnlyDictionary<Vector2Int, CitySurfaceDescriptor> byCell,
            ICollection<Bounds> destination,
            Rect? railSuppressionFootprint)
        {
            if (!byCell.TryGetValue(
                    neighbourCell,
                    out CitySurfaceDescriptor second) ||
                IsAuthoredWaterEdge(first) ||
                IsAuthoredWaterEdge(second) ||
                layout.HasRoad(
                    RoadEdge.ForCellFrontage(first.Cell, direction)))
            {
                return;
            }

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

                AddGroundBoundaryRailSegments(
                    layout,
                    first,
                    second,
                    false,
                    (first.WorldBounds.xMax +
                     second.WorldBounds.xMin) * 0.5f,
                    zMin,
                    zMax,
                    destination,
                    railSuppressionFootprint);
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

            AddGroundBoundaryRailSegments(
                layout,
                first,
                second,
                true,
                (first.WorldBounds.yMax +
                 second.WorldBounds.yMin) * 0.5f,
                xMin,
                xMax,
                destination,
                railSuppressionFootprint);
        }

        private static void AddGroundBoundaryRailSegments(
            CityLayout layout,
            CitySurfaceDescriptor firstSurface,
            CitySurfaceDescriptor secondSurface,
            bool isHorizontal,
            float fixedCoordinate,
            float minimumCoordinate,
            float maximumCoordinate,
            ICollection<Bounds> destination,
            Rect? railSuppressionFootprint)
        {
            if (CityRoadGroundBoundaryPlanner.IsGroundBoundarySafe(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    minimumCoordinate,
                    maximumCoordinate))
            {
                return;
            }

            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (maximumCoordinate - minimumCoordinate) /
                    RoadRailSegmentLength));
            for (int index = 0; index < segmentCount; index++)
            {
                float segmentMinimum = Mathf.Lerp(
                    minimumCoordinate,
                    maximumCoordinate,
                    index / (float)segmentCount);
                float segmentMaximum = Mathf.Lerp(
                    minimumCoordinate,
                    maximumCoordinate,
                    (index + 1f) / segmentCount);
                float segmentMiddle =
                    (segmentMinimum + segmentMaximum) * 0.5f;
                if (CityRoadGroundBoundaryPlanner.IsGroundBoundarySafe(
                        layout,
                        firstSurface,
                        secondSurface,
                        isHorizontal,
                        fixedCoordinate,
                        segmentMinimum,
                        segmentMaximum))
                {
                    continue;
                }

                float firstBase = SampleHigherTop(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    segmentMinimum);
                float middleBase = SampleHigherTop(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    segmentMiddle);
                float secondBase = SampleHigherTop(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    segmentMaximum);
                float firstLowerBase = SampleLowerTop(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    segmentMinimum);
                float middleLowerBase = SampleLowerTop(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    segmentMiddle);
                float secondLowerBase = SampleLowerTop(
                    layout,
                    firstSurface,
                    secondSurface,
                    isHorizontal,
                    fixedCoordinate,
                    segmentMaximum);
                float minimumBase = Mathf.Min(
                    firstLowerBase,
                    middleLowerBase,
                    secondLowerBase);
                float maximumBase = Mathf.Max(
                    firstBase,
                    middleBase,
                    secondBase);
                float height = RailHeight + maximumBase - minimumBase;
                Vector3 center = isHorizontal
                    ? new Vector3(
                        segmentMiddle,
                        minimumBase + height * 0.5f,
                        fixedCoordinate)
                    : new Vector3(
                        fixedCoordinate,
                        minimumBase + height * 0.5f,
                        segmentMiddle);
                Vector3 size = isHorizontal
                    ? new Vector3(
                        segmentMaximum - segmentMinimum,
                        height,
                        RailThickness)
                    : new Vector3(
                        RailThickness,
                        height,
                        segmentMaximum - segmentMinimum);
                AddWithSuppression(
                    new Bounds(center, size),
                    isHorizontal,
                    destination,
                    railSuppressionFootprint);
            }
        }

        private static void AddWithSuppression(
            Bounds bounds,
            bool isHorizontal,
            ICollection<Bounds> destination,
            Rect? railSuppressionFootprint)
        {
            if (!railSuppressionFootprint.HasValue)
            {
                destination.Add(bounds);
                return;
            }

            var footprint = Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
            Rect suppression = railSuppressionFootprint.Value;
            if (!footprint.Overlaps(suppression, true))
            {
                destination.Add(bounds);
                return;
            }

            float minimum = isHorizontal ? bounds.min.x : bounds.min.z;
            float maximum = isHorizontal ? bounds.max.x : bounds.max.z;
            float cutMinimum = Mathf.Max(
                minimum,
                isHorizontal ? suppression.xMin : suppression.yMin);
            float cutMaximum = Mathf.Min(
                maximum,
                isHorizontal ? suppression.xMax : suppression.yMax);
            AddRailSlice(
                bounds,
                isHorizontal,
                minimum,
                cutMinimum,
                destination);
            AddRailSlice(
                bounds,
                isHorizontal,
                cutMaximum,
                maximum,
                destination);
        }

        private static void AddRailSlice(
            Bounds source,
            bool isHorizontal,
            float minimum,
            float maximum,
            ICollection<Bounds> destination)
        {
            const float minimumLength = 0.02f;
            if (maximum - minimum <= minimumLength)
            {
                return;
            }

            Vector3 center = source.center;
            Vector3 size = source.size;
            if (isHorizontal)
            {
                center.x = (minimum + maximum) * 0.5f;
                size.x = maximum - minimum;
            }
            else
            {
                center.z = (minimum + maximum) * 0.5f;
                size.z = maximum - minimum;
            }

            destination.Add(new Bounds(center, size));
        }

        private static float SampleHigherTop(
            CityLayout layout,
            CitySurfaceDescriptor firstSurface,
            CitySurfaceDescriptor secondSurface,
            bool isHorizontal,
            float fixedCoordinate,
            float variableCoordinate)
        {
            Vector2 point = isHorizontal
                ? new Vector2(variableCoordinate, fixedCoordinate)
                : new Vector2(fixedCoordinate, variableCoordinate);
            return Mathf.Max(
                CityTerrainSurfacePlan.SampleTop(
                    layout,
                    firstSurface,
                    point),
                CityTerrainSurfacePlan.SampleTop(
                    layout,
                    secondSurface,
                    point));
        }

        private static float SampleLowerTop(
            CityLayout layout,
            CitySurfaceDescriptor firstSurface,
            CitySurfaceDescriptor secondSurface,
            bool isHorizontal,
            float fixedCoordinate,
            float variableCoordinate)
        {
            Vector2 point = isHorizontal
                ? new Vector2(variableCoordinate, fixedCoordinate)
                : new Vector2(fixedCoordinate, variableCoordinate);
            return Mathf.Min(
                CityTerrainSurfacePlan.SampleTop(
                    layout,
                    firstSurface,
                    point),
                CityTerrainSurfacePlan.SampleTop(
                    layout,
                    secondSurface,
                    point));
        }

    }
}
