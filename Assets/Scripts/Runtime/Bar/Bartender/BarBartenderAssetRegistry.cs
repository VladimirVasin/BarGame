using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum BarBartenderClipKind
    {
        Wipe = 0,
        Walk = 1,
        Pour = 2,
        Notice = 3
    }

    [Serializable]
    public sealed class BarBartenderClipBinding
    {
        [SerializeField] private BarBartenderClipKind kind;
        [SerializeField] private AnimationClip clip;
        [SerializeField] private bool loop;

        public BarBartenderClipBinding(
            BarBartenderClipKind configuredKind,
            AnimationClip configuredClip,
            bool configuredLoop)
        {
            kind = configuredKind;
            clip = configuredClip != null
                ? configuredClip
                : throw new ArgumentNullException(
                    nameof(configuredClip));
            loop = configuredLoop;
        }

        public BarBartenderClipKind Kind => kind;
        public AnimationClip Clip => clip;
        public bool Loop => loop;
    }

    [Serializable]
    public sealed class BarBartenderRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string role;
        [SerializeField] private string boneName;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor = Color.white;

        public BarBartenderRendererBinding(
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
    /// One rigid extra-arm chain of the Six-Armed Bartender: four
    /// authored pivot empties from shoulder to grip that procedural
    /// pose code rotates, wheelchair-mechanism style, without bones.
    /// </summary>
    [Serializable]
    public sealed class BarBartenderArmChain
    {
        [SerializeField] private string chainId;
        [SerializeField] private Transform shoulderPivot;
        [SerializeField] private Transform elbowPivot;
        [SerializeField] private Transform wristPivot;
        [SerializeField] private Transform gripPivot;

        public BarBartenderArmChain(
            string configuredChainId,
            Transform configuredShoulderPivot,
            Transform configuredElbowPivot,
            Transform configuredWristPivot,
            Transform configuredGripPivot)
        {
            chainId = configuredChainId ?? string.Empty;
            shoulderPivot = configuredShoulderPivot;
            elbowPivot = configuredElbowPivot;
            wristPivot = configuredWristPivot;
            gripPivot = configuredGripPivot;
        }

        public string ChainId => chainId;
        public Transform ShoulderPivot => shoulderPivot;
        public Transform ElbowPivot => elbowPivot;
        public Transform WristPivot => wristPivot;
        public Transform GripPivot => gripPivot;
    }

    /// <summary>
    /// Serialized editor-built bindings shared by both bartender assets.
    /// The inactive legacy prefab owns four rigid extra-arm chains; the
    /// active ordinary prefab owns the four authored waiter clips and
    /// explicit left-vessel/right-bottle sockets. Both keep the exact
    /// NpcHumanV2 bones and per-renderer manifest colours.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarBartenderAssetRegistry : MonoBehaviour
    {
        public const int ExtraArmChainCount = 4;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField]
        private BarBartenderRendererBinding[] rendererBindings =
            Array.Empty<BarBartenderRendererBinding>();

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

        [Header("Extra arm chains")]
        [SerializeField] private BarBartenderArmChain[] extraArmChains =
            Array.Empty<BarBartenderArmChain>();

        [Header("Ordinary service")]
        [SerializeField] private BarBartenderClipBinding[] clipBindings =
            Array.Empty<BarBartenderClipBinding>();
        [SerializeField] private Transform leftGripSocket;
        [SerializeField] private Transform leftVesselSocket;
        [SerializeField] private Transform rightGripSocket;
        [SerializeField] private Transform rightBottleSocket;
        [SerializeField] private Transform vesselGripAnchor;
        [SerializeField] private Transform bottleGripAnchor;

        [Header("Source contract")]
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<BarBartenderRendererBinding>
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
        public IReadOnlyList<BarBartenderArmChain> ExtraArmChains =>
            extraArmChains;
        public IReadOnlyList<BarBartenderClipBinding> ClipBindings =>
            clipBindings ?? Array.Empty<BarBartenderClipBinding>();
        public Transform LeftGripSocket => leftGripSocket;
        public Transform LeftVesselSocket => leftVesselSocket;
        public Transform RightGripSocket => rightGripSocket;
        public Transform RightBottleSocket => rightBottleSocket;
        public Transform VesselGripAnchor => vesselGripAnchor;
        public Transform BottleGripAnchor => bottleGripAnchor;
        public bool UsesAuthoredServiceClips =>
            clipBindings != null && clipBindings.Length > 0;

        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public void Configure(
            Animator configuredAnimator,
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            BarBartenderRendererBinding[] configuredBindings,
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
            BarBartenderArmChain[] configuredExtraArmChains,
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
                Array.Empty<BarBartenderRendererBinding>();
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
            extraArmChains = configuredExtraArmChains ??
                Array.Empty<BarBartenderArmChain>();
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
            ApplyBaseColors();
        }

        public void ConfigureOrdinaryService(
            BarBartenderClipBinding[] configuredClipBindings,
            Transform configuredLeftGripSocket,
            Transform configuredLeftVesselSocket,
            Transform configuredRightGripSocket,
            Transform configuredRightBottleSocket,
            Transform configuredVesselGripAnchor,
            Transform configuredBottleGripAnchor)
        {
            if (configuredClipBindings == null ||
                configuredClipBindings.Length != 4)
            {
                throw new ArgumentException(
                    "Ordinary bartender service requires exactly four " +
                    "authored clip bindings.",
                    nameof(configuredClipBindings));
            }

            clipBindings = configuredClipBindings;
            leftGripSocket = configuredLeftGripSocket != null
                ? configuredLeftGripSocket
                : throw new ArgumentNullException(
                    nameof(configuredLeftGripSocket));
            leftVesselSocket = configuredLeftVesselSocket != null
                ? configuredLeftVesselSocket
                : throw new ArgumentNullException(
                    nameof(configuredLeftVesselSocket));
            rightGripSocket = configuredRightGripSocket != null
                ? configuredRightGripSocket
                : throw new ArgumentNullException(
                    nameof(configuredRightGripSocket));
            rightBottleSocket = configuredRightBottleSocket != null
                ? configuredRightBottleSocket
                : throw new ArgumentNullException(
                    nameof(configuredRightBottleSocket));
            vesselGripAnchor = configuredVesselGripAnchor != null
                ? configuredVesselGripAnchor
                : throw new ArgumentNullException(
                    nameof(configuredVesselGripAnchor));
            bottleGripAnchor = configuredBottleGripAnchor != null
                ? configuredBottleGripAnchor
                : throw new ArgumentNullException(
                    nameof(configuredBottleGripAnchor));
        }

        public bool TryGetClip(
            BarBartenderClipKind kind,
            out AnimationClip clip,
            out bool loop)
        {
            if (clipBindings != null)
            {
                for (int index = 0;
                     index < clipBindings.Length;
                     index++)
                {
                    BarBartenderClipBinding binding =
                        clipBindings[index];
                    if (binding != null && binding.Kind == kind)
                    {
                        clip = binding.Clip;
                        loop = binding.Loop;
                        return clip != null;
                    }
                }
            }

            clip = null;
            loop = false;
            return false;
        }

        public void SetServiceTowelVisible(bool visible)
        {
            if (rendererBindings == null)
            {
                return;
            }

            for (int index = 0;
                 index < rendererBindings.Length;
                 index++)
            {
                BarBartenderRendererBinding binding =
                    rendererBindings[index];
                if (binding != null &&
                    string.Equals(
                        binding.RendererName,
                        "ACC_ServiceTowel",
                        StringComparison.Ordinal) &&
                    binding.Renderer != null)
                {
                    binding.Renderer.enabled = visible;
                    return;
                }
            }
        }

        public void ApplyBaseColors()
        {
            MaterialPropertyBlock properties =
                new MaterialPropertyBlock();
            for (int index = 0;
                 index < rendererBindings.Length;
                 index++)
            {
                BarBartenderRendererBinding binding =
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

        private void Awake()
        {
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
        }

        private void OnEnable()
        {
            ApplyBaseColors();
            SetServiceTowelVisible(true);
        }
    }
}
