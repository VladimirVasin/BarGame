using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    internal readonly struct CityRoadGroundBoundarySpan
    {
        internal CityRoadGroundBoundarySpan(
            CitySurfaceDescriptor surface,
            RoadEdge edge,
            bool isHorizontal,
            float fixedCoordinate,
            float minimumCoordinate,
            float maximumCoordinate,
            float groundTopY,
            float firstTravelTopY,
            float secondTravelTopY)
        {
            Surface = surface;
            Edge = edge;
            IsHorizontal = isHorizontal;
            FixedCoordinate = fixedCoordinate;
            MinimumCoordinate = minimumCoordinate;
            MaximumCoordinate = maximumCoordinate;
            GroundTopY = groundTopY;
            FirstTravelTopY = firstTravelTopY;
            SecondTravelTopY = secondTravelTopY;
        }

        internal CitySurfaceDescriptor Surface { get; }
        internal RoadEdge Edge { get; }
        internal bool IsHorizontal { get; }
        internal float FixedCoordinate { get; }
        internal float MinimumCoordinate { get; }
        internal float MaximumCoordinate { get; }
        internal float GroundTopY { get; }
        internal float FirstTravelTopY { get; }
        internal float SecondTravelTopY { get; }
        internal float Length => MaximumCoordinate - MinimumCoordinate;

        internal Rect CreateConnector(float reach)
        {
            return IsHorizontal
                ? Rect.MinMaxRect(
                    MinimumCoordinate,
                    FixedCoordinate - reach,
                    MaximumCoordinate,
                    FixedCoordinate + reach)
                : Rect.MinMaxRect(
                    FixedCoordinate - reach,
                    MinimumCoordinate,
                    FixedCoordinate + reach,
                    MaximumCoordinate);
        }
    }

    internal sealed class CityRoadGroundBoundaryPlan
    {
        internal CityRoadGroundBoundaryPlan(
            IList<CityRoadGroundBoundarySpan> safeConnections,
            IList<CityRoadGroundBoundarySpan> protectedDrops)
        {
            SafeConnections = new ReadOnlyCollection<
                CityRoadGroundBoundarySpan>(
                new List<CityRoadGroundBoundarySpan>(safeConnections));
            ProtectedDrops = new ReadOnlyCollection<
                CityRoadGroundBoundarySpan>(
                new List<CityRoadGroundBoundarySpan>(protectedDrops));
        }

        internal IReadOnlyList<CityRoadGroundBoundarySpan>
            SafeConnections { get; }
        internal IReadOnlyList<CityRoadGroundBoundarySpan>
            ProtectedDrops { get; }
    }

    internal static class CityRoadGroundBoundaryPlanner
    {
        internal const float MaximumSafeStep = 0.28f;

        private const float GeometryTolerance = 0.001f;
        private const float MinimumPassageWidth =
            (CityGroundTraversalPlanner.MaximumAgentRadius * 2f) + 0.1f;

        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.down,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left
        };

        internal static CityRoadGroundBoundaryPlan Create(
            CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var safeConnections = new List<
                CityRoadGroundBoundarySpan>();
            var protectedDrops = new List<
                CityRoadGroundBoundarySpan>();
            for (int surfaceIndex = 0;
                 surfaceIndex < layout.Surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface =
                    layout.Surfaces[surfaceIndex];
                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    RoadEdge edge = RoadEdge.ForCellFrontage(
                        surface.Cell,
                        direction);
                    if (!layout.HasRoad(edge))
                    {
                        continue;
                    }

                    CreateBoundarySpans(
                        layout,
                        surface,
                        direction,
                        edge,
                        safeConnections,
                        protectedDrops);
                }
            }

            IntegrateSignatureStairBoundaries(
                layout,
                safeConnections,
                protectedDrops);
            return new CityRoadGroundBoundaryPlan(
                safeConnections,
                protectedDrops);
        }

        internal static bool SupportsGroundTraversal(
            CitySurfaceDescriptor surface)
        {
            return surface.Kind == CitySurfaceKind.BuildableGround ||
                   surface.Kind == CitySurfaceKind.ParkGround ||
                   surface.IsWalkable;
        }

        private static void CreateBoundarySpans(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2Int direction,
            RoadEdge edge,
            ICollection<CityRoadGroundBoundarySpan> safeConnections,
            ICollection<CityRoadGroundBoundarySpan> protectedDrops)
        {
            bool horizontal = direction.y != 0;
            float fixedCoordinate = horizontal
                ? (direction.y < 0
                    ? surface.WorldBounds.yMin
                    : surface.WorldBounds.yMax)
                : (direction.x < 0
                    ? surface.WorldBounds.xMin
                    : surface.WorldBounds.xMax);
            float minimum = horizontal
                ? surface.WorldBounds.xMin
                : surface.WorldBounds.yMin;
            float maximum = horizontal
                ? surface.WorldBounds.xMax
                : surface.WorldBounds.yMax;
            float groundTop = surface.PhysicalTopY;
            if (!SupportsGroundTraversal(surface))
            {
                AddSpan(
                    layout,
                    surface,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    groundTop,
                    protectedDrops);
                return;
            }

            List<float> breakpoints = CreateBreakpoints(
                layout,
                edge,
                horizontal,
                minimum,
                maximum);
            var classified = new List<ClassifiedInterval>();
            for (int index = 1; index < breakpoints.Count; index++)
            {
                float first = breakpoints[index - 1];
                float second = breakpoints[index];
                ClassifyLinearPiece(
                    layout,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    first,
                    second,
                    groundTop,
                    classified);
            }

            MergeAdjacent(classified);
            for (int index = 0; index < classified.Count; index++)
            {
                ClassifiedInterval interval = classified[index];
                if (!interval.IsSafe)
                {
                    AddSpan(
                        layout,
                        surface,
                        edge,
                        horizontal,
                        fixedCoordinate,
                        interval.Minimum,
                        interval.Maximum,
                        groundTop,
                        protectedDrops);
                    continue;
                }

                AddAuthorizedSafeSpans(
                    layout,
                    surface,
                    direction,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    interval.Minimum,
                    interval.Maximum,
                    groundTop,
                    safeConnections);
            }
        }

        private static void AddAuthorizedSafeSpans(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2Int direction,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTop,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            if (!RequiresAuthoredAccess(layout, surface, edge))
            {
                AddSafeSpanIfWideEnough(
                    layout,
                    surface,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    groundTop,
                    destination);
                return;
            }

            if (surface.Kind == CitySurfaceKind.ParkGround)
            {
                AddParkGateSpans(
                    layout,
                    surface,
                    direction,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    groundTop,
                    destination);
                return;
            }

            Vector3 expectedOutward = new Vector3(
                -direction.x,
                0f,
                -direction.y);
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                if (access.Cell != surface.Cell ||
                    access.FrontageEdge != edge ||
                    access.StreetSideDirection != direction ||
                    (access.OutwardNormal - expectedOutward)
                        .sqrMagnitude > GeometryTolerance)
                {
                    continue;
                }

                AddClippedSafeSpan(
                    layout,
                    surface,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    groundTop,
                    horizontal ? access.Center.x : access.Center.z,
                    access.Width,
                    destination);
            }
        }

        private static bool RequiresAuthoredAccess(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            RoadEdge edge)
        {
            if (surface.Kind == CitySurfaceKind.ParkGround)
            {
                return layout.GetPathKind(edge) == CityPathKind.Street;
            }

            return surface.Kind == CitySurfaceKind.Beach ||
                   surface.Kind == CitySurfaceKind.LakeShore ||
                   surface.Kind == CitySurfaceKind.CemeteryGround ||
                   surface.Kind == CitySurfaceKind.OpenGround;
        }

        private static void AddParkGateSpans(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            Vector2Int direction,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTop,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            Vector3 expectedOutward = new Vector3(
                -direction.x,
                0f,
                -direction.y);
            for (int index = 0; index < layout.Park.Gates.Count; index++)
            {
                CityParkGateDescriptor gate = layout.Park.Gates[index];
                float gateFixed = horizontal
                    ? gate.Center.z
                    : gate.Center.x;
                if ((gate.OutwardNormal - expectedOutward)
                        .sqrMagnitude > GeometryTolerance ||
                    Mathf.Abs(gateFixed - fixedCoordinate) >
                    GeometryTolerance)
                {
                    continue;
                }

                AddClippedSafeSpan(
                    layout,
                    surface,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    minimum,
                    maximum,
                    groundTop,
                    horizontal ? gate.Center.x : gate.Center.z,
                    gate.Width,
                    destination);
            }
        }

        private static void AddClippedSafeSpan(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTop,
            float openingCenter,
            float openingWidth,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            float halfWidth = openingWidth * 0.5f;
            AddSafeSpanIfWideEnough(
                layout,
                surface,
                edge,
                horizontal,
                fixedCoordinate,
                Mathf.Max(minimum, openingCenter - halfWidth),
                Mathf.Min(maximum, openingCenter + halfWidth),
                groundTop,
                destination);
        }

        private static void AddSafeSpanIfWideEnough(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTop,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            if (maximum - minimum < MinimumPassageWidth)
            {
                return;
            }

            AddSpan(
                layout,
                surface,
                edge,
                horizontal,
                fixedCoordinate,
                minimum,
                maximum,
                groundTop,
                destination);
        }

        private static List<float> CreateBreakpoints(
            CityLayout layout,
            RoadEdge edge,
            bool horizontal,
            float minimum,
            float maximum)
        {
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            float startCoordinate = horizontal ? start.x : start.z;
            float endCoordinate = horizontal ? end.x : end.z;
            float direction = Mathf.Sign(endCoordinate - startCoordinate);
            float halfRoad = layout.RoadWidth * 0.5f;
            var result = new List<float>
            {
                minimum,
                maximum
            };
            AddBreakpoint(
                result,
                startCoordinate + direction * halfRoad,
                minimum,
                maximum);
            AddBreakpoint(
                result,
                endCoordinate - direction * halfRoad,
                minimum,
                maximum);
            result.Sort();
            return result;
        }

        private static void AddBreakpoint(
            ICollection<float> destination,
            float value,
            float minimum,
            float maximum)
        {
            if (value > minimum + GeometryTolerance &&
                value < maximum - GeometryTolerance)
            {
                destination.Add(value);
            }
        }

        private static void ClassifyLinearPiece(
            CityLayout layout,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTop,
            ICollection<ClassifiedInterval> destination)
        {
            float firstDifference = SampleTravelTop(
                layout,
                edge,
                horizontal,
                fixedCoordinate,
                minimum) - groundTop;
            float secondDifference = SampleTravelTop(
                layout,
                edge,
                horizontal,
                fixedCoordinate,
                maximum) - groundTop;
            float slope = secondDifference - firstDifference;
            if (Mathf.Abs(slope) <= GeometryTolerance)
            {
                destination.Add(new ClassifiedInterval(
                    minimum,
                    maximum,
                    Mathf.Abs(firstDifference) <= MaximumSafeStep));
                return;
            }

            float firstCrossing =
                (-MaximumSafeStep - firstDifference) / slope;
            float secondCrossing =
                (MaximumSafeStep - firstDifference) / slope;
            float safeStartAmount = Mathf.Clamp01(
                Mathf.Min(firstCrossing, secondCrossing));
            float safeEndAmount = Mathf.Clamp01(
                Mathf.Max(firstCrossing, secondCrossing));
            float safeMinimum = Mathf.Lerp(
                minimum,
                maximum,
                safeStartAmount);
            float safeMaximum = Mathf.Lerp(
                minimum,
                maximum,
                safeEndAmount);
            bool hasSafeInterval = safeMaximum - safeMinimum >
                                   GeometryTolerance &&
                                   Mathf.Abs(Mathf.Lerp(
                                       firstDifference,
                                       secondDifference,
                                       (safeStartAmount +
                                        safeEndAmount) * 0.5f)) <=
                                   MaximumSafeStep + GeometryTolerance;
            if (!hasSafeInterval)
            {
                destination.Add(new ClassifiedInterval(
                    minimum,
                    maximum,
                    false));
                return;
            }

            AddIntervalIfPositive(
                destination,
                minimum,
                safeMinimum,
                false);
            AddIntervalIfPositive(
                destination,
                safeMinimum,
                safeMaximum,
                true);
            AddIntervalIfPositive(
                destination,
                safeMaximum,
                maximum,
                false);
        }

        private static void AddIntervalIfPositive(
            ICollection<ClassifiedInterval> destination,
            float minimum,
            float maximum,
            bool isSafe)
        {
            if (maximum - minimum > GeometryTolerance)
            {
                destination.Add(new ClassifiedInterval(
                    minimum,
                    maximum,
                    isSafe));
            }
        }

        private static void MergeAdjacent(
            List<ClassifiedInterval> intervals)
        {
            if (intervals.Count < 2)
            {
                return;
            }

            var merged = new List<ClassifiedInterval>(intervals.Count);
            ClassifiedInterval current = intervals[0];
            for (int index = 1; index < intervals.Count; index++)
            {
                ClassifiedInterval next = intervals[index];
                if (current.IsSafe == next.IsSafe &&
                    Mathf.Abs(current.Maximum - next.Minimum) <=
                    GeometryTolerance)
                {
                    current = new ClassifiedInterval(
                        current.Minimum,
                        next.Maximum,
                        current.IsSafe);
                    continue;
                }

                merged.Add(current);
                current = next;
            }

            merged.Add(current);
            intervals.Clear();
            intervals.AddRange(merged);
        }

        private static void AddSpan(
            CityLayout layout,
            CitySurfaceDescriptor surface,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            float groundTop,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            if (maximum - minimum <= GeometryTolerance)
            {
                return;
            }

            destination.Add(new CityRoadGroundBoundarySpan(
                surface,
                edge,
                horizontal,
                fixedCoordinate,
                minimum,
                maximum,
                groundTop,
                SampleTravelTop(
                    layout,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    minimum),
                SampleTravelTop(
                    layout,
                    edge,
                    horizontal,
                    fixedCoordinate,
                    maximum)));
        }

        private static float SampleTravelTop(
            CityLayout layout,
            RoadEdge edge,
            bool horizontal,
            float fixedCoordinate,
            float variableCoordinate)
        {
            Vector2 point = horizontal
                ? new Vector2(variableCoordinate, fixedCoordinate)
                : new Vector2(fixedCoordinate, variableCoordinate);
            Vector3 start = layout.GetNodeWorldPosition(edge.A);
            Vector3 end = layout.GetNodeWorldPosition(edge.B);
            Vector2 startXZ = new Vector2(start.x, start.z);
            Vector2 endXZ = new Vector2(end.x, end.z);
            Vector2 delta = endXZ - startXZ;
            float amount = delta.sqrMagnitude > GeometryTolerance
                ? Mathf.Clamp01(
                    Vector2.Dot(point - startXZ, delta) /
                    delta.sqrMagnitude)
                : 0f;
            float topOffset = layout.GetPathKind(edge) ==
                              CityPathKind.Street
                ? CityStreetSurfacePlanner.SidewalkTop
                : CityStreetSurfacePlanner.RoadTop;
            return layout.ElevationPlan.SampleRoadDatum(edge, amount) +
                   topOffset;
        }

        private static void IntegrateSignatureStairBoundaries(
            CityLayout layout,
            List<CityRoadGroundBoundarySpan> safeConnections,
            List<CityRoadGroundBoundarySpan> protectedDrops)
        {
            for (int stairIndex = 0;
                 stairIndex < layout.ElevationPlan.SignatureStairs.Count;
                 stairIndex++)
            {
                CityElevationStairDescriptor stair =
                    layout.ElevationPlan.SignatureStairs[stairIndex];
                CityElevationStairPlacement placement =
                    CityElevationStairPlacementPlanner.Create(
                        layout,
                        stair);
                SubtractStairCorridor(
                    layout,
                    placement.GroundCutFootprint,
                    safeConnections,
                    true);
                SubtractStairCorridor(
                    layout,
                    placement.GroundCutFootprint,
                    protectedDrops,
                    false);

                if (!TryResolveStairOwnerBoundary(
                        layout,
                        stair,
                        placement,
                        out StairOwnerBoundary owner))
                {
                    throw new InvalidOperationException(
                        $"Signature stair '{stair.Id}' has no owning " +
                        "road-to-ground boundary.");
                }

                AddApproachGuardIfRequired(
                    owner,
                    placement,
                    placement.LowerApproachFootprint,
                    placement.LowerApproachStart.y,
                    protectedDrops);
                AddApproachGuardIfRequired(
                    owner,
                    placement,
                    placement.UpperApproachFootprint,
                    placement.UpperApproachStart.y,
                    protectedDrops);
            }
        }

        private static void SubtractStairCorridor(
            CityLayout layout,
            Rect corridor,
            List<CityRoadGroundBoundarySpan> spans,
            bool enforcePassageWidth)
        {
            var next = new List<CityRoadGroundBoundarySpan>(
                spans.Count + 1);
            for (int index = 0; index < spans.Count; index++)
            {
                CityRoadGroundBoundarySpan span = spans[index];
                if (!IntersectsBoundary(span, corridor))
                {
                    next.Add(span);
                    continue;
                }

                float cutMinimum = span.IsHorizontal
                    ? corridor.xMin
                    : corridor.yMin;
                float cutMaximum = span.IsHorizontal
                    ? corridor.xMax
                    : corridor.yMax;
                AddClippedBoundarySpan(
                    layout,
                    span,
                    span.MinimumCoordinate,
                    Mathf.Min(span.MaximumCoordinate, cutMinimum),
                    enforcePassageWidth,
                    next);
                AddClippedBoundarySpan(
                    layout,
                    span,
                    Mathf.Max(span.MinimumCoordinate, cutMaximum),
                    span.MaximumCoordinate,
                    enforcePassageWidth,
                    next);
            }

            spans.Clear();
            spans.AddRange(next);
        }

        private static void AddClippedBoundarySpan(
            CityLayout layout,
            CityRoadGroundBoundarySpan source,
            float minimum,
            float maximum,
            bool enforcePassageWidth,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            if (enforcePassageWidth &&
                maximum - minimum < MinimumPassageWidth)
            {
                return;
            }

            AddClippedSpan(
                layout,
                source,
                minimum,
                maximum,
                destination);
        }

        private static bool TryResolveStairOwnerBoundary(
            CityLayout layout,
            CityElevationStairDescriptor stair,
            CityElevationStairPlacement placement,
            out StairOwnerBoundary owner)
        {
            for (int surfaceIndex = 0;
                 surfaceIndex < layout.Surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface =
                    layout.Surfaces[surfaceIndex];
                for (int directionIndex = 0;
                     directionIndex < CardinalDirections.Length;
                     directionIndex++)
                {
                    Vector2Int direction =
                        CardinalDirections[directionIndex];
                    if (RoadEdge.ForCellFrontage(
                            surface.Cell,
                            direction) != stair.Edge)
                    {
                        continue;
                    }

                    Vector3 intoSurface = new Vector3(
                        -direction.x,
                        0f,
                        -direction.y);
                    if (Vector3.Dot(
                            intoSurface,
                            placement.SideDirection) < 0.99f)
                    {
                        continue;
                    }

                    bool horizontal = direction.y != 0;
                    float fixedCoordinate = horizontal
                        ? (direction.y < 0
                            ? surface.WorldBounds.yMin
                            : surface.WorldBounds.yMax)
                        : (direction.x < 0
                            ? surface.WorldBounds.xMin
                            : surface.WorldBounds.xMax);
                    float minimum = horizontal
                        ? surface.WorldBounds.xMin
                        : surface.WorldBounds.yMin;
                    float maximum = horizontal
                        ? surface.WorldBounds.xMax
                        : surface.WorldBounds.yMax;
                    owner = new StairOwnerBoundary(
                        surface,
                        stair.Edge,
                        horizontal,
                        fixedCoordinate,
                        minimum,
                        maximum);
                    return true;
                }
            }

            owner = default;
            return false;
        }

        private static void AddApproachGuardIfRequired(
            StairOwnerBoundary owner,
            CityElevationStairPlacement placement,
            Rect approach,
            float approachTopY,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            if (Mathf.Abs(
                    approachTopY - owner.Surface.PhysicalTopY) <=
                MaximumSafeStep + GeometryTolerance)
            {
                return;
            }

            float minimum = Mathf.Max(
                owner.MinimumCoordinate,
                owner.IsHorizontal ? approach.xMin : approach.yMin);
            float maximum = Mathf.Min(
                owner.MaximumCoordinate,
                owner.IsHorizontal ? approach.xMax : approach.yMax);
            if (maximum - minimum <= GeometryTolerance)
            {
                return;
            }

            float sideAmount = owner.IsHorizontal
                ? placement.SideDirection.z
                : placement.SideDirection.x;
            float fixedCoordinate;
            if (owner.IsHorizontal)
            {
                fixedCoordinate = sideAmount >= 0f
                    ? approach.yMax
                    : approach.yMin;
            }
            else
            {
                fixedCoordinate = sideAmount >= 0f
                    ? approach.xMax
                    : approach.xMin;
            }

            destination.Add(new CityRoadGroundBoundarySpan(
                owner.Surface,
                owner.Edge,
                owner.IsHorizontal,
                fixedCoordinate,
                minimum,
                maximum,
                owner.Surface.PhysicalTopY,
                approachTopY,
                approachTopY));
        }

        private static bool IntersectsBoundary(
            CityRoadGroundBoundarySpan span,
            Rect footprint)
        {
            bool intersectsFixed = span.IsHorizontal
                ? span.FixedCoordinate >=
                  footprint.yMin - GeometryTolerance &&
                  span.FixedCoordinate <=
                  footprint.yMax + GeometryTolerance
                : span.FixedCoordinate >=
                  footprint.xMin - GeometryTolerance &&
                  span.FixedCoordinate <=
                  footprint.xMax + GeometryTolerance;
            float minimum = span.IsHorizontal
                ? footprint.xMin
                : footprint.yMin;
            float maximum = span.IsHorizontal
                ? footprint.xMax
                : footprint.yMax;
            return intersectsFixed &&
                   maximum > span.MinimumCoordinate + GeometryTolerance &&
                   minimum < span.MaximumCoordinate - GeometryTolerance;
        }

        private static void AddClippedSpan(
            CityLayout layout,
            CityRoadGroundBoundarySpan source,
            float minimum,
            float maximum,
            ICollection<CityRoadGroundBoundarySpan> destination)
        {
            AddSpan(
                layout,
                source.Surface,
                source.Edge,
                source.IsHorizontal,
                source.FixedCoordinate,
                minimum,
                maximum,
                source.GroundTopY,
                destination);
        }

        private readonly struct StairOwnerBoundary
        {
            internal StairOwnerBoundary(
                CitySurfaceDescriptor surface,
                RoadEdge edge,
                bool isHorizontal,
                float fixedCoordinate,
                float minimumCoordinate,
                float maximumCoordinate)
            {
                Surface = surface;
                Edge = edge;
                IsHorizontal = isHorizontal;
                FixedCoordinate = fixedCoordinate;
                MinimumCoordinate = minimumCoordinate;
                MaximumCoordinate = maximumCoordinate;
            }

            internal CitySurfaceDescriptor Surface { get; }
            internal RoadEdge Edge { get; }
            internal bool IsHorizontal { get; }
            internal float FixedCoordinate { get; }
            internal float MinimumCoordinate { get; }
            internal float MaximumCoordinate { get; }
        }

        private readonly struct ClassifiedInterval
        {
            internal ClassifiedInterval(
                float minimum,
                float maximum,
                bool isSafe)
            {
                Minimum = minimum;
                Maximum = maximum;
                IsSafe = isSafe;
            }

            internal float Minimum { get; }
            internal float Maximum { get; }
            internal bool IsSafe { get; }
        }
    }
}
