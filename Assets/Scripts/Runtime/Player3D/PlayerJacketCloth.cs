using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Anchored field-jacket hem and cuffs, solved after animation on the original continuous rig.</summary>
    [DefaultExecutionOrder(408)]
    [DisallowMultipleComponent]
    public sealed class PlayerJacketCloth : MonoBehaviour
    {
        public const int NodeCount = 16;
        private const float StepSeconds = 1f / 120f;
        private const float MaximumStep = .25f;

        [Serializable]
        public sealed class SurfaceBinding
        {
            [SerializeField] private SkinnedMeshRenderer renderer;
            [SerializeField] private int region;
            [SerializeField] private Mesh source;
            [SerializeField] private Bounds authoredBounds;
            [SerializeField] private float[] freedom = Array.Empty<float>();
            [SerializeField] private int[] firstNode = Array.Empty<int>();
            [SerializeField] private int[] secondNode = Array.Empty<int>();
            [SerializeField] private float[] secondWeight = Array.Empty<float>();
            public SurfaceBinding(SkinnedMeshRenderer renderer, int region)
            { this.renderer = renderer; this.region = region; }
            public SkinnedMeshRenderer Renderer => renderer;
            public int Region => region;
            public Mesh Source => source;
            public Bounds AuthoredBounds => authoredBounds;
            internal float[] Freedom => freedom;
            internal int[] FirstNode => firstNode;
            internal int[] SecondNode => secondNode;
            internal float[] SecondWeight => secondWeight;
            internal void Configure()
            {
                source = renderer.sharedMesh;
                authoredBounds = renderer.localBounds;
                freedom = new float[source.vertexCount];
                firstNode = new int[source.vertexCount]; secondNode = new int[source.vertexCount];
                secondWeight = new float[source.vertexCount];
            }
        }

        [Serializable]
        private struct NodeBinding
        {
            public int Surface, Vertex;
            public Vector3 MeshPoint;
        }

        [SerializeField] private Player3DAssetRegistry registry;
        [SerializeField] private SurfaceBinding[] bindings = Array.Empty<SurfaceBinding>();
        [SerializeField] private NodeBinding[] nodes = Array.Empty<NodeBinding>();
        private PlayerJacketClothSurface[] surfaces;
        private PlayerScarfBodyContacts contacts;
        private PlayerWardrobe wardrobe;
        private PlayerSecondaryMotionEnvironment environment;
        private readonly Vector3[] points = new Vector3[NodeCount];
        private readonly Vector3[] velocities = new Vector3[NodeCount];
        private readonly Vector3[] targets = new Vector3[NodeCount];
        private readonly Vector3[] previousTargets = new Vector3[NodeCount];
        private readonly Vector3[] before = new Vector3[NodeCount];
        private readonly Vector3[] displacements = new Vector3[NodeCount];
        private Vector3 previousPosition;
        private Quaternion previousRotation;
        private double previousSeconds, simulationSeconds;
        private bool driven, initialized, resetPending = true;

        public bool HasAuthoredBindings
        {
            get
            {
                if (registry == null || bindings == null || bindings.Length == 0 || nodes == null || nodes.Length != NodeCount) return false;
                foreach (SurfaceBinding binding in bindings)
                    if (binding == null || binding.Renderer == null || binding.Source == null ||
                        binding.Freedom.Length != binding.Source.vertexCount || binding.FirstNode.Length != binding.Source.vertexCount ||
                        binding.SecondNode.Length != binding.Source.vertexCount || binding.SecondWeight.Length != binding.Source.vertexCount) return false;
                foreach (NodeBinding node in nodes)
                    if (node.Surface < 0 || node.Surface >= bindings.Length || node.Vertex < 0 ||
                        node.Vertex >= bindings[node.Surface].Source.vertexCount) return false;
                return true;
            }
        }
        public bool IsInitialized => initialized;
        public bool IsRuntimeDriven => driven;
        public bool IsActive { get; private set; }
        public int SurfaceCount => bindings.Length;
        public int ResetCount { get; private set; }
        public float LastStepSeconds { get; private set; }
        public float MaximumDisplacement { get; private set; }
        public int LastContactCount { get; private set; }
        public float MaximumSpeed
        {
            get { float value = 0f; foreach (Vector3 speed in velocities) value = Mathf.Max(value, speed.magnitude); return value; }
        }
        public Vector3 WorldPoint(int index) => points[index];
        public Vector3 RestWorldPoint(int index) => targets[index];
        public Mesh SourceMesh(int index) => bindings[index].Source;
        public Mesh DeformedMesh(int index) => surfaces == null ? null : surfaces[index].Mesh;

        /// <summary>Editor measures the imported bind pose. Region 0 is the open hem; 1/2 are left/right cuffs.</summary>
        public void Configure(Player3DAssetRegistry owner, SurfaceBinding[] configured, Vector3[] hemNodesActorLocal,
            float hemPinHeight = 1.115f, float hemFreeHeight = .805f,
            float cuffPinFraction = .52f, float cuffFreeTipOffset = .004f)
        {
            if (owner == null || configured == null || configured.Length == 0 ||
                hemNodesActorLocal == null || hemNodesActorLocal.Length != 8 || hemPinHeight <= hemFreeHeight)
                throw new ArgumentException("Jacket cloth requires its authored surfaces and eight ordered open-hem nodes.");
            DisposeSurfaces();
            registry = owner;
            bindings = (SurfaceBinding[])configured.Clone();
            var positions = new Vector3[bindings.Length][];
            var seen = new HashSet<SkinnedMeshRenderer>();
            foreach (SurfaceBinding binding in bindings)
            {
                if (binding == null || binding.Renderer == null || binding.Region < 0 || binding.Region > 2 ||
                    binding.Renderer.sharedMesh == null || !seen.Add(binding.Renderer))
                    throw new ArgumentException("Jacket cloth has a missing, duplicate or invalid surface.");
                binding.Configure();
            }
            Transform[] elbows = { null, FindBone(Player3DAnatomicalPart.LeftForearm), FindBone(Player3DAnatomicalPart.RightForearm) };
            Transform[] wrists = { null, FindBone(Player3DAnatomicalPart.LeftHand), FindBone(Player3DAnatomicalPart.RightHand) };
            for (int surface = 0; surface < bindings.Length; surface++)
            {
                SurfaceBinding binding = bindings[surface];
                positions[surface] = BindWorldVertices(binding);
                for (int vertex = 0; vertex < positions[surface].Length; vertex++)
                {
                    Vector3 world = positions[surface][vertex];
                    float amount;
                    if (binding.Region == 0)
                        amount = (hemPinHeight - transform.InverseTransformPoint(world).y) / (hemPinHeight - hemFreeHeight);
                    else
                    {
                        int region = binding.Region;
                        Vector3 axis = wrists[region].position - elbows[region].position;
                        float length = axis.magnitude;
                        float fraction = Vector3.Dot(world - elbows[region].position, axis.normalized) / length;
                        amount = (fraction - cuffPinFraction) / (1f + cuffFreeTipOffset / length - cuffPinFraction);
                    }
                    binding.Freedom[vertex] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
                }
            }
            var nodeWorld = new Vector3[NodeCount];
            for (int i = 0; i < 8; i++) nodeWorld[i] = transform.TransformPoint(hemNodesActorLocal[i]);
            for (int region = 1; region <= 2; region++)
            {
                Vector3 axis = (wrists[region].position - elbows[region].position).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, axis).normalized;
                if (forward.sqrMagnitude < .001f) forward = Vector3.ProjectOnPlane(transform.right, axis).normalized;
                Vector3 across = Vector3.Cross(axis, forward).normalized;
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    Vector3 direction = forward * Mathf.Cos(quadrant * Mathf.PI * .5f) + across * Mathf.Sin(quadrant * Mathf.PI * .5f);
                    float best = float.NegativeInfinity;
                    for (int surface = 0; surface < bindings.Length; surface++)
                    {
                        if (bindings[surface].Region != region) continue;
                        for (int vertex = 0; vertex < positions[surface].Length; vertex++)
                        {
                            if (bindings[surface].Freedom[vertex] < .9f) continue;
                            float score = Vector3.Dot(positions[surface][vertex] - wrists[region].position, direction);
                            if (score <= best) continue;
                            best = score; nodeWorld[8 + (region - 1) * 4 + quadrant] = positions[surface][vertex];
                        }
                    }
                    if (float.IsNegativeInfinity(best)) throw new ArgumentException("Jacket cuff has no authored free edge.");
                }
            }
            nodes = new NodeBinding[NodeCount];
            for (int node = 0; node < NodeCount; node++)
            {
                int region = node < 8 ? 0 : node < 12 ? 1 : 2;
                float nearest = float.PositiveInfinity;
                for (int surface = 0; surface < bindings.Length; surface++)
                {
                    if (bindings[surface].Region != region) continue;
                    for (int vertex = 0; vertex < positions[surface].Length; vertex++)
                    {
                        float distance = (positions[surface][vertex] - nodeWorld[node]).sqrMagnitude;
                        if (distance >= nearest) continue;
                        nearest = distance;
                        nodes[node] = new NodeBinding { Surface = surface, Vertex = vertex,
                            MeshPoint = BindSkin(bindings[surface], vertex).inverse.MultiplyPoint3x4(nodeWorld[node]) };
                    }
                }
                if (float.IsPositiveInfinity(nearest)) throw new ArgumentException("Jacket cloth is missing one motion region.");
            }
            for (int surface = 0; surface < bindings.Length; surface++)
            for (int vertex = 0; vertex < positions[surface].Length; vertex++)
                BindField(bindings[surface], vertex, positions[surface][vertex], nodeWorld);
            initialized = false;
            RequestReset();
        }

        private Transform FindBone(Player3DAnatomicalPart part)
        {
            foreach (Player3DAnatomicalPartBinding binding in registry.AnatomicalParts)
                if (binding.Part == part && binding.Bone != null) return binding.Bone;
            throw new InvalidOperationException("Jacket cloth requires the production forearm and hand anchors.");
        }

        private static Vector3[] BindWorldVertices(SurfaceBinding binding)
        {
            Vector3[] result = binding.Source.vertices;
            for (int i = 0; i < result.Length; i++) result[i] = BindSkin(binding, i).MultiplyPoint3x4(result[i]);
            return result;
        }

        private static Matrix4x4 BindSkin(SurfaceBinding binding, int vertex)
        {
            BoneWeight weight = binding.Source.boneWeights[vertex];
            Matrix4x4[] poses = binding.Source.bindposes;
            Transform[] bones = binding.Renderer.bones;
            Matrix4x4 result = Matrix4x4.zero;
            void Add(int index, float amount)
            {
                if (amount <= 0f) return;
                Matrix4x4 value = bones[index].localToWorldMatrix * poses[index];
                for (int column = 0; column < 4; column++) result.SetColumn(column, result.GetColumn(column) + value.GetColumn(column) * amount);
            }
            Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
            Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
            return result;
        }

        private static void BindField(SurfaceBinding binding, int vertex, Vector3 point, Vector3[] controls)
        {
            int start = binding.Region == 0 ? 0 : binding.Region == 1 ? 8 : 12;
            int count = binding.Region == 0 ? 8 : 4;
            float nearest = float.PositiveInfinity;
            for (int segment = 0; segment < (binding.Region == 0 ? count - 1 : count); segment++)
            {
                int a = start + segment, b = start + (segment + 1) % count;
                Vector3 edge = controls[b] - controls[a];
                float t = Mathf.Clamp01(Vector3.Dot(point - controls[a], edge) / Mathf.Max(.0000001f, edge.sqrMagnitude));
                float distance = (point - Vector3.Lerp(controls[a], controls[b], t)).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                binding.FirstNode[vertex] = a; binding.SecondNode[vertex] = b; binding.SecondWeight[vertex] = t;
            }
        }

        public void BindRuntime(PlayerRuntime owner)
        {
            if (!HasAuthoredBindings) throw new InvalidOperationException("The production jacket has no authored cloth bindings.");
            environment = new PlayerSecondaryMotionEnvironment(owner);
            driven = true;
            EnsureSurfaces();
            RequestReset();
        }

        private void EnsureSurfaces()
        {
            if (surfaces != null) return;
            surfaces = new PlayerJacketClothSurface[bindings.Length];
            for (int i = 0; i < surfaces.Length; i++) surfaces[i] = new PlayerJacketClothSurface(bindings[i]);
            wardrobe = registry.GetComponent<PlayerWardrobe>();
            contacts = new PlayerScarfBodyContacts(registry, "jacket", useMeshSupportPlanes: true);
        }

        private void LateUpdate()
        {
            if (!driven) return;
            bool paused = GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning;
            if (!paused) simulationSeconds += Time.deltaTime;
            environment.Sample(out bool outside, out WindSample wind);
            ApplyAt(simulationSeconds, paused, outside, wind);
        }

        public void RequestReset() => resetPending = true;

        public void ApplyAt(double seconds, bool paused, bool outside, WindSample wind)
        {
            if (!HasAuthoredBindings) return;
            EnsureSurfaces();
            bool worn = wardrobe == null || wardrobe.IsRendererWorn(bindings[0].Renderer);
            if (wardrobe != null && wardrobe.HasAppearanceLease && !bindings[0].Renderer.enabled) worn = false;
            IsActive = worn;
            if (!worn)
            {
                if (initialized) foreach (PlayerJacketClothSurface surface in surfaces) surface.Restore();
                initialized = false; resetPending = true; LastStepSeconds = MaximumDisplacement = 0f;
                Array.Clear(velocities, 0, velocities.Length);
                return;
            }
            if (paused && initialized && !resetPending)
            { if (IsFinite(seconds)) previousSeconds = seconds; LastStepSeconds = 0f; return; }
            foreach (PlayerJacketClothSurface surface in surfaces) surface.UpdatePose();
            for (int i = 0; i < NodeCount; i++)
                targets[i] = surfaces[nodes[i].Surface].Skin(nodes[i].Vertex).MultiplyPoint3x4(nodes[i].MeshPoint);
            contacts.UpdatePose();
            double elapsed = seconds - previousSeconds;
            bool teleported = initialized && ((transform.position - previousPosition).sqrMagnitude > .3025f ||
                Quaternion.Angle(transform.rotation, previousRotation) > 100f);
            if (!initialized || resetPending || !IsFinite(seconds) || elapsed < 0d || elapsed > MaximumStep || teleported)
            {
                Array.Copy(targets, points, NodeCount); Array.Copy(targets, previousTargets, NodeCount);
                Array.Clear(velocities, 0, NodeCount);
                initialized = true; resetPending = false; ResetCount++; LastStepSeconds = 0f;
            }
            else
            {
                LastStepSeconds = (float)elapsed;
                int steps = elapsed > 0d ? Mathf.Max(1, Mathf.CeilToInt((float)elapsed / StepSeconds)) : 0;
                float dt = steps > 0 ? (float)elapsed / steps : 0f;
                Vector3 air = outside ? wind.Velocity(1.25f) : Vector3.zero;
                for (int step = 0; step < steps; step++)
                {
                    float alpha = (step + 1f) / steps;
                    Array.Copy(points, before, NodeCount);
                    for (int i = 0; i < NodeCount; i++)
                    {
                        Vector3 rest = Vector3.Lerp(previousTargets[i], targets[i], alpha);
                        float stiffness = i < 8 ? 62f : 115f;
                        Vector3 acceleration = (rest - points[i]) * stiffness + Physics.gravity * (i < 8 ? .14f : .04f) +
                            (air - velocities[i]) * (i < 8 ? 1.7f : .65f) - velocities[i] * 7f;
                        velocities[i] = Vector3.ClampMagnitude(velocities[i] + acceleration * dt, 1.5f);
                        points[i] += velocities[i] * dt;
                    }
                    Constrain(alpha);
                    for (int i = 0; i < NodeCount; i++) velocities[i] = Vector3.ClampMagnitude((points[i] - before[i]) / dt, 1.5f);
                }
            }
            LastContactCount = 0; MaximumDisplacement = 0f;
            for (int i = 0; i < NodeCount; i++)
            {
                displacements[i] = points[i] - targets[i];
                MaximumDisplacement = Mathf.Max(MaximumDisplacement, displacements[i].magnitude);
            }
            foreach (PlayerJacketClothSurface surface in surfaces)
            { surface.Deform(displacements, contacts); LastContactCount += contacts.LastContactCount; }
            Array.Copy(targets, previousTargets, NodeCount);
            previousPosition = transform.position; previousRotation = transform.rotation;
            previousSeconds = IsFinite(seconds) ? seconds : 0d;
        }

        private void Constrain(float alpha)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < NodeCount; i++)
                {
                    int next = i < 7 ? i + 1 : i == 7 ? -1 : i == 11 ? 8 : i == 15 ? 12 : i + 1;
                    if (next < 0) continue; // The two open front edges are never sewn together.
                    Vector3 a = Vector3.Lerp(previousTargets[i], targets[i], alpha);
                    Vector3 b = Vector3.Lerp(previousTargets[next], targets[next], alpha);
                    Vector3 edge = points[next] - points[i];
                    float length = edge.magnitude;
                    if (length < .00001f) continue;
                    Vector3 correction = edge * ((length - Vector3.Distance(a, b)) / length) * .38f;
                    points[i] += correction; points[next] -= correction;
                }
                contacts.Resolve(points);
                for (int i = 0; i < NodeCount; i++)
                {
                    Vector3 rest = Vector3.Lerp(previousTargets[i], targets[i], alpha);
                    points[i] = rest + Vector3.ClampMagnitude(points[i] - rest, i < 8 ? .075f : .025f);
                }
            }
        }

        /// <summary>Mirror and camera-local subsets consume these same bind-space vertices without simulating.</summary>
        public void CopyPoseTo(PlayerJacketCloth target)
        {
            if (target == null || target == this || surfaces == null || !target.HasAuthoredBindings) return;
            target.driven = false;
            target.EnsureSurfaces();
            for (int i = 0; i < surfaces.Length; i++)
                for (int j = 0; j < target.surfaces.Length; j++)
                    if (bindings[i].Source == target.bindings[j].Source) surfaces[i].CopyTo(target.surfaces[j]);
            target.IsActive = IsActive;
        }

        private void DisposeSurfaces()
        {
            if (surfaces == null) return;
            foreach (PlayerJacketClothSurface surface in surfaces) surface?.Dispose();
            surfaces = null;
        }
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void OnEnable() => RequestReset();
        private void OnDisable() { RequestReset(); if (surfaces != null) foreach (PlayerJacketClothSurface surface in surfaces) surface.Restore(); }
        private void OnDestroy() => DisposeSurfaces();
    }
}
