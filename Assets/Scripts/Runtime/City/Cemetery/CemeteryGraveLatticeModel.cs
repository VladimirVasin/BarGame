using System;

namespace BarPromenade
{
    /// <summary>Which way the earth is moving.</summary>
    public enum CemeteryGraveLatticeMode
    {
        /// <summary>Courses come out, top down.</summary>
        Digging = 0,

        /// <summary>Courses go back, bottom up, off the heap.
        /// </summary>
        Filling = 1
    }

    /// <summary>
    /// The hole as a gravedigger actually works it: not one lump of
    /// earth that vanishes, but a lattice of segments each taken down
    /// a course at a time.
    ///
    /// One rule holds the whole thing together — a segment may only be
    /// worked while it is no deeper than its shallowest neighbour. It
    /// is what stops the hero sinking one shaft in a corner and
    /// leaving pillars of earth standing between him and the coffin,
    /// and it is the reason the pit is divided at all. The rule can
    /// never deadlock: whichever segment is shallowest in the whole
    /// lattice always satisfies it.
    ///
    /// Filling is the same lattice read upward, so one model serves
    /// both acts.
    ///
    /// It carries no notion of what the earth is made of. It did once
    /// — turf, loam, clay, stone and root, each with its own window on
    /// a timing bar — but both spade acts are now a choice of square
    /// and a press, and a kind of ground that changes nothing and is
    /// shown nowhere is not detail, it is weight.
    /// </summary>
    public sealed class CemeteryGraveLatticeModel
    {
        /// <summary>
        /// Segments along the grave and across it. Three by two over a
        /// `2.30 x 1.05` mouth gives cells of about `0.77 x 0.53` — a
        /// spade's bite, near enough, and a lattice small enough to
        /// read at a glance from the rim.
        /// </summary>
        public const int SegmentsAlong = 3;
        public const int SegmentsAcross = 2;
        public const int SegmentCount =
            SegmentsAlong * SegmentsAcross;

        /// <summary>Courses per segment. Three of them share the
        /// `1.60 m` of depth at a little over half a metre each.
        /// </summary>
        public const int CoursesPerSegment = 3;

        public const int TotalCourses =
            SegmentCount * CoursesPerSegment;

        private readonly int[] coursesDone;
        private readonly CemeteryGraveLatticeMode mode;

        /// <summary>Lays out one hole's worth of work.</summary>
        public CemeteryGraveLatticeModel(
            CemeteryGraveLatticeMode latticeMode)
        {
            mode = latticeMode;
            coursesDone = new int[SegmentCount];
        }

        public CemeteryGraveLatticeMode Mode => mode;

        /// <summary>Courses finished across the whole lattice.
        /// </summary>
        public int CompletedCourses { get; private set; }

        public bool IsComplete => CompletedCourses >= TotalCourses;

        public float Progress01 =>
            CompletedCourses / (float)TotalCourses;

        /// <summary>How many courses of this segment are done. In
        /// digging that is how deep it is; in filling, how high.
        /// </summary>
        public int GetCoursesDone(int segment)
        {
            RequireSegment(segment);
            return coursesDone[segment];
        }

        /// <summary>
        /// True while this segment is the hero's to work: unfinished,
        /// and not already ahead of the shallowest ground beside it.
        /// </summary>
        public bool IsWorkable(int segment)
        {
            RequireSegment(segment);
            if (coursesDone[segment] >= CoursesPerSegment)
            {
                return false;
            }

            return coursesDone[segment] <=
                   GetShallowestNeighbour(segment);
        }

        /// <summary>
        /// The first workable segment at or after a starting point,
        /// wrapping once. The view's cursor uses it so a blocked
        /// segment is never left selected.
        /// </summary>
        public int FindWorkable(int from, int direction)
        {
            int step = direction < 0 ? -1 : 1;
            int start = ClampSegment(from);
            for (int offset = 0; offset < SegmentCount; offset++)
            {
                int candidate =
                    (((start + (step * offset)) % SegmentCount) +
                     SegmentCount) % SegmentCount;
                if (IsWorkable(candidate))
                {
                    return candidate;
                }
            }

            return -1;
        }

        /// <summary>
        /// Lands one stroke on one segment. False when the segment was
        /// not the hero's to work — the caller then simply keeps the
        /// stroke and the ground as they were.
        ///
        /// One press is one course. The difficulty of both spade acts
        /// is the rule above — a segment may not get ahead of its
        /// shallowest neighbour — and not the stroke.
        /// </summary>
        public bool TryStrike(
            int segment,
            CemeteryStrokeOutcome outcome,
            out bool courseCompleted)
        {
            courseCompleted = false;
            RequireSegment(segment);
            if (!IsWorkable(segment))
            {
                return false;
            }

            if (outcome != CemeteryStrokeOutcome.Bite)
            {
                return true;
            }

            coursesDone[segment]++;
            CompletedCourses++;
            courseCompleted = true;
            return true;
        }

        /// <summary>
        /// The shallowest orthogonal neighbour. A corner segment has
        /// two neighbours and an edge one has three; the lattice is
        /// too small for any of them to have four.
        /// </summary>
        private int GetShallowestNeighbour(int segment)
        {
            int along = segment / SegmentsAcross;
            int across = segment % SegmentsAcross;
            int shallowest = int.MaxValue;
            shallowest = Math.Min(
                shallowest,
                Sample(along - 1, across));
            shallowest = Math.Min(
                shallowest,
                Sample(along + 1, across));
            shallowest = Math.Min(
                shallowest,
                Sample(along, across - 1));
            shallowest = Math.Min(
                shallowest,
                Sample(along, across + 1));
            return shallowest == int.MaxValue
                ? coursesDone[segment]
                : shallowest;
        }

        private int Sample(int along, int across)
        {
            if (along < 0 ||
                along >= SegmentsAlong ||
                across < 0 ||
                across >= SegmentsAcross)
            {
                return int.MaxValue;
            }

            return coursesDone[(along * SegmentsAcross) + across];
        }

        private static int ClampSegment(int segment)
        {
            if (segment < 0)
            {
                return 0;
            }

            return segment >= SegmentCount
                ? SegmentCount - 1
                : segment;
        }

        private static void RequireSegment(int segment)
        {
            if (segment < 0 || segment >= SegmentCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(segment));
            }
        }
    }
}
