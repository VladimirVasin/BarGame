using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded wind/motion deformation; detailed cloth is opt-in for legacy diagnostics.</summary>
    public sealed class PlayerScarfClothSimulation : IDisposable
    {
        private readonly int[] triangles;
        private readonly Vector3[] framePrevious, localOutput;
        private NativeArray<Vector3> nativeWorld, nativeVelocity, nativePrevious, nativeTargets, nativeRest;
        private NativeArray<float> nativeFreedom;
        private NativeArray<PlayerScarfIntegrationJob.Edge> nativeEdges;
        private bool initialized, resetPending;
        private float clock;
        private readonly float[] freedom;
        private readonly float backSign;
        private Vector3 previousMotionPosition, filteredMotion, bend;
        private float liftAngle, flutterStrength;
        private readonly bool detailedContacts;
        public Vector3[] RestVertices { get; }
        public Vector3[] WorldVertices { get; }
        public bool IsActive { get; set; }
        public Vector3 ExternalAcceleration { get; set; }
        public Vector3 RandomAcceleration { get; set; }
        public float LastStepMilliseconds { get; private set; }
        public int LastContactCount { get; private set; }
        public int LastContactPassCount { get; private set; }

        public PlayerScarfClothSimulation(Vector3[] restLocal, int[] indices, bool detailedContacts = false)
        {
            this.detailedContacts = detailedContacts;
            RestVertices = (Vector3[])restLocal.Clone();
            triangles = (int[])indices.Clone();
            int count = restLocal.Length;
            WorldVertices = new Vector3[count];
            framePrevious = new Vector3[count]; localOutput = new Vector3[count];
            freedom = new float[count];
            float authoredDepth = 0f;
            for (int i = 0; i < count; i++)
            {
                float row = Mathf.Clamp01((1.567f - restLocal[i].y) / PlayerScarfPresentation.TailLength);
                freedom[i] = row < .035f ? 0f : row;
                authoredDepth += restLocal[i].z;
            }
            // The imported hero's 180-degree facing conversion also rotates
            // this metre-space mesh. Read its authored back, not the FBX axis.
            backSign = authoredDepth < 0f ? -1f : 1f;
            // Production needs neither native buffers nor solver warmup.
            if (!detailedContacts) return;
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
                framePrevious[i] = WorldVertices[i];
                if (nativeVelocity.IsCreated) nativeVelocity[i] = Vector3.zero;
            }
            previousMotionPosition = MotionPosition(frame);
            filteredMotion = bend = Vector3.zero;
            liftAngle = flutterStrength = 0f;
            initialized = resetPending = true;
        }

        public void StepBodyOnly(float dt, Transform frame, PlayerScarfBodyContacts body)
        {
            if (!IsActive) return;
            long started = Stopwatch.GetTimestamp();
            if (!initialized) Reset(frame);
            if (dt <= 0f && !resetPending) { LastStepMilliseconds = 0f; return; }
            Matrix4x4 toWorld = frame.localToWorldMatrix;
            Vector3 motionPosition = MotionPosition(frame);
            if (dt > 0f)
            {
                // Root travel supplies airflow without mistaking idle head
                // animation for running. No catch-up steps or world particles.
                float step = Mathf.Min(dt, .05f);
                clock = (clock + step) % 1000f;
                Vector3 velocity = motionPosition - previousMotionPosition;
                velocity.y = 0f;
                velocity = Vector3.ClampMagnitude(velocity / dt, 6f);
                float follow = 1f - Mathf.Exp(-8f * step);
                filteredMotion = Vector3.Lerp(filteredMotion, velocity, follow);
                Vector3 gust = new Vector3(Mathf.Sin(clock * 3.7f),
                    Mathf.Sin(clock * 4.1f + .8f), Mathf.Sin(clock * 3.1f + 2f));
                Matrix4x4 toLocal = frame.worldToLocalMatrix;
                Vector3 localWind = toLocal.MultiplyVector(
                    ExternalAcceleration + Vector3.Scale(RandomAcceleration, gust));
                Vector3 localMotion = toLocal.MultiplyVector(filteredMotion);
                float running = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(1.4f, Player3DCharacterPresentation.FullRunSpeed, filteredMotion.magnitude));
                float wind = Mathf.Clamp01(ExternalAcceleration.magnitude / 7.5f);
                // A curved 45 cm ribbon lifts about 20 cm at running speed.
                // Ambient wind bends it too, but cannot cancel running lift.
                float targetAngle = running * 1.85f + Mathf.Max(0f, localWind.z * backSign) * .018f;
                float liftFollow = 1f - Mathf.Exp(-(targetAngle > liftAngle ? 5f : 3f) * step);
                liftAngle = Mathf.Lerp(liftAngle, Mathf.Min(targetAngle, 2f), liftFollow);
                flutterStrength = Mathf.Lerp(flutterStrength, running + wind * .25f, follow);
                Vector3 target = new Vector3(
                    Mathf.Clamp(localWind.x * .007f - localMotion.x * .03f, -.06f, .06f),
                    0f, Mathf.Clamp(localWind.z * .004f, -.012f, .03f));
                bend = Vector3.Lerp(bend, target, follow);
            }
            previousMotionPosition = motionPosition;
            for (int i = 0; i < RestVertices.Length; i++)
            {
                float row = freedom[i];
                float weight = row * row;
                Vector3 offset = bend * weight;
                float angle = liftAngle * row;
                if (liftAngle > .001f)
                {
                    // Integrate a circular centreline instead of translating
                    // the tip: rows bend progressively and retain arc length.
                    float radius = PlayerScarfPresentation.TailLength / liftAngle;
                    offset.y = PlayerScarfPresentation.TailLength * row - radius * Mathf.Sin(angle);
                    offset.z += backSign * radius * (1f - Mathf.Cos(angle));
                }
                float phase = clock * 9f - row * 8f;
                float flutter = Mathf.Sin(phase) * flutterStrength * .018f * weight;
                offset.y += flutter * Mathf.Sin(angle);
                offset.z += backSign * flutter * Mathf.Cos(angle);
                offset.x += Mathf.Sin(phase * .73f + .8f) * flutterStrength * .012f * weight;
                WorldVertices[i] = toWorld.MultiplyPoint3x4(RestVertices[i] + offset);
            }
            body.Resolve(WorldVertices, freedom);
            LastContactCount = body.LastContactCount;
            LastContactPassCount = 0;
            resetPending = false;
            LastStepMilliseconds = (float)((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
        }

        private static Vector3 MotionPosition(Transform frame) =>
            frame.parent != null ? frame.parent.position : frame.position;

        public void Step(float dt, Transform frame, PlayerScarfCollisionWorld world)
        {
            if (!detailedContacts) throw new InvalidOperationException("Detailed cloth requires diagnostic initialization.");
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
