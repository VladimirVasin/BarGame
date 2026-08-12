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
        [SerializeField] private Transform pelvisAnchor;
        [SerializeField] private Transform leftFootAnchor;
        [SerializeField] private Transform rightFootAnchor;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip sitClip;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;
        [SerializeField] private int paletteVariant;
        [SerializeField] private Light headLamp;
        [SerializeField] private bool preservesAirborneMotion;

        public Animator Animator => animator;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<Renderer> Renderers => renderers;
        public IReadOnlyList<CityPedestrianRendererBinding> RendererBindings =>
            rendererBindings;
        public Transform HeadAnchor => headAnchor;
        public Transform Head => headAnchor;

        /// <summary>
        /// The rest pelvis every design shares at `0.70 m`. Seating aligns
        /// this bone to the cushion anchor instead of pinning the lowest
        /// sole, which is what lets one seat rule serve five proportions.
        /// </summary>
        public Transform PelvisAnchor => pelvisAnchor;
        public Transform Pelvis => pelvisAnchor;
        public Transform LeftFootAnchor => leftFootAnchor;
        public Transform LeftFoot => leftFootAnchor;
        public Transform RightFootAnchor => rightFootAnchor;
        public Transform RightFoot => rightFootAnchor;
        public AnimationClip IdleClip => idleClip;
        public AnimationClip WalkClip => walkClip;

        /// <summary>
        /// The design's authored seated loop, or <c>null</c> for a design that
        /// declares no seated ride.
        /// </summary>
        public AnimationClip SitClip => sitClip;
        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;
        public int PaletteVariant => paletteVariant;

        /// <summary>
        /// The single shadowless Spot a design may carry as worn equipment,
        /// or <c>null</c> for every ordinary walker.
        /// </summary>
        public Light HeadLamp => headLamp;

        /// <summary>
        /// True when the design's clips deliberately leave the pavement. The
        /// presentation must then stop pinning the lowest sole every frame,
        /// which would otherwise flatten the authored arc.
        /// </summary>
        public bool PreservesAirborneMotion => preservesAirborneMotion;

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
            string configuredBuildSignature,
            Light configuredHeadLamp = null,
            bool configuredPreservesAirborneMotion = false,
            Transform configuredPelvisAnchor = null,
            AnimationClip configuredSitClip = null)
        {
            headLamp = configuredHeadLamp;
            preservesAirborneMotion = configuredPreservesAirborneMotion;
            animator = configuredAnimator;
            modelRoot = configuredModelRoot;
            renderers = configuredRenderers ?? Array.Empty<Renderer>();
            rendererBindings = configuredRendererBindings ??
                Array.Empty<CityPedestrianRendererBinding>();
            headAnchor = configuredHeadAnchor;
            pelvisAnchor = configuredPelvisAnchor;
            leftFootAnchor = configuredLeftFootAnchor;
            rightFootAnchor = configuredRightFootAnchor;
            idleClip = configuredIdleClip;
            walkClip = configuredWalkClip;
            sitClip = configuredSitClip;
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
