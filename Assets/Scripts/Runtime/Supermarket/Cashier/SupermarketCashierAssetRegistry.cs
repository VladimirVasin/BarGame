using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum SupermarketCashierNeckMode
    {
        FixedHuman = 0,
        ExtensibleWatcher = 1
    }

    [Serializable]
    public sealed class SupermarketCashierRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string role;
        [SerializeField] private string boneName;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor = Color.white;
        [SerializeField] private bool usesDetailAtlas;

        public SupermarketCashierRendererBinding(
            string configuredRendererName,
            string configuredRole,
            string configuredBoneName,
            string configuredPaletteName,
            Renderer configuredRenderer,
            Color configuredBaseColor,
            bool configuredUsesDetailAtlas = false)
        {
            rendererName = configuredRendererName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            boneName = configuredBoneName ?? string.Empty;
            paletteName = configuredPaletteName ?? string.Empty;
            renderer = configuredRenderer;
            baseColor = configuredBaseColor;
            usesDetailAtlas = configuredUsesDetailAtlas;
        }

        public string RendererName => rendererName;
        public string Role => role;
        public string BoneName => boneName;
        public string PaletteName => paletteName;
        public Renderer Renderer => renderer;
        public Color BaseColor => baseColor;
        public bool UsesDetailAtlas => usesDetailAtlas;
    }

    /// <summary>
    /// Serialized editor-built bindings shared by the active ordinary
    /// cashier and the retained Watcher variant. Both use the same rig and
    /// palette bindings; only the Watcher carries the optional five-pivot
    /// extensible neck chain.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketCashierAssetRegistry : MonoBehaviour
    {
        public const int WatcherNeckSegmentCount = 5;

        // Kept as a source-compatible alias for the preserved Watcher tests
        // and tooling. New code should use WatcherNeckSegmentCount.
        public const int NeckSegmentCount = WatcherNeckSegmentCount;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");

        /// <summary>
        /// The `256 px` greyscale sheet that multiplies under the flat
        /// palette. Null on a build that predates it, and everything
        /// still renders - it only stops being detailed.
        /// </summary>
        [SerializeField] private Texture2D detailAtlas;

        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField]
        private SupermarketCashierRendererBinding[] rendererBindings =
            Array.Empty<SupermarketCashierRendererBinding>();

        [Header("Torso and face")]
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform faceEyeLeft;
        [SerializeField] private Transform faceEyeRight;

        [Header("Limbs")]
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftForearm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform rightForearm;
        [SerializeField] private Transform rightHand;

        [Header("Neck chain")]
        [SerializeField] private SupermarketCashierNeckMode neckMode;
        [SerializeField] private Transform[] neckPivots =
            Array.Empty<Transform>();

        [Header("Source contract")]
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<SupermarketCashierRendererBinding>
            RendererBindings => rendererBindings;

        public Transform Pelvis => pelvis;
        public Transform Spine => spine;
        public Transform Chest => chest;
        public Transform Neck => neck;
        public Transform Head => head;
        public Transform FaceEyeLeft => faceEyeLeft;
        public Transform FaceEyeRight => faceEyeRight;
        public Transform LeftUpperArm => leftUpperArm;
        public Transform LeftForearm => leftForearm;
        public Transform LeftHand => leftHand;
        public Transform RightUpperArm => rightUpperArm;
        public Transform RightForearm => rightForearm;
        public Transform RightHand => rightHand;
        public SupermarketCashierNeckMode NeckMode => neckMode;
        public bool UsesExtensibleNeck =>
            neckMode == SupermarketCashierNeckMode.ExtensibleWatcher;
        public IReadOnlyList<Transform> NeckPivots => neckPivots;

        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public void Configure(
            Animator configuredAnimator,
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            SupermarketCashierRendererBinding[] configuredBindings,
            Transform configuredPelvis,
            Transform configuredSpine,
            Transform configuredChest,
            Transform configuredNeck,
            Transform configuredHead,
            Transform configuredFaceEyeLeft,
            Transform configuredFaceEyeRight,
            Transform configuredLeftUpperArm,
            Transform configuredLeftForearm,
            Transform configuredLeftHand,
            Transform configuredRightUpperArm,
            Transform configuredRightForearm,
            Transform configuredRightHand,
            SupermarketCashierNeckMode configuredNeckMode,
            Transform[] configuredNeckPivots,
            Bounds configuredLocalBounds,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature,
            Texture2D configuredDetailAtlas = null)
        {
            detailAtlas = configuredDetailAtlas;
            animator = configuredAnimator;
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredBindings ??
                Array.Empty<SupermarketCashierRendererBinding>();
            pelvis = configuredPelvis;
            spine = configuredSpine;
            chest = configuredChest;
            neck = configuredNeck;
            head = configuredHead;
            faceEyeLeft = configuredFaceEyeLeft;
            faceEyeRight = configuredFaceEyeRight;
            leftUpperArm = configuredLeftUpperArm;
            leftForearm = configuredLeftForearm;
            leftHand = configuredLeftHand;
            rightUpperArm = configuredRightUpperArm;
            rightForearm = configuredRightForearm;
            rightHand = configuredRightHand;
            neckMode = configuredNeckMode;
            neckPivots = configuredNeckPivots ??
                Array.Empty<Transform>();
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
            ApplyBaseColors();
        }

        public void ApplyBaseColors()
        {
            MaterialPropertyBlock properties =
                new MaterialPropertyBlock();
            for (int index = 0;
                 index < rendererBindings.Length;
                 index++)
            {
                SupermarketCashierRendererBinding binding =
                    rendererBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                binding.Renderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, binding.BaseColor);
                properties.SetColor(LegacyColorId, binding.BaseColor);

                // One shared material serves every authored part, so the atlas
                // rides the property block beside the colour. Parts
                // without a region keep the plain material and sample the
                // reserved white cell at texel (0, 0) if they ever do get
                // a UV, so leaving them off the atlas is safe either way.
                if (binding.UsesDetailAtlas && detailAtlas != null)
                {
                    properties.SetTexture(BaseMapId, detailAtlas);
                    properties.SetTexture(LegacyMapId, detailAtlas);
                }

                binding.Renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void Awake()
        {
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
        }

        private void OnEnable()
        {
            ApplyBaseColors();
        }
    }
}
