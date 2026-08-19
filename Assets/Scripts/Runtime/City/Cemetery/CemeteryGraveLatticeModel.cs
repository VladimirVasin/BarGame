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
    /// both acts — though it meets nothing but the loose spoil it came
    /// off, so only the digging has ground worth varying.
    ///
    /// The model is a pure function of its seed: one number is one
    /// arrangement of ground, every time, which is what makes it
    /// testable. What varies is who supplies the number. The work
    /// re-rolls it on every attempt, so two goes at the same hole never
    /// meet the same stone in the same corner.
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

        /// <summary>
        /// Keeps the ground apart from anything else drawn out of the
        /// same seed — the sway on the ropes, the lean of the stone.
        /// </summary>
        private const uint SoilSalt = 0x51F0D9A3u;

        private readonly CemeterySoilKind[] soil;
        private readonly int[] coursesDone;
        private readonly CemeteryGraveLatticeMode mode;

        /// <summary>
        /// Lays the ground out for one attempt at one hole. The seed
        /// is the only entropy, and the caller owns where it comes
        /// from: a fixed number here is a fixed grave, which is what
        /// every test wants, and a fresh one is fresh ground, which is
        /// what the hero gets.
        /// </summary>
        public CemeteryGraveLatticeModel(
            int seed,
            CemeteryGraveLatticeMode latticeMode)
        {
            mode = latticeMode;
            soil = new CemeterySoilKind[TotalCourses];
            coursesDone = new int[SegmentCount];
            for (int segment = 0;
                 segment < SegmentCount;
                 segment++)
            {
                for (int course = 0;
                     course < CoursesPerSegment;
                     course++)
                {
                    soil[(segment * CoursesPerSegment) + course] =
                        ResolveSoil(
                            seed,
                            segment,
                            course,
                            latticeMode);
                }
            }
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
        /// The ground this segment would meet next. A finished segment
        /// reports the last course it went through, because there is
        /// nothing left to meet.
        /// </summary>
        public CemeterySoilKind GetSoil(int segment)
        {
            RequireSegment(segment);
            int course = Math.Min(
                coursesDone[segment],
                CoursesPerSegment - 1);
            return soil[(segment * CoursesPerSegment) + course];
        }

        /// <summary>The ground at one exact course, for tests and for
        /// the map the view draws.</summary>
        public CemeterySoilKind GetSoilAt(int segment, int course)
        {
            RequireSegment(segment);
            if (course < 0 || course >= CoursesPerSegment)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(course));
            }

            return soil[(segment * CoursesPerSegment) + course];
        }

        public CemeterySoilProfile GetProfile(int segment)
        {
            return CemeterySoilTable.Get(GetSoil(segment));
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
        /// One good strike is one course, whatever the ground. Asking
        /// for a second on the same square is the same shot demanded
        /// twice; what makes hard ground hard is the width of the
        /// window it gives, and that is stated in the profile.
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

        /// <summary>
        /// The sod is always the lid, and there is only ever one hard
        /// course in a segment — a grave that is stone all the way
        /// down is a grave nobody finishes. Filling meets nothing but
        /// the heap it came off.
        /// </summary>
        private static CemeterySoilKind ResolveSoil(
            int seed,
            int segment,
            int course,
            CemeteryGraveLatticeMode latticeMode)
        {
            if (latticeMode == CemeteryGraveLatticeMode.Filling)
            {
                return CemeterySoilKind.Spoil;
            }

            if (course == 0)
            {
                return CemeterySoilKind.Turf;
            }

            uint roll = Mix(
                unchecked((uint)seed),
                (uint)((segment * CoursesPerSegment) + course)) %
                100u;
            if (roll < 52u)
            {
                return CemeterySoilKind.Loam;
            }

            if (roll < 78u)
            {
                return CemeterySoilKind.Clay;
            }

            return roll < 90u
                ? CemeterySoilKind.Root
                : CemeterySoilKind.Stone;
        }

        /// <summary>
        /// The seed and the course, stirred hard enough that adjacent
        /// courses of the same seed are unrelated. Neighbouring slots
        /// differ by one, and a weak mix would lay stone in stripes.
        /// </summary>
        private static uint Mix(uint seed, uint slot)
        {
            unchecked
            {
                uint hash = seed ^ SoilSalt;
                hash ^= (slot + 0x9E3779B9u) + (hash << 6) +
                        (hash >> 2);
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return hash;
            }
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
