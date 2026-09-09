using System;
using Unity.Collections;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Numeric copy of the current collision world for synchronous Burst jobs.
    /// Counts delimit active entries; capacity is retained between frames.
    /// The owning world updates or disposes these buffers only after jobs finish.
    /// </summary>
    internal sealed class PlayerScarfCollisionSnapshot : IDisposable
    {
        public struct Triangle
        {
            public Vector3 A, B, C;
            public Vector3 PreviousA, PreviousB, PreviousC;
            public Vector3 Normal, PreviousNormal;
            public Bounds Bounds;
            public int Flags;
            public bool ClosedSurface => (Flags & 1) != 0;
            public bool OneSided => (Flags & 2) != 0;
        }

        public struct Node
        {
            public Bounds Bounds;
            public int Left, Right, Start, Count;
        }

        public struct Component
        {
            public Bounds Bounds;
            public int Root;
        }

        private NativeArray<Triangle> triangles;
        private NativeArray<Node> nodes;
        private NativeArray<int> orders;
        private NativeArray<Component> components;
        private bool disposed;

        public NativeArray<Triangle> Triangles => triangles;
        public NativeArray<Node> Nodes => nodes;
        public NativeArray<int> Orders => orders;
        public NativeArray<Component> Components => components;
        internal Triangle[] StagedTriangles { get; private set; } = Array.Empty<Triangle>();
        internal Node[] StagedNodes { get; private set; } = Array.Empty<Node>();
        internal int[] StagedOrders { get; private set; } = Array.Empty<int>();
        internal Component[] StagedComponents { get; private set; } = Array.Empty<Component>();
        public int TriangleCount { get; private set; }
        public int NodeCount { get; private set; }
        public int OrderCount { get; private set; }
        public int ComponentCount { get; private set; }
        public int RootGlobal { get; internal set; } = -1;
        public int RootComponents { get; internal set; } = -1;

        public PlayerScarfCollisionSnapshot() => Begin(0, 0, 0, 0);

        internal void Begin(int triangleCount, int nodeCount, int orderCount, int componentCount)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlayerScarfCollisionSnapshot));
            EnsureCapacity(ref triangles, triangleCount);
            EnsureCapacity(ref nodes, nodeCount);
            EnsureCapacity(ref orders, orderCount);
            EnsureCapacity(ref components, componentCount);
            if (StagedTriangles.Length < triangles.Length) StagedTriangles = new Triangle[triangles.Length];
            if (StagedNodes.Length < nodes.Length) StagedNodes = new Node[nodes.Length];
            if (StagedOrders.Length < orders.Length) StagedOrders = new int[orders.Length];
            if (StagedComponents.Length < components.Length) StagedComponents = new Component[components.Length];
            TriangleCount = triangleCount;
            NodeCount = nodeCount;
            OrderCount = orderCount;
            ComponentCount = componentCount;
            RootGlobal = RootComponents = -1;
        }

        internal void Commit()
        {
            // Bulk copies cross the native safety boundary once per buffer,
            // rather than once for every node, order entry and triangle.
            NativeArray<Triangle>.Copy(StagedTriangles, 0, triangles, 0, TriangleCount);
            NativeArray<Node>.Copy(StagedNodes, 0, nodes, 0, NodeCount);
            NativeArray<int>.Copy(StagedOrders, 0, orders, 0, OrderCount);
            NativeArray<Component>.Copy(StagedComponents, 0, components, 0, ComponentCount);
        }

        internal void Clear()
        {
            TriangleCount = NodeCount = OrderCount = ComponentCount = 0;
            RootGlobal = RootComponents = -1;
        }

        private static void EnsureCapacity<T>(ref NativeArray<T> buffer, int count) where T : unmanaged
        {
            int required = Math.Max(1, count);
            if (buffer.IsCreated && buffer.Length >= required) return;
            int capacity = 1;
            while (capacity < required) capacity = checked(capacity * 2);
            // Allocate first so an allocation failure preserves the old buffer.
            var replacement = new NativeArray<T>(capacity, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            if (buffer.IsCreated) buffer.Dispose();
            buffer = replacement;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (triangles.IsCreated) triangles.Dispose();
            if (nodes.IsCreated) nodes.Dispose();
            if (orders.IsCreated) orders.Dispose();
            if (components.IsCreated) components.Dispose();
            StagedTriangles = Array.Empty<Triangle>(); StagedNodes = Array.Empty<Node>();
            StagedOrders = Array.Empty<int>(); StagedComponents = Array.Empty<Component>();
            Clear();
        }
    }
}
