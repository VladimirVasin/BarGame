using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Local, rendered triangle geometry for scarf contact. This deliberately
    /// does not depend on gameplay colliders: trim, glass and animated bodies
    /// remain surfaces even when their visual asset has no physics component.
    /// </summary>
    public sealed class PlayerScarfCollisionWorld : IDisposable
    {
        private sealed class EdgeHashComparer : IEqualityComparer<ulong>
        {
            public static readonly EdgeHashComparer Instance = new EdgeHashComparer();
            public bool Equals(ulong a, ulong b) => a == b;
            public int GetHashCode(ulong value)
            {
                // UInt64's default high XOR low hash sends almost every
                // consecutive grid edge to the same few buckets. Avalanche
                // both vertex indices before folding them to a 32-bit hash.
                unchecked
                {
                    value ^= value >> 30;
                    value *= 0xbf58476d1ce4e5b9UL;
                    value ^= value >> 27;
                    value *= 0x94d049bb133111ebUL;
                    value ^= value >> 31;
                    return (int)(value ^ (value >> 32));
                }
            }
        }

        public struct Triangle
        {
            public Vector3 A, B, C;
            public Vector3 PreviousA, PreviousB, PreviousC;
            public Vector3 Normal, PreviousNormal;
            public Bounds Bounds;
            public Bounds SurfaceBounds;
            public Renderer Owner;
            public bool ClosedSurface;
            public bool OneSided;
            public int SurfaceId;
        }

        private sealed class Geometry
        {
            public Vector3[] Vertices;
            public int[] Indices;
            public bool[] Closed;
            public float[] Orientation;
            public int[] Components;
            public BoundsTree StaticTree;
            public int[] MaterialEnds;
            public int ComponentCount;
            public ComponentVertex[] ComponentVertices;
        }

        private readonly struct ComponentVertex
        {
            public readonly int Component, Vertex;
            public ComponentVertex(int component, int vertex) { Component = component; Vertex = vertex; }
        }

        private sealed class Snapshot
        {
            public Mesh Source;
            public Mesh Baked;
            public Vector3[] World;
            public Vector3[] Scratch;
            public Matrix4x4 Matrix;
            public Bounds Bounds;
            public int Seen;
            public int BakedOn;
            public int[] SurfaceIds;
            public Bounds[] SurfaceBounds;
            public bool[] SurfaceBoundsInitialized;
            public Vector3[] SurfaceMin, SurfaceMax;
            public readonly List<Vector3> Local = new List<Vector3>();
            public Matrix4x4 CachedMatrix;
            public bool HasStaticCache;
            public readonly List<Material> Materials = new List<Material>();
            public bool[] MaterialOneSided;
            public bool BodySurface;
        }

        private sealed class BoundsTree
        {
            private struct Node
            {
                public Bounds Bounds;
                public int Left, Right, Start, Count;
            }

            private readonly List<Triangle> triangles;
            private readonly List<Bounds> worldBounds;
            private readonly Bounds[] localBounds;
            private readonly List<Node> nodes = new List<Node>();
            private int[] order = Array.Empty<int>();
            private int[] membership = Array.Empty<int>();
            private int membershipCount;
            private int refits;
            public readonly List<int> Input = new List<int>();
            public Bounds SurfaceBounds;
            public int NodeCount => nodes.Count;
            public int OrderCount => membershipCount;

            public BoundsTree(List<Triangle> triangles, List<Bounds> bounds)
            { this.triangles = triangles; worldBounds = bounds; }
            public BoundsTree(List<Bounds> bounds)
            { worldBounds = bounds; }
            public BoundsTree(Bounds[] bounds)
            { localBounds = bounds; }

            private Bounds BoundsAt(int index) => localBounds != null ? localBounds[index] : worldBounds[index];

            public int Export(PlayerScarfCollisionSnapshot snapshot, ref int nodeOffset, ref int orderOffset)
            {
                if (nodes.Count == 0) return -1;
                int root = nodeOffset;
                var destinationNodes = snapshot.StagedNodes;
                var destinationOrders = snapshot.StagedOrders;
                for (int i = 0; i < nodes.Count; ++i)
                {
                    Node node = nodes[i];
                    destinationNodes[nodeOffset + i] = new PlayerScarfCollisionSnapshot.Node
                    {
                        Bounds = node.Bounds,
                        Left = node.Count == 0 ? nodeOffset + node.Left : -1,
                        Right = node.Count == 0 ? nodeOffset + node.Right : -1,
                        Start = node.Count != 0 ? orderOffset + node.Start : 0,
                        Count = node.Count
                    };
                }
                Array.Copy(order, 0, destinationOrders, orderOffset, membershipCount);
                nodeOffset += nodes.Count;
                orderOffset += membershipCount;
                return root;
            }

            public void Build()
            {
                if (Input.Count == 0)
                {
                    nodes.Clear(); membershipCount = refits = 0;
                    return;
                }
                bool unchanged = nodes.Count != 0 && Input.Count == membershipCount;
                for (int i = 0; unchanged && i < Input.Count; ++i)
                    unchanged = Input[i] == membership[i];
                if (unchanged && refits < 63)
                {
                    Refit();
                    ++refits;
                    return;
                }
                nodes.Clear();
                if (order.Length < Input.Count) order = new int[Input.Count];
                if (membership.Length < Input.Count) membership = new int[Input.Count];
                Input.CopyTo(order);
                Input.CopyTo(membership);
                membershipCount = Input.Count;
                refits = 0;
                BuildNode(0, Input.Count);
            }

            private void Refit()
            {
                // Nodes were emitted parent-first, so this visits children
                // before parents. Every bound is current even when faces move
                // between spatial regions; the periodic rebuild only improves
                // partition quality and never changes candidate coverage.
                for (int index = nodes.Count - 1; index >= 0; --index)
                {
                    Node node = nodes[index];
                    Bounds bounds;
                    if (node.Count != 0)
                    {
                        bounds = BoundsAt(order[node.Start]);
                        for (int i = node.Start + 1; i < node.Start + node.Count; ++i)
                            bounds.Encapsulate(BoundsAt(order[i]));
                    }
                    else
                    {
                        bounds = nodes[node.Left].Bounds;
                        bounds.Encapsulate(nodes[node.Right].Bounds);
                    }
                    node.Bounds = bounds;
                    nodes[index] = node;
                }
            }

            private int BuildNode(int start, int count)
            {
                Bounds bounds = BoundsAt(order[start]);
                for (int i = start + 1; i < start + count; ++i) bounds.Encapsulate(BoundsAt(order[i]));
                int index = nodes.Count;
                nodes.Add(default);
                if (count <= 8)
                {
                    nodes[index] = new Node { Bounds = bounds, Start = start, Count = count };
                    return index;
                }
                Vector3 size = bounds.size;
                int half = count / 2;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                PartitionMedian(start, start + count - 1, start + half, axis);
                int left = BuildNode(start, half);
                int right = BuildNode(start + half, count - half);
                nodes[index] = new Node { Bounds = bounds, Left = left, Right = right };
                return index;
            }

            private void PartitionMedian(int left, int right, int median, int axis)
            {
                // Only partition membership is needed for a BVH. Sorting both
                // complete halves again at every depth repeated substantial work
                // whenever moving contacts changed the collected triangle count.
                while (left < right)
                {
                    float first = BoundsAt(order[left]).center[axis];
                    float middle = BoundsAt(order[left + (right - left) / 2]).center[axis];
                    float last = BoundsAt(order[right]).center[axis];
                    float pivot = first < middle
                        ? (middle < last ? middle : first < last ? last : first)
                        : (first < last ? first : middle < last ? last : middle);
                    int i = left, j = right;
                    while (i <= j)
                    {
                        while (i <= right && BoundsAt(order[i]).center[axis] < pivot) ++i;
                        while (j >= left && BoundsAt(order[j]).center[axis] > pivot) --j;
                        if (i > j) break;
                        int swap = order[i]; order[i] = order[j]; order[j] = swap;
                        ++i; --j;
                    }
                    if (median <= j) right = j;
                    else if (median >= i) left = i;
                    else return;
                }
            }

            public void Query(Bounds bounds, List<int> output)
            { if (nodes.Count != 0) QueryNode(0, bounds, output); }

            public void QueryPoint(Vector3 point, List<int> output)
            { if (nodes.Count != 0) QueryPointNode(0, point, output); }

            private void QueryPointNode(int index, Vector3 point, List<int> output)
            {
                Node node = nodes[index];
                if (!BoundsContainsPoint(node.Bounds, point)) return;
                if (node.Count != 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; ++i)
                        if (BoundsContainsPoint(BoundsAt(order[i]), point)) output.Add(order[i]);
                    return;
                }
                QueryPointNode(node.Left, point, output);
                QueryPointNode(node.Right, point, output);
            }

            private void QueryNode(int index, Bounds bounds, List<int> output)
            {
                Node node = nodes[index];
                if (!node.Bounds.Intersects(bounds)) return;
                if (node.Count != 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; ++i)
                        if (BoundsAt(order[i]).Intersects(bounds)) output.Add(order[i]);
                    return;
                }
                QueryNode(node.Left, bounds, output);
                QueryNode(node.Right, bounds, output);
            }

            public int Nearest(Vector3 point)
            {
                int nearest = -1;
                float squared = float.PositiveInfinity;
                if (nodes.Count != 0) NearestNode(0, point, ref squared, ref nearest);
                return nearest;
            }

            private void NearestNode(int index, Vector3 point, ref float squared, ref int nearest)
            {
                Node node = nodes[index];
                if (BoundsSquaredDistance(node.Bounds, point) > squared) return;
                if (node.Count != 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; ++i)
                    {
                        Triangle triangle = triangles[order[i]];
                        if (BoundsSquaredDistance(triangle.Bounds, point) > squared) continue;
                        Vector3 closest = PlayerScarfContactSolver.ClosestPoint(point, triangle.A, triangle.B, triangle.C);
                        float distance = (closest - point).sqrMagnitude;
                        if (distance < squared) { squared = distance; nearest = order[i]; }
                    }
                    return;
                }
                float left = BoundsSquaredDistance(nodes[node.Left].Bounds, point);
                float right = BoundsSquaredDistance(nodes[node.Right].Bounds, point);
                // Visit the nearer subtree first to tighten the distance bound.
                NearestNode(left <= right ? node.Left : node.Right, point, ref squared, ref nearest);
                NearestNode(left <= right ? node.Right : node.Left, point, ref squared, ref nearest);
            }
        }

        private readonly Dictionary<Mesh, Geometry> meshes = new Dictionary<Mesh, Geometry>();
        private readonly Dictionary<Renderer, Snapshot> snapshots = new Dictionary<Renderer, Snapshot>();
        private readonly List<Renderer> expired = new List<Renderer>();
        private readonly List<Triangle> triangles = new List<Triangle>(2048);
        private readonly List<Bounds> triangleBounds = new List<Bounds>(2048);
        private readonly List<Transform> presentationCopies = new List<Transform>();
        private readonly HashSet<Renderer> physicalBody = new HashSet<Renderer>();
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly BoundsTree triangleTree;
        private readonly Dictionary<int, BoundsTree> closedTrees = new Dictionary<int, BoundsTree>();
        private readonly List<BoundsTree> closedComponents = new List<BoundsTree>();
        private readonly List<Bounds> closedComponentBounds = new List<Bounds>();
        private readonly List<int> nearbyClosedComponents = new List<int>();
        private readonly BoundsTree closedComponentTree;
        private readonly PlayerScarfCollisionSnapshot nativeSnapshot = new PlayerScarfCollisionSnapshot();
        private readonly List<int> expiredSurfaces = new List<int>();
        private readonly List<int> staticCandidates = new List<int>();
        private int update;
        private int nextSurfaceId;
        private bool disposed;

        public IReadOnlyList<Triangle> Triangles => triangles;
        internal PlayerScarfCollisionSnapshot NativeSnapshot => nativeSnapshot;
        public int CandidateRendererCount { get; private set; }
        public int CollectedTriangleCount => triangles.Count;
        public int ScannedRendererCount { get; private set; }
        public double LastUpdateMilliseconds { get; private set; }
        public double LastSpatialBuildMilliseconds { get; private set; }
        public double LastDiscoveryMilliseconds { get; private set; }
        public double LastCollectionMilliseconds { get; private set; }

        public PlayerScarfCollisionWorld()
        {
            triangleTree = new BoundsTree(triangles, triangleBounds);
            closedComponentTree = new BoundsTree(closedComponentBounds);
        }

        /// <summary>
        /// Starts a fresh motion sample after teleporting or equipping. Cached
        /// source geometry and spatial-index buffers survive; old world poses
        /// must never become a swept contact in the new location.
        /// </summary>
        public void ResetHistory()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlayerScarfCollisionWorld));
            foreach (Snapshot snapshot in snapshots.Values) DestroyMesh(snapshot.Baked);
            snapshots.Clear();
            expired.Clear();
            triangles.Clear();
            triangleBounds.Clear();
            triangleTree.Input.Clear();
            triangleTree.Build();
            foreach (BoundsTree tree in closedTrees.Values)
            {
                tree.Input.Clear();
                tree.Build();
            }
            closedComponents.Clear();
            closedComponentBounds.Clear();
            nearbyClosedComponents.Clear();
            closedComponentTree.Input.Clear();
            closedComponentTree.Build();
            nativeSnapshot.Clear();
            // Reuse the existing per-surface buffers as new snapshots assign IDs.
            nextSurfaceId = 0;
            expiredSurfaces.Clear();
            presentationCopies.Clear();
            physicalBody.Clear();
            CandidateRendererCount = ScannedRendererCount = 0;
            LastUpdateMilliseconds = LastSpatialBuildMilliseconds = 0d;
            LastDiscoveryMilliseconds = LastCollectionMilliseconds = 0d;
        }

        /// <summary>Replaces output with indices whose swept triangle AABBs intersect query.</summary>
        public void Query(Bounds query, List<int> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            triangleTree.Query(query, output);
        }

        /// <summary>
        /// Replaces output with the exact nearest collected face of each closed
        /// component containing the point in its full world AABB. The caller
        /// decides whether that nearest outward face puts the point inside.
        /// </summary>
        public void QueryNearestClosedSurfaces(Vector3 point, List<int> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            nearbyClosedComponents.Clear();
            closedComponentTree.QueryPoint(point, nearbyClosedComponents);
            // Preserve the established closed-surface recovery order when
            // several solids overlap. Only the containing subset is visited.
            nearbyClosedComponents.Sort();
            foreach (int component in nearbyClosedComponents)
            {
                BoundsTree tree = closedComponents[component];
                int face = tree.Nearest(point);
                if (face >= 0) output.Add(face);
            }
        }

        private void BuildSpatialQueries()
        {
            long started = Stopwatch.GetTimestamp();
            triangleTree.Input.Clear();
            triangleBounds.Clear();
            foreach (BoundsTree tree in closedTrees.Values) tree.Input.Clear();
            for (int i = 0; i < triangles.Count; ++i)
            {
                triangleTree.Input.Add(i);
                Triangle triangle = triangles[i];
                triangleBounds.Add(triangle.Bounds);
                if (!triangle.ClosedSurface) continue;
                if (!closedTrees.TryGetValue(triangle.SurfaceId, out BoundsTree tree))
                {
                    tree = new BoundsTree(triangles, triangleBounds);
                    closedTrees.Add(triangle.SurfaceId, tree);
                }
                tree.SurfaceBounds = triangle.SurfaceBounds;
                tree.Input.Add(i);
            }
            triangleTree.Build();
            expiredSurfaces.Clear();
            foreach (KeyValuePair<int, BoundsTree> entry in closedTrees)
            {
                if (entry.Value.Input.Count == 0) expiredSurfaces.Add(entry.Key);
                else entry.Value.Build();
            }
            foreach (int surface in expiredSurfaces) closedTrees.Remove(surface);
            closedComponents.Clear();
            closedComponentBounds.Clear();
            closedComponentTree.Input.Clear();
            foreach (BoundsTree tree in closedTrees.Values)
            {
                closedComponentTree.Input.Add(closedComponents.Count);
                closedComponents.Add(tree);
                closedComponentBounds.Add(tree.SurfaceBounds);
            }
            closedComponentTree.Build();
            ExportNativeSnapshot();
            LastSpatialBuildMilliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }

        private void ExportNativeSnapshot()
        {
            int nodeCount = triangleTree.NodeCount + closedComponentTree.NodeCount;
            int orderCount = triangleTree.OrderCount + closedComponentTree.OrderCount;
            foreach (BoundsTree tree in closedComponents)
            {
                nodeCount += tree.NodeCount;
                orderCount += tree.OrderCount;
            }
            nativeSnapshot.Begin(triangles.Count, nodeCount, orderCount, closedComponents.Count);
            var nativeTriangles = nativeSnapshot.StagedTriangles;
            for (int i = 0; i < triangles.Count; ++i)
            {
                Triangle triangle = triangles[i];
                nativeTriangles[i] = new PlayerScarfCollisionSnapshot.Triangle
                {
                    A = triangle.A, B = triangle.B, C = triangle.C,
                    PreviousA = triangle.PreviousA, PreviousB = triangle.PreviousB, PreviousC = triangle.PreviousC,
                    Normal = triangle.Normal, PreviousNormal = triangle.PreviousNormal,
                    Bounds = triangle.Bounds,
                    Flags = (triangle.ClosedSurface ? 1 : 0) | (triangle.OneSided ? 2 : 0)
                };
            }
            int nodeOffset = 0, orderOffset = 0;
            nativeSnapshot.RootGlobal = triangleTree.Export(nativeSnapshot, ref nodeOffset, ref orderOffset);
            var nativeComponents = nativeSnapshot.StagedComponents;
            for (int i = 0; i < closedComponents.Count; ++i)
            {
                BoundsTree tree = closedComponents[i];
                nativeComponents[i] = new PlayerScarfCollisionSnapshot.Component
                {
                    Bounds = tree.SurfaceBounds,
                    Root = tree.Export(nativeSnapshot, ref nodeOffset, ref orderOffset)
                };
            }
            // The final tree's orders index component ordinals, not triangles.
            nativeSnapshot.RootComponents = closedComponentTree.Export(nativeSnapshot, ref nodeOffset, ref orderOffset);
            nativeSnapshot.Commit();
        }

        public void Update(Bounds sweptBounds, ICollection<Renderer> excluded)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlayerScarfCollisionWorld));
            stopwatch.Restart();
            try
            {
                ++update;

                triangles.Clear();
                CandidateRendererCount = 0;
                // Include a neighbouring face when a point starts inside a solid.
                sweptBounds.Expand(0.4f);

                long phase = Stopwatch.GetTimestamp();
                CollectPresentationCopies();

                Renderer[] all = Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                LastDiscoveryMilliseconds = ElapsedMilliseconds(phase);
                phase = Stopwatch.GetTimestamp();
                ScannedRendererCount = all.Length;
                foreach (Renderer renderer in all)
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    if (((!renderer.enabled || renderer.forceRenderingOff ||
                          renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) && !physicalBody.Contains(renderer)) ||
                        (excluded != null && excluded.Contains(renderer)) || IsPresentationCopy(renderer.transform)) continue;

                    if (!snapshots.TryGetValue(renderer, out Snapshot state))
                    {
                        state = new Snapshot { Matrix = renderer.localToWorldMatrix, Bounds = renderer.bounds };
                        snapshots.Add(renderer, state);
                    }
                    state.Seen = update;
                    Bounds bounds = renderer.bounds;
                    Bounds broadphase = bounds;
                    broadphase.Encapsulate(state.Bounds);
                    Matrix4x4 matrix = renderer.localToWorldMatrix;
                    if (broadphase.Intersects(sweptBounds))
                    {
                        // Preserve every renderer's motion history, but do not
                        // cross the native mesh/component API for distant ones.
                        Mesh source = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                            renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (source != null && source.vertexCount != 0)
                        {
                            ++CandidateRendererCount;
                            Collect(renderer, source, state, matrix, sweptBounds);
                        }
                    }
                    state.Bounds = bounds;
                    state.Matrix = matrix;
                }
                expired.Clear();
                foreach (KeyValuePair<Renderer, Snapshot> entry in snapshots)
                    if (entry.Key == null || entry.Value.Seen != update) expired.Add(entry.Key);
                foreach (Renderer renderer in expired)
                {
                    DestroyMesh(snapshots[renderer].Baked);
                    snapshots.Remove(renderer);
                }
                LastCollectionMilliseconds = ElapsedMilliseconds(phase);
                BuildSpatialQueries();

            }
            finally
            {
                stopwatch.Stop();
                LastUpdateMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private void CollectPresentationCopies()
        {
            presentationCopies.Clear();
            physicalBody.Clear();
            // The physical reflection is a second rendered scene on the same
            // camera layer. Its owning component, not an object-name whitelist,
            // identifies that presentation-only geometry.
            foreach (HomeBathroomMirrorWorld mirror in Object.FindObjectsByType<HomeBathroomMirrorWorld>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (mirror.MirrorSpace != null) presentationCopies.Add(mirror.MirrorSpace);
            foreach (InventoryItemPreviewRenderer preview in Object.FindObjectsByType<InventoryItemPreviewRenderer>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (preview.ModelRoot != null) presentationCopies.Add(preview.ModelRoot);
            // Camera-only hiding of the real rig must not remove its body
            // from the simulation that also drives the reflected scarf.
            foreach (Player3DAssetRegistry registry in Object.FindObjectsByType<Player3DAssetRegistry>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (IsPresentationCopy(registry.transform)) continue;
                foreach (Player3DMeshBinding binding in registry.MeshBindings)
                    if (binding?.Renderer != null) physicalBody.Add(binding.Renderer);
            }
        }

        private bool IsPresentationCopy(Transform transform)
        {
            foreach (Transform root in presentationCopies)
                if (transform == root || transform.IsChildOf(root)) return true;
            return false;
        }

        private void Collect(Renderer renderer, Mesh source, Snapshot state,
            Matrix4x4 matrix, Bounds query)
        {

            if (!meshes.TryGetValue(source, out Geometry geometry))
            {
                try
                {

                    ReadMesh(source, out Vector3[] vertices, out int[] indices);

                    geometry = new Geometry { Vertices = vertices, Indices = indices };
                    geometry.MaterialEnds = new int[source.subMeshCount];
                    int faces = 0;
                    for (int submesh = 0; submesh < source.subMeshCount; ++submesh)
                    {
                        SubMeshDescriptor part = source.GetSubMesh(submesh);
                        faces += part.topology == MeshTopology.Triangles ? part.indexCount / 3 :
                            part.topology == MeshTopology.Quads ? part.indexCount / 4 * 2 : 0;
                        geometry.MaterialEnds[submesh] = faces;
                    }

                    BuildTopology(geometry);

                    meshes.Add(source, geometry);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Scarf cannot read collision mesh '{source.name}' on '{renderer.name}'. " +
                        "The rendered surface must supply a readable CPU or GPU position/index buffer.", exception);
                }
            }
            ReadSurfaceSides(renderer, state);
            // A stationary rigid mesh can use a local index without changing
            // swept contact coverage. Moving meshes keep the complete previous/
            // current vertex path below, including rotation through the query.
            if (renderer is MeshRenderer && matrix == state.Matrix)
            {
                CollectStationary(renderer, source, geometry, state, matrix, query);
                return;
            }
            bool continuous = state.Source == source && state.BakedOn == update - 1 &&
                              state.World != null && state.World.Length == geometry.Vertices.Length;
            if (state.Source != source || state.SurfaceIds == null)
            {
                InitializeSurfaces(state, geometry.ComponentCount);
            }
            state.Source = source;
            int count = geometry.Vertices.Length;
            if (state.Scratch == null || state.Scratch.Length != count) state.Scratch = new Vector3[count];
            Vector3[] local = geometry.Vertices;
            if (renderer is SkinnedMeshRenderer skin)
            {

                if (state.Baked == null)
                    state.Baked = new Mesh { name = "Scarf collision pose", hideFlags = HideFlags.HideAndDontSave };
                // Compensate renderer scale in the snapshot so the complete
                // localToWorldMatrix applies it exactly once (FBX roots use 100).
                skin.BakeMesh(state.Baked, true);
                state.Local.Clear();
                state.Baked.GetVertices(state.Local);
                if (state.Local.Count != count)
                    throw new InvalidOperationException($"Scarf skin '{renderer.name}' changed its vertex count while baking.");
                for (int i = 0; i < count; ++i) state.Scratch[i] = matrix.MultiplyPoint3x4(state.Local[i]);

            }
            else
            {
                for (int i = 0; i < count; ++i) state.Scratch[i] = matrix.MultiplyPoint3x4(local[i]);
            }
            if (!continuous)
            {
                if (state.World == null || state.World.Length != count) state.World = new Vector3[count];
                // A newly approached object has no nearby pose sample yet.
                // Retain its recorded root motion, using the current skin pose.
                for (int i = 0; i < count; ++i)
                    state.World[i] = state.Matrix.MultiplyPoint3x4(
                        renderer is SkinnedMeshRenderer ? state.Local[i] : local[i]);
            }
            float determinant = matrix.determinant < 0f ? -1f : 1f;
            float previousDeterminant = state.Matrix.determinant < 0f ? -1f : 1f;
            UpdateComponentBounds(geometry, state, state.Scratch);
            for (int i = 0, face = 0; i < geometry.Indices.Length; i += 3, ++face)
            {
                int a = geometry.Indices[i], b = geometry.Indices[i + 1], c = geometry.Indices[i + 2];
                Vector3 pa = state.World[a], pb = state.World[b], pc = state.World[c];
                Vector3 va = state.Scratch[a], vb = state.Scratch[b], vc = state.Scratch[c];
                Bounds bounds = new Bounds(va, Vector3.zero);
                bounds.Encapsulate(vb); bounds.Encapsulate(vc);
                bounds.Encapsulate(pa); bounds.Encapsulate(pb); bounds.Encapsulate(pc);
                if (!bounds.Intersects(query)) continue;
                Vector3 normal = Vector3.Cross(vb - va, vc - va);
                if (normal.sqrMagnitude < 1e-16f) continue;
                Vector3 previousNormal = Vector3.Cross(pb - pa, pc - pa);
                float sign = geometry.Orientation[face];
                int component = geometry.Components[face];
                if (state.SurfaceIds[component] == 0) state.SurfaceIds[component] = ++nextSurfaceId;
                bool closed = geometry.Closed[face];
                bool oneSided = IsOneSided(geometry, state, face);
                triangles.Add(new Triangle
                {
                    A = va, B = vb, C = vc, PreviousA = pa, PreviousB = pb, PreviousC = pc,
                    Normal = normal.normalized * sign * (closed || oneSided ? determinant : 1f),
                    PreviousNormal = previousNormal.normalized * sign * (closed || oneSided ? previousDeterminant : 1f),
                    Bounds = bounds, Owner = renderer, ClosedSurface = closed, OneSided = oneSided,
                    SurfaceId = state.SurfaceIds[component], SurfaceBounds = state.SurfaceBounds[component]
                });
            }
            Vector3[] previous = state.World;
            state.World = state.Scratch;
            state.Scratch = previous;
            state.BakedOn = update;
            state.HasStaticCache = renderer is MeshRenderer;
            state.CachedMatrix = matrix;
        }

        private void CollectStationary(Renderer renderer, Mesh source, Geometry geometry,
            Snapshot state, Matrix4x4 matrix, Bounds query)
        {
            if (geometry.StaticTree == null)
            {

                int count = geometry.Indices.Length / 3;
                var localBounds = new Bounds[count];
                for (int face = 0; face < count; ++face)
                {
                    int at = face * 3;
                    Bounds bounds = new Bounds(geometry.Vertices[geometry.Indices[at]], Vector3.zero);
                    bounds.Encapsulate(geometry.Vertices[geometry.Indices[at + 1]]);
                    bounds.Encapsulate(geometry.Vertices[geometry.Indices[at + 2]]);
                    localBounds[face] = bounds;
                }
                geometry.StaticTree = new BoundsTree(localBounds);
                for (int face = 0; face < count; ++face) geometry.StaticTree.Input.Add(face);
                geometry.StaticTree.Build();

            }
            if (state.Source != source || !state.HasStaticCache || state.CachedMatrix != matrix)
            {
                if (state.Source != source || state.SurfaceIds == null)
                {
                    InitializeSurfaces(state, geometry.ComponentCount);
                }
                if (state.World == null || state.World.Length != geometry.Vertices.Length)
                    state.World = new Vector3[geometry.Vertices.Length];
                for (int i = 0; i < state.World.Length; ++i)
                    state.World[i] = matrix.MultiplyPoint3x4(geometry.Vertices[i]);
                UpdateComponentBounds(geometry, state, state.World);
                state.Source = source;
                state.CachedMatrix = matrix;
                state.HasStaticCache = true;
            }
            Bounds localQuery = TransformBounds(query, matrix.inverse);
            staticCandidates.Clear();
            geometry.StaticTree.Query(localQuery, staticCandidates);
            float determinant = matrix.determinant < 0f ? -1f : 1f;
            foreach (int face in staticCandidates)
            {
                int at = face * 3;
                Vector3 a = state.World[geometry.Indices[at]];
                Vector3 b = state.World[geometry.Indices[at + 1]];
                Vector3 c = state.World[geometry.Indices[at + 2]];
                Bounds bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b); bounds.Encapsulate(c);
                if (!bounds.Intersects(query)) continue;
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-16f) continue;
                int component = geometry.Components[face];
                if (state.SurfaceIds[component] == 0) state.SurfaceIds[component] = ++nextSurfaceId;
                bool closed = geometry.Closed[face];
                bool oneSided = IsOneSided(geometry, state, face);
                normal = normal.normalized * geometry.Orientation[face] * (closed || oneSided ? determinant : 1f);
                triangles.Add(new Triangle
                {
                    A = a, B = b, C = c, PreviousA = a, PreviousB = b, PreviousC = c,
                    Normal = normal, PreviousNormal = normal, Bounds = bounds, Owner = renderer,
                    ClosedSurface = closed, OneSided = oneSided, SurfaceId = state.SurfaceIds[component],
                    SurfaceBounds = state.SurfaceBounds[component]
                });
            }
            state.BakedOn = update;
        }

        private static void InitializeSurfaces(Snapshot state, int count)
        {
            state.SurfaceIds = new int[count];
            state.SurfaceBounds = new Bounds[count];
            state.SurfaceBoundsInitialized = new bool[count];
            state.SurfaceMin = new Vector3[count];
            state.SurfaceMax = new Vector3[count];
        }

        private static void UpdateComponentBounds(Geometry geometry, Snapshot state, Vector3[] vertices)
        {
            Array.Clear(state.SurfaceBoundsInitialized, 0, state.SurfaceBoundsInitialized.Length);
            // Visit every contributing vertex once per component, including all
            // faces outside the local query. A vertex shared only at a point by
            // disconnected components contributes to both, without approximation.
            foreach (ComponentVertex entry in geometry.ComponentVertices)
            {
                int component = entry.Component;
                Vector3 point = vertices[entry.Vertex];
                if (!state.SurfaceBoundsInitialized[component])
                {
                    state.SurfaceMin[component] = state.SurfaceMax[component] = point;
                    state.SurfaceBoundsInitialized[component] = true;
                }
                else
                {
                    state.SurfaceMin[component] = Vector3.Min(state.SurfaceMin[component], point);
                    state.SurfaceMax[component] = Vector3.Max(state.SurfaceMax[component], point);
                }
            }
            for (int component = 0; component < geometry.ComponentCount; ++component)
            {
                Bounds bounds = default;
                bounds.SetMinMax(state.SurfaceMin[component], state.SurfaceMax[component]);
                state.SurfaceBounds[component] = bounds;
            }
        }

        private void ReadSurfaceSides(Renderer renderer, Snapshot state)
        {
            state.BodySurface = physicalBody.Contains(renderer);
            // Physical rig geometry always uses its outward side below, so
            // material culling values cannot change this renderer's contacts.
            if (state.BodySurface) return;
            renderer.GetSharedMaterials(state.Materials);
            int count = Mathf.Max(1, state.Materials.Count);
            if (state.MaterialOneSided == null || state.MaterialOneSided.Length != count)
                state.MaterialOneSided = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                Material material = i < state.Materials.Count ? state.Materials[i] : null;
                bool oneSided = false;
                if (material != null)
                {
                    if (material.HasProperty("_Cull"))
                        oneSided = Mathf.RoundToInt(material.GetFloat("_Cull")) == (int)CullMode.Back;
                    else if (material.HasProperty("_CullMode"))
                        oneSided = Mathf.RoundToInt(material.GetFloat("_CullMode")) == (int)CullMode.Back;
                    else
                        oneSided = material.renderQueue <= (int)RenderQueue.AlphaTest;
                }
                state.MaterialOneSided[i] = oneSided;
            }
        }

        private static bool IsOneSided(Geometry geometry, Snapshot state, int face)
        {
            if (state.BodySurface) return true;
            int slot = 0;
            while (slot < geometry.MaterialEnds.Length - 1 && face >= geometry.MaterialEnds[slot]) ++slot;
            return state.MaterialOneSided[Mathf.Min(slot, state.MaterialOneSided.Length - 1)];
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 min = bounds.min, max = bounds.max;
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 1; corner < 8; ++corner)
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsContainsPoint(Bounds bounds, Vector3 point)
        {
            Vector3 offset = point - bounds.center;
            Vector3 extent = bounds.extents;
            return Mathf.Abs(offset.x) <= extent.x && Mathf.Abs(offset.y) <= extent.y &&
                   Mathf.Abs(offset.z) <= extent.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BoundsSquaredDistance(Bounds bounds, Vector3 point)
        {
            Vector3 offset = point - bounds.center;
            Vector3 extent = bounds.extents;
            float x = Mathf.Abs(offset.x) - extent.x;
            float y = Mathf.Abs(offset.y) - extent.y;
            float z = Mathf.Abs(offset.z) - extent.z;
            return (x > 0f ? x * x : 0f) + (y > 0f ? y * y : 0f) + (z > 0f ? z * z : 0f);
        }

        /// <summary>
        /// Returns local positions and flattened triangle indices, including
        /// submesh base vertices. GPU-only imported meshes are read without
        /// changing their importer, buffer targets, or material instances.
        /// </summary>
        public static void ReadMesh(Mesh mesh, out Vector3[] vertices, out int[] triangles)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            vertices = new Vector3[mesh.vertexCount];
            var indices = new List<int>();
            if (mesh.isReadable)
            {
                vertices = mesh.vertices;
                for (int submesh = 0; submesh < mesh.subMeshCount; ++submesh)
                    AppendTriangles(indices, mesh.GetIndices(submesh, true), mesh.GetTopology(submesh));
            }
            else
            {
                if (!mesh.HasVertexAttribute(VertexAttribute.Position))
                    throw new InvalidOperationException($"Mesh '{mesh.name}' has no position stream.");
                VertexAttributeFormat format = mesh.GetVertexAttributeFormat(VertexAttribute.Position);
                int dimension = mesh.GetVertexAttributeDimension(VertexAttribute.Position);
                if ((format != VertexAttributeFormat.Float32 && format != VertexAttributeFormat.Float16) || dimension < 3)
                    throw new InvalidOperationException($"Mesh '{mesh.name}' has unsupported positions: {format} x {dimension}.");
                int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
                int stride = mesh.GetVertexBufferStride(stream);
                int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
                int width = format == VertexAttributeFormat.Float32 ? 4 : 2;

                using (GraphicsBuffer buffer = mesh.GetVertexBuffer(stream))
                {
                    if (buffer == null) throw new InvalidOperationException($"Mesh '{mesh.name}' has no GPU vertex buffer.");
                    byte[] data = new byte[checked(buffer.count * buffer.stride)];

                    buffer.GetData(data);

                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        int at = checked(i * stride + offset);
                        vertices[i] = new Vector3(ReadFloat(data, at, width),
                            ReadFloat(data, at + width, width), ReadFloat(data, at + 2 * width, width));
                    }
                }

                using (GraphicsBuffer buffer = mesh.GetIndexBuffer())
                {
                    if (buffer == null) throw new InvalidOperationException($"Mesh '{mesh.name}' has no GPU index buffer.");
                    byte[] data = new byte[checked(buffer.count * buffer.stride)];

                    buffer.GetData(data);

                    bool wide = mesh.indexFormat == IndexFormat.UInt32;
                    for (int submesh = 0; submesh < mesh.subMeshCount; ++submesh)
                    {
                        SubMeshDescriptor part = mesh.GetSubMesh(submesh);
                        int[] raw = new int[part.indexCount];
                        for (int i = 0; i < raw.Length; ++i)
                        {
                            int at = checked((part.indexStart + i) * (wide ? 4 : 2));
                            raw[i] = checked((int)(wide ? BitConverter.ToUInt32(data, at) :
                                BitConverter.ToUInt16(data, at)) + part.baseVertex);
                        }
                        AppendTriangles(indices, raw, part.topology);
                    }
                }

            }
            triangles = indices.ToArray();
            foreach (Vector3 vertex in vertices)
                if (float.IsNaN(vertex.x) || float.IsInfinity(vertex.x) ||
                    float.IsNaN(vertex.y) || float.IsInfinity(vertex.y) ||
                    float.IsNaN(vertex.z) || float.IsInfinity(vertex.z))
                    throw new InvalidOperationException($"Mesh '{mesh.name}' contains a non-finite position.");
            foreach (int index in triangles)
                if ((uint)index >= (uint)vertices.Length)
                    throw new InvalidOperationException($"Mesh '{mesh.name}' has an invalid triangle index {index}.");
        }

        private static float ReadFloat(byte[] data, int offset, int width)
        {
            if (width == 4) return BitConverter.ToSingle(data, offset);
            ushort bits = BitConverter.ToUInt16(data, offset);
            int sign = (bits & 0x8000) == 0 ? 1 : -1;
            int exponent = (bits >> 10) & 31;
            int mantissa = bits & 1023;
            if (exponent == 31) return mantissa == 0 ? sign * float.PositiveInfinity : float.NaN;
            return sign * (exponent == 0 ? mantissa * (1f / 16777216f) :
                (1f + mantissa / 1024f) * Mathf.Pow(2f, exponent - 15));
        }

        private static void AppendTriangles(List<int> target, int[] source, MeshTopology topology)
        {
            if (topology == MeshTopology.Triangles)
            {
                if (source.Length % 3 != 0) throw new InvalidOperationException("Malformed triangle submesh.");
                target.AddRange(source);
            }
            else if (topology == MeshTopology.Quads)
            {
                if (source.Length % 4 != 0) throw new InvalidOperationException("Malformed quad submesh.");
                for (int i = 0; i < source.Length; i += 4)
                {
                    target.Add(source[i]); target.Add(source[i + 1]); target.Add(source[i + 2]);
                    target.Add(source[i]); target.Add(source[i + 2]); target.Add(source[i + 3]);
                }
            }
            // Points and lines have no solid face; their renderer is not cloth geometry.
        }

        private static void BuildTopology(Geometry geometry)
        {
            int faces = geometry.Indices.Length / 3;
            geometry.Closed = new bool[faces];
            geometry.Orientation = new float[faces];
            geometry.Components = new int[faces];
            var welded = new Dictionary<Vector3Int, int>();
            int[] ids = new int[geometry.Vertices.Length];
            for (int i = 0; i < ids.Length; ++i)
            {
                Vector3 v = geometry.Vertices[i] * 100000f;
                var key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded.Add(key, id); }
                ids[i] = id;
            }
            int[] parent = new int[faces];
            for (int i = 0; i < faces; ++i) parent[i] = i;
            var edgeFaces = new Dictionary<ulong, List<int>>(EdgeHashComparer.Instance);
            for (int face = 0; face < faces; ++face)
            {
                int at = face * 3;
                for (int side = 0; side < 3; ++side)
                {
                    uint a = (uint)ids[geometry.Indices[at + side]];
                    uint b = (uint)ids[geometry.Indices[at + (side + 1) % 3]];
                    ulong edge = a < b ? ((ulong)a << 32) | b : ((ulong)b << 32) | a;
                    if (!edgeFaces.TryGetValue(edge, out List<int> adjacent))
                    { adjacent = new List<int>(2); edgeFaces.Add(edge, adjacent); }
                    else parent[Find(parent, face)] = Find(parent, adjacent[0]);
                    adjacent.Add(face);
                }
            }
            bool[] closed = new bool[faces];
            double[] volumes = new double[faces];
            for (int face = 0; face < faces; ++face) closed[face] = true;
            foreach (List<int> adjacent in edgeFaces.Values)
                if (adjacent.Count != 2) closed[Find(parent, adjacent[0])] = false;
            for (int face = 0; face < faces; ++face)
            {
                int at = face * 3;
                Vector3 a = geometry.Vertices[geometry.Indices[at]];
                Vector3 b = geometry.Vertices[geometry.Indices[at + 1]];
                Vector3 c = geometry.Vertices[geometry.Indices[at + 2]];
                volumes[Find(parent, face)] += Vector3.Dot(a, Vector3.Cross(b, c));
            }
            var compactComponents = new Dictionary<int, int>();
            for (int face = 0; face < faces; ++face)
            {
                int component = Find(parent, face);
                if (!compactComponents.TryGetValue(component, out int compact))
                {
                    compact = compactComponents.Count;
                    compactComponents.Add(component, compact);
                }
                geometry.Components[face] = compact;
                geometry.Closed[face] = closed[component] && Math.Abs(volumes[component]) > 1e-15;
                geometry.Orientation[face] = geometry.Closed[face] && volumes[component] < 0d ? -1f : 1f;
            }
            geometry.ComponentCount = compactComponents.Count;
            var unique = new HashSet<ulong>(EdgeHashComparer.Instance);
            var componentVertices = new List<ComponentVertex>(geometry.Vertices.Length);
            for (int face = 0; face < faces; ++face)
            {
                int component = geometry.Components[face];
                for (int corner = 0; corner < 3; ++corner)
                {
                    int vertex = geometry.Indices[face * 3 + corner];
                    ulong key = ((ulong)(uint)component << 32) | (uint)vertex;
                    if (unique.Add(key)) componentVertices.Add(new ComponentVertex(component, vertex));
                }
            }
            geometry.ComponentVertices = componentVertices.ToArray();
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            { parent[index] = parent[parent[index]]; index = parent[index]; }
            return index;
        }

        private static double ElapsedMilliseconds(long started) =>
            (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Snapshot snapshot in snapshots.Values) DestroyMesh(snapshot.Baked);
            snapshots.Clear(); meshes.Clear(); triangles.Clear(); triangleBounds.Clear(); presentationCopies.Clear(); physicalBody.Clear();
            triangleTree.Input.Clear(); triangleTree.Build(); closedTrees.Clear(); expiredSurfaces.Clear();
            closedComponents.Clear(); closedComponentBounds.Clear(); nearbyClosedComponents.Clear();
            closedComponentTree.Input.Clear(); closedComponentTree.Build();
            nativeSnapshot.Dispose();
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
    }
}
