using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The marks the hero's vomit leaves in the world: one combined mesh of
    /// ragged puddle fans on the residue material, and a few small boxes for
    /// the lumps. Owns a <see cref="HeroVomitResidueModel"/> and rebuilds the
    /// mesh when it reports a change.
    ///
    /// Works in world space. It hangs under the effect host so it dies with
    /// the scene like the host does, but its transform is held at identity so
    /// the model's world-space points are the mesh's vertices unchanged. The
    /// marks are meant to outlast the bout: nothing here dries or fades.
    ///
    /// Runs after the stream effect (order 280) so the frame's impacts are on
    /// the floor before the frame is drawn.
    /// </summary>
    [DefaultExecutionOrder(281)]
    [DisallowMultipleComponent]
    public sealed class HeroVomitResidue : MonoBehaviour
    {
        public const string RuntimeObjectName = "Hero Vomit Residue";
        /// <summary>Rebuilds are batched: no more than one per this many seconds.</summary>
        public const float RebuildIntervalSeconds = 0.1f;
        /// <summary>How deep a lump sinks into the surface it landed on.</summary>
        public const float ChunkSinkFraction = 0.4f;
        private const float BoundsLiftMetres = 0.2f;

        private readonly List<Vector3> vertices = new List<Vector3>(
            HeroVomitResidueModel.MaxPatches *
            (HeroVomitResidueModel.RimVertexCount + 1));
        private readonly List<Vector3> normals = new List<Vector3>(
            HeroVomitResidueModel.MaxPatches *
            (HeroVomitResidueModel.RimVertexCount + 1));
        private readonly List<Vector2> uvs = new List<Vector2>(
            HeroVomitResidueModel.MaxPatches *
            (HeroVomitResidueModel.RimVertexCount + 1));
        private readonly List<int> triangles = new List<int>(
            HeroVomitResidueModel.MaxPatches *
            HeroVomitResidueModel.RimVertexCount * 3);
        private readonly List<GameObject> chunkBoxes =
            new List<GameObject>(HeroVomitResidueModel.MaxChunks);

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private float sinceRebuild = float.PositiveInfinity;

        public HeroVomitResidueModel Model { get; private set; }
        public bool IsInitialized => Model != null;
        public int PatchCount => Model != null ? Model.PatchCount : 0;
        public int ChunkCount => Model != null ? Model.ChunkCount : 0;
        public int RebuildCount { get; private set; }

        public float LargestRadius
        {
            get
            {
                if (Model == null)
                {
                    return 0f;
                }

                float largest = 0f;
                IReadOnlyList<HeroVomitPatch> patches = Model.Patches;
                for (int index = 0; index < patches.Count; index++)
                {
                    largest = Mathf.Max(largest, patches[index].Radius);
                }

                return largest;
            }
        }

        /// <summary>
        /// Creates the residue under a host. The host is expected to be
        /// unscaled (the status controller's `ui` object is); the residue
        /// pins its own world position and rotation to identity.
        /// </summary>
        public static HeroVomitResidue Create(Transform host, int seed)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            var residueObject = new GameObject(RuntimeObjectName);
            residueObject.transform.SetParent(host, false);
            residueObject.layer = host.gameObject.layer;
            HeroVomitResidue residue =
                residueObject.AddComponent<HeroVomitResidue>();
            residue.Initialize(seed);
            return residue;
        }

        public void Initialize(int seed)
        {
            Model = new HeroVomitResidueModel(seed);
            PinToWorld();
            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "Hero Vomit Residue",
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = IndexFormat.UInt16
                };
                mesh.MarkDynamic();
                gameObject.AddComponent<RuntimeGeneratedMeshOwner>()
                    .Initialize(mesh);
            }
            else
            {
                mesh.Clear();
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = gameObject.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = mesh;
            if (meshRenderer == null)
            {
                meshRenderer = GetComponent<MeshRenderer>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            meshRenderer.sharedMaterial = HeroVomitResources.ResidueMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = true;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            meshRenderer.enabled = false;
            DestroyChunkBoxes();
            sinceRebuild = float.PositiveInfinity;
            RebuildCount = 0;
        }

        public void AddImpact(Vector3 point, Vector3 normal, float volume)
        {
            Model?.AddImpact(point, normal, volume);
        }

        public void AddChunk(Vector3 point, Vector3 normal)
        {
            Model?.AddChunk(point, normal);
        }

        /// <summary>The puddle whose centre is closest to a point.</summary>
        public bool TryGetNearestPatch(
            Vector3 point,
            out Vector3 center,
            out Vector3 normal,
            out float radius)
        {
            center = Vector3.zero;
            normal = Vector3.up;
            radius = 0f;
            if (Model == null)
            {
                return false;
            }

            IReadOnlyList<HeroVomitPatch> patches = Model.Patches;
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int index = 0; index < patches.Count; index++)
            {
                HeroVomitPatch patch = patches[index];
                float distance = (patch.Center - point).sqrMagnitude;
                if (distance >= nearest)
                {
                    continue;
                }

                nearest = distance;
                center = patch.Center;
                normal = patch.Normal;
                radius = patch.Radius;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Rebuilds the mesh and the lump boxes from the model now, ignoring
        /// the batching interval. The effect calls this at the end of its own
        /// LateUpdate when it has to; tests call it directly.
        /// </summary>
        public void RebuildNow()
        {
            if (Model == null || mesh == null)
            {
                return;
            }

            PinToWorld();
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            triangles.Clear();
            IReadOnlyList<HeroVomitPatch> patches = Model.Patches;
            for (int index = 0; index < patches.Count; index++)
            {
                HeroVomitPatch patch = patches[index];
                HeroVomitResidueModel.BuildPatchMesh(
                    in patch,
                    vertices,
                    normals,
                    uvs,
                    triangles);
            }

            mesh.Clear();
            if (vertices.Count > 0)
            {
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0, false);
                mesh.RecalculateBounds();
                // The fans are paper-thin; a culler looking at a flat AABB
                // through a floor edge would drop them a frame early.
                Bounds bounds = mesh.bounds;
                bounds.Encapsulate(bounds.max + Vector3.up * BoundsLiftMetres);
                bounds.Encapsulate(bounds.min + Vector3.down * 0.02f);
                mesh.bounds = bounds;
            }

            meshRenderer.enabled = vertices.Count > 0;
            SyncChunkBoxes();
            Model.ClearDirty();
            sinceRebuild = 0f;
            RebuildCount++;
        }

        private void LateUpdate()
        {
            if (Model == null)
            {
                return;
            }

            sinceRebuild += Time.deltaTime;
            if (Model.Dirty && sinceRebuild >= RebuildIntervalSeconds)
            {
                RebuildNow();
            }
        }

        private void OnDestroy()
        {
            DestroyChunkBoxes();
            // The RuntimeGeneratedMeshOwner on this object destroys the mesh.
            mesh = null;
        }

        private void PinToWorld()
        {
            transform.SetPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        private void SyncChunkBoxes()
        {
            IReadOnlyList<HeroVomitChunk> chunks = Model.Chunks;
            while (chunkBoxes.Count > chunks.Count)
            {
                int last = chunkBoxes.Count - 1;
                DestroyBox(chunkBoxes[last]);
                chunkBoxes.RemoveAt(last);
            }

            for (int index = chunkBoxes.Count; index < chunks.Count; index++)
            {
                chunkBoxes.Add(CreateChunkBox(chunks[index], index));
            }
        }

        private GameObject CreateChunkBox(in HeroVomitChunk chunk, int index)
        {
            // Sunk into the surface by ChunkSinkFraction of its height so it
            // reads as lying in the film rather than balancing on it.
            Vector3 center = chunk.Position +
                             chunk.Normal *
                             (chunk.Size * (0.5f - ChunkSinkFraction));
            GameObject box = RuntimePrimitiveFactory.CreateBox(
                $"Chunk {index:00}",
                transform,
                center,
                Vector3.one * chunk.Size,
                chunk.Pale
                    ? HeroVomitResources.PaleChunkColor
                    : HeroVomitResources.ChunkColor,
                HeroVomitResources.ChunkMaterial,
                false);
            box.layer = gameObject.layer;
            box.transform.rotation =
                Quaternion.FromToRotation(Vector3.up, chunk.Normal) *
                Quaternion.Euler(0f, chunk.YawDegrees, 0f);
            // Primitive collider removal is deferred to the end of the frame;
            // the stream's own sweeps must not find the lump on its spawn frame.
            Collider pendingCollider = box.GetComponent<Collider>();
            if (pendingCollider != null)
            {
                pendingCollider.enabled = false;
            }

            var renderer = box.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return box;
        }

        private void DestroyChunkBoxes()
        {
            for (int index = 0; index < chunkBoxes.Count; index++)
            {
                DestroyBox(chunkBoxes[index]);
            }

            chunkBoxes.Clear();
        }

        private static void DestroyBox(GameObject box)
        {
            if (box == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(box);
            }
            else
            {
                DestroyImmediate(box);
            }
        }
    }
}
