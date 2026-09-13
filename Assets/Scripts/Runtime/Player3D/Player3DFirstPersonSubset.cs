using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal enum Player3DFirstPersonSide
    {
        Left,
        Right
    }

    /// <summary>
    /// Owns a camera-local arm subset instantiated from the production player
    /// prefab. Meshes and palette stay shared with the world character while
    /// the unused body renderers remain disabled.
    /// </summary>
    internal sealed class Player3DFirstPersonSubset : IDisposable
    {
        private readonly List<Renderer> visibleRenderers =
            new List<Renderer>();

        private Player3DAssetRegistry registry;
        private Player3DAssetRegistry sourceRegistry;
        private readonly List<Player3DMeshBinding> armBindings = new List<Player3DMeshBinding>();
        private readonly Dictionary<string, Player3DMeshBinding> sourceBindings =
            new Dictionary<string, Player3DMeshBinding>(StringComparer.Ordinal);
        private readonly MaterialPropertyBlock appearance = new MaterialPropertyBlock();

        private Player3DFirstPersonSubset()
        {
        }

        public Player3DAssetRegistry Registry => registry;
        public IReadOnlyList<Renderer> VisibleRenderers => visibleRenderers;
        public Transform SourceGrip { get; private set; }
        public Transform SourceUpperArm { get; private set; }

        public static Player3DFirstPersonSubset Create(
            Transform parent,
            Player3DFirstPersonSide side,
            int layer,
            string instanceName,
            Player3DAssetRegistry source = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Player3DFirstPersonSubset subset =
                new Player3DFirstPersonSubset();
            try
            {
                subset.registry = Player3DResources.Instantiate(parent);
                subset.registry.gameObject.name = instanceName;
                subset.sourceRegistry = source;
                if (source != null)
                    foreach (Player3DMeshBinding binding in source.MeshBindings)
                        if (binding?.Renderer != null) subset.sourceBindings[binding.MeshName] = binding;
                subset.Configure(side, layer);
                subset.AlignGripAtParentOrigin();
                return subset;
            }
            catch
            {
                subset.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            visibleRenderers.Clear();
            armBindings.Clear();
            sourceBindings.Clear();
            sourceRegistry = null;
            SourceGrip = null;
            SourceUpperArm = null;
            if (registry == null)
            {
                return;
            }

            GameObject instance = registry.gameObject;
            registry = null;
            instance.SetActive(false);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private void Configure(
            Player3DFirstPersonSide side,
            int layer)
        {
            Transform root = registry.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            PlayerWardrobe wardrobe = registry.GetComponent<PlayerWardrobe>();
            if (wardrobe != null) wardrobe.enabled = false;

            Animator animator = registry.Animator;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.enabled = false;
            }

            IReadOnlyList<Renderer> allRenderers = registry.Renderers;
            for (int index = 0; index < allRenderers.Count; index++)
            {
                Renderer renderer = allRenderers[index];
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            string sideName = side == Player3DFirstPersonSide.Left
                ? "Left"
                : "Right";
            string boneSuffix = side == Player3DFirstPersonSide.Left
                ? ".L"
                : ".R";
            IReadOnlyList<Player3DMeshBinding> bindings =
                registry.MeshBindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                Player3DMeshBinding binding = bindings[index];
                if (binding == null || binding.Renderer == null ||
                    !string.Equals(
                        binding.AnatomicalSide,
                        sideName,
                        StringComparison.Ordinal) ||
                    !IsArmBone(binding.BoneName, boneSuffix))
                {
                    continue;
                }

                Renderer renderer = binding.Renderer;
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    skinnedRenderer.updateWhenOffscreen = true;
                }

                armBindings.Add(binding);
                visibleRenderers.Add(renderer);
            }

            Player3DAnatomicalPart upperArmPart =
                side == Player3DFirstPersonSide.Left
                    ? Player3DAnatomicalPart.LeftUpperArm
                    : Player3DAnatomicalPart.RightUpperArm;
            Player3DAnatomicalPart forearmPart =
                side == Player3DFirstPersonSide.Left
                    ? Player3DAnatomicalPart.LeftForearm
                    : Player3DAnatomicalPart.RightForearm;
            Player3DAnatomicalPart handPart =
                side == Player3DFirstPersonSide.Left
                    ? Player3DAnatomicalPart.LeftHand
                    : Player3DAnatomicalPart.RightHand;
            RequireVisiblePart(upperArmPart, out Transform upperArm);
            RequireVisiblePart(forearmPart, out _);
            RequireVisiblePart(handPart, out _);
            SourceUpperArm = upperArm;
            SourceGrip = side == Player3DFirstPersonSide.Left
                ? registry.Anchors.LeftGrip
                : registry.Anchors.RightGrip;
            if (SourceGrip == null)
            {
                throw new InvalidOperationException(
                    $"Player 3D prefab has no {sideName} grip anchor.");
            }

            Transform[] hierarchy =
                root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < hierarchy.Length; index++)
            {
                hierarchy[index].gameObject.layer = layer;
            }

            Collider[] colliders =
                root.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                colliders[index].enabled = false;
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int index = 0; index < lights.Length; index++)
            {
                lights[index].enabled = false;
            }

            registry.ApplyPalette();
            RefreshAppearance();
        }

        /// <summary>Copy the live outfit, then restrict it to this authored arm.</summary>
        public void RefreshAppearance(bool shown = true)
        {
            if (sourceRegistry != null && registry != null)
                sourceRegistry.GetComponent<PlayerJacketCloth>()?.CopyPoseTo(registry.GetComponent<PlayerJacketCloth>());
            PlayerWardrobe sourceWardrobe = sourceRegistry != null ? sourceRegistry.GetComponent<PlayerWardrobe>() : null;
            PlayerWardrobe localWardrobe = registry != null ? registry.GetComponent<PlayerWardrobe>() : null;
            visibleRenderers.Clear();
            foreach (Player3DMeshBinding binding in armBindings)
            {
                Renderer target = binding.Renderer;
                bool worn = localWardrobe == null || !localWardrobe.IsConfigured || localWardrobe.IsRendererWorn(target);
                if (sourceBindings.TryGetValue(binding.MeshName, out Player3DMeshBinding source) && source.Renderer != null)
                {
                    worn = sourceWardrobe != null && sourceWardrobe.IsConfigured
                        ? sourceWardrobe.IsRendererWorn(source.Renderer) : source.Renderer.enabled;
                    target.sharedMaterials = source.Renderer.sharedMaterials;
                    appearance.Clear();
                    source.Renderer.GetPropertyBlock(appearance);
                    target.SetPropertyBlock(appearance);
                }
                target.enabled = shown && worn;
                if (worn) visibleRenderers.Add(target);
            }
        }

        private void RequireVisiblePart(
            Player3DAnatomicalPart part,
            out Transform bone)
        {
            if (!registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding) ||
                binding == null ||
                binding.Renderer == null ||
                binding.Bone == null ||
                !binding.Renderer.enabled ||
                !visibleRenderers.Contains(binding.Renderer))
            {
                throw new InvalidOperationException(
                    $"Player 3D first-person subset is missing '{part}'.");
            }

            bone = binding.Bone;
        }

        private void AlignGripAtParentOrigin()
        {
            Transform root = registry.transform;
            Transform parent = root.parent;
            Vector3 armDirection = parent.InverseTransformDirection(
                SourceGrip.position - SourceUpperArm.position);
            if (armDirection.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException(
                    "Player 3D arm and grip anchors overlap.");
            }

            Quaternion alignment = Quaternion.FromToRotation(
                armDirection.normalized,
                Vector3.up);
            Vector3 sourceForward = parent.InverseTransformDirection(
                root.TransformDirection(registry.Metrics.LocalForward));
            Vector3 alignedForward = alignment * sourceForward;
            Vector3 planarForward = Vector3.ProjectOnPlane(
                alignedForward,
                Vector3.up);
            if (planarForward.sqrMagnitude > 0.000001f)
            {
                float twist = Vector3.SignedAngle(
                    planarForward,
                    Vector3.forward,
                    Vector3.up);
                alignment = Quaternion.AngleAxis(twist, Vector3.up) *
                    alignment;
            }

            root.localRotation = alignment;
            root.localPosition = -parent.InverseTransformPoint(
                SourceGrip.position);
        }

        private static bool IsArmBone(
            string boneName,
            string suffix)
        {
            return string.Equals(
                       boneName,
                       "upper_arm" + suffix,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       boneName,
                       "forearm" + suffix,
                       StringComparison.Ordinal) ||
                   string.Equals(
                       boneName,
                       "hand" + suffix,
                       StringComparison.Ordinal);
        }
    }
}
