using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [Serializable]
    public sealed class CityPedestrianRendererBinding
    {
        [SerializeField] private string rendererName;
        [SerializeField] private string role;
        [SerializeField] private string paletteName;
        [SerializeField] private Renderer renderer;
        [SerializeField] private Color baseColor = Color.white;
        [SerializeField] private Color variantOneColor = Color.white;
        [SerializeField] private Color variantTwoColor = Color.white;
        [SerializeField] private Color variantThreeColor = Color.white;

        public CityPedestrianRendererBinding(
            string configuredRendererName,
            string configuredRole,
            string configuredPaletteName,
            Renderer configuredRenderer,
            Color configuredBaseColor,
            Color configuredVariantOneColor,
            Color configuredVariantTwoColor,
            Color configuredVariantThreeColor)
        {
            rendererName = configuredRendererName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            paletteName = configuredPaletteName ?? string.Empty;
            renderer = configuredRenderer;
            baseColor = configuredBaseColor;
            variantOneColor = configuredVariantOneColor;
            variantTwoColor = configuredVariantTwoColor;
            variantThreeColor = configuredVariantThreeColor;
        }

        public string RendererName => rendererName;
        public string Role => role;
        public string PaletteName => paletteName;
        public Renderer Renderer => renderer;
        public Color BaseColor => baseColor;
        public Color VariantOneColor => variantOneColor;
        public Color VariantTwoColor => variantTwoColor;
        public Color VariantThreeColor => variantThreeColor;

        public Color GetColor(int paletteVariant)
        {
            switch (NormalizeVariant(paletteVariant))
            {
                case 1:
                    return variantOneColor;
                case 2:
                    return variantTwoColor;
                case 3:
                    return variantThreeColor;
                default:
                    return baseColor;
            }
        }

        private static int NormalizeVariant(int paletteVariant)
        {
            int normalized = paletteVariant % 4;
            return normalized < 0 ? normalized + 4 : normalized;
        }
    }

    [DisallowMultipleComponent]
    public sealed class CityPedestrianAssetRegistry : MonoBehaviour
    {
        public const string PrefabResourcePath =
            "Pedestrians/CityPedestrian3D";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private Renderer[] renderers =
            Array.Empty<Renderer>();
        [SerializeField] private CityPedestrianRendererBinding[] rendererBindings =
            Array.Empty<CityPedestrianRendererBinding>();
        [SerializeField] private Transform headAnchor;
        [SerializeField] private Transform leftFootAnchor;
        [SerializeField] private Transform rightFootAnchor;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;
        [SerializeField] private int paletteVariant;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<CityPedestrianRendererBinding> RendererBindings =>
            rendererBindings;
        public Transform HeadAnchor => headAnchor;
        public Transform Head => headAnchor;
        public Transform LeftFootAnchor => leftFootAnchor;
        public Transform LeftFoot => leftFootAnchor;
        public Transform RightFootAnchor => rightFootAnchor;
        public Transform RightFoot => rightFootAnchor;
        public AnimationClip IdleClip => idleClip;
        public AnimationClip WalkClip => walkClip;
        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;
        public int PaletteVariant => paletteVariant;

        public static GameObject LoadPrefab()
        {
            return Resources.Load<GameObject>(PrefabResourcePath);
        }

        public void Configure(
            Animator configuredAnimator,
            Transform configuredModelRoot,
            Renderer[] configuredRenderers,
            CityPedestrianRendererBinding[] configuredRendererBindings,
            Transform configuredHeadAnchor,
            Transform configuredLeftFootAnchor,
            Transform configuredRightFootAnchor,
            AnimationClip configuredIdleClip,
            AnimationClip configuredWalkClip,
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
                Array.Empty<CityPedestrianRendererBinding>();
            headAnchor = configuredHeadAnchor;
            leftFootAnchor = configuredLeftFootAnchor;
            rightFootAnchor = configuredRightFootAnchor;
            idleClip = configuredIdleClip;
            walkClip = configuredWalkClip;
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            ApplyPaletteVariant(0);
        }

        public void ApplyPaletteVariant(int variant)
        {
            int normalized = variant % 4;
            paletteVariant = normalized < 0 ? normalized + 4 : normalized;

            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            for (int index = 0; index < rendererBindings.Length; index++)
            {
                CityPedestrianRendererBinding binding =
                    rendererBindings[index];
                if (binding == null || binding.Renderer == null)
                {
                    continue;
                }

                Renderer target = binding.Renderer;
                target.GetPropertyBlock(properties);
                Color color = binding.GetColor(paletteVariant);
                properties.SetColor(BaseColorId, color);
                properties.SetColor(LegacyColorId, color);
                target.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void OnEnable()
        {
            ApplyPaletteVariant(paletteVariant);
        }
    }
}
