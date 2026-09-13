using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CanneryWomanHair
    {
        public const float AttachmentProgress = .20f;
        private const float SurfaceClearance = .003f;
        private const int ContactIterations = 8;
        [SerializeField] private Transform[] surfaceBones = Array.Empty<Transform>();
        [SerializeField] private SurfaceBinding[] surfaceBindings = Array.Empty<SurfaceBinding>();
        [SerializeField] private SurfaceBinding[] collisionBindings = Array.Empty<SurfaceBinding>();
        [SerializeField] private SurfaceEdge[] surfaceEdges = Array.Empty<SurfaceEdge>();
        [SerializeField] private int[] groupVertexStarts = Array.Empty<int>(), groupVertexIndices = Array.Empty<int>();
        [SerializeField] private int[] surfaceBoneChains = Array.Empty<int>(), surfaceBoneLevels = Array.Empty<int>();
        [SerializeField] private RingBinding[] rings = Array.Empty<RingBinding>();
        [SerializeField] private ArmCapsule[] arms = Array.Empty<ArmCapsule>();
        private Matrix4x4[] surfaceMatrices;
        private Vector3[] surfaceWorldVertices, collisionWorldPoints;
        private bool surfacePoseDirty = true, contactDiagnosticsDirty = true;
        private Vector3[] ringCenters, ringWidths, ringDepths, ringHeights;
        private Vector3[] ringMinimum, ringMaximum;
        private Vector3[] ringAxes, ringAxisUnits;
        private float[] ringInverseLengths, ringAxisLengths, ringHeightLengths;
        private readonly Vector3[] strandMinimum = new Vector3[ChainCount], strandMaximum = new Vector3[ChainCount];
        private readonly Vector3[] armMinimum = new Vector3[2], armMaximum = new Vector3[2];
        private const int ContactGroupCount = ChainCount * 13;
        private readonly Vector3[] contactGroupMinimum = new Vector3[ContactGroupCount], contactGroupMaximum = new Vector3[ContactGroupCount];
        private readonly int[] contactGroupBodies = new int[ContactGroupCount];
        private readonly ulong[] contactGroupStrands = new ulong[ContactGroupCount];
        private readonly bool[] contactGroupActive = new bool[ContactGroupCount];
        private readonly bool[] contactGroupHands = new bool[ContactGroupCount];
        private byte[] sampleSeparatingPlanes, edgeSeparatingPlanes, groupSeparatingPlanes;
        private Bounds[] bodyBounds;
        private const int ContactDofs = 9;
        private const int ContactStride = ContactDofs + 1, ContactSystemSize = ContactDofs * ContactStride;
        private readonly double[] contactSystems = new double[ChainCount * ContactSystemSize];
        private readonly double[] contactRow = new double[ContactDofs], contactSolution = new double[ContactDofs];
        private readonly Vector3[] contactOrigins = new Vector3[PointCount];
        private readonly Vector3[] contactStartPoints = new Vector3[PointCount], contactJacobians = new Vector3[3];
        private readonly Quaternion[] contactWorldRotations = new Quaternion[PointCount];
        private readonly Vector3[] armOrigins = new Vector3[6], armDirections = new Vector3[6];
        private readonly float[] armLengthsSquared = new float[6], armRadii = new float[6];
        private float[] bodyMinimumRadii;

        [Serializable] private struct SkinTerm { public int Bone; public float Weight; public Vector3 Local; }
        [Serializable] private struct SkinPoint { public SkinTerm A, B, C, D; }
        [Serializable] private struct SurfaceBinding
        {
            public string Renderer;
            public int Vertex, Chain, Group;
            public float Progress;
            public bool Attached;
            public SkinPoint Point;
            public int SourceA, SourceB, SourceC;
            public byte SourceCount;
        }
        [Serializable] private struct RingBinding
        {
            public int Chain;
            public float Progress;
            public SkinPoint Center, Width, Depth, Height;
        }
        [Serializable] private struct ArmCapsule { public Transform From, To; public float Radius; }
        [Serializable] private struct SurfaceEdge { public int A, B, Chain, Group; public bool Attached; }

        public bool HasSurfaceBindings => surfaceBindings.Length > 0 && collisionBindings.Length > surfaceBindings.Length && surfaceBones.Length > 0 &&
            rings.Length == ChainCount * 13 && arms.Length == 2 && surfaceEdges.Length > 0 && surfaceBindings[0].SourceCount == 1 &&
            groupVertexStarts.Length == ContactGroupCount + 1;
        public int SurfacePointCount => surfaceBindings.Length;
        public int CollisionPointCount => collisionBindings.Length;
        public string SurfaceRendererName(int index) => surfaceBindings[index].Renderer;
        public int SurfaceVertexIndex(int index) => surfaceBindings[index].Vertex;
        public float SurfaceProgress(int index) => surfaceBindings[index].Progress;
        public bool SurfaceIsAttached(int index) => surfaceBindings[index].Attached;
        public Vector3 SurfaceWorldPoint(int index)
        {
            EnsureSurfacePose();
            return surfaceWorldVertices[index];
        }
        private float maximumSurfaceBodyPenetration, maximumStrandOverlap, maximumContinuousBodyPenetration;
        private string deepestBodyVolume = string.Empty;
        private Vector3 deepestBodyPoint, deepestBodyCorrection;
        public float MaximumSurfaceBodyPenetration { get { EnsureContactDiagnostics(); return maximumSurfaceBodyPenetration; } }
        public float MaximumStrandOverlap { get { EnsureContactDiagnostics(); return maximumStrandOverlap; } }
        public int LastSurfaceContactCount { get; private set; }
        public int LastContinuousContactCount { get; private set; }
        public float MaximumContinuousBodyPenetration { get { EnsureContactDiagnostics(); return maximumContinuousBodyPenetration; } }
        public string DeepestBodyVolume { get { EnsureContactDiagnostics(); return deepestBodyVolume; } }
        public Vector3 DeepestBodyPoint { get { EnsureContactDiagnostics(); return deepestBodyPoint; } }
        public Vector3 DeepestBodyCorrection { get { EnsureContactDiagnostics(); return deepestBodyCorrection; } }

        private void EnsureSurfacePose() { if (surfacePoseDirty) RefreshSurfaceMatrices(); }
        private void EnsureContactDiagnostics() { if (contactDiagnosticsDirty) MeasureSurfaceContacts(); }

        // This is called by explicit prefab authoring only. Runtime uses these
        // cached skin terms, never mesh reads, BakeMesh or a second renderer.
        private void ConfigureSurfaceBindings(VillageResidentPresentation motion)
        {
            var boneList = new List<Transform>();
            var bindings = new List<SurfaceBinding>();
            var contacts = new List<SurfaceBinding>();
            var contactEdges = new List<SurfaceEdge>();
            for (int chain = 0; chain < ChainCount; chain++)
            {
                string rendererName = "HAIR_Long" + ChainNames[chain].Substring(4);
                var renderer = CityPedestrianHandProps.FindSocket(motion.ModelRoot, rendererName)?.GetComponent<SkinnedMeshRenderer>();
                if (renderer == null) throw new InvalidOperationException("Missing skinned hair surface " + rendererName);
                Mesh mesh = renderer.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] bindposes = mesh.bindposes;
                Transform[] bones = renderer.bones;
                int first = bindings.Count;
                float rootHeight = transform.InverseTransformPoint(joints[chain * PointsPerChain].position).y;
                float tipHeight = transform.InverseTransformPoint(joints[chain * PointsPerChain + 3].position).y;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    BoneWeight weight = weights[vertex];
                    Vector3 localVertex = vertices[vertex];
                    SkinTerm Term(int boneIndex, float amount)
                    {
                        if (amount <= 0f) return default;
                        Transform bone = bones[boneIndex];
                        int index = boneList.IndexOf(bone);
                        if (index < 0) { index = boneList.Count; boneList.Add(bone); }
                        return new SkinTerm { Bone = index, Weight = amount, Local = bindposes[boneIndex].MultiplyPoint3x4(localVertex) };
                    }
                    Vector3 world = renderer.transform.TransformPoint(localVertex);
                    bool IsAttached(int boneIndex, float amount) => amount > .00001f && bones[boneIndex] == head;
                    float progress = Mathf.Clamp01((rootHeight - transform.InverseTransformPoint(world).y) / (rootHeight - tipHeight));
                    bindings.Add(new SurfaceBinding {
                        Renderer = rendererName, Vertex = vertex, Chain = chain,
                        SourceA = first + vertex, SourceCount = 1,
                        Progress = progress, Group = ContactGroup(chain, progress),
                        Attached = IsAttached(weight.boneIndex0, weight.weight0) || IsAttached(weight.boneIndex1, weight.weight1) ||
                            IsAttached(weight.boneIndex2, weight.weight2) || IsAttached(weight.boneIndex3, weight.weight3),
                        Point = new SkinPoint { A = Term(weight.boneIndex0, weight.weight0), B = Term(weight.boneIndex1, weight.weight1),
                            C = Term(weight.boneIndex2, weight.weight2), D = Term(weight.boneIndex3, weight.weight3) }
                    });
                }
                for (int i = first; i < bindings.Count; i++) contacts.Add(bindings[i]);
                var edges = new HashSet<long>();
                int[] triangles = mesh.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    contacts.Add(Blend(new[] { a, b, c }));
                    Edge(a, b); Edge(b, c); Edge(c, a);
                }
                void Edge(int a, int b)
                {
                    long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                    if (!edges.Add(key)) return;
                    contacts.Add(Blend(new[] { a, b }));
                    contactEdges.Add(new SurfaceEdge { A = first + a, B = first + b, Chain = chain,
                        Group = ContactGroup(chain, (bindings[first + a].Progress + bindings[first + b].Progress) * .5f),
                        Attached = bindings[first + a].Attached || bindings[first + b].Attached });
                }
                SurfaceBinding Blend(int[] indices)
                {
                    var terms = new Dictionary<int, SkinTerm>();
                    bool attached = false; float progress = 0f;
                    foreach (int index in indices)
                    {
                        SurfaceBinding source = bindings[first + index];
                        attached |= source.Attached; progress += source.Progress / indices.Length;
                        Add(source.Point.A); Add(source.Point.B); Add(source.Point.C); Add(source.Point.D);
                    }
                    if (terms.Count > 4) throw new InvalidOperationException("Hair contact samples exceed the four authored skin influences.");
                    var packed = new SkinTerm[4]; int count = 0;
                    foreach (SkinTerm term in terms.Values)
                        packed[count++] = new SkinTerm { Bone = term.Bone, Weight = term.Weight, Local = term.Local / term.Weight };
                    return new SurfaceBinding { Chain = chain, Vertex = -1, Progress = progress, Group = ContactGroup(chain, progress), Attached = attached,
                        SourceA = first + indices[0], SourceB = first + indices[1],
                        SourceC = indices.Length == 3 ? first + indices[2] : 0, SourceCount = (byte)indices.Length,
                        Point = new SkinPoint { A = packed[0], B = packed[1], C = packed[2], D = packed[3] } };
                    void Add(SkinTerm term)
                    {
                        if (term.Weight <= 0f) return;
                        terms.TryGetValue(term.Bone, out SkinTerm sum);
                        float weight = term.Weight / indices.Length;
                        sum.Bone = term.Bone; sum.Weight += weight; sum.Local += term.Local * weight; terms[term.Bone] = sum;
                    }
                }
            }
            surfaceBones = boneList.ToArray(); surfaceBindings = bindings.ToArray(); collisionBindings = contacts.ToArray();
            surfaceEdges = contactEdges.ToArray();
            var groups = new HashSet<int>[ContactGroupCount];
            for (int group = 0; group < groups.Length; group++) groups[group] = new HashSet<int>();
            foreach (SurfaceBinding binding in collisionBindings)
            {
                if (binding.Attached) continue;
                groups[binding.Group].Add(binding.SourceA);
                if (binding.SourceCount > 1) groups[binding.Group].Add(binding.SourceB);
                if (binding.SourceCount > 2) groups[binding.Group].Add(binding.SourceC);
            }
            foreach (SurfaceEdge edge in surfaceEdges)
            {
                if (edge.Attached) continue;
                groups[edge.Group].Add(edge.A); groups[edge.Group].Add(edge.B);
            }
            groupVertexStarts = new int[ContactGroupCount + 1];
            var groupVertices = new List<int>();
            for (int group = 0; group < groups.Length; group++)
            {
                groupVertexStarts[group] = groupVertices.Count;
                groupVertices.AddRange(groups[group]);
            }
            groupVertexStarts[ContactGroupCount] = groupVertices.Count;
            groupVertexIndices = groupVertices.ToArray();
            if (collisionBindings.Length > 8192)
                throw new InvalidOperationException("Authored hair exceeds the bounded cached contact sample budget.");
            surfaceBoneChains = new int[surfaceBones.Length]; surfaceBoneLevels = new int[surfaceBones.Length];
            for (int i = 0; i < surfaceBones.Length; i++)
            {
                string name = surfaceBones[i].name;
                surfaceBoneChains[i] = Array.FindIndex(ChainNames, prefix => name.StartsWith(prefix + ".", StringComparison.Ordinal));
                surfaceBoneLevels[i] = surfaceBoneChains[i] < 0 ? -1 : int.Parse(name.Substring(name.LastIndexOf('.') + 1));
            }
            var capsules = new List<ArmCapsule>();
            foreach (string side in new[] { "L", "R" })
            {
                Add("hand." + side, "SOCKET_Grip." + side, MeasureHand(side));
            }
            arms = capsules.ToArray();
            void Add(string from, string to, float radius) => capsules.Add(new ArmCapsule {
                From = CityPedestrianHandProps.FindSocket(motion.ModelRoot, from),
                To = CityPedestrianHandProps.FindSocket(motion.ModelRoot, to), Radius = radius
            });
            float MeasureHand(string side)
            {
                Vector3 from = CityPedestrianHandProps.FindSocket(motion.ModelRoot, "hand." + side).position;
                Vector3 axis = CityPedestrianHandProps.FindSocket(motion.ModelRoot, "SOCKET_Grip." + side).position - from;
                float squared = axis.sqrMagnitude, radius = 0f;
                foreach (SkinnedMeshRenderer renderer in motion.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string name = renderer.name;
                    if (!name.EndsWith("." + side, StringComparison.Ordinal) ||
                        !(name.StartsWith("GEO_Palm", StringComparison.Ordinal) || name.StartsWith("GEO_Finger", StringComparison.Ordinal) ||
                          name.StartsWith("GEO_Thumb", StringComparison.Ordinal))) continue;
                    foreach (Vector3 vertex in renderer.sharedMesh.vertices)
                    {
                        Vector3 point = renderer.transform.TransformPoint(vertex);
                        float t = squared > .000001f ? Mathf.Clamp01(Vector3.Dot(point - from, axis) / squared) : 0f;
                        radius = Mathf.Max(radius, Vector3.Distance(point, from + axis * t));
                    }
                }
                if (radius <= 0f) throw new InvalidOperationException("Missing measured hand surface " + side);
                return radius / transform.lossyScale.x + .002f;
            }
        }

        private static int ContactGroup(int chain, float progress) => chain * 13 + Mathf.Clamp(Mathf.FloorToInt(progress * 12f), 0, 12);

        /// <summary>Measured authoring rings; coordinates are actor-local metres before the actor height scale.</summary>
        public void ConfigureEnvelopes(HairEnvelope[] envelopes)
        {
            if (envelopes == null || envelopes.Length != ChainCount)
                throw new InvalidOperationException("Every authored hair lock needs its measured envelope.");
            var values = new List<RingBinding>();
            foreach (HairEnvelope envelope in envelopes)
            {
                int chain = Array.IndexOf(ChainNames, envelope.Chain);
                if (chain < 0 || envelope.Rings.Length != 13) throw new InvalidOperationException("Invalid measured hair envelope.");
                foreach (HairRing ring in envelope.Rings)
                    values.Add(new RingBinding { Chain = chain, Progress = ring.Progress,
                        Center = Bind(ring.Center, ring),
                        Width = Bind(ring.Center + Vector3.right * ring.HalfWidth, ring),
                        Depth = Bind(ring.Center + Vector3.forward * ring.HalfDepth, ring),
                        Height = Bind(ring.Center + Vector3.up * ring.HalfHeight, ring) });
            }
            values.Sort((left, right) => left.Chain == right.Chain ? left.Progress.CompareTo(right.Progress) : left.Chain.CompareTo(right.Chain));
            rings = values.ToArray();
            surfaceMatrices = null;
            surfacePoseDirty = contactDiagnosticsDirty = true;

            SkinPoint Bind(Vector3 position, HairRing ring)
            {
                Vector3 world = transform.TransformPoint(position);
                SkinTerm Term(int index)
                {
                    if (index >= ring.Bones.Length || ring.Weights[index] <= 0f) return default;
                    int bone = Array.FindIndex(surfaceBones, candidate => candidate.name == ring.Bones[index]);
                    if (bone < 0) throw new InvalidOperationException("Missing hair surface bone " + ring.Bones[index]);
                    return new SkinTerm { Bone = bone, Weight = ring.Weights[index], Local = surfaceBones[bone].InverseTransformPoint(world) };
                }
                return new SkinPoint { A = Term(0), B = Term(1), C = Term(2), D = Term(3) };
            }
        }

        public sealed class HairEnvelope { public string Chain; public HairRing[] Rings; }
        public sealed class HairRing
        {
            public float Progress, HalfWidth, HalfDepth, HalfHeight;
            public Vector3 Center;
            public string[] Bones;
            public float[] Weights;
        }

        private void RefreshSurfaceMatrices()
        {
            bool newCache = surfaceMatrices == null || surfaceMatrices.Length != surfaceBones.Length;
            if (newCache)
            {
                surfaceMatrices = new Matrix4x4[surfaceBones.Length];
                surfaceWorldVertices = new Vector3[surfaceBindings.Length];
                collisionWorldPoints = new Vector3[collisionBindings.Length];
                sampleSeparatingPlanes = new byte[collisionBindings.Length * body.Length];
                edgeSeparatingPlanes = new byte[surfaceEdges.Length * body.Length];
                groupSeparatingPlanes = new byte[ContactGroupCount * body.Length];
                ringCenters = new Vector3[rings.Length]; ringWidths = new Vector3[rings.Length]; ringDepths = new Vector3[rings.Length];
                ringHeights = new Vector3[rings.Length];
                ringMinimum = new Vector3[rings.Length]; ringMaximum = new Vector3[rings.Length]; ringAxes = new Vector3[rings.Length];
                ringAxisUnits = new Vector3[rings.Length]; ringInverseLengths = new float[rings.Length];
                ringAxisLengths = new float[rings.Length]; ringHeightLengths = new float[rings.Length];
            }
            int changedChains = newCache ? (1 << ChainCount) - 1 : 0;
            for (int i = 0; i < surfaceBones.Length; i++)
            {
                Matrix4x4 current = surfaceBones[i].localToWorldMatrix;
                if (!current.Equals(surfaceMatrices[i]))
                    changedChains |= surfaceBoneChains[i] < 0 ? (1 << ChainCount) - 1 : 1 << surfaceBoneChains[i];
                surfaceMatrices[i] = current;
            }
            // LBS is affine: edge midpoints and triangle centres are exactly
            // the corresponding averages of their skinned shared vertices.
            // Each imported vertex is transformed once for this solver pose.
            for (int i = 0; i < surfaceBindings.Length; i++)
                if ((changedChains & (1 << surfaceBindings[i].Chain)) != 0)
                    surfaceWorldVertices[i] = Skin(surfaceBindings[i].Point);
            for (int i = 0; i < collisionBindings.Length; i++)
            {
                ref readonly SurfaceBinding binding = ref collisionBindings[i];
                if (binding.Attached || (changedChains & (1 << binding.Chain)) == 0) continue;
                Vector3 a = surfaceWorldVertices[binding.SourceA];
                collisionWorldPoints[i] = binding.SourceCount == 1 ? a : binding.SourceCount == 2
                    ? (a + surfaceWorldVertices[binding.SourceB]) * .5f
                    : (a + surfaceWorldVertices[binding.SourceB] + surfaceWorldVertices[binding.SourceC]) / 3f;
            }
            for (int group = 0; group < ContactGroupCount; group++)
            {
                if ((changedChains & (1 << (group / 13))) == 0) continue;
                int first = groupVertexStarts[group], end = groupVertexStarts[group + 1];
                contactGroupActive[group] = first < end;
                if (first == end) continue;
                Vector3 minimum = surfaceWorldVertices[groupVertexIndices[first]], maximum = minimum;
                for (int index = first + 1; index < end; index++)
                {
                    Vector3 point = surfaceWorldVertices[groupVertexIndices[index]];
                    minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point);
                }
                // All centres and entire edges are convex combinations of
                // these unique vertices; no repeated sample scan is needed.
                contactGroupMinimum[group] = minimum; contactGroupMaximum[group] = maximum;
            }
            for (int i = 0; i < PointCount; i++)
            {
                contactOrigins[i] = joints[i].position;
                contactWorldRotations[i] = joints[i].rotation;
            }
            for (int i = 0; i < rings.Length; i++)
            {
                if ((changedChains & (1 << rings[i].Chain)) == 0) continue;
                ringCenters[i] = Skin(rings[i].Center);
                ringWidths[i] = Skin(rings[i].Width) - ringCenters[i];
                ringDepths[i] = Skin(rings[i].Depth) - ringCenters[i];
                ringHeights[i] = Skin(rings[i].Height) - ringCenters[i];
                ringHeightLengths[i] = ringHeights[i].magnitude;
            }
            for (int chain = 0; chain < ChainCount; chain++)
            {
                if ((changedChains & (1 << chain)) == 0) continue;
                int first = chain * 13;
                Vector3 minimum = ringCenters[first], maximum = minimum;
                for (int segment = 0; segment < 12; segment++)
                {
                    int i = first + segment;
                    // Bound the entire interpolated cross-section, including
                    // its orientation between the two authored endpoints.
                    ringAxes[i] = ringCenters[i + 1] - ringCenters[i];
                    float squared = ringAxes[i].sqrMagnitude;
                    ringInverseLengths[i] = squared > .000001f ? 1f / squared : 0f;
                    ringAxisLengths[i] = Mathf.Sqrt(squared);
                    ringAxisUnits[i] = squared > .000001f ? ringAxes[i] / ringAxisLengths[i] : Vector3.down;
                    Vector3 extent = ConservativeRingExtent(i);
                    Vector3 low = Vector3.Min(ringCenters[i], ringCenters[i + 1]) - extent;
                    Vector3 high = Vector3.Max(ringCenters[i], ringCenters[i + 1]) + extent;
                    ringMinimum[i] = low; ringMaximum[i] = high;
                    minimum = Vector3.Min(minimum, low); maximum = Vector3.Max(maximum, high);
                }
                strandMinimum[chain] = minimum; strandMaximum[chain] = maximum;
            }
            UpdateContactGroupMasks();
            surfacePoseDirty = false;
        }

        private void UpdateContactGroupMasks()
        {
            for (int group = 0; group < ContactGroupCount; group++)
            {
                int bodyMask = 0; ulong strandMask = 0; bool hands = false;
                if (contactGroupActive[group])
                {
                    Vector3 minimum = contactGroupMinimum[group], maximum = contactGroupMaximum[group];
                    Vector3 minimumRelative = minimum - bodyPlaneOrigin, maximumRelative = maximum - bodyPlaneOrigin;
                    for (int volume = 0; volume < body.Length; volume++)
                        if (IntersectsBodyBounds(minimum, maximum, volume) &&
                            !BodyBoundsSeparated(minimumRelative, maximumRelative, volume, SurfaceClearance,
                                groupSeparatingPlanes, group * body.Length + volume)) bodyMask |= 1 << volume;
                    for (int arm = 0; arm < arms.Length; arm++)
                        hands |= IntersectsBounds(minimum, maximum, armMinimum[arm], armMaximum[arm]);
                    for (int chain = 0; chain < ChainCount; chain++)
                    for (int segment = 0; segment < 12; segment++)
                    {
                        if (chain == group / 13) continue;
                        int index = chain * 13 + segment;
                        if (IntersectsBounds(minimum, maximum, ringMinimum[index], ringMaximum[index])) strandMask |= 1UL << index;
                    }
                }
                contactGroupBodies[group] = bodyMask; contactGroupStrands[group] = strandMask; contactGroupHands[group] = hands;
            }
        }

        private Vector3 Skin(in SkinPoint point) => Term(point.A) + Term(point.B) + Term(point.C) + Term(point.D);
        private Vector3 Term(in SkinTerm term) => term.Weight > 0f
            ? surfaceMatrices[term.Bone].MultiplyPoint3x4(term.Local) * term.Weight : Vector3.zero;

        private void SolveSurfaceContacts()
        {
            LastSurfaceContactCount = LastContinuousContactCount = 0;
            contactDiagnosticsDirty = true;
            if (!HasSurfaceBindings) return;
            for (int i = 0; i < PointCount; i++) contactStartPoints[i] = joints[i].position;
            for (int iteration = 0; iteration < ContactIterations; iteration++)
            {
                RefreshSurfaceMatrices();
                Array.Clear(contactSystems, 0, contactSystems.Length);
                bool corrected = AccumulateSampleBodyContacts();
                corrected |= AccumulateSampleStrandContacts();
                corrected |= AccumulateContinuousBodyContacts();
                if (!corrected) break;
                for (int chain = 0; chain < ChainCount; chain++)
                {
                    SolveContactSystem(chain);
                    float largest = 0f;
                    for (int bone = 0; bone < 3; bone++) largest = Mathf.Max(largest, ContactTurn(bone).magnitude);
                    float step = largest > .10f ? .10f / largest : 1f;
                    for (int bone = 0; bone < 3; bone++)
                    {
                        int index = chain * PointsPerChain + bone;
                        Vector3 turn = ContactTurn(bone) * step;
                        float angle = turn.magnitude;
                        joints[index].rotation = angle > .000001f
                            ? Quaternion.AngleAxis(angle * Mathf.Rad2Deg, turn.normalized) * contactWorldRotations[index]
                            : contactWorldRotations[index];
                    }
                }
                // Keep contacts in the same swing-only representation that
                // will be reconstructed next frame; an untracked twist must
                // not disappear and recreate the same collision every frame.
                for (int i = 0; i < PointCount; i++) points[i] = joints[i].position;
                RestoreBindPose();
                // The full surface constraints already contain the joints.
                // Reprojecting endpoints here would overwrite the coupled
                // contact solve with a second, independent correction.
                AimBonesAtPoints(false);
            }
            for (int i = 0; i < PointCount; i++)
            {
                Vector3 normal = joints[i].position - contactStartPoints[i];
                if (normal.sqrMagnitude < .00000001f) continue;
                normal.Normalize();
                Vector3 relative = velocities[i] - contactTransportVelocity;
                float outward = Mathf.Max(0f, Vector3.Dot(relative, normal));
                Vector3 tangent = Vector3.ProjectOnPlane(relative, normal);
                // Cloth/strand friction dissipates contact sliding. Free hair
                // and outward release retain their ordinary inertial damping.
                tangent *= Mathf.Exp(-12f * LastStepSeconds);
                velocities[i] = contactTransportVelocity + normal * outward + tangent;
            }
        }

        private bool AccumulateSampleBodyContacts()
        {
            bool corrected = false;
            for (int sample = 0; sample < collisionBindings.Length; sample++)
            {
                ref readonly SurfaceBinding binding = ref collisionBindings[sample];
                if (binding.Attached) continue;
                int candidates = contactGroupBodies[binding.Group];
                bool hands = contactGroupHands[binding.Group];
                if (candidates == 0 && !hands) continue;
                Vector3 point = collisionWorldPoints[sample];
                Vector3 relative = point - bodyPlaneOrigin;
                for (int volume = 0; candidates != 0; volume++, candidates >>= 1)
                    if ((candidates & 1) != 0)
                        corrected |= AccumulateContact(binding.Point, binding.Chain,
                            ResolveBodyVolumeCached(point, relative, volume, SurfaceClearance,
                                sampleSeparatingPlanes, sample * body.Length + volume) - point);
                if (hands) corrected |= AccumulateContact(binding.Point, binding.Chain, ResolveHands(point, SurfaceClearance) - point);
            }
            return corrected;
        }

        private bool AccumulateSampleStrandContacts()
        {
            bool corrected = false;
            for (int sample = 0; sample < collisionBindings.Length; sample++)
            {
                ref readonly SurfaceBinding binding = ref collisionBindings[sample];
                if (binding.Attached) continue;
                ulong candidates = contactGroupStrands[binding.Group];
                if (candidates == 0) continue;
                Vector3 point = collisionWorldPoints[sample];
                corrected |= AccumulateContact(binding.Point, binding.Chain,
                    ResolveOtherStrands(point, binding.Chain, SurfaceClearance, candidates) - point);
            }
            return corrected;
        }

        private bool AccumulateContact(in SkinPoint point, int chain, Vector3 correction)
        {
            float squared = correction.sqrMagnitude;
            if (squared < .00000025f) return false;
            LastSurfaceContactCount++;
            float distance = Mathf.Sqrt(squared);
            Vector3 normal = correction / distance;
            float denominator = 0f;
            for (int bone = 0; bone < 3; bone++)
            {
                int index = chain * PointsPerChain + bone;
                Vector3 lever = InfluencedLever(point, chain, bone, contactOrigins[index]);
                Vector3 axis = (contactOrigins[index + 1] - contactOrigins[index]).normalized;
                Vector3 jacobian = Vector3.ProjectOnPlane(Vector3.Cross(lever, normal), axis);
                contactJacobians[bone] = jacobian; denominator += jacobian.sqrMagnitude;
            }
            if (denominator < .00000001f) return false;
            for (int bone = 0; bone < 3; bone++)
            {
                Vector3 row = contactJacobians[bone]; int column = bone * 3;
                contactRow[column] = row.x; contactRow[column + 1] = row.y; contactRow[column + 2] = row.z;
            }
            // Solve all contacting surfaces together. Averaging individual
            // inverse-Jacobian turns cancels unrelated shoulder/head contacts
            // and cannot account for their shared upper-link movement.
            double error = distance * .65;
            for (int row = 0; row < ContactDofs; row++)
            {
                double value = contactRow[row];
                if (value == 0d) continue;
                int first = chain * ContactSystemSize + row * ContactStride;
                contactSystems[first + ContactDofs] += value * error;
                // Cholesky reads only the lower symmetric half.
                for (int column = 0; column <= row; column++)
                    contactSystems[first + column] += value * contactRow[column];
            }
            return true;
        }

        private Vector3 ContactTurn(int bone) => new Vector3((float)contactSolution[bone * 3],
            (float)contactSolution[bone * 3 + 1], (float)contactSolution[bone * 3 + 2]);

        private void SolveContactSystem(int chain)
        {
            int first = chain * ContactSystemSize;
            double trace = 0d;
            for (int i = 0; i < ContactDofs; i++) trace += contactSystems[first + i * ContactStride + i];
            double damping = Math.Max(.00001d, trace * .02d);
            for (int i = 0; i < ContactDofs; i++) contactSystems[first + i * ContactStride + i] += damping;
            // Cholesky of the positive-definite damped normal matrix. The
            // projected Jacobians have no twist DOF; damping keeps those null
            // directions well-defined without a per-frame allocation.
            for (int row = 0; row < ContactDofs; row++)
            for (int column = 0; column <= row; column++)
            {
                int rowStart = first + row * ContactStride, columnStart = first + column * ContactStride;
                double value = contactSystems[rowStart + column];
                for (int k = 0; k < column; k++) value -= contactSystems[rowStart + k] * contactSystems[columnStart + k];
                contactSystems[rowStart + column] = row == column ? Math.Sqrt(Math.Max(damping * .001d, value))
                    : value / contactSystems[columnStart + column];
            }
            for (int row = 0; row < ContactDofs; row++)
            {
                int rowStart = first + row * ContactStride;
                double value = contactSystems[rowStart + ContactDofs];
                for (int k = 0; k < row; k++) value -= contactSystems[rowStart + k] * contactSolution[k];
                contactSolution[row] = value / contactSystems[rowStart + row];
            }
            for (int row = ContactDofs - 1; row >= 0; row--)
            {
                double value = contactSolution[row];
                for (int k = row + 1; k < ContactDofs; k++) value -= contactSystems[first + k * ContactStride + row] * contactSolution[k];
                contactSolution[row] = value / contactSystems[first + row * ContactStride + row];
            }
        }

        private bool AccumulateContinuousBodyContacts()
        {
            bool corrected = false;
            for (int edgeIndex = 0; edgeIndex < surfaceEdges.Length; edgeIndex++)
            {
                SurfaceEdge edge = surfaceEdges[edgeIndex];
                if (edge.Attached) continue;
                Vector3 a = surfaceWorldVertices[edge.A], b = surfaceWorldVertices[edge.B];
                Vector3 relativeA = a - bodyPlaneOrigin, relativeB = b - bodyPlaneOrigin;
                Vector3 minimum = Vector3.Min(a, b), maximum = Vector3.Max(a, b);
                int candidates = contactGroupBodies[edge.Group];
                for (int volume = 0; candidates != 0; volume++, candidates >>= 1)
                {
                    if ((candidates & 1) == 0 || !IntersectsBodyBounds(minimum, maximum, volume) ||
                        !ClipBodySegment(relativeA, relativeB, volume, SurfaceClearance,
                            edgeIndex * body.Length + volume, out float fraction)) continue;
                    Vector3 point = Vector3.Lerp(a, b, fraction);
                    Vector3 correction = ResolveBodyVolume(point, volume, SurfaceClearance) - point;
                    if (correction.sqrMagnitude < .00000025f) continue;
                    LastContinuousContactCount++;
                    corrected |= AccumulateContact(BlendSkin(surfaceBindings[edge.A].Point, surfaceBindings[edge.B].Point, fraction), edge.Chain, correction);
                }
            }
            return corrected;
        }

        // Convex segment clipping finds an arbitrary crossing position. It does
        // not assume that a vertex, midpoint or triangle centre lies inside.
        private bool ClipBodySegment(Vector3 a, Vector3 b, int volume, float clearance, int pair, out float fraction)
        {
            int first = volume * BodyPlaneCount;
            int cached = edgeSeparatingPlanes[pair] - 1;
            float cachedA = 0f, cachedB = 0f;
            if (cached >= 0)
            {
                cachedA = PlaneDistance(a, first + cached) - clearance;
                cachedB = PlaneDistance(b, first + cached) - clearance;
                // The complete segment is outside this current half-space.
                // A previous plane is only a hint, never a stale result.
                if (cachedA > 0f && cachedB > 0f) { fraction = 0f; return false; }
            }
            float enter = 0f, exit = 1f;
            for (int plane = 0; plane < BodyPlaneCount; plane++)
            {
                float da = plane == cached ? cachedA : PlaneDistance(a, first + plane) - clearance;
                float db = plane == cached ? cachedB : PlaneDistance(b, first + plane) - clearance;
                if (da > 0f && db > 0f)
                {
                    edgeSeparatingPlanes[pair] = (byte)(plane + 1);
                    fraction = 0f; return false;
                }
                float delta = db - da;
                if (Mathf.Abs(delta) < .00000001f) continue;
                float t = -da / delta;
                if (delta < 0f) enter = Mathf.Max(enter, t); else exit = Mathf.Min(exit, t);
                if (enter > exit) { fraction = 0f; return false; }
            }
            edgeSeparatingPlanes[pair] = 0;
            fraction = (enter + exit) * .5f;
            return exit - enter > .000001f;
        }

        private static SkinPoint BlendSkin(in SkinPoint a, in SkinPoint b, float t)
        {
            SkinPoint result = default;
            Append(ref result, a.A, 1f - t); Append(ref result, a.B, 1f - t);
            Append(ref result, a.C, 1f - t); Append(ref result, a.D, 1f - t);
            Append(ref result, b.A, t); Append(ref result, b.B, t);
            Append(ref result, b.C, t); Append(ref result, b.D, t);
            return result;
        }

        private static void Append(ref SkinPoint point, SkinTerm term, float fraction)
        {
            float weight = term.Weight * fraction;
            if (weight <= 0f) return;
            if (Merge(ref point.A, term, weight) || Merge(ref point.B, term, weight) ||
                Merge(ref point.C, term, weight) || Merge(ref point.D, term, weight)) return;
            throw new InvalidOperationException("An authored hair edge exceeds four skin influences.");
        }

        private static bool Merge(ref SkinTerm destination, SkinTerm term, float weight)
        {
            if (destination.Weight > 0f && destination.Bone != term.Bone) return false;
            float total = destination.Weight + weight;
            destination.Local = (destination.Local * destination.Weight + term.Local * weight) / total;
            destination.Bone = term.Bone; destination.Weight = total;
            return true;
        }

        private Vector3 InfluencedLever(in SkinPoint point, int chain, int level, Vector3 pivot)
        {
            return Lever(point.A) + Lever(point.B) + Lever(point.C) + Lever(point.D);
            Vector3 Lever(SkinTerm term)
            {
                if (term.Weight <= 0f || surfaceBoneChains[term.Bone] != chain || surfaceBoneLevels[term.Bone] < level) return Vector3.zero;
                // A lower segment keeps its own world orientation. Turning
                // this upper link translates its base without swinging the
                // entire remaining length into a rigid fan.
                Vector3 affected = surfaceBoneLevels[term.Bone] == level
                    ? surfaceMatrices[term.Bone].MultiplyPoint3x4(term.Local)
                    : contactOrigins[chain * PointsPerChain + level + 1];
                return (affected - pivot) * term.Weight;
            }
        }

        private Vector3 ResolveSurfaceBody(Vector3 point, float clearance)
        {
            for (int i = 0; i < body.Length; i++)
            {
                point = ResolveBodyVolume(point, i, clearance);
            }
            return ResolveHands(point, clearance);
        }

        private Vector3 ResolveHands(Vector3 point, float clearance)
        {
            for (int i = 0; i < arms.Length; i++)
            {
                if (!ContainsBounds(point, armMinimum[i], armMaximum[i])) continue;
                Vector3 from = armOrigins[i], direction = armDirections[i];
                float t = armLengthsSquared[i] > .000001f ? Mathf.Clamp01(Vector3.Dot(point - from, direction) / armLengthsSquared[i]) : 0f;
                Vector3 center = from + direction * t, delta = point - center;
                float radius = armRadii[i] + clearance;
                if (delta.sqrMagnitude < radius * radius)
                    point = center + (delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.back) * radius;
            }
            return point;
        }

        private void UpdateSurfaceBody()
        {
            float scale = transform.lossyScale.x;
            for (int i = 0; i < arms.Length; i++)
            {
                armOrigins[i] = arms[i].From.position;
                armDirections[i] = arms[i].To.position - armOrigins[i];
                armLengthsSquared[i] = armDirections[i].sqrMagnitude;
                armRadii[i] = arms[i].Radius * scale;
                Vector3 extent = Vector3.one * (armRadii[i] + SurfaceClearance);
                armMinimum[i] = Vector3.Min(armOrigins[i], armOrigins[i] + armDirections[i]) - extent;
                armMaximum[i] = Vector3.Max(armOrigins[i], armOrigins[i] + armDirections[i]) + extent;
            }
        }

        private Vector3 ResolveOtherStrands(Vector3 point, int ownChain, float clearance, ulong candidates = ulong.MaxValue)
        {
            const ulong segments = (1UL << 12) - 1UL;
            if ((candidates & ((segments | (segments << 13) | (segments << 26)) & ~(segments << (ownChain * 13)))) == 0) return point;
            bool projected = false;
            for (int chain = 0; chain < ChainCount; chain++)
            {
                if (chain == ownChain || (!projected && (candidates & (segments << (chain * 13))) == 0) ||
                    !ContainsBounds(point, strandMinimum[chain], strandMaximum[chain])) continue;
                for (int segment = 0; segment < 12; segment++)
                {
                    int i = chain * 13 + segment;
                    if (!projected && (candidates & (1UL << i)) == 0) continue;
                    if (!ContainsBounds(point, ringMinimum[i], ringMaximum[i]) || ringInverseLengths[i] == 0f) continue;
                    float t = Vector3.Dot(point - ringCenters[i], ringAxes[i]) * ringInverseLengths[i];
                    if (t < 0f)
                    {
                        if (segment != 0 || -t * ringAxisLengths[i] > ringHeightLengths[i] + clearance) continue;
                        t = 0f;
                    }
                    else if (t > 1f)
                    {
                        if (segment != 11 || (t - 1f) * ringAxisLengths[i] > ringHeightLengths[i + 1] + clearance) continue;
                        t = 1f;
                    }
                    Vector3 axisUnit = ringAxisUnits[i];
                    Vector3 width = Vector3.Lerp(ringWidths[i], ringWidths[i + 1], t);
                    Vector3 depth = Vector3.Lerp(ringDepths[i], ringDepths[i + 1], t);
                    Vector3 widthUnit = Vector3.ProjectOnPlane(width, axisUnit).normalized;
                    Vector3 depthUnit = Vector3.Cross(axisUnit, widthUnit).normalized;
                    float rx = width.magnitude + clearance, ry = depth.magnitude + clearance;
                    Vector3 center = Vector3.Lerp(ringCenters[i], ringCenters[i + 1], t);
                    Vector3 delta = point - center;
                    Vector3 capOffset = axisUnit * Vector3.Dot(delta, axisUnit);
                    float x = Vector3.Dot(delta, widthUnit) / rx, y = Vector3.Dot(delta, depthUnit) / ry;
                    float radius = Mathf.Sqrt(x * x + y * y);
                    if (radius >= 1f) continue;
                    if (radius < .000001f) { x = 0f; y = 1f; radius = 1f; }
                    point = center + capOffset + widthUnit * (x / radius * rx) + depthUnit * (y / radius * ry);
                    // A projected point can leave its original group box.
                    // Preserve the former sequential solve by visiting every
                    // remaining segment after that first real correction.
                    projected = true;
                }
            }
            return point;
        }

        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsBounds(Vector3 point, Vector3 minimum, Vector3 maximum) =>
            point.x >= minimum.x && point.x <= maximum.x && point.y >= minimum.y && point.y <= maximum.y &&
            point.z >= minimum.z && point.z <= maximum.z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IntersectsBounds(Vector3 aMinimum, Vector3 aMaximum, Vector3 bMinimum, Vector3 bMaximum) =>
            aMinimum.x <= bMaximum.x && aMaximum.x >= bMinimum.x && aMinimum.y <= bMaximum.y && aMaximum.y >= bMinimum.y &&
            aMinimum.z <= bMaximum.z && aMaximum.z >= bMinimum.z;

        private void MeasureSurfaceContacts()
        {
            maximumSurfaceBodyPenetration = maximumStrandOverlap = 0f;
            maximumContinuousBodyPenetration = 0f;
            deepestBodyVolume = string.Empty;
            deepestBodyPoint = deepestBodyCorrection = Vector3.zero;
            if (!HasSurfaceBindings) return;
            EnsureSurfacePose();
            for (int i = 0; i < collisionBindings.Length; i++)
            {
                if (collisionBindings[i].Attached) continue;
                Vector3 point = collisionWorldPoints[i];
                int candidates = contactGroupBodies[collisionBindings[i].Group];
                for (int volume = 0; candidates != 0; volume++, candidates >>= 1)
                    if ((candidates & 1) != 0) MeasureBodyContact(point, volume, false);
                maximumSurfaceBodyPenetration = Mathf.Max(maximumSurfaceBodyPenetration,
                    Vector3.Distance(point, ResolveHands(point, 0f)));
                maximumStrandOverlap = Mathf.Max(maximumStrandOverlap,
                    Vector3.Distance(point, ResolveOtherStrands(point, collisionBindings[i].Chain, 0f,
                        contactGroupStrands[collisionBindings[i].Group])));
            }
            for (int edgeIndex = 0; edgeIndex < surfaceEdges.Length; edgeIndex++)
            {
                SurfaceEdge edge = surfaceEdges[edgeIndex];
                if (edge.Attached) continue;
                Vector3 a = surfaceWorldVertices[edge.A], b = surfaceWorldVertices[edge.B];
                Vector3 relativeA = a - bodyPlaneOrigin, relativeB = b - bodyPlaneOrigin;
                Vector3 minimum = Vector3.Min(a, b), maximum = Vector3.Max(a, b);
                int candidates = contactGroupBodies[edge.Group];
                for (int volume = 0; candidates != 0; volume++, candidates >>= 1)
                {
                    if ((candidates & 1) == 0 || !IntersectsBodyBounds(minimum, maximum, volume) ||
                        !ClipBodySegment(relativeA, relativeB, volume, 0f, edgeIndex * body.Length + volume, out float t)) continue;
                    Vector3 point = Vector3.Lerp(a, b, t);
                    MeasureBodyContact(point, volume, true);
                }
            }
            contactDiagnosticsDirty = false;
        }

        private void MeasureBodyContact(Vector3 point, int volume, bool continuous)
        {
            Vector3 correction = ResolveBodyVolume(point, volume, 0f) - point;
            float depth = correction.magnitude;
            if (continuous) maximumContinuousBodyPenetration = Mathf.Max(maximumContinuousBodyPenetration, depth);
            else maximumSurfaceBodyPenetration = Mathf.Max(maximumSurfaceBodyPenetration, depth);
            if (depth <= deepestBodyCorrection.magnitude) return;
            deepestBodyVolume = body[volume].Name; deepestBodyPoint = point; deepestBodyCorrection = correction;
        }
    }
}
