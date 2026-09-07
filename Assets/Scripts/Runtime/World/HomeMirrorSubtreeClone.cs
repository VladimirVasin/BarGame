using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// A renderer-only copy of a built subtree, kept in step with its
    /// source. It is walked by hand rather than instantiated, so no
    /// behaviour (a hinged lid, a light halo, an effect) wakes up twice:
    /// every node becomes a bare GameObject with the same name, layer and
    /// local pose, every mesh renderer a mesh renderer sharing the same
    /// mesh, materials and property block; lights, particles, cameras,
    /// audio, colliders and scripts are left behind. Under a parent whose
    /// scale flips one axis the copy is the source's reflection.
    /// </summary>
    internal sealed class HomeMirrorSubtreeClone
    {
        private const string GpuDrivenOptOutTypeName = "DisallowGPUDrivenRendering";

        private readonly List<Transform> sourceNodes = new List<Transform>();
        private readonly List<Transform> cloneNodes = new List<Transform>();
        private readonly List<Renderer> sourceRenderers = new List<Renderer>();
        private readonly List<Renderer> cloneRenderers = new List<Renderer>();
        private readonly List<Material> sourceMaterials = new List<Material>();
        private readonly List<Material> cloneMaterials = new List<Material>();
        private MaterialPropertyBlock scratch;
        private Transform sourceRoot;
        private Transform cloneRoot;
        private Transform ownedRoot;

        public Transform Source => sourceRoot;
        public Transform Root => cloneRoot;
        public int NodeCount => cloneNodes.Count;
        public int RendererCount => cloneRenderers.Count;

        public Renderer SourceRenderer(int index) => sourceRenderers[index];
        public Renderer CloneRenderer(int index) => cloneRenderers[index];

        /// <summary>
        /// Copies <paramref name="source"/> under <paramref name="cloneParent"/>,
        /// every node's local pose verbatim. The source and the copy's parent
        /// are siblings in the same frame, so under a parent whose scale flips
        /// one axis the copy lands on the source's reflection by itself.
        /// </summary>
        public static HomeMirrorSubtreeClone Create(
            Transform source,
            Transform cloneParent,
            Func<Transform, bool> skipNode = null,
            string rootName = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (cloneParent == null)
            {
                throw new ArgumentNullException(nameof(cloneParent));
            }

            var clone = new HomeMirrorSubtreeClone();
            clone.sourceRoot = source;
            clone.Copy(source, cloneParent, skipNode, rootName ?? source.name, true);
            clone.cloneRoot = clone.cloneNodes[0];
            clone.ownedRoot = clone.cloneRoot;
            clone.SyncTransforms();
            return clone;
        }

        /// <summary>
        /// Reflects a prop nested anywhere under the source frame. Bare
        /// ancestor transforms retain imported scales and animated sockets
        /// exactly, including non-uniform scales that cannot be flattened
        /// into a world position, rotation and lossyScale without distortion.
        /// Ancestor renderers and behaviours are never copied.
        /// </summary>
        public static HomeMirrorSubtreeClone CreateInFrame(
            Transform source,
            Transform cloneParent,
            Transform sourceFrame,
            string rootName = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (cloneParent == null) throw new ArgumentNullException(nameof(cloneParent));
            if (sourceFrame == null) throw new ArgumentNullException(nameof(sourceFrame));
            if (source == sourceFrame || !source.IsChildOf(sourceFrame))
            {
                throw new ArgumentException("A reflected prop must be below its source frame.", nameof(source));
            }

            var clone = new HomeMirrorSubtreeClone { sourceRoot = source };
            var ancestors = new List<Transform>();
            for (Transform ancestor = source.parent; ancestor != sourceFrame; ancestor = ancestor.parent)
            {
                ancestors.Add(ancestor);
            }

            Transform parent = cloneParent;
            for (int index = ancestors.Count - 1; index >= 0; index--)
            {
                parent = clone.CopyTransform(ancestors[index], parent, ancestors[index].name);
            }

            int rootIndex = clone.cloneNodes.Count;
            clone.Copy(source, parent, null, rootName ?? source.name, true);
            clone.cloneRoot = clone.cloneNodes[rootIndex];
            clone.ownedRoot = clone.cloneNodes[0];
            clone.SyncTransforms();
            return clone;
        }

        /// <summary>Whether a node carries something a bare copy must not duplicate.</summary>
        public static bool IsEffectNode(Transform node)
        {
            if (node == null)
            {
                return true;
            }

            if (node.GetComponent<Light>() != null ||
                node.GetComponent<ParticleSystem>() != null ||
                node.GetComponent<Camera>() != null ||
                node.GetComponent<AudioSource>() != null ||
                node.GetComponent<CityLightHalo>() != null)
            {
                return true;
            }

            // A CPU-rebuilt mesh opted out of the GPU Resident Drawer; a
            // second renderer on the same mesh would not be.
            Component[] components = node.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component != null && component.GetType().Name == GpuDrivenOptOutTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Active flags and local poses, the root included.</summary>
        public void SyncTransforms()
        {
            for (int index = 0; index < sourceNodes.Count; index++)
            {
                Transform source = sourceNodes[index];
                Transform clone = cloneNodes[index];
                if (clone == null)
                {
                    continue;
                }

                if (source == null)
                {
                    clone.gameObject.SetActive(false);
                    continue;
                }

                if (clone.gameObject.activeSelf != source.gameObject.activeSelf)
                {
                    clone.gameObject.SetActive(source.gameObject.activeSelf);
                }

                clone.localPosition = source.localPosition;
                clone.localRotation = source.localRotation;
                clone.localScale = source.localScale;
            }
        }

        /// <summary>
        /// Enabled flags (through an optional override) and, when asked,
        /// the shared materials — a renderer whose material was swapped at
        /// runtime (the hero's bare skin) swaps its copy too.
        /// </summary>
        public void SyncRenderers(bool copyMaterials, Func<int, bool, bool> enabledOverride = null)
        {
            for (int index = 0; index < sourceRenderers.Count; index++)
            {
                Renderer source = sourceRenderers[index];
                Renderer clone = cloneRenderers[index];
                if (source == null || clone == null)
                {
                    continue;
                }

                bool enabled = enabledOverride != null
                    ? enabledOverride(index, source.enabled)
                    : source.enabled;
                if (clone.enabled != enabled)
                {
                    clone.enabled = enabled;
                }

                if (copyMaterials)
                {
                    source.GetSharedMaterials(sourceMaterials);
                    clone.GetSharedMaterials(cloneMaterials);
                    bool changed = sourceMaterials.Count != cloneMaterials.Count;
                    for (int material = 0; !changed && material < sourceMaterials.Count; material++)
                    {
                        changed = !ReferenceEquals(sourceMaterials[material], cloneMaterials[material]);
                    }

                    if (changed) clone.SetSharedMaterials(sourceMaterials);
                }
            }
        }

        /// <summary>Property blocks: tints, atlases, texture transforms.</summary>
        public void SyncPropertyBlocks()
        {
            scratch ??= new MaterialPropertyBlock();
            for (int index = 0; index < sourceRenderers.Count; index++)
            {
                Renderer source = sourceRenderers[index];
                Renderer clone = cloneRenderers[index];
                if (source == null || clone == null)
                {
                    continue;
                }

                scratch.Clear();
                source.GetPropertyBlock(scratch);
                clone.SetPropertyBlock(scratch);
            }
        }

        public void Destroy()
        {
            Transform root = ownedRoot;
            sourceRoot = cloneRoot = ownedRoot = null;
            sourceNodes.Clear();
            cloneNodes.Clear();
            sourceRenderers.Clear();
            cloneRenderers.Clear();
            if (root == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(root.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
            }
        }

        private void Copy(
            Transform source,
            Transform cloneParent,
            Func<Transform, bool> skipNode,
            string name,
            bool isRoot)
        {
            if (!isRoot && (IsEffectNode(source) || (skipNode != null && skipNode(source))))
            {
                return;
            }

            Transform clone = CopyTransform(source, cloneParent, name);
            GameObject cloneObject = clone.gameObject;

            MeshFilter filter = source.GetComponent<MeshFilter>();
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null && filter.sharedMesh != null && !IsEffectNode(source))
            {
                cloneObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer copy = cloneObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                copy.receiveShadows = renderer.receiveShadows;
                copy.lightProbeUsage = LightProbeUsage.Off;
                copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
                copy.enabled = renderer.enabled;
                scratch ??= new MaterialPropertyBlock();
                scratch.Clear();
                renderer.GetPropertyBlock(scratch);
                copy.SetPropertyBlock(scratch);
                sourceRenderers.Add(renderer);
                cloneRenderers.Add(copy);
            }

            for (int index = 0; index < source.childCount; index++)
            {
                Transform child = source.GetChild(index);
                Copy(child, clone, skipNode, child.name, false);
            }
        }

        private Transform CopyTransform(Transform source, Transform cloneParent, string name)
        {
            var cloneObject = new GameObject(name);
            cloneObject.layer = source.gameObject.layer;
            Transform clone = cloneObject.transform;
            clone.SetParent(cloneParent, false);
            clone.localPosition = source.localPosition;
            clone.localRotation = source.localRotation;
            clone.localScale = source.localScale;
            cloneObject.SetActive(source.gameObject.activeSelf);
            sourceNodes.Add(source);
            cloneNodes.Add(clone);

            return clone;
        }
    }
}
