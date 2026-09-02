using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [Serializable]
    public sealed class SupermarketInteriorPartBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private string group;
        [SerializeField] private string sheet;
        [SerializeField] private Color baseColor = Color.white;
        [SerializeField] private bool emissive;
        [SerializeField] private bool castsShadows;
        [SerializeField] private bool initiallyVisible = true;
        [SerializeField] private Renderer renderer;

        public SupermarketInteriorPartBinding(
            string configuredSourceName,
            string configuredRole,
            string configuredGroup,
            string configuredSheet,
            Color configuredBaseColor,
            bool configuredEmissive,
            bool configuredCastsShadows,
            bool configuredInitiallyVisible,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            group = configuredGroup ?? string.Empty;
            sheet = configuredSheet ?? string.Empty;
            baseColor = configuredBaseColor;
            emissive = configuredEmissive;
            castsShadows = configuredCastsShadows;
            initiallyVisible = configuredInitiallyVisible;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public string Group => group;
        public string Sheet => sheet;
        public string SurfaceKind => sheet;
        public Color BaseColor => baseColor;
        public bool Emissive => emissive;
        public bool CastsShadows => castsShadows;
        public bool InitiallyVisible => initiallyVisible;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public sealed class SupermarketInteriorAnchorBinding
    {
        [SerializeField] private string anchorName;
        [SerializeField] private string role;
        [SerializeField] private Transform anchor;

        public SupermarketInteriorAnchorBinding(
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
    public struct SupermarketInteriorDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float depth;
        [SerializeField] private float height;
        [SerializeField] private float wallThickness;
        [SerializeField] private float entranceWidth;
        [SerializeField] private float entranceHeight;

        public SupermarketInteriorDimensions(
            float configuredWidth,
            float configuredDepth,
            float configuredHeight,
            float configuredWallThickness,
            float configuredEntranceWidth,
            float configuredEntranceHeight)
        {
            width = configuredWidth;
            depth = configuredDepth;
            height = configuredHeight;
            wallThickness = configuredWallThickness;
            entranceWidth = configuredEntranceWidth;
            entranceHeight = configuredEntranceHeight;
        }

        public float Width => width;
        public float Depth => depth;
        public float Height => height;
        public float WallThickness => wallThickness;
        public float EntranceWidth => entranceWidth;
        public float EntranceHeight => entranceHeight;
    }

    /// <summary>
    /// Typed bridge from the deterministic Blender-authored shop dressing to
    /// runtime composition. The prefab is passive: the layout plan continues
    /// to own collision, products, interactions, lights and moving actors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketInteriorAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "Supermarket/SupermarketInterior3D";
        public const string ExpectedDesignId =
            "supermarket_interior_v1";

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        [SerializeField] private Transform modelRoot;
        [SerializeField] private SupermarketInteriorAnchorBinding[] anchors =
            Array.Empty<SupermarketInteriorAnchorBinding>();
        [SerializeField] private SupermarketInteriorPartBinding[] parts =
            Array.Empty<SupermarketInteriorPartBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private SupermarketInteriorDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<SupermarketInteriorAnchorBinding> Anchors =>
            anchors;
        public IReadOnlyList<SupermarketInteriorPartBinding> Parts => parts;
        public Bounds LocalBounds => localBounds;
        public SupermarketInteriorDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public Transform GetAnchor(string role)
        {
            if (TryGetAnchor(role, out Transform anchor))
            {
                return anchor;
            }

            throw new KeyNotFoundException(
                $"The supermarket interior has no anchor '{role}'.");
        }

        public bool TryGetAnchor(string role, out Transform anchor)
        {
            if (TryGetAnchorBinding(
                    role,
                    out SupermarketInteriorAnchorBinding binding))
            {
                anchor = binding.Anchor;
                return true;
            }

            anchor = null;
            return false;
        }

        public bool TryGetAnchorBinding(
            string role,
            out SupermarketInteriorAnchorBinding binding)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                SupermarketInteriorAnchorBinding candidate = anchors[index];
                if (candidate != null && candidate.Anchor != null &&
                    (string.Equals(
                         candidate.Role,
                         role,
                         StringComparison.Ordinal) ||
                     string.Equals(
                         candidate.AnchorName,
                         role,
                         StringComparison.Ordinal)))
                {
                    binding = candidate;
                    return true;
                }
            }

            binding = null;
            return false;
        }

        public bool TryGetPart(
            string sourceName,
            out SupermarketInteriorPartBinding part)
        {
            for (int index = 0; index < parts.Length; index++)
            {
                SupermarketInteriorPartBinding candidate = parts[index];
                if (candidate != null &&
                    string.Equals(
                        candidate.SourceName,
                        sourceName,
                        StringComparison.Ordinal))
                {
                    part = candidate;
                    return true;
                }
            }

            part = null;
            return false;
        }

        public IReadOnlyList<SupermarketInteriorPartBinding> GetPartsByRole(
            string role)
        {
            var matches = new List<SupermarketInteriorPartBinding>();
            for (int index = 0; index < parts.Length; index++)
            {
                SupermarketInteriorPartBinding part = parts[index];
                if (part != null &&
                    string.Equals(part.Role, role, StringComparison.Ordinal))
                {
                    matches.Add(part);
                }
            }

            return matches;
        }

        public Renderer GetRendererByRole(string role, int roleIndex = 0)
        {
            if (TryGetRendererByRole(role, roleIndex, out Renderer renderer))
            {
                return renderer;
            }

            throw new KeyNotFoundException(
                $"The supermarket interior has no renderer role '{role}' " +
                $"at index {roleIndex}.");
        }

        public bool TryGetRendererByRole(
            string role,
            out Renderer renderer)
        {
            return TryGetRendererByRole(role, 0, out renderer);
        }

        public bool TryGetRendererByRole(
            string role,
            int roleIndex,
            out Renderer renderer)
        {
            if (roleIndex < 0)
            {
                renderer = null;
                return false;
            }

            int matchIndex = 0;
            for (int index = 0; index < parts.Length; index++)
            {
                SupermarketInteriorPartBinding part = parts[index];
                if (part == null || part.Renderer == null ||
                    !MatchesRendererRole(part, role))
                {
                    continue;
                }

                if (matchIndex == roleIndex)
                {
                    renderer = part.Renderer;
                    return true;
                }

                matchIndex++;
            }

            renderer = null;
            return false;
        }

        private static bool MatchesRendererRole(
            SupermarketInteriorPartBinding part,
            string requestedRole)
        {
            if (string.Equals(
                    part.Role,
                    requestedRole,
                    StringComparison.Ordinal))
            {
                return true;
            }

            // The manifest groups all four meshes under the semantic
            // fluorescent_tube role. Runtime choreography addresses the
            // individual authored rows using their matching anchor IDs.
            if (string.IsNullOrEmpty(requestedRole) ||
                !requestedRole.StartsWith(
                    "tube_",
                    StringComparison.Ordinal) ||
                requestedRole.Length != 7)
            {
                return false;
            }

            string ordinal = requestedRole.Substring(5, 2);
            return string.Equals(
                part.SourceName,
                $"Fluorescent Tube {ordinal}",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Re-applies all material property blocks after instantiation. Unity
        /// does not serialize MaterialPropertyBlock state into the prefab.
        /// Authored meshes already contain their metre-aware UVs, so the
        /// texture transform remains identity here.
        /// </summary>
        public void ApplySurfaceAppearance()
        {
            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < parts.Length; index++)
            {
                SupermarketInteriorPartBinding part = parts[index];
                if (part == null || part.Renderer == null)
                {
                    continue;
                }

                Renderer renderer = part.Renderer;
                renderer.sharedMaterial = part.Emissive
                    ? CityNightResources.EmissiveMaterial
                    : RuntimePrimitiveFactory.DefaultMaterial;
                renderer.shadowCastingMode = part.CastsShadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = part.CastsShadows;
                renderer.enabled = part.InitiallyVisible;

                properties.Clear();
                if (part.Emissive)
                {
                    ApplyEmissiveAppearance(properties, part.BaseColor);
                }
                else if (TryParseSurfaceKind(
                             part.SurfaceKind,
                             out SupermarketSurfaceKind surfaceKind))
                {
                    ApplyTexturedAppearance(
                        properties,
                        surfaceKind,
                        part.BaseColor);
                }
                else
                {
                    ApplyFlatAppearance(properties, part.BaseColor);
                }

                renderer.SetPropertyBlock(properties);
            }
        }

        public void Configure(
            Transform configuredModelRoot,
            SupermarketInteriorAnchorBinding[] configuredAnchors,
            SupermarketInteriorPartBinding[] configuredParts,
            Bounds configuredLocalBounds,
            SupermarketInteriorDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            anchors = configuredAnchors ??
                Array.Empty<SupermarketInteriorAnchorBinding>();
            parts = configuredParts ??
                Array.Empty<SupermarketInteriorPartBinding>();
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        private static bool TryParseSurfaceKind(
            string value,
            out SupermarketSurfaceKind kind)
        {
            kind = default;
            return !string.IsNullOrWhiteSpace(value) &&
                Enum.TryParse(value.Trim(), true, out kind) &&
                Enum.IsDefined(typeof(SupermarketSurfaceKind), kind);
        }

        private static void ApplyTexturedAppearance(
            MaterialPropertyBlock properties,
            SupermarketSurfaceKind kind,
            Color sourceTint)
        {
            HomeSurfaceRecipe recipe =
                SupermarketSurfaceAppearance.GetRecipe(kind);
            Color displayTint =
                SupermarketSurfaceAppearance.CreateDisplayTint(
                    sourceTint,
                    kind);
            properties.SetTexture(
                BaseMapId,
                SupermarketSurfaceAppearance.GetTexture(kind));
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(LegacyColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
        }

        private static void ApplyFlatAppearance(
            MaterialPropertyBlock properties,
            Color color)
        {
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor(BaseColorId, color);
            properties.SetColor(LegacyColorId, color);
            properties.SetFloat(SmoothnessId, 0.08f);
            properties.SetFloat(MetallicId, 0f);
        }

        private static void ApplyEmissiveAppearance(
            MaterialPropertyBlock properties,
            Color color)
        {
            properties.SetColor(BaseColorId, color);
            properties.SetColor(LegacyColorId, color);
            properties.SetColor(EmissionColorId, color * 1.35f);
            properties.SetFloat(SmoothnessId, 0.10f);
            properties.SetFloat(MetallicId, 0f);
        }
    }
}
