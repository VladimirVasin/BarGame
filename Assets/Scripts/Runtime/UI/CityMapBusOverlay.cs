using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityMapBusStopMarker
    {
        internal CityMapBusStopMarker(
            string stableId,
            int ordinal,
            Vector3 worldPosition,
            string labelLocalizationKey)
        {
            StableId = stableId ?? string.Empty;
            Ordinal = ordinal;
            WorldPosition = worldPosition;
            LabelLocalizationKey = labelLocalizationKey ?? string.Empty;
        }

        public string StableId { get; }
        public int Ordinal { get; }
        public Vector3 WorldPosition { get; }
        public string LabelLocalizationKey { get; }
    }

    public sealed class CityMapBusOverlay
    {
        private static readonly IReadOnlyList<Vector3> NoRoutePoints =
            new ReadOnlyCollection<Vector3>(new List<Vector3>());
        private static readonly IReadOnlyList<CityMapBusStopMarker> NoStops =
            new ReadOnlyCollection<CityMapBusStopMarker>(
                new List<CityMapBusStopMarker>());

        internal CityMapBusOverlay(
            string routeId,
            IList<Vector3> routePoints,
            IList<CityMapBusStopMarker> stops)
        {
            RouteId = routeId ?? string.Empty;
            RoutePoints = new ReadOnlyCollection<Vector3>(
                new List<Vector3>(routePoints ??
                    throw new ArgumentNullException(nameof(routePoints))));
            Stops = new ReadOnlyCollection<CityMapBusStopMarker>(
                new List<CityMapBusStopMarker>(stops ??
                    throw new ArgumentNullException(nameof(stops))));
        }

        private CityMapBusOverlay()
        {
            RouteId = string.Empty;
            RoutePoints = NoRoutePoints;
            Stops = NoStops;
        }

        public static CityMapBusOverlay Empty { get; } =
            new CityMapBusOverlay();

        public string RouteId { get; }
        public IReadOnlyList<Vector3> RoutePoints { get; }
        public IReadOnlyList<CityMapBusStopMarker> Stops { get; }
        public bool IsEmpty => RoutePoints.Count < 4;
    }

    public static class CityMapBusOverlayBuilder
    {
        public const float RouteSimplificationTolerance = 0.2f;

        private const float DuplicatePointTolerance = 0.0001f;
        private const float RouteSeamTolerance = 0.25f;

        public static CityMapBusOverlay Create(CityBusPlan plan)
        {
            if (plan == null ||
                plan.Links.Count == 0 ||
                plan.OrderedLinkIndices.Count == 0)
            {
                return CityMapBusOverlay.Empty;
            }

            List<Vector3> rawRoute = BuildOrderedClosedRoute(plan);
            if (rawRoute.Count < 4)
            {
                return CityMapBusOverlay.Empty;
            }

            IReadOnlyList<Vector3> simplified = SimplifyClosed(
                rawRoute,
                RouteSimplificationTolerance);
            if (simplified.Count < 4)
            {
                return CityMapBusOverlay.Empty;
            }

            var routePoints = new List<Vector3>(simplified.Count);
            for (int index = 0; index < simplified.Count; index++)
            {
                routePoints.Add(simplified[index]);
            }

            List<CityMapBusStopMarker> stops = CreateStopMarkers(plan);
            return new CityMapBusOverlay(
                plan.RouteId,
                routePoints,
                stops);
        }

        internal static IReadOnlyList<Vector3> SimplifyClosed(
            IReadOnlyList<Vector3> closedPoints,
            float tolerance)
        {
            if (closedPoints == null || closedPoints.Count == 0)
            {
                return new ReadOnlyCollection<Vector3>(
                    new List<Vector3>());
            }

            float toleranceSquared = Mathf.Max(0f, tolerance);
            toleranceSquared *= toleranceSquared;
            var ring = new List<Vector3>(closedPoints.Count);
            for (int index = 0; index < closedPoints.Count; index++)
            {
                Vector3 point = closedPoints[index];
                if (!IsFinite(point))
                {
                    return new ReadOnlyCollection<Vector3>(
                        new List<Vector3>());
                }

                if (ring.Count == 0 ||
                    PlanarDistanceSquared(
                        ring[ring.Count - 1],
                        point) > DuplicatePointTolerance *
                                   DuplicatePointTolerance)
                {
                    ring.Add(point);
                }
            }

            if (ring.Count > 1 &&
                PlanarDistanceSquared(
                    ring[0],
                    ring[ring.Count - 1]) <=
                DuplicatePointTolerance * DuplicatePointTolerance)
            {
                ring.RemoveAt(ring.Count - 1);
            }

            if (ring.Count < 3)
            {
                var minimal = new List<Vector3>(ring);
                if (minimal.Count > 0)
                {
                    minimal.Add(minimal[0]);
                }

                return new ReadOnlyCollection<Vector3>(minimal);
            }

            int splitIndex = FindFarthestPointIndex(ring, ring[0]);
            var firstArc = new List<Vector3>(splitIndex + 1);
            for (int index = 0; index <= splitIndex; index++)
            {
                firstArc.Add(ring[index]);
            }

            var secondArc = new List<Vector3>(
                ring.Count - splitIndex + 1);
            for (int index = splitIndex; index < ring.Count; index++)
            {
                secondArc.Add(ring[index]);
            }

            secondArc.Add(ring[0]);
            List<Vector3> simplifiedFirst = SimplifyOpen(
                firstArc,
                toleranceSquared);
            List<Vector3> simplifiedSecond = SimplifyOpen(
                secondArc,
                toleranceSquared);
            var result = new List<Vector3>(
                simplifiedFirst.Count + simplifiedSecond.Count - 1);
            result.AddRange(simplifiedFirst);
            for (int index = 1; index < simplifiedSecond.Count; index++)
            {
                result.Add(simplifiedSecond[index]);
            }

            result[result.Count - 1] = result[0];
            return new ReadOnlyCollection<Vector3>(result);
        }

        private static List<Vector3> BuildOrderedClosedRoute(
            CityBusPlan plan)
        {
            var points = new List<Vector3>();
            for (int orderIndex = 0;
                 orderIndex < plan.OrderedLinkIndices.Count;
                 orderIndex++)
            {
                int linkIndex = plan.OrderedLinkIndices[orderIndex];
                if (linkIndex < 0 || linkIndex >= plan.Links.Count)
                {
                    return new List<Vector3>();
                }

                IReadOnlyList<CityBusPathSample> samples =
                    plan.Links[linkIndex].Samples;
                for (int sampleIndex = 0;
                     sampleIndex < samples.Count;
                     sampleIndex++)
                {
                    Vector3 position = samples[sampleIndex].Position;
                    if (!IsFinite(position))
                    {
                        return new List<Vector3>();
                    }

                    if (points.Count == 0 ||
                        PlanarDistanceSquared(
                            points[points.Count - 1],
                            position) > DuplicatePointTolerance *
                                        DuplicatePointTolerance)
                    {
                        points.Add(position);
                    }
                }
            }

            if (points.Count < 3 ||
                PlanarDistanceSquared(points[0], points[points.Count - 1]) >
                RouteSeamTolerance * RouteSeamTolerance)
            {
                return new List<Vector3>();
            }

            points[points.Count - 1] = points[0];
            return points;
        }

        private static List<CityMapBusStopMarker> CreateStopMarkers(
            CityBusPlan plan)
        {
            var orderedStops = new List<CityBusStopDescriptor>(plan.Stops);
            orderedStops.Sort(CompareStops);
            var markers = new List<CityMapBusStopMarker>(
                orderedStops.Count);
            for (int index = 0; index < orderedStops.Count; index++)
            {
                CityBusStopDescriptor stop = orderedStops[index];
                if (!IsFinite(stop.Position))
                {
                    continue;
                }

                markers.Add(new CityMapBusStopMarker(
                    stop.Id,
                    stop.SequenceIndex >= 0
                        ? stop.SequenceIndex + 1
                        : index + 1,
                    stop.Position,
                    stop.NameLocalizationKey));
            }

            return markers;
        }

        private static int CompareStops(
            CityBusStopDescriptor left,
            CityBusStopDescriptor right)
        {
            int sequenceComparison =
                left.SequenceIndex.CompareTo(right.SequenceIndex);
            return sequenceComparison != 0
                ? sequenceComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }

        private static int FindFarthestPointIndex(
            IReadOnlyList<Vector3> points,
            Vector3 origin)
        {
            int farthestIndex = 1;
            float farthestDistance = -1f;
            for (int index = 1; index < points.Count; index++)
            {
                float distance = PlanarDistanceSquared(
                    points[index],
                    origin);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestIndex = index;
                }
            }

            return farthestIndex;
        }

        private static List<Vector3> SimplifyOpen(
            IReadOnlyList<Vector3> points,
            float toleranceSquared)
        {
            if (points.Count <= 2)
            {
                return new List<Vector3>(points);
            }

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;
            var ranges = new Stack<Vector2Int>();
            ranges.Push(new Vector2Int(0, points.Count - 1));
            while (ranges.Count > 0)
            {
                Vector2Int range = ranges.Pop();
                int farthestIndex = -1;
                float farthestDistance = toleranceSquared;
                for (int index = range.x + 1;
                     index < range.y;
                     index++)
                {
                    float distance = DistanceToSegmentSquared(
                        points[index],
                        points[range.x],
                        points[range.y]);
                    if (distance > farthestDistance)
                    {
                        farthestDistance = distance;
                        farthestIndex = index;
                    }
                }

                if (farthestIndex < 0)
                {
                    continue;
                }

                keep[farthestIndex] = true;
                ranges.Push(new Vector2Int(range.x, farthestIndex));
                ranges.Push(new Vector2Int(farthestIndex, range.y));
            }

            var result = new List<Vector3>();
            for (int index = 0; index < points.Count; index++)
            {
                if (keep[index])
                {
                    result.Add(points[index]);
                }
            }

            return result;
        }

        private static float DistanceToSegmentSquared(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector2 point2D = new Vector2(point.x, point.z);
            Vector2 start2D = new Vector2(start.x, start.z);
            Vector2 delta = new Vector2(
                end.x - start.x,
                end.z - start.z);
            float lengthSquared = delta.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
            {
                return (point2D - start2D).sqrMagnitude;
            }

            float progress = Mathf.Clamp01(
                Vector2.Dot(point2D - start2D, delta) /
                lengthSquared);
            return (point2D - (start2D + delta * progress)).sqrMagnitude;
        }

        private static float PlanarDistanceSquared(
            Vector3 left,
            Vector3 right)
        {
            float x = left.x - right.x;
            float z = left.z - right.z;
            return x * x + z * z;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
