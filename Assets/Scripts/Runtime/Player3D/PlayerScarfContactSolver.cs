using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One sequential native contact solve over the real model surfaces.</summary>
    public static class PlayerScarfContactSolver
    {
        public const float Thickness = .004f;
        private static NativeArray<Vector3> previousBuffer, currentBuffer, checkedPoints, checkedA, checkedB, checkedC;
        private static NativeArray<int> indicesBuffer, candidates, closedCandidates, componentCandidates, stack, results;
        private static NativeArray<bool> stablePoints, stableFaces;
        private static bool warmed;
        public static int LastContactCount { get; private set; }
        public static int LastPassCount { get; private set; }
        public static bool LastExecutionWasNative { get; private set; }

        // Compile the Editor job while the area is loading. Player builds
        // receive native code already compiled, before any inventory action.
        internal static void Warmup()
        {
            if (warmed) return;
            using (var empty = new PlayerScarfCollisionSnapshot())
            {
                EnsureBuffers(0, 0, empty);
                CreateJob(empty, 0, 0, Thickness, 0).Run();
            }
            warmed = true;
        }

        public static void Resolve(Vector3[] previous, Vector3[] current, int[] indices,
            PlayerScarfCollisionWorld world, float thickness = Thickness, int iterationLimit = 32)
        {
            LastContactCount = LastPassCount = 0;
            if (world == null || current == null || current.Length == 0) return;
            var snapshot = world.NativeSnapshot;
            EnsureBuffers(current.Length, indices.Length, snapshot);
            NativeArray<Vector3>.Copy(previous, 0, previousBuffer, 0, current.Length);
            NativeArray<Vector3>.Copy(current, 0, currentBuffer, 0, current.Length);
            NativeArray<int>.Copy(indices, 0, indicesBuffer, 0, indices.Length);
            // Run completes before returning. The world, other scarf surfaces
            // and mirror never race this shared scratch storage.
            CreateJob(snapshot, current.Length, indices.Length, thickness, iterationLimit).Run();
            NativeArray<Vector3>.Copy(currentBuffer, 0, current, 0, current.Length);
            LastContactCount = results[0];
            LastPassCount = results[1];
            LastExecutionWasNative = results[2] != 0;
        }

        private static PlayerScarfContactJob CreateJob(PlayerScarfCollisionSnapshot snapshot,
            int vertices, int indices, float thickness, int passes) => new PlayerScarfContactJob
        {
            Previous = previousBuffer, Current = currentBuffer, Indices = indicesBuffer,
            Triangles = snapshot.Triangles, Nodes = snapshot.Nodes, Orders = snapshot.Orders,
            Components = snapshot.Components, RootGlobal = snapshot.RootGlobal, RootComponents = snapshot.RootComponents,
            candidates = candidates, closedCandidates = closedCandidates, componentCandidates = componentCandidates,
            stack = stack, Results = results, checkedPoints = checkedPoints,
            checkedA = checkedA, checkedB = checkedB, checkedC = checkedC,
            stablePoints = stablePoints, stableFaces = stableFaces,
            VertexCount = vertices, IndexCount = indices, Thickness = thickness, IterationLimit = passes
        };

        private static void EnsureBuffers(int vertexCount, int indexCount, PlayerScarfCollisionSnapshot snapshot)
        {
            Ensure(ref previousBuffer, vertexCount); Ensure(ref currentBuffer, vertexCount);
            Ensure(ref indicesBuffer, indexCount); Ensure(ref checkedPoints, vertexCount);
            Ensure(ref stablePoints, vertexCount);
            int faces = indexCount / 3;
            Ensure(ref checkedA, faces); Ensure(ref checkedB, faces); Ensure(ref checkedC, faces);
            Ensure(ref stableFaces, faces);
            Ensure(ref candidates, snapshot.TriangleCount);
            Ensure(ref closedCandidates, snapshot.ComponentCount);
            Ensure(ref componentCandidates, snapshot.ComponentCount);
            Ensure(ref stack, snapshot.NodeCount);
            Ensure(ref results, 3);
        }

        private static void Ensure<T>(ref NativeArray<T> buffer, int required) where T : unmanaged
        {
            required = Math.Max(1, required);
            if (buffer.IsCreated && buffer.Length >= required) return;
            int capacity = 1;
            while (capacity < required) capacity = checked(capacity * 2);
            var replacement = new NativeArray<T>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (buffer.IsCreated) buffer.Dispose();
            buffer = replacement;
        }

        public static bool SegmentTriangle(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c,
            out Vector3 hit) => PlayerScarfContactJob.SegmentTriangle(start, end, a, b, c, out hit);

        public static Vector3 ClosestPoint(Vector3 point, Vector3 a, Vector3 b, Vector3 c) =>
            PlayerScarfContactJob.ClosestPoint(point, a, b, c);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ReleaseBuffers();
            warmed = false;
        }

        internal static void ReleaseBuffers()
        {
            Release(ref previousBuffer); Release(ref currentBuffer); Release(ref indicesBuffer);
            Release(ref checkedPoints); Release(ref checkedA); Release(ref checkedB); Release(ref checkedC);
            Release(ref stablePoints); Release(ref stableFaces);
            Release(ref candidates); Release(ref closedCandidates); Release(ref componentCandidates);
            Release(ref stack); Release(ref results);
        }

        private static void Release<T>(ref NativeArray<T> buffer) where T : unmanaged
        {
            if (buffer.IsCreated) buffer.Dispose();
            buffer = default;
        }
    }
}
