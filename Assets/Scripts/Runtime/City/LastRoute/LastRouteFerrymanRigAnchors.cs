using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The two things on the staged Ferryman that the runtime has to reach
    /// and the skeleton does not name: the hollow of his open left palm,
    /// where the coin lies between tosses, and the waist his coat hangs
    /// from.
    ///
    /// Both are the fisherman's problem again. Every part of every
    /// pedestrian is a rigidly skinned mesh, so a part's own Transform
    /// never moves - the palm and the hem are drawn by vertices that follow
    /// bones, and there is nothing to parent a coin or a cloth panel to.
    /// Reconstructing either from constants would mean re-deriving the
    /// Blender-to-Unity axis conversion and the prefab's own 180 degree
    /// model flip in gameplay code, and re-deriving it again every time the
    /// art moves.
    ///
    /// So the prefab build measures both once, in the bind pose, off the
    /// actual imported meshes, and parents an empty anchor to the bone that
    /// carries them. This component is asset metadata only: it drives
    /// nothing and, like the fisherman's, adds no behaviour of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanRigAnchors : MonoBehaviour
    {
        public const string CoinRestRendererName = "GEO_Hand.L";
        public const string CoatHemRendererName = "CLO_CoatHem";
        public const string CoinRestAnchorName = "ANCHOR_CoinRest";
        public const string CoatHemAnchorName = "ANCHOR_CoatHem";

        [SerializeField] private CityPedestrianAssetRegistry pedestrianRegistry;
        [SerializeField] private Transform coinRestAnchor;
        [SerializeField] private Transform coatHemAnchor;
        [SerializeField] private Renderer coatHemRenderer;
        [SerializeField] private Vector2 coatHemSize;
        [SerializeField] private float perchPelvisDrop;
        [SerializeField] private AnimationClip dismountClip;

        public CityPedestrianAssetRegistry PedestrianRegistry =>
            pedestrianRegistry;

        /// <summary>The open left palm, on the left hand bone. The coin
        /// lies here whenever it is not in the air.</summary>
        public Transform CoinRestAnchor => coinRestAnchor;

        /// <summary>
        /// The top of the drawn hem stub, on the pelvis bone - where the
        /// cloth skirt hangs from. The stub itself is short on purpose:
        /// the long skirt is a runtime Cloth panel, so it drapes over the
        /// bonnet edge instead of being a rigid slab through it.
        /// </summary>
        public Transform CoatHemAnchor => coatHemAnchor;

        /// <summary>
        /// The hem stub's renderer, so the runtime can hide it once the
        /// cloth that replaces it exists. Without this the two would be
        /// drawn one inside the other.
        /// </summary>
        public Renderer CoatHemRenderer => coatHemRenderer;

        /// <summary>
        /// Width and drop of the hem stub in metres, measured in the bind
        /// pose. The cloth panel is cut to these rather than to constants,
        /// so a coat redrawn in Blender still gets a skirt that fits it.
        /// </summary>
        public Vector2 CoatHemSize => coatHemSize;

        /// <summary>
        /// How far his pelvis bone rides above the lowest drawn point of
        /// the perched pose - his boot sole - measured in Blender off the
        /// posed meshes and carried here.
        ///
        /// The runtime cannot work this out for itself. The model origin
        /// is the sole plane of the BIND pose, standing straight, and the
        /// perch has both knees up on a car bonnet: the feet leave that
        /// plane entirely. Unity will not recompute skinned bounds for a
        /// manually driven PlayableGraph, and the ankle bone is no
        /// substitute because the perch draws one boot back onto its toe.
        /// So the number crosses from the generator that measured it, and
        /// the presentation sets him down by it.
        /// </summary>
        public float PerchPelvisDrop => perchPelvisDrop;

        /// <summary>
        /// His drop off the bonnet - the fifth clip, and the reason this
        /// component carries one at all.
        ///
        /// `CityPedestrianAssetRegistry` has exactly four clip slots (idle,
        /// walk, sit, action) and this design already fills all four with
        /// his wait, his trudge, his driving loop and his board transition.
        /// He is the only pedestrian with a fifth posture, so the fifth clip
        /// rides the component that is already his alone rather than
        /// widening a type every other design shares.
        /// </summary>
        public AnimationClip DismountClip => dismountClip;

        public void Configure(
            CityPedestrianAssetRegistry configuredPedestrianRegistry,
            Transform configuredCoinRestAnchor,
            Transform configuredCoatHemAnchor,
            Renderer configuredCoatHemRenderer,
            Vector2 configuredCoatHemSize,
            float configuredPerchPelvisDrop,
            AnimationClip configuredDismountClip)
        {
            pedestrianRegistry = configuredPedestrianRegistry;
            coinRestAnchor = configuredCoinRestAnchor;
            coatHemAnchor = configuredCoatHemAnchor;
            coatHemRenderer = configuredCoatHemRenderer;
            coatHemSize = configuredCoatHemSize;
            perchPelvisDrop = configuredPerchPelvisDrop;
            dismountClip = configuredDismountClip;
        }
    }
}
