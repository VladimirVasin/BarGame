using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class MountainRoadCafePartBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private string group;
        [SerializeField] private string sheet;
        [SerializeField] private string baseSurface;
        [SerializeField] private bool emissive;
        [SerializeField] private bool castsShadows;
        [SerializeField] private bool initiallyVisible;
        [SerializeField] private Renderer renderer;

        public MountainRoadCafePartBinding(
            string configuredSourceName,
            string configuredRole,
            string configuredGroup,
            string configuredSheet,
            string configuredBaseSurface,
            bool configuredEmissive,
            bool configuredCastsShadows,
            bool configuredInitiallyVisible,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            group = configuredGroup ?? string.Empty;
            sheet = configuredSheet ?? string.Empty;
            baseSurface = configuredBaseSurface ?? string.Empty;
            emissive = configuredEmissive;
            castsShadows = configuredCastsShadows;
            initiallyVisible = configuredInitiallyVisible;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public string Group => group;
        public string Sheet => sheet;
        public string BaseSurface => baseSurface;
        public bool Emissive => emissive;
        public bool CastsShadows => castsShadows;
        public bool InitiallyVisible => initiallyVisible;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public sealed class MountainRoadCafeAnchorBinding
    {
        [SerializeField] private string anchorName;
        [SerializeField] private string role;
        [SerializeField] private Vector3 authoredForward;
        [SerializeField] private Vector3 authoredUp;
        [SerializeField] private Transform anchor;

        public MountainRoadCafeAnchorBinding(
            string configuredAnchorName,
            string configuredRole,
            Vector3 configuredForward,
            Vector3 configuredUp,
            Transform configuredAnchor)
        {
            anchorName = configuredAnchorName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            authoredForward = configuredForward;
            authoredUp = configuredUp;
            anchor = configuredAnchor;
        }

        public string AnchorName => anchorName;
        public string Role => role;
        public Vector3 AuthoredForward => authoredForward;
        public Vector3 AuthoredUp => authoredUp;
        public Transform Anchor => anchor;

        public Vector3 WorldForward(Transform modelRoot)
        {
            return modelRoot != null
                ? modelRoot.TransformDirection(authoredForward).normalized
                : authoredForward.normalized;
        }
    }

    [Serializable]
    public sealed class MountainRoadCafeDynamicPropBinding
    {
        [SerializeField] private string propName;
        [SerializeField] private string role;
        [SerializeField] private string owner;
        [SerializeField] private Transform propRoot;
        [SerializeField] private Transform liftRoot;
        [SerializeField] private Transform gripAnchor;
        [SerializeField] private Transform pourTarget;
        [SerializeField] private Transform liquidTransform;
        [SerializeField] private Renderer liquidRenderer;
        [SerializeField] private Renderer[] renderers = Array.Empty<Renderer>();
        [SerializeField] private Vector3 emptyLocalPosition;
        [SerializeField] private Vector3 fullLocalPosition;

        public MountainRoadCafeDynamicPropBinding(
            string configuredPropName,
            string configuredRole,
            string configuredOwner,
            Transform configuredPropRoot,
            Transform configuredLiftRoot,
            Transform configuredGripAnchor,
            Transform configuredPourTarget,
            Transform configuredLiquidTransform,
            Renderer configuredLiquidRenderer,
            Renderer[] configuredRenderers,
            Vector3 configuredEmptyLocalPosition,
            Vector3 configuredFullLocalPosition)
        {
            propName = configuredPropName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            owner = configuredOwner ?? string.Empty;
            propRoot = configuredPropRoot;
            liftRoot = configuredLiftRoot;
            gripAnchor = configuredGripAnchor;
            pourTarget = configuredPourTarget;
            liquidTransform = configuredLiquidTransform;
            liquidRenderer = configuredLiquidRenderer;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            emptyLocalPosition = configuredEmptyLocalPosition;
            fullLocalPosition = configuredFullLocalPosition;
        }

        public string PropName => propName;
        public string Role => role;
        public string Owner => owner;
        public Transform PropRoot => propRoot;
        public Transform LiftRoot => liftRoot;
        public Transform GripAnchor => gripAnchor;
        public Transform PourTarget => pourTarget;
        public Transform LiquidTransform => liquidTransform;
        public Renderer LiquidRenderer => liquidRenderer;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public Vector3 EmptyLocalPosition => emptyLocalPosition;
        public Vector3 FullLocalPosition => fullLocalPosition;
        public float FillTravelDistance => Vector3.Distance(
            emptyLocalPosition,
            fullLocalPosition);
    }

    public enum MountainRoadCafeColliderShape
    {
        Box,
        Capsule
    }

    [Serializable]
    public sealed class MountainRoadCafeColliderDescriptor
    {
        [SerializeField] private string stableId;
        [SerializeField] private MountainRoadCafeColliderShape shape;
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size;
        [SerializeField] private float yawDegrees;
        [SerializeField] private float radius;
        [SerializeField] private float height;

        public MountainRoadCafeColliderDescriptor(
            string configuredStableId,
            MountainRoadCafeColliderShape configuredShape,
            Vector3 configuredCenter,
            Vector3 configuredSize,
            float configuredYawDegrees,
            float configuredRadius,
            float configuredHeight)
        {
            stableId = configuredStableId ?? string.Empty;
            shape = configuredShape;
            center = configuredCenter;
            size = configuredSize;
            yawDegrees = configuredYawDegrees;
            radius = configuredRadius;
            height = configuredHeight;
        }

        public string StableId => stableId;
        public MountainRoadCafeColliderShape Shape => shape;
        public Vector3 Center => center;
        public Vector3 Size => size;
        public float YawDegrees => yawDegrees;
        public float Radius => radius;
        public float Height => height;
    }

    [Serializable]
    public struct MountainRoadCafeDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float depth;
        [SerializeField] private float height;

        public MountainRoadCafeDimensions(
            float configuredWidth,
            float configuredDepth,
            float configuredHeight)
        {
            width = configuredWidth;
            depth = configuredDepth;
            height = configuredHeight;
        }

        public float Width => width;
        public float Depth => depth;
        public float Height => height;
    }

    /// <summary>
    /// Semantic bridge for the passive authored cafe. The FBX owns visible
    /// geometry and prop sockets only; runtime plans still own collision,
    /// lights, sound and interaction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "MountainRoad/Cafe/MountainRoadCafe3D";

        [SerializeField] private Transform modelRoot;
        [SerializeField] private MountainRoadCafeAnchorBinding[] anchors =
            Array.Empty<MountainRoadCafeAnchorBinding>();
        [SerializeField] private MountainRoadCafePartBinding[] parts =
            Array.Empty<MountainRoadCafePartBinding>();
        [SerializeField] private MountainRoadCafeDynamicPropBinding[] props =
            Array.Empty<MountainRoadCafeDynamicPropBinding>();
        [SerializeField] private MountainRoadCafeColliderDescriptor[] colliders =
            Array.Empty<MountainRoadCafeColliderDescriptor>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private MountainRoadCafeDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<MountainRoadCafeAnchorBinding> Anchors => anchors;
        public IReadOnlyList<MountainRoadCafePartBinding> Parts => parts;
        public IReadOnlyList<MountainRoadCafeDynamicPropBinding> Props => props;
        public IReadOnlyList<MountainRoadCafeColliderDescriptor> Colliders => colliders;
        public Bounds LocalBounds => localBounds;
        public MountainRoadCafeDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool TryGetAnchor(string anchorName, out Transform anchor)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                MountainRoadCafeAnchorBinding binding = anchors[index];
                if (binding != null &&
                    string.Equals(
                        binding.AnchorName,
                        anchorName,
                        StringComparison.Ordinal) &&
                    binding.Anchor != null)
                {
                    anchor = binding.Anchor;
                    return true;
                }
            }

            anchor = null;
            return false;
        }

        public bool TryGetAnchorBinding(
            string anchorName,
            out MountainRoadCafeAnchorBinding binding)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                MountainRoadCafeAnchorBinding candidate = anchors[index];
                if (candidate != null &&
                    string.Equals(
                        candidate.AnchorName,
                        anchorName,
                        StringComparison.Ordinal))
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public bool TryGetProp(
            string propName,
            out MountainRoadCafeDynamicPropBinding prop)
        {
            for (int index = 0; index < props.Length; index++)
            {
                MountainRoadCafeDynamicPropBinding candidate = props[index];
                if (candidate != null &&
                    string.Equals(
                        candidate.PropName,
                        propName,
                        StringComparison.Ordinal))
                {
                    prop = candidate;
                    return true;
                }
            }

            prop = null;
            return false;
        }

        public void ApplyAppearance()
        {
            for (int index = 0; index < parts.Length; index++)
            {
                MountainRoadCafePartBinding binding = parts[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                MountainRoadCafeSurfaceAppearance.Apply(binding);
                binding.Renderer.enabled = binding.InitiallyVisible;
            }
        }

        public void Configure(
            Transform configuredModelRoot,
            MountainRoadCafeAnchorBinding[] configuredAnchors,
            MountainRoadCafePartBinding[] configuredParts,
            MountainRoadCafeDynamicPropBinding[] configuredProps,
            MountainRoadCafeColliderDescriptor[] configuredColliders,
            Bounds configuredLocalBounds,
            MountainRoadCafeDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            anchors = configuredAnchors ??
                Array.Empty<MountainRoadCafeAnchorBinding>();
            parts = configuredParts ??
                Array.Empty<MountainRoadCafePartBinding>();
            props = configuredProps ??
                Array.Empty<MountainRoadCafeDynamicPropBinding>();
            colliders = configuredColliders ??
                Array.Empty<MountainRoadCafeColliderDescriptor>();
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }
    }
}
