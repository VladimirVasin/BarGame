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
        private MaterialPropertyBlock scratch;

        public Transform Source => sourceNodes.Count > 0 ? sourceNodes[0] : null;
        public Transform Root => cloneNodes.Count > 0 ? cloneNodes[0] : null;
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
            clone.Copy(source, cloneParent, skipNode, rootName ?? source.name, true);
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
                if (source == null || clone == null)
                {
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

                if (copyMaterials && !ReferenceEquals(clone.sharedMaterial, source.sharedMaterial))
                {
                    clone.sharedMaterials = source.sharedMaterials;
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
            Transform root = Root;
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
    }
}
