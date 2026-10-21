using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Presentation of the coast's existing stems; roots, colour and collision stay authored.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(70)]
    public sealed class CityCoastalGrassWind : MonoBehaviour
    {
        public const int MaximumStems = 42, MaximumChunks = MaximumStems;
        public const float MaximumWindTravel = .09f, MaximumInteractionTravel = .20f;
        public const float InteractionRadius = .65f, InteractionHeight = .9f;
        public const float CullingPadding = .32f;
        private static readonly int RootsId = Shader.PropertyToID("_CoastalGrassRoots");
        private static readonly int WindId = Shader.PropertyToID("_CoastalGrassWind");
        private static readonly int InteractionId = Shader.PropertyToID("_CoastalGrassInteraction");
        private static readonly int MotionId = Shader.PropertyToID("_CoastalGrassMotion");
        private static readonly int PreviousWindId = Shader.PropertyToID("_CoastalGrassPreviousWind");
        private static readonly int PreviousInteractionId = Shader.PropertyToID("_CoastalGrassPreviousInteraction");
        private static readonly int PreviousMotionId = Shader.PropertyToID("_CoastalGrassPreviousMotion");
        private static Material sharedMaterial;

        private sealed class Chunk
        {
            internal Renderer Renderer;
            internal Mesh Mesh;
            internal Material OriginalMaterial;
            internal MaterialPropertyBlock Original, Active;
            internal Vector2[] OriginalUvs, BendUvs;
            internal Bounds OriginalBounds;
            internal MotionVectorGenerationMode OriginalMotionMode;
        }
        private readonly struct Stem
        {
            internal readonly Transform Owner;
            internal readonly Vector3 Root, Tip;
            internal readonly Bounds RootBounds, TipBounds;
            internal Stem(Transform owner, Vector3 root, Vector3 tip, Bounds rootBounds, Bounds tipBounds)
            { Owner = owner; Root = root; Tip = tip; RootBounds = rootBounds; TipBounds = tipBounds; }
        }

        private readonly List<Chunk> chunks = new List<Chunk>(MaximumChunks);
        private readonly List<Stem> stems = new List<Stem>(MaximumStems);
        private CityWeatherController weather;
        private Transform hero;
        private float phaseSeconds;
        private Vector4 currentWind, currentInteraction, currentMotion;
        private Vector4 renderedWind, renderedInteraction, renderedMotion;
        private Vector4 historyWind, historyInteraction, historyMotion;
        private int renderedFrame = -1;
        private bool historyNeedsUpload = true;
        private bool parametersDirty = true;
        public int StemCount => stems.Count;
        public int ChunkCount => chunks.Count;
        public bool IsInitialized { get; private set; }
        public WindSample AppliedWind { get; private set; }
        public Vector3 AppliedInteractionPosition { get; private set; }
        public float AppliedPhaseSeconds { get; private set; }
        public float PreviousRenderedPhaseSeconds => historyWind.w;
        public Vector3 PreviousRenderedInteractionPosition => historyInteraction;
        public int MotionHistoryFrame => renderedFrame;

        // Capture diagnostics only: no UI or persistent preferences.
        public bool DebugDisableMotion { get; set; }
        public bool DebugDisableInteraction { get; set; }
        public float? DebugPhaseSeconds { get; set; }
        public Vector3? DebugInteractionPosition { get; set; }
        public WindSample? DebugWind { get; set; }

        internal void ConfigureChunk(Renderer renderer, Mesh mesh, IReadOnlyList<RuntimeOrientedBox> boxes)
        {
            if (IsInitialized || renderer == null || mesh == null || boxes == null || boxes.Count == 0)
                throw new InvalidOperationException("Configure existing coastal stems before binding their weather.");
            if (chunks.Count >= MaximumChunks || stems.Count + boxes.Count > MaximumStems)
                throw new InvalidOperationException("Coastal grass exceeds its existing fourteen clumps of three stems.");
            // The existing combined box builder retains the engine cube's 24 vertices per stem.
            // Only UV metadata changes; positions, normals and triangle topology are preserved.
            const int verticesPerStem = 24;
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length != boxes.Count * verticesPerStem)
                throw new InvalidOperationException("The existing coastal stem topology changed.");
            var roots = new Vector4[MaximumStems];
            var bendUvs = new Vector2[vertices.Length];
            for (int stem = 0; stem < boxes.Count; stem++)
            {
                RuntimeOrientedBox box = boxes[stem];
                Vector3 root = box.Center + box.Rotation * (Vector3.down * (box.Size.y * .5f));
                Vector3 tip = box.Center + box.Rotation * (Vector3.up * (box.Size.y * .5f));
                roots[stem] = new Vector4(root.x, root.y, root.z, box.Size.y);
                Quaternion inverse = Quaternion.Inverse(box.Rotation);
                Bounds rootBounds = default, tipBounds = default;
                bool hasRoot = false, hasTip = false;
                for (int vertex = 0; vertex < verticesPerStem; vertex++)
                {
                    int index = stem * verticesPerStem + vertex;
                    Vector3 local = inverse * (vertices[index] - box.Center);
                    float height = local.y / box.Size.y + .5f;
                    if (Mathf.Min(Mathf.Abs(height), Mathf.Abs(height - 1f)) > .001f)
                        throw new InvalidOperationException("A coastal stem left its authored top or rooted base plane.");
                    bool rooted = height < .5f;
                    bendUvs[index] = new Vector2(stem, rooted ? 0f : 1f);
                    if (rooted)
                    {
                        if (!hasRoot) { rootBounds = new Bounds(vertices[index], Vector3.zero); hasRoot = true; }
                        else rootBounds.Encapsulate(vertices[index]);
                    }
                    else
                    {
                        if (!hasTip) { tipBounds = new Bounds(vertices[index], Vector3.zero); hasTip = true; }
                        else tipBounds.Encapsulate(vertices[index]);
                    }
                }
                stems.Add(new Stem(renderer.transform, root, tip, rootBounds, tipBounds));
            }
            var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original);
            var active = new MaterialPropertyBlock(); renderer.GetPropertyBlock(active);
            active.SetVectorArray(RootsId, roots);
            var chunk = new Chunk { Renderer = renderer, Mesh = mesh, OriginalMaterial = renderer.sharedMaterial,
                Original = original, Active = active, OriginalUvs = mesh.uv, BendUvs = bendUvs,
                OriginalBounds = mesh.bounds, OriginalMotionMode = renderer.motionVectorGenerationMode };
            chunks.Add(chunk);
            ApplyChunk(chunk);
        }

        public void Initialize(CityWeatherController weatherController, Transform player)
        {
            if (IsInitialized) throw new InvalidOperationException("Coastal grass weather is already bound.");
            weather = weatherController != null ? weatherController : throw new ArgumentNullException(nameof(weatherController));
            hero = player != null ? player : throw new ArgumentNullException(nameof(player));
            IsInitialized = true;
            RefreshPresentation();
        }

        public bool TryGetStem(int index, out Vector3 root, out Vector3 tip)
        {
            if (index < 0 || index >= stems.Count) { root = tip = default; return false; }
            Stem stem = stems[index];
            root = stem.Owner.TransformPoint(stem.Root); tip = stem.Owner.TransformPoint(stem.Tip); return true;
        }

        public bool TryGetStemBounds(int index, out Bounds roots, out Bounds tips)
        {
            if (index < 0 || index >= stems.Count) { roots = tips = default; return false; }
            Stem stem = stems[index];
            roots = WorldBounds(stem.Owner, stem.RootBounds); tips = WorldBounds(stem.Owner, stem.TipBounds); return true;
        }
        private static Bounds WorldBounds(Transform owner, Bounds local)
        {
            Bounds world = new Bounds(owner.TransformPoint(local.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                world.Encapsulate(owner.TransformPoint(new Vector3((corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y, (corner & 4) == 0 ? local.min.z : local.max.z)));
            return world;
        }

        private void LateUpdate()
        {
            if (!IsInitialized) return;
            if (!GameTimeScaleRuntime.IsPaused) phaseSeconds += Time.deltaTime;
            RefreshPresentation();
        }

        public void RefreshPresentation()
        {
            if (!isActiveAndEnabled) return;
            AppliedWind = DebugWind ?? (weather != null ? weather.CurrentWind : new WindSample(0f, 0f));
            AppliedPhaseSeconds = DebugPhaseSeconds ?? phaseSeconds;
            AppliedInteractionPosition = DebugInteractionPosition ?? (hero != null ? hero.position : Vector3.zero);
            Vector3 direction = AppliedWind.HorizontalDirection;
            Vector4 wind = new Vector4(direction.x, direction.z, AppliedWind.Strength01, AppliedPhaseSeconds);
            Vector3 position = AppliedInteractionPosition;
            Vector4 interaction = new Vector4(position.x, position.y, position.z,
                !DebugDisableInteraction && (hero != null || DebugInteractionPosition.HasValue) ? InteractionRadius : 0f);
            Vector4 motion = new Vector4(DebugDisableMotion ? 0f : 1f,
                MaximumWindTravel, MaximumInteractionTravel, InteractionHeight);
            if (!parametersDirty && wind == currentWind && interaction == currentInteraction && motion == currentMotion) return;
            currentWind = wind; currentInteraction = interaction; currentMotion = motion; parametersDirty = false;
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i]; if (chunk.Renderer == null) continue;
                chunk.Active.SetVector(WindId, wind); chunk.Active.SetVector(InteractionId, interaction);
                chunk.Active.SetVector(MotionId, motion); chunk.Renderer.SetPropertyBlock(chunk.Active);
            }
        }

        private void PrepareRenderedMotionHistory(ScriptableRenderContext context, Camera camera)
        {
            if (!IsInitialized || !isActiveAndEnabled || camera == null ||
                camera.cameraType != CameraType.Game || camera.gameObject.scene != gameObject.scene ||
                camera.TryGetComponent(out ScenePresentationExclusion _)) return;
            if (renderedFrame != Time.frameCount)
            {
                // Advance once per rendered engine frame. A cube/preview render and repeated
                // RefreshPresentation calls must never replace the previous world frame.
                bool contiguous = renderedFrame == Time.frameCount - 1;
                Vector4 wind = contiguous ? renderedWind : currentWind;
                Vector4 interaction = contiguous ? renderedInteraction : currentInteraction;
                Vector4 motion = contiguous ? renderedMotion : currentMotion;
                historyNeedsUpload |= historyWind != wind || historyInteraction != interaction || historyMotion != motion;
                historyWind = wind;
                historyInteraction = interaction;
                historyMotion = motion;
                renderedFrame = Time.frameCount;
            }
            renderedWind = currentWind;
            renderedInteraction = currentInteraction;
            renderedMotion = currentMotion;
            if (!historyNeedsUpload) return;
            historyNeedsUpload = false;
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i]; if (chunk.Renderer == null) continue;
                chunk.Active.SetVector(PreviousWindId, historyWind);
                chunk.Active.SetVector(PreviousInteractionId, historyInteraction);
                chunk.Active.SetVector(PreviousMotionId, historyMotion);
                chunk.Renderer.SetPropertyBlock(chunk.Active);
            }
        }

        private static void ApplyChunk(Chunk chunk)
        {
            chunk.Renderer.sharedMaterial = SharedMaterial;
            chunk.Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            chunk.Mesh.uv = chunk.BendUvs;
            Vector3 scale = chunk.Renderer.transform.lossyScale;
            float minimumScale = Mathf.Max(.001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            Bounds bounds = chunk.OriginalBounds; bounds.Expand(CullingPadding * 2f / minimumScale); chunk.Mesh.bounds = bounds;
            chunk.Mesh.UploadMeshData(false);
            chunk.Renderer.SetPropertyBlock(chunk.Active);
        }

        private void OnEnable()
        {
            parametersDirty = true;
            renderedFrame = -1;
            historyNeedsUpload = true;
            RenderPipelineManager.beginCameraRendering += PrepareRenderedMotionHistory;
            for (int i = 0; i < chunks.Count; i++) if (chunks[i].Renderer != null) ApplyChunk(chunks[i]);
            RefreshPresentation();
        }
        private void OnDisable()
        {
            parametersDirty = true;
            renderedFrame = -1;
            RenderPipelineManager.beginCameraRendering -= PrepareRenderedMotionHistory;
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i];
                if (chunk.Renderer != null)
                { chunk.Renderer.sharedMaterial = chunk.OriginalMaterial; chunk.Renderer.SetPropertyBlock(chunk.Original);
                    chunk.Renderer.motionVectorGenerationMode = chunk.OriginalMotionMode; }
                if (chunk.Mesh != null)
                { chunk.Mesh.uv = chunk.OriginalUvs; chunk.Mesh.bounds = chunk.OriginalBounds; chunk.Mesh.UploadMeshData(false); }
            }
        }

        private static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;
                Shader shader = Resources.Load<Shader>("Shaders/CityCoastalGrassLit");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Missing coastal grass Lit shader.");
                sharedMaterial = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                    { name = "Coastal grass flat colour (Shared)", shader = shader, hideFlags = HideFlags.HideAndDontSave };
                sharedMaterial.SetShaderPassEnabled("MotionVectors", true);
                return sharedMaterial;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedMaterial() => sharedMaterial = null;
    }
}
