using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Sse = Unity.Burst.Intrinsics.X86.Sse;

namespace BarPromenade
{
    // Calibrated arithmetic can clip ordered planes directly. The conservative
    // candidate route remains available; uncertain arithmetic falls back to Mono.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodyRayJob : IJob
    {
        public const int CandidateCapacity = 32;
        public const int StackCapacity = 64;
        public const int CertifiedMiss = -2;
        public const int DirectInterval = -4;
        private const double FloatError = 8d / 16777216d;
        private const double DoubleError = 16d / 9007199254740992d;
        private const double TinyError = 1e-44d;
        private const float FilterError = 32f / 16777216f;
        private const float ScoreError = 64f / 16777216f;
        private const float FilterTiny = 1e-35f;
        private const int NonFiniteCandidates = -3;
        // Half the overflow threshold leaves ample rounding margin. Every
        // nonparallel slope is >=1e-7, so these distances divide finitely.
        private const float FiniteDivisionDistance = (float)((double)float.MaxValue * .0000001f * .5d);

        public struct Range { public int First, Count; }
        public struct PointPlane { public float Distance, AbsoluteDistance; }
        public struct PlanePack { public v128 X, Y, Z, Distance; }
        public struct PointPack { public v128 Distance, AbsoluteDistance; }
        public struct Interval { public float Entry, Exit; public bool Hit; }
        public struct DirectResult { public float Entry, Exit; public int Hit; }
        public struct Tree { public int First, Count, OrderFirst; public Vector3 Origin; }
        public struct Node
        {
            public int First, Count, Left, Right;
            public Vector3 Minimum, Maximum, Absolute;
            public double MaximumCentredOffset, MaximumAbsoluteDistance;
        }
        public struct Work { public int Node; public double Entry, Exit; public bool Mandatory; }
        private struct Query { public double X, Y, Z, ErrorX, ErrorY, ErrorZ; }
        private struct RayBound { public double Entry, Exit; public bool Mandatory; }
        public struct Crossing
        {
            public double Minimum, Maximum;
            // Zero ignores a harmless parallel face; two is mandatory; three
            // certifies a parallel face with the origin strictly outside it.
            public int Kind;
        }

        [ReadOnly] public NativeArray<Plane> Planes;
        [ReadOnly] public NativeArray<Range> Ranges;
        [ReadOnly] public NativeArray<bool> Eligible;
        [ReadOnly] public NativeArray<bool> Inside;
        public uint EligibleMask, InsideMask;
        [MarshalAs(UnmanagedType.U1)] public bool UseMasks;
        [ReadOnly] public NativeArray<Tree> Trees;
        [ReadOnly] public NativeArray<Node> Nodes;
        [ReadOnly] public NativeArray<int> Order;
        public NativeArray<Crossing> Crossings;
        public NativeArray<int> CandidateIndices, CandidateCounts;
        public NativeArray<Work> Stack;
        public NativeArray<double> MaximumDistances;
        public NativeArray<uint> DistanceVersions;
        public NativeArray<PointPlane> PointPlanes;
        public NativeArray<uint> ProxyPointVersions;
        [ReadOnly] public NativeArray<Range> PackRanges;
        [ReadOnly] public NativeArray<PlanePack> PlanePacks;
        public NativeArray<PointPack> PointPacks;
        public NativeArray<uint> PackPointVersions;
        public NativeArray<float> ExactPointDistances;
        public NativeArray<uint> ExactPointVersions;
        public NativeArray<DirectResult> DirectResults;
        public uint PointVersion;
        public Vector3 Point, Direction;
        public int ArithmeticMode;
        public int ManagedExecution;
        public int UsedSimd;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodyRayJob job) => job.Execute();

