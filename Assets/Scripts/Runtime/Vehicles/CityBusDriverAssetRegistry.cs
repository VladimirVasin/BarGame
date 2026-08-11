using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class CityBusDriverRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string role;
        [SerializeField] private string boneName;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor = Color.white;

        public CityBusDriverRendererBinding(
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

    [DisallowMultipleComponent]
    public sealed class CityBusDriverAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "Vehicles/CityBusDriver3D";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private CityBusDriverRendererBinding[] rendererBindings =
            Array.Empty<CityBusDriverRendererBinding>();

        [Header("Torso and face")]
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform spine;
        [SerializeField] private Transform chest;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform head;
        [SerializeField] private Transform faceEyeLeft;
        [SerializeField] private Transform faceEyeRight;

        [Header("Left limbs")]
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform leftForearm;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform leftGripSocket;
        [SerializeField] private Transform leftThigh;
        [SerializeField] private Transform leftShin;
        [SerializeField] private Transform leftFoot;

        [Header("Right limbs")]
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform rightForearm;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform rightGripSocket;
        [SerializeField] private Transform rightThigh;
        [SerializeField] private Transform rightShin;
        [SerializeField] private Transform rightFoot;

        [Header("Source contract")]
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<CityBusDriverRendererBinding> RendererBindings =>
            rendererBindings;

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
        public Transform LeftGripSocket => leftGripSocket;
        public Transform LeftThigh => leftThigh;
        public Transform LeftShin => leftShin;
        public Transform LeftFoot => leftFoot;

        public Transform RightUpperArm => rightUpperArm;
        public Transform RightForearm => rightForearm;
        public Transform RightHand => rightHand;
        public Transform RightGripSocket => rightGripSocket;
        public Transform RightThigh => rightThigh;
        public Transform RightShin => rightShin;
        public Transform RightFoot => rightFoot;

        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public void Configure(
            Animator configuredAnimator,
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            CityBusDriverRendererBinding[] configuredRendererBindings,
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
            Transform configuredLeftGripSocket,
            Transform configuredLeftThigh,
            Transform configuredLeftShin,
            Transform configuredLeftFoot,
            Transform configuredRightUpperArm,
            Transform configuredRightForearm,
            Transform configuredRightHand,
            Transform configuredRightGripSocket,
            Transform configuredRightThigh,
            Transform configuredRightShin,
            Transform configuredRightFoot,
            Bounds configuredLocalBounds,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            animator = configuredAnimator;
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredRendererBindings ??
                Array.Empty<CityBusDriverRendererBinding>();
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
            leftGripSocket = configuredLeftGripSocket;
            leftThigh = configuredLeftThigh;
            leftShin = configuredLeftShin;
            leftFoot = configuredLeftFoot;
            rightUpperArm = configuredRightUpperArm;
            rightForearm = configuredRightForearm;
            rightHand = configuredRightHand;
            rightGripSocket = configuredRightGripSocket;
            rightThigh = configuredRightThigh;
            rightShin = configuredRightShin;
            rightFoot = configuredRightFoot;
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            ApplyBaseColors();
        }

        public void ApplyBaseColors()
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            for (int index = 0; index < rendererBindings.Length; index++)
            {
                CityBusDriverRendererBinding binding =
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
