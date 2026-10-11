using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // World planes are already canonical managed values. Only their search
    // bounds are refitted here; preorder topology permits a reverse pass.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodyPlaneRefitJob : IJob
    {
        private const double DoubleError = 16d / 9007199254740992d;
        [ReadOnly] public NativeArray<Plane> Planes;
        [ReadOnly] public NativeArray<int> Order;
        public NativeArray<PlayerBodyRayJob.Node> Nodes;
        public Vector3 Origin;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodyPlaneRefitJob job) => job.Execute();

        public void Execute()
        {
            for (int index = Nodes.Length - 1; index >= 0; index--)
            {
                PlayerBodyRayJob.Node node = Nodes[index];
                if (node.Left >= 0)
                {
                    PlayerBodyRayJob.Node left = Nodes[node.Left], right = Nodes[node.Right];
                    node.Minimum = Minimum(left.Minimum, right.Minimum);
                    node.Maximum = Maximum(left.Maximum, right.Maximum);
                    node.Absolute = Maximum(left.Absolute, right.Absolute);
                    node.MaximumCentredOffset = Math.Max(left.MaximumCentredOffset, right.MaximumCentredOffset);
                    node.MaximumAbsoluteDistance = Math.Max(left.MaximumAbsoluteDistance, right.MaximumAbsoluteDistance);
                }
                else
                {
                    node.Minimum = node.Maximum = Planes[Order[node.First]].normal;
                    node.MaximumCentredOffset = double.NegativeInfinity;
                    node.MaximumAbsoluteDistance = 0d;
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        Plane plane = Planes[Order[i]];
                        Vector3 normal = plane.normal;
                        node.Minimum = Minimum(node.Minimum, normal);
                        node.Maximum = Maximum(node.Maximum, normal);
                        double a = (double)normal.x * Origin.x, b = (double)normal.y * Origin.y, c = (double)normal.z * Origin.z;
                        double error = DoubleError * (Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(plane.distance));
                        node.MaximumCentredOffset = Math.Max(node.MaximumCentredOffset, a + b + c + plane.distance + error);
                        node.MaximumAbsoluteDistance = Math.Max(node.MaximumAbsoluteDistance, Math.Abs((double)plane.distance));
                    }
                    node.Absolute = new Vector3(
                        Maximum(Mathf.Abs(node.Minimum.x), Mathf.Abs(node.Maximum.x)),
                        Maximum(Mathf.Abs(node.Minimum.y), Mathf.Abs(node.Maximum.y)),
                        Maximum(Mathf.Abs(node.Minimum.z), Mathf.Abs(node.Maximum.z)));
                }
                Nodes[index] = node;
            }
        }

        // Match Mathf's second-operand tie choice, including signed zero.
        private static float Minimum(float a, float b) => a < b ? a : b;
        private static float Maximum(float a, float b) => a > b ? a : b;
        private static Vector3 Minimum(Vector3 a, Vector3 b) => new Vector3(
            Minimum(a.x, b.x), Minimum(a.y, b.y), Minimum(a.z, b.z));
        private static Vector3 Maximum(Vector3 a, Vector3 b) => new Vector3(
            Maximum(a.x, b.x), Maximum(a.y, b.y), Maximum(a.z, b.z));
    }
}
