using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A measured route section where an existing strip hands its
    /// surface to a junction. Direction is the outward tangent at the mouth.</summary>
    internal sealed class AlpineVillageJunctionPort
    {
        internal AlpineVillageJunctionPort(string pathStableId, Vector2 mouthCenter,
            Vector2 direction, float halfWidth, float reach, AlpineVillagePathKind kind,
            List<Vector2> centerline)
        {
            PathStableId = pathStableId;
            MouthCenter = mouthCenter;
            Direction = direction;
            HalfWidth = halfWidth;
            Reach = reach;
            Kind = kind;
            Centerline = centerline.AsReadOnly();
        }

        internal string PathStableId { get; }
        internal Vector2 MouthCenter { get; }
        internal Vector2 Direction { get; }
        internal float HalfWidth { get; }
        internal float Reach { get; }
        internal AlpineVillagePathKind Kind { get; }
        internal IReadOnlyList<Vector2> Centerline { get; }
    }

    /// <summary>One surface owner for a route meeting. Ownership removes the
    /// old strip ends; Contour supplies the new shared ground and snow opening.
    /// All coordinates are world XZ and all polygons are counterclockwise.</summary>
    internal sealed class AlpineVillageJunctionPlan
    {
        private const float Epsilon = .00001f;
        private const int CurveSteps = 14;
        private readonly List<Vector2> asphaltOutline;
        private readonly bool allAsphalt;

        private AlpineVillageJunctionPlan(string stableId, Vector2 center,
            List<AlpineVillageJunctionPort> ports)
        {
            StableId = stableId;
            Center = center;
            ports.Sort((a, b) => Angle(a.MouthCenter - center).CompareTo(Angle(b.MouthCenter - center)));
            Ports = ports.AsReadOnly();
            List<Vector2> contour = BuildContour(center, ports, stableId == "trade-yard-junction");
            Contour = contour.AsReadOnly();
            OuterTriangles = Triangulate(contour).AsReadOnly();

            var envelope = new List<Vector2>(contour);
            foreach (AlpineVillageJunctionPort port in ports)
            {
                // Intermediate bends belong wholly to this owner too. The
                // mouth itself keeps the exact cross-section, with no cap.
                for (int index = 0; index + 1 < port.Centerline.Count; index++)
                {
                    Vector2 point = port.Centerline[index];
                    float width = port.HalfWidth + .025f;
                    envelope.Add(point + new Vector2(-width, -width));
                    envelope.Add(point + new Vector2(-width, width));
                    envelope.Add(point + new Vector2(width, -width));
                    envelope.Add(point + new Vector2(width, width));
                }
            }
            List<Vector2> ownership = ConvexHull(envelope);
            OwnershipContour = ownership.AsReadOnly();
            Vector2 min = ownership[0], max = min;
            foreach (Vector2 point in ownership)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            allAsphalt = ports.TrueForAll(port => port.Kind == AlpineVillagePathKind.AbandonedRoad);
            asphaltOutline = BuildAsphaltOutline(ports);
            if (!Contains(center))
                throw new InvalidOperationException(stableId + " excludes its own route meeting.");
        }

        internal string StableId { get; }
        internal Vector2 Center { get; }
        internal IReadOnlyList<AlpineVillageJunctionPort> Ports { get; }
        internal IReadOnlyList<Vector2> Contour { get; }
        internal IReadOnlyList<Vector2> OwnershipContour { get; }
        internal IReadOnlyList<IReadOnlyList<Vector2>> OuterTriangles { get; }
        internal Rect Bounds { get; }

        /// <summary>The initial paint mask, not a geometry boundary. Saved mask
        /// PNGs may be painted independently after their first generation.</summary>
        internal float SampleAsphaltWeight(Vector2 point)
        {
            if (allAsphalt) return 1f;
            if (asphaltOutline.Count == 0) return 0f;

            bool inside = false;
            float nearestSquared = float.PositiveInfinity;
            for (int index = 0; index < asphaltOutline.Count; index++)
            {
                Vector2 a = asphaltOutline[index], b = asphaltOutline[(index + 1) % asphaltOutline.Count];
                Vector2 edge = b - a;
                Vector2 nearest = a + edge * Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude);
                nearestSquared = Mathf.Min(nearestSquared, (point - nearest).sqrMagnitude);
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < a.x + (double)(b.x - a.x) * (point.y - a.y) / (b.y - a.y)) inside = !inside;
            }
            float distance = Mathf.Sqrt(nearestSquared) * (inside ? 1f : -1f);
            // World-locked shallow wear prevents a mechanically even gradient.
            float wear = .075f * Mathf.Sin(point.x * 3.1f + point.y * 1.7f) +
                .035f * Mathf.Sin(point.x * 7.3f - point.y * 4.7f);
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.6f, .6f, distance + wear));
            foreach (AlpineVillageJunctionPort port in Ports)
            {
                Vector2 delta = point - port.MouthCenter;
                float along = Vector2.Dot(delta, port.Direction);
                float across = Mathf.Abs(Vector2.Dot(delta, Left(port.Direction)));
                float mouth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1.3f, -.3f, along)) *
                    (1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(port.HalfWidth + .15f, port.HalfWidth + .65f, across)));
                weight = Mathf.Lerp(weight, port.Kind == AlpineVillagePathKind.AbandonedRoad ? 1f : 0f, mouth);
            }
            return weight;
        }

        internal bool Owns(Vector2 point)
        {
            if (point.x < Bounds.xMin - Epsilon || point.x > Bounds.xMax + Epsilon ||
                point.y < Bounds.yMin - Epsilon || point.y > Bounds.yMax + Epsilon) return false;
            for (int index = 0; index < OwnershipContour.Count; index++)
                if (Cross(OwnershipContour[(index + 1) % OwnershipContour.Count] - OwnershipContour[index],
                    point - OwnershipContour[index]) < -Epsilon) return false;
            return true;
        }

        internal bool Contains(Vector2 point) => DistanceOutside(point, out _) <= 0f;

        internal float DistanceOutside(Vector2 point, out Vector2 outward)
        {
            bool inside = false;
            float distanceSquared = float.PositiveInfinity;
            Vector2 nearest = Center;
            Vector2 edgeNormal = Vector2.up;
            for (int index = 0; index < Contour.Count; index++)
            {
                Vector2 a = Contour[index], b = Contour[(index + 1) % Contour.Count];
                Vector2 edge = b - a;
                float amount = Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude);
                Vector2 candidate = a + edge * amount;
                float squared = (point - candidate).sqrMagnitude;
                if (squared < distanceSquared)
                {
                    distanceSquared = squared;
                    nearest = candidate;
                    edgeNormal = new Vector2(edge.y, -edge.x).normalized;
                }
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < a.x + (double)(b.x - a.x) * (point.y - a.y) / (b.y - a.y))
                    inside = !inside;
            }
            float distance = Mathf.Sqrt(distanceSquared);
            outward = distance <= Epsilon ? edgeNormal :
                (inside ? nearest - point : point - nearest) / distance;
            return distance <= Epsilon ? 0f : inside ? -distance : distance;
        }

        internal static IReadOnlyList<AlpineVillageJunctionPlan> Create(AlpineVillageExpansionPlan expansion)
        {
            var result = new List<AlpineVillageJunctionPlan>();
            Add("forest-return-junction", new Vector2(-37f, -3f), 3);
            Add("ski-base-junction", new Vector2(-120f, 22f), 3);
            Add("forest-loop-road-junction", new Vector2(-112f, 93f), 2);
            Add("trade-yard-junction", new Vector2(-130f, -28f), 3);
            return result.AsReadOnly();

            void Add(string id, Vector2 local, int expectedPorts)
            {
                Vector3 at = expansion.ToWorld(local);
                Vector2 center = new Vector2(at.x, at.z);
                var ports = new List<AlpineVillageJunctionPort>();
                for (int index = 0; index < expansion.Paths.Count; index++)
                {
                    AlpineVillagePathDescriptor path = expansion.Paths[index];
                    Vector2 a = XZ(path.Start), b = XZ(path.End), span = b - a;
                    float along = Vector2.Dot(center - a, span) / span.sqrMagnitude;
                    if (along < -.00001f || along > 1.00001f ||
                        (a + span * Mathf.Clamp01(along) - center).sqrMagnitude > .000001f) continue;
                    if (along > .00001f) ports.Add(WalkPort(expansion.Paths, index, center, true));
                    if (along < .99999f) ports.Add(WalkPort(expansion.Paths, index, center, false));
                }
                if (ports.Count != expectedPorts)
                    throw new InvalidOperationException(id + " has " + ports.Count + " route mouths.");
                result.Add(new AlpineVillageJunctionPlan(id, center, ports));
            }
        }

        private static AlpineVillageJunctionPort WalkPort(IReadOnlyList<AlpineVillagePathDescriptor> paths,
            int index, Vector2 center, bool reverse)
        {
            AlpineVillagePathDescriptor first = paths[index];
            string route = first.StableId.Substring(0, first.StableId.LastIndexOf('-'));
            float reach = first.Kind == AlpineVillagePathKind.AbandonedRoad ? 6f :
                first.Kind == AlpineVillagePathKind.SkiBaseAccess ? 5.6f : 5.2f;
            // At the acute forest-return meeting the broad road mouth projects
            // beyond this narrow branch's former 5.2 m cut. Move its section
            // onto the ownership hull boundary, so trimming cannot leave an
            // unpainted gap between the junction and the continuing trail.
            if (first.StableId == "village-forest-approach-3") reach = 5.75f;
            if (route == "village-trade-yard") reach = 4.5f;
            float remaining = reach;
            Vector2 current = center;
            var centerline = new List<Vector2> { center };
            for (int step = 0; step < paths.Count; step++)
            {
                AlpineVillagePathDescriptor path = paths[index];
                Vector2 end = XZ(reverse ? path.Start : path.End);
                Vector2 delta = end - current;
                float length = delta.magnitude;
                Vector2 direction = delta / length;
                if (remaining <= length)
                {
                    Vector2 mouth = current + direction * remaining;
                    centerline.Add(mouth);
                    return new AlpineVillageJunctionPort(first.StableId, mouth, direction,
                        first.SurfaceHalfWidth, reach, first.Kind, centerline);
                }
                remaining -= length;
                centerline.Add(end);
                int next = -1;
                for (int candidate = 0; candidate < paths.Count; candidate++)
                {
                    if (candidate == index || !paths[candidate].StableId.StartsWith(route + "-", StringComparison.Ordinal)) continue;
                    if ((XZ(reverse ? paths[candidate].End : paths[candidate].Start) - end).sqrMagnitude < .000001f)
                    { next = candidate; break; }
                }
                if (next < 0)
                    return new AlpineVillageJunctionPort(first.StableId, end, direction,
                        first.SurfaceHalfWidth, reach - remaining, first.Kind, centerline);
                index = next;
                current = end;
            }
            throw new InvalidOperationException(first.StableId + " cannot resolve its junction mouth.");
        }

        private static List<Vector2> BuildContour(Vector2 center, List<AlpineVillageJunctionPort> ports,
            bool warehouseApron)
        {
            var points = new List<Vector2>();
            for (int index = 0; index < ports.Count; index++)
            {
                AlpineVillageJunctionPort port = ports[index], next = ports[(index + 1) % ports.Count];
                Vector2 left = Left(port.Direction) * port.HalfWidth;
                Vector2 a = port.MouthCenter - center + left;
                Vector2 b = next.MouthCenter - center - Left(next.Direction) * next.HalfWidth;
                points.Add(port.MouthCenter - center - left);
                points.Add(a);
                // The warehouse spur turns alongside the main road just past
                // this mouth, and their 5.4 m strips already overlap. An inner
                // bank here becomes a closed snow island in that shared apron.
                // Join these two mouth corners directly; the continuation on
                // the other side is asphalt too. The visible outer curves and
                // all mouth sections stay exactly where they were.
                if (warehouseApron && port.PathStableId.StartsWith("village-trade-yard-", StringComparison.Ordinal))
                    continue;
                float gap = Mathf.Repeat(Angle(next.MouthCenter - center) - Angle(port.MouthCenter - center), 2f * Mathf.PI);
                float firstHandle = port.Reach * (gap > Mathf.PI ? 1.15f : .82f);
                float nextHandle = next.Reach * (gap > Mathf.PI ? 1.15f : .82f);
                double turn = Cross(port.Direction, next.Direction);
                if (gap < Mathf.PI && Math.Abs(turn) > .0001)
                {
                    float firstIntersection = (float)(Cross(b - a, -next.Direction) / turn);
                    float nextIntersection = (float)(Cross(b - a, -port.Direction) / turn);
                    if (firstIntersection > 0f && nextIntersection > 0f)
                    {
                        firstHandle = Mathf.Min(firstHandle, firstIntersection * .92f);
                        nextHandle = Mathf.Min(nextHandle, nextIntersection * .92f);
                    }
                }
                Vector2 firstControl = a - port.Direction * firstHandle;
                Vector2 secondControl = b - next.Direction * nextHandle;
                for (int step = 1; step < CurveSteps; step++)
                {
                    float t = step / (float)CurveSteps, one = 1f - t;
                    points.Add(one * one * one * a + 3f * one * one * t * firstControl +
                        3f * one * t * t * secondControl + t * t * t * b);
                }
            }
            EnsureCounterclockwise(points);
            for (int index = 0; index < points.Count; index++) points[index] += center;
            return points;
        }

        private List<Vector2> BuildAsphaltOutline(List<AlpineVillageJunctionPort> ports)
        {
            AlpineVillageJunctionPort road = null;
            foreach (AlpineVillageJunctionPort port in ports)
                if (port.Kind == AlpineVillagePathKind.AbandonedRoad) road = port;
            var result = new List<Vector2>();
            if (allAsphalt) return result;
            if (road == null) return result;

            // One worn cross-edge, not a round cap laid over the soil. A
            // modest flare lets the broad road enter the shared meeting;
            // all soil mouths remain soil at their boundary with old paths.
            float[] across = { -1f, -.68f, -.26f, .17f, .61f, 1f };
            float[] wear = { -.31f, .34f, -.16f, .43f, -.39f, .12f };
            Vector2 side = Left(road.Direction);
            float flare = road.HalfWidth + .45f;
            for (int index = 0; index < across.Length; index++)
                result.Add(Center + side * (across[index] * flare) + road.Direction * wear[index]);
            result.Add(road.MouthCenter + side * road.HalfWidth);
            result.Add(road.MouthCenter - side * road.HalfWidth);
            EnsureCounterclockwise(result);
            return result;
        }

        private static List<IReadOnlyList<Vector2>> Triangulate(List<Vector2> polygon)
        {
            var remaining = new List<int>();
            for (int index = 0; index < polygon.Count; index++) remaining.Add(index);
            var result = new List<IReadOnlyList<Vector2>>();
            while (remaining.Count > 3)
            {
                bool found = false;
                for (int index = 0; index < remaining.Count; index++)
                {
                    int first = remaining[(index + remaining.Count - 1) % remaining.Count];
                    int middle = remaining[index], last = remaining[(index + 1) % remaining.Count];
                    Vector2 a = polygon[first], b = polygon[middle], c = polygon[last];
                    if (Cross(b - a, c - a) <= .000001) continue;
                    bool blocked = false;
                    foreach (int candidate in remaining)
                    {
                        if (candidate == first || candidate == middle || candidate == last) continue;
                        Vector2 point = polygon[candidate];
                        if (Cross(b - a, point - a) >= -Epsilon && Cross(c - b, point - b) >= -Epsilon &&
                            Cross(a - c, point - c) >= -Epsilon) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    result.Add(Array.AsReadOnly(new[] { a, b, c }));
                    remaining.RemoveAt(index);
                    found = true;
                    break;
                }
                if (!found) throw new InvalidOperationException("The junction contour cannot be triangulated.");
            }
            result.Add(Array.AsReadOnly(new[] { polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]] }));
            return result;
        }

        private static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var hull = new List<Vector2>();
            foreach (Vector2 point in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                    point - hull[hull.Count - 1]) <= 0d) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int index = points.Count - 2; index >= 0; index--)
            {
                Vector2 point = points[index];
                while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2],
                    point - hull[hull.Count - 1]) <= 0d) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static void EnsureCounterclockwise(List<Vector2> polygon)
        { if (Area(polygon) < 0d) polygon.Reverse(); }
        private static double Area(IReadOnlyList<Vector2> polygon)
        {
            double area = 0d;
            for (int index = 1; index + 1 < polygon.Count; index++)
                area += Cross(polygon[index] - polygon[0], polygon[index + 1] - polygon[0]);
            return area * .5d;
        }
        private static Vector2 Left(Vector2 direction) => new Vector2(-direction.y, direction.x);
        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x);
        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
    }
}
