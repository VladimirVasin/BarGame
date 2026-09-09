using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Sequential ordering is part of the cloth contract: adjacent edges see
    // each other's corrections immediately, including the final pin clamp.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    internal struct PlayerScarfIntegrationJob : IJob
    {
        internal enum Operation { Warmup, Integrate, Constrain, UpdateVelocity }

        internal readonly struct Edge
        {
            public readonly int A, B;
            public readonly float Length, Stiffness;
            public Edge(int a, int b, float length, float stiffness)
            { A = a; B = b; Length = length; Stiffness = stiffness; }
        }

        [ReadOnly] public NativeArray<Vector3> Rest, Previous;
        [ReadOnly] public NativeArray<float> Freedom;
        [ReadOnly] public NativeArray<Edge> Edges;
        public NativeArray<Vector3> Current, Velocity, Targets;
        public Operation Mode;
        public Matrix4x4 ToWorld;
        public Vector3 Force;
        public int VertexCount, Substeps;
        public float StepSize, Damping, DeltaTime, VelocityDamping;

        public void Execute()
        {
            switch (Mode)
            {
                case Operation.Integrate:
                    for (int i = 0; i < VertexCount; i++) Targets[i] = ToWorld.MultiplyPoint3x4(Rest[i]);
                    for (int substep = 0; substep < Substeps; substep++)
                    {
                        for (int i = 0; i < VertexCount; i++)
                        {
                            if (Freedom[i] == 0f) { Current[i] = Targets[i]; continue; }
                            Velocity[i] = Velocity[i] * Damping + Force * StepSize;
                            Current[i] += Velocity[i] * StepSize;
                        }
                        for (int pass = 0; pass < 6; pass++) Constrain();
                    }
                    break;
                case Operation.Constrain:
                    Constrain();
                    break;
                case Operation.UpdateVelocity:
                    for (int i = 0; i < VertexCount; i++)
                        Velocity[i] = Vector3.ClampMagnitude((Current[i] - Previous[i]) / DeltaTime, 2.5f) * VelocityDamping;
                    break;
            }
        }

        private void Constrain()
        {
            for (int index = 0; index < Edges.Length; index++)
            {
                Edge edge = Edges[index];
                Vector3 separation = Current[edge.B] - Current[edge.A];
                float length = separation.magnitude;
                if (length < .000001f) continue;
                float wa = Freedom[edge.A] == 0f ? 0f : 1f, wb = Freedom[edge.B] == 0f ? 0f : 1f;
                if (wa + wb == 0f) continue;
                Vector3 correction = separation * ((length - edge.Length) / length * edge.Stiffness / (wa + wb));
                Current[edge.A] += correction * wa;
                Current[edge.B] -= correction * wb;
            }
            for (int i = 0; i < VertexCount; i++)
            {
                if (Freedom[i] == 0f) Current[i] = Targets[i];
                else Current[i] = Targets[i] + Vector3.ClampMagnitude(Current[i] - Targets[i], .24f * Freedom[i]);
            }
        }
    }
}
