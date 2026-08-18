using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The two points on the staged fisherman that the runtime has to
    /// reach and the skeleton does not name: the top of his pipe bowl
    /// and the far end of his rod.
    ///
    /// Both are real problems rather than conveniences. Every part of
    /// every pedestrian is a rigidly skinned mesh, so a part's own
    /// Transform never moves — the pipe bowl and the rod tip are drawn
    /// by vertices that follow bones, and there is nothing to parent an
    /// ember or a fishing line to. Reconstructing either from constants
    /// would mean re-deriving the Blender-to-Unity axis conversion and
    /// the prefab's own 180 degree model flip in gameplay code, twice,
    /// and re-deriving it again every time the art moves.
    ///
    /// So the prefab build measures both once, in the bind pose, off
    /// the actual imported meshes, and parents an empty anchor to the
    /// bone that carries them. This component is asset metadata only:
    /// it drives nothing and, like the wheelchair registry, adds no
    /// behaviour of its own.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LakeFishermanRigAnchors : MonoBehaviour
    {
        public const string PipeEmberRendererName = "ACC_PipeEmber";
        public const string RodTipRendererName = "ACC_RodTip";
        public const string PipeEmberAnchorName = "ANCHOR_PipeEmber";
        public const string RodTipAnchorName = "ANCHOR_RodTip";

        [SerializeField] private CityPedestrianAssetRegistry pedestrianRegistry;
        [SerializeField] private Transform pipeEmberAnchor;
        [SerializeField] private Transform rodTipAnchor;
        [SerializeField] private Renderer pipeEmberRenderer;

        public CityPedestrianAssetRegistry PedestrianRegistry =>
            pedestrianRegistry;

        /// <summary>Top of the pipe bowl, on the head bone.</summary>
        public Transform PipeEmberAnchor => pipeEmberAnchor;

        /// <summary>Far end of the rod, on the right hand bone.</summary>
        public Transform RodTipAnchor => rodTipAnchor;

        /// <summary>The ember disc itself, so the runtime can light it
        /// without searching the hierarchy by name.</summary>
        public Renderer PipeEmberRenderer => pipeEmberRenderer;

        public void Configure(
            CityPedestrianAssetRegistry configuredPedestrianRegistry,
            Transform configuredPipeEmberAnchor,
            Transform configuredRodTipAnchor,
            Renderer configuredPipeEmberRenderer)
        {
            pedestrianRegistry = configuredPedestrianRegistry;
            pipeEmberAnchor = configuredPipeEmberAnchor;
            rodTipAnchor = configuredRodTipAnchor;
            pipeEmberRenderer = configuredPipeEmberRenderer;
        }
    }
}