        public void Execute()
        {
            RecordManagedExecution(ref ManagedExecution);
            for (int proxy = 0; proxy < Ranges.Length; proxy++)
            {
                CandidateCounts[proxy] = 0;
                bool eligible = UseMasks ? (EligibleMask & (1u << proxy)) != 0 : Eligible[proxy];
                if (!eligible) continue;
                Range range = Ranges[proxy];
                if (ArithmeticMode != 0)
                {
                    CandidateCounts[proxy] = ArithmeticMode > 0 && ClipExact(proxy, range)
                        ? DirectInterval : -1;
                    continue;
                }
                if (Trees[proxy].Count > 0)
                {
                    bool inside = UseMasks ? (InsideMask & (1u << proxy)) != 0 : Inside[proxy];
                    if (Sse.IsSseSupported)
                    {
                        UsedSimd = 1;
                        CandidateCounts[proxy] = CollectFour(proxy, range, Trees[proxy], inside);
                    }
                    else CandidateCounts[proxy] = CollectFast(proxy, range, Trees[proxy], inside);
                    continue;
                }
                double entryLower = double.NegativeInfinity, exitUpper = double.PositiveInfinity;
                bool miss = false;
                for (int i = range.First; i < range.First + range.Count; i++)
                {
                    Crossing crossing = Measure(Planes[i]);
                    Crossings[i] = crossing;
                    if (crossing.Kind == 3) { miss = true; break; }
                    if (crossing.Kind == 1) exitUpper = Math.Min(exitUpper, crossing.Maximum);
                    else if (crossing.Kind == -1) entryLower = Math.Max(entryLower, crossing.Minimum);
                    // These bounds enclose the original Mono crossings. A
                    // strict conflict proves a miss without scanning any of
                    // the remaining faces; unused miss intervals have no bits
                    // to preserve.
                    if (entryLower > exitUpper || exitUpper <= 0d) { miss = true; break; }
                }
                if (miss) { CandidateCounts[proxy] = CertifiedMiss; continue; }
                int count = 0, first = proxy * CandidateCapacity;
                for (int i = range.First; i < range.First + range.Count; i++)
                {
                    Crossing crossing = Crossings[i];
                    bool possible = crossing.Kind == 2 ||
                        crossing.Kind == 1 && crossing.Minimum <= exitUpper ||
                        crossing.Kind == -1 && crossing.Maximum >= entryLower;
                    if (!possible) continue;
                    if (count == CandidateCapacity) { count = -1; break; }
                    // Natural face order preserves the old clip's ordering.
                    CandidateIndices[first + count++] = i - range.First;
                }
                CandidateCounts[proxy] = count;
            }
        }

        [BurstDiscard]
        private static void RecordManagedExecution(ref int value) => value = 1;

