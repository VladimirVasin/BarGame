using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One serialized renderer of the cat prefab with the pivot the
    /// runtime adopts it under and its deterministic manifest color.
    /// </summary>
    [Serializable]
    public sealed class StairwellCatRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string pivotName;
        [SerializeField] private string role;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor;

        public StairwellCatRendererBinding(
            string configuredRendererName,
            string configuredPivotName,
            string configuredRole,
            string configuredPaletteName,
            Renderer configuredRenderer,
            Color configuredBaseColor)
        {
            rendererName = configuredRendererName ?? string.Empty;
            pivotName = configuredPivotName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            paletteName = configuredPaletteName ?? string.Empty;
            renderer = configuredRenderer;
            baseColor = configuredBaseColor;
        }

        public string RendererName => rendererName;
        public string PivotName => pivotName;
        public string Role => role;
        public string PaletteName => paletteName;
        public Renderer Renderer => renderer;
        public Color BaseColor => baseColor;
    }

    /// <summary>
    /// Serialized editor-built bindings of the stairwell cat prefab:
    /// the pivot empties the procedural pose drives, the muzzle
    /// anchor, the grin renderer and the per-renderer manifest
    /// colors. Asset metadata only - it drives nothing itself; the
    /// actor adopts the meshes under the pivots and articulates them
    /// (the wheelchair mechanism pattern).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StairwellCatRigAnchors : MonoBehaviour
    {
        public const string ChestPivotName = "PIVOT_Chest";
        public const string HeadPivotName = "PIVOT_Head";
        public const string EarLeftPivotName = "PIVOT_Ear.L";
        public const string EarRightPivotName = "PIVOT_Ear.R";
        public const string MuzzleAnchorName = "ANCHOR_Muzzle";
        public const string GrinRendererName = "ACC_Grin";
        public const int TailPivotCount = 3;

        public static readonly string[] TailPivotNames =
        {
            "PIVOT_Tail.01",
            "PIVOT_Tail.02",
            "PIVOT_Tail.03"
        };

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField]
        private StairwellCatRendererBinding[] rendererBindings =
            Array.Empty<StairwellCatRendererBinding>();

        [Header("Articulation")]
        [SerializeField] private Transform chestPivot;
        [SerializeField] private Transform headPivot;
        [SerializeField] private Transform earLeftPivot;
        [SerializeField] private Transform earRightPivot;
        [SerializeField] private Transform[] tailPivots =
            Array.Empty<Transform>();
        [SerializeField] private Transform muzzleAnchor;

        [Header("Presentation")]
        [SerializeField] private Renderer grinRenderer;
        [SerializeField] private Renderer bodyRenderer;

        [Header("Source contract")]
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<StairwellCatRendererBinding>
            RendererBindings => rendererBindings;

        public Transform ChestPivot => chestPivot;
        public Transform HeadPivot => headPivot;
        public Transform EarLeftPivot => earLeftPivot;
        public Transform EarRightPivot => earRightPivot;
        public IReadOnlyList<Transform> TailPivots => tailPivots;
        public Transform MuzzleAnchor => muzzleAnchor;

        public Renderer GrinRenderer => grinRenderer;

        /// <summary>The haunches renderer: the widest, most central
        /// body piece, used for visibility bounds checks.</summary>
        public Renderer BodyRenderer => bodyRenderer;

        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public bool IsBound =>
            modelRoot != null &&
            chestPivot != null &&
            headPivot != null &&
            earLeftPivot != null &&
            earRightPivot != null &&
            muzzleAnchor != null &&
            grinRenderer != null &&
            bodyRenderer != null &&
            tailPivots != null &&
            tailPivots.Length == TailPivotCount &&
            tailPivots[0] != null &&
            tailPivots[1] != null &&
            tailPivots[2] != null;

        public void Configure(
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            StairwellCatRendererBinding[] configuredBindings,
            Transform configuredChestPivot,
            Transform configuredHeadPivot,
            Transform configuredEarLeftPivot,
            Transform configuredEarRightPivot,
            Transform[] configuredTailPivots,
            Transform configuredMuzzleAnchor,
            Renderer configuredGrinRenderer,
            Renderer configuredBodyRenderer,
            Bounds configuredLocalBounds,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredBindings ??
                Array.Empty<StairwellCatRendererBinding>();
            chestPivot = configuredChestPivot;
            headPivot = configuredHeadPivot;
            earLeftPivot = configuredEarLeftPivot;
            earRightPivot = configuredEarRightPivot;
            tailPivots = configuredTailPivots ??
                Array.Empty<Transform>();
            muzzleAnchor = configuredMuzzleAnchor;
            grinRenderer = configuredGrinRenderer;
            bodyRenderer = configuredBodyRenderer;
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            ApplyBaseColors();
        }

        /// <summary>
        /// Writes each body renderer's manifest color through a
        /// MaterialPropertyBlock. The grin renderer is skipped: its
        /// dedicated material owns its enamel color and its
        /// _GrinProgress belongs to the grin controller.
        /// </summary>
        public void ApplyBaseColors()
        {
            MaterialPropertyBlock properties =
                new MaterialPropertyBlock();
            for (int index = 0;
                 index < rendererBindings.Length;
                 index++)
            {
                StairwellCatRendererBinding binding =
                    rendererBindings[index];
                if (binding == null ||
                    binding.Renderer == null ||
                    binding.Renderer == grinRenderer)
                {
                    continue;
                }

                binding.Renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, binding.BaseColor);
                properties.SetColor(LegacyColorId, binding.BaseColor);
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
