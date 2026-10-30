using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bounded, approximate cloth contacts with the hero alone. Installation
    /// measures bare anatomy and primary garment shells once. Owners may opt
    /// into posing cached source skin and corrective data for the worn shell.
    /// No scene geometry, physics queries or per-frame body baking is involved.
    /// </summary>
    public sealed class PlayerScarfBodyContacts : IDisposable
    {
        private const int MaximumParts = 17;
        private const float DefaultSurfacePadding = .003f;
        private const float SplitOverlap = .015f;
        private static readonly bool WideMonoBounds = UsesWideMonoMagnitude();
        private static readonly ProfilerMarker SkinMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Skin");
        private static readonly ProfilerMarker SupportMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Support");
        private static readonly ProfilerMarker SupportLocalSkinMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Support.LocalSkin");
        private static readonly ProfilerMarker SupportBoundsMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Support.Bounds");
        private static readonly ProfilerMarker SupportNativeMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Support.Native");
        private static readonly ProfilerMarker SupportNativeExecuteMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Support.Native.Execute");
        private static readonly ProfilerMarker SupportMonoMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Support.Mono");
        private static readonly ProfilerMarker PlanesMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Planes");
        private static readonly ProfilerMarker RayMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray");
        private static readonly ProfilerMarker RayNativeExecuteMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray.Native.Execute");
        private static readonly ProfilerMarker RayNativeContainingMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray.Native.Containing");
        private static readonly ProfilerMarker RayNativeRemainingMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray.Native.Remaining");
        private static readonly ProfilerMarker RayMonoMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray.Mono");
        private static readonly ProfilerMarker RayPrepareMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray.Prepare");
        private static readonly ProfilerMarker RayWorkspaceMarker = new ProfilerMarker("BarPromenade.WeaponCarry.BodyContacts.Ray.Workspace");
        private readonly Proxy[] proxies;
        private readonly PlayerWardrobe wardrobe;
        private readonly Player3DAssetRegistry registry;
        private readonly bool includeEquipment;
        private readonly bool includeEquipmentRibbons;
        private readonly float[] escapeEntries, escapeExits, escapeDepths;
        private readonly bool[] escapeContaining;
        private readonly Vector3[] escapeNormals;
        private PlayerEquipmentContacts equipment;
        private uint poseVersion;
        internal uint ClothPoseVersion => poseVersion;
        internal uint ClothEquipmentVersion => equipment?.SnapshotVersion ?? 0u;
        private readonly Dictionary<Transform, LocalJoint> localJoints = new Dictionary<Transform, LocalJoint>();
        private readonly bool nativeQueriesEnabled;
        private NativeRayQueries nativeRayQueries;
        private NativeRayQueries retainedNativeRayQueries;
        private ulong nativeRayQueryLease;
        private bool disposed;
        internal int NativeRayWorkspaceCreateCount { get; private set; }
        internal int NativeRayBufferAllocationCount { get; private set; }
        internal bool HasRetainedRayWorkspace => retainedNativeRayQueries != null;
        private SupportWorkArena retainedSupportWork;
        internal int SupportArenaCreateCount { get; private set; }
        internal bool HasRetainedSupportWorkspace => retainedSupportWork != null;
        private static readonly int ExactRayArithmeticMode = CalibrateRayArithmetic();
        internal static int NativeArithmeticMode => ExactRayArithmeticMode;
        internal static bool NativeClothSupported => NativeArithmeticMode == 1 && WideMonoBounds;
        internal int NativeRayArithmeticMode => NativeArithmeticMode;
        internal bool NativeRayExecutedInBurst { get; private set; }
        internal bool NativeRayExecutedWithSimd { get; private set; }
        internal bool NativeNearestExecutedInBurst { get; private set; }
        internal bool NativeNearestSupported => nativeQueriesEnabled && NativeArithmeticMode == 1 && WideMonoBounds && proxies.Length <= 32;
        internal long NativeRayRunCount { get; private set; }
        internal long NativeRayVerifiedIntervalCount { get; private set; }
        internal long NativeRayOverflowCount { get; private set; }
        internal long NativeRayCertifiedMissCount { get; private set; }
        internal long NativeRayCandidateCount { get; private set; }
        internal int NativeRayMaximumCandidateCount { get; private set; }
        internal long NativeRayDirectIntervalCount { get; private set; }
        internal void ResetNativeRayDiagnostics()
        {
            NativeRayRunCount = NativeRayVerifiedIntervalCount = NativeRayOverflowCount =
                NativeRayCertifiedMissCount = NativeRayCandidateCount = 0;
            NativeRayMaximumCandidateCount = 0;
            NativeRayDirectIntervalCount = 0;
        }
        private readonly Vector3[] freeSpacePoints;
        private readonly double[] freeSpaceRadii;
        private sealed class FreeSurfaceSupport
        {
            public Proxy Proxy;
            public Envelope Envelope;
            public Plane[] Planes;
            public int PlaneIndex;
            public readonly int[] SeparatingPlaneIndices;
            public FreeSurfaceSupport(int proxyCount)
            {
                SeparatingPlaneIndices = new int[proxyCount];
                Array.Fill(SeparatingPlaneIndices, -1);
            }
        }
        private readonly Dictionary<Vector3[], FreeSurfaceSupport> freeSurfaceSupports =
            new Dictionary<Vector3[], FreeSurfaceSupport>();
        internal sealed class PointQueryCache
        {
            internal readonly PlayerScarfBodyContacts Owner;
            internal readonly int PointCount;
            internal readonly int[] PlaneIndices;
            internal readonly Plane[][] PlaneArrays;
            internal readonly object[] EnvelopeIdentities;
            internal PointQueryCache(PlayerScarfBodyContacts owner, int pointCount)
            {
                Owner = owner; PointCount = pointCount;
                PlaneIndices = new int[pointCount * owner.proxies.Length];
                PlaneArrays = new Plane[owner.proxies.Length][];
                EnvelopeIdentities = new object[owner.proxies.Length];
            }
        }
        internal PointQueryCache CreatePointQueryCache(int pointCount)
        {
            if (pointCount <= 0) throw new ArgumentOutOfRangeException(nameof(pointCount));
            return new PointQueryCache(this, pointCount);
        }

        internal bool TryGetClothSnapshotSizes(out int bodyCount, out int planeCount, out int boxCount)
        {
            bodyCount = proxies.Length; planeCount = boxCount = 0;
            if (!NativeClothSupported) return false;
            foreach (Proxy proxy in proxies)
            {
                if (!proxy.Active) continue;
                if (proxy.WorldPlanes == null || proxy.WorldPlanes.Length == 0) return false;
                foreach (Plane plane in proxy.WorldPlanes)
                    if (!Finite(plane.normal) || !float.IsFinite(plane.distance)) return false;
                if (!Finite(proxy.WorldMinimum) || !Finite(proxy.WorldMaximum)) return false;
                planeCount += proxy.WorldPlanes.Length;
            }
            boxCount = equipment == null ? 0 : equipment.BoxSnapshotCount(includeEquipmentRibbons);
            return true;

            static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        }

        internal void CopyClothSnapshot(NativeArray<PlayerJacketSurfaceJob.BodyProxy> bodies,
            NativeArray<Plane> planes, NativeArray<PlayerEquipmentContacts.BoxSnapshot> boxes)
        {
            int first = 0;
            for (int i = 0; i < proxies.Length; i++)
            {
                Proxy proxy = proxies[i];
                int count = proxy.Active ? proxy.WorldPlanes.Length : 0;
                bodies[i] = new PlayerJacketSurfaceJob.BodyProxy { Minimum = proxy.WorldMinimum,
                    Maximum = proxy.WorldMaximum, First = first, Count = count };
                if (count > 0) NativeArray<Plane>.Copy(proxy.WorldPlanes, 0, planes, first, count);
                first += count;
            }
            if (equipment != null) equipment.CopyBoxSnapshots(boxes, includeEquipmentRibbons);
        }

        internal bool TryGetClothEquipmentBounds(out Bounds bounds)
        {
            bounds = default;
            return equipment != null && equipment.TryGetSnapshotBounds(out bounds);
        }

        // These are primary shells in the production registry. Jacket coverage
        // deliberately does not hide the torso: the shirt shows through its open
        // front, but the jacket still owns the outer physical envelope.
        private static readonly Dictionary<Player3DAnatomicalPart, string[]> PrimaryGarments =
            new Dictionary<Player3DAnatomicalPart, string[]>
            {
                { Player3DAnatomicalPart.Torso, new[] { "CLO_JacketBody", "CLO_ShirtBody" } },
                { Player3DAnatomicalPart.LowerTorso, new[] { "CLO_JacketBody", "CLO_ShirtBody" } },
                { Player3DAnatomicalPart.Pelvis, new[] { "CLO_TrousersPelvis" } },
                { Player3DAnatomicalPart.LeftUpperArm, new[] { "CLO_JacketSleeve.L" } },
                { Player3DAnatomicalPart.RightUpperArm, new[] { "CLO_JacketSleeve.R" } },
                { Player3DAnatomicalPart.LeftForearm, new[] { "CLO_JacketForearm.L" } },
                { Player3DAnatomicalPart.RightForearm, new[] { "CLO_JacketForearm.R" } },
                { Player3DAnatomicalPart.LeftThigh, new[] { "CLO_TrousersThigh.L" } },
                { Player3DAnatomicalPart.RightThigh, new[] { "CLO_TrousersThigh.R" } },
                { Player3DAnatomicalPart.LeftShin, new[] { "CLO_TrousersShin.L" } },
                { Player3DAnatomicalPart.RightShin, new[] { "CLO_TrousersShin.R" } },
                { Player3DAnatomicalPart.LeftFoot, new[] { "CLO_Boot.L" } },
                { Player3DAnatomicalPart.RightFoot, new[] { "CLO_Boot.R" } }
            };

        private int supportVertexCapacity, supportNormalCapacity, supportMatrixCapacity;

        private sealed class Envelope
        {
            public Renderer Renderer;
            public string GarmentId;
            public float SurfacePadding;
            public Matrix4x4 UnitToBone;
            public Matrix4x4 BoneToUnit;
            public Vector3[] SupportNormals;
            public float[] SupportOffsets;
            public PosedMesh Skin;
            public int[] SupportVertices;
            public Vector3[] PosedVertices, NextPosedVertices;
            public bool[] ChangedVertices;
            public int[] SupportPoints;
            public VertexSupportTree SupportTree;
            public BoneLocalSkin LocalSkin;
            public Vector3 BoundsPadding;
            public Vector3 BoundsCenter, BoundsRadii, BoundsInputPadding;
            public bool HasNativeBounds;
            public bool CloudMatches;
            public uint SupportVersion;
        }

        private sealed class SupportWorkArena : IDisposable
        {
            public NativeArray<Vector3> Vertices, Normals, SourcePoints;
            public NativeArray<BoneWeight> Weights;
            public NativeArray<Matrix4x4> Matrices;
            public NativeArray<int> Order, Points, Candidates, CandidateCounts, Stack;
            public NativeArray<PlayerBodySupportJob.Node> Nodes;
            public NativeArray<bool> Changed;
            public NativeArray<float> Offsets;
            public NativeArray<double> Scores;
            public NativeArray<Plane> ScorePlanes;
            public NativeArray<int> ScoreOrder;
            public NativeArray<PlayerBodyRayJob.Node> ScoreNodes;
            public Vector3[] UploadedVertices;

            public void UploadVertices(Vector3[] source)
            {
                if (ReferenceEquals(UploadedVertices, source)) return;
                NativeArray<Vector3>.Copy(source, 0, Vertices, 0, source.Length);
                UploadedVertices = source;
            }

            public SupportWorkArena(int vertexCapacity, int nodeCapacity, int normalCapacity, int matrixCapacity = 1,
                Allocator allocator = Allocator.TempJob)
            {
                vertexCapacity = Mathf.Max(1, vertexCapacity);
                nodeCapacity = Mathf.Max(1, nodeCapacity);
                normalCapacity = Mathf.Max(1, normalCapacity);
                matrixCapacity = Mathf.Max(1, matrixCapacity);
                try
                {
                    Vertices = new NativeArray<Vector3>(vertexCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Order = new NativeArray<int>(vertexCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Nodes = new NativeArray<PlayerBodySupportJob.Node>(nodeCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Changed = new NativeArray<bool>(vertexCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Normals = new NativeArray<Vector3>(normalCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Offsets = new NativeArray<float>(normalCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Points = new NativeArray<int>(normalCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Candidates = new NativeArray<int>(normalCapacity * PlayerBodySupportJob.CandidateCapacity,
                        allocator, NativeArrayOptions.UninitializedMemory);
                    CandidateCounts = new NativeArray<int>(normalCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Stack = new NativeArray<int>(PlayerBodySupportJob.RequiredStackCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Scores = new NativeArray<double>(PlayerBodySupportJob.CandidateCapacity + PlayerBodySupportJob.RequiredStackCapacity,
                        allocator, NativeArrayOptions.UninitializedMemory);
                    SourcePoints = new NativeArray<Vector3>(vertexCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Weights = new NativeArray<BoneWeight>(vertexCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    Matrices = new NativeArray<Matrix4x4>(matrixCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    ScorePlanes = new NativeArray<Plane>(normalCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    ScoreOrder = new NativeArray<int>(normalCapacity, allocator, NativeArrayOptions.UninitializedMemory);
                    ScoreNodes = new NativeArray<PlayerBodyRayJob.Node>(normalCapacity * 2, allocator, NativeArrayOptions.UninitializedMemory);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (Vertices.IsCreated) Vertices.Dispose();
                if (Order.IsCreated) Order.Dispose();
                if (Nodes.IsCreated) Nodes.Dispose();
                if (Changed.IsCreated) Changed.Dispose();
                if (Normals.IsCreated) Normals.Dispose();
                if (Offsets.IsCreated) Offsets.Dispose();
                if (Points.IsCreated) Points.Dispose();
                if (Candidates.IsCreated) Candidates.Dispose();
                if (CandidateCounts.IsCreated) CandidateCounts.Dispose();
                if (Stack.IsCreated) Stack.Dispose();
                if (Scores.IsCreated) Scores.Dispose();
                if (SourcePoints.IsCreated) SourcePoints.Dispose();
                if (Weights.IsCreated) Weights.Dispose();
                if (Matrices.IsCreated) Matrices.Dispose();
                if (ScorePlanes.IsCreated) ScorePlanes.Dispose();
                if (ScoreOrder.IsCreated) ScoreOrder.Dispose();
                if (ScoreNodes.IsCreated) ScoreNodes.Dispose();
            }
        }


        // Spatial ordering is built once. Refitting preserves the exact live
        // source cloud, while each plane queries its true extreme vertex.
        private sealed class VertexSupportTree
        {
            private const int LeafSize = 12;
            private readonly Vector3[] vertices;
            private readonly bool[] changedVertices;
            private readonly int[] order;
            private readonly Node[] nodes;
            private readonly VertexComparer comparer;
            private int count;
            private bool hasPreviousCloud;
            private PlayerBodySupportJob.Node[] nativeTemplate;
            private int[] candidateBuffer, candidateCountBuffer;
            public int VertexCount => vertices.Length;
            public int NodeCount => count;

            private struct Node
            {
                public int First, Count, Left, Right;
                public Vector3 Minimum, Maximum;
                public Vector3 ChangedMinimum, ChangedMaximum;
                public bool Changed;
            }

            private sealed class VertexComparer : IComparer<int>
            {
                private readonly Vector3[] vertices;
                public int Axis;
                public VertexComparer(Vector3[] vertices) => this.vertices = vertices;
                public int Compare(int a, int b) => vertices[a][Axis].CompareTo(vertices[b][Axis]);
            }

            public VertexSupportTree(Vector3[] vertices, bool[] changedVertices)
            {
                this.vertices = vertices;
                this.changedVertices = changedVertices;
                order = new int[vertices.Length];
                nodes = new Node[vertices.Length * 2];
                comparer = new VertexComparer(vertices);
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Build(0, order.Length);
            }

            private int Build(int first, int length)
            {
                int index = count++;
                var node = new Node { First = first, Count = length, Left = -1, Right = -1 };
                MeasureLeaf(ref node);
                if (length > LeafSize)
                {
                    Vector3 extent = node.Maximum - node.Minimum;
                    comparer.Axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
                    Array.Sort(order, first, length, comparer);
                    int half = length / 2;
                    node.Left = Build(first, half);
                    node.Right = Build(first + half, length - half);
                }
                nodes[index] = node;
                return index;
            }

            public void Refit()
            {
                Refit(0);
                hasPreviousCloud = true;
            }

            private void Refit(int index)
            {
                Node node = nodes[index];
                if (node.Left < 0) MeasureLeaf(ref node);
                else
                {
                    Refit(node.Left); Refit(node.Right);
                    node.Minimum = Vector3.Min(nodes[node.Left].Minimum, nodes[node.Right].Minimum);
                    node.Maximum = Vector3.Max(nodes[node.Left].Maximum, nodes[node.Right].Maximum);
                    ref Node left = ref nodes[node.Left];
                    ref Node right = ref nodes[node.Right];
                    node.Changed = left.Changed || right.Changed;
                    if (left.Changed && right.Changed)
                    {
                        node.ChangedMinimum = Vector3.Min(left.ChangedMinimum, right.ChangedMinimum);
                        node.ChangedMaximum = Vector3.Max(left.ChangedMaximum, right.ChangedMaximum);
                    }
                    else if (node.Changed)
                    {
                        node.ChangedMinimum = left.Changed ? left.ChangedMinimum : right.ChangedMinimum;
                        node.ChangedMaximum = left.Changed ? left.ChangedMaximum : right.ChangedMaximum;
                    }
                }
                nodes[index] = node;
            }

            private void MeasureLeaf(ref Node node)
            {
                node.Minimum = node.Maximum = vertices[order[node.First]];
                int changedCount = changedVertices[order[node.First]] ? 1 : 0;
                for (int i = node.First + 1; i < node.First + node.Count; i++)
                {
                    Vector3 point = vertices[order[i]];
                    node.Minimum = Vector3.Min(node.Minimum, point);
                    node.Maximum = Vector3.Max(node.Maximum, point);
                    if (changedVertices[order[i]]) changedCount++;
                }
                node.Changed = changedCount > 0;
                if (changedCount == node.Count)
                {
                    node.ChangedMinimum = node.Minimum;
                    node.ChangedMaximum = node.Maximum;
                }
                else if (node.Changed)
                {
                    bool first = true;
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int vertex = order[i];
                        if (!changedVertices[vertex]) continue;
                        Vector3 point = vertices[vertex];
                        if (first) { node.ChangedMinimum = node.ChangedMaximum = point; first = false; }
                        else
                        {
                            node.ChangedMinimum = Vector3.Min(node.ChangedMinimum, point);
                            node.ChangedMaximum = Vector3.Max(node.ChangedMaximum, point);
                        }
                    }
                }
            }

            public float Maximum(Vector3 normal, ref int supportPoint)
            {
                float maximum = Vector3.Dot(normal, vertices[supportPoint]);
                // If the old extreme stayed exactly fixed, every other fixed
                // vertex still lies below it. Only moved vertices can win.
                bool changedOnly = hasPreviousCloud && !changedVertices[supportPoint];
                Search(0, UpperBound(in nodes[0], normal, changedOnly), normal, changedOnly, ref maximum, ref supportPoint);
                return maximum;
            }

            public void MaximumBatch(Vector3[] normals, float[] offsets, int[] supportPoints, SupportWorkArena arena = null)
            {
                // One pose owns the native arena; each tree retains only its
                // managed result scratch. Exact Mono dots stay outside Burst.
                if (arena == null)
                {
                    using var local = new SupportWorkArena(vertices.Length, count, normals.Length);
                    MaximumBatch(normals, offsets, supportPoints, local);
                    return;
                }
                if (vertices.Length > arena.Vertices.Length || count > arena.Nodes.Length || normals.Length > arena.Normals.Length)
                    throw new ArgumentException("Support work arena is smaller than the source cloud.");
                if (nativeTemplate == null)
                {
                    nativeTemplate = new PlayerBodySupportJob.Node[count];
                    for (int i = 0; i < count; i++)
                    {
                        ref Node node = ref nodes[i];
                        nativeTemplate[i] = new PlayerBodySupportJob.Node
                        {
                            First = node.First, Count = node.Count, Left = node.Left, Right = node.Right
                        };
                    }
                }
                int candidateLength = normals.Length * PlayerBodySupportJob.CandidateCapacity;
                if (candidateBuffer == null || candidateBuffer.Length != candidateLength)
                    candidateBuffer = new int[candidateLength];
                if (candidateCountBuffer == null || candidateCountBuffer.Length != normals.Length)
                    candidateCountBuffer = new int[normals.Length];
                using (SupportNativeMarker.Auto())
                {
                    arena.UploadVertices(vertices);
                    NativeArray<int>.Copy(order, 0, arena.Order, 0, order.Length);
                    NativeArray<PlayerBodySupportJob.Node>.Copy(nativeTemplate, 0, arena.Nodes, 0, count);
                    NativeArray<bool>.Copy(changedVertices, 0, arena.Changed, 0, changedVertices.Length);
                    NativeArray<Vector3>.Copy(normals, 0, arena.Normals, 0, normals.Length);
                    NativeArray<int>.Copy(supportPoints, 0, arena.Points, 0, normals.Length);
                    var job = new PlayerBodySupportJob
                    {
                        Vertices = arena.Vertices.GetSubArray(0, vertices.Length),
                        Order = arena.Order.GetSubArray(0, order.Length),
                        Nodes = arena.Nodes.GetSubArray(0, count),
                        ChangedVertices = arena.Changed.GetSubArray(0, changedVertices.Length),
                        Normals = arena.Normals.GetSubArray(0, normals.Length),
                        Offsets = arena.Offsets.GetSubArray(0, normals.Length),
                        SupportPoints = arena.Points.GetSubArray(0, normals.Length),
                        Stack = arena.Stack,
                        CandidateScores = arena.Scores.GetSubArray(0, PlayerBodySupportJob.CandidateCapacity),
                        StackBounds = arena.Scores.GetSubArray(PlayerBodySupportJob.CandidateCapacity, PlayerBodySupportJob.RequiredStackCapacity),
                        CandidateIndices = arena.Candidates.GetSubArray(0, candidateLength),
                        CandidateCounts = arena.CandidateCounts.GetSubArray(0, normals.Length),
                        RefitTree = true, HasPreviousCloud = hasPreviousCloud,
                        ExactArithmeticMode = NativeArithmeticMode == 1 ? 1 : 0
                    };
                    using (SupportNativeExecuteMarker.Auto())
                        PlayerBodySupportJob.RunSynchronous(ref job);
                    NativeArray<int>.Copy(arena.CandidateCounts, 0, candidateCountBuffer, 0, normals.Length);
                    bool needsReplay = false;
                    foreach (int count in candidateCountBuffer)
                        if (count != PlayerBodySupportJob.ExactResult) { needsReplay = true; break; }
                    if (needsReplay) NativeArray<int>.Copy(arena.Candidates, 0, candidateBuffer, 0, candidateLength);
                }
                using var monoProfile = SupportMonoMarker.Auto();
                for (int i = 0; i < normals.Length; i++)
                {
                    int candidateCount = candidateCountBuffer[i];
                    if (candidateCount == PlayerBodySupportJob.ExactResult)
                    {
                        offsets[i] = arena.Offsets[i];
                        supportPoints[i] = arena.Points[i];
                        continue;
                    }
                    int length = candidateCount < 0 ? vertices.Length : candidateCount;
                    float maximum = float.NegativeInfinity;
                    int winner = 0;
                    Vector3 normal = normals[i];
                    int first = i * PlayerBodySupportJob.CandidateCapacity;
                    for (int j = 0; j < length; j++)
                    {
                        int vertex = candidateCount < 0 ? j : candidateBuffer[first + j];
                        float score = Vector3.Dot(normal, vertices[vertex]);
                        if (score > maximum) { maximum = score; winner = vertex; }
                    }
                    offsets[i] = maximum;
                    supportPoints[i] = winner;
                }
                hasPreviousCloud = true;
            }

            private void Search(int index, float bound, Vector3 normal, bool changedOnly, ref float maximum, ref int supportPoint)
            {
                if (bound <= maximum) return;
                ref Node node = ref nodes[index];
                if (changedOnly && !node.Changed) return;
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int vertex = order[i];
                        if (changedOnly && !changedVertices[vertex]) continue;
                        float dot = Vector3.Dot(normal, vertices[vertex]);
                        if (dot <= maximum) continue;
                        maximum = dot; supportPoint = vertex;
                    }
                    return;
                }
                float leftBound = UpperBound(in nodes[node.Left], normal, changedOnly), rightBound = UpperBound(in nodes[node.Right], normal, changedOnly);
                if (leftBound >= rightBound)
                {
                    Search(node.Left, leftBound, normal, changedOnly, ref maximum, ref supportPoint);
                    Search(node.Right, rightBound, normal, changedOnly, ref maximum, ref supportPoint);
                }
                else
                {
                    Search(node.Right, rightBound, normal, changedOnly, ref maximum, ref supportPoint);
                    Search(node.Left, leftBound, normal, changedOnly, ref maximum, ref supportPoint);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static float UpperBound(in Node node, Vector3 normal, bool changedOnly)
            {
                // Select stored min/max coordinates directly. The dot of this
                // corner bounds every vertex component-wise, avoiding rounded
                // centre/extent reconstruction that could prune a real support.
                if (!changedOnly)
                    return Vector3.Dot(normal, new Vector3(
                        normal.x >= 0f ? node.Maximum.x : node.Minimum.x,
                        normal.y >= 0f ? node.Maximum.y : node.Minimum.y,
                        normal.z >= 0f ? node.Maximum.z : node.Minimum.z));
                if (!node.Changed) return float.NegativeInfinity;
                return Vector3.Dot(normal, new Vector3(
                    normal.x >= 0f ? node.ChangedMaximum.x : node.ChangedMinimum.x,
                    normal.y >= 0f ? node.ChangedMaximum.y : node.ChangedMinimum.y,
                    normal.z >= 0f ? node.ChangedMaximum.z : node.ChangedMinimum.z));
            }
        }

        private sealed class PosedMesh
        {
            private readonly SkinnedMeshRenderer renderer;
            private readonly Vector3[] vertices, correctedVertices;
            private readonly BoneWeight[] weights;
            private readonly Transform[] bones;
            private readonly Matrix4x4[] bindposes, matrices, boneWorldMatrices;
            private readonly int[] usedBones;
            private readonly Shape[] shapes;
            private uint version;
            private bool initialized;
            private bool worldVerticesCurrent;
            private readonly Vector3[] worldVertices;
            // Support acceleration is independent of skin/bounds arithmetic.
            // Jacket keeps its original Mono pose while sharing native support.
            public readonly bool Profile, NativePose;
            public Vector3[] WorldVertices
            {
                get
                {
                    if (!worldVerticesCurrent) RefreshWorldVertices();
                    return worldVertices;
                }
            }
            public Vector3[] CorrectedVertices => correctedVertices;
            public BoneWeight[] Weights => weights;
            public Transform[] Bones => bones;
            public Matrix4x4[] Bindposes => bindposes;
            public uint ShapeVersion { get; private set; }

            private sealed class Shape
            {
                public int Index;
                public float FrameWeight, Weight;
                public Vector3[] Delta;
                public int[] AffectedVertices;
            }

            public PosedMesh(SkinnedMeshRenderer source, Mesh mesh, bool profile, bool nativePose)
            {
                renderer = source;
                Profile = profile;
                NativePose = nativePose;
                vertices = mesh.vertices;
                correctedVertices = new Vector3[vertices.Length];
                weights = mesh.boneWeights;
                bones = source.bones;
                bindposes = mesh.bindposes;
                matrices = new Matrix4x4[bindposes.Length];
                boneWorldMatrices = new Matrix4x4[bindposes.Length];
                var used = new bool[bindposes.Length];
                foreach (BoneWeight weight in weights)
                {
                    used[weight.boneIndex0] = true;
                    if (weight.weight1 > 0f) used[weight.boneIndex1] = true;
                    if (weight.weight2 > 0f) used[weight.boneIndex2] = true;
                    if (weight.weight3 > 0f) used[weight.boneIndex3] = true;
                }
                var boneIndices = new List<int>();
                for (int i = 0; i < used.Length; i++) if (used[i]) boneIndices.Add(i);
                usedBones = boneIndices.ToArray();
                worldVertices = new Vector3[vertices.Length];
                var corrections = new List<Shape>();
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    int marker = name.IndexOf("JointVolume.", StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                        marker = name.IndexOf("TrouserKneeFold.", StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                        marker = name.IndexOf("BootToeRoll", StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                        marker = name.IndexOf("BootAnkle", StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                        marker = name.IndexOf(PlayerCorruptionAppearance.ShapeName, StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                        marker = name.IndexOf("HeroHairShort", StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.')
                        marker = name.IndexOf("HeroHairLong", StringComparison.Ordinal);
                    if (marker < 0 || marker > 0 && name[marker - 1] != '.') continue;
                    int frame = mesh.GetBlendShapeFrameCount(i) - 1;
                    var delta = new Vector3[vertices.Length];
                    mesh.GetBlendShapeFrameVertices(i, frame, delta, null, null);
                    var affected = new List<int>();
                    for (int vertex = 0; vertex < delta.Length; vertex++)
                        if (delta[vertex].x != 0f || delta[vertex].y != 0f || delta[vertex].z != 0f) affected.Add(vertex);
                    corrections.Add(new Shape { Index = i, Delta = delta,
                        AffectedVertices = affected.ToArray(), FrameWeight = mesh.GetBlendShapeFrameWeight(i, frame) });
                }
                shapes = corrections.ToArray();
            }

            public void UpdatePose(uint currentVersion)
            {
                if (version == currentVersion) return;
                version = currentVersion;
                using var profile = Profile ? SkinMarker.Auto() : default;
                bool changed = !initialized, shapesChanged = !initialized;
                foreach (int i in usedBones)
                {
                    Matrix4x4 matrix = bones[i].localToWorldMatrix;
                    if (initialized && matrix.Equals(boneWorldMatrices[i])) continue;
                    boneWorldMatrices[i] = matrix;
                    matrices[i] = matrix * bindposes[i];
                    changed = true;
                }
                foreach (Shape shape in shapes)
                {
                    float weight = renderer.GetBlendShapeWeight(shape.Index) / Mathf.Max(.00001f, shape.FrameWeight);
                    if (weight != shape.Weight) shapesChanged = true;
                    shape.Weight = weight;
                }
                if (shapesChanged)
                {
                    unchecked { ShapeVersion++; }
                    Array.Copy(vertices, correctedVertices, vertices.Length);
                    // Preserve shape addition order; zero weights and zero
                    // deltas do not require a multiply/add at every vertex.
                    foreach (Shape shape in shapes)
                    {
                        if (shape.Weight == 0f) continue;
                        foreach (int vertex in shape.AffectedVertices)
                            correctedVertices[vertex] += shape.Delta[vertex] * shape.Weight;
                    }
                    changed = true;
                }
                if (!changed) return;
                initialized = true;
                // Live support skins directly into each bone's local space.
                // Construction and the independent reference proof are the only
                // consumers of this full world cloud; defer its duplicate skin.
                worldVerticesCurrent = false;
            }

            private void RefreshWorldVertices()
            {
                using var profile = Profile ? SkinMarker.Auto() : default;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = correctedVertices[i];
                    BoneWeight weight = weights[i];
                    Vector3 posed = matrices[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0;
                    if (weight.weight1 > 0f) posed += matrices[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1;
                    if (weight.weight2 > 0f) posed += matrices[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2;
                    if (weight.weight3 > 0f) posed += matrices[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
                    worldVertices[i] = posed;
                }
                worldVerticesCurrent = true;
            }
        }

        private sealed class BoneLocalSkin
        {
            private readonly PosedMesh skin;
            private readonly int[] vertices, usedBones;
            private readonly BoneRelativePath[] paths;
            private readonly Matrix4x4[] matrices;
            private readonly BoneWeight[] ownedWeights;
            private readonly Vector3[] sourcePoints;
            private uint shapeVersion;
            private bool initialized;

            public BoneLocalSkin(PosedMesh skin, Transform target, int[] vertices, Dictionary<Transform, LocalJoint> joints)
            {
                this.skin = skin; this.vertices = vertices;
                paths = new BoneRelativePath[skin.Bones.Length];
                matrices = new Matrix4x4[skin.Bones.Length];
                ownedWeights = new BoneWeight[vertices.Length];
                sourcePoints = skin.NativePose ? new Vector3[vertices.Length] : null;
                var used = new bool[skin.Bones.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight weight = skin.Weights[vertices[i]];
                    ownedWeights[i] = weight;
                    used[weight.boneIndex0] = true;
                    if (weight.weight1 > 0f) used[weight.boneIndex1] = true;
                    if (weight.weight2 > 0f) used[weight.boneIndex2] = true;
                    if (weight.weight3 > 0f) used[weight.boneIndex3] = true;
                }
                var indices = new List<int>();
                for (int i = 0; i < used.Length; i++)
                    if (used[i]) { indices.Add(i); paths[i] = new BoneRelativePath(skin.Bones[i], target, joints); }
                usedBones = indices.ToArray();
            }

            public bool Update(Vector3[] posed, uint version, SupportWorkArena arena = null)
            {
                using var profile = skin.Profile ? SupportLocalSkinMarker.Auto() : default;
                bool shapesChanged = !initialized || shapeVersion != skin.ShapeVersion;
                bool changed = shapesChanged;
                foreach (int index in usedBones)
                {
                    if (!paths[index].Update(version)) continue;
                    matrices[index] = paths[index].Matrix * skin.Bindposes[index];
                    changed = true;
                }
                if (!changed) return false;
                if (shapesChanged && sourcePoints != null)
                    for (int i = 0; i < vertices.Length; i++) sourcePoints[i] = skin.CorrectedVertices[vertices[i]];
                if (skin.NativePose && arena != null && vertices.Length <= arena.Vertices.Length && matrices.Length <= arena.Matrices.Length)
                {
                    NativeArray<Vector3>.Copy(sourcePoints, 0, arena.SourcePoints, 0, sourcePoints.Length);
                    NativeArray<BoneWeight>.Copy(ownedWeights, 0, arena.Weights, 0, ownedWeights.Length);
                    NativeArray<Matrix4x4>.Copy(matrices, 0, arena.Matrices, 0, matrices.Length);
                    var job = new PlayerBodySkinJob
                    {
                        SourcePoints = arena.SourcePoints.GetSubArray(0, vertices.Length),
                        Weights = arena.Weights.GetSubArray(0, vertices.Length),
                        Matrices = arena.Matrices.GetSubArray(0, matrices.Length),
                        PosedVertices = arena.Vertices.GetSubArray(0, vertices.Length)
                    };
                    PlayerBodySkinJob.RunSynchronous(ref job);
                    NativeArray<Vector3>.Copy(arena.Vertices, 0, posed, 0, vertices.Length);
                    arena.UploadedVertices = posed;
                    initialized = true; shapeVersion = skin.ShapeVersion;
                    return true;
                }
                for (int i = 0; i < vertices.Length; i++)
                {
                    int vertex = vertices[i];
                    Vector3 point = skin.CorrectedVertices[vertex];
                    BoneWeight weight = ownedWeights[i];
                    Vector3 result = matrices[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0;
                    if (weight.weight1 > 0f) result += matrices[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1;
                    if (weight.weight2 > 0f) result += matrices[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2;
                    if (weight.weight3 > 0f) result += matrices[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
                    posed[i] = result;
                }
                initialized = true; shapeVersion = skin.ShapeVersion;
                return true;
            }
        }

        private sealed class LocalJoint
        {
            public readonly Transform Bone;
            public Transform Parent;
            public Matrix4x4 Matrix;
            private Vector3 position, scale;
            private Quaternion rotation;
            private uint version;
            private bool initialized, changed;

            public LocalJoint(Transform bone) => Bone = bone;

            public bool Read(uint currentVersion)
            {
                if (initialized && version == currentVersion) return changed;
                version = currentVersion;
                Parent = Bone.parent;
                Vector3 nextPosition = Bone.localPosition, nextScale = Bone.localScale;
                Quaternion nextRotation = Bone.localRotation;
                changed = !initialized || !nextPosition.Equals(position) ||
                    !nextScale.Equals(scale) || !nextRotation.Equals(rotation);
                if (changed)
                {
                    position = nextPosition; scale = nextScale; rotation = nextRotation;
                    Matrix = Matrix4x4.TRS(position, rotation, scale);
                }
                initialized = true;
                // Every path sharing this joint sees the same change for the
                // whole pose version, including readers after the first one.
                return changed;
            }
        }

        private sealed class BoneRelativePath
        {
            private readonly Transform source, target;
            private readonly Dictionary<Transform, LocalJoint> joints;
            private PathJoint[] sourcePath, targetPath;
            private bool initialized;
            public Matrix4x4 Matrix { get; private set; }

            private readonly struct PathJoint
            {
                public readonly LocalJoint Joint;
                public readonly Transform ExpectedParent;
                public PathJoint(LocalJoint joint) { Joint = joint; ExpectedParent = joint.Parent; }
            }

            public BoneRelativePath(Transform source, Transform target, Dictionary<Transform, LocalJoint> joints)
            {
                this.source = source; this.target = target; this.joints = joints;
                Rebuild(0u);
            }

            private LocalJoint ReadJoint(Transform bone, uint version)
            {
                if (!joints.TryGetValue(bone, out LocalJoint joint))
                    joints.Add(bone, joint = new LocalJoint(bone));
                joint.Read(version);
                return joint;
            }

            private void Rebuild(uint version)
            {
                Transform common = null;
                for (Transform a = source; a != null && common == null; a = ReadJoint(a, version).Parent)
                    for (Transform b = target; b != null; b = ReadJoint(b, version).Parent)
                        if (a == b) { common = a; break; }
                sourcePath = Path(source, common, version); targetPath = Path(target, common, version);
                initialized = false;
            }

            private PathJoint[] Path(Transform bone, Transform common, uint version)
            {
                var path = new List<PathJoint>();
                for (Transform current = bone; current != common && current != null;)
                {
                    LocalJoint joint = ReadJoint(current, version);
                    path.Add(new PathJoint(joint));
                    current = joint.Parent;
                }
                return path.ToArray();
            }

            public bool Update(uint version)
            {
                bool changed = !initialized;
                bool hierarchyMatches = ReadPath(sourcePath, version, ref changed);
                hierarchyMatches &= ReadPath(targetPath, version, ref changed);
                if (!hierarchyMatches) { Rebuild(version); changed = true; }
                if (!changed) return false;
                Matrix4x4 from = Matrix4x4.identity, to = Matrix4x4.identity;
                foreach (PathJoint part in sourcePath) from = part.Joint.Matrix * from;
                foreach (PathJoint part in targetPath) to = part.Joint.Matrix * to;
                // The common ancestor's world transform cancels exactly. Its
                // motion cannot invalidate an unchanged local source cloud.
                Matrix = targetPath.Length == 0 ? from : to.inverse * from;
                initialized = true;
                return true;
            }

            private static bool ReadPath(PathJoint[] path, uint version, ref bool changed)
            {
                bool hierarchyMatches = true;
                foreach (PathJoint part in path)
                {
                    changed |= part.Joint.Read(version);
                    // A shared joint's current parent must not replace this
                    // path's independently captured topology expectation.
                    hierarchyMatches &= part.Joint.Parent == part.ExpectedParent;
                }
                return hierarchyMatches;
            }
        }

        private sealed class PlaneScoreTree
        {
            private const int LeafSize = 8;
            private const double FloatError = 8d / 16777216d, DoubleError = 16d / 9007199254740992d;
            public readonly Plane[] Planes;
            private readonly PlayerBodyRayJob.Node[] nodes;
            private readonly int[] order;
            private readonly DirectionComparer comparer;
            private Vector3 origin;
            private int count;
            public bool HasNativeRefit { get; private set; }

            private struct Query
            {
                public Vector3 Point;
                public double X, Y, Z, ErrorX, ErrorY, ErrorZ;
            }

            private struct RayBounds
            {
                public float Entry, Exit;
            }

            private sealed class DirectionComparer : IComparer<int>
            {
                private readonly Vector3[] directions;
                public int Axis;
                public DirectionComparer(Vector3[] directions) => this.directions = directions;
                public int Compare(int a, int b) => directions[a][Axis].CompareTo(directions[b][Axis]);
            }

            public PlaneScoreTree(Plane[] planes, Vector3[] directions)
            {
                Planes = planes;
                nodes = new PlayerBodyRayJob.Node[planes.Length * 2]; order = new int[planes.Length];
                comparer = new DirectionComparer(directions);
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Build(0, order.Length, directions);
            }

            private int Build(int first, int length, Vector3[] directions)
            {
                int index = count++;
                var node = new PlayerBodyRayJob.Node { First = first, Count = length, Left = -1, Right = -1 };
                if (length > LeafSize)
                {
                    Vector3 minimum = directions[order[first]], maximum = minimum;
                    for (int i = first + 1; i < first + length; i++)
                    {
                        minimum = Vector3.Min(minimum, directions[order[i]]);
                        maximum = Vector3.Max(maximum, directions[order[i]]);
                    }
                    Vector3 extent = maximum - minimum;
                    comparer.Axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
                    Array.Sort(order, first, length, comparer);
                    int half = length / 2;
                    node.Left = Build(first, half, directions); node.Right = Build(first + half, length - half, directions);
                }
                nodes[index] = node;
                return index;
            }

            public void Refit(Vector3 centre) { origin = centre; Refit(0); }

            public void RefitNative(Vector3 centre, SupportWorkArena arena, bool planesReady = false)
            {
                if (arena.ScorePlanes.Length < Planes.Length || arena.ScoreOrder.Length < order.Length || arena.ScoreNodes.Length < count)
                    throw new ArgumentException("Plane refit arena does not fit the live search tree.", nameof(arena));
                origin = centre;
                if (!planesReady) NativeArray<Plane>.Copy(Planes, 0, arena.ScorePlanes, 0, Planes.Length);
                NativeArray<int>.Copy(order, 0, arena.ScoreOrder, 0, order.Length);
                NativeArray<PlayerBodyRayJob.Node>.Copy(nodes, 0, arena.ScoreNodes, 0, count);
                var job = new PlayerBodyPlaneRefitJob
                {
                    Planes = arena.ScorePlanes.GetSubArray(0, Planes.Length),
                    Order = arena.ScoreOrder.GetSubArray(0, order.Length),
                    Nodes = arena.ScoreNodes.GetSubArray(0, count), Origin = centre
                };
                PlayerBodyPlaneRefitJob.RunSynchronous(ref job);
                NativeArray<PlayerBodyRayJob.Node>.Copy(arena.ScoreNodes, 0, nodes, 0, count);
                HasNativeRefit = true;
            }

            public int NodeCount => count;

            public Vector3 CopyRayTree(NativeArray<PlayerBodyRayJob.Node> destination, int first,
                NativeArray<int> planeOrder, int orderFirst)
            {
                NativeArray<int>.Copy(order, 0, planeOrder, orderFirst, order.Length);
                NativeArray<PlayerBodyRayJob.Node>.Copy(nodes, 0, destination, first, count);
                return origin;
            }

            private void Refit(int index)
            {
                ref PlayerBodyRayJob.Node node = ref nodes[index];
                if (node.Left >= 0)
                {
                    Refit(node.Left); Refit(node.Right);
                    ref PlayerBodyRayJob.Node left = ref nodes[node.Left]; ref PlayerBodyRayJob.Node right = ref nodes[node.Right];
                    node.Minimum = Vector3.Min(left.Minimum, right.Minimum);
                    node.Maximum = Vector3.Max(left.Maximum, right.Maximum);
                    node.Absolute = Vector3.Max(left.Absolute, right.Absolute);
                    node.MaximumCentredOffset = Math.Max(left.MaximumCentredOffset, right.MaximumCentredOffset);
                    node.MaximumAbsoluteDistance = Math.Max(left.MaximumAbsoluteDistance, right.MaximumAbsoluteDistance);
                    return;
                }
                node.Minimum = node.Maximum = Planes[order[node.First]].normal;
                node.MaximumCentredOffset = double.NegativeInfinity;
                node.MaximumAbsoluteDistance = 0d;
                for (int i = node.First; i < node.First + node.Count; i++)
                {
                    Plane plane = Planes[order[i]];
                    Vector3 normal = plane.normal;
                    node.Minimum = Vector3.Min(node.Minimum, normal); node.Maximum = Vector3.Max(node.Maximum, normal);
                    double a = (double)normal.x * origin.x, b = (double)normal.y * origin.y, c = (double)normal.z * origin.z;
                    double error = DoubleError * (Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(plane.distance));
                    node.MaximumCentredOffset = Math.Max(node.MaximumCentredOffset, a + b + c + plane.distance + error);
                    node.MaximumAbsoluteDistance = Math.Max(node.MaximumAbsoluteDistance, Math.Abs((double)plane.distance));
                }
                node.Absolute = new Vector3(
                    Mathf.Max(Mathf.Abs(node.Minimum.x), Mathf.Abs(node.Maximum.x)),
                    Mathf.Max(Mathf.Abs(node.Minimum.y), Mathf.Abs(node.Maximum.y)),
                    Mathf.Max(Mathf.Abs(node.Minimum.z), Mathf.Abs(node.Maximum.z)));
            }

            private Query At(Vector3 point)
            {
                double x = (double)point.x - origin.x, y = (double)point.y - origin.y, z = (double)point.z - origin.z;
                return new Query
                {
                    Point = point, X = x, Y = y, Z = z,
                    ErrorX = FloatError * Math.Abs((double)point.x) + DoubleError * (Math.Abs(x) + Math.Abs((double)point.x) + Math.Abs((double)origin.x)),
                    ErrorY = FloatError * Math.Abs((double)point.y) + DoubleError * (Math.Abs(y) + Math.Abs((double)point.y) + Math.Abs((double)origin.y)),
                    ErrorZ = FloatError * Math.Abs((double)point.z) + DoubleError * (Math.Abs(z) + Math.Abs((double)point.z) + Math.Abs((double)origin.z))
                };
            }

            private static double UpperBound(in PlayerBodyRayJob.Node node, in Query query)
            {
                double a = (query.X >= 0d ? node.Maximum.x : node.Minimum.x) * query.X;
                double b = (query.Y >= 0d ? node.Maximum.y : node.Minimum.y) * query.Y;
                double c = (query.Z >= 0d ? node.Maximum.z : node.Minimum.z) * query.Z;
                // Six single-precision multiply/add operations produce the
                // original Plane distance. Bound their rounding conservatively
                // while centred double coefficients avoid world-origin loss.
                double error = node.Absolute.x * query.ErrorX + node.Absolute.y * query.ErrorY + node.Absolute.z * query.ErrorZ +
                    (FloatError + DoubleError) * node.MaximumAbsoluteDistance + DoubleError * Math.Abs(node.MaximumCentredOffset);
                return a + b + c + node.MaximumCentredOffset + error;
            }

            public bool NearestInside(Vector3 point, ref int planeIndex, out float distance)
            {
                Query query = At(point);
                distance = Planes[planeIndex].GetDistanceToPoint(point);
                if (distance >= 0f) return false;
                return !SearchNearest(0, UpperBound(in nodes[0], in query), in query, ref planeIndex, ref distance);
            }

            private bool SearchNearest(int index, double bound, in Query query, ref int winner, ref float distance)
            {
                if (bound < distance) return false;
                ref PlayerBodyRayJob.Node node = ref nodes[index];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int plane = order[i];
                        float score = Planes[plane].GetDistanceToPoint(query.Point);
                        if (score >= 0f) { winner = plane; distance = score; return true; }
                        if (score > distance || score == distance && plane < winner) { winner = plane; distance = score; }
                    }
                    return false;
                }
                double left = UpperBound(in nodes[node.Left], in query), right = UpperBound(in nodes[node.Right], in query);
                if (left >= right)
                    return SearchNearest(node.Left, left, in query, ref winner, ref distance) ||
                        SearchNearest(node.Right, right, in query, ref winner, ref distance);
                return SearchNearest(node.Right, right, in query, ref winner, ref distance) ||
                    SearchNearest(node.Left, left, in query, ref winner, ref distance);
            }

            public bool Contains(Vector3 point, ref int separatingPlane)
                => ContainsAtThreshold(point, ref separatingPlane, 0f);

            public bool ContainsAtThreshold(Vector3 point, ref int separatingPlane, float threshold)
            {
                Query query = At(point);
                return !SearchSeparation(0, UpperBound(in nodes[0], in query), in query, ref separatingPlane, threshold);
            }

            private bool SearchSeparation(int index, double bound, in Query query, ref int separatingPlane, float threshold)
            {
                // The bound encloses the original float plane distances. A
                // shifted clearance gate changes only the comparison, never
                // the posed planes or the leaf arithmetic.
                if (bound < threshold) return false;
                ref PlayerBodyRayJob.Node node = ref nodes[index];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                        if (Planes[order[i]].GetDistanceToPoint(query.Point) >= threshold) { separatingPlane = order[i]; return true; }
                    return false;
                }
                double left = UpperBound(in nodes[node.Left], in query), right = UpperBound(in nodes[node.Right], in query);
                if (left >= right)
                    return SearchSeparation(node.Left, left, in query, ref separatingPlane, threshold) ||
                        SearchSeparation(node.Right, right, in query, ref separatingPlane, threshold);
                return SearchSeparation(node.Right, right, in query, ref separatingPlane, threshold) ||
                    SearchSeparation(node.Left, left, in query, ref separatingPlane, threshold);
            }

            // The caller already certified every native plane distance is
            // strictly negative. Positive and negative crossings can then be
            // bounded independently without changing the original interval.
            public bool RayFromInside(Vector3 point, Vector3 direction, int seedPlane, out float entry, out float exit)
            {
                entry = float.NegativeInfinity; exit = float.PositiveInfinity;
                if (seedPlane >= 0 && seedPlane < Planes.Length && !ClipRay(seedPlane, point, direction, ref entry, ref exit)) return false;
                Query query = At(point);
                return SearchRay(0, BoundRay(in nodes[0], in query, direction), in query, direction, ref entry, ref exit) && exit > 0f;
            }

            private static RayBounds BoundRay(in PlayerBodyRayJob.Node node, in Query query, Vector3 direction)
            {
                // Identical float dot arithmetic is monotone in each selected
                // normal component, so these corners bound every leaf slope.
                float maximumSlope = Vector3.Dot(direction, new Vector3(
                    direction.x >= 0f ? node.Maximum.x : node.Minimum.x,
                    direction.y >= 0f ? node.Maximum.y : node.Minimum.y,
                    direction.z >= 0f ? node.Maximum.z : node.Minimum.z));
                float minimumSlope = Vector3.Dot(direction, new Vector3(
                    direction.x >= 0f ? node.Minimum.x : node.Maximum.x,
                    direction.y >= 0f ? node.Minimum.y : node.Maximum.y,
                    direction.z >= 0f ? node.Minimum.z : node.Maximum.z));
                bool positive = maximumSlope >= .0000001f, negative = minimumSlope <= -.0000001f;
                var bounds = new RayBounds
                {
                    Entry = negative ? 0f : float.NegativeInfinity,
                    Exit = positive ? 0f : float.PositiveInfinity
                };
                if (!positive && !negative) return bounds;
                float maximumDistance = (float)UpperBound(in node, in query);
                if (maximumDistance < 0f)
                {
                    // Rounded float division stays monotone. A negative
                    // upper distance gives the nearest possible positive
                    // crossing and the latest possible negative crossing.
                    if (positive) bounds.Exit = -maximumDistance / maximumSlope;
                    if (negative) bounds.Entry = -maximumDistance / minimumSlope;
                }
                return bounds;
            }

            private bool SearchRay(int index, RayBounds bound, in Query query, Vector3 direction, ref float entry, ref float exit)
            {
                if (bound.Exit >= exit && bound.Entry <= entry) return true;
                ref PlayerBodyRayJob.Node node = ref nodes[index];
                if (node.Left < 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                        if (!ClipRay(order[i], query.Point, direction, ref entry, ref exit)) return false;
                    return true;
                }
                RayBounds left = BoundRay(in nodes[node.Left], in query, direction);
                RayBounds right = BoundRay(in nodes[node.Right], in query, direction);
                float leftFirst = Mathf.Min(left.Exit, -left.Entry), rightFirst = Mathf.Min(right.Exit, -right.Entry);
                if (leftFirst <= rightFirst)
                    return SearchRay(node.Left, left, in query, direction, ref entry, ref exit) &&
                        SearchRay(node.Right, right, in query, direction, ref entry, ref exit);
                return SearchRay(node.Right, right, in query, direction, ref entry, ref exit) &&
                    SearchRay(node.Left, left, in query, direction, ref entry, ref exit);
            }

            private bool ClipRay(int index, Vector3 point, Vector3 direction, ref float entry, ref float exit)
            {
                Plane plane = Planes[index];
                float distance = plane.GetDistanceToPoint(point), slope = Vector3.Dot(plane.normal, direction);
                if (Mathf.Abs(slope) < .0000001f) return distance <= 0f;
                float crossing = -distance / slope;
                if (slope > 0f) exit = Mathf.Min(exit, crossing);
                else entry = Mathf.Max(entry, crossing);
                return entry <= exit;
            }
        }

        private sealed class Proxy
        {
            public Player3DAnatomicalPart Part;
            public Transform Bone;
            public Envelope Bare;
            public Envelope[] Clothing;
            public Envelope Selected;
            public Matrix4x4 UnitToWorld;
            public Matrix4x4 WorldToUnit;
            public Bounds WorldBounds;
            public Vector3 WorldMinimum, WorldMaximum;
            public Vector3 CenterEscape;
            public float MinimumWorldRadius;
            public bool Active;
            public Plane[] WorldPlanes;
            public int SeparatingPlane;
            public int[] SeparatingPlanes;
            public PlaneScoreTree ScoreTree;
            public Matrix4x4 RayWorldToBone;
            public Vector3 RayMinimum, RayMaximum;
            public Envelope PlaneEnvelope;
            public Matrix4x4 PlaneBoneToWorld;
            public uint PlaneSupportVersion;
            public Envelope NormalEnvelope;
            public Matrix4x4 NormalMatrix;
            public Vector3[] OffsetNormals, PlaneNormals;
            public Matrix4x4 PlaneNormalMatrix;
        }

        public int Count => proxies.Length;
        /// <summary>Number of vertices corrected by the most recent Resolve.</summary>
        public int LastContactCount { get; private set; }

        /// <summary>The measured shell currently supplying one anatomy's contact envelope.</summary>
        public Renderer EnvelopeRenderer(Player3DAnatomicalPart part)
        {
            foreach (Proxy proxy in proxies)
                if (proxy.Part == part) return proxy.Selected?.Renderer;
            return null;
        }

        public PlayerScarfBodyContacts(Player3DAssetRegistry registry, string excludedGarmentSlot = null,
            bool useMeshSupportPlanes = false, bool includeEquipment = true, bool poseSourceSkin = false,
            int pointQuerySlots = 0, bool includeEquipmentRibbons = true, float jacketSurfacePadding = DefaultSurfacePadding)
        {
            this.registry = registry;
            this.includeEquipment = includeEquipment;
            this.includeEquipmentRibbons = includeEquipmentRibbons;
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (pointQuerySlots < 0) throw new ArgumentOutOfRangeException(nameof(pointQuerySlots));
            if (!(jacketSurfacePadding > 0f) || float.IsInfinity(jacketSurfacePadding))
                throw new ArgumentOutOfRangeException(nameof(jacketSurfacePadding));
            nativeQueriesEnabled = pointQuerySlots > 0;
            freeSpacePoints = new Vector3[pointQuerySlots];
            freeSpaceRadii = new double[pointQuerySlots];
            wardrobe = registry.GetComponent<PlayerWardrobe>();
            PlayerJacketCloth jacket = poseSourceSkin ? registry.GetComponent<PlayerJacketCloth>() : null;
            var garmentIds = new Dictionary<Renderer, string>();
            if (wardrobe != null && wardrobe.IsConfigured)
                foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                {
                    if (garment.Slot == excludedGarmentSlot) continue;
                    foreach (Renderer renderer in garment.Renderers)
                        if (renderer != null) garmentIds[renderer] = garment.Id;
                }
            var meshBindings = new Dictionary<string, Renderer>(StringComparer.Ordinal);
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
                if (binding != null && binding.Renderer != null && !string.IsNullOrEmpty(binding.MeshName))
                    meshBindings[binding.MeshName] = binding.Renderer;
            var parts = new List<Player3DAnatomicalPartBinding>(MaximumParts);
            var seenParts = new HashSet<Player3DAnatomicalPart>();
            foreach (Player3DAnatomicalPartBinding part in registry.AnatomicalParts)
            {
                if (part == null || part.Renderer == null || part.Bone == null ||
                    parts.Count >= MaximumParts || !seenParts.Add(part.Part)) continue;
                parts.Add(part);
            }

            // Production V2 has one continuous torso renderer and a separate
            // spine anchor, rather than a serialized LowerTorso renderer.
            if (!seenParts.Contains(Player3DAnatomicalPart.LowerTorso) &&
                parts.Count < MaximumParts && registry.Anchors.Spine != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].Part != Player3DAnatomicalPart.Torso ||
                        parts[i].Bone == registry.Anchors.Spine) continue;
                    parts.Add(new Player3DAnatomicalPartBinding(
                        Player3DAnatomicalPart.LowerTorso, parts[i].Renderer,
                        registry.Anchors.Spine));
                    break;
                }
            }

            var measured = new Dictionary<Renderer, Vector3[]>();
            var posedMeshes = new Dictionary<Renderer, PosedMesh>();
            var built = new List<Proxy>(parts.Count);
            var scratch = new Mesh { name = "Scarf Body Measurement", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Vector3[] MeasureOnce(Renderer renderer)
                {
                    if (!measured.TryGetValue(renderer, out Vector3[] vertices))
                    {
                        PosedMesh skin = poseSourceSkin ? CacheSkin(renderer) : null;
                        if (skin != null)
                        {
                            // The first support directions and owned indices
                            // use the same immutable source as later updates.
                            // Sampling cloth output here would retain feedback
                            // even if subsequent skinning avoided that output.
                            skin.UpdatePose(1u);
                            vertices = skin.WorldVertices;
                        }
                        else vertices = MeasureWorldVertices(renderer, scratch);
                        measured.Add(renderer, vertices);
                    }
                    return vertices;
                }
                PosedMesh CacheSkin(Renderer renderer)
                {
                    // Hair can contact the live, cloth-owned jacket mesh; its
                    // existing measured envelopes must not use that mesh's
                    // initial vertices as an immutable skin source.
                    if (!useMeshSupportPlanes || (!poseSourceSkin && excludedGarmentSlot != "jacket") ||
                        !(renderer is SkinnedMeshRenderer skin)) return null;
                    Mesh mesh = skin.sharedMesh;
                    if (poseSourceSkin && jacket != null)
                        for (int i = 0; i < jacket.SurfaceCount; i++)
                        {
                            Mesh authored = jacket.SourceMesh(i);
                            if (authored != mesh && jacket.DeformedMesh(i) != mesh) continue;
                            mesh = authored;
                            break;
                        }
                    if (mesh == null || !mesh.isReadable) return null;
                    if (!posedMeshes.TryGetValue(renderer, out PosedMesh cached))
                    {
                        // Jacket contacts already pose their immutable body
                        // sources. Use the same ordered native support path as
                        // the sling without changing which mesh is sampled.
                        cached = new PosedMesh(skin, mesh, poseSourceSkin ||
                            nativeQueriesEnabled && excludedGarmentSlot == "jacket", nativePose: poseSourceSkin);
                        posedMeshes.Add(renderer, cached);
                    }
                    return cached;
                }
                foreach (Player3DAnatomicalPartBinding part in parts)
                {
                    Envelope bare = BuildEnvelope(part, parts, part.Renderer, MeasureOnce(part.Renderer),
                        useMeshSupportPlanes, CacheSkin(part.Renderer), localJoints, DefaultSurfacePadding);
                    if (bare == null) continue;
                    var clothing = new List<Envelope>(2);
                    if (wardrobe != null && wardrobe.IsConfigured &&
                        PrimaryGarments.TryGetValue(part.Part, out string[] candidates))
                    {
                        foreach (string name in candidates)
                        {
                            if (!meshBindings.TryGetValue(name, out Renderer renderer) ||
                                !garmentIds.TryGetValue(renderer, out string garmentId)) continue;
                            Envelope envelope = BuildEnvelope(part, parts, renderer, MeasureOnce(renderer),
                                useMeshSupportPlanes, CacheSkin(renderer), localJoints,
                                name == "CLO_JacketBody" ? jacketSurfacePadding : DefaultSurfacePadding);
                            if (envelope != null) { envelope.GarmentId = garmentId; clothing.Add(envelope); }
                        }
                    }
                    built.Add(new Proxy { Part = part.Part, Bone = part.Bone, Bare = bare, Clothing = clothing.ToArray(),
                        SeparatingPlanes = pointQuerySlots > 0 ? new int[pointQuerySlots] : null });
                }
            }
            finally
            {
                PlayerScarfResources.DestroyOwned(scratch);
            }
            proxies = built.ToArray();
            foreach (Proxy proxy in proxies)
            {
                MeasureSupportCapacity(proxy.Bare);
                int normalCapacity = proxy.Bare.SupportNormals?.Length ?? 0;
                foreach (Envelope envelope in proxy.Clothing)
                {
                    MeasureSupportCapacity(envelope);
                    normalCapacity = Math.Max(normalCapacity, envelope.SupportNormals?.Length ?? 0);
                }
                proxy.OffsetNormals = normalCapacity > 0 ? new Vector3[normalCapacity] : Array.Empty<Vector3>();
                proxy.PlaneNormals = normalCapacity > 0 ? new Vector3[normalCapacity] : Array.Empty<Vector3>();
            }
            escapeEntries = new float[proxies.Length];
            escapeExits = new float[proxies.Length];
            escapeContaining = new bool[proxies.Length];
            escapeDepths = new float[proxies.Length];
            escapeNormals = new Vector3[proxies.Length];
            try { UpdatePose(); }
            catch { Dispose(); throw; }

            void MeasureSupportCapacity(Envelope envelope)
            {
                if (!nativeQueriesEnabled || envelope.Skin?.Profile != true || envelope.PosedVertices == null) return;
                supportVertexCapacity = Math.Max(supportVertexCapacity, envelope.PosedVertices.Length);
                supportNormalCapacity = Math.Max(supportNormalCapacity, envelope.SupportNormals?.Length ?? 0);
                supportMatrixCapacity = Math.Max(supportMatrixCapacity,
                    Math.Max(envelope.Skin.Bindposes.Length, envelope.Skin.Bones.Length));
            }
        }

        public void UpdatePose()
        {
            Array.Clear(freeSpaceRadii, 0, freeSpaceRadii.Length);
            if (includeEquipment && equipment == null && registry != null) equipment = registry.GetComponent<PlayerEquipmentContacts>();
            equipment?.UpdatePose();
            unchecked { poseVersion++; }
            SupportWorkArena supportWork = RetainSupportWorkspace();
            // The buffers persist, but a mutable cloud upload belongs only to
            // this pose. Envelope updates invalidate it again before skinning.
            if (supportWork != null) supportWork.UploadedVertices = null;
            foreach (Proxy proxy in proxies)
            {
                proxy.Active = proxy.Bone != null;
                if (!proxy.Active) continue;
                proxy.Selected = proxy.Bare;
                if (wardrobe != null)
                    foreach (Envelope envelope in proxy.Clothing)
                    {
                        if (envelope.Renderer == null || !wardrobe.IsEquipped(envelope.GarmentId)) continue;
                        // A camera can hide dressed geometry without removing it.
                        // An owned appearance may temporarily undress the same selection.
                        if (wardrobe.HasAppearanceLease && !envelope.Renderer.enabled) continue;
                        proxy.Selected = envelope;
                        break;
                    }
                Matrix4x4 boneToWorld = proxy.Bone.localToWorldMatrix;
                Matrix4x4 worldToBone = proxy.Bone.worldToLocalMatrix;
                float surfacePadding = proxy.Selected.SurfacePadding;
                RefreshSupportEnvelope(proxy.Selected, worldToBone, boneToWorld, poseVersion, supportWork);
                proxy.UnitToWorld = boneToWorld * proxy.Selected.UnitToBone;
                proxy.WorldToUnit = proxy.Selected.BoneToUnit * worldToBone;
                Matrix4x4 matrix = proxy.UnitToWorld;
                // The row lengths are the exact AABB extents of a transformed
                // unit sphere, including rotated/non-uniform imported scales.
                var extent = new Vector3(
                    Mathf.Sqrt(matrix.m00 * matrix.m00 + matrix.m01 * matrix.m01 + matrix.m02 * matrix.m02),
                    Mathf.Sqrt(matrix.m10 * matrix.m10 + matrix.m11 * matrix.m11 + matrix.m12 * matrix.m12),
                    Mathf.Sqrt(matrix.m20 * matrix.m20 + matrix.m21 * matrix.m21 + matrix.m22 * matrix.m22));
                proxy.WorldBounds = new Bounds(matrix.MultiplyPoint3x4(Vector3.zero), extent * 2f);
                proxy.WorldMinimum = proxy.WorldBounds.min;
                proxy.WorldMaximum = proxy.WorldBounds.max;
                float x = matrix.MultiplyVector(Vector3.right).magnitude;
                float y = matrix.MultiplyVector(Vector3.up).magnitude;
                float z = matrix.MultiplyVector(Vector3.forward).magnitude;
                proxy.MinimumWorldRadius = Mathf.Max(.00001f, Mathf.Min(x, Mathf.Min(y, z)));
                proxy.CenterEscape = x <= y && x <= z ? Vector3.right : y <= z ? Vector3.up : Vector3.forward;
                if (proxy.Selected.SupportNormals != null)
                {
                    Matrix4x4 normalMatrix = worldToBone.transpose;
                    // Every input is unchanged: retain the exact world planes
                    // and search bounds for this stationary anatomy.
                    if (nativeQueriesEnabled && proxy.WorldPlanes != null &&
                        ReferenceEquals(proxy.PlaneEnvelope, proxy.Selected) &&
                        proxy.PlaneSupportVersion == proxy.Selected.SupportVersion &&
                        proxy.PlaneBoneToWorld.Equals(boneToWorld) &&
                        SameLinearMatrixBits(proxy.PlaneNormalMatrix, normalMatrix)) continue;
                    using var profile = proxy.Selected.Skin?.Profile == true ? PlanesMarker.Auto() : default;
                    int count = proxy.Selected.SupportNormals.Length;
                    if (proxy.WorldPlanes == null || proxy.WorldPlanes.Length != count) proxy.WorldPlanes = new Plane[count];
                    // The first six support planes are the bone-local axes.
                    // Their expanded box bounds the entire plane hull, unlike
                    // an ellipsoid fitted to its source vertex cloud.
                    Vector3 rayPadding = new Vector3(
                        new Vector3(worldToBone.m00, worldToBone.m01, worldToBone.m02).magnitude,
                        new Vector3(worldToBone.m10, worldToBone.m11, worldToBone.m12).magnitude,
                        new Vector3(worldToBone.m20, worldToBone.m21, worldToBone.m22).magnitude) * (surfacePadding + .0001f);
                    float[] offsets = proxy.Selected.SupportOffsets;
                    proxy.RayWorldToBone = worldToBone;
                    proxy.RayMinimum = new Vector3(-offsets[1], -offsets[3], -offsets[5]) - rayPadding;
                    proxy.RayMaximum = new Vector3(offsets[0], offsets[2], offsets[4]) + rayPadding;
                    bool generatedNative = nativeQueriesEnabled && proxy.Selected.Skin?.Profile == true &&
                        NativeArithmeticMode == 1 && WideMonoBounds && supportWork != null &&
                        TryGenerateWorldPlanes(proxy.Selected, boneToWorld, normalMatrix, supportWork);
                    if (!generatedNative)
                    {
                        bool normalsUnchanged = ReferenceEquals(proxy.NormalEnvelope, proxy.Selected) &&
                            SameLinearMatrixBits(proxy.NormalMatrix, normalMatrix);
                        if (!normalsUnchanged)
                        {
                            // Plane normalizes the already normalized offset direction
                            // again; cache both stages to retain its exact output bits.
                            for (int i = 0; i < count; i++)
                            {
                                Vector3 normal = normalMatrix.MultiplyVector(proxy.Selected.SupportNormals[i]).normalized;
                                proxy.OffsetNormals[i] = normal;
                                proxy.PlaneNormals[i] = Vector3.Normalize(normal);
                            }
                            proxy.NormalEnvelope = proxy.Selected;
                            proxy.NormalMatrix = normalMatrix;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            Vector3 normal = proxy.OffsetNormals[i];
                            Vector3 support = boneToWorld.MultiplyPoint3x4(proxy.Selected.SupportNormals[i] * proxy.Selected.SupportOffsets[i]);
                            Vector3 point = support + normal * surfacePadding;
                            Vector3 planeNormal = proxy.PlaneNormals[i];
                            proxy.WorldPlanes[i] = new Plane { normal = planeNormal, distance = -Vector3.Dot(planeNormal, point) };
                        }
                    }
                    if (proxy.SeparatingPlanes != null)
                    {
                        if (proxy.ScoreTree == null || !ReferenceEquals(proxy.ScoreTree.Planes, proxy.WorldPlanes))
                            proxy.ScoreTree = new PlaneScoreTree(proxy.WorldPlanes, proxy.Selected.SupportNormals);
                        if (nativeQueriesEnabled && proxy.Selected.Skin?.Profile == true && supportWork != null)
                            proxy.ScoreTree.RefitNative(proxy.WorldBounds.center, supportWork, generatedNative);
                        else proxy.ScoreTree.Refit(proxy.WorldBounds.center);
                    }
                    if (generatedNative) NativeArray<Plane>.Copy(supportWork.ScorePlanes, 0, proxy.WorldPlanes, 0, count);
                    proxy.PlaneEnvelope = proxy.Selected;
                    proxy.PlaneBoneToWorld = boneToWorld;
                    proxy.PlaneSupportVersion = proxy.Selected.SupportVersion;
                    proxy.PlaneNormalMatrix = normalMatrix;
                }
                else { proxy.WorldPlanes = null; proxy.ScoreTree = null; }
            }
        }

        private SupportWorkArena RetainSupportWorkspace()
        {
            if (supportVertexCapacity <= 0) return null;
            // Every bare/equipped envelope was measured at construction. Their
            // immutable topology fixes the owner's maximum required capacity.
            if (retainedSupportWork == null)
            {
                retainedSupportWork = new SupportWorkArena(supportVertexCapacity, supportVertexCapacity * 2,
                    supportNormalCapacity, supportMatrixCapacity, Allocator.Persistent);
                SupportArenaCreateCount++;
            }
            return retainedSupportWork;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            nativeRayQueries = null;
            retainedNativeRayQueries?.Dispose();
            retainedNativeRayQueries = null;
            retainedSupportWork?.Dispose();
            retainedSupportWork = null;
        }

        private static bool TryGenerateWorldPlanes(Envelope envelope, Matrix4x4 boneToWorld,
            Matrix4x4 normalMatrix, SupportWorkArena arena)
        {
            int count = envelope.SupportNormals.Length;
            if (count > arena.Normals.Length || count > arena.Offsets.Length || count > arena.ScorePlanes.Length ||
                envelope.SupportOffsets == null || envelope.SupportOffsets.Length != count) return false;
            NativeArray<Vector3>.Copy(envelope.SupportNormals, 0, arena.Normals, 0, count);
            NativeArray<float>.Copy(envelope.SupportOffsets, 0, arena.Offsets, 0, count);
            var job = new PlayerBodyWorldPlaneJob
            {
                Normals = arena.Normals.GetSubArray(0, count),
                Offsets = arena.Offsets.GetSubArray(0, count),
                Planes = arena.ScorePlanes.GetSubArray(0, count),
                NormalMatrix = normalMatrix, BoneToWorld = boneToWorld,
                SurfacePadding = envelope.SurfacePadding
            };
            PlayerBodyWorldPlaneJob.RunSynchronous(ref job);
            return job.Completed == 1;
        }

        private static bool SameLinearMatrixBits(Matrix4x4 left, Matrix4x4 right) =>
            BitConverter.SingleToInt32Bits(left.m00) == BitConverter.SingleToInt32Bits(right.m00) &&
            BitConverter.SingleToInt32Bits(left.m01) == BitConverter.SingleToInt32Bits(right.m01) &&
            BitConverter.SingleToInt32Bits(left.m02) == BitConverter.SingleToInt32Bits(right.m02) &&
            BitConverter.SingleToInt32Bits(left.m10) == BitConverter.SingleToInt32Bits(right.m10) &&
            BitConverter.SingleToInt32Bits(left.m11) == BitConverter.SingleToInt32Bits(right.m11) &&
            BitConverter.SingleToInt32Bits(left.m12) == BitConverter.SingleToInt32Bits(right.m12) &&
            BitConverter.SingleToInt32Bits(left.m20) == BitConverter.SingleToInt32Bits(right.m20) &&
            BitConverter.SingleToInt32Bits(left.m21) == BitConverter.SingleToInt32Bits(right.m21) &&
            BitConverter.SingleToInt32Bits(left.m22) == BitConverter.SingleToInt32Bits(right.m22);

        public void Resolve(Vector3[] worldVertices, float[] freedom = null)
        {
            if (worldVertices == null) throw new ArgumentNullException(nameof(worldVertices));
            if (freedom != null && freedom.Length != worldVertices.Length)
                throw new ArgumentException("Scarf freedom must match the vertex count.", nameof(freedom));
            LastContactCount = 0;
            for (int i = 0; i < worldVertices.Length; i++)
            {
                if (freedom != null && freedom[i] <= 0f) continue;
                Vector3 point = worldVertices[i];
                if (ResolvePoint(ref point))
                {
                    worldVertices[i] = point;
                    LastContactCount++;
                }
            }
        }

        // Area constraints query their current point after each adjacent
        // triangle correction, rather than a stale batch from the last pass.
        internal bool ResolvePoint(ref Vector3 point, Player3DAnatomicalPart? excludedPart = null)
            => ResolvePointCore(ref point, excludedPart, null, 0);

        internal bool ResolvePointCached(ref Vector3 point, PointQueryCache cache, int pointIndex,
            Player3DAnatomicalPart? excludedPart = null)
        {
            if (cache == null || !ReferenceEquals(cache.Owner, this)) throw new ArgumentException("Point cache must belong to these contacts.", nameof(cache));
            if ((uint)pointIndex >= cache.PointCount) throw new ArgumentOutOfRangeException(nameof(pointIndex));
            return ResolvePointCore(ref point, excludedPart, cache, pointIndex);
        }

        private bool ResolvePointCore(ref Vector3 point, Player3DAnatomicalPart? excludedPart,
            PointQueryCache cache, int pointIndex)
        {
            bool corrected = false;
            for (int pass = 0; pass < 2; pass++)
            {
                bool moved = false;
                for (int proxyIndex = 0; proxyIndex < proxies.Length; proxyIndex++)
                {
                    Proxy proxy = proxies[proxyIndex];
                    // A gripping palm's convex envelope includes the open
                    // finger cavity. The grasp owner may exempt that part
                    // locally while retaining every other body contact.
                    if (excludedPart.HasValue && proxy.Part == excludedPart.Value) continue;
                    // Hair/cloth ask this thousands of times per pose. The
                    // same inclusive AABB test stays managed, with bounds
                    // refreshed above rather than native calls per vertex.
                    if (!proxy.Active || !(point.x >= proxy.WorldMinimum.x && point.x <= proxy.WorldMaximum.x &&
                        point.y >= proxy.WorldMinimum.y && point.y <= proxy.WorldMaximum.y &&
                        point.z >= proxy.WorldMinimum.z && point.z <= proxy.WorldMaximum.z)) continue;
                    if (proxy.WorldPlanes != null)
                    {
                        float escape = float.PositiveInfinity;
                        Vector3 normal = Vector3.zero;
                        // Reuse the exact ordered-plane search already used by
                        // slotted sling contacts. No native workspace is needed
                        // for the jacket's many sequential area projections.
                        if (cache != null)
                        {
                            if (!TryCachedProxyExit(proxy, proxyIndex, point, cache, pointIndex, out normal, out escape)) continue;
                        }
                        else if (nativeQueriesEnabled && proxy.ScoreTree != null)
                        {
                            if (!TryNearestProxyExit(proxy, point, out normal, out escape)) continue;
                        }
                        else
                        {
                            foreach (Plane plane in proxy.WorldPlanes)
                            {
                                float distance = plane.GetDistanceToPoint(point);
                                if (distance >= 0f) { escape = 0f; break; }
                                if (-distance < escape) { escape = -distance; normal = plane.normal; }
                            }
                        }
                        if (escape <= 0f) continue;
                        point += normal * (escape + .0001f);
                        moved = corrected = true;
                        continue;
                    }
                    Vector3 unit = proxy.WorldToUnit.MultiplyPoint3x4(point);
                    float square = unit.sqrMagnitude;
                    if (square >= 1f) continue;
                    unit = square > .00000001f ? unit / Mathf.Sqrt(square) : proxy.CenterEscape;
                    // A tiny outward bias prevents boundary round-off from
                    // reporting the same stationary contact next frame.
                    point = proxy.UnitToWorld.MultiplyPoint3x4(unit * 1.0001f);
                    moved = corrected = true;
                }
                if (equipment != null && equipment.ResolvePoint(ref point, includeEquipmentRibbons)) moved = corrected = true;
                if (!moved) break;
            }
            return corrected;
        }

        private bool TryCachedProxyExit(Proxy proxy, int proxyIndex, Vector3 point, PointQueryCache cache,
            int pointIndex, out Vector3 normal, out float depth)
        {
            normal = Vector3.zero; depth = 0f;
            Plane[] planes = proxy.WorldPlanes;
            if (!ReferenceEquals(cache.PlaneArrays[proxyIndex], planes) ||
                !ReferenceEquals(cache.EnvelopeIdentities[proxyIndex], proxy.Selected))
            {
                cache.PlaneArrays[proxyIndex] = planes;
                cache.EnvelopeIdentities[proxyIndex] = proxy.Selected;
                for (int i = 0; i < cache.PointCount; i++) cache.PlaneIndices[i * proxies.Length + proxyIndex] = 0;
            }
            ref int hint = ref cache.PlaneIndices[pointIndex * proxies.Length + proxyIndex];
            // A previous separating face is only a hint. Read its CURRENT
            // posed plane; a positive score proves this point is still free.
            if ((uint)hint < planes.Length && planes[hint].GetDistanceToPoint(point) >= 0f) return false;
            if (proxy.ScoreTree != null)
            {
                if ((uint)hint >= planes.Length) hint = 0;
                if (!proxy.ScoreTree.NearestInside(point, ref hint, out float distance)) return false;
                normal = planes[hint].normal; depth = -distance;
                return true;
            }
            float nearest = float.NegativeInfinity;
            for (int i = 0; i < planes.Length; i++)
            {
                float distance = planes[i].GetDistanceToPoint(point);
                if (distance >= 0f) { hint = i; return false; }
                if (distance > nearest) { nearest = distance; normal = planes[i].normal; hint = i; }
            }
            depth = -nearest;
            return true;
        }

        // Exit every overlapping envelope at once. A fixed span direction can
        // traverse the far side of a palm; actual support normals offer nearby
        // exits while the caller's direction breaks ties on a coherent side.
        internal bool ResolvePointOutward(ref Vector3 point, Vector3 escapeDirection,
            Player3DAnatomicalPart? excludedPart = null, int querySlot = -1)
        {
            if (InCertifiedFreeSpace(point, querySlot)) return false;
            bool inside = false;
            float lowerBound = 0f;
            if (querySlot >= 0 && nativeRayQueries != null)
            {
                uint mask = 0;
                for (int i = 0; i < proxies.Length && i < 32; i++)
                    if (!(excludedPart.HasValue && proxies[i].Part == excludedPart.Value)) mask |= 1u << i;
                nativeRayQueries.PrepareNearest(point, mask, querySlot);
            }
            for (int i = 0; i < proxies.Length; i++)
            {
                Proxy proxy = proxies[i];
                escapeContaining[i] = false;
                if (excludedPart.HasValue && proxy.Part == excludedPart.Value) continue;
                Vector3 normal;
                float depth;
                if (querySlot >= 0 && nativeRayQueries != null &&
                    nativeRayQueries.TryNearest(i, out bool nativeInside, out int nativePlane, out float nativeDistance))
                {
                    if (!nativeInside) continue;
                    normal = proxy.WorldPlanes[nativePlane].normal;
                    depth = -nativeDistance;
                }
                else if (!TryNearestProxyExit(proxy, point, out normal, out depth, querySlot)) continue;
                escapeContaining[i] = true;
                escapeNormals[i] = normal; escapeDepths[i] = depth;
                lowerBound = Mathf.Max(lowerBound, depth);
                inside = true;
            }
            if (!inside)
            {
                CacheCertifiedFreeSpace(point, querySlot);
                return false;
            }

            Vector3 original = point;
            Vector3 preferred = escapeDirection.sqrMagnitude >= .00000001f ? escapeDirection.normalized : Vector3.zero;
            Vector3 best = point;
            Vector3 nearest = point;
            float minimumTravel = float.PositiveInfinity, bestTravel = float.PositiveInfinity;
            float bestAlignment = float.NegativeInfinity;
            // Escaping the union must escape each containing hull. The maximum
            // of their nearest-face depths is therefore a distance lower bound.
            // Most strand contacts touch one hull: its direct nearest exit is
            // exact and avoids normal-by-plane ray enumeration altogether.
            for (int i = 0; i < proxies.Length; i++)
            {
                if (!escapeContaining[i] || proxies[i].WorldPlanes == null) continue;
                float travel = escapeDepths[i] + .0001f;
                Accept(escapeNormals[i], travel);
                if (minimumTravel <= lowerBound + .000101f) { point = nearest; return true; }
            }
            for (int i = 0; i < proxies.Length; i++)
            {
                if (!escapeContaining[i]) continue;
                Consider(escapeNormals[i]);
                if (minimumTravel <= lowerBound + .000101f) { point = nearest; return true; }
            }
            // At a seam, the nearest two contact faces can require a diagonal
            // escape. Solve those two support constraints directly instead of
            // enumerating every authored triangle normal on every PBD pass.
            // The complete union still validates each proposed ray and point.
            for (int i = 0; i < proxies.Length; i++)
            {
                if (!escapeContaining[i]) continue;
                for (int j = i + 1; j < proxies.Length; j++)
                {
                    if (!escapeContaining[j]) continue;
                    float cosine = Vector3.Dot(escapeNormals[i], escapeNormals[j]);
                    float divisor = 1f - cosine * cosine;
                    if (divisor < .00001f) continue;
                    float a = (escapeDepths[i] - cosine * escapeDepths[j]) / divisor;
                    float b = (escapeDepths[j] - cosine * escapeDepths[i]) / divisor;
                    if (a <= 0f || b <= 0f) continue;
                    Vector3 direction = escapeNormals[i] * a + escapeNormals[j] * b;
                    if (direction.sqrMagnitude > .00000001f) Consider(direction.normalized);
                    if (minimumTravel <= lowerBound + .000101f) { point = nearest; return true; }
                }
            }
            if (preferred != Vector3.zero) Consider(preferred);
            if (float.IsPositiveInfinity(bestTravel)) return false;
            point = best;
            return true;

            void Consider(Vector3 direction)
            {
                if (direction.sqrMagnitude < .00000001f ||
                    !TryUnionRayExit(original, direction, excludedPart, minimumTravel * 1.02f, out float travel, querySlot)) return;
                Accept(direction, travel);
            }

            void Accept(Vector3 direction, float travel)
            {
                if (travel > minimumTravel * 1.02f) return;
                Vector3 candidate = original + direction * travel;
                // Ray merging must prove escape from the whole union, including
                // volumes that did not contain the original point.
                foreach (Proxy proxy in proxies)
                    if (!(excludedPart.HasValue && proxy.Part == excludedPart.Value) && ProxyContains(proxy, candidate, querySlot)) return;
                if (travel < minimumTravel) { minimumTravel = travel; nearest = candidate; }
                float alignment = Vector3.Dot(direction, preferred);
                // Direction preference may spend at most 2% of the nearest
                // escape. A distant wrong-side exit can never win this tie.
                if (bestTravel > minimumTravel * 1.02f ||
                    travel <= minimumTravel * 1.02f &&
                    (alignment > bestAlignment || alignment == bestAlignment && travel < bestTravel))
                { best = candidate; bestTravel = travel; bestAlignment = alignment; }
            }
        }

        private bool InCertifiedFreeSpace(Vector3 point, int querySlot)
        {
            if ((uint)querySlot >= freeSpaceRadii.Length || freeSpaceRadii[querySlot] <= 0d) return false;
            Vector3 centre = freeSpacePoints[querySlot];
            double x = (double)point.x - centre.x, y = (double)point.y - centre.y, z = (double)point.z - centre.z;
            double radius = freeSpaceRadii[querySlot];
            // Tighten the squared-radius comparison far beyond double's
            // roundoff, so an underestimated distance cannot admit the edge.
            return x * x + y * y + z * z <= radius * radius * (1d - 1e-12d);
        }

        private void CacheCertifiedFreeSpace(Vector3 point, int querySlot)
        {
            if ((uint)querySlot >= freeSpaceRadii.Length) return;
            double radius = double.PositiveInfinity;
            foreach (Proxy proxy in proxies)
            {
                if (!proxy.Active) continue;
                // Include every part, even an excluded palm: the cavity may
                // change between two queries using this same material slot.
                double margin = Math.Max(Math.Max((double)proxy.WorldMinimum.x - point.x, (double)point.x - proxy.WorldMaximum.x),
                    Math.Max(Math.Max((double)proxy.WorldMinimum.y - point.y, (double)point.y - proxy.WorldMaximum.y),
                        Math.Max((double)proxy.WorldMinimum.z - point.z, (double)point.z - proxy.WorldMaximum.z)));
                double safe = margin > 0d ? margin * (1d - 1e-12d) : 0d;
                if (safe <= 0d && proxy.WorldPlanes != null)
                {
                    Plane[] planes = proxy.WorldPlanes;
                    int hint = SeparatingPlaneHint(proxy, querySlot);
                    if ((uint)hint < planes.Length) safe = CertifiedPlaneRadius(planes[hint], point);
                    for (int i = 0; safe <= 0d && i < planes.Length; i++)
                        safe = CertifiedPlaneRadius(planes[i], point);
                }
                // An ellipsoid inside its AABB or an uncertain plane has no
                // certificate. It must keep the original exact query path.
                if (!(safe > 0d)) { freeSpaceRadii[querySlot] = 0d; return; }
                radius = Math.Min(radius, safe);
            }
            freeSpacePoints[querySlot] = point;
            freeSpaceRadii[querySlot] = double.IsInfinity(radius) || double.IsNaN(radius) ? 0d : radius;
        }

        private static double CertifiedPlaneRadius(Plane plane, Vector3 point)
        {
            float distance = plane.GetDistanceToPoint(point);
            if (!(distance > 0f) || float.IsInfinity(distance)) return 0d;
            Vector3 normal = plane.normal;
            double x = normal.x, y = normal.y, z = normal.z;
            // Six rounded float operations form the native plane distance.
            // 16 float unit roundoffs bounds both centre and future-point
            // error, including cancellation; q changes each term by <=|n|r.
            const double errorFactor = 16d / 16777216d;
            double absolute = Math.Abs(x * point.x) + Math.Abs(y * point.y) + Math.Abs(z * point.z) + Math.Abs(plane.distance);
            // Include absolute underflow error as well as relative error.
            double numerator = distance - 2d * errorFactor * absolute - 16d * 1.4012984643248171e-45d;
            double norm = Math.Sqrt(x * x + y * y + z * z) * (1d + 1e-12d);
            double divisor = norm + errorFactor * (Math.Abs(x) + Math.Abs(y) + Math.Abs(z));
            return numerator > 0d && divisor > 0d ? numerator / divisor * (1d - 1e-12d) : 0d;
        }

        private static bool TryNearestProxyExit(Proxy proxy, Vector3 point, out Vector3 normal, out float depth, int querySlot = -1)
        {
            normal = Vector3.zero; depth = 0f;
            if (!proxy.Active || point.x < proxy.WorldMinimum.x || point.x > proxy.WorldMaximum.x ||
                point.y < proxy.WorldMinimum.y || point.y > proxy.WorldMaximum.y ||
                point.z < proxy.WorldMinimum.z || point.z > proxy.WorldMaximum.z) return false;
            if (proxy.WorldPlanes == null)
            {
                Vector3 unit = proxy.WorldToUnit.MultiplyPoint3x4(point);
                if (unit.sqrMagnitude >= 1f) return false;
                normal = proxy.WorldToUnit.transpose.MultiplyVector(unit);
                if (normal.sqrMagnitude < .00000001f) normal = proxy.UnitToWorld.MultiplyVector(proxy.CenterEscape);
                normal.Normalize();
                // A general transformed ellipsoid has no plane-distance lower
                // bound. Keep zero; its ray candidate remains fully checked.
                return true;
            }
            float nearest = float.NegativeInfinity;
            Plane[] planes = proxy.WorldPlanes;
            ref int hint = ref SeparatingPlaneHint(proxy, querySlot);
            if (hint < planes.Length && planes[hint].GetDistanceToPoint(point) >= 0f) return false;
            if (proxy.ScoreTree != null)
            {
                if (hint >= planes.Length) hint = 0;
                if (!proxy.ScoreTree.NearestInside(point, ref hint, out float distance)) return false;
                normal = planes[hint].normal; depth = -distance;
                return true;
            }
            for (int i = 0; i < planes.Length; i++)
            {
                Plane plane = planes[i];
                float distance = plane.GetDistanceToPoint(point);
                if (distance >= 0f) { hint = i; return false; }
                if (distance > nearest) { nearest = distance; normal = plane.normal; }
            }
            depth = -nearest;
            return true;
        }

        private static bool ProxyContains(Proxy proxy, Vector3 point, int querySlot = -1)
        {
            if (!proxy.Active || point.x < proxy.WorldMinimum.x || point.x > proxy.WorldMaximum.x ||
                point.y < proxy.WorldMinimum.y || point.y > proxy.WorldMaximum.y ||
                point.z < proxy.WorldMinimum.z || point.z > proxy.WorldMaximum.z) return false;
            if (proxy.WorldPlanes == null) return proxy.WorldToUnit.MultiplyPoint3x4(point).sqrMagnitude < 1f;
            Plane[] planes = proxy.WorldPlanes;
            ref int hint = ref SeparatingPlaneHint(proxy, querySlot);
            if (hint < planes.Length && planes[hint].GetDistanceToPoint(point) >= 0f) return false;
            if (proxy.ScoreTree != null) return proxy.ScoreTree.Contains(point, ref hint);
            for (int i = 0; i < planes.Length; i++)
                if (planes[i].GetDistanceToPoint(point) >= 0f) { hint = i; return false; }
            return true;
        }

        private static ref int SeparatingPlaneHint(Proxy proxy, int querySlot)
        {
            if (querySlot >= 0 && proxy.SeparatingPlanes != null && querySlot < proxy.SeparatingPlanes.Length)
                return ref proxy.SeparatingPlanes[querySlot];
            return ref proxy.SeparatingPlane;
        }

        // Each lease fixes fresh world planes; its scratch allocation belongs
        // to the contact owner, rather than to one rendered presentation.
        internal RayQueryScope BeginNativeQueries()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlayerScarfBodyContacts));
            if (!nativeQueriesEnabled) return default;
            if (nativeRayQueries != null) throw new InvalidOperationException("Body ray query scopes cannot overlap.");
            using var workspaceProfile = RayWorkspaceMarker.Auto();
            if (retainedNativeRayQueries == null)
            {
                retainedNativeRayQueries = new NativeRayQueries(this);
                NativeRayWorkspaceCreateCount++;
            }
            try { retainedNativeRayQueries.RefreshPose(); }
            catch
            {
                retainedNativeRayQueries.Dispose();
                retainedNativeRayQueries = null;
                throw;
            }
            nativeRayQueries = retainedNativeRayQueries;
            if (++nativeRayQueryLease == 0) nativeRayQueryLease++;
            return new RayQueryScope(this, nativeRayQueryLease);
        }

        private NativeRayQueries RayQueriesForLease(ulong lease)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlayerScarfBodyContacts));
            if (lease != nativeRayQueryLease || nativeRayQueries == null)
                throw new InvalidOperationException("The body ray query scope has ended.");
            return nativeRayQueries;
        }

        internal readonly struct RayQueryScope : IDisposable
        {
            private readonly PlayerScarfBodyContacts owner;
            private readonly ulong lease;
            internal RayQueryScope(PlayerScarfBodyContacts owner, ulong lease)
            { this.owner = owner; this.lease = lease; }
            private NativeRayQueries Queries => owner.RayQueriesForLease(lease);
            public void PrepareNearest(Vector3 point, uint mask, int slot) => Queries.PrepareNearest(point, mask, slot);
            public bool TryNearest(int proxy, out bool inside, out int plane, out float distance) =>
                Queries.TryNearest(proxy, out inside, out plane, out distance);
            public void Clip(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excluded, float travel) =>
                Queries.Clip(point, direction, excluded, travel);
            public void ClipContaining(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excluded, float travel) =>
                Queries.ClipContaining(point, direction, excluded, travel);
            public void ClipRemaining(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excluded, float travel) =>
                Queries.ClipRemaining(point, direction, excluded, travel);
            public void ClipFromInside(Vector3 point, Vector3 direction, int proxy) => Queries.ClipFromInside(point, direction, proxy);
            public bool IsEligible(int proxy) => Queries.IsEligible(proxy);
            public bool TryInterval(int proxy, out float entry, out float exit) => Queries.TryInterval(proxy, out entry, out exit);
            public void Dispose()
            {
                // A copied or already closed lease cannot close a newer one.
                if (owner != null && owner.nativeRayQueryLease == lease) owner.nativeRayQueries = null;
            }
        }

        private static int CalibrateRayArithmetic()
        {
            // These captured operands separate wide sums from both orders of
            // rounded-float sums. Unknown runtime arithmetic keeps Mono clipping.
            var squared = new Vector3(BitConverter.Int32BitsToSingle(0x3E14AF05),
                BitConverter.Int32BitsToSingle(unchecked((int)0xBCE31AC0)), BitConverter.Int32BitsToSingle(unchecked((int)0xBBC5D760)));
            var a = new Vector3(BitConverter.Int32BitsToSingle(0x3DC37B40), BitConverter.Int32BitsToSingle(0x3DA5374A),
                BitConverter.Int32BitsToSingle(0x3F2EB332));
            var b = new Vector3(BitConverter.Int32BitsToSingle(0x3FE1CF67), BitConverter.Int32BitsToSingle(unchecked((int)0xBFDC385B)),
                BitConverter.Int32BitsToSingle(0x3E08C6F2));
            int first = BitConverter.SingleToInt32Bits(Vector3.Dot(squared, squared));
            int second = BitConverter.SingleToInt32Bits(Vector3.Dot(a, b));
            int mode = first == 0x3CB34DE1 && second == 0x3DF7498D ? 1 :
                first == 0x3CB34DE2 && second == 0x3DF7498F ? 2 : -1;
            if (mode < 0) return mode;
            var plane = new Plane
            {
                normal = new Vector3(BitConverter.Int32BitsToSingle(unchecked((int)0xBED12C09)),
                    BitConverter.Int32BitsToSingle(0x3D8E8799), BitConverter.Int32BitsToSingle(0x3F165E1E)),
                distance = BitConverter.Int32BitsToSingle(unchecked((int)0xBF5CAC9B))
            };
            var point = new Vector3(BitConverter.Int32BitsToSingle(0x3F24D821),
                BitConverter.Int32BitsToSingle(unchecked((int)0xBF02EE5A)), BitConverter.Int32BitsToSingle(unchecked((int)0xBF58F37C)));
            float dot = Vector3.Dot(plane.normal, point);
            float roundedPlane = RayCalibrationAdd(dot, plane.distance);
            if (BitConverter.SingleToInt32Bits(plane.GetDistanceToPoint(point)) != BitConverter.SingleToInt32Bits(roundedPlane)) return -1;
            float distance = BitConverter.Int32BitsToSingle(unchecked((int)0xBD9EA820));
            float slope = BitConverter.Int32BitsToSingle(0x3E08C6F2);
            return BitConverter.SingleToInt32Bits(RayCalibrationDivide(distance, slope)) ==
                BitConverter.SingleToInt32Bits((float)(-(double)distance / slope)) ? mode : -1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float RayCalibrationAdd(float a, float b) => a + b;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float RayCalibrationDivide(float distance, float slope) => -distance / slope;

        private sealed class NativeRayQueries : IDisposable
        {
            private PlayerScarfBodyContacts owner;
            private NativeArray<Plane> planes;
            private NativeArray<PlayerBodyRayJob.Range> ranges;
            private NativeArray<bool> eligible;
            private NativeArray<bool> inside;
            private NativeArray<PlayerBodyRayJob.Tree> trees;
            private NativeArray<PlayerBodyRayJob.Node> nodes;
            private NativeArray<PlayerBodyRayJob.Work> stack;
            private NativeArray<double> maximumDistances;
            private NativeArray<uint> distanceVersions;
            private NativeArray<PlayerBodyRayJob.PointPlane> pointPlanes;
            private NativeArray<uint> proxyPointVersions;
            private NativeArray<PlayerBodyRayJob.Range> packRanges;
            private NativeArray<PlayerBodyRayJob.PlanePack> planePacks;
            private NativeArray<PlayerBodyRayJob.PointPack> pointPacks;
            private NativeArray<uint> packPointVersions;
            private NativeArray<float> exactPointDistances;
            private NativeArray<uint> exactPointVersions;
            private NativeArray<PlayerBodyRayJob.DirectResult> directResults;
            private NativeArray<PlayerBodyNearestJob.Seed> nearestSeeds;
            private NativeArray<PlayerBodyNearestJob.Result> nearestResults;
            private readonly PlayerBodyNearestJob.Result[] nearestCached;
            private uint nearestPreparedMask, nearestValidMask;
            private int nearestQuerySlot;
            private NativeArray<int> order;
            private NativeArray<PlayerBodyRayJob.Crossing> crossings;
            private NativeArray<int> candidateIndices, candidateCounts;
            private readonly PlayerBodyRayJob.Interval[] results;
            private readonly bool[] verified;
            private readonly bool useMasks;
            private uint eligibleMask, insideMask;
            private Vector3 containingPoint;
            private Player3DAnatomicalPart? containingExcluded;
            private uint containingMask;
            private bool hasContainingMask;
            private Vector3 point, direction;
            private Vector3 cachedPoint;
            private uint pointVersion;
            private bool hasCachedPoint;

            public NativeRayQueries(PlayerScarfBodyContacts owner)
            {
                this.owner = owner;
                useMasks = owner.proxies.Length <= 32;
                results = new PlayerBodyRayJob.Interval[owner.proxies.Length];
                verified = new bool[owner.proxies.Length];
                nearestCached = new PlayerBodyNearestJob.Result[owner.proxies.Length];
            }

            public void RefreshPose()
            {
                // Retained point stamps remain monotonic across leases. The first
                // ray in every fresh pose advances the stamp, invalidating all
                // old distance data without clearing the entire search tree.
                hasCachedPoint = hasContainingMask = false;
                nearestPreparedMask = nearestValidMask = eligibleMask = insideMask = containingMask = 0;
                nearestQuerySlot = 0;
                Array.Clear(results, 0, results.Length);
                Array.Clear(verified, 0, verified.Length);
                bool conservative = ExactRayArithmeticMode == 0;
                bool nearestEnabled = owner.NativeNearestSupported;
                int count = 0, packCount = 0;
                int nodeCount = 0, orderCount = 0;
                foreach (Proxy proxy in owner.proxies)
                {
                    if (proxy.WorldPlanes != null)
                    {
                        count += proxy.WorldPlanes.Length;
                        if (conservative) packCount += (proxy.WorldPlanes.Length + 3) / 4;
                    }
                    if (!(conservative || nearestEnabled) || proxy.ScoreTree == null) continue;
                    nodeCount += proxy.ScoreTree.NodeCount;
                    orderCount += proxy.ScoreTree.Planes.Length;
                }
                RetainBuffer(ref planes, count);
                RetainBuffer(ref ranges, owner.proxies.Length);
                RetainBuffer(ref eligible, owner.proxies.Length);
                RetainBuffer(ref inside, owner.proxies.Length);
                RetainBuffer(ref exactPointDistances, count);
                RetainBuffer(ref exactPointVersions, owner.proxies.Length, true);
                RetainBuffer(ref directResults, owner.proxies.Length);
                RetainBuffer(ref candidateCounts, owner.proxies.Length);
                for (int i = 0; i < owner.proxies.Length; i++) eligible[i] = inside[i] = false;
                // Calibrated nearest queries and conservative rays share
                // immutable search data and sequential traversal scratch.
                if (conservative || nearestEnabled)
                {
                    RetainBuffer(ref trees, owner.proxies.Length);
                    RetainBuffer(ref nodes, nodeCount);
                    RetainBuffer(ref order, orderCount);
                    RetainBuffer(ref stack, PlayerBodyRayJob.StackCapacity);
                }
                if (nearestEnabled)
                {
                    RetainBuffer(ref nearestSeeds, owner.proxies.Length);
                    RetainBuffer(ref nearestResults, owner.proxies.Length);
                }
                if (conservative)
                {
                    RetainBuffer(ref maximumDistances, nodeCount);
                    RetainBuffer(ref distanceVersions, nodeCount, true);
                    RetainBuffer(ref pointPlanes, count);
                    RetainBuffer(ref proxyPointVersions, owner.proxies.Length, true);
                    RetainBuffer(ref packRanges, owner.proxies.Length);
                    RetainBuffer(ref planePacks, packCount);
                    RetainBuffer(ref pointPacks, packCount);
                    RetainBuffer(ref packPointVersions, owner.proxies.Length, true);
                    RetainBuffer(ref crossings, count);
                    RetainBuffer(ref candidateIndices, owner.proxies.Length * PlayerBodyRayJob.CandidateCapacity);
                }
                int first = 0;
                int nodeFirst = 0, orderFirst = 0, packFirst = 0;
                for (int i = 0; i < owner.proxies.Length; i++)
                {
                    Plane[] source = owner.proxies[i].WorldPlanes;
                    int length = source?.Length ?? 0;
                    ranges[i] = new PlayerBodyRayJob.Range { First = first, Count = length };
                    if (length > 0) NativeArray<Plane>.Copy(source, 0, planes, first, length);
                    first += length;
                    if (!(conservative || nearestEnabled)) continue;
                    if (conservative)
                    {
                        int groups = (length + 3) / 4;
                        packRanges[i] = new PlayerBodyRayJob.Range { First = packFirst, Count = groups };
                        for (int group = 0; group < groups; group++) planePacks[packFirst + group] = PackPlanes(source, group * 4);
                        packFirst += groups;
                    }
                    PlaneScoreTree tree = owner.proxies[i].ScoreTree;
                    trees[i] = tree == null ? default : new PlayerBodyRayJob.Tree
                    {
                        First = nodeFirst, Count = tree.NodeCount, OrderFirst = orderFirst,
                        Origin = tree.CopyRayTree(nodes, nodeFirst, order, orderFirst)
                    };
                    if (tree == null) continue;
                    nodeFirst += tree.NodeCount; orderFirst += tree.Planes.Length;
                }
                if (nearestEnabled)
                {
                    var validation = new PlayerBodyNearestJob
                    {
                        Planes = planes, Ranges = ranges, Trees = trees, Nodes = nodes,
                        Results = nearestResults, EligibleMask = owner.proxies.Length == 32 ? uint.MaxValue : (1u << owner.proxies.Length) - 1u,
                        ValidateOnly = 1
                    };
                    PlayerBodyNearestJob.RunSynchronous(ref validation);
                    for (int i = 0; i < owner.proxies.Length; i++)
                        if (nearestResults[i].Status == 3) nearestValidMask |= 1u << i;
                }
            }

            private void RetainBuffer<T>(ref NativeArray<T> buffer, int count, bool clear = false) where T : struct
            {
                if (buffer.IsCreated && buffer.Length >= count) return;
                if (buffer.IsCreated) buffer.Dispose();
                buffer = new NativeArray<T>(count, Allocator.Persistent,
                    clear ? NativeArrayOptions.ClearMemory : NativeArrayOptions.UninitializedMemory);
                owner.NativeRayBufferAllocationCount++;
            }

            public void PrepareNearest(Vector3 point, uint requestedMask, int querySlot)
            {
                nearestPreparedMask = 0;
                nearestQuerySlot = querySlot;
                if (!nearestSeeds.IsCreated || !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) return;
                uint unresolved = 0;
                for (int i = 0; i < owner.proxies.Length; i++)
                {
                    uint bit = 1u << i;
                    Proxy proxy = owner.proxies[i];
                    Plane[] source = proxy.WorldPlanes;
                    if ((requestedMask & nearestValidMask & bit) == 0 || source == null || proxy.ScoreTree == null) continue;
                    nearestPreparedMask |= bit;
                    if (!proxy.Active || point.x < proxy.WorldMinimum.x || point.x > proxy.WorldMaximum.x ||
                        point.y < proxy.WorldMinimum.y || point.y > proxy.WorldMaximum.y ||
                        point.z < proxy.WorldMinimum.z || point.z > proxy.WorldMaximum.z)
                    {
                        nearestCached[i] = new PlayerBodyNearestJob.Result { Status = 1, PlaneIndex = -1 };
                        continue;
                    }
                    int hint = SeparatingPlaneHint(proxy, querySlot);
                    if ((uint)hint >= source.Length) hint = 0;
                    float distance = source[hint].GetDistanceToPoint(point);
                    if (distance >= 0f)
                    {
                        nearestCached[i] = new PlayerBodyNearestJob.Result { Status = 1, PlaneIndex = hint, Distance = distance };
                        continue;
                    }
                    nearestSeeds[i] = new PlayerBodyNearestJob.Seed { PlaneIndex = hint, Distance = distance };
                    unresolved |= bit;
                }
                if (unresolved == 0) return;
                var job = new PlayerBodyNearestJob
                {
                    Planes = planes, Ranges = ranges, Trees = trees, Nodes = nodes, Order = order,
                    Seeds = nearestSeeds, Results = nearestResults, Stack = stack, Point = point, EligibleMask = unresolved
                };
                PlayerBodyNearestJob.RunSynchronous(ref job);
                owner.NativeNearestExecutedInBurst |= job.ManagedExecution == 0;
                for (int i = 0; i < owner.proxies.Length; i++)
                    if ((unresolved & (1u << i)) != 0) nearestCached[i] = nearestResults[i];
            }

            public bool TryNearest(int proxyIndex, out bool inside, out int planeIndex, out float distance)
            {
                inside = false; planeIndex = -1; distance = 0f;
                if ((uint)proxyIndex >= owner.proxies.Length || (nearestPreparedMask & (1u << proxyIndex)) == 0) return false;
                PlayerBodyNearestJob.Result result = nearestCached[proxyIndex];
                if (result.Status == 0) return false;
                inside = result.Status == 2; planeIndex = result.PlaneIndex; distance = result.Distance;
                if (planeIndex >= 0) SeparatingPlaneHint(owner.proxies[proxyIndex], nearestQuerySlot) = planeIndex;
                return true;
            }

            private static PlayerBodyRayJob.PlanePack PackPlanes(Plane[] source, int first)
            {
                Plane a = source[first], b = first + 1 < source.Length ? source[first + 1] : default;
                Plane c = first + 2 < source.Length ? source[first + 2] : default;
                Plane d = first + 3 < source.Length ? source[first + 3] : default;
                Vector3 an = a.normal, bn = b.normal, cn = c.normal, dn = d.normal;
                return new PlayerBodyRayJob.PlanePack
                {
                    X = new v128(an.x, bn.x, cn.x, dn.x),
                    Y = new v128(an.y, bn.y, cn.y, dn.y),
                    Z = new v128(an.z, bn.z, cn.z, dn.z),
                    Distance = new v128(a.distance, b.distance, c.distance, d.distance)
                };
            }

            public void Clip(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excludedPart, float maximumTravel)
                => ClipStage(point, direction, excludedPart, maximumTravel, 0);

            public void ClipContaining(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excludedPart, float maximumTravel)
                => ClipStage(point, direction, excludedPart, maximumTravel, 1);

            public void ClipRemaining(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excludedPart, float maximumTravel)
                => ClipStage(point, direction, excludedPart, maximumTravel, 2);

            // Direct proof of the certified-inside traversal without changing
            // the contact owner's scratch containing flags.
            public void ClipFromInside(Vector3 point, Vector3 direction, int proxyIndex)
            {
                using (RayPrepareMarker.Auto())
                {
                    this.point = point; this.direction = direction;
                    Array.Clear(verified, 0, verified.Length);
                    eligibleMask = insideMask = 0;
                    for (int i = 0; i < owner.proxies.Length; i++)
                    {
                        bool selected = i == proxyIndex && owner.proxies[i].Active && owner.proxies[i].WorldPlanes != null;
                        if (useMasks)
                        {
                            if (selected) eligibleMask |= 1u << i;
                            if (i == proxyIndex) insideMask |= 1u << i;
                        }
                        else { eligible[i] = selected; inside[i] = i == proxyIndex; }
                    }
                }
                Run(0);
            }

            private void ClipStage(Vector3 point, Vector3 direction, Player3DAnatomicalPart? excludedPart, float maximumTravel, int stage)
            {
                bool any = false;
                using (RayPrepareMarker.Auto())
                {
                    this.point = point; this.direction = direction;
                    if (stage != 2) Array.Clear(verified, 0, verified.Length);
                    eligibleMask = insideMask = 0;
                    if (stage == 0) hasContainingMask = false;
                    // Gameplay's fixed-pose containing classification depends
                    // on point and excluded part, never the probe direction.
                    // General proof queries invalidate this shortcut above.
                    if (useMasks && stage == 1 && hasContainingMask && containingPoint.Equals(point) && containingExcluded == excludedPart)
                    {
                        eligibleMask = insideMask = containingMask;
                        any = containingMask != 0;
                    }
                    else
                    {
                        for (int i = 0; i < owner.proxies.Length; i++)
                        {
                            Proxy proxy = owner.proxies[i];
                            bool selected = proxy.Active && proxy.WorldPlanes != null &&
                                !(excludedPart.HasValue && proxy.Part == excludedPart.Value) &&
                                (stage == 0 || owner.escapeContaining[i] == (stage == 1)) &&
                                (owner.escapeContaining[i] || RayMayReachBounds(proxy, point, direction, maximumTravel));
                            if (selected) any = true;
                            // General proof queries do not own the contact scratch
                            // flags. Certify their actual point; gameplay's first stage
                            // already has the exact containing classification.
                            bool containing = selected && (stage == 1 && owner.escapeContaining[i] ||
                                stage == 0 && ProxyContains(proxy, point));
                            if (useMasks)
                            {
                                if (selected) eligibleMask |= 1u << i;
                                if (containing) insideMask |= 1u << i;
                            }
                            else { eligible[i] = selected; inside[i] = containing; }
                        }
                        if (useMasks && stage == 1)
                        {
                            containingPoint = point; containingExcluded = excludedPart;
                            containingMask = eligibleMask; hasContainingMask = true;
                        }
                    }
                }
                if (!any) return;
                Run(stage);
            }

            private void Run(int stage)
            {
                // Each scope fixes the planes and tree origins. Distances
                // therefore stay identical while only the probe direction
                // changes; exact point equality is the entire cache key.
                if (!hasCachedPoint || !cachedPoint.Equals(point))
                {
                    cachedPoint = point; hasCachedPoint = true;
                    if (++pointVersion == 0)
                    {
                        for (int i = 0; i < distanceVersions.Length; i++) distanceVersions[i] = 0;
                        for (int i = 0; i < proxyPointVersions.Length; i++) proxyPointVersions[i] = 0;
                        for (int i = 0; i < packPointVersions.Length; i++) packPointVersions[i] = 0;
                        for (int i = 0; i < exactPointVersions.Length; i++) exactPointVersions[i] = 0;
                        pointVersion = 1;
                    }
                }
                var job = new PlayerBodyRayJob
                {
                    Planes = planes, Ranges = ranges, Eligible = eligible, Inside = inside,
                    EligibleMask = eligibleMask, InsideMask = insideMask, UseMasks = useMasks,
                    Trees = trees, Nodes = nodes, Order = order, Stack = stack, Crossings = crossings,
                    MaximumDistances = maximumDistances, DistanceVersions = distanceVersions, PointVersion = pointVersion,
                    PointPlanes = pointPlanes, ProxyPointVersions = proxyPointVersions,
                    PackRanges = packRanges, PlanePacks = planePacks, PointPacks = pointPacks, PackPointVersions = packPointVersions,
                    ExactPointDistances = exactPointDistances, ExactPointVersions = exactPointVersions, DirectResults = directResults,
                    CandidateIndices = candidateIndices, CandidateCounts = candidateCounts,
                    Point = point, Direction = direction, ArithmeticMode = ExactRayArithmeticMode
                };
                if (stage == 0)
                {
                    using (RayNativeExecuteMarker.Auto()) PlayerBodyRayJob.RunSynchronous(ref job);
                }
                else
                {
                    using var stageProfile = (stage == 1 ? RayNativeContainingMarker : RayNativeRemainingMarker).Auto();
                    using (RayNativeExecuteMarker.Auto()) PlayerBodyRayJob.RunSynchronous(ref job);
                }
                owner.NativeRayRunCount++;
                owner.NativeRayExecutedInBurst = job.ManagedExecution == 0;
                owner.NativeRayExecutedWithSimd = job.UsedSimd != 0;
            }

            public bool IsEligible(int proxyIndex)
                => useMasks ? (eligibleMask & (1u << proxyIndex)) != 0 : eligible[proxyIndex];

            public bool TryInterval(int proxyIndex, out float entry, out float exit)
            {
                if (!verified[proxyIndex])
                {
                    using var profile = RayMonoMarker.Auto();
                    var exact = new PlayerBodyRayJob.Interval { Entry = float.NegativeInfinity, Exit = float.PositiveInfinity };
                    if (IsEligible(proxyIndex))
                    {
                        owner.NativeRayVerifiedIntervalCount++;
                        Proxy proxy = owner.proxies[proxyIndex];
                        int count = candidateCounts[proxyIndex];
                        if (count == PlayerBodyRayJob.DirectInterval)
                        {
                            PlayerBodyRayJob.DirectResult direct = directResults[proxyIndex];
                            exact.Entry = direct.Entry; exact.Exit = direct.Exit; exact.Hit = direct.Hit != 0;
                            owner.NativeRayDirectIntervalCount++;
                        }
                        else if (count == PlayerBodyRayJob.CertifiedMiss)
                        {
                            owner.NativeRayCertifiedMissCount++;
                            exact.Hit = false;
                        }
                        else if (count < 0)
                        {
                            owner.NativeRayOverflowCount++;
                            exact.Hit = RayInterval(proxy, point, direction, out exact.Entry, out exact.Exit);
                        }
                        else
                        {
                            owner.NativeRayCandidateCount += count;
                            owner.NativeRayMaximumCandidateCount = Math.Max(owner.NativeRayMaximumCandidateCount, count);
                            bool hit = true;
                            int first = proxyIndex * PlayerBodyRayJob.CandidateCapacity;
                            for (int i = 0; i < count; i++)
                            {
                                Plane plane = proxy.WorldPlanes[candidateIndices[first + i]];
                                float distance = plane.GetDistanceToPoint(point), slope = Vector3.Dot(plane.normal, direction);
                                if (Mathf.Abs(slope) < .0000001f)
                                {
                                    if (distance > 0f) { hit = false; break; }
                                    continue;
                                }
                                float crossing = -distance / slope;
                                if (slope > 0f) exact.Exit = Mathf.Min(exact.Exit, crossing);
                                else exact.Entry = Mathf.Max(exact.Entry, crossing);
                                if (exact.Entry > exact.Exit) { hit = false; break; }
                            }
                            exact.Hit = hit && exact.Exit > 0f;
                        }
                    }
                    results[proxyIndex] = exact;
                    verified[proxyIndex] = true;
                }
                PlayerBodyRayJob.Interval interval = results[proxyIndex];
                entry = interval.Entry; exit = interval.Exit;
                return interval.Hit;
            }

            public void Dispose()
            {
                using var workspaceProfile = RayWorkspaceMarker.Auto();
                if (owner != null && ReferenceEquals(owner.nativeRayQueries, this)) owner.nativeRayQueries = null;
                owner = null;
                if (candidateCounts.IsCreated) candidateCounts.Dispose();
                if (candidateIndices.IsCreated) candidateIndices.Dispose();
                if (crossings.IsCreated) crossings.Dispose();
                if (distanceVersions.IsCreated) distanceVersions.Dispose();
                if (proxyPointVersions.IsCreated) proxyPointVersions.Dispose();
                if (pointPlanes.IsCreated) pointPlanes.Dispose();
                if (packPointVersions.IsCreated) packPointVersions.Dispose();
                if (directResults.IsCreated) directResults.Dispose();
                if (nearestResults.IsCreated) nearestResults.Dispose();
                if (nearestSeeds.IsCreated) nearestSeeds.Dispose();
                if (exactPointVersions.IsCreated) exactPointVersions.Dispose();
                if (exactPointDistances.IsCreated) exactPointDistances.Dispose();
                if (pointPacks.IsCreated) pointPacks.Dispose();
                if (planePacks.IsCreated) planePacks.Dispose();
                if (packRanges.IsCreated) packRanges.Dispose();
                if (maximumDistances.IsCreated) maximumDistances.Dispose();
                if (stack.IsCreated) stack.Dispose();
                if (order.IsCreated) order.Dispose();
                if (nodes.IsCreated) nodes.Dispose();
                if (trees.IsCreated) trees.Dispose();
                if (inside.IsCreated) inside.Dispose();
                if (eligible.IsCreated) eligible.Dispose();
                if (ranges.IsCreated) ranges.Dispose();
                if (planes.IsCreated) planes.Dispose();
            }
        }

        private bool TryUnionRayExit(Vector3 point, Vector3 direction,
            Player3DAnatomicalPart? excludedPart, float maximumTravel, out float travel, int querySlot = -1)
        {
            using var profile = RayMarker.Auto();
            travel = 0f;
            float exit = 0f;
            for (int i = 0; i < proxies.Length; i++)
            {
                escapeEntries[i] = float.PositiveInfinity;
                escapeExits[i] = float.NegativeInfinity;
            }
            nativeRayQueries?.ClipContaining(point, direction, excludedPart, maximumTravel);
            // A containing hull alone can already exceed the best candidate.
            // Reject such directions before querying all other anatomy.
            for (int i = 0; i < proxies.Length; i++)
            {
                if (!escapeContaining[i]) continue;
                Proxy proxy = proxies[i];
                float entry, end;
                bool hit = nativeRayQueries != null && proxy.WorldPlanes != null
                    ? nativeRayQueries.TryInterval(i, out entry, out end)
                    : proxy.ScoreTree != null
                        ? proxy.ScoreTree.RayFromInside(point, direction, SeparatingPlaneHint(proxy, querySlot), out entry, out end)
                        : RayInterval(proxy, point, direction, out entry, out end);
                if (!hit) continue;
                escapeEntries[i] = entry; escapeExits[i] = end;
                exit = Mathf.Max(exit, end);
                if (exit + .0001f > maximumTravel) return false;
            }
            nativeRayQueries?.ClipRemaining(point, direction, excludedPart, maximumTravel);
            for (int i = 0; i < proxies.Length; i++)
            {
                Proxy proxy = proxies[i];
                if (escapeContaining[i]) continue;
                bool native = nativeRayQueries != null && proxy.WorldPlanes != null;
                if (native)
                {
                    // ClipRemaining already selected this exact point,
                    // direction, excluded part and travel budget.
                    if (!nativeRayQueries.IsEligible(i)) continue;
                }
                else if (!proxy.Active || excludedPart.HasValue && proxy.Part == excludedPart.Value ||
                    !RayMayReachBounds(proxy, point, direction, maximumTravel)) continue;
                float entry, end;
                bool hit = native
                    ? nativeRayQueries.TryInterval(i, out entry, out end)
                    : RayInterval(proxy, point, direction, out entry, out end);
                if (!hit) continue;
                escapeEntries[i] = entry;
                escapeExits[i] = end;
                if (entry <= 0f && end > exit) exit = end;
                if (exit + .0001f > maximumTravel) return false;
            }
            // Include envelopes reached before the current exit, even if the
            // starting point was outside them. Ordering cannot change the exit.
            for (int pass = 0; pass < proxies.Length; pass++)
            {
                float previousExit = exit;
                for (int i = 0; i < proxies.Length; i++)
                    if (escapeEntries[i] <= exit + .0001f && escapeExits[i] > exit)
                        exit = escapeExits[i];
                if (exit + .0001f > maximumTravel) return false;
                if (exit == previousExit) break;
            }
            if (exit <= 0f || float.IsInfinity(exit)) return false;
            travel = exit + .0001f;
            return true;
        }

        private static bool RayInterval(Proxy proxy, Vector3 point, Vector3 direction,
            out float entry, out float exit)
        {
            entry = float.NegativeInfinity;
            exit = float.PositiveInfinity;
            if (proxy.WorldPlanes != null)
            {
                foreach (Plane plane in proxy.WorldPlanes)
                {
                    float distance = plane.GetDistanceToPoint(point);
                    float slope = Vector3.Dot(plane.normal, direction);
                    if (Mathf.Abs(slope) < .0000001f)
                    {
                        if (distance > 0f) return false;
                        continue;
                    }
                    float crossing = -distance / slope;
                    if (slope > 0f) exit = Mathf.Min(exit, crossing);
                    else entry = Mathf.Max(entry, crossing);
                    if (entry > exit) return false;
                }
                return exit > 0f;
            }
            Vector3 origin = proxy.WorldToUnit.MultiplyPoint3x4(point);
            Vector3 delta = proxy.WorldToUnit.MultiplyVector(direction);
            float a = delta.sqrMagnitude, b = Vector3.Dot(origin, delta);
            float discriminant = b * b - a * (origin.sqrMagnitude - 1f);
            if (a < .00000001f || discriminant < 0f) return false;
            float root = Mathf.Sqrt(discriminant);
            entry = (-b - root) / a;
            exit = (-b + root) / a;
            return exit > 0f;
        }

        private static bool RayMayReachBounds(Proxy proxy, Vector3 point, Vector3 direction, float maximumTravel)
        {
            float enter = 0f, leave = maximumTravel;
            Vector3 boundsMinimum, boundsMaximum;
            if (proxy.WorldPlanes != null)
            {
                point = proxy.RayWorldToBone.MultiplyPoint3x4(point);
                direction = proxy.RayWorldToBone.MultiplyVector(direction);
                boundsMinimum = proxy.RayMinimum; boundsMaximum = proxy.RayMaximum;
            }
            else
            {
                // The existing world AABB exactly bounds its ellipsoid.
                Vector3 padding = Vector3.one * .0001f;
                boundsMinimum = proxy.WorldMinimum - padding; boundsMaximum = proxy.WorldMaximum + padding;
            }
            return Slab(point.x, direction.x, boundsMinimum.x, boundsMaximum.x) &&
                Slab(point.y, direction.y, boundsMinimum.y, boundsMaximum.y) &&
                Slab(point.z, direction.z, boundsMinimum.z, boundsMaximum.z);

            bool Slab(float origin, float slope, float minimum, float maximum)
            {
                if (slope == 0f) return origin >= minimum && origin <= maximum;
                float a = (minimum - origin) / slope, b = (maximum - origin) / slope;
                enter = Mathf.Max(enter, Mathf.Min(a, b));
                leave = Mathf.Min(leave, Mathf.Max(a, b));
                return enter <= leave;
            }
        }

        internal bool TryHemObstacle(Vector3[] original, float[] freedom, Transform actor,
            out Bounds bounds, out float side, float requiredSide = 0f)
        {
            bounds = default; side = 0f;
            float deepest = .03f;
            Proxy selected = null;
            foreach (Proxy proxy in proxies)
            {
                if (!proxy.Active || proxy.WorldPlanes == null ||
                    (proxy.Part != Player3DAnatomicalPart.LeftThigh && proxy.Part != Player3DAnatomicalPart.RightThigh)) continue;
                if (requiredSide < 0f && proxy.Part != Player3DAnatomicalPart.LeftThigh ||
                    requiredSide > 0f && proxy.Part != Player3DAnatomicalPart.RightThigh) continue;
                for (int i = 0; i < original.Length; i++)
                {
                    if (freedom[i] < 1e-7f) continue;
                    float separation;
                    if (proxy.ScoreTree != null)
                    {
                        // Only an interior point deeper than the current winner
                        // can select a thigh. The same live plane tree returns
                        // the exact maximum plane distance without scanning
                        // every face for every free hem vertex.
                        ref int hint = ref SeparatingPlaneHint(proxy, -1);
                        if ((uint)hint >= (uint)proxy.WorldPlanes.Length) hint = 0;
                        if (!proxy.ScoreTree.NearestInside(original[i], ref hint, out separation)) continue;
                    }
                    else
                    {
                        separation = float.NegativeInfinity;
                        foreach (Plane plane in proxy.WorldPlanes)
                            separation = Mathf.Max(separation, plane.GetDistanceToPoint(original[i]));
                    }
                    if (-separation > deepest) { deepest = -separation; selected = proxy; }
                }
            }
            if (selected == null) return false;
            // Measure the posed ellipsoid in the actor's axes. Rotating the
            // actor must not change which side the hem takes around a thigh.
            Matrix4x4 matrix = actor.worldToLocalMatrix * selected.UnitToWorld;
            var extent = new Vector3(
                Mathf.Sqrt(matrix.m00 * matrix.m00 + matrix.m01 * matrix.m01 + matrix.m02 * matrix.m02),
                Mathf.Sqrt(matrix.m10 * matrix.m10 + matrix.m11 * matrix.m11 + matrix.m12 * matrix.m12),
                Mathf.Sqrt(matrix.m20 * matrix.m20 + matrix.m21 * matrix.m21 + matrix.m22 * matrix.m22));
            Vector3 center = matrix.MultiplyPoint3x4(Vector3.zero);
            bounds = new Bounds(center, extent * 2f);
            side = center.x < 0f ? -1f : 1f;
            return true;
        }

        internal bool OutsideSourceSupport(Vector3 point)
        {
            if (equipment != null && equipment.Contains(point, .0001f, includeEquipmentRibbons)) return false;
            // WorldPlanes include the selected shell's room beyond the actual posed
            // source support. Retain at least 0.1 mm outside that source hull:
            // this is a geometric acceptance check, never a travel clamp.
            foreach (Proxy proxy in proxies)
            {
                if (!proxy.Active) continue;
                if (proxy.WorldPlanes == null) return false;
                float threshold = .0001f - proxy.Selected.SurfacePadding;
                ref int hint = ref SeparatingPlaneHint(proxy, -1);
                // A hint is only an index into this live hull. Recheck the
                // exact current distance before using its clearance proof.
                if ((uint)hint < (uint)proxy.WorldPlanes.Length &&
                    proxy.WorldPlanes[hint].GetDistanceToPoint(point) >= threshold) continue;
                if (proxy.ScoreTree != null)
                {
                    if (proxy.ScoreTree.ContainsAtThreshold(point, ref hint, threshold)) return false;
                    continue;
                }
                bool separated = false;
                for (int i = 0; i < proxy.WorldPlanes.Length; i++)
                    if (proxy.WorldPlanes[i].GetDistanceToPoint(point) >= threshold)
                    { hint = i; separated = true; break; }
                if (!separated) return false;
            }
            return true;
        }

        internal bool ResolveFreeSurface(Vector3[] original, Vector3[] solved, int[] triangles,
            int[] layers, float[] depths, Vector3[] candidate, Vector3[] best)
        {
            bool residual = false;
            foreach (Vector3 point in solved)
                if (Contains(point, .0001f)) { residual = true; break; }
            for (int triangle = 0; !residual && triangle < triangles.Length; triangle += 3)
            {
                Vector3 a = solved[triangles[triangle]], b = solved[triangles[triangle + 1]], c = solved[triangles[triangle + 2]];
                residual = Contains((a + b + c) / 3f, .0001f) || Contains((a + b) * .5f, .0001f) ||
                    Contains((b + c) * .5f, .0001f) || Contains((c + a) * .5f, .0001f);
            }
            if (!residual) return false;

            // A wholly free thin panel can fold coherently onto one support
            // plane. Start from its undistorted posed shape: independent point
            // escapes may already have stretched it around opposite sides.
            // Pinned panels keep their existing area-constraint path.
            if (!freeSurfaceSupports.TryGetValue(best, out FreeSurfaceSupport remembered))
                freeSurfaceSupports.Add(best, remembered = new FreeSurfaceSupport(proxies.Length));
            Proxy cached = remembered.Proxy;
            // Recompute from the current pose and current support plane. A
            // still-feasible prior support gives a continuous cloth fold;
            // choosing it instead of searching for a new global cost minimum
            // is the bounded cosmetic simplification. No old vertices or
            // world normal are reused, and all clearance gates remain below.
            if (cached != null && cached.Active && cached.WorldPlanes != null &&
                ReferenceEquals(cached.Selected, remembered.Envelope) &&
                ReferenceEquals(cached.WorldPlanes, remembered.Planes) &&
                (uint)remembered.PlaneIndex < cached.WorldPlanes.Length &&
                Build(cached.WorldPlanes[remembered.PlaneIndex], out _) && Feasible())
            {
                Array.Copy(candidate, solved, solved.Length);
                return true;
            }
            float bestCost = float.PositiveInfinity;
            for (int proxyIndex = 0; proxyIndex < proxies.Length; proxyIndex++)
            {
                Proxy obstacle = proxies[proxyIndex];
                if (!obstacle.Active || obstacle.WorldPlanes == null || Separated(obstacle, original, proxyIndex)) continue;
                for (int planeIndex = 0; planeIndex < obstacle.WorldPlanes.Length; planeIndex++)
                {
                    if (!Build(obstacle.WorldPlanes[planeIndex], out float cost) || cost >= bestCost || !Feasible()) continue;
                    bestCost = cost;
                    Array.Copy(candidate, best, candidate.Length);
                    remembered.Proxy = obstacle;
                    remembered.Envelope = obstacle.Selected;
                    remembered.Planes = obstacle.WorldPlanes;
                    remembered.PlaneIndex = planeIndex;
                }
            }
            if (float.IsPositiveInfinity(bestCost)) return false;
            Array.Copy(best, solved, solved.Length);
            return true;

            bool Build(Plane plane, out float cost)
            {
                cost = 0f;
                Array.Clear(depths, 0, depths.Length);
                for (int i = 0; i < original.Length; i++)
                {
                    float depth = Mathf.Max(0f, .0001f - plane.GetDistanceToPoint(original[i]));
                    if (depth > .1f) return false;
                    depths[layers[i]] = Mathf.Max(depths[layers[i]], depth);
                }
                for (int i = 0; i < original.Length; i++)
                {
                    float depth = depths[layers[i]];
                    candidate[i] = original[i] + plane.normal * depth;
                    cost += depth * depth;
                }
                return true;
            }

            bool Feasible()
            {
                if (equipment != null)
                {
                    foreach (Vector3 point in candidate)
                        if (equipment.Contains(point, .0001f, includeEquipmentRibbons)) return false;
                    for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                    {
                        Vector3 a = candidate[triangles[triangle]], b = candidate[triangles[triangle + 1]], c = candidate[triangles[triangle + 2]];
                        if (equipment.Contains((a + b + c) / 3f, .0001f, includeEquipmentRibbons) ||
                            equipment.Contains((a + b) * .5f, .0001f, includeEquipmentRibbons) ||
                            equipment.Contains((b + c) * .5f, .0001f, includeEquipmentRibbons) ||
                            equipment.Contains((c + a) * .5f, .0001f, includeEquipmentRibbons)) return false;
                    }
                }
                // A proposed escape may enter another body volume. Every
                // corner/face must share a separating plane for every hull.
                for (int proxyIndex = 0; proxyIndex < proxies.Length; proxyIndex++)
                {
                    Proxy proxy = proxies[proxyIndex];
                    if (proxy.Active && (proxy.WorldPlanes == null || !Separated(proxy, candidate, proxyIndex))) return false;
                }
                return true;
            }

            bool Separated(Proxy proxy, Vector3[] points, int proxyIndex)
            {
                Plane[] planes = proxy.WorldPlanes;
                int hint = remembered.SeparatingPlaneIndices[proxyIndex];
                // The fixed proxy slot stores only an index, never old points
                // or planes. Recheck every current point against the current
                // plane; a failed hint keeps the original ordered full scan.
                if ((uint)hint < (uint)planes.Length && Separates(planes[hint])) return true;
                for (int planeIndex = 0; planeIndex < planes.Length; planeIndex++)
                {
                    if (!Separates(planes[planeIndex])) continue;
                    remembered.SeparatingPlaneIndices[proxyIndex] = planeIndex;
                    return true;
                }
                return false;

                bool Separates(Plane plane)
                {
                    foreach (Vector3 point in points)
                        if (plane.GetDistanceToPoint(point) < .00009f) return false;
                    return true;
                }
            }
        }

        public bool Contains(Vector3 worldPoint, float tolerance = .001f)
        {
            if (equipment != null && equipment.Contains(worldPoint, tolerance, includeEquipmentRibbons)) return true;
            foreach (Proxy proxy in proxies)
            {
                if (!proxy.Active || !proxy.WorldBounds.Contains(worldPoint)) continue;
                if (proxy.WorldPlanes != null)
                {
                    Plane[] planes = proxy.WorldPlanes;
                    float outsideDistance = -Mathf.Max(0f, tolerance);
                    ref int hint = ref SeparatingPlaneHint(proxy, -1);
                    // A current separating plane proves the same outside result
                    // at this tolerance; otherwise retain the ordered full scan.
                    if ((uint)hint < (uint)planes.Length &&
                        planes[hint].GetDistanceToPoint(worldPoint) >= outsideDistance) continue;
                    bool inside = true;
                    for (int i = 0; i < planes.Length; i++)
                        if (planes[i].GetDistanceToPoint(worldPoint) >= outsideDistance)
                        { hint = i; inside = false; break; }
                    if (inside) return true;
                    continue;
                }
                float limit = Mathf.Max(0f, 1f - Mathf.Max(0f, tolerance) / proxy.MinimumWorldRadius);
                if (proxy.WorldToUnit.MultiplyPoint3x4(worldPoint).sqrMagnitude < limit * limit) return true;
            }
            return false;
        }

        private static Vector3[] MeasureWorldVertices(Renderer renderer, Mesh scratch)
        {
            Vector3[] vertices;
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                // Match the production skin convention: useScale plus the full
                // renderer matrix applies FBX unit conversion exactly once.
                skin.BakeMesh(scratch, true);
                vertices = scratch.vertices;
            }
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return Array.Empty<Vector3>();
                vertices = filter.sharedMesh.vertices;
            }
            Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            return vertices;
        }

        private static Envelope BuildEnvelope(
            Player3DAnatomicalPartBinding part,
            List<Player3DAnatomicalPartBinding> parts,
            Renderer renderer,
            Vector3[] worldVertices, bool useMeshSupportPlanes, PosedMesh skin, Dictionary<Transform, LocalJoint> joints,
            float surfacePadding)
        {
            Matrix4x4 toBone = part.Bone.worldToLocalMatrix;
            var localVertices = new List<Vector3>(worldVertices.Length);
            var ownedIndices = useMeshSupportPlanes ? new List<int>(worldVertices.Length) : null;
            for (int index = 0; index < worldVertices.Length; index++)
            {
                Vector3 point = worldVertices[index];
                // A shared torso mesh is divided between chest and spine; a
                // small overlap keeps their join covered while bending.
                bool owned = true;
                foreach (Player3DAnatomicalPartBinding other in parts)
                {
                    if (other == part || other.Renderer != part.Renderer || other.Bone == part.Bone) continue;
                    Vector3 axis = other.Bone.position - part.Bone.position;
                    float length = axis.magnitude;
                    if (length > .00001f && Vector3.Dot(point - part.Bone.position, axis / length) > length * .5f + SplitOverlap)
                    {
                        owned = false;
                        break;
                    }
                }
                if (owned)
                {
                    localVertices.Add(toBone.MultiplyPoint3x4(point));
                    ownedIndices?.Add(index);
                }
            }
            if (localVertices.Count == 0) return null;
            var envelope = new Envelope { Renderer = renderer, SurfacePadding = surfacePadding };
            SetEnvelopeBounds(envelope, localVertices.ToArray(), part.Bone.localToWorldMatrix, surfacePadding);
            if (useMeshSupportPlanes)
            {
                BuildSupportPlanes(envelope, renderer, toBone, worldVertices, localVertices);
                if (envelope.SupportNormals != null && skin != null)
                {
                    envelope.Skin = skin;
                    envelope.SupportVertices = ownedIndices.ToArray();
                    envelope.PosedVertices = new Vector3[ownedIndices.Count];
                    envelope.NextPosedVertices = new Vector3[ownedIndices.Count];
                    envelope.ChangedVertices = new bool[ownedIndices.Count];
                    envelope.SupportPoints = new int[envelope.SupportNormals.Length];
                    envelope.LocalSkin = new BoneLocalSkin(skin, part.Bone, envelope.SupportVertices, joints);
                }
            }
            return envelope;
        }

        private static void SetEnvelopeBounds(Envelope envelope, Vector3[] vertices, Matrix4x4 toWorld, float surfacePadding)
        {
            Vector3 padding = new Vector3(
                surfacePadding / Mathf.Max(.00001f, toWorld.MultiplyVector(Vector3.right).magnitude),
                surfacePadding / Mathf.Max(.00001f, toWorld.MultiplyVector(Vector3.up).magnitude),
                surfacePadding / Mathf.Max(.00001f, toWorld.MultiplyVector(Vector3.forward).magnitude));
            SetEnvelopeBoundsWithPadding(envelope, vertices, padding);
        }

        private static void SetEnvelopeBoundsWithPadding(Envelope envelope, Vector3[] vertices, Vector3 padding)
        {
            int count = vertices.Length;
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < count; i++) bounds.Encapsulate(vertices[i]);
            Vector3 center = bounds.center;
            Vector3 radii = bounds.extents + padding;
            float expansion = 1f;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = vertices[i] - center;
                var unit = new Vector3(offset.x / radii.x, offset.y / radii.y, offset.z / radii.z);
                expansion = Mathf.Max(expansion, unit.magnitude);
            }
            radii *= expansion;
            envelope.BoundsInputPadding = padding;
            ApplyEnvelopeBounds(envelope, center, radii);
        }

        private static bool UsesWideMonoMagnitude()
        {
            // This probe separates the supported wide Mono expression from a
            // dot rounded to float before sqrt. Other arithmetic uses fallback.
            var probe = new Vector3(1.8662572f, 1.5749667f, -.8048444f);
            double squared = ((double)probe.x * probe.x + (double)probe.y * probe.y) + (double)probe.z * probe.z;
            return BitConverter.SingleToInt32Bits(probe.magnitude) == BitConverter.SingleToInt32Bits((float)Math.Sqrt(squared));
        }

        private static void SetEnvelopeBoundsForPose(Envelope envelope, Vector3[] vertices, Vector3 padding, SupportWorkArena arena)
        {
            if (!envelope.Skin.NativePose || !WideMonoBounds || arena == null || vertices.Length > arena.Vertices.Length || arena.Normals.Length < 2)
            {
                SetEnvelopeBoundsWithPadding(envelope, vertices, padding);
                return;
            }
            arena.UploadVertices(vertices);
            var job = new PlayerBodyBoundsJob
            {
                Vertices = arena.Vertices.GetSubArray(0, vertices.Length),
                Result = arena.Normals.GetSubArray(0, 2), Padding = padding
            };
            PlayerBodyBoundsJob.RunSynchronous(ref job);
            envelope.BoundsInputPadding = padding;
            ApplyEnvelopeBounds(envelope, arena.Normals[0], arena.Normals[1]);
            envelope.HasNativeBounds = true;
        }

        private static void ApplyEnvelopeBounds(Envelope envelope, Vector3 center, Vector3 radii)
        {
            envelope.BoundsCenter = center;
            envelope.BoundsRadii = radii;
            Matrix4x4 unitToBone = Matrix4x4.TRS(center, Quaternion.identity, radii);
            envelope.UnitToBone = unitToBone;
            envelope.BoneToUnit = unitToBone.inverse;
        }

        private static void RefreshSupportEnvelope(Envelope envelope, Matrix4x4 worldToBone,
            Matrix4x4 boneToWorld, uint version, SupportWorkArena supportWork = null)
        {
            if (envelope.Skin == null) return;
            // The arena is shared by envelopes during this one pose. Ownership
            // never survives a new cloud update or crosses to another envelope.
            if (supportWork != null) supportWork.UploadedVertices = null;
            envelope.Skin.UpdatePose(version);
            using var profile = envelope.Skin.Profile ? SupportMarker.Auto() : default;
            bool localChanged = envelope.LocalSkin.Update(envelope.NextPosedVertices, version, supportWork);
            bool changed = envelope.SupportTree == null;
            float surfacePadding = envelope.SurfacePadding;
            using (envelope.Skin.Profile ? SupportBoundsMarker.Auto() : default)
            {
                Vector3 padding = new Vector3(
                    surfacePadding / Mathf.Max(.00001f, boneToWorld.MultiplyVector(Vector3.right).magnitude),
                    surfacePadding / Mathf.Max(.00001f, boneToWorld.MultiplyVector(Vector3.up).magnitude),
                    surfacePadding / Mathf.Max(.00001f, boneToWorld.MultiplyVector(Vector3.forward).magnitude));
                if (!changed && !localChanged && envelope.CloudMatches)
                {
                    if ((padding - envelope.BoundsPadding).sqrMagnitude > 1e-18f)
                        SetEnvelopeBoundsForPose(envelope, envelope.PosedVertices, padding, supportWork);
                    envelope.BoundsPadding = padding;
                    return;
                }
                bool matches = true;
                for (int i = 0; i < envelope.PosedVertices.Length; i++)
                {
                    Vector3 point = envelope.NextPosedVertices[i];
                    envelope.ChangedVertices[i] = !point.Equals(envelope.PosedVertices[i]);
                    matches &= !envelope.ChangedVertices[i];
                    // Compare against the last supported cloud, so sub-micrometre
                    // changes accumulate instead of freezing a slowly moving skin.
                    if (!changed && envelope.ChangedVertices[i] &&
                        boneToWorld.MultiplyVector(point - envelope.PosedVertices[i]).sqrMagnitude > 1e-12f)
                        changed = true;
                }
                envelope.CloudMatches = matches;
                if (!changed)
                {
                    if ((padding - envelope.BoundsPadding).sqrMagnitude > 1e-18f)
                        SetEnvelopeBoundsForPose(envelope, envelope.PosedVertices, padding, supportWork);
                    envelope.BoundsPadding = padding;
                    return;
                }
                Array.Copy(envelope.NextPosedVertices, envelope.PosedVertices, envelope.PosedVertices.Length);
                envelope.CloudMatches = true;
                if (supportWork != null && ReferenceEquals(supportWork.UploadedVertices, envelope.NextPosedVertices))
                    supportWork.UploadedVertices = envelope.PosedVertices;
                SetEnvelopeBoundsForPose(envelope, envelope.PosedVertices, padding, supportWork);
                envelope.BoundsPadding = padding;
            }
            if (envelope.SupportTree == null) envelope.SupportTree = new VertexSupportTree(envelope.PosedVertices, envelope.ChangedVertices);
            else if (!envelope.Skin.Profile) envelope.SupportTree.Refit();
            if (envelope.Skin.Profile)
                envelope.SupportTree.MaximumBatch(envelope.SupportNormals, envelope.SupportOffsets, envelope.SupportPoints, supportWork);
            else
                for (int i = 0; i < envelope.SupportNormals.Length; i++)
                    envelope.SupportOffsets[i] = envelope.SupportTree.Maximum(envelope.SupportNormals[i], ref envelope.SupportPoints[i]);
            unchecked { envelope.SupportVersion++; }
        }

        private static void BuildSupportPlanes(Envelope envelope, Renderer renderer, Matrix4x4 toBone,
            Vector3[] worldVertices, List<Vector3> ownedVertices)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !mesh.isReadable) return;
            int[] triangles = mesh.triangles;
            var normals = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = toBone.MultiplyPoint3x4(worldVertices[triangles[i]]);
                Vector3 b = toBone.MultiplyPoint3x4(worldVertices[triangles[i + 1]]);
                Vector3 c = toBone.MultiplyPoint3x4(worldVertices[triangles[i + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                float length = normal.magnitude;
                if (length < 1e-16f) continue;
                normal /= length; // Imported bone coordinates can be much smaller than Vector3.Normalize's epsilon.
                bool duplicate = false;
                foreach (Vector3 previous in normals)
                    if (Vector3.Dot(normal, previous) > .9999f) { duplicate = true; break; }
                if (!duplicate) normals.Add(normal);
            }
            var offsets = new float[normals.Count];
            for (int i = 0; i < normals.Count; i++)
            {
                float maximum = float.NegativeInfinity;
                foreach (Vector3 vertex in ownedVertices) maximum = Mathf.Max(maximum, Vector3.Dot(normals[i], vertex));
                offsets[i] = maximum;
            }
            // Each half-space supports the real vertex cloud, including mildly
            // non-planar authored quads. This keeps every source point enclosed
            // without the empty corner volume of an expanded ellipsoid.
            envelope.SupportNormals = normals.ToArray(); envelope.SupportOffsets = offsets;
        }
    }
}
