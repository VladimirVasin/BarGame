using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class PlayerHomeExteriorPartBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private string group;
        [SerializeField] private string sheet;
        [SerializeField] private bool emissive;
        [SerializeField] private bool castsShadows;
        [SerializeField] private Renderer renderer;

        public PlayerHomeExteriorPartBinding(
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
    public sealed class PlayerHomeExteriorAnchorBinding
    {
        [SerializeField] private string anchorName;
        [SerializeField] private string role;
        [SerializeField] private Transform anchor;

        public PlayerHomeExteriorAnchorBinding(
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
    public struct PlayerHomeExteriorDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float depth;
        [SerializeField] private float height;

        public PlayerHomeExteriorDimensions(
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
    /// Semantic bridge from the deterministic Blender-authored player-home
    /// exterior to the generated city. The prefab is presentation-only:
    /// collision, entrances, the street approach and the balcony gameplay
    /// contract stay owned by the pure plans and runtime builders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHomeExteriorAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "PlayerHome/PlayerHomeExterior3D";

        [SerializeField] private Transform modelRoot;
        [SerializeField] private PlayerHomeExteriorAnchorBinding[] anchors =
            Array.Empty<PlayerHomeExteriorAnchorBinding>();
        [SerializeField] private PlayerHomeExteriorPartBinding[] parts =
            Array.Empty<PlayerHomeExteriorPartBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private PlayerHomeExteriorDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<PlayerHomeExteriorAnchorBinding> Anchors =>
            anchors;
        public IReadOnlyList<PlayerHomeExteriorPartBinding> Parts => parts;
        public Bounds LocalBounds => localBounds;
        public PlayerHomeExteriorDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool TryGetAnchor(string role, out Transform anchor)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                PlayerHomeExteriorAnchorBinding binding = anchors[index];
                if (binding != null &&
                    string.Equals(
                        binding.Role,
                        role,
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

        public void Configure(
            Transform configuredModelRoot,
            PlayerHomeExteriorAnchorBinding[] configuredAnchors,
            PlayerHomeExteriorPartBinding[] configuredParts,
            Bounds configuredLocalBounds,
            PlayerHomeExteriorDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            anchors = configuredAnchors ??
                Array.Empty<PlayerHomeExteriorAnchorBinding>();
            parts = configuredParts ??
                Array.Empty<PlayerHomeExteriorPartBinding>();
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
