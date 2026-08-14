using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class SupermarketCashierRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string role;
        [SerializeField] private string boneName;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor = Color.white;

        public SupermarketCashierRendererBinding(
            string configuredRendererName,
            string configuredRole,
            string configuredBoneName,
            string configuredPaletteName,
            Renderer configuredRenderer,
            Color configuredBaseColor)
        {
            rendererName = configuredRendererName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            boneName = configuredBoneName ?? string.Empty;
            paletteName = configuredPaletteName ?? string.Empty;
            renderer = configuredRenderer;
            baseColor = configuredBaseColor;
        }

        public string RendererName => rendererName;
        public string Role => role;
        public string BoneName => boneName;
        public string PaletteName => paletteName;
        public Renderer Renderer => renderer;
        public Color BaseColor => baseColor;
    }

    /// <summary>
    /// Serialized editor-built bindings of the Watcher Cashier prefab:
    /// the exact bones the procedural pose needs, the five neck chain
    /// pivots and the per-renderer manifest colors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketCashierAssetRegistry : MonoBehaviour
    {
        public const int NeckSegmentCount = 5;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

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
            Transform[] configuredNeckPivots,
            Bounds configuredLocalBounds,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
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
            neckPivots = configuredNeckPivots ??
                Array.Empty<Transform>();
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
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
