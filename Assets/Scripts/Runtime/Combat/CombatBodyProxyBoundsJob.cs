using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Consume the current BakeMesh output in the original triangle-corner order.
    // The calibrated Mono profile keeps each matrix dot wide until its return;
    // every Vector3 operation and Bounds field write remains a float boundary.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct CombatBodyProxyBoundsJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> Vertices;
        [ReadOnly] public NativeArray<int> Indices;
        public NativeArray<Vector3> Result;
        public Matrix4x4 Matrix;
        public Vector3 MinimumSize;
        public int VertexCount, IndexCount, Completed, ExecutedInBurst;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref CombatBodyProxyBoundsJob job) => job.Execute();

        public void Execute()
        {
            Completed = 0;
            ExecutedInBurst = 1;
            MarkManaged(ref ExecutedInBurst);
            if (IndexCount <= 0 || !Finite(Matrix) || !Finite(MinimumSize)) return;
            int first = Indices[0];
            if ((uint)first >= (uint)VertexCount) return;
            Vector3 centre = MultiplyPoint(Matrix, Vertices[first]);
            Vector3 extents = Vector3.zero;
            if (!Finite(centre)) return;
            // The first corner is intentionally included again: the original
            // Bounds initialization is followed by foreach over every index.
            for (int i = 0; i < IndexCount; i++)
            {
                int index = Indices[i];
                if ((uint)index >= (uint)VertexCount) return;
                Vector3 point = MultiplyPoint(Matrix, Vertices[index]);
                if (!Finite(point)) return;
                Vector3 minimum = Min(Subtract(centre, extents), point);
                Vector3 maximum = Max(Add(centre, extents), point);
                extents = Multiply(Subtract(maximum, minimum), .5f);
                centre = Add(minimum, extents);
                if (!Finite(centre) || !Finite(extents)) return;
            }
            Vector3 size = Max(Multiply(extents, 2f), MinimumSize);
            if (!Finite(size)) return;
            Result[0] = centre;
            Result[1] = size;
            Completed = 1;
        }

        [BurstDiscard] private static void MarkManaged(ref int value) => value = 0;
        private static Vector3 MultiplyPoint(Matrix4x4 m, Vector3 v) => new Vector3(
            (float)((((double)m.m00 * v.x + (double)m.m01 * v.y) + (double)m.m02 * v.z) + m.m03),
            (float)((((double)m.m10 * v.x + (double)m.m11 * v.y) + (double)m.m12 * v.z) + m.m13),
            (float)((((double)m.m20 * v.x + (double)m.m21 * v.y) + (double)m.m22 * v.z) + m.m23));
        private static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        private static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        private static Vector3 Multiply(Vector3 value, float amount) => new Vector3(value.x * amount, value.y * amount, value.z * amount);
        private static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(
            a.x < b.x ? a.x : b.x, a.y < b.y ? a.y : b.y, a.z < b.z ? a.z : b.z);
        private static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(
            a.x > b.x ? a.x : b.x, a.y > b.y ? a.y : b.y, a.z > b.z ? a.z : b.z);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Finite(Matrix4x4 m) =>
            Finite(m.m00) && Finite(m.m01) && Finite(m.m02) && Finite(m.m03) &&
            Finite(m.m10) && Finite(m.m11) && Finite(m.m12) && Finite(m.m13) &&
            Finite(m.m20) && Finite(m.m21) && Finite(m.m22) && Finite(m.m23) &&
            Finite(m.m30) && Finite(m.m31) && Finite(m.m32) && Finite(m.m33);
    }
}
