using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum Player3DAnatomicalPart
    {
        Head,
        Neck,
        Torso,
        Pelvis,
        LeftUpperArm,
        LeftForearm,
        LeftHand,
        RightUpperArm,
        RightForearm,
        RightHand,
        LeftThigh,
        LeftShin,
        LeftFoot,
        RightThigh,
        RightShin,
        RightFoot
    }

    [Serializable]
    public sealed class Player3DMeshBinding
    {
        [SerializeField] private string meshName;
        [SerializeField] private string role;
        [SerializeField] private string boneName;
        [SerializeField] private string bodyGroup;
        [SerializeField] private string anatomicalSide;
        [SerializeField] private string paletteMaterialName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Transform bone;
        [SerializeField] private Color baseColor = Color.white;

        public Player3DMeshBinding(
            string meshName,
            string role,
            string boneName,
            string bodyGroup,
            string anatomicalSide,
            string paletteMaterialName,
            Renderer renderer,
            Transform bone,
            Color baseColor)
        {
            this.meshName = meshName;
            this.role = role;
            this.boneName = boneName;
            this.bodyGroup = bodyGroup;
            this.anatomicalSide = anatomicalSide;
            this.paletteMaterialName = paletteMaterialName;
            this.renderer = renderer;
            this.bone = bone;
            this.baseColor = baseColor;
        }

        public string MeshName => meshName;
        public string Role => role;
        public string BoneName => boneName;
        public string BodyGroup => bodyGroup;
        public string AnatomicalSide => anatomicalSide;
        public string PaletteMaterialName => paletteMaterialName;
        public Renderer Renderer => renderer;
        public Transform Bone => bone;
        public Color BaseColor => baseColor;
    }

    [Serializable]
    public sealed class Player3DAnatomicalPartBinding
    {
        [SerializeField] private Player3DAnatomicalPart part;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Transform bone;

        public Player3DAnatomicalPartBinding(
            Player3DAnatomicalPart part,
            Renderer renderer,
            Transform bone)
        {
            this.part = part;
            this.renderer = renderer;
            this.bone = bone;
        }

        public Player3DAnatomicalPart Part => part;
        public Renderer Renderer => renderer;
        public Transform Bone => bone;
    }

    [Serializable]
    public sealed class Player3DAnimationBinding
    {
        [SerializeField] private string clipName;
        [SerializeField] private string category;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private float authoredDuration;
        [SerializeField] private bool looping;

        public Player3DAnimationBinding(
            string clipName,
            string category,
            AnimationClip clip,
            float authoredDuration,
            bool looping)
        {
            this.clipName = clipName;
            this.category = category;
            this.clip = clip;
            this.authoredDuration = authoredDuration;
            this.looping = looping;
        }

        public string ClipName => clipName;
        public string Category => category;
        public AnimationClip Clip => clip;
        public float AuthoredDuration => authoredDuration;
        public bool Looping => looping;
    }

    [Serializable]
    public struct Player3DBoneAnchors
    {
        [SerializeField] private Transform head;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;
        [SerializeField] private Transform leftGrip;
        [SerializeField] private Transform rightGrip;
        [SerializeField] private Transform rightCigarette;
        [SerializeField] private Transform mouth;

        public Player3DBoneAnchors(
            Transform head,
            Transform chest,
            Transform pelvis,
            Transform leftFoot,
            Transform rightFoot,
            Transform leftGrip,
            Transform rightGrip,
            Transform rightCigarette,
            Transform mouth)
        {
            this.head = head;
            this.chest = chest;
            this.pelvis = pelvis;
            this.leftFoot = leftFoot;
            this.rightFoot = rightFoot;
            this.leftGrip = leftGrip;
            this.rightGrip = rightGrip;
            this.rightCigarette = rightCigarette;
            this.mouth = mouth;
        }

        public Transform Head => head;
        public Transform Chest => chest;
        public Transform Pelvis => pelvis;
        public Transform LeftFoot => leftFoot;
        public Transform RightFoot => rightFoot;
        public Transform LeftGrip => leftGrip;
        public Transform RightGrip => rightGrip;
        public Transform RightCigarette => rightCigarette;
        public Transform Mouth => mouth;
    }

    [Serializable]
    public struct Player3DMetrics
    {
        [SerializeField] private float canonicalHeight;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private Vector3 localForward;

        public Player3DMetrics(
            float canonicalHeight,
            Bounds localBounds,
            Vector3 localForward)
        {
            this.canonicalHeight = canonicalHeight;
            this.localBounds = localBounds;
            this.localForward = localForward.sqrMagnitude > 0.0001f
                ? localForward.normalized
                : Vector3.forward;
        }

        public float CanonicalHeight => canonicalHeight;
        public Bounds LocalBounds => localBounds;
        public Vector3 LocalForward => localForward;
    }

    [DisallowMultipleComponent]
    public sealed class Player3DAssetRegistry : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private Player3DMeshBinding[] meshBindings =
            Array.Empty<Player3DMeshBinding>();
        [SerializeField]
        private Player3DAnatomicalPartBinding[] anatomicalParts =
            Array.Empty<Player3DAnatomicalPartBinding>();
        [SerializeField] private Player3DAnimationBinding[] animations =
            Array.Empty<Player3DAnimationBinding>();
        [SerializeField] private Player3DBoneAnchors anchors;
        [SerializeField] private Player3DMetrics metrics;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string sourcePose;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string buildSignature;
        [SerializeField] private bool applyPaletteOnEnable = true;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<Player3DMeshBinding> MeshBindings => meshBindings;
        public IReadOnlyList<Player3DAnatomicalPartBinding> AnatomicalParts =>
            anatomicalParts;
        public IReadOnlyList<Player3DAnimationBinding> Animations => animations;
        public Player3DBoneAnchors Anchors => anchors;
        public Player3DMetrics Metrics => metrics;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string SourcePose => sourcePose;
        public int SourceTriangleCount => sourceTriangleCount;
        public string BuildSignature => buildSignature;

        public void Configure(
            Animator configuredAnimator,
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            Player3DMeshBinding[] configuredMeshBindings,
            Player3DAnatomicalPartBinding[] configuredAnatomicalParts,
            Player3DAnimationBinding[] configuredAnimations,
            Player3DBoneAnchors configuredAnchors,
            Player3DMetrics configuredMetrics,
            string generatorVersion,
            string pose,
            int triangleCount,
            string configuredBuildSignature)
        {
            animator = configuredAnimator;
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            meshBindings = configuredMeshBindings ??
                Array.Empty<Player3DMeshBinding>();
            anatomicalParts = configuredAnatomicalParts ??
                Array.Empty<Player3DAnatomicalPartBinding>();
            animations = configuredAnimations ??
                Array.Empty<Player3DAnimationBinding>();
            anchors = configuredAnchors;
            metrics = configuredMetrics;
            sourceGeneratorVersion = generatorVersion ?? string.Empty;
            sourcePose = pose ?? string.Empty;
            sourceTriangleCount = triangleCount;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        public bool TryGetPart(
            Player3DAnatomicalPart part,
            out Player3DAnatomicalPartBinding binding)
        {
            for (int index = 0; index < anatomicalParts.Length; index++)
            {
                Player3DAnatomicalPartBinding candidate =
                    anatomicalParts[index];
                if (candidate != null && candidate.Part == part)
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public bool TryGetAnimation(
            string clipName,
            out Player3DAnimationBinding binding)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                binding = null;
                return false;
            }

            for (int index = 0; index < animations.Length; index++)
            {
                Player3DAnimationBinding candidate = animations[index];
                if (candidate != null && candidate.ClipName == clipName)
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public void ApplyPalette()
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            for (int index = 0; index < meshBindings.Length; index++)
            {
                Player3DMeshBinding binding = meshBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                Renderer target = binding.Renderer;
                target.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, binding.BaseColor);
                properties.SetColor(LegacyColorId, binding.BaseColor);
                target.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void OnEnable()
        {
            if (applyPaletteOnEnable)
            {
                ApplyPalette();
            }
        }
    }
}
