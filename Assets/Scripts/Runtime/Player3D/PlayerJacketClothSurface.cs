using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Owns a deformable mesh; imported geometry, materials and skin weights stay shared and immutable.</summary>
    internal sealed class PlayerJacketClothSurface : IDisposable
    {
        private static readonly ProfilerMarker MaterialMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Jacket.Material");
        private static readonly ProfilerMarker AreaMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Jacket.Area");
        private static readonly ProfilerMarker WriteMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Jacket.Write");
        private static readonly ProfilerMarker FreeSurfaceMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Jacket.FreeSurface");
        private static readonly ProfilerMarker NativeSolverMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Jacket.NativeSolver");
        private static readonly ProfilerMarker NativeAreaMarker = new ProfilerMarker("BarPromenade.SecondaryMotion.Jacket.NativeArea");
        internal static long TotalNativeSolves { get; private set; }
        internal static long TotalBurstNativeSolves { get; private set; }
        internal static long TotalNativeAreaSolves { get; private set; }
        internal static long TotalBurstNativeAreaSolves { get; private set; }
        internal static long TotalFreeSurfaceMemoHits { get; private set; }
        internal static long TotalFreeSurfaceQueries { get; private set; }
        private readonly Matrix4x4[] bindposes;
        private readonly Matrix4x4[] matrices;
        private readonly Transform[] bones;
        private readonly BoneWeight[] skinWeights;
        private readonly int[] vertexSkins;
        private readonly Matrix4x4[] skins;
        private readonly Matrix4x4[] inverseSkins;
        private readonly bool[] deformSkins;
        private readonly Vector3[] original;
        private readonly Vector3[] sourceNormals;
        private readonly Vector3[] output;
        private readonly Vector3[] writtenOutput;
        private readonly Vector3[] world;
        private readonly Vector3[] foldDelta;
        private readonly Vector3[] posedOriginal;
        private readonly float[] contactFreedom;
        private readonly float[] baseContactFreedom;
        private readonly int[] contactVertices;
        private readonly int[] contactTriangles;
        private readonly Vector3[] clearVertexPoints, clearAreaPoints;
        private readonly bool[] clearVertices, clearAreaSamples;
        private PlayerScarfBodyContacts.PointQueryCache pointQueryCache;
        private readonly Vector3[] freeSurfaceOriginal, freeSurfaceCandidate, freeSurfaceBest;
        private readonly Vector3[] falseFreeOriginal, falseFreeInput;
        private bool falseFreeValid;
        private PlayerScarfBodyContacts falseFreeOwner;
        private uint falseFreePoseVersion, falseFreeEquipmentVersion;
        private readonly int[] freeSurfaceLayers;
        private readonly float[] freeSurfaceDepths;
        private readonly PanelShape panelShape;
        private NativeWork nativeWork;
        private readonly NativeSnapshot nativeSnapshot;
        private readonly Transform shoulder, elbow, wrist;
        private readonly int foldShape;
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly bool normalMapped;
        private readonly PlayerJacketCloth.SurfaceBinding binding;
        private readonly Vector3[] bindActorPoints;
        private readonly Transform actor;
        private int weaponAccessVertex = -1;
        public Mesh Mesh { get; }
        public Mesh Source => binding.Source;
        public SkinnedMeshRenderer Renderer => binding.Renderer;
        internal uint GeometryVersion { get; private set; }
        public int VertexCount => original.Length;
        internal bool IsHemPanel => panelShape != null && panelShape.HasPins;
        public Vector3 Original(int index) => original[index];
        public Vector3 World(int index) => world[index];
        public Vector3 WorldNormal(int index) => Skin(index).inverse.transpose.MultiplyVector(normals[index]).normalized;
        public Vector3 BindWorldNormal(int index) => Skin(index).inverse.transpose.MultiplyVector(sourceNormals[index]).normalized;

        public PlayerJacketClothSurface(PlayerJacketCloth.SurfaceBinding source,
            Transform shoulder = null, Transform elbow = null, Transform wrist = null, Transform actor = null,
            NativeSnapshot nativeSnapshot = null)
        {
            binding = source;
            this.nativeSnapshot = nativeSnapshot;
            this.actor = actor;
            this.shoulder = shoulder; this.elbow = elbow; this.wrist = wrist;
            Material material = source.Renderer.sharedMaterial;
            normalMapped = material != null && material.IsKeywordEnabled("_NORMALMAP");
            original = source.Source.vertices;
            sourceNormals = source.Source.normals;
            posedOriginal = (Vector3[])original.Clone();
            foldShape = CharacterJointDeformation.FindShape(source.Source, source.Region == 1 ? "JacketElbowFold.L" :
                source.Region == 2 ? "JacketElbowFold.R" : "");
            if (foldShape >= 0)
            {
                if (shoulder == null || elbow == null || wrist == null)
                    throw new InvalidOperationException("An authored sleeve correction requires all three arm anchors.");
                foldDelta = new Vector3[original.Length];
                int lastFrame = source.Source.GetBlendShapeFrameCount(foldShape) - 1;
                source.Source.GetBlendShapeFrameVertices(foldShape, lastFrame, foldDelta, null, null);
            }
            output = (Vector3[])original.Clone();
            writtenOutput = (Vector3[])original.Clone();
            contactFreedom = (float[])source.Freedom.Clone();
            if (foldDelta != null)
                for (int i = 0; i < contactFreedom.Length; i++)
                    if (foldDelta[i].sqrMagnitude > 1e-14f) contactFreedom[i] = 1f;
            baseContactFreedom = (float[])contactFreedom.Clone();
            world = new Vector3[original.Length];
            clearVertexPoints = new Vector3[original.Length];
            clearVertices = new bool[original.Length];
            // Identical authored weights share exactly the same posed skin matrix.
            // Cache their inverse too: every free vertex used to rebuild and invert it.
            BoneWeight[] weights = source.Source.boneWeights;
            vertexSkins = new int[weights.Length];
            var weightIndices = new Dictionary<BoneWeight, int>(ExactWeights.Instance);
            var uniqueWeights = new List<BoneWeight>();
            for (int i = 0; i < weights.Length; i++)
            {
                if (!weightIndices.TryGetValue(weights[i], out int skin))
                {
                    skin = uniqueWeights.Count;
                    weightIndices.Add(weights[i], skin);
                    uniqueWeights.Add(weights[i]);
                }
                vertexSkins[i] = skin;
            }
            skinWeights = uniqueWeights.ToArray();
            if (source.Region == 0)
            {
                // Imported UV splits share one physical cloth point. Cache
                // that correspondence once, so finite-area contacts cannot
                // pull the corners of adjacent UV charts apart.
                contactVertices = new int[original.Length];
                var coincident = new Dictionary<(int, Vector3), int>();
                for (int i = 0; i < original.Length; i++)
                {
                    var key = (vertexSkins[i], original[i]);
                    if (!coincident.TryGetValue(key, out int vertex)) coincident.Add(key, vertex = i);
                    contactVertices[i] = vertex;
                }
                var triangles = new List<int>();
                int[] sourceTriangles = source.Source.triangles;
                for (int i = 0; i < sourceTriangles.Length; i += 3)
                {
                    int a = contactVertices[sourceTriangles[i]], b = contactVertices[sourceTriangles[i + 1]],
                        c = contactVertices[sourceTriangles[i + 2]];
                    if (a == b || b == c || c == a) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                }
                contactTriangles = triangles.ToArray();
                clearAreaPoints = new Vector3[contactTriangles.Length / 3 * 4];
                clearAreaSamples = new bool[clearAreaPoints.Length];
                bool fullyFree = original.Length > 0;
                foreach (float freedom in contactFreedom) fullyFree &= freedom > 0f;
                if (fullyFree)
                {
                    freeSurfaceOriginal = new Vector3[original.Length];
                    freeSurfaceCandidate = new Vector3[original.Length];
                    freeSurfaceBest = new Vector3[original.Length];
                    falseFreeOriginal = new Vector3[original.Length];
                    falseFreeInput = new Vector3[original.Length];
                    freeSurfaceDepths = new float[original.Length];
                    freeSurfaceLayers = new int[original.Length];
                    // The thin constructed panels extrude along bind Z.
                    // Move their front/back pair together to keep thickness,
                    // including UV-split copies of either layer.
                    var layers = new Dictionary<(int, Vector2), int>();
                    for (int i = 0; i < original.Length; i++)
                    {
                        var key = (vertexSkins[i], new Vector2(original[i].x, original[i].y));
                        if (!layers.TryGetValue(key, out int vertex)) layers.Add(key, vertex = i);
                        freeSurfaceLayers[i] = vertex;
                    }
                }
            }
            skins = new Matrix4x4[skinWeights.Length];
            inverseSkins = new Matrix4x4[skinWeights.Length];
            deformSkins = new bool[skinWeights.Length];
            for (int i = 0; i < weights.Length; i++)
                deformSkins[vertexSkins[i]] = true;
            bindposes = source.Source.bindposes;
            bones = source.Renderer.bones;
            matrices = new Matrix4x4[bindposes.Length];
            Mesh = UnityEngine.Object.Instantiate(source.Source);
            Mesh.name = source.Source.name + " Jacket Motion";
            Mesh.hideFlags = HideFlags.HideAndDontSave;
            Mesh.MarkDynamic();
            source.Renderer.sharedMesh = Mesh;
            Mesh.GetNormals(normals);
            UpdatePose();
            if (source.Region == 0 && actor != null)
            {
                bindActorPoints = new Vector3[world.Length];
                for (int i = 0; i < world.Length; i++) bindActorPoints[i] = actor.InverseTransformPoint(world[i]);
            }
            if (source.Region == 0 && actor != null)
                panelShape = new PanelShape(world, original, source.Renderer.localToWorldMatrix, actor,
                    contactFreedom, contactVertices, contactTriangles, vertexSkins);
        }

        public void UpdatePose()
        {
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
            for (int i = 0; i < skins.Length; i++)
            {
                skins[i] = BuildSkin(skinWeights[i]);
                if (deformSkins[i]) inverseSkins[i] = skins[i].inverse;
            }
            float fold = 0f;
            if (foldDelta != null)
            {
                float angle = Vector3.Angle(elbow.position - shoulder.position, wrist.position - elbow.position);
                float minimumCosine = Mathf.Cos(135f * Mathf.Deg2Rad * .5f);
                float cosine = Mathf.Max(minimumCosine, Mathf.Cos(angle * Mathf.Deg2Rad * .5f));
                fold = Mathf.Clamp01((1f / cosine - 1f) / (1f / minimumCosine - 1f));
                // The composed vertices already contain this shape; renderer skinning must not add it twice.
                if (Renderer.GetBlendShapeWeight(foldShape) != 0f)
                    Renderer.SetBlendShapeWeight(foldShape, 0f);
            }
            for (int i = 0; i < original.Length; i++)
            {
                posedOriginal[i] = original[i] + (foldDelta == null ? Vector3.zero : foldDelta[i] * fold);
                world[i] = Skin(i).MultiplyPoint3x4(posedOriginal[i]);
            }
            panelShape?.CapturePose(world);
        }

        public Matrix4x4 Skin(int vertex) => skins[vertexSkins[vertex]];

        private Matrix4x4 BuildSkin(BoneWeight weight)
        {
            Matrix4x4 result = Scale(matrices[weight.boneIndex0], weight.weight0);
            Add(ref result, weight.boneIndex1, weight.weight1);
            Add(ref result, weight.boneIndex2, weight.weight2);
            Add(ref result, weight.boneIndex3, weight.weight3);
            return result;
        }

        private void Add(ref Matrix4x4 result, int bone, float weight)
        {
            if (weight <= 0f) return;
            Matrix4x4 value = matrices[bone];
            for (int i = 0; i < 4; i++) result.SetColumn(i, result.GetColumn(i) + value.GetColumn(i) * weight);
        }

        private static Matrix4x4 Scale(Matrix4x4 value, float weight)
        {
            for (int i = 0; i < 4; i++) value.SetColumn(i, value.GetColumn(i) * weight);
            return value;
        }

        public int Deform(Vector3[] displacements, PlayerScarfBodyContacts contacts,
            Transform heldPanelGrip = null, float weaponAccessAmount = 0f)
        {
            falseFreeValid = false;
            if (pointQueryCache == null || !ReferenceEquals(pointQueryCache.Owner, contacts))
                pointQueryCache = contacts.CreatePointQueryCache(world.Length + (clearAreaPoints?.Length ?? 0));
            // The posed body/equipment stays fixed throughout this solve.
            // Reuse only an unchanged point that was already found clear.
            Array.Clear(clearVertices, 0, clearVertices.Length);
            if (clearAreaSamples != null) Array.Clear(clearAreaSamples, 0, clearAreaSamples.Length);
            UpdateWeaponAccessFreedom(heldPanelGrip, weaponAccessAmount);
            ApplyWeaponAccess(heldPanelGrip, weaponAccessAmount);
            for (int i = 0; i < world.Length; i++)
            {
                float freedom = binding.Freedom[i];
                if (freedom <= 0f) continue;
                world[i] += Vector3.Lerp(displacements[binding.FirstNode[i]], displacements[binding.SecondNode[i]],
                    binding.SecondWeight[i]) * freedom;
            }
            if (freeSurfaceOriginal != null) Array.Copy(world, freeSurfaceOriginal, world.Length);
            panelShape?.CaptureRaw(world);
            int contactCount;
            // A whole free patch can already share one current support plane
            // outside every body hull/gear and across every sampled face. If
            // its material edges also need no shortening, repeating point
            // and area escapes adds work without changing a valid surface.
            if (!TryNativeSolve(contacts, world, out contactCount))
            {
                bool coherent = ResolveFreePanelSurface(contacts, world);
                if (coherent && (panelShape == null ||
                    panelShape.LimitMaterialDistances(world, contacts, passes: 1, queryCache: pointQueryCache)))
                {
                    Array.Copy(freeSurfaceOriginal, world, world.Length);
                    coherent = false;
                }
                if (coherent) contactCount = world.Length;
                else
                {
                    contactCount = ResolveSurfacePoints(contacts, world);
                    contactCount += ResolvePanelContacts(contacts, areaPasses: freeSurfaceOriginal == null ? 4 : 2);
                    if (ResolveFreePanelSurface(contacts, world)) contactCount += world.Length;
                    panelShape?.LimitMaterialDistances(world, contacts, queryCache: pointQueryCache);
                }
            }
            for (int i = 0; i < output.Length; i++)
                output[i] = contactFreedom[i] <= 0f ? posedOriginal[i] : inverseSkins[vertexSkins[i]].MultiplyPoint3x4(world[i]);
            Write();
            return contactCount;
        }

        private bool TryNativeSolve(PlayerScarfBodyContacts contacts, Vector3[] positions, out int contactCount)
        {
            contactCount = 0;
            if (panelShape == null || !panelShape.HasPins || nativeSnapshot == null || !PlayerScarfBodyContacts.NativeClothSupported) return false;
            nativeWork ??= panelShape.CreateNativeWork();
            using var profile = NativeSolverMarker.Auto();
            if (!nativeWork.Run(positions, contactFreedom, panelShape.MaterialLengths, contacts, nativeSnapshot, out contactCount)) return false;
            TotalNativeSolves++;
            if (nativeWork.ExecutedInBurst) TotalBurstNativeSolves++;
            return true;
        }

        private bool TryNativeArea(PlayerScarfBodyContacts contacts, Vector3[] positions,
            bool constrainMaterial, float maximumStretch, int areaPasses, out int contactCount)
        {
            contactCount = 0;
            if (panelShape == null || nativeSnapshot == null || !PlayerScarfBodyContacts.NativeClothSupported) return false;
            nativeWork ??= panelShape.CreateNativeWork();
            using var profile = NativeAreaMarker.Auto();
            if (!nativeWork.Run(positions, contactFreedom, panelShape.MaterialLengths, contacts, nativeSnapshot, out contactCount,
                initialResolve: false, finalMaterial: false, constrainMaterial: constrainMaterial,
                maximumStretch: maximumStretch, areaPasses: areaPasses,
                materialPasses: freeSurfaceOriginal == null ? 8 : 2)) return false;
            TotalNativeAreaSolves++;
            if (nativeWork.ExecutedInBurst) TotalBurstNativeAreaSolves++;
            return true;
        }

        internal bool TryCompareNativeSolver(PlayerScarfBodyContacts contacts, out Vector3[] managed,
            out Vector3[] native, out int managedContacts, out int nativeContacts, out bool executedInBurst)
        {
            managed = native = null; managedContacts = nativeContacts = 0; executedInBurst = false;
            if (panelShape == null || !panelShape.HasPins) return false;
            if (nativeSnapshot == null || !nativeSnapshot.Capture(contacts, force: true)) return false;
            managed = (Vector3[])panelShape.Raw.Clone(); native = (Vector3[])panelShape.Raw.Clone();
            Array.Clear(clearVertices, 0, clearVertices.Length);
            Array.Clear(clearAreaSamples, 0, clearAreaSamples.Length);
            managedContacts = ResolveSurfacePoints(contacts, managed);
            managedContacts += ResolvePanelContacts(contacts, managed, preferNative: false);
            panelShape.LimitMaterialDistances(managed, contacts, queryCache: pointQueryCache);
            if (!TryNativeSolve(contacts, native, out nativeContacts)) return false;
            executedInBurst = nativeWork.ExecutedInBurst;
            return true;
        }

        internal bool TryCompareNativeArea(PlayerScarfBodyContacts contacts, bool constrainMaterial, int areaPasses,
            out Vector3[] managed, out Vector3[] native, out int managedContacts, out int nativeContacts,
            out bool executedInBurst)
        {
            managed = native = null; managedContacts = nativeContacts = 0; executedInBurst = false;
            if (panelShape == null || contactTriangles == null || contactTriangles.Length == 0) return false;
            if (nativeSnapshot == null || !nativeSnapshot.Capture(contacts, force: true)) return false;
            managed = (Vector3[])panelShape.Raw.Clone();
            Array.Clear(clearVertices, 0, clearVertices.Length);
            Array.Clear(clearAreaSamples, 0, clearAreaSamples.Length);
            ResolveSurfacePoints(contacts, managed);
            native = (Vector3[])managed.Clone();
            managedContacts = ResolvePanelContacts(contacts, managed, constrainMaterial, areaPasses: areaPasses,
                preferNative: false);
            if (!TryNativeArea(contacts, native, constrainMaterial, 1.16f, areaPasses, out nativeContacts)) return false;
            executedInBurst = nativeWork.ExecutedInBurst;
            return true;
        }

        // One immutable snapshot of this jacket owner's contacts pose. Every
        // surface job reads it synchronously; each keeps its own mutable solve.
        internal sealed class NativeSnapshot : IDisposable
        {
            private NativeArray<PlayerJacketSurfaceJob.BodyProxy> bodies;
            private NativeArray<Plane> planes;
            private NativeArray<PlayerEquipmentContacts.BoxSnapshot> boxes;
            private PlayerScarfBodyContacts owner;
            private uint poseVersion, equipmentVersion;
            private bool ready;
            private int bodyCount, planeCount, boxCount;
            public int BodyCount => bodyCount;
            public NativeArray<PlayerJacketSurfaceJob.BodyProxy> Bodies => bodies.GetSubArray(0, bodyCount);
            public NativeArray<Plane> Planes => planes.GetSubArray(0, planeCount);
            public NativeArray<PlayerEquipmentContacts.BoxSnapshot> Boxes => boxes.GetSubArray(0, boxCount);
            public bool HasEquipmentBounds { get; private set; }
            public Vector3 EquipmentMinimum { get; private set; }
            public Vector3 EquipmentMaximum { get; private set; }
            public long CaptureCount { get; private set; }
            public long ReuseCount { get; private set; }

            public bool Capture(PlayerScarfBodyContacts contacts, bool force = false)
            {
                if (!force && ReferenceEquals(owner, contacts) && poseVersion == contacts.ClothPoseVersion &&
                    equipmentVersion == contacts.ClothEquipmentVersion)
                {
                    if (ready) ReuseCount++;
                    return ready;
                }
                ready = false;
                owner = contacts; poseVersion = contacts.ClothPoseVersion; equipmentVersion = contacts.ClothEquipmentVersion;
                if (!contacts.TryGetClothSnapshotSizes(out bodyCount, out planeCount, out boxCount)) return false;
                try
                {
                    Ensure(ref bodies, bodyCount); Ensure(ref planes, Math.Max(1, planeCount)); Ensure(ref boxes, Math.Max(32, boxCount));
                    contacts.CopyClothSnapshot(bodies, planes, boxes);
                    HasEquipmentBounds = contacts.TryGetClothEquipmentBounds(out Bounds equipmentBounds);
                    EquipmentMinimum = equipmentBounds.min; EquipmentMaximum = equipmentBounds.max;
                    ready = true;
                    CaptureCount++;
                    return true;
                }
                catch { Dispose(); throw; }
            }

            private static void Ensure<T>(ref NativeArray<T> buffer, int count) where T : struct
            {
                if (buffer.IsCreated && buffer.Length >= count) return;
                Dispose(ref buffer);
                buffer = new NativeArray<T>(Mathf.NextPowerOfTwo(Math.Max(1, count)), Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            }
            private static void Dispose<T>(ref NativeArray<T> buffer) where T : struct
            { if (buffer.IsCreated) buffer.Dispose(); buffer = default; }
            public void Dispose()
            {
                Dispose(ref bodies); Dispose(ref planes); Dispose(ref boxes);
                owner = null; ready = false;
            }
        }

        private sealed class NativeWork : IDisposable
        {
            private NativeArray<Vector3> positions, clearVertexPoints, clearAreaPoints;
            private NativeArray<float> freedom, lengths;
            private NativeArray<int> canonical, layers, pinned, layerFirst, layerMembers, populated, duplicates, triangles,
                moved, clearVertices, clearArea, hints;
            private NativeArray<PlayerJacketSurfaceJob.Edge> edges;
            public bool ExecutedInBurst { get; private set; }

            public NativeWork(float[] freedom, int[] canonical, int[] layers, bool[] pinned,
                (int A, int B)[] edges, float[] lengths, int[][] members, int[] populated, int[] duplicates, int[] triangles)
            {
                try
                {
                    int count = freedom.Length, samples = triangles.Length / 3 * 4;
                    positions = Buffer<Vector3>(count); clearVertexPoints = Buffer<Vector3>(count); clearAreaPoints = Buffer<Vector3>(samples);
                    this.freedom = new NativeArray<float>(freedom, Allocator.Persistent);
                    this.lengths = new NativeArray<float>(lengths, Allocator.Persistent);
                    this.canonical = new NativeArray<int>(canonical, Allocator.Persistent);
                    this.layers = new NativeArray<int>(layers, Allocator.Persistent);
                    this.populated = new NativeArray<int>(populated, Allocator.Persistent);
                    this.duplicates = new NativeArray<int>(duplicates, Allocator.Persistent);
                    this.triangles = new NativeArray<int>(triangles, Allocator.Persistent);
                    this.pinned = Buffer<int>(count); this.edges = Buffer<PlayerJacketSurfaceJob.Edge>(edges.Length);
                    for (int i = 0; i < count; i++) this.pinned[i] = pinned[i] ? 1 : 0;
                    for (int i = 0; i < edges.Length; i++) this.edges[i] = new PlayerJacketSurfaceJob.Edge { A = edges[i].A, B = edges[i].B };
                    var first = new int[count + 1]; var flat = new List<int>();
                    for (int i = 0; i < count; i++) { first[i] = flat.Count; if (members[i] != null) flat.AddRange(members[i]); }
                    first[count] = flat.Count;
                    layerFirst = new NativeArray<int>(first, Allocator.Persistent);
                    layerMembers = new NativeArray<int>(flat.ToArray(), Allocator.Persistent);
                    moved = Buffer<int>(count); clearVertices = Buffer<int>(count); clearArea = Buffer<int>(samples);
                }
                catch { Dispose(); throw; }
            }

            public bool Run(Vector3[] solved, float[] currentFreedom, float[] currentLengths,
                PlayerScarfBodyContacts contacts, NativeSnapshot snapshot, out int contactCount, bool initialResolve = true,
                bool finalMaterial = true, bool constrainMaterial = true, float maximumStretch = 1.16f,
                int areaPasses = 4, int materialPasses = 8)
            {
                contactCount = 0;
                if (!snapshot.Capture(contacts)) return false;
                foreach (Vector3 p in solved)
                    if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z)) return false;
                int hintCount = (solved.Length + clearArea.Length) * snapshot.BodyCount;
                if (!hints.IsCreated || hints.Length != hintCount)
                { Dispose(ref hints); hints = new NativeArray<int>(hintCount, Allocator.Persistent); }
                NativeArray<Vector3>.Copy(solved, positions);
                NativeArray<float>.Copy(currentFreedom, freedom); NativeArray<float>.Copy(currentLengths, lengths);
                var job = new PlayerJacketSurfaceJob
                {
                    Positions = positions, Freedom = freedom, MaterialLengths = lengths, Canonical = canonical, Layers = layers,
                    Pinned = pinned, LayerFirst = layerFirst, LayerMembers = layerMembers, PopulatedLayers = populated,
                    DuplicateVertices = duplicates, Triangles = triangles, Edges = edges,
                    Bodies = snapshot.Bodies, Planes = snapshot.Planes, Boxes = snapshot.Boxes,
                    MovedLayers = moved, ClearVertices = clearVertices, ClearArea = clearArea, PlaneHints = hints,
                    ClearVertexPoints = clearVertexPoints, ClearAreaPoints = clearAreaPoints, MaximumStretch = maximumStretch,
                    InitialResolve = initialResolve ? 1 : 0, FinalMaterial = finalMaterial ? 1 : 0,
                    ConstrainMaterial = constrainMaterial ? 1 : 0, AreaPasses = areaPasses, MaterialPasses = materialPasses,
                    HasEquipmentBounds = snapshot.HasEquipmentBounds ? 1 : 0,
                    EquipmentMinimum = snapshot.EquipmentMinimum, EquipmentMaximum = snapshot.EquipmentMaximum
                };
                PlayerJacketSurfaceJob.RunSynchronous(ref job);
                NativeArray<Vector3>.Copy(positions, solved);
                ExecutedInBurst = job.ManagedExecution == 0; contactCount = job.ContactCount;
                return true;
            }

            private static NativeArray<T> Buffer<T>(int count) where T : struct =>
                new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            private static void Dispose<T>(ref NativeArray<T> buffer) where T : struct
            { if (buffer.IsCreated) buffer.Dispose(); buffer = default; }
            public void Dispose()
            {
                Dispose(ref positions); Dispose(ref clearVertexPoints); Dispose(ref clearAreaPoints); Dispose(ref freedom); Dispose(ref lengths);
                Dispose(ref canonical); Dispose(ref layers); Dispose(ref pinned); Dispose(ref layerFirst); Dispose(ref layerMembers);
                Dispose(ref populated); Dispose(ref duplicates); Dispose(ref triangles); Dispose(ref moved); Dispose(ref clearVertices);
                Dispose(ref clearArea); Dispose(ref hints); Dispose(ref edges);
            }
        }

        private int ResolveSurfacePoints(PlayerScarfBodyContacts contacts, Vector3[] positions)
        {
            int count = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (contactFreedom[i] <= 0f) continue;
                Vector3 point = positions[i];
                if (clearVertices[i] && point.Equals(clearVertexPoints[i])) continue;
                if (contacts.ResolvePointCached(ref point, pointQueryCache, i))
                {
                    positions[i] = point;
                    clearVertices[i] = false;
                    count++;
                }
                else { clearVertices[i] = true; clearVertexPoints[i] = point; }
            }
            return count;
        }

        private bool ResolveFreePanelSurface(PlayerScarfBodyContacts contacts, Vector3[] positions)
        {
            if (freeSurfaceOriginal == null) return false;
            using var profile = FreeSurfaceMarker.Auto();
            TotalFreeSurfaceQueries++;
            if (falseFreeValid && ReferenceEquals(falseFreeOwner, contacts) &&
                falseFreePoseVersion == contacts.ClothPoseVersion &&
                falseFreeEquipmentVersion == contacts.ClothEquipmentVersion &&
                SamePoints(freeSurfaceOriginal, falseFreeOriginal) && SamePoints(positions, falseFreeInput))
            {
                TotalFreeSurfaceMemoHits++;
                return false;
            }
            bool result = contacts.ResolveFreeSurface(freeSurfaceOriginal, positions, contactTriangles,
                freeSurfaceLayers, freeSurfaceDepths, freeSurfaceCandidate, freeSurfaceBest);
            falseFreeValid = !result;
            if (!result)
            {
                Array.Copy(freeSurfaceOriginal, falseFreeOriginal, freeSurfaceOriginal.Length);
                Array.Copy(positions, falseFreeInput, positions.Length);
                falseFreeOwner = contacts;
                falseFreePoseVersion = contacts.ClothPoseVersion;
                falseFreeEquipmentVersion = contacts.ClothEquipmentVersion;
            }
            return result;

            static bool SamePoints(Vector3[] a, Vector3[] b)
            {
                for (int i = 0; i < a.Length; i++)
                    if (a[i].x != b[i].x || a[i].y != b[i].y || a[i].z != b[i].z) return false;
                return true;
            }
        }

        internal bool TryCompareFreeSurfaceMemo(PlayerScarfBodyContacts contacts, Vector3 originalOffset,
            Vector3 inputOffset, Action invalidateContacts, out Vector3[] reference, out Vector3[] cached,
            out bool referenceResult, out bool cachedResult, out long reuseDelta)
        {
            reference = cached = null; referenceResult = cachedResult = false; reuseDelta = 0;
            if (freeSurfaceOriginal == null) return false;
            Vector3[] savedOriginal = (Vector3[])freeSurfaceOriginal.Clone();
            try
            {
                Array.Copy(world, freeSurfaceOriginal, world.Length);
                cached = (Vector3[])world.Clone();
                falseFreeValid = false;
                if (ResolveFreePanelSurface(contacts, cached)) return false;
                for (int i = 0; i < cached.Length; i++)
                { freeSurfaceOriginal[i] += originalOffset; cached[i] += inputOffset; }
                invalidateContacts?.Invoke();
                reference = (Vector3[])cached.Clone();
                referenceResult = contacts.ResolveFreeSurface(freeSurfaceOriginal, reference, contactTriangles,
                    freeSurfaceLayers, freeSurfaceDepths, freeSurfaceCandidate, freeSurfaceBest);
                long before = TotalFreeSurfaceMemoHits;
                cachedResult = ResolveFreePanelSurface(contacts, cached);
                reuseDelta = TotalFreeSurfaceMemoHits - before;
                return true;
            }
            finally
            {
                Array.Copy(savedOriginal, freeSurfaceOriginal, savedOriginal.Length);
                falseFreeValid = false;
            }
        }

        internal void ClearWeaponAccess() => weaponAccessVertex = -1;

        private void UpdateWeaponAccessFreedom(Transform grip, float amount)
        {
            if (bindActorPoints == null) return;
            float handHeight = grip == null ? 0f : actor.InverseTransformPoint(grip.position).y;
            for (int i = 0; i < world.Length; i++)
            {
                // The left hand can lift the chest-height inner edge above the
                // ordinary free hem. Release only its local access region.
                Vector3 bind = bindActorPoints[i];
                bool access = amount > 0f && grip != null && bind.x < -.01f && bind.z > .025f &&
                    Mathf.Abs(actor.InverseTransformPoint(world[i]).y - handHeight) < .20f;
                contactFreedom[i] = access ? 1f : baseContactFreedom[i];
            }
        }

        private void ApplyWeaponAccess(Transform grip, float amount)
        {
            if (grip == null || amount <= 0f || bindActorPoints == null)
            { weaponAccessVertex = -1; return; }
            // Grasp the free inner edge of the left front panel. Picking the
            // closest outer panel only dents the pocket and leaves the jacket
            // opening across the holster. The actual hand supplies the target.
            Vector3 hand = actor.InverseTransformPoint(grip.position);
            if (weaponAccessVertex < 0 || amount < .95f)
            {
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < world.Length; i++)
                {
                    Vector3 bind = bindActorPoints[i];
                    if (contactFreedom[i] <= 0f || bind.x <= -.11f || bind.x >= -.01f || bind.z <= .025f) continue;
                    Vector3 posed = actor.InverseTransformPoint(world[i]);
                    float distance = 4f * (bind.x + .025f) * (bind.x + .025f) +
                        (posed.y - hand.y) * (posed.y - hand.y) + .1f * (posed.z - hand.z) * (posed.z - hand.z);
                    if (distance >= nearest) continue;
                    nearest = distance; weaponAccessVertex = i;
                }
            }
            if (weaponAccessVertex < 0) return;
            Vector3 delta = Vector3.ClampMagnitude(grip.position - world[weaponAccessVertex], .24f) * amount;
            Vector3 reference = bindActorPoints[weaponAccessVertex];
            for (int i = 0; i < world.Length; i++)
            {
                if (contactFreedom[i] <= 0f || bindActorPoints[i].x >= 0f || bindActorPoints[i].z <= .025f) continue;
                float influence = 1f - Mathf.SmoothStep(0f, 1f,
                    Vector3.Distance(bindActorPoints[i], reference) / .24f);
                world[i] += delta * influence;
            }
        }

        internal bool TryHemFold(PlayerScarfBodyContacts contacts, Transform actor,
            PlayerJacketClothSurface[] panels, out HemFoldSet folds)
        {
            folds = default;
            if (panelShape == null || !panelShape.HasPins ||
                !contacts.TryHemObstacle(panelShape.Raw, contactFreedom, actor, out Bounds obstacle, out float side)) return false;
            // Material limits can shorten an edge across a deeply raised
            // thigh. Retain the established coherent fold for residual area
            // contacts even when that edge no longer exceeds the stretch gate.
            if (!panelShape.Stretched(world) && panelShape.Clear(world, contacts)) return false;
            // The lifted row sits over the raised thigh; lower rows retain
            // the hanging hem. A little lateral room chooses one coherent
            // escape side instead of adjacent corners passing opposite ways.
            // A horizontal thigh can obstruct a lower row while the upper
            // row already clears it. Try a small extra outward reserve before
            // lifting clear upper fabric; both candidates obey the same gates.
            bool hasOther = contacts.TryHemObstacle(panelShape.Raw, contactFreedom, actor, out Bounds other,
                out float otherSide, -side);
            for (int motion = 0; motion < 2; motion++)
            {
                for (int candidate = 0; candidate < 2; candidate++)
                {
                    folds = new HemFoldSet(Create(obstacle, side, candidate, motion == 0 ? 1f : 0f));
                    if (Accept(folds)) return true;
                }
                // Both thighs can obstruct the hem in a crouch. Compose the
                // disjoint halves from the same raw fabric state before
                // testing; sequential application would restore one half.
                if (hasOther)
                    for (int candidate = 0; candidate < 2; candidate++)
                    {
                        float secondary = motion == 0 ? 1f : 0f;
                        folds = new HemFoldSet(Create(obstacle, side, candidate, secondary),
                            Create(other, otherSide, candidate, secondary));
                        if (Accept(folds)) return true;
                    }
                // Secondary hem motion can stretch an otherwise feasible
                // deep fold. Retry from the skinned fabric pose only after
                // the full-motion one- and two-thigh candidates fail.
            }
            // At extreme hip poses the authored lift gradient can stretch a
            // small pocket or placket. Keep free patches compact, then reuse
            // area contacts and the original deep-fold stretch bound.
            for (int candidate = 0; candidate < 2; candidate++)
            {
                folds = new HemFoldSet(Create(obstacle, side, candidate, 0f), refineContacts: true);
                if (Accept(folds)) return true;
                if (!hasOther) continue;
                folds = new HemFoldSet(Create(obstacle, side, candidate, 0f),
                    Create(other, otherSide, candidate, 0f), refineContacts: true);
                if (Accept(folds)) return true;
            }
            return false;

            bool Accept(HemFoldSet candidate)
            {
                foreach (PlayerJacketClothSurface panel in panels)
                    if (!panel.AcceptsHemFold(candidate, contacts, actor)) return false;
                return true;
            }

            HemFold Create(Bounds bounds, float half, int reserve, float secondary)
            {
                float rise = Mathf.Ceil(Mathf.Max(0f, bounds.max.y - panelShape.Peak) * .8f / .02f) * .02f;
                float outside = half < 0f ? bounds.min.x - panelShape.HalfWidth * .195f :
                    bounds.max.x + panelShape.HalfWidth * .195f;
                return new HemFold(half, outside + half * reserve * .02f, rise, panelShape.Start,
                    panelShape.Middle, panelShape.Peak, panelShape.Pin, secondary);
            }
        }

        private bool AcceptsHemFold(HemFoldSet folds, PlayerScarfBodyContacts contacts, Transform actor)
        {
            if (panelShape == null) return true;
            BuildHemCandidate(folds, contacts, actor);
            return panelShape.Clear(contacts) && !panelShape.Stretched(panelShape.Candidate);
        }

        internal bool ApplyHemFold(HemFoldSet folds, PlayerScarfBodyContacts contacts, Transform actor)
        {
            if (panelShape == null) return false;
            BuildHemCandidate(folds, contacts, actor);
            if (!panelShape.Clear(contacts) || panelShape.Stretched(panelShape.Candidate)) return false;
            Array.Copy(panelShape.Candidate, world, world.Length);
            for (int i = 0; i < output.Length; i++)
                output[i] = contactFreedom[i] <= 0f ? posedOriginal[i] : inverseSkins[vertexSkins[i]].MultiplyPoint3x4(world[i]);
            Write();
            return true;
        }

        private void BuildHemCandidate(HemFoldSet folds, PlayerScarfBodyContacts contacts, Transform actor)
        {
            panelShape.Apply(folds, world, actor);
            if (!folds.RefineContacts) return;
            if (freeSurfaceOriginal != null) Array.Copy(panelShape.Candidate, freeSurfaceOriginal, world.Length);
            // Extreme authored folds retain their established refinement;
            // the smaller ordinary-frame budget does not alter this fallback.
            ResolvePanelContacts(contacts, panelShape.Candidate, constrainMaterial: false, areaPasses: 4);
            ResolveFreePanelSurface(contacts, panelShape.Candidate);
            if (panelShape.Stretched(panelShape.Candidate))
                panelShape.LimitMaterialDistances(panelShape.Candidate, contacts, maximumStretch: 1.96f, passes: 1,
                    queryCache: pointQueryCache);
        }

        internal readonly struct HemFold
        {
            public readonly float Side, Lateral, Lift, Start, Middle, Peak, Pin, SecondaryMotion;
            public HemFold(float side, float lateral, float lift, float start, float middle, float peak, float pin,
                float secondaryMotion = 1f)
            { Side = side; Lateral = lateral; Lift = lift; Start = start; Middle = middle; Peak = peak; Pin = pin;
                SecondaryMotion = secondaryMotion; }
        }

        internal readonly struct HemFoldSet
        {
            public readonly HemFold First, Second;
            public readonly bool RefineContacts;
            public HemFoldSet(HemFold first, HemFold second = default, bool refineContacts = false)
            { First = first; Second = second; RefineContacts = refineContacts; }
        }

        // Cached cloth particles join UV splits and the two extrusion layers.
        // This is a contact fallback for overstretched panels, rather than a
        // second per-frame mesh/physics simulation.
        private sealed class PanelShape
        {
            public readonly Vector3[] Raw, Candidate;
            public readonly bool HasPins;
            public readonly float HalfWidth, Start, Middle, Peak, Pin;
            private readonly Vector3[] bind, pose;
            private readonly int[] canonical, triangles, layers;
            private readonly (int A, int B)[] edges;
            private readonly bool[] pinned;
            private readonly float[] freedom;
            private readonly float[] materialLengths;
            private readonly int[][] layerMembers;
            private readonly int[] populatedLayers, duplicateVertices;
            private readonly bool[] movedLayers;
            private readonly Vector3 centre;

            public PanelShape(Vector3[] source, Vector3[] authored, Matrix4x4 authoredToWorld, Transform actor,
                float[] freedom, int[] canonical, int[] triangles, int[] skins)
            {
                this.freedom = freedom; this.canonical = canonical; this.triangles = triangles;
                int count = source.Length;
                Raw = new Vector3[count]; Candidate = new Vector3[count]; pose = (Vector3[])source.Clone();
                bind = new Vector3[count]; layers = new int[count]; pinned = new bool[count];
                var rows = new List<float>();
                float peak = float.NegativeInfinity, pin = float.PositiveInfinity, width = 0f;
                for (int i = 0; i < count; i++)
                {
                    bind[i] = actor.InverseTransformPoint(authoredToWorld.MultiplyPoint3x4(authored[i]));
                    layers[i] = canonical[i];
                    pinned[i] = freedom[i] < 1e-7f;
                    HasPins |= pinned[i];
                    if (pinned[i]) continue;
                    width = Mathf.Max(width, Mathf.Abs(bind[i].x));
                    peak = Mathf.Max(peak, bind[i].y);
                    bool seen = false;
                    foreach (float row in rows) if (Mathf.Abs(row - bind[i].y) < .0001f) { seen = true; break; }
                    if (!seen) rows.Add(bind[i].y);
                }
                rows.Sort(); HalfWidth = width; Peak = peak;
                Start = rows.Count > 1 ? rows[1] : peak;
                Middle = rows.Count > 2 ? rows[rows.Count - 2] : peak;
                for (int i = 0; i < count; i++)
                    if (pinned[i] && bind[i].y > peak) pin = Mathf.Min(pin, bind[i].y);
                Pin = pin;
                // Mutual nearest matching rest-weight points are the authored
                // 4 mm inner/outer wall. Circumferential neighbors are much
                // farther away. Pair once; never search geometry at runtime.
                var nearest = new int[count];
                for (int i = 0; i < count; i++)
                {
                    nearest[i] = -1;
                    if (canonical[i] != i) continue;
                    float distance = .006f * .006f;
                    for (int j = 0; j < count; j++)
                    {
                        if (j == i || canonical[j] != j || skins[j] != skins[i]) continue;
                        float square = (bind[j] - bind[i]).sqrMagnitude;
                        if (square < distance) { distance = square; nearest[i] = j; }
                    }
                }
                for (int i = 0; i < count; i++)
                    if (canonical[i] == i && nearest[i] >= 0 && nearest[nearest[i]] == i)
                        layers[i] = Mathf.Min(i, nearest[i]);
                var sums = new Vector3[count]; var sizes = new int[count];
                for (int i = 0; i < count; i++)
                {
                    if (canonical[i] != i) continue;
                    int layer = layers[i]; sums[layer] += bind[i]; sizes[layer]++;
                    pinned[layer] |= pinned[i];
                }
                for (int i = 0; i < count; i++)
                {
                    layers[i] = layers[canonical[i]];
                    int layer = layers[i]; bind[i] = sums[layer] / sizes[layer]; pinned[i] = pinned[layer];
                    centre += bind[i] / count;
                }
                var members = new List<int>[count];
                for (int i = 0; i < count; i++)
                {
                    if (canonical[i] != i) continue;
                    int layer = layers[i];
                    if (members[layer] == null) members[layer] = new List<int>();
                    members[layer].Add(i);
                }
                layerMembers = new int[count][];
                movedLayers = new bool[count];
                var populated = new List<int>();
                var duplicates = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (members[i] != null)
                    {
                        layerMembers[i] = members[i].ToArray();
                        populated.Add(i);
                    }
                    if (canonical[i] != i) duplicates.Add(i);
                }
                populatedLayers = populated.ToArray();
                duplicateVertices = duplicates.ToArray();
                var unique = new HashSet<(int, int)>();
                for (int i = 0; i < triangles.Length; i += 3)
                { Add(triangles[i], triangles[i + 1]); Add(triangles[i + 1], triangles[i + 2]); Add(triangles[i + 2], triangles[i]); }
                edges = new (int, int)[unique.Count]; unique.CopyTo(edges);
                materialLengths = new float[edges.Length];
                CapturePose(source);

                void Add(int a, int b) { unique.Add(a < b ? (a, b) : (b, a)); }
            }

            public void CapturePose(Vector3[] source)
            {
                Array.Copy(source, pose, source.Length);
                for (int i = 0; i < edges.Length; i++)
                {
                    (int a, int b) = edges[i];
                    float length = Vector3.Distance(pose[a], pose[b]);
                    materialLengths[i] = length < .015f ? 0f : length;
                }
            }
            public void CaptureRaw(Vector3[] source) => Array.Copy(source, Raw, source.Length);
            internal float[] MaterialLengths => materialLengths;
            public NativeWork CreateNativeWork() => new NativeWork(freedom, canonical, layers, pinned, edges,
                materialLengths, layerMembers, populatedLayers, duplicateVertices, triangles);

            // Contact projections share material edges. Independent corner
            // escapes around a thigh, pelvis or hand must not pull one cloth
            // triangle into a long wing. This adds no integration/history;
            // it bounds the existing posed area-contact correction.
            public bool LimitMaterialDistances(Vector3[] solved, PlayerScarfBodyContacts contacts,
                float maximumStretch = 1.16f, int passes = 8, PlayerScarfBodyContacts.PointQueryCache queryCache = null)
            {
                using var profile = MaterialMarker.Auto();
                bool changed = false;
                for (int pass = 0; pass < passes; pass++)
                {
                    bool shortened = false;
                    Array.Clear(movedLayers, 0, movedLayers.Length);
                    for (int i = 0; i < edges.Length; i++)
                    {
                        float maximum = materialLengths[i] * maximumStretch;
                        if (maximum <= 0f) continue;
                        (int a, int b) = edges[i];
                        float wa = pinned[a] ? 0f : freedom[a], wb = pinned[b] ? 0f : freedom[b];
                        if (wa + wb <= 0f) continue;
                        Vector3 edge = solved[b] - solved[a];
                        float length = edge.magnitude;
                        if (length <= maximum + .000001f) continue;
                        Vector3 correction = edge * ((length - maximum) / (length * (wa + wb)));
                        if (wa > 0f)
                        {
                            MoveLayer(a, correction * wa);
                        }
                        if (wb > 0f)
                        {
                            MoveLayer(b, -correction * wb);
                        }
                        shortened = changed = true;
                    }
                    // Resolve a moved material point once per sweep, after
                    // its adjacent edge corrections agree. Re-querying both
                    // walls after every edge wastes the same body tests and
                    // can send them back through opposite support faces.
                    foreach (int layer in populatedLayers)
                    {
                        if (!movedLayers[layer]) continue;
                        int[] members = layerMembers[layer];
                        for (int contactPass = 0; contactPass < 2; contactPass++)
                        {
                            bool escaped = false;
                            foreach (int member in members)
                            {
                                Vector3 point = solved[member];
                                bool corrected = queryCache != null ? contacts.ResolvePointCached(ref point, queryCache, member) :
                                    contacts.ResolvePoint(ref point);
                                if (!corrected) continue;
                                Vector3 escape = point - solved[member];
                                foreach (int peer in members) solved[peer] += escape;
                                escaped = true;
                            }
                            if (!escaped) break;
                        }
                    }
                    foreach (int i in duplicateVertices) solved[i] = solved[canonical[i]];
                    if (!shortened) break;
                }
                return changed;

                void MoveLayer(int vertex, Vector3 correction)
                {
                    int[] members = layerMembers[layers[vertex]];
                    foreach (int member in members) solved[member] += correction;
                    movedLayers[layers[vertex]] = true;
                }
            }
            public bool Stretched(Vector3[] source)
            {
                foreach ((int a, int b) in edges)
                {
                    if (freedom[a] < 1e-7f && freedom[b] < 1e-7f) continue;
                    float rest = (pose[b] - pose[a]).sqrMagnitude;
                    if (rest >= .015f * .015f && (source[b] - source[a]).sqrMagnitude >= rest * 4f) return true;
                }
                return false;
            }

            public void Apply(HemFoldSet folds, Vector3[] solved, Transform actor)
            {
                Array.Copy(solved, Candidate, solved.Length);
                // A pocket or flap has no pinned upper seam in this surface.
                // Its small patch follows the fold at its centre without
                // stretching across the fold's steep vertical field.
                bool translate = folds.RefineContacts && !HasPins;
                Vector3 offset = translate ? Displacement(centre) : Vector3.zero;
                for (int i = 0; i < Candidate.Length; i++)
                {
                    Vector3 point = bind[i];
                    if (pinned[i]) continue;
                    HemFold fold = point.x * folds.First.Side > 0f ? folds.First : folds.Second;
                    if (point.x * fold.Side <= 0f) continue;
                    Candidate[i] = Vector3.LerpUnclamped(pose[i], Raw[i], fold.SecondaryMotion) +
                        actor.TransformVector(translate ? offset : Displacement(point));
                }

                Vector3 Displacement(Vector3 point)
                {
                    HemFold fold = point.x * folds.First.Side > 0f ? folds.First : folds.Second;
                    if (point.x * fold.Side <= 0f) return Vector3.zero;
                    float front = Mathf.Clamp01((point.z + .02f) / .20f);
                    if (front <= 0f) return Vector3.zero;
                    float rise = point.y < fold.Middle ? .63f * Mathf.InverseLerp(fold.Start, fold.Middle, point.y) :
                        point.y < fold.Peak ? Mathf.Lerp(.63f, 1f, Mathf.InverseLerp(fold.Middle, fold.Peak, point.y)) :
                        1f - Mathf.InverseLerp(fold.Peak, fold.Pin, point.y);
                    float lateral = fold.Side < 0f ? Mathf.Min(point.x, fold.Lateral) : Mathf.Max(point.x, fold.Lateral);
                    return new Vector3((lateral - point.x) * front, fold.Lift * rise * front, 0f);
                }
            }

            public bool Clear(PlayerScarfBodyContacts contacts) => Clear(Candidate, contacts);

            public bool Clear(Vector3[] source, PlayerScarfBodyContacts contacts)
            {
                for (int i = 0; i < source.Length; i++)
                    if (canonical[i] == i && freedom[i] >= 1e-7f && !contacts.OutsideSourceSupport(source[i])) return false;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    if (freedom[a] < 1e-7f && freedom[b] < 1e-7f && freedom[c] < 1e-7f) continue;
                    Vector3 x = source[a], y = source[b], z = source[c];
                    if (!contacts.OutsideSourceSupport((x + y + z) / 3f) || !contacts.OutsideSourceSupport((x + y) * .5f) ||
                        !contacts.OutsideSourceSupport((y + z) * .5f) || !contacts.OutsideSourceSupport((z + x) * .5f)) return false;
                }
                return true;
            }
        }

        private int ResolvePanelContacts(PlayerScarfBodyContacts contacts, Vector3[] candidate = null,
            bool constrainMaterial = true, float maximumStretch = 1.16f, int areaPasses = 4, bool preferNative = true)
        {
            using var profile = AreaMarker.Auto();
            if (contactTriangles == null || contactTriangles.Length == 0) return 0;
            Vector3[] positions = candidate ?? world;
            if (preferNative && TryNativeArea(contacts, positions, constrainMaterial, maximumStretch, areaPasses,
                out int nativeContacts)) return nativeContacts;
            int contactCount = 0;
            // A clear vertex does not imply a clear cloth triangle: a raised
            // thigh can pass through an edge or the middle of a broad panel.
            // Bounded area constraints reuse the posed support envelopes;
            // there is no live body baking or collision-mesh allocation.
            for (int pass = 0; pass < areaPasses; pass++)
            {
                bool shortened = constrainMaterial && panelShape != null &&
                    panelShape.LimitMaterialDistances(positions, contacts, maximumStretch,
                        passes: freeSurfaceOriginal == null ? 8 : 2,
                        queryCache: pointQueryCache);
                int corrected = 0;
                for (int triangle = 0; triangle < contactTriangles.Length; triangle += 3)
                {
                    int a = contactTriangles[triangle], b = contactTriangles[triangle + 1], c = contactTriangles[triangle + 2];
                    if (contactFreedom[a] <= 0f && contactFreedom[b] <= 0f && contactFreedom[c] <= 0f) continue;
                    int sample = triangle / 3 * 4;
                    corrected += Distribute(a, b, c, new Vector3(1f / 3f, 1f / 3f, 1f / 3f), sample);
                    corrected += Distribute(a, b, c, new Vector3(.5f, .5f, 0f), sample + 1);
                    corrected += Distribute(a, b, c, new Vector3(0f, .5f, .5f), sample + 2);
                    corrected += Distribute(a, b, c, new Vector3(.5f, 0f, .5f), sample + 3);
                }
                if (corrected == 0 && !shortened) break;
                contactCount += corrected;
                for (int i = 0; i < positions.Length; i++)
                    positions[i] = positions[contactVertices[i]];
                // Area corrections also keep every free corner outside.
                contactCount += ResolveSurfacePoints(contacts, positions);
            }
            return contactCount;

            int Distribute(int a, int b, int c, Vector3 weights, int sample)
            {
                Vector3 point = positions[a] * weights.x + positions[b] * weights.y + positions[c] * weights.z;
                if (clearAreaSamples[sample] && point.Equals(clearAreaPoints[sample])) return 0;
                Vector3 resolved = point;
                bool corrected = pointQueryCache != null ?
                    contacts.ResolvePointCached(ref resolved, pointQueryCache, world.Length + sample) :
                    contacts.ResolvePoint(ref resolved);
                if (!corrected)
                { clearAreaSamples[sample] = true; clearAreaPoints[sample] = point; return 0; }
                clearAreaSamples[sample] = false;
                float denominator = contactFreedom[a] * weights.x * weights.x +
                    contactFreedom[b] * weights.y * weights.y + contactFreedom[c] * weights.z * weights.z;
                if (denominator <= 1e-12f) return 0;
                Vector3 correction = (resolved - point) / denominator;
                Add(a, weights.x); Add(b, weights.y); Add(c, weights.z);
                return 1;

                void Add(int vertex, float amount)
                {
                    if (amount <= 0f || contactFreedom[vertex] <= 0f) return;
                    positions[vertex] += correction * (contactFreedom[vertex] * amount);
                }
            }
        }

        // Partition boundaries are one surface even though wardrobe keeps their renderers addressable.
        public void SetWorldNormal(int index, Vector3 normal)
        {
            Vector3 local = Skin(index).transpose.MultiplyVector(normal).normalized;
            normals[index] = local;
            if (tangents.Count == original.Length)
            {
                Vector4 tangent = tangents[index];
                Vector3 direction = Vector3.ProjectOnPlane(new Vector3(tangent.x, tangent.y, tangent.z), local).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
        }

        public void CommitSharedNormals()
        {
            Mesh.SetNormals(normals);
            if (tangents.Count == original.Length) Mesh.SetTangents(tangents);
        }

        private sealed class ExactWeights : IEqualityComparer<BoneWeight>
        {
            internal static readonly ExactWeights Instance = new ExactWeights();
            public bool Equals(BoneWeight a, BoneWeight b) =>
                a.boneIndex0 == b.boneIndex0 && a.boneIndex1 == b.boneIndex1 &&
                a.boneIndex2 == b.boneIndex2 && a.boneIndex3 == b.boneIndex3 &&
                a.weight0.Equals(b.weight0) && a.weight1.Equals(b.weight1) &&
                a.weight2.Equals(b.weight2) && a.weight3.Equals(b.weight3);

            public int GetHashCode(BoneWeight weight)
            {
                unchecked
                {
                    int hash = weight.boneIndex0;
                    hash = hash * 397 ^ weight.boneIndex1;
                    hash = hash * 397 ^ weight.boneIndex2;
                    hash = hash * 397 ^ weight.boneIndex3;
                    hash = hash * 397 ^ weight.weight0.GetHashCode();
                    hash = hash * 397 ^ weight.weight1.GetHashCode();
                    hash = hash * 397 ^ weight.weight2.GetHashCode();
                    return hash * 397 ^ weight.weight3.GetHashCode();
                }
            }
        }

        public void Restore()
        {
            Array.Copy(original, output, original.Length);
            Array.Copy(original, posedOriginal, original.Length);
            if (foldShape >= 0) Renderer.SetBlendShapeWeight(foldShape, 0f);
            Write();
        }

        public void CopyTo(PlayerJacketClothSurface target)
        {
            if (target == null || target.output.Length != output.Length) return;
            // A camera-local arm may have its own elbow pose. Copy only secondary motion;
            // derive its authored sleeve correction from that instance's actual bones.
            target.UpdatePose();
            for (int i = 0; i < output.Length; i++)
                target.output[i] = target.posedOriginal[i] + output[i] - posedOriginal[i];
            if (target.foldShape >= 0) target.Renderer.SetBlendShapeWeight(target.foldShape, 0f);
            target.Write();
        }

        private void Write()
        {
            using var profile = WriteMarker.Auto();
            // Identical bind-space geometry needs no upload or lighting/bounds
            // rebuild, including passive copies and unchanged pinned panels.
            bool changed = false;
            for (int i = 0; i < output.Length; i++)
                if (!output[i].Equals(writtenOutput[i])) { changed = true; break; }
            if (!changed) return;
            Array.Copy(output, writtenOutput, output.Length);
            Mesh.vertices = output;
            Mesh.RecalculateNormals();
            // Keep the lighting basis attached to the deformed cuff/hem, also
            // when these vertices are copied into the mirror or first person.
            if (normalMapped) Mesh.RecalculateTangents();
            Mesh.GetNormals(normals);
            if (normalMapped) Mesh.GetTangents(tangents);
            Mesh.RecalculateBounds();
            // Consumers can reuse this geometry between writes. A manual
            // ApplyAt or a passive copy can write several poses in one frame.
            unchecked { GeometryVersion++; }
            Renderer.sharedMesh = Mesh;
            // Skinned bounds are renderer-local; retain the authored animated
            // bounds and add the maximum free-cloth excursion on every axis.
            Bounds bounds = binding.AuthoredBounds;
            Vector3 scale = Renderer.transform.lossyScale;
            bounds.Expand(new Vector3(.18f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                .18f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), .18f / Mathf.Max(.0001f, Mathf.Abs(scale.z))));
            Renderer.localBounds = bounds;
        }

        public void Dispose()
        {
            nativeWork?.Dispose();
            if (Renderer != null && Renderer.sharedMesh == Mesh)
            { Renderer.sharedMesh = Source; Renderer.localBounds = binding.AuthoredBounds; }
            PlayerScarfResources.DestroyOwned(Mesh);
        }
    }
}
