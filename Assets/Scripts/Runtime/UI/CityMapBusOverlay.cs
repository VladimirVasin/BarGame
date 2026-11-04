using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct CityMapBusStopMarker
    {
        internal CityMapBusStopMarker(CityBusPlan plan, CityBusStopDescriptor stop, int ordinal,
            int transferOrdinal, bool shared, CityLayout layout)
        {
            StableId = stop.Id;
            Ordinal = ordinal;
            RouteNumber = plan.Number;
            TransferNumber = transferOrdinal > 0 ? stop.TransferNumber : 0;
            TransferId = TransferNumber > 0 ? stop.TargetId : string.Empty;
            WorldPosition = stop.ShelterPosition;
            StandingPosition = CityBusStopWaitPlanner.StandingPosition(plan, stop, layout, .8f);
            SharedPlatform = shared;
            LabelLocalizationKey = stop.NameLocalizationKey;
            Label = new CityBusStopLabel(RouteNumber, ordinal, TransferNumber, transferOrdinal, shared);
            string text = Label.MarkerText;
            int lineBreak = text.IndexOf('\n');
            int width = lineBreak < 0 ? text.Length : Math.Max(lineBreak, text.Length - lineBreak - 1);
            MarkerSize = new Vector2(6f + 5.25f * width, shared ? 24f : 14f);
        }

        public string StableId { get; }
        public int Ordinal { get; }
        public int RouteNumber { get; }
        public int TransferNumber { get; }
        public string TransferId { get; }
        public Vector3 WorldPosition { get; }
        public Vector3 StandingPosition { get; }
        public bool SharedPlatform { get; }
        public string LabelLocalizationKey { get; }
        internal CityBusStopLabel Label { get; }
        internal Vector2 MarkerSize { get; }
    }

    public sealed class CityMapBusLine
    {
        internal CityMapBusLine(CityBusPlan plan, IReadOnlyList<Vector3> points)
        {
            RouteId = plan.RouteId;
            Number = plan.Number;
            Points = points;
        }

        public string RouteId { get; }
        public int Number { get; }
        public IReadOnlyList<Vector3> Points { get; }
    }

    public sealed class CityMapBusOverlay
    {
        internal CityMapBusOverlay(IReadOnlyList<CityMapBusLine> lines,
            IReadOnlyList<CityMapBusStopMarker> stops, IReadOnlyList<Vector2Int> transferPairs,
            IReadOnlyList<IReadOnlyList<Vector3>> transferWalks)
        {
            Lines = lines;
            Stops = stops;
            TransferPairs = transferPairs;
            TransferWalks = transferWalks;
        }

        public static CityMapBusOverlay Empty { get; } = new CityMapBusOverlay(
            Array.Empty<CityMapBusLine>(), Array.Empty<CityMapBusStopMarker>(), Array.Empty<Vector2Int>(), Array.Empty<IReadOnlyList<Vector3>>());
        public IReadOnlyList<CityMapBusLine> Lines { get; }
        public IReadOnlyList<CityMapBusStopMarker> Stops { get; }
        public IReadOnlyList<Vector2Int> TransferPairs { get; }
        public IReadOnlyList<IReadOnlyList<Vector3>> TransferWalks { get; }
        public bool IsEmpty => Lines.Count == 0;
    }

    public static class CityMapBusOverlayBuilder
    {
        public const float RouteSimplificationTolerance = 0.2f;

        private const float DuplicatePointTolerance = 0.0001f;
        private const float RouteSeamTolerance = 0.25f;

        public static CityMapBusOverlay Create(CityBusPlan plan) => plan == null
            ? CityMapBusOverlay.Empty : CreateNetwork(new[] { plan });

        public static CityMapBusOverlay CreateNetwork(IReadOnlyList<CityBusPlan> plans, CityLayout layout = null,
            CityPedestrianPlan footways = null)
        {
            if (plans == null || plans.Count == 0) return CityMapBusOverlay.Empty;
            var lines = new List<CityMapBusLine>(plans.Count);
            var validPlans = new List<CityBusPlan>(plans.Count);
            foreach (CityBusPlan plan in plans)
            {
                if (plan == null || plan.IsEmpty) continue;
                List<Vector3> rawRoute = BuildOrderedClosedRoute(plan);
                IReadOnlyList<Vector3> points = SimplifyClosed(rawRoute, RouteSimplificationTolerance);
                if (points.Count < 4) continue;
                lines.Add(new CityMapBusLine(plan, points));
                validPlans.Add(plan);
            }
            if (lines.Count == 0) return CityMapBusOverlay.Empty;

            var stops = new List<CityMapBusStopMarker>();
            var transfers = new Dictionary<string, int>(StringComparer.Ordinal);
            var pairs = new List<Vector2Int>();
            foreach (var platform in CityBusNetworkFurniture.Platforms(validPlans))
            {
                if (!IsFinite(platform.stop.ShelterPosition)) continue;
                var marker = new CityMapBusStopMarker(platform.line, platform.stop, platform.ordinal,
                    platform.transferOrdinal, platform.shared, layout);
                if (marker.TransferNumber > 0 && !marker.SharedPlatform)
                {
                    if (transfers.TryGetValue(marker.TransferId, out int partner))
                        pairs.Add(new Vector2Int(partner, stops.Count));
                    else transfers.Add(marker.TransferId, stops.Count);
                }
                stops.Add(marker);
            }
            var walks = new List<IReadOnlyList<Vector3>>(pairs.Count);
            if (pairs.Count > 0 && footways == null && layout != null)
                footways = CityPedestrianPlanner.Create(layout, layout.Seed);
            foreach (var pair in pairs) walks.Add(TransferWalk(footways, stops[pair.x], stops[pair.y]));
            return new CityMapBusOverlay(lines.AsReadOnly(), stops.AsReadOnly(), pairs.AsReadOnly(), walks.AsReadOnly());
        }

        private static IReadOnlyList<Vector3> TransferWalk(CityPedestrianPlan footways, CityMapBusStopMarker from,
            CityMapBusStopMarker to)
        {
            if (footways == null) return new[] { from.WorldPosition, to.WorldPosition };
            if (!CityBusStopWaitPlanner.TryFindNearestNode(footways, from.StandingPosition, out int a) ||
                !CityBusStopWaitPlanner.TryFindNearestNode(footways, to.StandingPosition, out int b)) return Array.Empty<Vector3>();
            var route = CityResidentWorld.BuildRoute(footways, a, b, CityBusStopWaitPlanner.CreateNodeDistances(footways, b));
            if (route.Nodes[route.Nodes.Length - 1] != b) return Array.Empty<Vector3>();
            var points = new List<Vector3> { from.WorldPosition, footways.Nodes[a].Position };
            for (int i = 0; i < route.Links.Length; i++)
            {
                var link = footways.Links[route.Links[i]];
                var vertices = link.Path?.Vertices;
                if (vertices != null)
                    for (int n = 1; n < vertices.Count - 1; n++)
                    {
                        Vector2 point = vertices[link.FirstNodeIndex == route.Nodes[i] ? n : vertices.Count - 1 - n];
                        points.Add(new Vector3(point.x, link.PathHeightSampler?.Invoke(point) ?? 0f, point.y));
                    }
                points.Add(footways.Nodes[route.Nodes[i + 1]].Position);
            }
            points.Add(to.WorldPosition);
            return points.AsReadOnly();
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
