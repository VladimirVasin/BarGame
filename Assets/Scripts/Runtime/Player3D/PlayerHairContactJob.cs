using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerHairContactJob : IJob
    {
        public struct Garment { public Matrix4x4 ToUnit, ToWorld; public int Flat; }
        public struct TailTriangle { public Vector3 A, B, C, Minimum, Maximum; }
        public NativeArray<Vector3> Points, Samples, Originals;
        [ReadOnly] public NativeArray<Garment> Garments;
        [ReadOnly] public NativeArray<TailTriangle> Tail;
        public PlayerJacketSurfaceJob Body;
        public int ContactCount, ManagedExecution;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerHairContactJob job) => job.Execute();

        public void Execute()
        {
            RecordManagedExecution(ref ManagedExecution);
            ContactCount = 0;
            for (int i = 0; i < Points.Length; i++)
            {
                int chainPoint = i % PlayerHair.PointsPerChain;
                if (chainPoint == 0) continue;
                Vector3 corrected = ResolveHairPoint(Points[i], chainPoint, i);
                if (Square(corrected - Points[i]) > .00000001f) ContactCount++;
                Points[i] = corrected;
            }
            for (int i = 1; i < Points.Length; i++)
                for (int j = i + 1; j < Points.Length; j++)
                {
                    if (i / PlayerHair.PointsPerChain == j / PlayerHair.PointsPerChain ||
                        i % PlayerHair.PointsPerChain == 0 || j % PlayerHair.PointsPerChain == 0) continue;
                    float separation = Radius(i % PlayerHair.PointsPerChain) + Radius(j % PlayerHair.PointsPerChain);
                    Vector3 difference = Points[i] - Points[j];
                    float distance = Magnitude(difference);
                    if (distance >= separation) continue;
                    Vector3 correction = (distance > .00001f ? Divide(difference, distance) : Vector3.right) *
                        (float)(((double)separation - distance) * .5f);
                    Points[i] += correction; Points[j] -= correction; ContactCount++;
                }
        }

        private Vector3 ResolveHairPoint(Vector3 point, int chainPoint, int location)
        {
            float radius = Radius(chainPoint);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int sample = 0; sample < 7; sample++)
                {
                    Vector3 direction = sample == 0 ? Vector3.zero : sample == 1 ? Vector3.right : sample == 2 ? Vector3.left :
                        sample == 3 ? Vector3.up : sample == 4 ? Vector3.down : sample == 5 ? Vector3.forward : Vector3.back;
                    Vector3 value = point + direction * radius;
                    Originals[sample] = value;
                    Body.ResolveSingle(ref value, location * 7 + sample);
                    Samples[sample] = value;
                }
                for (int g = 0; g < Garments.Length; g++)
                {
                    Garment garment = Garments[g];
                    for (int sample = 0; sample < 7; sample++)
                    {
                        Vector3 unit = PlayerJacketSurfaceJob.TransformPoint(garment.ToUnit, Samples[sample]);
                        if (garment.Flat != 0)
                        {
                            Vector3 absolute = new Vector3(Math.Abs(unit.x), Math.Abs(unit.y), Math.Abs(unit.z));
                            if (Math.Max(absolute.x, Math.Max(absolute.y, absolute.z)) >= 1f) continue;
                            int axis = absolute.x >= absolute.y && absolute.x >= absolute.z ? 0 : absolute.y >= absolute.z ? 1 : 2;
                            unit[axis] = unit[axis] >= 0f ? 1.0001f : -1.0001f;
                            Samples[sample] = PlayerJacketSurfaceJob.TransformPoint(garment.ToWorld, unit);
                        }
                        else
                        {
                            if (Square(unit) >= 1f) continue;
                            unit = Square(unit) > .00000001f ? Normalize(unit) : Vector3.back;
                            Samples[sample] = PlayerJacketSurfaceJob.TransformPoint(garment.ToWorld, unit * 1.0001f);
                        }
                    }
                }
                Vector3 correction = Vector3.zero;
                for (int sample = 0; sample < 7; sample++)
                {
                    Vector3 candidate = Samples[sample] - Originals[sample];
                    if (Square(candidate) > Square(correction)) correction = candidate;
                }
                point += correction;
                point = ResolveTail(point, (float)((double)radius + .002f));
                if (Square(correction) < .00000001f) break;
            }
            return point;
        }

        private Vector3 ResolveTail(Vector3 point, float radius)
        {
            for (int i = 0; i < Tail.Length; i++)
            {
                TailTriangle t = Tail[i];
                if (!(point.x >= t.Minimum.x && point.x <= t.Maximum.x && point.y >= t.Minimum.y &&
                    point.y <= t.Maximum.y && point.z >= t.Minimum.z && point.z <= t.Maximum.z)) continue;
                Vector3 closest = ClosestPoint(point, t.A, t.B, t.C), offset = point - closest;
                float square = Square(offset);
                if (square >= (double)radius * radius) continue;
                Vector3 normal = square > .00000001f ? Divide(offset, (float)Math.Sqrt(square)) : Normalize(Cross(t.B - t.A, t.C - t.A));
                point = closest + normal * radius;
            }
            return point;
        }

        private static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Dot(ab, ap), d2 = Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Dot(ab, bp), d4 = Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = (float)((double)d1 * d4 - (double)d3 * d2);
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (float)((double)d1 / ((double)d1 - d3));
            Vector3 cp = p - c;
            float d5 = Dot(ab, cp), d6 = Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = (float)((double)d5 * d2 - (double)d1 * d6);
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (float)((double)d2 / ((double)d2 - d6));
            float va = (float)((double)d3 * d6 - (double)d5 * d4);
            if (va <= 0f && (double)d4 - d3 >= 0d && (double)d5 - d6 >= 0d)
                return b + (c - b) * (float)(((double)d4 - d3) / (((double)d4 - d3) + ((double)d5 - d6)));
            float denominator = (float)(((double)va + vb) + vc);
            if (Math.Abs(denominator) < 1e-16f) return a;
            return a + ab * (float)((double)vb / denominator) + ac * (float)((double)vc / denominator);
        }

        private static float Radius(int point) => point == 1 ? .014f : point == 2 ? .010f : .005f;
        private static float Dot(Vector3 a, Vector3 b) => (float)(((double)a.x * b.x + (double)a.y * b.y) + (double)a.z * b.z);
        private static float Square(Vector3 v) => Dot(v, v);
        private static float Magnitude(Vector3 v) => (float)Math.Sqrt(((double)v.x * v.x + (double)v.y * v.y) + (double)v.z * v.z);
        private static Vector3 Divide(Vector3 v, float s) => new Vector3((float)((double)v.x / s), (float)((double)v.y / s), (float)((double)v.z / s));
        private static Vector3 Normalize(Vector3 v) { float length = Magnitude(v); return length > .00001f ? Divide(v, length) : Vector3.zero; }
        private static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3((float)((double)a.y * b.z - (double)a.z * b.y),
            (float)((double)a.z * b.x - (double)a.x * b.z), (float)((double)a.x * b.y - (double)a.y * b.x));
        [BurstDiscard] private static void RecordManagedExecution(ref int value) => value = 1;
    }
}
