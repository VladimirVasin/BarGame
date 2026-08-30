using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class SupermarketExteriorPartBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private string group;
        [SerializeField] private string sheet;
        [SerializeField] private bool emissive;
        [SerializeField] private bool castsShadows;
        [SerializeField] private Renderer renderer;

        public SupermarketExteriorPartBinding(
            string configuredSourceName,
            string configuredRole,
            string configuredGroup,
            string configuredSheet,
            bool configuredEmissive,
            bool configuredCastsShadows,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            group = configuredGroup ?? string.Empty;
            sheet = configuredSheet ?? string.Empty;
            emissive = configuredEmissive;
            castsShadows = configuredCastsShadows;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public string Group => group;
        public string Sheet => sheet;
        public bool Emissive => emissive;
        public bool CastsShadows => castsShadows;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public sealed class SupermarketExteriorAnchorBinding
    {
        [SerializeField] private string anchorName;
        [SerializeField] private string role;
        [SerializeField] private Transform anchor;

        public SupermarketExteriorAnchorBinding(
            string configuredAnchorName,
            string configuredRole,
            Transform configuredAnchor)
        {
            anchorName = configuredAnchorName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            anchor = configuredAnchor;
        }

        public string AnchorName => anchorName;
        public string Role => role;
        public Transform Anchor => anchor;
    }

    [Serializable]
    public struct SupermarketExteriorDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float depth;
        [SerializeField] private float height;

        public SupermarketExteriorDimensions(
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
    /// Semantic bridge from the deterministic Blender supermarket exterior
    /// to the runtime world. The authored asset is deliberately passive:
    /// gameplay collision, entrance interaction and light sources remain
    /// owned by the city plans.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketExteriorAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "Supermarket/SupermarketExterior3D";

        [SerializeField] private Transform modelRoot;
        [SerializeField] private SupermarketExteriorAnchorBinding[] anchors =
            Array.Empty<SupermarketExteriorAnchorBinding>();
        [SerializeField] private SupermarketExteriorPartBinding[] parts =
            Array.Empty<SupermarketExteriorPartBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private SupermarketExteriorDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<SupermarketExteriorAnchorBinding> Anchors =>
            anchors;
        public IReadOnlyList<SupermarketExteriorPartBinding> Parts => parts;
        public Bounds LocalBounds => localBounds;
        public SupermarketExteriorDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool TryGetAnchor(string role, out Transform anchor)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                SupermarketExteriorAnchorBinding binding = anchors[index];
                if (binding != null &&
                    string.Equals(binding.Role, role, StringComparison.Ordinal) &&
                    binding.Anchor != null)
                {
                    anchor = binding.Anchor;
                    return true;
                }
            }

            anchor = null;
            return false;
        }

        public void Configure(
            Transform configuredModelRoot,
            SupermarketExteriorAnchorBinding[] configuredAnchors,
            SupermarketExteriorPartBinding[] configuredParts,
            Bounds configuredLocalBounds,
            SupermarketExteriorDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            anchors = configuredAnchors ??
                Array.Empty<SupermarketExteriorAnchorBinding>();
            parts = configuredParts ??
                Array.Empty<SupermarketExteriorPartBinding>();
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }
    }
}
