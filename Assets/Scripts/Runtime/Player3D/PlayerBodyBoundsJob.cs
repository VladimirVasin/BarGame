using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Match the sequential Bounds.Encapsulate updates, then the original
    // expansion over every point. The owner retains managed TRS and inverse.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodyBoundsJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> Vertices;
        public NativeArray<Vector3> Result;
        public Vector3 Padding;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodyBoundsJob job) => job.Execute();

        public void Execute()
        {
            Vector3 centre = Vertices[0], extents = Vector3.zero;
            for (int i = 1; i < Vertices.Length; i++)
            {
                Vector3 point = Vertices[i];
                Vector3 minimum = Minimum(Subtract(centre, extents), point);
                Vector3 maximum = Maximum(Add(centre, extents), point);
                extents = Multiply(Subtract(maximum, minimum), .5f);
                centre = Add(minimum, extents);
            }
            Vector3 radii = Add(extents, Padding);
            float expansion = 1f;
            for (int i = 0; i < Vertices.Length; i++)
            {
                Vector3 offset = Subtract(Vertices[i], centre);
                var unit = new Vector3((float)((double)offset.x / radii.x),
                    (float)((double)offset.y / radii.y), (float)((double)offset.z / radii.z));
                // The supported Mono JIT retains these scalar intermediates
                // through Math.Sqrt; Vector3.magnitude rounds at its return.
                double squared = ((double)unit.x * unit.x + (double)unit.y * unit.y) + (double)unit.z * unit.z;
                float magnitude = (float)Math.Sqrt(squared);
                expansion = expansion > magnitude ? expansion : magnitude;
            }
            Result[0] = centre;
            Result[1] = Multiply(radii, expansion);
        }

        private static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        private static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        private static Vector3 Multiply(Vector3 value, float scale) => new Vector3(value.x * scale, value.y * scale, value.z * scale);
        private static Vector3 Minimum(Vector3 a, Vector3 b) => new Vector3(
            a.x < b.x ? a.x : b.x, a.y < b.y ? a.y : b.y, a.z < b.z ? a.z : b.z);
        private static Vector3 Maximum(Vector3 a, Vector3 b) => new Vector3(
            a.x > b.x ? a.x : b.x, a.y > b.y ? a.y : b.y, a.z > b.z ? a.z : b.z);
    }
}
