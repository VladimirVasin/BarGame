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
            string instanceName)
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
