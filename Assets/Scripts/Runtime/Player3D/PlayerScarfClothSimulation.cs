using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Small world-space cloth solver over the unchanged authored scarf topology.</summary>
    public sealed class PlayerScarfClothSimulation : IDisposable
    {
        private readonly int[] triangles;
        private readonly Vector3[] framePrevious, localOutput;
        private NativeArray<Vector3> nativeWorld, nativeVelocity, nativePrevious, nativeTargets, nativeRest;
        private NativeArray<float> nativeFreedom;
        private NativeArray<PlayerScarfIntegrationJob.Edge> nativeEdges;
        private bool initialized, resetPending;
        private float clock;
        public Vector3[] RestVertices { get; }
        public Vector3[] WorldVertices { get; }
        public bool IsActive { get; set; }
        public Vector3 ExternalAcceleration { get; set; }
        public Vector3 RandomAcceleration { get; set; }
        public float LastStepMilliseconds { get; private set; }
        public int LastContactCount { get; private set; }
        public int LastContactPassCount { get; private set; }

        public PlayerScarfClothSimulation(Vector3[] restLocal, int[] indices)
        {
            RestVertices = (Vector3[])restLocal.Clone();
            triangles = (int[])indices.Clone();
            int count = restLocal.Length;
            WorldVertices = new Vector3[count];
            framePrevious = new Vector3[count]; localOutput = new Vector3[count];
            var freedom = new float[count];
            for (int i = 0; i < count; i++)
            {
                float row = Mathf.Clamp01((1.567f - restLocal[i].y) / PlayerScarfPresentation.TailLength);
                freedom[i] = row < .035f ? 0f : row;
            }
            var constraints = new List<PlayerScarfIntegrationJob.Edge>();
            var seen = new HashSet<ulong>();
            void Add(int a, int b, float stiffness)
            {
                if (a > b) { int swap = a; a = b; b = swap; }
                ulong key = ((ulong)(uint)a << 32) | (uint)b;
                if (!seen.Add(key)) return;
                float length = Vector3.Distance(restLocal[a], restLocal[b]);
                if (length > .00001f) constraints.Add(new PlayerScarfIntegrationJob.Edge(a, b, length, stiffness));
            }
            for (int i = 0; i < indices.Length; i += 3)
            { Add(indices[i], indices[i + 1], 1f); Add(indices[i + 1], indices[i + 2], 1f); Add(indices[i + 2], indices[i], 1f); }
            // Longer local links supply bending resistance without changing the
            // authored mesh or relying on the importer's vertex ordering.
            for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                    if ((restLocal[a] - restLocal[b]).sqrMagnitude < .052f * .052f) Add(a, b, .22f);
            try
            {
                nativeWorld = new NativeArray<Vector3>(count, Allocator.Persistent);
                nativeVelocity = new NativeArray<Vector3>(count, Allocator.Persistent);
                nativePrevious = new NativeArray<Vector3>(count, Allocator.Persistent);
                nativeTargets = new NativeArray<Vector3>(count, Allocator.Persistent);
                nativeRest = new NativeArray<Vector3>(RestVertices, Allocator.Persistent);
                nativeFreedom = new NativeArray<float>(freedom, Allocator.Persistent);
                nativeEdges = new NativeArray<PlayerScarfIntegrationJob.Edge>(constraints.ToArray(), Allocator.Persistent);
                // Compile during scene construction, before equipping or opening
                // inventory. This operation touches no simulation state.
                CreateJob(PlayerScarfIntegrationJob.Operation.Warmup).Run();
            }
            catch { Dispose(); throw; }
        }

        public void Reset(Transform frame)
        {
            Matrix4x4 toWorld = frame.localToWorldMatrix;
            for (int i = 0; i < RestVertices.Length; i++)
            {
                WorldVertices[i] = toWorld.MultiplyPoint3x4(RestVertices[i]);
                framePrevious[i] = WorldVertices[i]; nativeVelocity[i] = Vector3.zero;
            }
            initialized = resetPending = true;
        }

        public void Step(float dt, Transform frame, PlayerScarfCollisionWorld world)
        {
            if (!IsActive) return;
            long started = Stopwatch.GetTimestamp();
            if (!initialized) Reset(frame);
            if (dt <= 0f && !resetPending) { LastStepMilliseconds = 0f; return; }
            Array.Copy(WorldVertices, framePrevious, WorldVertices.Length);
            Matrix4x4 toWorld = frame.localToWorldMatrix;
            dt = Mathf.Clamp(dt, 0f, .05f);
            clock += dt;
            int substeps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
            float step = dt / substeps;
            float damping = Mathf.Exp(-5.5f * step);
            Vector3 gust = new Vector3(Mathf.Sin(clock * 3.7f), Mathf.Sin(clock * 4.1f + .8f), Mathf.Sin(clock * 3.1f + 2f));
            Vector3 force = Physics.gravity + ExternalAcceleration + Vector3.Scale(RandomAcceleration, gust);
            NativeArray<Vector3>.Copy(WorldVertices, nativeWorld);
            NativeArray<Vector3>.Copy(framePrevious, nativePrevious);
            NativeArray<Vector3>.Copy(RestVertices, nativeRest);
            PlayerScarfIntegrationJob integration = CreateJob(PlayerScarfIntegrationJob.Operation.Integrate);
            integration.ToWorld = toWorld;
            integration.Substeps = substeps;
            integration.StepSize = step;
            integration.Damping = damping;
            integration.Force = force;
            integration.Run();
            NativeArray<Vector3>.Copy(nativeWorld, WorldVertices);
            LastContactCount = LastContactPassCount = 0;
            // Feed corrected contacts back into both position and velocity. This
            // avoids repeatedly rendering a correction over an intersecting sim.
            for (int pass = 0; pass < 3; pass++)
            {
                if (pass > 0)
                {
                    NativeArray<Vector3>.Copy(WorldVertices, nativeWorld);
                    CreateJob(PlayerScarfIntegrationJob.Operation.Constrain).Run();
                    NativeArray<Vector3>.Copy(nativeWorld, WorldVertices);
                }
                // Intermediate stretch/contact alternations only need a short
                // relaxation. The final solve retains the complete 32-pass
                // separation budget, after the last stretch operation.
                PlayerScarfContactSolver.Resolve(framePrevious, WorldVertices, triangles, world,
                    iterationLimit: pass == 2 ? 32 : 4);
                LastContactCount += PlayerScarfContactSolver.LastContactCount;
                LastContactPassCount += PlayerScarfContactSolver.LastPassCount;
            }
            if (dt > 0f)
            {
                NativeArray<Vector3>.Copy(WorldVertices, nativeWorld);
                PlayerScarfIntegrationJob velocity = CreateJob(PlayerScarfIntegrationJob.Operation.UpdateVelocity);
                velocity.DeltaTime = dt;
                velocity.VelocityDamping = LastContactCount > 0 ? .65f : .98f;
                velocity.Run();
            }
            resetPending = false;
            LastStepMilliseconds = (float)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }

        private PlayerScarfIntegrationJob CreateJob(PlayerScarfIntegrationJob.Operation operation) =>
            new PlayerScarfIntegrationJob
            {
                Rest = nativeRest, Previous = nativePrevious, Freedom = nativeFreedom, Edges = nativeEdges,
                Current = nativeWorld, Velocity = nativeVelocity, Targets = nativeTargets,
                Mode = operation, VertexCount = WorldVertices.Length
            };

        public void WriteToMesh(Mesh mesh, Transform frame)
        {
            if (!initialized) Reset(frame);
            Matrix4x4 toLocal = frame.worldToLocalMatrix;
            for (int i = 0; i < WorldVertices.Length; i++) localOutput[i] = toLocal.MultiplyPoint3x4(WorldVertices[i]);
            mesh.vertices = localOutput;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            IsActive = false;
            if (nativeWorld.IsCreated) nativeWorld.Dispose();
            if (nativeVelocity.IsCreated) nativeVelocity.Dispose();
            if (nativePrevious.IsCreated) nativePrevious.Dispose();
            if (nativeTargets.IsCreated) nativeTargets.Dispose();
            if (nativeRest.IsCreated) nativeRest.Dispose();
            if (nativeFreedom.IsCreated) nativeFreedom.Dispose();
            if (nativeEdges.IsCreated) nativeEdges.Dispose();
        }
    }
}
