using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Only the calibrated wide Mono profile calls this replay. Every vector
    // field store and scalar return remains an explicit float boundary.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodyWorldPlaneJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> Normals;
        [ReadOnly] public NativeArray<float> Offsets;
        public NativeArray<Plane> Planes;
        public Matrix4x4 NormalMatrix, BoneToWorld;
        public float SurfacePadding;
        public int Completed;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodyWorldPlaneJob job) => job.Execute();

        public void Execute()
        {
            Completed = 0;
            if (!Finite(NormalMatrix) || !Finite(BoneToWorld) || !Finite(SurfacePadding)) return;
            for (int i = 0; i < Normals.Length; i++)
            {
                Vector3 source = Normals[i];
                float offset = Offsets[i];
                if (!Finite(source) || !Finite(offset)) return;
                Vector3 transformed = MultiplyVector(NormalMatrix, source);
                if (!Finite(transformed)) return;
                Vector3 n0 = Normalize(transformed);
                Vector3 n1 = Normalize(n0);
                Vector3 localSupport = Multiply(source, offset);
                Vector3 support = MultiplyPoint(BoneToWorld, localSupport);
                Vector3 shift = Multiply(n0, SurfacePadding);
                Vector3 point = Add(support, shift);
                float distance = -Dot(n1, point);
                if (!Finite(n0) || !Finite(n1) || !Finite(localSupport) ||
                    !Finite(support) || !Finite(shift) || !Finite(point) || !Finite(distance)) return;
                Planes[i] = new Plane { normal = n1, distance = distance };
            }
            Completed = 1;
        }

        private static Vector3 MultiplyVector(Matrix4x4 matrix, Vector3 value) => new Vector3(
            (float)(((double)matrix.m00 * value.x + (double)matrix.m01 * value.y) + (double)matrix.m02 * value.z),
            (float)(((double)matrix.m10 * value.x + (double)matrix.m11 * value.y) + (double)matrix.m12 * value.z),
            (float)(((double)matrix.m20 * value.x + (double)matrix.m21 * value.y) + (double)matrix.m22 * value.z));

        private static Vector3 MultiplyPoint(Matrix4x4 matrix, Vector3 value) => new Vector3(
            (float)((((double)matrix.m00 * value.x + (double)matrix.m01 * value.y) + (double)matrix.m02 * value.z) + matrix.m03),
            (float)((((double)matrix.m10 * value.x + (double)matrix.m11 * value.y) + (double)matrix.m12 * value.z) + matrix.m13),
            (float)((((double)matrix.m20 * value.x + (double)matrix.m21 * value.y) + (double)matrix.m22 * value.z) + matrix.m23));

        private static Vector3 Normalize(Vector3 value)
        {
            double squared = ((double)value.x * value.x + (double)value.y * value.y) + (double)value.z * value.z;
            float magnitude = (float)Math.Sqrt(squared);
            if (!(magnitude > 1e-5f)) return Vector3.zero;
            return new Vector3((float)((double)value.x / magnitude),
                (float)((double)value.y / magnitude), (float)((double)value.z / magnitude));
        }

        private static float Dot(Vector3 a, Vector3 b) =>
            (float)(((double)a.x * b.x + (double)a.y * b.y) + (double)a.z * b.z);

        private static Vector3 Multiply(Vector3 value, float scale) => new Vector3(
            (float)((double)value.x * scale), (float)((double)value.y * scale), (float)((double)value.z * scale));

        private static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(
            (float)((double)a.x + b.x), (float)((double)a.y + b.y), (float)((double)a.z + b.z));

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Matrix4x4 value) =>
            Finite(value.m00) && Finite(value.m01) && Finite(value.m02) && Finite(value.m03) &&
            Finite(value.m10) && Finite(value.m11) && Finite(value.m12) && Finite(value.m13) &&
            Finite(value.m20) && Finite(value.m21) && Finite(value.m22) && Finite(value.m23) &&
            Finite(value.m30) && Finite(value.m31) && Finite(value.m32) && Finite(value.m33);
    }
}
