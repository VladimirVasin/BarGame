using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class RoadFencePlanner
    {
        private const float CoordinateEpsilon = 0.0001f;

        public static RoadFencePlan CreatePlan(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            layout.ValidateOrThrow();
            IReadOnlyList<Rect> streetRectangles =
                CreateFenceSourceStreetRects(layout);
            var exposedSides = new List<BoundarySpan>(
                checked(streetRectangles.Count * 4));

            for (int roadIndex = 0;
                 roadIndex < streetRectangles.Count;
                 roadIndex++)
            {
                Rect road = streetRectangles[roadIndex];
                AddExposedSide(
                    exposedSides,
                    streetRectangles,
                    roadIndex,
                    true,
                    road.yMin,
                    road.xMin,
                    road.xMax,
                    Vector3.back);
                AddExposedSide(
                    exposedSides,
                    streetRectangles,
                    roadIndex,
                    true,
                    road.yMax,
                    road.xMin,
                    road.xMax,
                    Vector3.forward);
                AddExposedSide(
                    exposedSides,
                    streetRectangles,
                    roadIndex,
                    false,
                    road.xMin,
                    road.yMin,
                    road.yMax,
                    Vector3.left);
                AddExposedSide(
                    exposedSides,
                    streetRectangles,
                    roadIndex,
                    false,
                    road.xMax,
                    road.yMin,
                    road.yMax,
                    Vector3.right);
            }

            List<BoundarySpan> mapBoundary = KeepUnsupportedBoundary(
                Merge(exposedSides),
                layout.Surfaces,
                CreateSupportingTravelRects(layout));
            List<BoundarySpan> deadEnds = CreateDeadEndCaps(layout);
            mapBoundary = SubtractSpans(mapBoundary, deadEnds);
            mapBoundary = Merge(mapBoundary);
            deadEnds = Merge(deadEnds);
            mapBoundary.Sort(CompareSpans);
            deadEnds.Sort(CompareSpans);
            List<RoadFenceOpeningDescriptor> openings =
                CreateOpenings(layout);

            var segments =
                new List<RoadFenceSegmentDescriptor>(
                    mapBoundary.Count + deadEnds.Count);
            AddDescriptors(
                segments,
                mapBoundary,
                RoadFenceSegmentPurpose.MapBoundary,
                layout);
            AddDescriptors(
                segments,
                deadEnds,
                RoadFenceSegmentPurpose.DeadEnd,
                layout);
            segments.Sort(CompareSegments);

            return new RoadFencePlan(segments, openings);
        }

        private static IReadOnlyList<Rect> CreateFenceSourceStreetRects(
            CityLayout layout)
        {
            var streets = new List<Rect>(layout.RoadEdges.Count);
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) == CityPathKind.Street &&
                    !layout.IsRiverBridgeEdge(edge))
                {
                    streets.Add(layout.GetRoadRect(edge));
                }
            }

            return streets;
        }

        private static void AddDescriptors(
            ICollection<RoadFenceSegmentDescriptor> destination,
            IReadOnlyList<BoundarySpan> spans,
            RoadFenceSegmentPurpose purpose,
            CityLayout layout)
        {
            for (int index = 0; index < spans.Count; index++)
            {
                BoundarySpan span = spans[index];
                Vector3 start = span.Horizontal
                    ? new Vector3(span.Minimum, 0f, span.Fixed)
                    : new Vector3(span.Fixed, 0f, span.Minimum);
                Vector3 end = span.Horizontal
                    ? new Vector3(span.Maximum, 0f, span.Fixed)
                    : new Vector3(span.Fixed, 0f, span.Maximum);
                start.y = SampleRoadDatum(layout, start);
                end.y = SampleRoadDatum(layout, end);
                destination.Add(new RoadFenceSegmentDescriptor(
                    start,
                    end,
                    span.Outward,
                    purpose));
            }
        }

        private static List<BoundarySpan> KeepUnsupportedBoundary(
            IReadOnlyList<BoundarySpan> boundary,
            IReadOnlyList<CitySurfaceDescriptor> surfaces,
            IReadOnlyList<Rect> supportingTravel)
        {
            var remaining = new List<BoundarySpan>();
            for (int spanIndex = 0;
                 spanIndex < boundary.Count;
                 spanIndex++)
            {
                BoundarySpan span = boundary[spanIndex];
                var fragments = new List<AxisRange>(1)
                {
                    new AxisRange(span.Minimum, span.Maximum)
                };

                for (int surfaceIndex = 0;
                     surfaceIndex < surfaces.Count && fragments.Count > 0;
                     surfaceIndex++)
                {
                    CitySurfaceDescriptor surface = surfaces[surfaceIndex];
                    if (surface.IsWater ||
                        !ExtendsAcrossBoundary(
                            surface.WorldBounds,
                            span.Horizontal,
                            span.Fixed,
                            span.Outward))
                    {
                        continue;
                    }

                    SubtractRange(
                        fragments,
                        span.Horizontal
                            ? surface.WorldBounds.xMin
                            : surface.WorldBounds.yMin,
                        span.Horizontal
                            ? surface.WorldBounds.xMax
                            : surface.WorldBounds.yMax);
                }

                for (int pathIndex = 0;
                     pathIndex < supportingTravel.Count &&
                     fragments.Count > 0;
                     pathIndex++)
                {
                    Rect path = supportingTravel[pathIndex];
                    if (!ExtendsAcrossBoundary(
                            path,
                            span.Horizontal,
                            span.Fixed,
                            span.Outward))
                    {
                        continue;
                    }

                    SubtractRange(
                        fragments,
                        span.Horizontal ? path.xMin : path.yMin,
                        span.Horizontal ? path.xMax : path.yMax);
                }

                for (int fragmentIndex = 0;
                     fragmentIndex < fragments.Count;
                     fragmentIndex++)
                {
                    AxisRange fragment = fragments[fragmentIndex];
                    remaining.Add(new BoundarySpan(
                        span.Horizontal,
                        span.Fixed,
                        fragment.Minimum,
                        fragment.Maximum,
                        span.Outward));
                }
            }

            return remaining;
        }

        private static IReadOnlyList<Rect> CreateSupportingTravelRects(
            CityLayout layout)
        {
            var paths = new List<Rect>();
            for (int index = 0; index < layout.RoadEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadEdges[index];
                if (layout.GetPathKind(edge) == CityPathKind.ParkPath ||
                    layout.IsRiverBridgeEdge(edge))
                {
                    paths.Add(layout.GetRoadRect(edge));
                }
            }

            for (int index = 0;
                 index < layout.River.Promenades.Count;
                 index++)
            {
                paths.Add(layout.River.Promenades[index].Bounds);
            }

            return paths;
        }

        private static List<BoundarySpan> CreateDeadEndCaps(
            CityLayout layout)
        {
            var degreeByNode = new Dictionary<Vector2Int, int>();
            for (int edgeIndex = 0;
                 edgeIndex < layout.RoadEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = layout.RoadEdges[edgeIndex];
                IncrementDegree(degreeByNode, edge.A);
                IncrementDegree(degreeByNode, edge.B);
            }

            var result = new List<BoundarySpan>();
            float halfRoad = layout.RoadWidth * 0.5f;
            for (int edgeIndex = 0;
                 edgeIndex < layout.RoadEdges.Count;
                 edgeIndex++)
            {
                RoadEdge edge = layout.RoadEdges[edgeIndex];
                if (layout.GetPathKind(edge) != CityPathKind.Street)
                {
                    continue;
                }

                AddDeadEndIfTerminal(
                    result,
                    layout,
                    edge,
                    edge.A,
                    edge.B,
                    degreeByNode,
                    halfRoad);
                AddDeadEndIfTerminal(
                    result,
                    layout,
                    edge,
                    edge.B,
                    edge.A,
                    degreeByNode,
                    halfRoad);
            }

            return result;
        }

        private static void IncrementDegree(
            IDictionary<Vector2Int, int> degrees,
            Vector2Int node)
        {
            degrees.TryGetValue(node, out int degree);
            degrees[node] = degree + 1;
        }

        private static void AddDeadEndIfTerminal(
            ICollection<BoundarySpan> destination,
            CityLayout layout,
            RoadEdge edge,
            Vector2Int terminalNode,
            Vector2Int neighbourNode,
            IReadOnlyDictionary<Vector2Int, int> degreeByNode,
            float halfRoad)
        {
            if (!degreeByNode.TryGetValue(
                    terminalNode,
                    out int degree) ||
                degree != 1)
            {
                return;
            }

            Vector3 terminal = layout.GetNodeWorldPosition(terminalNode);
            Vector3 neighbour = layout.GetNodeWorldPosition(neighbourNode);
            Vector3 outward = terminal - neighbour;
            outward.y = 0f;
            outward.Normalize();
            Vector3 center = terminal + (outward * halfRoad);
            destination.Add(edge.IsHorizontal
                ? new BoundarySpan(
                    false,
                    center.x,
                    center.z - halfRoad,
                    center.z + halfRoad,
                    outward)
                : new BoundarySpan(
                    true,
                    center.z,
                    center.x - halfRoad,
                    center.x + halfRoad,
                    outward));
        }

        private static float SampleRoadDatum(
            CityLayout layout,
            Vector3 position)
        {
            return layout.ElevationPlan.TrySampleSurface(
                new Vector2(position.x, position.z),
                CitySurfaceRole.RoadDatum,
                out float height,
                out _)
                ? height
                : 0f;
        }

        private static List<BoundarySpan> SubtractSpans(
            IReadOnlyList<BoundarySpan> source,
            IReadOnlyList<BoundarySpan> blockers)
        {
            var remaining = new List<BoundarySpan>(source);
            for (int blockerIndex = 0;
                 blockerIndex < blockers.Count;
                 blockerIndex++)
            {
                BoundarySpan blocker = blockers[blockerIndex];
                var next = new List<BoundarySpan>(remaining.Count + 1);
                for (int sourceIndex = 0;
                     sourceIndex < remaining.Count;
                     sourceIndex++)
                {
                    BoundarySpan span = remaining[sourceIndex];
                    if (!BelongsToSameLine(span, blocker))
                    {
                        next.Add(span);
                        continue;
                    }

                    float overlapMinimum = Mathf.Max(
                        span.Minimum,
                        blocker.Minimum);
                    float overlapMaximum = Mathf.Min(
                        span.Maximum,
                        blocker.Maximum);
                    if (overlapMaximum - overlapMinimum <=
                        CoordinateEpsilon)
                    {
                        next.Add(span);
                        continue;
                    }

                    AddSpanIfPositive(
                        next,
                        span,
                        span.Minimum,
                        overlapMinimum);
                    AddSpanIfPositive(
                        next,
                        span,
                        overlapMaximum,
                        span.Maximum);
                }

                remaining = next;
            }

            return remaining;
        }

        private static void AddExposedSide(
            ICollection<BoundarySpan> destination,
            IReadOnlyList<Rect> roads,
            int sourceIndex,
            bool horizontal,
            float fixedCoordinate,
            float minimum,
            float maximum,
            Vector3 outward)
        {
            var fragments = new List<AxisRange>(1)
            {
                new AxisRange(minimum, maximum)
            };

            for (int otherIndex = 0;
                 otherIndex < roads.Count && fragments.Count > 0;
                 otherIndex++)
            {
                if (otherIndex == sourceIndex)
                {
                    continue;
                }

                Rect other = roads[otherIndex];
                if (!ExtendsAcrossBoundary(
                        other,
                        horizontal,
                        fixedCoordinate,
                        outward))
                {
                    continue;
                }

                float blockerMinimum =
                    horizontal ? other.xMin : other.yMin;
                float blockerMaximum =
                    horizontal ? other.xMax : other.yMax;
                SubtractRange(
                    fragments,
                    blockerMinimum,
                    blockerMaximum);
            }

            for (int index = 0; index < fragments.Count; index++)
            {
                AxisRange fragment = fragments[index];
                destination.Add(new BoundarySpan(
                    horizontal,
                    fixedCoordinate,
                    fragment.Minimum,
                    fragment.Maximum,
                    outward));
            }
        }

        private static bool ExtendsAcrossBoundary(
            Rect rectangle,
            bool horizontal,
            float fixedCoordinate,
            Vector3 outward)
        {
            if (horizontal)
            {
                return outward.z > 0f
                    ? rectangle.yMin <= fixedCoordinate &&
                      rectangle.yMax > fixedCoordinate
                    : rectangle.yMin < fixedCoordinate &&
                      rectangle.yMax >= fixedCoordinate;
            }

            return outward.x > 0f
                ? rectangle.xMin <= fixedCoordinate &&
                  rectangle.xMax > fixedCoordinate
                : rectangle.xMin < fixedCoordinate &&
                  rectangle.xMax >= fixedCoordinate;
        }

        private static void SubtractRange(
            IList<AxisRange> fragments,
            float blockerMinimum,
            float blockerMaximum)
        {
            for (int index = fragments.Count - 1; index >= 0; index--)
            {
                AxisRange fragment = fragments[index];
                float overlapMinimum =
                    Mathf.Max(fragment.Minimum, blockerMinimum);
                float overlapMaximum =
                    Mathf.Min(fragment.Maximum, blockerMaximum);
                if (overlapMaximum - overlapMinimum <=
                    CoordinateEpsilon)
                {
                    continue;
                }

                fragments.RemoveAt(index);
                AddRangeIfPositive(
                    fragments,
                    fragment.Minimum,
                    overlapMinimum);
                AddRangeIfPositive(
                    fragments,
                    overlapMaximum,
                    fragment.Maximum);
            }
        }

        private static void AddRangeIfPositive(
            ICollection<AxisRange> destination,
            float minimum,
            float maximum)
        {
            if (maximum - minimum > CoordinateEpsilon)
            {
                destination.Add(new AxisRange(minimum, maximum));
            }
        }

        private static List<BoundarySpan> Merge(
            IList<BoundarySpan> spans)
        {
            var sorted = new List<BoundarySpan>(spans);
            sorted.Sort(CompareSpans);
            var merged = new List<BoundarySpan>(sorted.Count);
            if (sorted.Count == 0)
            {
                return merged;
            }

            BoundarySpan current = sorted[0];
            for (int index = 1; index < sorted.Count; index++)
            {
                BoundarySpan next = sorted[index];
                if (BelongsToSameLine(current, next) &&
                    next.Minimum <=
                    current.Maximum + CoordinateEpsilon)
                {
                    current = new BoundarySpan(
                        current.Horizontal,
                        current.Fixed,
                        current.Minimum,
                        Mathf.Max(current.Maximum, next.Maximum),
                        current.Outward);
                    continue;
                }

                merged.Add(current);
                current = next;
            }

            merged.Add(current);
            return merged;
        }

        private static List<RoadFenceOpeningDescriptor>
            CreateOpenings(CityLayout layout)
        {
            var openings =
                new List<RoadFenceOpeningDescriptor>();
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.IsBar)
                {
                    continue;
                }

                Vector3 frontage = new Vector3(
                    lot.FrontageDirection.x,
                    0f,
                    lot.FrontageDirection.y);
                Vector3 outward = -frontage;
                Vector3 center =
                    lot.ReturnPosition +
                    (outward * (layout.RoadWidth * 0.5f));
                openings.Add(new RoadFenceOpeningDescriptor(
                    lot.BarId,
                    center,
                    outward,
                    BarEntranceGeometry.FenceOpeningWidth));
            }

            BuildingLot playerHome = layout.PlayerHome;
            if (playerHome != null)
            {
                Vector3 frontage = new Vector3(
                    playerHome.FrontageDirection.x,
                    0f,
                    playerHome.FrontageDirection.y);
                Vector3 outward = -frontage;
                Vector3 center =
                    playerHome.ReturnPosition +
                    (outward * (layout.RoadWidth * 0.5f));
                openings.Add(new RoadFenceOpeningDescriptor(
                    RoadFenceOpeningKind.PlayerHomeEntrance,
                    "player-home",
                    center,
                    outward,
                    PlayerHomeEntranceGeometry.FenceOpeningWidth));
            }

            BuildingLot supermarket = layout.Supermarket;
            if (supermarket != null)
            {
                Vector3 frontage = new Vector3(
                    supermarket.FrontageDirection.x,
                    0f,
                    supermarket.FrontageDirection.y);
                Vector3 outward = -frontage;
                Vector3 center =
                    supermarket.ReturnPosition +
                    (outward * (layout.RoadWidth * 0.5f));
                openings.Add(new RoadFenceOpeningDescriptor(
                    RoadFenceOpeningKind.SupermarketEntrance,
                    "supermarket",
                    center,
                    outward,
                    SupermarketEntranceGeometry.FenceOpeningWidth));
            }

            for (int index = 0;
                 index < layout.Park.Gates.Count;
                 index++)
            {
                CityParkGateDescriptor gate =
                    layout.Park.Gates[index];
                openings.Add(new RoadFenceOpeningDescriptor(
                    RoadFenceOpeningKind.ParkGate,
                    gate.Id,
                    gate.Center,
                    gate.OutwardNormal,
                    gate.Width));
            }

            for (int pointIndex = 0;
                 pointIndex < layout.DistrictPointsOfInterest.Count;
                 pointIndex++)
            {
                CityDistrictPointOfInterestDescriptor point =
                    layout.DistrictPointsOfInterest[pointIndex];
                for (int accessIndex = 0;
                     accessIndex < point.Accesses.Count;
                     accessIndex++)
                {
                    CityDistrictPointOfInterestAccessDescriptor access =
                        point.Accesses[accessIndex];
                    openings.Add(new RoadFenceOpeningDescriptor(
                        RoadFenceOpeningKind.DistrictPointOfInterest,
                        access.Id,
                        access.Center,
                        access.OutwardNormal,
                        access.Width));
                }
            }

            for (int accessIndex = 0;
                 accessIndex < layout.OpenAreaAccesses.Count;
                 accessIndex++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[accessIndex];
                openings.Add(new RoadFenceOpeningDescriptor(
                    RoadFenceOpeningKind.OpenAreaAccess,
                    access.Id,
                    access.Center,
                    access.OutwardNormal,
                    access.Width));
            }

            openings.Sort(CompareOpenings);
            return openings;
        }

        private static List<BoundarySpan> SubtractOpenings(
            IList<BoundarySpan> boundary,
            IReadOnlyList<RoadFenceOpeningDescriptor> openings)
        {
            var remaining = new List<BoundarySpan>(boundary);
            for (int openingIndex = 0;
                 openingIndex < openings.Count;
                 openingIndex++)
            {
                RoadFenceOpeningDescriptor opening =
                    openings[openingIndex];
                var next = new List<BoundarySpan>(
                    remaining.Count + 1);

                for (int spanIndex = 0;
                     spanIndex < remaining.Count;
                     spanIndex++)
                {
                    BoundarySpan span = remaining[spanIndex];
                    if (!Matches(span, opening))
                    {
                        next.Add(span);
                        continue;
                    }

                    float overlapMinimum = Mathf.Max(
                        span.Minimum,
                        opening.MinimumCoordinate);
                    float overlapMaximum = Mathf.Min(
                        span.Maximum,
                        opening.MaximumCoordinate);
                    if (overlapMaximum - overlapMinimum <=
                        CoordinateEpsilon)
                    {
                        next.Add(span);
                        continue;
                    }

                    AddSpanIfPositive(
                        next,
                        span,
                        span.Minimum,
                        overlapMinimum);
                    AddSpanIfPositive(
                        next,
                        span,
                        overlapMaximum,
                        span.Maximum);
                }

                remaining = next;
            }

            return remaining;
        }

        private static void AddSpanIfPositive(
            ICollection<BoundarySpan> destination,
            BoundarySpan source,
            float minimum,
            float maximum)
        {
            if (maximum - minimum <= CoordinateEpsilon)
            {
                return;
            }

            destination.Add(new BoundarySpan(
                source.Horizontal,
                source.Fixed,
                minimum,
                maximum,
                source.Outward));
        }

        private static bool Matches(
            BoundarySpan span,
            RoadFenceOpeningDescriptor opening)
        {
            return span.Horizontal == opening.IsHorizontal &&
                   Mathf.Abs(
                       span.Fixed -
                       opening.FixedCoordinate) <= CoordinateEpsilon &&
                   (span.Outward - opening.OutwardNormal).sqrMagnitude <=
                   CoordinateEpsilon * CoordinateEpsilon;
        }

        private static bool BelongsToSameLine(
            BoundarySpan left,
            BoundarySpan right)
        {
            return left.Horizontal == right.Horizontal &&
                   Mathf.Abs(left.Fixed - right.Fixed) <=
                   CoordinateEpsilon &&
                   (left.Outward - right.Outward).sqrMagnitude <=
                   CoordinateEpsilon * CoordinateEpsilon;
        }

        private static int CompareSpans(
            BoundarySpan left,
            BoundarySpan right)
        {
            if (left.Horizontal != right.Horizontal)
            {
                return left.Horizontal ? -1 : 1;
            }

            int fixedComparison = left.Fixed.CompareTo(right.Fixed);
            if (fixedComparison != 0)
            {
                return fixedComparison;
            }

            int normalXComparison =
                left.Outward.x.CompareTo(right.Outward.x);
            if (normalXComparison != 0)
            {
                return normalXComparison;
            }

            int normalZComparison =
                left.Outward.z.CompareTo(right.Outward.z);
            if (normalZComparison != 0)
            {
                return normalZComparison;
            }

            int minimumComparison =
                left.Minimum.CompareTo(right.Minimum);
            return minimumComparison != 0
                ? minimumComparison
                : left.Maximum.CompareTo(right.Maximum);
        }

        private static int CompareSegments(
            RoadFenceSegmentDescriptor left,
            RoadFenceSegmentDescriptor right)
        {
            int purposeComparison = left.Purpose.CompareTo(right.Purpose);
            if (purposeComparison != 0)
            {
                return purposeComparison;
            }

            if (left.IsHorizontal != right.IsHorizontal)
            {
                return left.IsHorizontal ? -1 : 1;
            }

            int fixedComparison =
                left.FixedCoordinate.CompareTo(right.FixedCoordinate);
            if (fixedComparison != 0)
            {
                return fixedComparison;
            }

            int normalXComparison =
                left.OutwardNormal.x.CompareTo(right.OutwardNormal.x);
            if (normalXComparison != 0)
            {
                return normalXComparison;
            }

            int normalZComparison =
                left.OutwardNormal.z.CompareTo(right.OutwardNormal.z);
            if (normalZComparison != 0)
            {
                return normalZComparison;
            }

            int minimumComparison =
                left.MinimumCoordinate.CompareTo(right.MinimumCoordinate);
            return minimumComparison != 0
                ? minimumComparison
                : left.MaximumCoordinate.CompareTo(right.MaximumCoordinate);
        }

        private static int CompareOpenings(
            RoadFenceOpeningDescriptor left,
            RoadFenceOpeningDescriptor right)
        {
            if (left.IsHorizontal != right.IsHorizontal)
            {
                return left.IsHorizontal ? -1 : 1;
            }

            int fixedComparison =
                left.FixedCoordinate.CompareTo(
                    right.FixedCoordinate);
            if (fixedComparison != 0)
            {
                return fixedComparison;
            }

            int minimumComparison =
                left.MinimumCoordinate.CompareTo(
                    right.MinimumCoordinate);
            if (minimumComparison != 0)
            {
                return minimumComparison;
            }

            int kindComparison =
                left.Kind.CompareTo(right.Kind);
            if (kindComparison != 0)
            {
                return kindComparison;
            }

            return string.Compare(
                left.Id,
                right.Id,
                StringComparison.Ordinal);
        }

        private readonly struct AxisRange
        {
            public AxisRange(float minimum, float maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public float Minimum { get; }
            public float Maximum { get; }
        }

        private readonly struct BoundarySpan
        {
            public BoundarySpan(
                bool horizontal,
                float fixedCoordinate,
                float minimum,
                float maximum,
                Vector3 outward)
            {
                Horizontal = horizontal;
                Fixed = fixedCoordinate;
                Minimum = minimum;
                Maximum = maximum;
                Outward = outward;
            }

            public bool Horizontal { get; }
            public float Fixed { get; }
            public float Minimum { get; }
            public float Maximum { get; }
            public Vector3 Outward { get; }
        }
    }
}
