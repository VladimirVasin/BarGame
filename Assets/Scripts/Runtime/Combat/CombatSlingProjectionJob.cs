using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Contacts and convergence remain managed. This direct synchronous kernel
    // preserves the ordered pin/material/link projections between contacts.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct CombatSlingProjectionJob : IJob
    {
        public struct Parameters
        {
            public int StartIteration, IterationCount, ShoulderPoint, HandPoint;
            public float Rest;
            public Vector3 PinnedFrom, PinnedTo, PinnedShoulder, PinnedHand, FromToAxis, BodyForward;
            [MarshalAs(UnmanagedType.U1)] public bool PinShoulder;
            [MarshalAs(UnmanagedType.U1)] public bool PinHand;
        }

        public NativeArray<Vector3> Points;
        [ReadOnly] public NativeArray<float> InverseMass;
        public Parameters Settings;
        public int ManagedExecution;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.High, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref CombatSlingProjectionJob job) => job.Execute();

        public void Execute()
        {
            RecordManagedExecution(ref ManagedExecution);
            int links = Points.Length - 1;
            for (int iteration = Settings.StartIteration; iteration < Settings.StartIteration + Settings.IterationCount; iteration++)
            {
                Points[0] = Settings.PinnedFrom;
                Points[links] = Settings.PinnedTo;
                if (Settings.PinShoulder) Points[Settings.ShoulderPoint] = Settings.PinnedShoulder;
                if (Settings.PinHand) Points[Settings.HandPoint] = Settings.PinnedHand;
                bool reverse = (iteration & 1) != 0;
                LimitMaterialDistances(links, reverse);
                for (int link = 0; link < links; link++)
                {
                    int a = reverse ? links - 1 - link : link, b = a + 1;
                    Vector3 delta = Subtract(Points[b], Points[a]);
                    float length = Magnitude(delta), mass = InverseMass[a] + InverseMass[b];
                    if (mass <= 0f) continue;
                    Vector3 correction;
                    if (length <= .000001f)
                    {
                        Vector3 axis = a > 0 ? Subtract(Points[a], Points[a - 1]) : Settings.FromToAxis;
                        if (SquaredMagnitude(axis) < .000001f) axis = Settings.BodyForward;
                        correction = Multiply(-Normalize(axis), (float)((double)Settings.Rest / mass));
                    }
                    else correction = Multiply(delta, CorrectionFactor(length, Settings.Rest, mass));
                    Points[a] = Add(Points[a], Multiply(correction, InverseMass[a]));
                    Points[b] = Subtract(Points[b], Multiply(correction, InverseMass[b]));
                }
            }
        }

        private void LimitMaterialDistances(int links, bool reverse)
        {
            int first = 0;
            for (int last = 1; last <= links; last++)
            {
                if (InverseMass[last] != 0f) continue;
                for (int offset = 1; offset < last - first; offset++)
                {
                    int point = reverse ? last - offset : first + offset;
                    if (reverse)
                    {
                        Limit(point, last, (last - point) * Settings.Rest);
                        Limit(point, first, (point - first) * Settings.Rest);
                    }
                    else
                    {
                        Limit(point, first, (point - first) * Settings.Rest);
                        Limit(point, last, (last - point) * Settings.Rest);
                    }
                }
                first = last;
            }
        }

        private void Limit(int point, int pin, float maximum)
        {
            Vector3 delta = Subtract(Points[point], Points[pin]);
            if ((double)SquaredMagnitude(delta) <= (double)maximum * maximum) return;
            Points[point] = Add(Points[pin], Multiply(Normalize(delta), maximum));
        }

        private static float SquaredMagnitude(Vector3 value) => (float)DoubleSquaredMagnitude(value);

        // The production Mono JIT keeps scalar expression intermediates in
        // double. Vector3.sqrMagnitude rounds at its return, while magnitude
        // carries the complete dot into Math.Sqrt before returning a float.
        private static float Magnitude(Vector3 value) => (float)DoubleSquareRoot(DoubleSquaredMagnitude(value));

        private static double DoubleSquaredMagnitude(Vector3 value) =>
            ((double)value.x * value.x + (double)value.y * value.y) + (double)value.z * value.z;

        private static float CorrectionFactor(float length, float rest, float mass) =>
            (float)(((double)length - rest) / ((double)length * mass));

        private static double DoubleSquareRoot(double value) => Math.Sqrt(value);

        private static Vector3 Normalize(Vector3 value)
        {
            float length = Magnitude(value);
            return length > .00001f ? Divide(value, length) : Vector3.zero;
        }

        // Each Vector3 operator returns rounded float components; strict
        // compilation preserves those boundaries and scalar expression order.
        private static Vector3 Add(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        private static Vector3 Subtract(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        private static Vector3 Multiply(Vector3 value, float scale) => new Vector3(value.x * scale, value.y * scale, value.z * scale);
        private static Vector3 Divide(Vector3 value, float scale) => new Vector3(
            (float)((double)value.x / scale), (float)((double)value.y / scale), (float)((double)value.z / scale));

        [BurstDiscard]
        private static void RecordManagedExecution(ref int executed) => executed = 1;
    }
}
