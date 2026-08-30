using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One serialized renderer of the raven prefab with the pivot the
    /// runtime adopts it under, its deterministic manifest color and
    /// whether its UVs are authored into the detail atlas.
    /// </summary>
    [Serializable]
    public sealed class CemeteryRavenRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string pivotName;
        [SerializeField] private string role;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor;
        [SerializeField] private bool usesDetailAtlas;

        public CemeteryRavenRendererBinding(
            string configuredRendererName,
            string configuredPivotName,
            string configuredRole,
            string configuredPaletteName,
            Renderer configuredRenderer,
            Color configuredBaseColor,
            bool configuredUsesDetailAtlas)
        {
            rendererName = configuredRendererName ?? string.Empty;
            pivotName = configuredPivotName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            paletteName = configuredPaletteName ?? string.Empty;
            renderer = configuredRenderer;
            baseColor = configuredBaseColor;
            usesDetailAtlas = configuredUsesDetailAtlas;
        }

        public string RendererName => rendererName;
        public string PivotName => pivotName;
        public string Role => role;
        public string PaletteName => paletteName;
        public Renderer Renderer => renderer;
        public Color BaseColor => baseColor;

        /// <summary>
        /// True for a part whose UVs are authored into the raven's
        /// detail atlas. The atlas is darkening greys multiplied under
        /// the flat palette, so binding it per renderer keeps the whole
        /// bird on one shared material; a part outside the atlas (the
        /// eyes) samples the reserved white cell and stays flat colour.
        /// </summary>
        public bool UsesDetailAtlas => usesDetailAtlas;
    }

    /// <summary>
    /// Serialized editor-built bindings of the cemetery raven prefab:
    /// the pivot empties the procedural pose drives, the feet-contact
    /// anchor, the per-renderer manifest colors and the detail atlas.
    /// Asset metadata only — it drives nothing itself; the actor
    /// adopts the meshes under the pivots and articulates them (the
    /// stairwell cat's wheelchair-mechanism pattern).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryRavenRigAnchors : MonoBehaviour
    {
        public const string BodyRootPivotName = "PIVOT_BodyRoot";
        public const string HeadPivotName = "PIVOT_Head";
        public const string WingLeftPivotName = "PIVOT_Wing.L";
        public const string WingRightPivotName = "PIVOT_Wing.R";
        public const string TailPivotName = "PIVOT_Tail";
        public const string FeetContactAnchorName =
            "ANCHOR_FeetContact";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");

        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField]
        private CemeteryRavenRendererBinding[] rendererBindings =
            Array.Empty<CemeteryRavenRendererBinding>();

        [Header("Articulation")]
        [SerializeField] private Transform bodyRootPivot;
        [SerializeField] private Transform headPivot;
        [SerializeField] private Transform wingLeftPivot;
        [SerializeField] private Transform wingRightPivot;
        [SerializeField] private Transform tailPivot;
        [SerializeField] private Transform feetContactAnchor;

        [Header("Presentation")]
        [SerializeField] private Texture2D detailAtlas;

        [Header("Source contract")]
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<CemeteryRavenRendererBinding>
            RendererBindings => rendererBindings;

        public Transform BodyRootPivot => bodyRootPivot;
        public Transform HeadPivot => headPivot;
        public Transform WingLeftPivot => wingLeftPivot;
        public Transform WingRightPivot => wingRightPivot;
        public Transform TailPivot => tailPivot;

        /// <summary>The authored ground/perch contact point at the
        /// model origin — the reason the host root can simply be
        /// placed ON a perch point.</summary>
        public Transform FeetContactAnchor => feetContactAnchor;

        /// <summary>
        /// The bird's detail atlas. It reaches the renderers through
        /// the same property block as the palette tint, never through
        /// a material of its own: the raven shares the one
        /// Player3DLit material with every character in the game.
        /// </summary>
        public Texture2D DetailAtlas => detailAtlas;

        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool IsBound =>
            modelRoot != null &&
            bodyRootPivot != null &&
            headPivot != null &&
            wingLeftPivot != null &&
            wingRightPivot != null &&
            tailPivot != null &&
            feetContactAnchor != null &&
            rendererBindings != null &&
            rendererBindings.Length > 0;

        public void Configure(
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            CemeteryRavenRendererBinding[] configuredBindings,
            Transform configuredBodyRootPivot,
            Transform configuredHeadPivot,
            Transform configuredWingLeftPivot,
            Transform configuredWingRightPivot,
            Transform configuredTailPivot,
            Transform configuredFeetContactAnchor,
            Texture2D configuredDetailAtlas,
            Bounds configuredLocalBounds,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredBindings ??
                Array.Empty<CemeteryRavenRendererBinding>();
            bodyRootPivot = configuredBodyRootPivot;
            headPivot = configuredHeadPivot;
            wingLeftPivot = configuredWingLeftPivot;
            wingRightPivot = configuredWingRightPivot;
            tailPivot = configuredTailPivot;
            feetContactAnchor = configuredFeetContactAnchor;
            detailAtlas = configuredDetailAtlas;
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            ApplyBaseColors();
        }

        /// <summary>
        /// Writes each renderer's manifest color AND the detail atlas
        /// through one MaterialPropertyBlock — the kettle-hat idiom:
        /// the texture rides the block (_BaseMap for URP, _MainTex for
        /// the legacy name) rather than a per-instance material, so
        /// the whole bird keeps sharing Player3DLit. There is no
        /// _BaseMap_ST: the UVs are authored straight into the atlas
        /// sub-rectangles, so the texture is bound whole.
        /// </summary>
        public void ApplyBaseColors()
        {
            MaterialPropertyBlock properties =
                new MaterialPropertyBlock();
            for (int index = 0;
                 index < rendererBindings.Length;
                 index++)
            {
                CemeteryRavenRendererBinding binding =
                    rendererBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                binding.Renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, binding.BaseColor);
                properties.SetColor(LegacyColorId, binding.BaseColor);
                if (binding.UsesDetailAtlas && detailAtlas != null)
                {
                    properties.SetTexture(BaseMapId, detailAtlas);
                    properties.SetTexture(LegacyMapId, detailAtlas);
                }

                binding.Renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void OnEnable()
        {
            ApplyBaseColors();
        }
    }
}
