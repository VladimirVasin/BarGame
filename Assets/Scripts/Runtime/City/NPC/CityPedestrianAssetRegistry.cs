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
        [SerializeField] private bool usesDetailAtlas;

        public CityPedestrianRendererBinding(
            string configuredRendererName,
            string configuredRole,
            string configuredPaletteName,
            Renderer configuredRenderer,
            Color configuredBaseColor,
            Color configuredVariantOneColor,
            Color configuredVariantTwoColor,
            Color configuredVariantThreeColor,
            bool usesDetailAtlas = false)
        {
            rendererName = configuredRendererName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            paletteName = configuredPaletteName ?? string.Empty;
            renderer = configuredRenderer;
            baseColor = configuredBaseColor;
            variantOneColor = configuredVariantOneColor;
            variantTwoColor = configuredVariantTwoColor;
            variantThreeColor = configuredVariantThreeColor;
            this.usesDetailAtlas = usesDetailAtlas;
        }

        public string RendererName => rendererName;
        public string Role => role;
        public string PaletteName => paletteName;
        public Renderer Renderer => renderer;
        public Color BaseColor => baseColor;
        public Color VariantOneColor => variantOneColor;
        public Color VariantTwoColor => variantTwoColor;
        public Color VariantThreeColor => variantThreeColor;

        /// <summary>
        /// True for a part whose UVs are authored into the registry's
        /// detail atlas. The atlas is light greys multiplied by the palette
        /// tint, so the four variants keep sharing one texture and one
        /// material; a part outside the atlas samples nothing and stays
        /// flat colour.
        /// </summary>
        public bool UsesDetailAtlas => usesDetailAtlas;

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
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");

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
        /// <summary>
        /// The street gait, for a design that also has a placed role.
        ///
        /// The babushka's `walkClip` is `BabushkaBeat` - a stationary carpet
        /// beating her yard presentation plays deliberately - so a promoted
        /// resident cannot simply have its walk slot rewritten. The roaming
        /// pool reads this pair when it is set; every staged presentation
        /// goes on reading `idleClip` / `walkClip` untouched.
        /// </summary>
        [SerializeField] private AnimationClip ambientIdleClip;

        [SerializeField] private AnimationClip ambientWalkClip;
        [SerializeField] private AnimationClip sitClip;
        [SerializeField] private AnimationClip actionClip;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion;
        [SerializeField] private string designId;
        [SerializeField] private string buildSignature;
        [SerializeField] private int paletteVariant;
        [SerializeField] private Light headLamp;
        [SerializeField] private bool preservesAirborneMotion;
        [SerializeField] private Texture2D detailAtlas;

        /// <summary>
        /// The hero's expression grid, on the designs that carry one. Null
        /// on every ambient walker; see <see cref="ConfigureFaceAtlas"/>.
        /// </summary>
        [SerializeField] private Player3DFaceAtlasBinding faceAtlas;

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
        public AnimationClip AmbientIdleClip => ambientIdleClip;
        public AnimationClip AmbientWalkClip => ambientWalkClip;

        /// <summary>The pair the roaming pool should actually play.</summary>
        public AnimationClip RoamingIdleClip =>
            ambientIdleClip != null ? ambientIdleClip : idleClip;

        public AnimationClip RoamingWalkClip =>
            ambientWalkClip != null ? ambientWalkClip : walkClip;

        public AnimationClip SitClip => sitClip;

        /// <summary>
        /// The design's one authored non-locomotion beat — the shout at
        /// the chess set — or <c>null</c> for a design that has nothing
        /// to say. It is a slot rather than a list because no design has
        /// ever needed two; a second one earns the list.
        /// </summary>
        public AnimationClip ActionClip => actionClip;
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

        /// <summary>
        /// The design's detail atlas, or <c>null</c> for a flat-colour
        /// design. It reaches the renderers through the same property block
        /// as the palette tint, never through a material of its own: the
        /// whole pedestrian library is validated to share one material.
        /// </summary>
        public Texture2D DetailAtlas => detailAtlas;

        public Player3DFaceAtlasBinding FaceAtlas => faceAtlas;

        /// <summary>
        /// Whether this design can change expression at all. All-or-nothing
        /// on purpose: the binding refuses to report itself configured
        /// unless every one of the five canonical cells resolves, so a
        /// half-authored atlas reads as no atlas instead of as a face with
        /// holes in it.
        /// </summary>
        public bool HasFaceAtlas => faceAtlas != null && faceAtlas.IsConfigured;

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
            AnimationClip configuredSitClip = null,
            AnimationClip configuredActionClip = null,
            AnimationClip configuredAmbientIdleClip = null,
            AnimationClip configuredAmbientWalkClip = null)
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
            actionClip = configuredActionClip;
            ambientIdleClip = configuredAmbientIdleClip;
            ambientWalkClip = configuredAmbientWalkClip;
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion = configuredSourceGeneratorVersion ??
                string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
            ApplyPaletteVariant(0);
        }

        public void ConfigureDetailAtlas(Texture2D atlas)
        {
            detailAtlas = atlas;
            ApplyPaletteVariant(paletteVariant);
        }

        /// <summary>
        /// Hands this design the hero's runtime-switched facial atlas.
        ///
        /// A DIFFERENT thing from <see cref="ConfigureDetailAtlas"/>, and the
        /// two must not be confused. A detail atlas is a grey multiply mask
        /// whose UV is baked into a sub-rect, so the face it carries is one
        /// drawing forever; that is what every other NPC in the game wears.
        /// This is the full-colour 4x4 expression grid `_BaseMap_ST` selects
        /// a cell of at runtime, and it is the whole difference between a
        /// painted face and a face that can change.
        ///
        /// Optional by construction: a design that never calls this keeps a
        /// null binding, and `Player3DFaceAtlasPresenter.Apply` simply
        /// returns false for it.
        /// </summary>
        public void ConfigureFaceAtlas(Player3DFaceAtlasBinding binding)
        {
            faceAtlas = binding;
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
                // No _BaseMap_ST: the UVs are authored straight into the
                // atlas sub-rectangles, so the texture is bound whole.
                if (binding.UsesDetailAtlas && detailAtlas != null)
                {
                    properties.SetTexture(BaseMapId, detailAtlas);
                    properties.SetTexture(LegacyMapId, detailAtlas);
                }

                target.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void Awake()
        {
            NpcSkinnedMeshCullingGuard.EnableDynamicBounds(modelRoot);
        }

        private void OnEnable()
        {
            ApplyPaletteVariant(paletteVariant);
        }
    }
}