        private bool ClipExact(int proxy, Range range)
        {
            if (ExactPointVersions[proxy] != PointVersion)
            {
                for (int i = range.First; i < range.First + range.Count; i++)
                {
                    Plane plane = Planes[i];
                    float dot = ExactDot(plane.normal, Point);
                    float distance = (float)((double)dot + plane.distance);
                    if (!Finite(dot) || !Finite(distance)) return false;
                    ExactPointDistances[i] = distance;
                }
                ExactPointVersions[proxy] = PointVersion;
            }
            float entry = float.NegativeInfinity, exit = float.PositiveInfinity;
            for (int i = range.First; i < range.First + range.Count; i++)
            {
                float distance = ExactPointDistances[i];
                float slope = ExactDot(Planes[i].normal, Direction);
                if (!Finite(slope)) return false;
                if (Mathf.Abs(slope) < .0000001f)
                {
                    if (distance > 0f)
                    {
                        DirectResults[proxy] = new DirectResult { Entry = entry, Exit = exit };
                        return true;
                    }
                    continue;
                }
                float incumbent = slope > 0f ? exit : entry;
                if (Finite(incumbent) && incumbent != 0f && Mathf.Abs(distance) <= FiniteDivisionDistance &&
                    (double)distance + (double)slope * incumbent <= 0d)
                {
                    // The product of two floats is exact in double. Adding a
                    // float cannot reverse its sign: the least nonzero exact
                    // result is far above double underflow. A nonpositive
                    // score therefore proves that the continuous crossing
                    // cannot improve this bound. Float rounding may only tie
                    // the same nonzero incumbent bits. Zero still divides so
                    // the original ordered signed-zero equality is retained.
                    continue;
                }
                float crossing = (float)(-(double)distance / slope);
                if (!Finite(crossing)) return false;
                // Match Mathf's ordered ternaries, including equal signed
                // zero: the newly visited crossing wins an equality.
                if (slope > 0f) exit = exit < crossing ? exit : crossing;
                else entry = entry > crossing ? entry : crossing;
                if (entry > exit)
                {
                    DirectResults[proxy] = new DirectResult { Entry = entry, Exit = exit };
                    return true;
                }
            }
            DirectResults[proxy] = new DirectResult { Entry = entry, Exit = exit, Hit = exit > 0f ? 1 : 0 };
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float ExactDot(Vector3 normal, Vector3 value)
        {
            if (ArithmeticMode == 1)
                return (float)(((double)normal.x * value.x + (double)normal.y * value.y) + (double)normal.z * value.z);
            float x = (float)((double)normal.x * value.x);
            float y = (float)((double)normal.y * value.y);
            float z = (float)((double)normal.z * value.z);
            float xy = (float)((double)x + y);
            return (float)((double)xy + z);
        }

        private int CollectFast(int proxy, Range range, Tree tree, bool inside)
        {
            const float threshold = .0000001f;
            double entryLower = double.NegativeInfinity, exitUpper = double.PositiveInfinity;
            if (!CachePointData(proxy, range)) return CollectTree(proxy, range, tree, !inside);
            float approximateEntry = float.NegativeInfinity, approximateExit = float.PositiveInfinity;
            int entryFace = -1, exitFace = -1;
            // Native floats nominate witnesses from cached point distances.
            // Compare at the incumbent before dividing; most faces need only
            // a direction dot and a score.
            for (int i = 0; i < range.Count; i++)
            {
                Plane plane = Planes[range.First + i];
                float distance = PointPlanes[range.First + i].Distance;
                float slope = Vector3.Dot(plane.normal, Direction);
                if (!Finite(slope)) return CollectTree(proxy, range, tree, !inside);
                if (slope >= threshold && (exitFace < 0 || distance + slope * approximateExit > 0f))
                { approximateExit = -distance / slope; exitFace = i; }
                else if (slope <= -threshold && (entryFace < 0 || distance + slope * approximateEntry > 0f))
                { approximateEntry = -distance / slope; entryFace = i; }
            }

            if (entryFace >= 0 && !IncludeWitness(Measure(Planes[range.First + entryFace]), ref entryLower, ref exitUpper)) return CertifiedMiss;
            if (exitFace >= 0 && !IncludeWitness(Measure(Planes[range.First + exitFace]), ref entryLower, ref exitUpper)) return CertifiedMiss;
            if (!CompleteWitnesses(range, ref entryLower, ref exitUpper)) return CertifiedMiss;
            if (entryLower > exitUpper || exitUpper <= 0d) return CertifiedMiss;
            if (double.IsNaN(entryLower) || double.IsNaN(exitUpper) || double.IsInfinity(entryLower) || double.IsInfinity(exitUpper))
                return CollectTree(proxy, range, tree, !inside);

            // The witness windows enclose original float crossings. Expand
            // another 32 unit roundoffs before casting: after cast rounding,
            // these times still enclose the unrounded original divisions.
            float entryTime = (float)(entryLower - Math.Abs(entryLower) * FilterError - FilterTiny);
            float exitTime = (float)(exitUpper + Math.Abs(exitUpper) * FilterError + FilterTiny);
            if (!Finite(entryTime) || !Finite(exitTime)) return CollectTree(proxy, range, tree, !inside);
            Vector3 absoluteDirection = Absolute(Direction);
            int first = proxy * CandidateCapacity, count = 0;
            for (int i = 0; i < range.Count; i++)
            {
                Plane plane = Planes[range.First + i];
                PointPlane pointPlane = PointPlanes[range.First + i];
                Vector3 normal = plane.normal, absoluteNormal = Absolute(normal);
                float slope = Vector3.Dot(normal, Direction);
                float absoluteSlope = Vector3.Dot(absoluteNormal, absoluteDirection);
                float slopeError = absoluteSlope * FilterError + FilterTiny;
                bool positive = slope - slopeError >= threshold, negative = slope + slopeError <= -threshold;
                bool parallel = Mathf.Abs(slope) + slopeError < threshold;
                if (parallel)
                {
                    if (inside || pointPlane.Distance + pointPlane.AbsoluteDistance * ScoreError + FilterTiny < 0f) continue;
                    Crossing parallelCrossing = Measure(plane);
                    if (parallelCrossing.Kind == 3) return CertifiedMiss;
                    if (parallelCrossing.Kind == 0) continue;
                    if (count == CandidateCapacity) return -1;
                    CandidateIndices[first + count++] = i;
                    continue;
                }
                if (!positive && !negative)
                {
                    // Preserve every threshold/sign-ambiguous face for the
                    // original Mono clip, irrespective of its native score.
                    if (count == CandidateCapacity) return -1;
                    CandidateIndices[first + count++] = i;
                    continue;
                }
                float time = positive ? exitTime : entryTime;
                float score = pointPlane.Distance + slope * time;
                // 64u covers both native/Mono dots, distance addition, the
                // final multiply/add and rounding of the absolute sum itself.
                float error = (pointPlane.AbsoluteDistance + Mathf.Abs(time) * absoluteSlope) * ScoreError + FilterTiny;
                if (!Finite(score) || !Finite(error)) return CollectTree(proxy, range, tree, !inside);
                if (score < -error) continue;
                Crossing crossing = Measure(plane);
                if (crossing.Kind == 3) return CertifiedMiss;
                bool possible = crossing.Kind == 2 ||
                    crossing.Kind == 1 && crossing.Minimum <= exitUpper ||
                    crossing.Kind == -1 && crossing.Maximum >= entryLower;
                if (!possible) continue;
                if (count == CandidateCapacity) return -1;
                CandidateIndices[first + count++] = i;
            }
            return count;
        }

        private bool CachePointData(int proxy, Range range)
        {
            // A scope fixes every plane. These approximate values are reusable
            // for an identical point; Measure and final Mono clipping always
            // evaluate the actual point independently.
            if (ProxyPointVersions[proxy] == PointVersion) return true;
            Vector3 absolutePoint = Absolute(Point);
            for (int i = range.First; i < range.First + range.Count; i++)
            {
                Plane plane = Planes[i];
                float distance = Vector3.Dot(plane.normal, Point) + plane.distance;
                float absolute = Vector3.Dot(Absolute(plane.normal), absolutePoint) + Mathf.Abs(plane.distance);
                if (!Finite(distance) || !Finite(absolute)) return false;
                PointPlanes[i] = new PointPlane { Distance = distance, AbsoluteDistance = absolute };
            }
            ProxyPointVersions[proxy] = PointVersion;
            return true;
        }

        private static Vector3 Absolute(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IncludeWitness(Crossing crossing, ref double entryLower, ref double exitUpper)
        {
            if (crossing.Kind == 3) return false;
            if (crossing.Kind == 1) exitUpper = Math.Min(exitUpper, crossing.Maximum);
            else if (crossing.Kind == -1) entryLower = Math.Max(entryLower, crossing.Minimum);
            return true;
        }

        private bool CompleteWitnesses(Range range, ref double entryLower, ref double exitUpper)
        {
            // Two nominated faces usually establish finite rigorous windows.
            // Axis witnesses are needed only if a nomination is ambiguous.
            if (!double.IsNaN(entryLower) && !double.IsNaN(exitUpper) &&
                !double.IsInfinity(entryLower) && !double.IsInfinity(exitUpper)) return true;
            for (int i = 0; i < Math.Min(6, range.Count); i++)
                if (!IncludeWitness(Measure(Planes[range.First + i]), ref entryLower, ref exitUpper) ||
                    entryLower > exitUpper || exitUpper <= 0d) return false;
            return true;
        }

        private int CollectFour(int proxy, Range range, Tree tree, bool inside)
        {
            const float threshold = .0000001f;
            double entryLower = double.NegativeInfinity, exitUpper = double.PositiveInfinity;
            if (!CachePointFour(proxy)) return CollectTree(proxy, range, tree, !inside);
            Range packs = PackRanges[proxy];
            float approximateEntry = float.NegativeInfinity, approximateExit = float.PositiveInfinity;
            int entryFace = -1, exitFace = -1;
            for (int group = 0; group < packs.Count; group++)
            {
                int packIndex = packs.First + group;
                v128 slopes = DotFour(PlanePacks[packIndex], Direction);
                v128 distances = PointPacks[packIndex].Distance;
                int count = Math.Min(4, range.Count - group * 4);
                for (int lane = 0; lane < count; lane++)
                {
                    float distance = Lane(distances, lane), slope = Lane(slopes, lane);
                    if (!Finite(slope)) return CollectTree(proxy, range, tree, !inside);
                    int index = group * 4 + lane;
                    if (slope >= threshold && (exitFace < 0 || distance + slope * approximateExit > 0f))
                    { approximateExit = -distance / slope; exitFace = index; }
                    else if (slope <= -threshold && (entryFace < 0 || distance + slope * approximateEntry > 0f))
                    { approximateEntry = -distance / slope; entryFace = index; }
                }
            }
            if (entryFace >= 0 && !IncludeWitness(Measure(Planes[range.First + entryFace]), ref entryLower, ref exitUpper)) return CertifiedMiss;
            if (exitFace >= 0 && !IncludeWitness(Measure(Planes[range.First + exitFace]), ref entryLower, ref exitUpper)) return CertifiedMiss;
            if (!CompleteWitnesses(range, ref entryLower, ref exitUpper)) return CertifiedMiss;
            if (entryLower > exitUpper || exitUpper <= 0d) return CertifiedMiss;
            if (double.IsNaN(entryLower) || double.IsNaN(exitUpper) || double.IsInfinity(entryLower) || double.IsInfinity(exitUpper))
                return CollectTree(proxy, range, tree, !inside);
            float entryTime = (float)(entryLower - Math.Abs(entryLower) * FilterError - FilterTiny);
            float exitTime = (float)(exitUpper + Math.Abs(exitUpper) * FilterError + FilterTiny);
            if (!Finite(entryTime) || !Finite(exitTime)) return CollectTree(proxy, range, tree, !inside);
            Vector3 absoluteDirection = Absolute(Direction);
            var filter = new v128(FilterError);
            var scoreGuard = new v128(ScoreError);
            var tiny = new v128(FilterTiny);
            int selected = 0;
            for (int group = 0; group < packs.Count; group++)
            {
                int packIndex = packs.First + group;
                PlanePack plane = PlanePacks[packIndex];
                PointPack point = PointPacks[packIndex];
                v128 slopes = DotFour(plane, Direction);
                v128 absoluteSlopes = DotAbsoluteFour(plane, absoluteDirection);
                v128 slopeErrors = Sse.add_ps(Sse.mul_ps(absoluteSlopes, filter), tiny);
                v128 distanceUpper = Sse.add_ps(Sse.add_ps(point.Distance, Sse.mul_ps(point.AbsoluteDistance, scoreGuard)), tiny);
                v128 exitScores = Sse.add_ps(point.Distance, Sse.mul_ps(slopes, new v128(exitTime)));
                v128 entryScores = Sse.add_ps(point.Distance, Sse.mul_ps(slopes, new v128(entryTime)));
                v128 exitErrors = Sse.add_ps(Sse.mul_ps(Sse.add_ps(point.AbsoluteDistance,
                    Sse.mul_ps(absoluteSlopes, new v128(Mathf.Abs(exitTime)))), scoreGuard), tiny);
                v128 entryErrors = Sse.add_ps(Sse.mul_ps(Sse.add_ps(point.AbsoluteDistance,
                    Sse.mul_ps(absoluteSlopes, new v128(Mathf.Abs(entryTime)))), scoreGuard), tiny);
                int count = Math.Min(4, range.Count - group * 4);
                int remaining = ((1 << count) - 1) & ~RejectedFour(inside, slopes, slopeErrors, distanceUpper,
                    exitScores, entryScores, exitErrors, entryErrors);
                // Almost every face is certainly below the incumbent. Keep
                // those comparisons packed; extract scalar lanes only for
                // uncertain faces, in their original ascending order.
                while (remaining != 0)
                {
                    int lane = (remaining & 1) != 0 ? 0 : (remaining & 2) != 0 ? 1 : (remaining & 4) != 0 ? 2 : 3;
                    remaining &= remaining - 1;
                    selected = FilterFourLane(proxy, range, inside, group * 4 + lane,
                        Lane(slopes, lane), Lane(slopeErrors, lane), Lane(distanceUpper, lane),
                        Lane(exitScores, lane), Lane(entryScores, lane), Lane(exitErrors, lane), Lane(entryErrors, lane),
                        entryLower, exitUpper, selected);
                    if (selected < 0) return selected == NonFiniteCandidates ? CollectTree(proxy, range, tree, !inside) : selected;
                }
            }
            return selected;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int RejectedFour(bool inside, v128 slopes, v128 slopeErrors, v128 distanceUpper,
            v128 exitScores, v128 entryScores, v128 exitErrors, v128 entryErrors)
        {
            var threshold = new v128(.0000001f);
            var zero = new v128(0f);
            var maximum = new v128(float.MaxValue);
            var sign = new v128(int.MinValue);
            v128 positive = Sse.cmpge_ps(Sse.sub_ps(slopes, slopeErrors), threshold);
            v128 negative = Sse.cmple_ps(Sse.add_ps(slopes, slopeErrors), new v128(-.0000001f));
            v128 parallel = Sse.cmplt_ps(Sse.add_ps(Absolute(slopes), slopeErrors), threshold);
            v128 rejectedParallel = inside ? parallel : Sse.and_ps(parallel, Sse.cmplt_ps(distanceUpper, zero));
            // Ordered finite comparisons leave NaN/Infinity lanes for the
            // original scalar fallback instead of making a new certificate.
            v128 finiteExit = Sse.and_ps(Sse.cmple_ps(Absolute(exitScores), maximum), Sse.cmple_ps(Absolute(exitErrors), maximum));
            v128 finiteEntry = Sse.and_ps(Sse.cmple_ps(Absolute(entryScores), maximum), Sse.cmple_ps(Absolute(entryErrors), maximum));
            v128 rejectedExit = Sse.and_ps(Sse.and_ps(positive, finiteExit),
                Sse.cmplt_ps(exitScores, Sse.xor_ps(exitErrors, sign)));
            v128 rejectedEntry = Sse.and_ps(Sse.and_ps(Sse.andnot_ps(positive, negative), finiteEntry),
                Sse.cmplt_ps(entryScores, Sse.xor_ps(entryErrors, sign)));
            // Parallel has precedence in FilterFourLane. Every other reject
            // matches its reliably-signed, finite score test exactly.
            v128 rejectedSigned = Sse.andnot_ps(parallel, Sse.or_ps(rejectedExit, rejectedEntry));
            return Sse.movemask_ps(Sse.or_ps(rejectedParallel, rejectedSigned));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FilterFourLane(int proxy, Range range, bool inside, int index, float slope, float slopeError,
            float distanceUpper, float exitScore, float entryScore, float exitError, float entryError,
            double entryLower, double exitUpper, int count)
        {
            const float threshold = .0000001f;
            bool positive = slope - slopeError >= threshold, negative = slope + slopeError <= -threshold;
            if (Mathf.Abs(slope) + slopeError < threshold)
            {
                if (inside || distanceUpper < 0f) return count;
                Crossing crossing = Measure(Planes[range.First + index]);
                if (crossing.Kind == 3) return CertifiedMiss;
                if (crossing.Kind == 0) return count;
            }
            else if (positive || negative)
            {
                float score = positive ? exitScore : entryScore, error = positive ? exitError : entryError;
                if (!Finite(score) || !Finite(error)) return NonFiniteCandidates;
                if (score < -error) return count;
                Crossing crossing = Measure(Planes[range.First + index]);
                if (crossing.Kind == 3) return CertifiedMiss;
                bool possible = crossing.Kind == 2 ||
                    crossing.Kind == 1 && crossing.Minimum <= exitUpper ||
                    crossing.Kind == -1 && crossing.Maximum >= entryLower;
                if (!possible) return count;
            }
            // Threshold-ambiguous lanes remain mandatory. Serial lane order
            // retains the original face ordering for exact Mono verification.
            if (count == CandidateCapacity) return -1;
            CandidateIndices[proxy * CandidateCapacity + count] = index;
            return count + 1;
        }

        private bool CachePointFour(int proxy)
        {
            if (PackPointVersions[proxy] == PointVersion) return true;
            Range range = PackRanges[proxy];
            Vector3 absolutePoint = Absolute(Point);
            for (int i = range.First; i < range.First + range.Count; i++)
            {
                PlanePack plane = PlanePacks[i];
                v128 distance = Sse.add_ps(DotFour(plane, Point), plane.Distance);
                v128 absolute = Sse.add_ps(DotAbsoluteFour(plane, absolutePoint), Absolute(plane.Distance));
                if (!Finite(distance) || !Finite(absolute)) return false;
                PointPacks[i] = new PointPack { Distance = distance, AbsoluteDistance = absolute };
            }
            PackPointVersions[proxy] = PointVersion;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static v128 DotFour(PlanePack plane, Vector3 value)
            => Sse.add_ps(Sse.add_ps(Sse.mul_ps(plane.X, new v128(value.x)), Sse.mul_ps(plane.Y, new v128(value.y))),
                Sse.mul_ps(plane.Z, new v128(value.z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static v128 DotAbsoluteFour(PlanePack plane, Vector3 value)
            => Sse.add_ps(Sse.add_ps(Sse.mul_ps(Absolute(plane.X), new v128(value.x)), Sse.mul_ps(Absolute(plane.Y), new v128(value.y))),
                Sse.mul_ps(Absolute(plane.Z), new v128(value.z)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static v128 Absolute(v128 value) => Sse.and_ps(value, new v128(int.MaxValue));
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Finite(v128 value) => Finite(value.Float0) && Finite(value.Float1) && Finite(value.Float2) && Finite(value.Float3);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Lane(v128 value, int lane) => lane == 0 ? value.Float0 : lane == 1 ? value.Float1 : lane == 2 ? value.Float2 : value.Float3;

        private int CollectTree(int proxy, Range range, Tree tree, bool outside)
        {
            Query query = At(tree.Origin);
            double entryLower = double.NegativeInfinity, exitUpper = double.PositiveInfinity;
            // Every authored support hull starts with its six bone-local axis
            // faces. Their crossings give finite incumbent bounds cheaply.
            for (int i = 0; i < Math.Min(6, range.Count); i++)
            {
                Crossing crossing = Measure(Planes[range.First + i]);
                if (crossing.Kind == 3) return CertifiedMiss;
                if (crossing.Kind == 1) exitUpper = Math.Min(exitUpper, crossing.Maximum);
                else if (crossing.Kind == -1) entryLower = Math.Max(entryLower, crossing.Minimum);
                if (entryLower > exitUpper || exitUpper <= 0d) return CertifiedMiss;
            }
            // Find conservative thresholds first. Equal bounds need not tighten
            // them; the second walk includes equality to retain every Mono tie.
            int pending = 1;
            RayBound rootBound = Bound(tree.First, query, outside);
            Stack[0] = new Work { Node = 0, Entry = rootBound.Entry, Exit = rootBound.Exit, Mandatory = rootBound.Mandatory };
            while (pending > 0)
            {
                Work work = Stack[--pending];
                if (!work.Mandatory && (double.IsPositiveInfinity(work.Exit) || work.Exit >= exitUpper) &&
                    (double.IsNegativeInfinity(work.Entry) || work.Entry <= entryLower)) continue;
                Node node = Nodes[tree.First + work.Node];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int plane = range.First + Order[tree.OrderFirst + i];
                        Crossing crossing = Measure(Planes[plane]);
                        if (crossing.Kind == 3) return CertifiedMiss;
                        if (crossing.Kind == 1) exitUpper = Math.Min(exitUpper, crossing.Maximum);
                        else if (crossing.Kind == -1) entryLower = Math.Max(entryLower, crossing.Minimum);
                        if (entryLower > exitUpper || exitUpper <= 0d) return CertifiedMiss;
                    }
                }
                else if (!PushChildren(tree, node, query, outside, ref pending)) return -1;
            }
            int count = 0, first = proxy * CandidateCapacity;
            pending = 1; Stack[0] = new Work { Node = 0, Entry = rootBound.Entry, Exit = rootBound.Exit, Mandatory = rootBound.Mandatory };
            while (pending > 0)
            {
                Work work = Stack[--pending];
                if (!work.Mandatory && (double.IsPositiveInfinity(work.Exit) || work.Exit > exitUpper) &&
                    (double.IsNegativeInfinity(work.Entry) || work.Entry < entryLower)) continue;
                Node node = Nodes[tree.First + work.Node];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int index = Order[tree.OrderFirst + i];
                        Crossing crossing = Measure(Planes[range.First + index]);
                        if (crossing.Kind == 3) return CertifiedMiss;
                        bool possible = crossing.Kind == 2 ||
                            crossing.Kind == 1 && crossing.Minimum <= exitUpper ||
                            crossing.Kind == -1 && crossing.Maximum >= entryLower;
                        if (!possible) continue;
                        if (count == CandidateCapacity) return -1;
                        CandidateIndices[first + count++] = index;
                    }
                }
                else if (!PushChildren(tree, node, query, outside, ref pending)) return -1;
            }
            // Mono visits the retained faces in their original order, including
            // signed-zero ties and any defensive ambiguous-plane rejection.
            for (int i = 1; i < count; i++)
            {
                int index = CandidateIndices[first + i], j = i;
                while (j > 0 && CandidateIndices[first + j - 1] > index)
                { CandidateIndices[first + j] = CandidateIndices[first + j - 1]; j--; }
                CandidateIndices[first + j] = index;
            }
            return count;
        }

        private bool PushChildren(Tree tree, Node node, Query query, bool outside, ref int pending)
        {
            if (pending > Stack.Length - 2) return false;
            RayBound left = Bound(tree.First + node.Left, query, outside), right = Bound(tree.First + node.Right, query, outside);
            double leftFirst = Math.Min(left.Exit, -left.Entry), rightFirst = Math.Min(right.Exit, -right.Entry);
            // LIFO: the closest possible crossing improves thresholds first.
            Work leftWork = new Work { Node = node.Left, Entry = left.Entry, Exit = left.Exit, Mandatory = left.Mandatory };
            Work rightWork = new Work { Node = node.Right, Entry = right.Entry, Exit = right.Exit, Mandatory = right.Mandatory };
            Stack[pending++] = leftFirst <= rightFirst ? rightWork : leftWork;
            Stack[pending++] = leftFirst <= rightFirst ? leftWork : rightWork;
            return true;
        }

        private Query At(Vector3 origin)
        {
            double x = (double)Point.x - origin.x, y = (double)Point.y - origin.y, z = (double)Point.z - origin.z;
            return new Query
            {
                X = x, Y = y, Z = z,
                ErrorX = FloatError * Math.Abs((double)Point.x) + DoubleError * (Math.Abs(x) + Math.Abs((double)Point.x) + Math.Abs((double)origin.x)),
                ErrorY = FloatError * Math.Abs((double)Point.y) + DoubleError * (Math.Abs(y) + Math.Abs((double)Point.y) + Math.Abs((double)origin.y)),
                ErrorZ = FloatError * Math.Abs((double)Point.z) + DoubleError * (Math.Abs(z) + Math.Abs((double)Point.z) + Math.Abs((double)origin.z))
            };
        }

        private RayBound Bound(int nodeIndex, Query query, bool outside)
        {
            Node node = Nodes[nodeIndex];
            double maximumDistance;
            if (DistanceVersions[nodeIndex] == PointVersion) maximumDistance = MaximumDistances[nodeIndex];
            else
            {
                double a = (query.X >= 0d ? node.Maximum.x : node.Minimum.x) * query.X;
                double b = (query.Y >= 0d ? node.Maximum.y : node.Minimum.y) * query.Y;
                double c = (query.Z >= 0d ? node.Maximum.z : node.Minimum.z) * query.Z;
                maximumDistance = a + b + c + node.MaximumCentredOffset +
                    node.Absolute.x * query.ErrorX + node.Absolute.y * query.ErrorY + node.Absolute.z * query.ErrorZ +
                    (FloatError + DoubleError) * node.MaximumAbsoluteDistance +
                    DoubleError * (Math.Abs(node.MaximumCentredOffset) + Math.Abs(a) + Math.Abs(b) + Math.Abs(c)) + TinyError;
                MaximumDistances[nodeIndex] = maximumDistance;
                DistanceVersions[nodeIndex] = PointVersion;
            }
            double maximumSlope = (double)(Direction.x >= 0f ? node.Maximum.x : node.Minimum.x) * Direction.x +
                (double)(Direction.y >= 0f ? node.Maximum.y : node.Minimum.y) * Direction.y +
                (double)(Direction.z >= 0f ? node.Maximum.z : node.Minimum.z) * Direction.z;
            double minimumSlope = (double)(Direction.x >= 0f ? node.Minimum.x : node.Maximum.x) * Direction.x +
                (double)(Direction.y >= 0f ? node.Minimum.y : node.Maximum.y) * Direction.y +
                (double)(Direction.z >= 0f ? node.Minimum.z : node.Maximum.z) * Direction.z;
            double slopeError = ((double)node.Absolute.x * Math.Abs((double)Direction.x) +
                (double)node.Absolute.y * Math.Abs((double)Direction.y) + (double)node.Absolute.z * Math.Abs((double)Direction.z)) *
                (FloatError + DoubleError) + TinyError;
            maximumSlope += slopeError; minimumSlope -= slopeError;
            bool positive = maximumSlope >= (double).0000001f, negative = minimumSlope <= (double)-.0000001f;
            var bound = new RayBound { Exit = positive ? 0d : double.PositiveInfinity, Entry = negative ? 0d : double.NegativeInfinity };
            if (outside)
            {
                // For a positive slope, -maxDistance/s is smallest at the
                // smallest slope when maxDistance >= 0, and at the largest
                // slope otherwise. Negative slopes have the symmetric upper
                // bound. Round outward to include the original float divide.
                const double threshold = (double).0000001f;
                if (positive)
                {
                    double divisor = maximumDistance >= 0d ? Math.Max(threshold, minimumSlope) : maximumSlope;
                    double crossing = -maximumDistance / divisor;
                    bound.Exit = crossing - Math.Abs(crossing) * FloatError - DoubleError * (Math.Abs(crossing) + 1d) - TinyError;
                }
                if (negative)
                {
                    double divisor = maximumDistance >= 0d ? Math.Min(-threshold, maximumSlope) : minimumSlope;
                    double crossing = -maximumDistance / divisor;
                    bound.Entry = crossing + Math.Abs(crossing) * FloatError + DoubleError * (Math.Abs(crossing) + 1d) + TinyError;
                }
                // A parallel positive-distance face can reject the ray even
                // when it cannot improve either crossing. Its node must reach
                // the original leaf classification instead of being pruned.
                bound.Mandatory = maximumDistance >= 0d && minimumSlope < threshold && maximumSlope > -threshold;
            }
            else if (maximumDistance < 0d)
            {
                if (positive)
                {
                    double crossing = -maximumDistance / maximumSlope;
                    bound.Exit = crossing * (1d - FloatError) - DoubleError * (Math.Abs(crossing) + 1d) - TinyError;
                }
                if (negative)
                {
                    double crossing = -maximumDistance / minimumSlope;
                    bound.Entry = crossing * (1d - FloatError) + DoubleError * (Math.Abs(crossing) + 1d) + TinyError;
                }
            }
            return bound;
        }

        private Crossing Measure(Plane plane)
        {
            Vector3 normal = plane.normal;
            double ax = (double)normal.x * Point.x, ay = (double)normal.y * Point.y, az = (double)normal.z * Point.z;
            double distance = ax + ay + az + plane.distance;
            double distanceError = (Math.Abs(ax) + Math.Abs(ay) + Math.Abs(az) + Math.Abs((double)plane.distance)) *
                (FloatError + DoubleError) + TinyError;
            double sx = (double)normal.x * Direction.x, sy = (double)normal.y * Direction.y, sz = (double)normal.z * Direction.z;
            double slope = sx + sy + sz;
            double slopeError = (Math.Abs(sx) + Math.Abs(sy) + Math.Abs(sz)) * (FloatError + DoubleError) + TinyError;
            double slopeMinimum = slope - slopeError, slopeMaximum = slope + slopeError;
            double threshold = (double).0000001f;
            bool positive = slopeMinimum >= threshold, negative = slopeMaximum <= -threshold;
            if (!positive && !negative)
            {
                bool parallel = slopeMinimum > -threshold && slopeMaximum < threshold;
                return new Crossing { Kind = parallel && distance + distanceError < 0d ? 0 :
                    parallel && distance - distanceError > 0d ? 3 : 2 };
            }
            // Sign and zero ambiguity can change parallel rejection, ray hit,
            // or the final positive-exit check. Keep every such plane in Mono.
            if (Math.Abs(distance) <= distanceError) return new Crossing { Kind = 2 };
            double value = -distance / slope;
            double uncertainty = (distanceError + Math.Abs(value) * slopeError) / (Math.Abs(slope) - slopeError);
            // This bounds operand uncertainty plus the rounded original float
            // division. Additional double room protects bound construction.
            double error = uncertainty + (Math.Abs(value) + uncertainty) * FloatError +
                (Math.Abs(value) + uncertainty + 1d) * DoubleError + TinyError;
            if (double.IsNaN(value) || double.IsInfinity(value) || double.IsNaN(error) || double.IsInfinity(error))
                return new Crossing { Kind = 2 };
            return new Crossing { Kind = positive ? 1 : -1, Minimum = value - error, Maximum = value + error };
        }
    }
}
