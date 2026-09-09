using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Authored scarf geometry with world contacts and one physical tail simulation.</summary>
    // NPC attention (350) and carried/help contact poses (400) finish first.
    [DefaultExecutionOrder(410)]
    [DisallowMultipleComponent]
    public sealed class PlayerScarfPresentation : MonoBehaviour
    {
        public const float LoweringDurationSeconds = 0.55f;
        public const float TailLength = 0.45f;
        private static readonly List<PlayerScarfPresentation> instances = new List<PlayerScarfPresentation>();
        private static Mesh metreTailMesh;
        private readonly List<Renderer> renderers = new List<Renderer>(3);
        private readonly List<Renderer> sourceRenderers = new List<Renderer>(3);
        private readonly List<Renderer> collisionExclusions = new List<Renderer>();
        private readonly List<PlayerScarfContactSurface> surfaces = new List<PlayerScarfContactSurface>(2);
        private Player3DAssetRegistry registry;
        private GameObject model;
        private SkinnedMeshRenderer wrap;
        private SkinnedMeshRenderer tail;
        private Transform sourceHead;
        private Transform head;
        private Matrix4x4 sourceHeadBindInverse;
        private Matrix4x4 tailHeadBindpose;
        private Transform clothFrame;
        private Mesh tailMesh;
        private Vector3[] tailRestVertices;
        private Vector3[] mirrorTailVertices;
        private PlayerScarfClothSimulation simulation;
        private PlayerScarfCollisionWorld collisionWorld;
        private int loweredShape = -1;
        private bool equipped;
        private bool temporaryRemoved;
        private bool headDrawn = true;
        private bool worldDrawn = true;
        private bool mirror;
        private bool visible;
        private bool physicalActive;
        private bool hasPreviousBounds;
        private Bounds previousBounds;
        private float mouthLowered;
        private Vector3 lastPosition;
        private Quaternion lastRotation;

        public IReadOnlyList<Renderer> Renderers => renderers;
        public Player3DAssetRegistry Registry => registry;
        public PlayerScarfClothSimulation TailSimulation => simulation;
        public PlayerScarfCollisionWorld CollisionWorld => collisionWorld;
        public Vector3[] TailRestVertices => tailRestVertices != null ? (Vector3[])tailRestVertices.Clone() : Array.Empty<Vector3>();
        public SkinnedMeshRenderer TailRenderer => tail;
        public bool IsEquipped => equipped;
        public bool IsVisible => visible;
        public float MouthLowered => mouthLowered;
        public double LastGeometryMilliseconds { get; private set; }
        public double LastSurfaceMilliseconds { get; private set; }
        public int LastSurfaceContactPassCount { get; private set; }

        public static PlayerScarfPresentation Install(PlayerRuntime runtime)
        {
            if (!(runtime.Visual is Player3DCharacterPresentation hero))
                throw new ArgumentException("A scarf requires the production 3D hero.", nameof(runtime));
            return Install(hero.Registry);
        }

        public static PlayerScarfPresentation Install(Player3DAssetRegistry registry, bool isMirror = false)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            PlayerScarfPresentation existing = registry.GetComponent<PlayerScarfPresentation>();
            if (existing != null) return existing;
            var result = registry.gameObject.AddComponent<PlayerScarfPresentation>();
            try { result.Initialize(registry, isMirror); return result; }
            catch { PlayerScarfResources.DestroyOwned(result); throw; }
        }

        private void Initialize(Player3DAssetRegistry owner, bool isMirror)
        {
            registry = owner;
            mirror = isMirror;
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Player3DMeshBinding binding in registry.MeshBindings)
                if (binding != null && binding.Bone != null && !string.IsNullOrEmpty(binding.BoneName))
                    bones[binding.BoneName] = binding.Bone;
            foreach (Transform bone in registry.ModelRoot.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);
            if (!bones.TryGetValue("head", out head) || !bones.ContainsKey("neck") || !bones.ContainsKey("chest"))
                throw new InvalidOperationException("The scarf requires the hero's registered head, neck and chest.");
            model = new GameObject("Player Worn Scarf Metre Frame");
            model.transform.SetParent(registry.transform, false);
            model.transform.localPosition = registry.ModelRoot.localPosition;
            model.transform.localRotation = registry.ModelRoot.localRotation;
            model.transform.localScale = Vector3.one;
            PlayerScarfResources.InstantiateModel(PlayerScarfResources.WornResourcePath, model.transform);
            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = registry.gameObject.layer;
            SkinnedMeshRenderer knot = null;
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name == "ScarfWrap") wrap = renderer;
                if (renderer.name == "ScarfKnot") knot = renderer;
                if (renderer.name == "ScarfTail") tail = renderer;
                Transform[] remapped = renderer.bones;
                for (int index = 0; index < remapped.Length; index++)
                {
                    Transform original = remapped[index];
                    if (original.name == "head" && sourceHead == null) sourceHead = original;
                    if (!bones.TryGetValue(original.name, out Transform target))
                        throw new InvalidOperationException("Unexpected scarf skin bone " + original.name);
                    remapped[index] = target;
                }
                renderer.bones = remapped;
                renderer.rootBone = head;
                renderer.updateWhenOffscreen = true;
                renderer.enabled = false;
                sourceRenderers.Add(renderer);
            }
            if (wrap == null || knot == null || tail == null || sourceHead == null || sourceRenderers.Count != 3)
                throw new InvalidOperationException("The scarf needs its authored wrap, knot, tail and head binding.");
            for (int shape = 0; shape < wrap.sharedMesh.blendShapeCount; shape++)
                if (wrap.sharedMesh.GetBlendShapeName(shape).EndsWith("MouthLowered", StringComparison.Ordinal)) loweredShape = shape;
            if (loweredShape < 0) throw new InvalidOperationException("Missing scarf mouth-lowered shape.");
            sourceHeadBindInverse = sourceHead.worldToLocalMatrix * model.transform.localToWorldMatrix;
            surfaces.Add(new PlayerScarfContactSurface(wrap, registry.transform, mirror));
            surfaces.Add(new PlayerScarfContactSurface(knot, registry.transform, mirror));
            foreach (PlayerScarfContactSurface surface in surfaces) renderers.Add(surface.Renderer);
            NormalizeTailIntoMetres();
            renderers.Add(tail);
            if (!mirror)
            {
                PlayerScarfContactSolver.Warmup();
                simulation = new PlayerScarfClothSimulation(tailRestVertices, tailMesh.triangles);
                collisionWorld = new PlayerScarfCollisionWorld();
            }
            instances.Add(this);
            lastPosition = head.position;
            lastRotation = head.rotation;
            if (!mirror) PrepareCollisionGeometry();
            ApplyVisibility();
        }

        private void PrepareCollisionGeometry()
        {
            // PlayerFactory runs inside world construction, before control is
            // handed over. Prepare the nearby source topology and static trees
            // here even when unequipped: a large terrain mesh must never be
            // decoded and indexed for the first time by the inventory button.
            UpdateTailFrame();
            simulation.Reset(clothFrame);
            Bounds bounds = new Bounds(simulation.WorldVertices[0], Vector3.zero);
            foreach (Vector3 vertex in simulation.WorldVertices) bounds.Encapsulate(vertex);
            foreach (PlayerScarfContactSurface surface in surfaces)
            {
                surface.PreparePose();
                foreach (Vector3 vertex in surface.WorldVertices) bounds.Encapsulate(vertex);
            }
            bounds.Expand(.4f);
            CollectCollisionExclusions();
            collisionWorld.Update(bounds, collisionExclusions);
            // Keep immutable geometry caches, but do not turn a loading pose
            // into swept motion when the player eventually equips the scarf.
            collisionWorld.ResetHistory();
        }

        private void NormalizeTailIntoMetres()
        {
            Matrix4x4 conversion = registry.transform.worldToLocalMatrix * tail.transform.localToWorldMatrix;
            Mesh imported = tail.sharedMesh;
            int headIndex = Array.IndexOf(tail.bones, head);
            if (headIndex < 0) throw new InvalidOperationException("The scarf tail must follow the production head.");
            tailHeadBindpose = imported.bindposes[headIndex] * conversion.inverse;
            if (metreTailMesh == null)
            {
                metreTailMesh = Instantiate(imported);
                metreTailMesh.name = "Scarf Tail Authored Metre Mesh";
                metreTailMesh.hideFlags = HideFlags.HideAndDontSave;
                Vector3[] vertices = imported.vertices;
                Vector3[] normals = imported.normals;
                Vector4[] tangents = imported.tangents;
                Matrix4x4 normalMatrix = conversion.inverse.transpose;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = conversion.MultiplyPoint3x4(vertices[i]);
                for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                TransformTangents(tangents, conversion);
                var weights = new BoneWeight[vertices.Length];
                for (int i = 0; i < weights.Length; i++)
                    weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                metreTailMesh.vertices = vertices;
                metreTailMesh.normals = normals;
                metreTailMesh.tangents = tangents;
                metreTailMesh.boneWeights = weights;
                metreTailMesh.bindposes = new[] { Matrix4x4.identity };
                metreTailMesh.RecalculateBounds();
            }
            // The rest mesh is immutable; every actor and reflection owns a
            // distinct deformation buffer with the original authored topology.
            tailMesh = Instantiate(metreTailMesh);
            tailMesh.name = mirror ? "Scarf Reflected Tail" : "Scarf Physical Tail";
            tailMesh.hideFlags = HideFlags.HideAndDontSave;
            tailMesh.MarkDynamic();
            tailRestVertices = metreTailMesh.vertices;
            mirrorTailVertices = new Vector3[tailRestVertices.Length];
            var host = new GameObject("ScarfTail");
            host.transform.SetParent(registry.transform, false);
            host.layer = registry.gameObject.layer;
            clothFrame = host.transform;
            if (!mirror) UpdateTailFrame();
            var normalized = host.AddComponent<SkinnedMeshRenderer>();
            normalized.sharedMesh = tailMesh;
            normalized.sharedMaterial = PlayerScarfResources.SharedMaterial;
            normalized.bones = new[] { host.transform };
            normalized.rootBone = host.transform;
            normalized.updateWhenOffscreen = true;
            normalized.localBounds = tailMesh.bounds;
            normalized.shadowCastingMode = mirror ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
            normalized.enabled = false;
            PlayerScarfResources.DestroyOwned(tail.gameObject);
            tail = normalized;
        }

        public void SetEquipped(bool value) { equipped = value; ApplyVisibility(); }
        public void SetTemporaryRemoved(bool value) { temporaryRemoved = value; ApplyVisibility(); }
        public void SyncVisibility(bool drawHead, bool drawWorld)
        { headDrawn = drawHead; worldDrawn = drawWorld; ApplyVisibility(); }

        public void SetMouthLowered(float amount)
        {
            mouthLowered = Mathf.Clamp01(amount);
            if (wrap != null && loweredShape >= 0) wrap.SetBlendShapeWeight(loweredShape, mouthLowered * 100f);
        }

        public void SetEnvironment(bool exterior, WindSample wind)
        {
            if (simulation == null) return;
            float strength = exterior ? wind.Strength01 : 0f;
            Vector3 steady = wind.HorizontalDirection * (strength * 7.5f);
            simulation.ExternalAcceleration = steady;
            simulation.RandomAcceleration = steady * .42f + Vector3.up * (strength * 1.0f);
        }

        public Vector3 GetFrontGripPosition(float lowered)
        {
            Vector3 source = Vector3.Lerp(new Vector3(0f, 1.604f, -.148f),
                new Vector3(0f, 1.474f, -.094f), Mathf.Clamp01(lowered));
            return head.TransformPoint(sourceHeadBindInverse.MultiplyPoint3x4(source));
        }

        private void ApplyVisibility()
        {
            bool next = isActiveAndEnabled && equipped && !temporaryRemoved && headDrawn && worldDrawn;
            foreach (Renderer renderer in renderers) if (renderer != null) renderer.enabled = next;
            bool nextPhysical = isActiveAndEnabled && equipped && !temporaryRemoved && worldDrawn;
            if (simulation != null)
            {
                simulation.IsActive = nextPhysical;
                if (nextPhysical && !physicalActive)
                {
                    UpdateTailFrame();
                    simulation.Reset(clothFrame);
                    hasPreviousBounds = false;
                    foreach (PlayerScarfContactSurface surface in surfaces) surface.ResetHistory();
                    AdvanceGeometry(0f, true);
                }
            }
            physicalActive = nextPhysical;
            visible = next;
        }

        private void LateUpdate()
        {
            LastGeometryMilliseconds = LastSurfaceMilliseconds = 0d;
            LastSurfaceContactPassCount = 0;
            if (mirror || registry == null || simulation == null || !physicalActive) return;
            UpdateTailFrame();
            bool teleported = (head.position - lastPosition).sqrMagnitude > .64f ||
                Quaternion.Angle(head.rotation, lastRotation) > 100f;
            if (teleported)
            {
                simulation.Reset(clothFrame);
                hasPreviousBounds = false;
                foreach (PlayerScarfContactSurface surface in surfaces) surface.ResetHistory();
            }
            float delta = GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning ? 0f : Time.deltaTime;
            AdvanceGeometry(delta, teleported);
            lastPosition = head.position;
            lastRotation = head.rotation;
        }

        private void AdvanceGeometry(float delta, bool reset)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            // A paused frame changes neither contacts nor vertex positions.
            // Initial activation/teleport gets exactly one contact correction.
            if (delta <= 0f && !reset)
            {
                return;
            }

            foreach (PlayerScarfContactSurface surface in surfaces) surface.PreparePose();

            Bounds bounds = new Bounds(clothFrame.TransformPoint(tailRestVertices[0]), Vector3.zero);
            foreach (Vector3 vertex in tailRestVertices) bounds.Encapsulate(clothFrame.TransformPoint(vertex));
            foreach (Vector3 vertex in simulation.WorldVertices) bounds.Encapsulate(vertex);
            foreach (PlayerScarfContactSurface surface in surfaces)
            {
                foreach (Vector3 vertex in surface.WorldVertices) bounds.Encapsulate(vertex);
                if (surface.HasPrevious)
                    foreach (Vector3 vertex in surface.PreviousWorldVertices) bounds.Encapsulate(vertex);
            }
            Bounds swept = bounds;
            if (hasPreviousBounds) swept.Encapsulate(previousBounds);
            swept.Expand(.4f);
            CollectCollisionExclusions();

            if (reset) collisionWorld.ResetHistory();
            collisionWorld.Update(swept, collisionExclusions);

            simulation.Step(delta, clothFrame, collisionWorld);
            simulation.WriteToMesh(tailMesh, clothFrame);

            tail.localBounds = tailMesh.bounds;
            long surfaceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (PlayerScarfContactSurface surface in surfaces)
            {
                surface.Resolve(collisionWorld, reset);
                LastSurfaceContactPassCount += PlayerScarfContactSolver.LastPassCount;
            }
            LastSurfaceMilliseconds = ElapsedMilliseconds(surfaceStarted);
            previousBounds = bounds;
            hasPreviousBounds = true;
            LastGeometryMilliseconds = ElapsedMilliseconds(started);
        }

        private static double ElapsedMilliseconds(long started) =>
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;

        private void CollectCollisionExclusions()
        {
            collisionExclusions.Clear();
            foreach (PlayerScarfPresentation instance in instances)
            {
                if (instance == null) continue;
                foreach (Renderer renderer in instance.sourceRenderers) if (renderer != null) collisionExclusions.Add(renderer);
                foreach (Renderer renderer in instance.renderers) if (renderer != null) collisionExclusions.Add(renderer);
            }
        }

        private void UpdateTailFrame()
        {
            if (clothFrame == null || head == null || mirror) return;
            Matrix4x4 skin = head.localToWorldMatrix * tailHeadBindpose;
            clothFrame.SetPositionAndRotation(skin.MultiplyPoint3x4(Vector3.zero), skin.rotation);
        }

        private static void TransformTangents(Vector4[] tangents, Matrix4x4 matrix)
        {
            float handedness = matrix.determinant < 0f ? -1f : 1f;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector4 original = tangents[i];
                Vector3 direction = matrix.MultiplyVector(new Vector3(original.x, original.y, original.z)).normalized;
                tangents[i] = new Vector4(direction.x, direction.y, direction.z, original.w * handedness);
            }
        }

        public void CopyAppearanceTo(PlayerScarfPresentation target)
        {
            if (target == null || !target.mirror || tailMesh == null || target.tailMesh == null) return;
            target.SetEquipped(equipped);
            target.SetTemporaryRemoved(temporaryRemoved);
            target.SetMouthLowered(mouthLowered);
            target.SyncVisibility(true, worldDrawn);
            for (int i = 0; i < surfaces.Count && i < target.surfaces.Count; i++) surfaces[i].CopyTo(target.surfaces[i]);
            Vector3[] vertices = tailMesh.vertices;
            Matrix4x4 toActor = registry.transform.worldToLocalMatrix * clothFrame.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++)
                target.mirrorTailVertices[i] = toActor.MultiplyPoint3x4(vertices[i]);
            target.tailMesh.vertices = target.mirrorTailVertices;
            target.tailMesh.RecalculateNormals();
            target.tailMesh.RecalculateBounds();
            target.tail.localBounds = target.tailMesh.bounds;
        }

        private void OnDisable()
        {
            if (simulation != null) simulation.IsActive = false;
            foreach (Renderer renderer in renderers) if (renderer != null) renderer.enabled = false;
            physicalActive = visible = false;
            hasPreviousBounds = false;
        }

        private void OnEnable() { if (registry != null) ApplyVisibility(); }

        private void OnDestroy()
        {
            instances.Remove(this);
            bool hasPhysicalOwner = false;
            foreach (PlayerScarfPresentation instance in instances)
                if (instance != null && !instance.mirror) { hasPhysicalOwner = true; break; }
            if (!hasPhysicalOwner) PlayerScarfContactSolver.ReleaseBuffers();
            simulation?.Dispose();
            collisionWorld?.Dispose();
            foreach (PlayerScarfContactSurface surface in surfaces) surface.Dispose();
            if (tail != null) PlayerScarfResources.DestroyOwned(tail.gameObject);
            PlayerScarfResources.DestroyOwned(tailMesh);
            PlayerScarfResources.DestroyOwned(model);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            instances.Clear();
            metreTailMesh = null;
        }
    }
}
