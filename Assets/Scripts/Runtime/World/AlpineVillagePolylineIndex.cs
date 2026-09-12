using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pruning data for a nearest-segment scan over a fixed polyline, so a
    /// query can skip the segments that cannot be the nearest WITHOUT
    /// changing which segment is chosen or the arithmetic it is measured
    /// with.
    ///
    /// Every vertex of the village ground used to measure itself against
    /// every lane segment and then every brook segment - some three hundred
    /// point-to-segment distances per sample, nearly all of them for
    /// segments on the far side of the village. The scan stays a scan: the
    /// caller keeps its own loop, its own per-segment arithmetic and its own
    /// tie rule, in index order, and only asks this index three things -
    /// <see cref="GuessSegment"/> for a first candidate so the pruning has a
    /// bound to work against from the first segment on,
    /// <see cref="ChunkCannotWin"/> and <see cref="CannotWin"/> for a
    /// triangle-inequality lower bound on a run of segments or one segment,
    /// and <see cref="FartherThan"/> for the whole polyline at once.
    ///
    /// WHY THE RESULT IS BIT-IDENTICAL. The caller's loop returns the
    /// earliest segment whose COMPUTED distance is the minimum. A segment is
    /// skipped here only when the distance from the point to a circle
    /// enclosing it exceeds, by more than <see cref="Margin"/>, a distance
    /// the caller has already computed for some other segment. Its true
    /// distance is at least the circle bound, and the caller's own value for
    /// it differs from the true distance by a few ten-thousandths of a metre
    /// at village coordinates - far less than the margin - so the skipped
    /// segment's computed distance is strictly greater than an existing one
    /// and it could never have won or tied. Every segment that can win is
    /// evaluated by the unchanged code, in the unchanged order, against the
    /// unchanged running minimum, so the winner and every value derived from
    /// it come out bit for bit as before.
    /// </summary>
    internal sealed class AlpineVillagePolylineIndex
    {
        /// <summary>Segments per bounding circle. A run this long is skipped
        /// by one comparison when the whole run is out of reach.</summary>
        internal const int ChunkSize = 8;

        /// <summary>
        /// Slack every bound carries, in metres. The float error of a
        /// point-to-segment distance at coordinates near `1000 m` is about
        /// `1e-4`; this is two and a half thousand times that, and it costs
        /// a couple of extra candidates per query at most.
        /// </summary>
        internal const float Margin = 0.25f;

        private const float GuessCell = 4f;

        // A polyline with a NaN or an infinity in it makes every bound
        // meaningless and, worse, makes the caller's own comparisons behave
        // differently once a NaN has been recorded; such an index answers
        // "no guess, nothing pruned, nothing farther" and the caller runs
        // its scan whole, as it always did.
        private readonly bool disabled;
        private readonly int segmentCount;
        private readonly float[] midX;
        private readonly float[] midZ;
        private readonly float[] reach;
        private readonly float[] chunkX;
        private readonly float[] chunkZ;
        private readonly float[] chunkReach;
        private readonly int[] guesses;
        private readonly int guessColumns;
        private readonly int guessRows;
        private readonly float guessMinX;
        private readonly float guessMinZ;
        private readonly float boundsMinX;
        private readonly float boundsMinZ;
        private readonly float boundsMaxX;
        private readonly float boundsMaxZ;

        /// <param name="points">The polyline's vertices on the ground plane;
        /// segment `k` runs from `points[k]` to `points[k + 1]`.</param>
        internal AlpineVillagePolylineIndex(Vector2[] points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (points.Length < 2)
            {
                throw new ArgumentException(
                    "A polyline index needs at least one segment.",
                    nameof(points));
            }

            segmentCount = points.Length - 1;
            ChunkCount = (segmentCount + ChunkSize - 1) / ChunkSize;
            midX = new float[segmentCount];
            midZ = new float[segmentCount];
            reach = new float[segmentCount];
            chunkX = new float[ChunkCount];
            chunkZ = new float[ChunkCount];
            chunkReach = new float[ChunkCount];
            boundsMinX = float.PositiveInfinity;
            boundsMinZ = float.PositiveInfinity;
            boundsMaxX = float.NegativeInfinity;
            boundsMaxZ = float.NegativeInfinity;
            for (int index = 0; index < points.Length; index++)
            {
                Vector2 point = points[index];
                if (!(Mathf.Abs(point.x) < float.PositiveInfinity) ||
                    !(Mathf.Abs(point.y) < float.PositiveInfinity))
                {
                    disabled = true;
                }

                boundsMinX = Mathf.Min(boundsMinX, point.x);
                boundsMinZ = Mathf.Min(boundsMinZ, point.y);
                boundsMaxX = Mathf.Max(boundsMaxX, point.x);
                boundsMaxZ = Mathf.Max(boundsMaxZ, point.y);
            }

            if (disabled)
            {
                // The chunk count stays honest so a caller's chunked loop
                // still visits every segment; every bound it asks for says
                // "cannot tell".
                guessColumns = 1;
                guessRows = 1;
                guesses = new int[1];
                return;
            }

            for (int index = 0; index < segmentCount; index++)
            {
                Vector2 a = points[index];
                Vector2 b = points[index + 1];
                midX[index] = (a.x + b.x) * 0.5f;
                midZ[index] = (a.y + b.y) * 0.5f;
                reach[index] = (b - a).magnitude * 0.5f + Margin;
            }

            for (int chunk = 0; chunk < ChunkCount; chunk++)
            {
                int firstPoint = chunk * ChunkSize;
                int lastPoint = Math.Min(
                    points.Length - 1,
                    firstPoint + ChunkSize);
                float minX = float.PositiveInfinity;
                float minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float maxZ = float.NegativeInfinity;
                for (int index = firstPoint; index <= lastPoint; index++)
                {
                    Vector2 point = points[index];
                    minX = Mathf.Min(minX, point.x);
                    minZ = Mathf.Min(minZ, point.y);
                    maxX = Mathf.Max(maxX, point.x);
                    maxZ = Mathf.Max(maxZ, point.y);
                }

                var center = new Vector2(
                    (minX + maxX) * 0.5f,
                    (minZ + maxZ) * 0.5f);
                float radius = 0f;
                for (int index = firstPoint; index <= lastPoint; index++)
                {
                    radius = Mathf.Max(
                        radius,
                        (points[index] - center).magnitude);
                }

                chunkX[chunk] = center.x;
                chunkZ[chunk] = center.y;
                chunkReach[chunk] = radius + Margin;
            }

            // The guess grid reaches two cells past the polyline; a query
            // outside it is clamped to the rim, whose guess is still a fair
            // one. A guess is only ever a starting bound, so its quality
            // changes how much is pruned and never what is returned.
            guessMinX = boundsMinX - GuessCell * 2f;
            guessMinZ = boundsMinZ - GuessCell * 2f;
            guessColumns = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (boundsMaxX - boundsMinX + GuessCell * 4f) / GuessCell));
            guessRows = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (boundsMaxZ - boundsMinZ + GuessCell * 4f) / GuessCell));
            guesses = new int[guessColumns * guessRows];
            for (int row = 0; row < guessRows; row++)
            {
                for (int column = 0; column < guessColumns; column++)
                {
                    var center = new Vector2(
                        guessMinX + (column + 0.5f) * GuessCell,
                        guessMinZ + (row + 0.5f) * GuessCell);
                    guesses[row * guessColumns + column] =
                        NearestSegmentTo(points, center);
                }
            }
        }

        internal int ChunkCount { get; }

        /// <summary>
        /// The segment nearest the centre of the query's grid cell, or `-1`
        /// when the query is not a number and no cell can be named.
        /// </summary>
        internal int GuessSegment(Vector2 point)
        {
            if (disabled || float.IsNaN(point.x) || float.IsNaN(point.y))
            {
                return -1;
            }

            int column = (int)Mathf.Clamp(
                (point.x - guessMinX) / GuessCell,
                0f,
                guessColumns - 1);
            int row = (int)Mathf.Clamp(
                (point.y - guessMinZ) / GuessCell,
                0f,
                guessRows - 1);
            return guesses[row * guessColumns + column];
        }

        /// <summary>
        /// True when no segment of the chunk can lie within `best` of the
        /// point, by the distance to the chunk's enclosing circle. With
        /// `best` infinite nothing is ever pruned, and a NaN query fails
        /// every comparison and is likewise never pruned.
        /// </summary>
        internal bool ChunkCannotWin(int chunk, Vector2 point, float best)
        {
            if (disabled)
            {
                return false;
            }

            float dx = point.x - chunkX[chunk];
            float dz = point.y - chunkZ[chunk];
            float limit = best + chunkReach[chunk];
            return dx * dx + dz * dz > limit * limit;
        }

        /// <summary>
        /// True when the segment cannot lie within `best` of the point: the
        /// distance to its midpoint less half its length is a lower bound
        /// on the distance to any point of it.
        /// </summary>
        internal bool CannotWin(int segment, Vector2 point, float best)
        {
            if (disabled)
            {
                return false;
            }

            float dx = point.x - midX[segment];
            float dz = point.y - midZ[segment];
            float limit = best + reach[segment];
            return dx * dx + dz * dz > limit * limit;
        }

        /// <summary>
        /// True when every segment is provably farther from the point than
        /// `distance`, by the polyline's bounding rectangle and then by each
        /// chunk's circle. A NaN query is never "farther", and neither is
        /// anything against an infinite or undefined `distance`: the caller
        /// then runs the full arithmetic and produces whatever it always
        /// did.
        /// </summary>
        internal bool FartherThan(Vector2 point, float distance)
        {
            if (disabled ||
                float.IsNaN(point.x) ||
                float.IsNaN(point.y) ||
                !(distance < float.PositiveInfinity))
            {
                return false;
            }

            float outsideX = Mathf.Max(
                0f,
                Mathf.Max(boundsMinX - point.x, point.x - boundsMaxX));
            float outsideZ = Mathf.Max(
                0f,
                Mathf.Max(boundsMinZ - point.y, point.y - boundsMaxZ));
            if (outsideX * outsideX + outsideZ * outsideZ >
                distance * distance)
            {
                return true;
            }

            for (int chunk = 0; chunk < ChunkCount; chunk++)
            {
                float dx = point.x - chunkX[chunk];
                float dz = point.y - chunkZ[chunk];
                float limit = distance + chunkReach[chunk];
                if (dx * dx + dz * dz <= limit * limit)
                {
                    return false;
                }
            }

            return true;
        }

        private static int NearestSegmentTo(Vector2[] points, Vector2 point)
        {
            int nearest = 0;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector2 a = points[index];
                Vector2 segment = points[index + 1] - a;
                float lengthSquared = segment.sqrMagnitude;
                float amount = lengthSquared <= 0.000001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - a, segment) / lengthSquared);
                float distance = (point - (a + segment * amount)).magnitude;
                if (distance < nearestDistance)
                {
                    nearest = index;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }
    }
}
