using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// How a model part gets its colour.
    ///
    /// The bar's appearance varies by district and nothing else, so the
    /// generator declares WHERE each tint comes from and the runtime
    /// resolves it. The alternative - a sixty-case switch in C# keyed on
    /// part name - would be the same table written twice, and the two
    /// copies would drift the first time a part was renamed.
    /// </summary>
    [Serializable]
    public struct BarTintSpec
    {
        [SerializeField] private string field;
        [SerializeField] private Color rgb;
        [SerializeField] private float scale;
        [SerializeField] private string lerpField;
        [SerializeField] private Color lerpRgb;
        [SerializeField] private float lerpT;

        public BarTintSpec(
            string configuredField,
            Color configuredRgb,
            float configuredScale,
            string configuredLerpField,
            Color configuredLerpRgb,
            float configuredLerpT)
        {
            field = configuredField ?? string.Empty;
            rgb = configuredRgb;
            scale = configuredScale;
            lerpField = configuredLerpField ?? string.Empty;
            lerpRgb = configuredLerpRgb;
            lerpT = configuredLerpT;
        }

        public string Field => field;
        public string LerpField => lerpField;

        public Color Resolve(BarDistrictIdentity identity)
        {
            Color color = Sample(field, rgb, identity) * scale;
            if (!string.IsNullOrEmpty(lerpField) || lerpT > 0f)
            {
                color = Color.Lerp(
                    color,
                    Sample(lerpField, lerpRgb, identity),
                    lerpT);
            }

            color.a = 1f;
            return color;
        }

        private static Color Sample(
            string name,
            Color fallback,
            BarDistrictIdentity identity)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            switch (name)
            {
                case "CounterWoodTint": return identity.CounterWoodTint;
                case "WallTint": return identity.WallTint;
                case "PendantColor": return identity.PendantColor;
                case "SignAccentColor": return identity.SignAccentColor;
                case "SignGlowColor": return identity.SignGlowColor;
                case "FloorTint": return identity.FloorTint;
                case "CeilingTint": return identity.CeilingTint;
                case "WallPanelTint": return identity.WallPanelTint;
                case "DarkWoodTint": return identity.DarkWoodTint;
                case "WoodTint": return identity.WoodTint;
                case "UpholsteryTint": return identity.UpholsteryTint;
                case "MetalTint": return identity.MetalTint;
                case "GlassTint": return identity.GlassTint;
                default:
                    throw new InvalidOperationException(
                        $"The bar model asks for an unknown district tint " +
                        $"'{name}'.");
            }
        }
    }

    [Serializable]
    public struct BarColliderSpec
    {
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size;

        public BarColliderSpec(Vector3 configuredCenter, Vector3 configuredSize)
        {
            center = configuredCenter;
            size = configuredSize;
        }

        public Vector3 Center => center;
        public Vector3 Size => size;
    }

    [Serializable]
    public sealed class BarPartBinding
    {
        [SerializeField] private string sourceName;
        [SerializeField] private string role;
        [SerializeField] private string group;
        [SerializeField] private string sheet;
        [SerializeField] private bool emissive;
        [SerializeField] private bool castsShadows;
        [SerializeField] private BarTintSpec tint;
        [SerializeField] private BarColliderSpec[] colliders =
            Array.Empty<BarColliderSpec>();
        [SerializeField] private Renderer renderer;

        public BarPartBinding(
            string configuredSourceName,
            string configuredRole,
            string configuredGroup,
            string configuredSheet,
            bool configuredEmissive,
            bool configuredCastsShadows,
            BarTintSpec configuredTint,
            BarColliderSpec[] configuredColliders,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            group = configuredGroup ?? string.Empty;
            sheet = configuredSheet ?? string.Empty;
            emissive = configuredEmissive;
            castsShadows = configuredCastsShadows;
            tint = configuredTint;
            colliders = configuredColliders ?? Array.Empty<BarColliderSpec>();
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public string Group => group;
        public string Sheet => sheet;
        public bool Emissive => emissive;
        public bool CastsShadows => castsShadows;
        public BarTintSpec Tint => tint;
        public IReadOnlyList<BarColliderSpec> Colliders => colliders;
        public Renderer Renderer => renderer;
    }

    [Serializable]
    public struct BarRoomDimensions
    {
        [SerializeField] private float width;
        [SerializeField] private float depth;
        [SerializeField] private float height;
        [SerializeField] private float wallThickness;
        [SerializeField] private float doorWidth;
        [SerializeField] private float doorHeight;

        public BarRoomDimensions(
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
    public sealed class BarAnchorBinding
    {
        [SerializeField] private string anchorName;
        [SerializeField] private string role;
        [SerializeField] private Transform anchor;

        public BarAnchorBinding(
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

    /// <summary>
    /// Serialized semantic bridge from the Blender bar model to
    /// `BarInteriorWorldBuilder`.
    ///
    /// The model holds geometry, named anchors, and a declaration per
    /// part of how it should be coloured and where it collides. It holds
    /// no colliders, lights or cameras of its own - `BarAssetSetup`
    /// refuses to build a prefab that does - because those are the
    /// layout plan's, and a duplicate arriving inside an FBX would fight
    /// the plan silently rather than fail.
    ///
    /// Parts are a flat LIST here rather than one field apiece, unlike
    /// `ChurchAssetRegistry`. The church has a closed set of five
    /// anchors; this room has 156 parts across twelve groups, four of
    /// which are mutually exclusive activity sets and four mutually
    /// exclusive district sets.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarAssetRegistry : MonoBehaviour
    {
        public const string InteriorPrefabResourcePath =
            "Bar/BarInterior3D";
        public const string FacadePrefabResourcePath =
            "Bar/BarFacade3D";

        public const string DistrictGroupPrefix = "district:";
        public const string ActivityGroupPrefix = "activity:";
        public const string PivotGroupPrefix = "pivot:";
        public const string PrefabGroupPrefix = "prefab:";

        [SerializeField] private Transform modelRoot;
        [SerializeField] private BarAnchorBinding[] anchors =
            Array.Empty<BarAnchorBinding>();
        [SerializeField] private BarPartBinding[] parts =
            Array.Empty<BarPartBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private BarRoomDimensions dimensions;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<BarAnchorBinding> Anchors => anchors;
        public IReadOnlyList<BarPartBinding> Parts => parts;
        public Bounds LocalBounds => localBounds;
        public BarRoomDimensions Dimensions => dimensions;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool TryGetAnchor(string role, out Transform anchor)
        {
            for (int index = 0; index < anchors.Length; index++)
            {
                BarAnchorBinding binding = anchors[index];
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
            BarAnchorBinding[] configuredAnchors,
            BarPartBinding[] configuredParts,
            Bounds configuredLocalBounds,
            BarRoomDimensions configuredDimensions,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            anchors = configuredAnchors ?? Array.Empty<BarAnchorBinding>();
            parts = configuredParts ?? Array.Empty<BarPartBinding>();
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
