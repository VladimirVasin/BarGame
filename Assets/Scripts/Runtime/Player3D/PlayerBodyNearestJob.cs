using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Scoped initial containment only. The caller handles broadphase and warm
    // witnesses; exact leaves retain the original nearest-face tie order.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodyNearestJob : IJob
    {
        private const double FloatError = 8d / 16777216d, DoubleError = 16d / 9007199254740992d;
        public struct Seed { public int PlaneIndex; public float Distance; }
        public struct Result { public int Status, PlaneIndex; public float Distance; }
        private struct Query { public double X, Y, Z, ErrorX, ErrorY, ErrorZ; }

        [ReadOnly] public NativeArray<Plane> Planes;
        [ReadOnly] public NativeArray<PlayerBodyRayJob.Range> Ranges;
        [ReadOnly] public NativeArray<PlayerBodyRayJob.Tree> Trees;
        [ReadOnly] public NativeArray<PlayerBodyRayJob.Node> Nodes;
        [ReadOnly] public NativeArray<int> Order;
        [ReadOnly] public NativeArray<Seed> Seeds;
        public NativeArray<Result> Results;
        public NativeArray<PlayerBodyRayJob.Work> Stack;
        public Vector3 Point;
        public uint EligibleMask;
        public int ManagedExecution, ValidateOnly;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodyNearestJob job) => job.Execute();

        public void Execute()
        {
            RecordManagedExecution(ref ManagedExecution);
            for (int proxy = 0; proxy < Ranges.Length; proxy++)
            {
                if ((EligibleMask & (1u << proxy)) == 0) continue;
                Results[proxy] = ValidateOnly != 0
                    ? new Result { Status = ValidProxy(proxy) ? 3 : 0 }
                    : FindNearest(proxy);
            }
        }

        [BurstDiscard]
        private static void RecordManagedExecution(ref int value) => value = 1;

        private bool ValidProxy(int proxy)
        {
            PlayerBodyRayJob.Tree tree = Trees[proxy];
            PlayerBodyRayJob.Range range = Ranges[proxy];
            if (tree.Count == 0 || !Finite(tree.Origin)) return false;
            // Validate once per scope, before bounds can hide a nonfinite face.
            for (int i = range.First; i < range.First + range.Count; i++)
                if (!Finite(Planes[i].normal) || !Finite(Planes[i].distance)) return false;
            for (int i = tree.First; i < tree.First + tree.Count; i++)
            {
                PlayerBodyRayJob.Node node = Nodes[i];
                if (!Finite(node.Minimum) || !Finite(node.Maximum) || !Finite(node.Absolute) ||
                    !Finite(node.MaximumCentredOffset) || !Finite(node.MaximumAbsoluteDistance)) return false;
            }
            return true;
        }

        private Result FindNearest(int proxy)
        {
            PlayerBodyRayJob.Tree tree = Trees[proxy];
            PlayerBodyRayJob.Range range = Ranges[proxy];
            Seed seed = Seeds[proxy];
            if (!Finite(Point) || tree.Count == 0 || Stack.Length == 0 ||
                (uint)seed.PlaneIndex >= range.Count || !Finite(seed.Distance)) return default;
            int winner = seed.PlaneIndex;
            float distance = seed.Distance;
            if (distance >= 0f) return new Result { Status = 1, PlaneIndex = winner, Distance = distance };
            Query query = At(tree.Origin);
            double rootBound = UpperBound(Nodes[tree.First], query);
            if (!Finite(rootBound)) return default;
            int pending = 1;
            Stack[0] = new PlayerBodyRayJob.Work { Node = 0, Entry = rootBound };
            while (pending > 0)
            {
                PlayerBodyRayJob.Work next = Stack[--pending];
                if (next.Entry < distance) continue;
                PlayerBodyRayJob.Node node = Nodes[tree.First + next.Node];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int index = Order[tree.OrderFirst + i];
                        Plane plane = Planes[range.First + index];
                        float dot = (float)(((double)plane.normal.x * Point.x +
                            (double)plane.normal.y * Point.y) + (double)plane.normal.z * Point.z);
                        float score = (float)((double)dot + plane.distance);
                        if (!Finite(dot) || !Finite(score)) return default;
                        if (score >= 0f) return new Result { Status = 1, PlaneIndex = index, Distance = score };
                        if (score > distance || score == distance && index < winner) { winner = index; distance = score; }
                    }
                    continue;
                }
                double left = UpperBound(Nodes[tree.First + node.Left], query);
                double right = UpperBound(Nodes[tree.First + node.Right], query);
                if (!Finite(left) || !Finite(right) || pending > Stack.Length - 2) return default;
                // Reverse pushes reproduce the managed recursive visit order,
                // including its first nonnegative witness when outside.
                if (left >= right)
                {
                    Stack[pending++] = new PlayerBodyRayJob.Work { Node = node.Right, Entry = right };
                    Stack[pending++] = new PlayerBodyRayJob.Work { Node = node.Left, Entry = left };
                }
                else
                {
                    Stack[pending++] = new PlayerBodyRayJob.Work { Node = node.Left, Entry = left };
                    Stack[pending++] = new PlayerBodyRayJob.Work { Node = node.Right, Entry = right };
                }
            }
            return new Result { Status = 2, PlaneIndex = winner, Distance = distance };
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

        private static double UpperBound(PlayerBodyRayJob.Node node, Query query)
        {
            double a = (query.X >= 0d ? node.Maximum.x : node.Minimum.x) * query.X;
            double b = (query.Y >= 0d ? node.Maximum.y : node.Minimum.y) * query.Y;
            double c = (query.Z >= 0d ? node.Maximum.z : node.Minimum.z) * query.Z;
            double error = node.Absolute.x * query.ErrorX + node.Absolute.y * query.ErrorY + node.Absolute.z * query.ErrorZ +
                (FloatError + DoubleError) * node.MaximumAbsoluteDistance + DoubleError * Math.Abs(node.MaximumCentredOffset);
            return a + b + c + node.MaximumCentredOffset + error;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
