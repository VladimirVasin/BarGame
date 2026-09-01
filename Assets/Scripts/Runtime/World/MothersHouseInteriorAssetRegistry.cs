using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [Serializable]
    public sealed class MothersHouseInteriorPartBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private string group;
        [SerializeField] private string sheet;
        [SerializeField] private bool emissive;
        [SerializeField] private bool castsShadows;
        [SerializeField] private Color tint;
        [SerializeField] private Rect sourceUvBounds;
        [SerializeField] private Vector4 baseMapTransform;
        [SerializeField] private Renderer renderer;

        public MothersHouseInteriorPartBinding(
            string configuredSourceName,
            string configuredRole,
            string configuredGroup,
            string configuredSheet,
            bool configuredEmissive,
            bool configuredCastsShadows,
            Color configuredTint,
            Rect configuredSourceUvBounds,
            Vector4 configuredBaseMapTransform,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            group = configuredGroup ?? string.Empty;
            sheet = configuredSheet ?? string.Empty;
            emissive = configuredEmissive;
            castsShadows = configuredCastsShadows;
            tint = configuredTint;
            sourceUvBounds = configuredSourceUvBounds;
            baseMapTransform = configuredBaseMapTransform;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public string Group => group;
        public string Sheet => sheet;
        public bool Emissive => emissive;
        public bool CastsShadows => castsShadows;
        public Color Tint => tint;
        public Rect SourceUvBounds => sourceUvBounds;
        public Vector4 BaseMapTransform => baseMapTransform;
        public Renderer Renderer => renderer;

        public Rect TransformedUvBounds
        {
            get
            {
                float firstX =
                    sourceUvBounds.xMin * baseMapTransform.x +
                    baseMapTransform.z;
                float secondX =
                    sourceUvBounds.xMax * baseMapTransform.x +
                    baseMapTransform.z;
                float firstY =
                    sourceUvBounds.yMin * baseMapTransform.y +
                    baseMapTransform.w;
                float secondY =
                    sourceUvBounds.yMax * baseMapTransform.y +
                    baseMapTransform.w;
                return Rect.MinMaxRect(
                    Mathf.Min(firstX, secondX),
                    Mathf.Min(firstY, secondY),
                    Mathf.Max(firstX, secondX),
                    Mathf.Max(firstY, secondY));
            }
        }
    }

    [Serializable]
    public sealed class MothersHouseInteriorAnchorBinding
    {
        [SerializeField] private string anchorName;
        [SerializeField] private string role;
        [SerializeField] private Transform anchor;

        public MothersHouseInteriorAnchorBinding(
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
    public struct MothersHouseInteriorDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float depth;
        [SerializeField] private float height;
        [SerializeField] private float wallThickness;
        [SerializeField] private float doorWidth;
        [SerializeField] private float doorHeight;

        public MothersHouseInteriorDimensions(
            float configuredWidth,
            float configuredDepth,
            float configuredHeight,
            float configuredWallThickness,
            float configuredDoorWidth,
            float configuredDoorHeight)
        {
            width = configuredWidth;
            depth = configuredDepth;
            height = configuredHeight;
            wallThickness = configuredWallThickness;
            doorWidth = configuredDoorWidth;
            doorHeight = configuredDoorHeight;
        }

        public float Width => width;
        public float Depth => depth;
        public float Height => height;
        public float WallThickness => wallThickness;
        public float DoorWidth => doorWidth;
        public float DoorHeight => doorHeight;
    }

    [Serializable]
    public struct MothersHouseInteriorAtlasCell
    {
        [SerializeField] private string sheet;
        [SerializeField] private int column;
        [SerializeField] private int row;

        public MothersHouseInteriorAtlasCell(
            string configuredSheet,
            int configuredColumn,
            int configuredRow)
        {
            sheet = configuredSheet ?? string.Empty;
            column = configuredColumn;
            row = configuredRow;
        }

        public string Sheet => sheet;
        public int Column => column;
        public int Row => row;
    }

    /// <summary>
    /// Serialized runtime proof for the unique positive room atlas. Atlas
    /// rows use Unity UV order: row zero starts at the texture bottom.
    /// </summary>
    [Serializable]
    public sealed class MothersHouseInteriorAtlasContract
    {
        [SerializeField] private Texture2D texture;
        [SerializeField] private string resourcePath;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private int columns;
        [SerializeField] private int rows;
        [SerializeField] private int insetPixels;
        [SerializeField] private bool sRgb;
        [SerializeField] private bool mipmaps;
        [SerializeField] private bool uncompressed;
        [SerializeField] private TextureWrapMode wrapMode;
        [SerializeField] private FilterMode filterMode;
        [SerializeField] private MothersHouseInteriorAtlasCell[] cells =
            Array.Empty<MothersHouseInteriorAtlasCell>();

        public MothersHouseInteriorAtlasContract(
            Texture2D configuredTexture,
            string configuredResourcePath,
            int configuredWidth,
            int configuredHeight,
            int configuredColumns,
            int configuredRows,
            int configuredInsetPixels,
            bool configuredSrgb,
            bool configuredMipmaps,
            bool configuredUncompressed,
            TextureWrapMode configuredWrapMode,
            FilterMode configuredFilterMode,
            MothersHouseInteriorAtlasCell[] configuredCells)
        {
            texture = configuredTexture;
            resourcePath = configuredResourcePath ?? string.Empty;
            width = configuredWidth;
            height = configuredHeight;
            columns = configuredColumns;
            rows = configuredRows;
            insetPixels = configuredInsetPixels;
            sRgb = configuredSrgb;
            mipmaps = configuredMipmaps;
            uncompressed = configuredUncompressed;
            wrapMode = configuredWrapMode;
            filterMode = configuredFilterMode;
            cells = configuredCells ??
                Array.Empty<MothersHouseInteriorAtlasCell>();
        }

        public Texture2D Texture => texture;
        public string ResourcePath => resourcePath;
        public int Width => width;
        public int Height => height;
        public int Columns => columns;
        public int Rows => rows;
        public int InsetPixels => insetPixels;
        public bool SRgb => sRgb;
        public bool Mipmaps => mipmaps;
        public bool Uncompressed => uncompressed;
        public TextureWrapMode WrapMode => wrapMode;
        public FilterMode FilterMode => filterMode;
        public IReadOnlyList<MothersHouseInteriorAtlasCell> Cells => cells;

        public bool IsConfigured =>
            texture != null &&
            width > 0 &&
            height > 0 &&
            texture.width == width &&
            texture.height == height &&
            columns > 0 &&
            rows > 0 &&
            insetPixels > 0 &&
            insetPixels * 2 < width / columns &&
            insetPixels * 2 < height / rows &&
            !string.IsNullOrWhiteSpace(resourcePath) &&
            HasValidCells();

        public bool TryGetCell(
            string sheet,
            out MothersHouseInteriorAtlasCell cell)
        {
            if (cells != null)
            {
                for (int index = 0; index < cells.Length; index++)
                {
                    if (string.Equals(
                            cells[index].Sheet,
                            sheet,
                            StringComparison.Ordinal))
                    {
                        cell = cells[index];
                        return true;
                    }
                }
            }

            cell = default;
            return false;
        }

        public bool TryGetInsetCellBounds(
            string sheet,
            out Rect bounds)
        {
            if (!TryGetCell(sheet, out MothersHouseInteriorAtlasCell cell) ||
                width <= 0 ||
                height <= 0 ||
                columns <= 0 ||
                rows <= 0)
            {
                bounds = default;
                return false;
            }

            float cellWidth = 1f / columns;
            float cellHeight = 1f / rows;
            float insetX = insetPixels / (float)width;
            float insetY = insetPixels / (float)height;
            bounds = Rect.MinMaxRect(
                cell.Column * cellWidth + insetX,
                cell.Row * cellHeight + insetY,
                (cell.Column + 1) * cellWidth - insetX,
                (cell.Row + 1) * cellHeight - insetY);
            return bounds.width > 0f && bounds.height > 0f;
        }

        public bool TryCreateBaseMapTransform(
            string sheet,
            Rect sourceUvBounds,
            out Vector4 textureTransform)
        {
            if (!TryGetInsetCellBounds(sheet, out Rect target) ||
                !IsFinite(sourceUvBounds) ||
                sourceUvBounds.width <= 0.000001f ||
                sourceUvBounds.height <= 0.000001f)
            {
                textureTransform = default;
                return false;
            }

            float scaleX = target.width / sourceUvBounds.width;
            float scaleY = target.height / sourceUvBounds.height;
            textureTransform = new Vector4(
                scaleX,
                scaleY,
                target.xMin - sourceUvBounds.xMin * scaleX,
                target.yMin - sourceUvBounds.yMin * scaleY);
            return IsFinite(textureTransform);
        }

        private bool HasValidCells()
        {
            if (cells == null || cells.Length == 0)
            {
                return false;
            }

            var sheets = new HashSet<string>(StringComparer.Ordinal);
            var coordinates = new HashSet<int>();
            for (int index = 0; index < cells.Length; index++)
            {
                MothersHouseInteriorAtlasCell cell = cells[index];
                if (string.IsNullOrWhiteSpace(cell.Sheet) ||
                    cell.Column < 0 ||
                    cell.Column >= columns ||
                    cell.Row < 0 ||
                    cell.Row >= rows ||
                    !sheets.Add(cell.Sheet) ||
                    !coordinates.Add(cell.Row * columns + cell.Column))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(Rect value)
        {
            return IsFinite(value.xMin) &&
                   IsFinite(value.xMax) &&
                   IsFinite(value.yMin) &&
                   IsFinite(value.yMax);
        }

        private static bool IsFinite(Vector4 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Typed bridge from the deterministic Blender room to runtime plans.
    /// The FBX remains passive: collision, lights, camera and interaction are
    /// plan-owned. Renderers share the project's two material assets and use
    /// property blocks here for their baked-metre UVs and authored tints.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseInteriorAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "MothersHouse/MothersHouseInterior3D";
        public const string PositiveAtlasResourcePath =
            "MothersHouse/Textures/MothersHousePositiveAtlas";
        public const int PositiveAtlasWidth = 1254;
        public const int PositiveAtlasHeight = 1254;
        public const int PositiveAtlasColumns = 4;
        public const int PositiveAtlasRows = 4;
        public const int PositiveAtlasInsetPixels = 2;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Transform modelRoot;
        [SerializeField] private MothersHouseInteriorAnchorBinding[] anchors =
            Array.Empty<MothersHouseInteriorAnchorBinding>();
        [SerializeField] private MothersHouseInteriorPartBinding[] parts =
            Array.Empty<MothersHouseInteriorPartBinding>();
        [SerializeField]
        private MothersHouseInteriorAtlasContract positiveAtlas;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private MothersHouseInteriorDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<MothersHouseInteriorAnchorBinding> Anchors =>
            anchors;
        public IReadOnlyList<MothersHouseInteriorPartBinding> Parts => parts;
        public MothersHouseInteriorAtlasContract PositiveAtlas =>
            positiveAtlas;
        public Bounds LocalBounds => localBounds;
        public MothersHouseInteriorDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool TryGetAnchor(string role, out Transform anchor)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                MothersHouseInteriorAnchorBinding binding = anchors[index];
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

        public bool TryGetPart(
            string sourceName,
            out MothersHouseInteriorPartBinding part)
        {
            for (int index = 0; index < parts.Length; index++)
            {
                MothersHouseInteriorPartBinding candidate = parts[index];
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

        public void Configure(
            Transform configuredModelRoot,
            MothersHouseInteriorAnchorBinding[] configuredAnchors,
            MothersHouseInteriorPartBinding[] configuredParts,
            MothersHouseInteriorAtlasContract configuredPositiveAtlas,
            Bounds configuredLocalBounds,
            MothersHouseInteriorDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            anchors = configuredAnchors ??
                Array.Empty<MothersHouseInteriorAnchorBinding>();
            parts = configuredParts ??
                Array.Empty<MothersHouseInteriorPartBinding>();
            positiveAtlas = configuredPositiveAtlas;
            localBounds = configuredLocalBounds;
            dimensions = configuredDimensions;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        public void ApplyAppearance()
        {
            if (positiveAtlas == null || !positiveAtlas.IsConfigured)
            {
                throw new InvalidOperationException(
                    "The mother's house positive atlas contract is " +
                    "missing or invalid.");
            }

            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < parts.Length; index++)
            {
                MothersHouseInteriorPartBinding part = parts[index];
                if (part == null || part.Renderer == null)
                {
                    continue;
                }

                Renderer renderer = part.Renderer;
                renderer.shadowCastingMode = part.CastsShadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = part.CastsShadows;
                renderer.GetPropertyBlock(properties);
                ApplySheet(
                    properties,
                    part,
                    positiveAtlas.Texture);
                renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private static void ApplySheet(
            MaterialPropertyBlock properties,
            MothersHouseInteriorPartBinding part,
            Texture2D atlas)
        {
            SurfaceProperties surface = ResolveSurfaceProperties(
                part.Sheet);
            properties.SetTexture(BaseMapId, atlas);
            properties.SetVector(
                BaseMapTransformId,
                part.BaseMapTransform);

            if (part.Emissive ||
                string.Equals(part.Sheet, "Fire", StringComparison.Ordinal))
            {
                properties.SetColor(BaseColorId, part.Tint);
                properties.SetColor(ColorId, part.Tint);
                properties.SetColor(EmissionColorId, part.Tint * 1.8f);
                properties.SetFloat(SmoothnessId, surface.Smoothness);
                properties.SetFloat(MetallicId, surface.Metallic);
                return;
            }

            Color cleanTint = Color.white;
            if (string.Equals(part.Sheet, "Glass", StringComparison.Ordinal))
            {
                cleanTint.a = part.Tint.a;
            }

            properties.SetColor(BaseColorId, cleanTint);
            properties.SetColor(ColorId, cleanTint);
            properties.SetFloat(SmoothnessId, surface.Smoothness);
            properties.SetFloat(MetallicId, surface.Metallic);
        }

        private static SurfaceProperties ResolveSurfaceProperties(
            string sheet)
        {
            switch (sheet)
            {
                case "Wallpaper": return new SurfaceProperties(0.05f, 0f);
                case "CeilingPlaster":
                    return new SurfaceProperties(0.04f, 0f);
                case "PlankFloor":
                    return new SurfaceProperties(0.10f, 0f);
                case "DarkWood":
                    return new SurfaceProperties(0.12f, 0f);
                case "Upholstery":
                    return new SurfaceProperties(0.03f, 0f);
                case "BedLinen":
                    return new SurfaceProperties(0.03f, 0f);
                case "Rug":
                    return new SurfaceProperties(0.02f, 0f);
                case "Concrete":
                    return new SurfaceProperties(0.06f, 0f);
                case "Ceramic":
                    return new SurfaceProperties(0.45f, 0.05f);
                case "PaintedMetal":
                    return new SurfaceProperties(0.22f, 0.30f);
                case "Glass":
                    return new SurfaceProperties(0.32f, 0.02f);
                case "Fire":
                    return new SurfaceProperties(0.05f, 0f);
                default:
                    throw new InvalidOperationException(
                        $"The mother's house model declares unknown " +
                        $"surface sheet '{sheet}'.");
            }
        }

        private readonly struct SurfaceProperties
        {
            public SurfaceProperties(float smoothness, float metallic)
            {
                Smoothness = smoothness;
                Metallic = metallic;
            }

            public float Smoothness { get; }
            public float Metallic { get; }
        }

        private void Awake()
        {
            ApplyAppearance();
        }
    }
}
