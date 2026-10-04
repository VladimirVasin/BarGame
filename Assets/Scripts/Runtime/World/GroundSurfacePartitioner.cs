using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Partitions upward terrain faces in XZ by caller-defined priority.
    /// Cuts retain each source plane and its appearance coordinates; volume and
    /// material ownership remain the caller's responsibility.</summary>
    internal static class GroundSurfacePartitioner
    {
        private const double MinimumArea = 1e-10;
        private const double OverlapTolerance = 1e-6;

        internal readonly struct Vertex
        {
            internal Vertex(Vector3 position, Vector3 normal, Vector2 uv0,
                Vector4 roadCoordinates, Color color)
            { Position = position; Normal = normal; Uv0 = uv0; RoadCoordinates = roadCoordinates; Color = color; }
            internal Vector3 Position { get; }
            internal Vector3 Normal { get; }
            internal Vector2 Uv0 { get; }
            internal Vector4 RoadCoordinates { get; }
            internal Color Color { get; }
            internal static Vertex Lerp(Vertex first, Vertex last, float fraction) => new Vertex(
                Vector3.LerpUnclamped(first.Position, last.Position, fraction),
                Vector3.LerpUnclamped(first.Normal, last.Normal, fraction),
                Vector2.LerpUnclamped(first.Uv0, last.Uv0, fraction),
                Vector4.LerpUnclamped(first.RoadCoordinates, last.RoadCoordinates, fraction),
                UnityEngine.Color.LerpUnclamped(first.Color, last.Color, fraction));
        }

        internal readonly struct Triangle
        {
            internal Triangle(Vertex a, Vertex b, Vertex c, int surface)
            { A = a; B = b; C = c; Surface = surface; }
            internal Vertex A { get; }
            internal Vertex B { get; }
            internal Vertex C { get; }
            internal int Surface { get; }
        }

        internal readonly struct Statistics
        {
            internal Statistics(int inputs, int outputs, long candidates, long overlaps, long bucketEntriesVisited = 0)
            {
                InputTriangles = inputs; OutputTriangles = outputs; CandidatePairs = candidates;
                ClippedPairs = overlaps; BucketEntriesVisited = bucketEntriesVisited;
            }
            internal int InputTriangles { get; }
            internal int OutputTriangles { get; }
            internal long CandidatePairs { get; }
            internal long ClippedPairs { get; }
            internal long BucketEntriesVisited { get; }
        }

        internal static List<Triangle> Partition(IReadOnlyList<Triangle> source, float bucketSize = 8f)
            => Partition(source, out _, bucketSize);

        /// <summary>Clips a face to a convex footprint in XZ. Either contour
        /// winding is accepted; the source face's winding and attributes stay.</summary>
        internal static List<Triangle> Intersect(Triangle source, IReadOnlyList<Vector2> footprint)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (footprint.Count < 3) return new List<Triangle>();
            return Intersect(source, new Footprint(footprint));
        }

        internal static Footprint Prepare(Triangle source) => new Footprint(new[]
        {
            XZ(source.A.Position), XZ(source.C.Position), XZ(source.B.Position)
        });

        /// <summary>Reuses a support contour and its SAT axes across paint faces.
        /// Misses allocate no appearance polygon or discarded split halves.</summary>
        internal static List<Triangle> Intersect(Triangle source, Footprint cut)
        {
            if (cut == null) throw new ArgumentNullException(nameof(cut));
            var result = new List<Triangle>();
            if (!HasArea(source.A, source.B, source.C)
                || !Bounds(XZ(source.A.Position), XZ(source.B.Position), XZ(source.C.Position)).Overlaps(cut.Bounds)
                || !HasPositiveOverlap(source, cut)) return result;
            var inside = new List<Vertex> { source.A, source.B, source.C };
            for (int edge = 0; edge < cut.Points.Length && inside.Count >= 3; edge++)
                inside = ClipInside(inside, cut.Points[edge], cut.Points[(edge + 1) % cut.Points.Length]);
            AppendFan(inside, source.Surface, result);
            return result;
        }

        /// <summary>Retains a rectangle's portion of a face without changing
        /// its plane or appearance. Useful for an independently rebuilt sector.</summary>
        internal static List<Triangle> ClipToRect(Triangle source, Rect bounds)
        {
            if (bounds.width <= 0f || bounds.height <= 0f) return new List<Triangle>();
            return Intersect(source, new Footprint(bounds));
        }

        /// <summary>The complementary static portion of ClipToRect. Touching
        /// edges retain the original face instead of introducing slivers.</summary>
        internal static List<Triangle> SubtractRect(Triangle source, Rect bounds)
        {
            var result = new List<Triangle>();
            if (!HasArea(source.A, source.B, source.C)) return result;
            if (bounds.width <= 0f || bounds.height <= 0f)
            { result.Add(source); return result; }
            var footprint = new Footprint(bounds);
            if (!Bounds(XZ(source.A.Position), XZ(source.B.Position), XZ(source.C.Position)).Overlaps(bounds)
                || !HasPositiveOverlap(source, footprint))
            { result.Add(source); return result; }
            var inside = new List<Vertex> { source.A, source.B, source.C };
            for (int edge = 0; edge < footprint.Points.Length && inside.Count >= 3; edge++)
            {
                Split(inside, footprint.Points[edge], footprint.Points[(edge + 1) % footprint.Points.Length],
                    out List<Vertex> kept, out List<Vertex> rejected);
                AppendFan(rejected, source.Surface, result);
                inside = kept;
            }
            return result;
        }

        /// <summary>Faces arrive highest priority first. Equal-priority ordering
        /// is explicit too: the first face owns any positive projected overlap.</summary>
        internal static List<Triangle> Partition(IReadOnlyList<Triangle> source,
            out Statistics statistics, float bucketSize = 8f)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidatePositive(bucketSize, nameof(bucketSize));
            var result = new List<Triangle>(source.Count);
            var footprints = new List<Footprint>(source.Count);
            var buckets = new Dictionary<Vector2Int, List<int>>();
            var candidates = new List<int>();
            var visited = new HashSet<int>();
            long candidatePairs = 0, clippedPairs = 0, bucketEntriesVisited = 0;
            foreach (Triangle triangle in source)
            {
                if (!HasArea(triangle.A, triangle.B, triangle.C)) continue;
                var footprint = new Footprint(triangle);
                candidates.Clear();
                visited.Clear();
                Rect bounds = footprint.Bounds;
                Vector2Int minimumBucket = Bucket(bounds.min, bucketSize), maximumBucket = Bucket(bounds.max, bucketSize);
                for (int x = minimumBucket.x; x <= maximumBucket.x; x++)
                for (int z = minimumBucket.y; z <= maximumBucket.y; z++)
                {
                    if (!buckets.TryGetValue(new Vector2Int(x, z), out List<int> entries)) continue;
                    foreach (int entry in entries)
                    {
                        bucketEntriesVisited++;
                        // Dense buckets contain many nearby faces whose bounds
                        // miss this face. Reject those before growing or probing
                        // the per-face deduplication set.
                        if (!bounds.Overlaps(footprints[entry].Bounds)) continue;
                        if (visited.Add(entry)) candidates.Add(entry);
                    }
                }
                // Bucket and insertion traversal are deterministic. Polygon
                // subtraction preserves the union regardless of candidate order.
                List<List<Vertex>> remaining = null;
                foreach (int candidate in candidates)
                {
                    candidatePairs++;
                    Footprint cut = footprints[candidate];
                    if (remaining == null)
                    {
                        // Most terrain faces are unchanged. Avoid allocating
                        // polygon storage or copying appearance data for them.
                        if (!HasPositiveOverlap(triangle, cut)) continue;
                        remaining = new List<List<Vertex>> { new List<Vertex> { triangle.A, triangle.B, triangle.C } };
                    }
                    List<List<Vertex>> outside = null;
                    for (int piece = 0; piece < remaining.Count; piece++)
                    {
                        List<Vertex> polygon = remaining[piece];
                        if (!HasPositiveOverlap(polygon, cut))
                        {
                            if (outside != null) outside.Add(polygon);
                            continue;
                        }
                        // Most bucket candidates only touch or miss a face.
                        // Allocate residual lists only once a real cut exists.
                        if (outside == null)
                        {
                            outside = new List<List<Vertex>>();
                            for (int retained = 0; retained < piece; retained++) outside.Add(remaining[retained]);
                        }
                        clippedPairs++;
                        List<Vertex> inside = polygon;
                        for (int edge = 0; edge < cut.Points.Length && inside.Count >= 3; edge++)
                        {
                            Split(inside, cut.Points[edge], cut.Points[(edge + 1) % cut.Points.Length],
                                out List<Vertex> kept, out List<Vertex> rejected);
                            if (HasArea(rejected)) outside.Add(rejected);
                            inside = kept;
                        }
                    }
                    if (outside != null) remaining = outside;
                    if (remaining.Count == 0) break;
                }
                if (remaining == null) result.Add(triangle);
                else foreach (List<Vertex> polygon in remaining) AppendFan(polygon, triangle.Surface, result);
                // Original footprints have the same union as their accepted
                // residuals plus earlier owners, and avoid indexing every sliver.
                int index = footprints.Count;
                footprints.Add(footprint);
                for (int x = minimumBucket.x; x <= maximumBucket.x; x++)
                for (int z = minimumBucket.y; z <= maximumBucket.y; z++)
                {
                    var key = new Vector2Int(x, z);
                    if (!buckets.TryGetValue(key, out List<int> entries))
                        buckets.Add(key, entries = new List<int>());
                    entries.Add(index);
                }
            }
            statistics = new Statistics(source.Count, result.Count, candidatePairs, clippedPairs, bucketEntriesVisited);
            return result;
        }

        /// <summary>Splits matching-height neighbouring edges at the same XZ
        /// samples. Each face keeps its own height, UVs and normal; raised curbs
        /// and unrelated stacked surfaces are never pulled together.</summary>
        internal static List<Triangle> Conform(IReadOnlyList<Triangle> source,
            float positionTolerance = .00015f, float heightTolerance = .001f,
            float bucketSize = 2f, IReadOnlyList<Rect> regions = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidatePositive(positionTolerance, nameof(positionTolerance));
            ValidatePositive(heightTolerance, nameof(heightTolerance));
            ValidatePositive(bucketSize, nameof(bucketSize));
            float squaredTolerance = positionTolerance * positionTolerance;
            var points = new Dictionary<Vector2Int, List<Vector3>>();
            foreach (Triangle triangle in source)
            {
                if (!Affected(triangle, regions)) continue;
                AddPoint(triangle.A.Position); AddPoint(triangle.B.Position); AddPoint(triangle.C.Position);
            }
            var result = new List<Triangle>(source.Count);
            var ring = new List<Vertex>();
            var cuts = new List<float>();
            foreach (Triangle triangle in source)
            {
                if (!Affected(triangle, regions)) { result.Add(triangle); continue; }
                ring.Clear();
                Edge(triangle.A, triangle.B); Edge(triangle.B, triangle.C); Edge(triangle.C, triangle.A);
                if (ring.Count == 3) { result.Add(triangle); continue; }
                // A centre fan retains collinear inserted rim samples. A fan
                // from a rim corner would skip some as degenerate triangles.
                Vertex centre = Vertex.Lerp(Vertex.Lerp(triangle.A, triangle.B, .5f), triangle.C, 1f / 3f);
                for (int edge = 0; edge < ring.Count; edge++)
                    if (HasArea(centre, ring[edge], ring[(edge + 1) % ring.Count]))
                        result.Add(new Triangle(centre, ring[edge], ring[(edge + 1) % ring.Count], triangle.Surface));
            }
            return result;

            void AddPoint(Vector3 point)
            {
                Vector2Int key = Bucket(XZ(point), bucketSize);
                if (!points.TryGetValue(key, out List<Vector3> entries))
                    points.Add(key, entries = new List<Vector3>());
                foreach (Vector3 existing in entries)
                    if ((existing - point).sqrMagnitude <= squaredTolerance) return;
                entries.Add(point);
            }

            void Edge(Vertex first, Vertex last)
            {
                ring.Add(first);
                Vector2 start = XZ(first.Position), end = XZ(last.Position), delta = end - start;
                float squaredLength = delta.sqrMagnitude;
                if (squaredLength <= squaredTolerance) return;
                float length = Mathf.Sqrt(squaredLength);
                cuts.Clear();
                Rect bounds = Rect.MinMaxRect(Mathf.Min(start.x, end.x) - positionTolerance,
                    Mathf.Min(start.y, end.y) - positionTolerance,
                    Mathf.Max(start.x, end.x) + positionTolerance, Mathf.Max(start.y, end.y) + positionTolerance);
                Vector2Int min = Bucket(bounds.min, bucketSize), max = Bucket(bounds.max, bucketSize);
                bool restrictRows = max.y - min.y > 2 && max.x > min.x;
                for (int x = min.x; x <= max.x; x++)
                {
                    int firstRow = min.y, lastRow = max.y;
                    if (restrictRows && !EdgeBucketRows(start, end, positionTolerance, bucketSize,
                        x, min.y, max.y, out firstRow, out lastRow)) continue;
                    for (int z = firstRow; z <= lastRow; z++)
                    {
                        if (!points.TryGetValue(new Vector2Int(x, z), out List<Vector3> entries)) continue;
                        foreach (Vector3 point in entries)
                        {
                            float fraction = Vector2.Dot(XZ(point) - start, delta) / squaredLength;
                            if (fraction * length <= positionTolerance || (1f - fraction) * length <= positionTolerance) continue;
                            // Keep the original position interpolation's float
                            // rounding while avoiding every appearance channel
                            // for candidates that will never become a cut.
                            Vector3 onEdge = Vector3.LerpUnclamped(first.Position, last.Position, fraction);
                            float dx = point.x - onEdge.x, dz = point.z - onEdge.z;
                            if (dx * dx + dz * dz > squaredTolerance) continue;
                            if (Mathf.Abs(point.y - onEdge.y) > heightTolerance) continue;
                            // Appearance interpolation is only needed when
                            // this point actually becomes an edge subdivision.
                            cuts.Add(fraction);
                        }
                    }
                }
                if (cuts.Count > 1) cuts.Sort();
                float previous = -1f;
                foreach (float fraction in cuts)
                {
                    if ((fraction - previous) * length <= positionTolerance) continue;
                    ring.Add(Vertex.Lerp(first, last, fraction));
                    previous = fraction;
                }
            }
        }

        private static bool Affected(Triangle triangle, IReadOnlyList<Rect> regions)
        {
            if (regions == null) return true;
            Rect bounds = Bounds(XZ(triangle.A.Position), XZ(triangle.B.Position), XZ(triangle.C.Position));
            foreach (Rect region in regions) if (region.Overlaps(bounds)) return true;
            return false;
        }

        private static bool HasPositiveOverlap(List<Vertex> polygon, Footprint cut)
        {
            Vector2 min = XZ(polygon[0].Position), max = min;
            for (int index = 1; index < polygon.Count; index++)
            {
                Vector2 point = XZ(polygon[index].Position);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            if (!Rect.MinMaxRect(min.x, min.y, max.x, max.y).Overlaps(cut.Bounds)) return false;
            for (int edge = 0; edge < polygon.Count; edge++)
            {
                Vector2 start = XZ(polygon[edge].Position);
                if (Separated(start, new SatEdge(XZ(polygon[(edge + 1) % polygon.Count].Position) - start))) return false;
            }
            for (int edge = 0; edge < cut.Points.Length; edge++)
                if (Separated(cut.Points[edge], cut.Edge(edge))) return false;
            return true;

            bool Separated(Vector2 start, SatEdge edge)
            {
                if (edge.Tolerance < 0d) return false;
                Vector2 axis = edge.Axis;
                double firstMin = double.PositiveInfinity, firstMax = double.NegativeInfinity;
                double secondMin = double.PositiveInfinity, secondMax = double.NegativeInfinity;
                foreach (Vertex vertex in polygon)
                {
                    double value = Cross(axis, XZ(vertex.Position) - start);
                    firstMin = Math.Min(firstMin, value); firstMax = Math.Max(firstMax, value);
                }
                foreach (Vector2 point in cut.Points)
                {
                    double value = Cross(axis, point - start);
                    secondMin = Math.Min(secondMin, value); secondMax = Math.Max(secondMax, value);
                }
                return Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin) <= edge.Tolerance;
            }
        }

        private static bool HasPositiveOverlap(Triangle triangle, Footprint cut)
        {
            Vector2 a = XZ(triangle.A.Position), b = XZ(triangle.B.Position), c = XZ(triangle.C.Position);
            if (Separated(a, new SatEdge(b - a)) || Separated(b, new SatEdge(c - b))
                || Separated(c, new SatEdge(a - c))) return false;
            for (int edge = 0; edge < cut.Points.Length; edge++)
                if (Separated(cut.Points[edge], cut.Edge(edge))) return false;
            return true;

            bool Separated(Vector2 start, SatEdge edge)
            {
                if (edge.Tolerance < 0d) return false;
                Vector2 axis = edge.Axis;
                double first = Cross(axis, a - start), second = Cross(axis, b - start), third = Cross(axis, c - start);
                double firstMin = Math.Min(first, Math.Min(second, third));
                double firstMax = Math.Max(first, Math.Max(second, third));
                double secondMin = double.PositiveInfinity, secondMax = double.NegativeInfinity;
                foreach (Vector2 point in cut.Points)
                {
                    double value = Cross(axis, point - start);
                    secondMin = Math.Min(secondMin, value); secondMax = Math.Max(secondMax, value);
                }
                return Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin) <= edge.Tolerance;
            }
        }

        private static List<Vertex> ClipInside(List<Vertex> polygon, Vector2 start, Vector2 end)
        {
            var inside = new List<Vertex>(polygon.Count + 1);
            Vector2 axis = end - start;
            Vertex previous = polygon[polygon.Count - 1];
            double previousDistance = Cross(axis, XZ(previous.Position) - start);
            foreach (Vertex current in polygon)
            {
                double distance = Cross(axis, XZ(current.Position) - start);
                if ((previousDistance >= 0) != (distance >= 0))
                    inside.Add(Vertex.Lerp(previous, current,
                        (float)(previousDistance / (previousDistance - distance))));
                if (distance >= 0) inside.Add(current);
                previous = current; previousDistance = distance;
            }
            return inside;
        }

        private static void Split(List<Vertex> polygon, Vector2 start, Vector2 end,
            out List<Vertex> inside, out List<Vertex> outside)
        {
            inside = new List<Vertex>(polygon.Count + 1);
            outside = new List<Vertex>(polygon.Count + 1);
            Vector2 axis = end - start;
            Vertex previous = polygon[polygon.Count - 1];
            double previousDistance = Cross(axis, XZ(previous.Position) - start);
            foreach (Vertex current in polygon)
            {
                double distance = Cross(axis, XZ(current.Position) - start);
                if ((previousDistance >= 0) != (distance >= 0))
                {
                    Vertex crossing = Vertex.Lerp(previous, current,
                        (float)(previousDistance / (previousDistance - distance)));
                    inside.Add(crossing); outside.Add(crossing);
                }
                if (distance >= 0) inside.Add(current); else outside.Add(current);
                previous = current; previousDistance = distance;
            }
        }

        private static void AppendFan(List<Vertex> polygon, int surface, List<Triangle> result)
        {
            for (int corner = 1; corner + 1 < polygon.Count; corner++)
                if (HasArea(polygon[0], polygon[corner], polygon[corner + 1]))
                    result.Add(new Triangle(polygon[0], polygon[corner], polygon[corner + 1], surface));
        }

        private static bool HasArea(List<Vertex> polygon)
        {
            if (polygon.Count < 3) return false;
            double area = 0;
            for (int corner = 1; corner + 1 < polygon.Count; corner++)
                area += Cross(XZ(polygon[corner].Position - polygon[0].Position),
                    XZ(polygon[corner + 1].Position - polygon[0].Position));
            return Math.Abs(area) > MinimumArea;
        }

        private static bool HasArea(in Vertex a, in Vertex b, in Vertex c)
        {
            double abx = (double)b.Position.x - a.Position.x, abz = (double)b.Position.z - a.Position.z;
            double acx = (double)c.Position.x - a.Position.x, acz = (double)c.Position.z - a.Position.z;
            double bcx = (double)c.Position.x - b.Position.x, bcz = (double)c.Position.z - b.Position.z;
            double cross = Math.Abs(abx * acz - abz * acx);
            if (cross <= MinimumArea) return false;
            double longest = Math.Sqrt(Math.Max(abx * abx + abz * abz,
                Math.Max(acx * acx + acz * acz, bcx * bcx + bcz * bcz)));
            // An absolute area guard alone retains long float-roundoff wedges.
            // PhysX can hit their ill-conditioned plane away from the ray. The
            // minimum altitude uses the same spatial precision as overlap SAT,
            // while genuinely small, well-shaped source faces remain valid.
            return cross >= longest * OverlapTolerance;
        }

        internal readonly struct SatEdge
        {
            internal SatEdge(Vector2 axis)
            {
                Axis = axis;
                double length = Math.Sqrt((double)axis.x * axis.x + (double)axis.y * axis.y);
                Tolerance = length < 1e-12 ? -1d : length * OverlapTolerance;
            }
            internal Vector2 Axis { get; }
            internal double Tolerance { get; }
        }

        internal sealed class Footprint
        {
            private readonly SatEdge firstEdge, secondEdge, thirdEdge;
            internal Footprint(IReadOnlyList<Vector2> points)
            {
                Points = new Vector2[points.Count];
                Vector2 min = points[0], max = min;
                double area = 0;
                for (int index = 0; index < points.Count; index++)
                {
                    Points[index] = points[index];
                    min = Vector2.Min(min, points[index]); max = Vector2.Max(max, points[index]);
                    if (index > 0 && index + 1 < points.Count)
                        area += Cross(points[index] - points[0], points[index + 1] - points[0]);
                }
                if (area < 0) Array.Reverse(Points);
                Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                firstEdge = new SatEdge(Points[1] - Points[0]);
                secondEdge = new SatEdge(Points[2] - Points[1]);
                thirdEdge = new SatEdge(Points[3 % Points.Length] - Points[2]);
            }
            internal Footprint(Rect bounds)
            {
                Points = new[] { bounds.min, new Vector2(bounds.xMax, bounds.yMin),
                    bounds.max, new Vector2(bounds.xMin, bounds.yMax) };
                Bounds = bounds;
                firstEdge = new SatEdge(Points[1] - Points[0]);
                secondEdge = new SatEdge(Points[2] - Points[1]);
                thirdEdge = new SatEdge(Points[3] - Points[2]);
            }
            internal Footprint(Triangle triangle)
            {
                Points = new[] { XZ(triangle.A.Position), XZ(triangle.B.Position), XZ(triangle.C.Position) };
                if (Cross(Points[1] - Points[0], Points[2] - Points[0]) < 0) Array.Reverse(Points);
                Bounds = GroundSurfacePartitioner.Bounds(Points[0], Points[1], Points[2]);
                firstEdge = new SatEdge(Points[1] - Points[0]);
                secondEdge = new SatEdge(Points[2] - Points[1]);
                thirdEdge = new SatEdge(Points[0] - Points[2]);
            }
            internal Vector2[] Points { get; }
            internal Rect Bounds { get; }
            internal SatEdge Edge(int index)
            {
                if (index == 0) return firstEdge;
                if (index == 1) return secondEdge;
                if (index == 2) return thirdEdge;
                return new SatEdge(Points[(index + 1) % Points.Length] - Points[index]);
            }
        }

        private static bool EdgeBucketRows(Vector2 start, Vector2 end, float tolerance, float size,
            int column, int minimumRow, int maximumRow, out int firstRow, out int lastRow)
        {
            // Query a conservative tube around the edge, not every cell in its
            // diagonal AABB. Extra padding covers float projection roundoff;
            // the actual point-distance and height tests retain their tolerances.
            double scale = Math.Max(Math.Max(Math.Abs(start.x), Math.Abs(start.y)),
                Math.Max(Math.Abs(end.x), Math.Abs(end.y)));
            double padding = tolerance + scale * (8d * 1.1920928955078125e-7d);
            double dx = (double)end.x - start.x;
            if (dx == 0d) { firstRow = minimumRow; lastRow = maximumRow; return true; }
            double lower = ((double)column * size - padding - start.x) / dx;
            double upper = (((double)column + 1d) * size + padding - start.x) / dx;
            double first = Math.Max(0d, Math.Min(lower, upper));
            double last = Math.Min(1d, Math.Max(lower, upper));
            if (first > last) { firstRow = 0; lastRow = -1; return false; }
            double dz = (double)end.y - start.y;
            double zFirst = start.y + dz * first, zLast = start.y + dz * last;
            firstRow = Math.Max(minimumRow, (int)Math.Floor((Math.Min(zFirst, zLast) - padding) / size));
            lastRow = Math.Min(maximumRow, (int)Math.Floor((Math.Max(zFirst, zLast) + padding) / size));
            return firstRow <= lastRow;
        }
        private static Vector2Int Bucket(Vector2 point, float size) =>
            new Vector2Int(Mathf.FloorToInt(point.x / size), Mathf.FloorToInt(point.y / size));
        private static Rect Bounds(Vector2 a, Vector2 b, Vector2 c) => Rect.MinMaxRect(
            Mathf.Min(a.x, Mathf.Min(b.x, c.x)), Mathf.Min(a.y, Mathf.Min(b.y, c.y)),
            Mathf.Max(a.x, Mathf.Max(b.x, c.x)), Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
        private static Vector2 XZ(Vector3 value) => new Vector2(value.x, value.z);
        private static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
        private static void ValidatePositive(float value, string argument)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(argument);
        }
    }
}
