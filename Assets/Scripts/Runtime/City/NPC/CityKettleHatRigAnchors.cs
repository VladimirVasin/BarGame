using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The two things on the Kettle Hat walker that the runtime has to
    /// reach and the shared 31-bone skeleton does not name: a pivot the
    /// lid and its knob are skinned to, and the mouth of the spout.
    ///
    /// Every kettle part is rigidly skinned to the head bone, so there
    /// is no lid transform to rotate and nothing to parent steam to, and
    /// the rig is locked at thirty-one bones so a lid bone cannot be
    /// added. The prefab build therefore creates an empty under the head
    /// with an identical local frame, repoints the lid's and knob's bone
    /// references at it, and measures - in the bind pose, off the actual
    /// imported meshes - the lid's centre, the kettle's axis and the two
    /// tilt axes across it, all stored head-local so the Blender axis
    /// conversion and the prefab's own 180 degree model flip are never
    /// re-derived in gameplay code.
    ///
    /// The numbers live in the head bone's own units: under Unity's 100x
    /// FBX root one metre is 0.01 of them, which is why the runtime
    /// converts every metric lift through InverseTransformVector rather
    /// than adding a constant to a localPosition.
    ///
    /// This component is asset metadata only. It drives nothing;
    /// <see cref="ResetLid"/> exists so the factory and the effect can put
    /// the pivot back where the bind pose had it without knowing how the
    /// effect moved it, and it is the one thing here that writes a
    /// transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityKettleHatRigAnchors : MonoBehaviour
    {
        public const string LidRendererName = "ACC_KettleLid";
        public const string KnobRendererName = "ACC_KettleKnob";
        public const string KettleBodyRendererName = "ACC_KettleBody";
        public const string SpoutRendererName = "ACC_KettleSpout";
        public const string SpoutTipRendererName = "ACC_KettleSpoutTip";
        public const string LidAnchorName = "ANCHOR_KettleLid";
        public const string SpoutAnchorName = "ANCHOR_KettleSpout";

        [SerializeField] private CityPedestrianAssetRegistry pedestrianRegistry;
        [SerializeField] private Transform lidPivot;
        [SerializeField] private Transform spoutAnchor;
        [SerializeField] private Renderer lidRenderer;
        [SerializeField] private Renderer knobRenderer;
        [SerializeField] private Vector3 lidCentreLocal;
        [SerializeField] private Vector3 kettleAxisLocal = Vector3.up;
        [SerializeField] private Vector3 lidTiltAxisALocal = Vector3.right;
        [SerializeField] private Vector3 lidTiltAxisBLocal = Vector3.forward;
        [SerializeField] private float spoutReachMetres;

        public CityPedestrianAssetRegistry PedestrianRegistry =>
            pedestrianRegistry;

        /// <summary>The empty under the head bone that the lid and knob
        /// are skinned to. Identity in the bind pose.</summary>
        public Transform LidPivot => lidPivot;

        /// <summary>The mouth of the spout, on the head bone, with its
        /// forward along the spout.</summary>
        public Transform SpoutAnchor => spoutAnchor;

        public Renderer LidRenderer => lidRenderer;
        public Renderer KnobRenderer => knobRenderer;

        /// <summary>Centre of the lid in head-local units.</summary>
        public Vector3 LidCentreLocal => lidCentreLocal;

        /// <summary>Unit axis from the kettle body up through the lid,
        /// head-local.</summary>
        public Vector3 KettleAxisLocal => kettleAxisLocal;

        /// <summary>Unit axis across the spout line, head-local.</summary>
        public Vector3 LidTiltAxisALocal => lidTiltAxisALocal;

        /// <summary>Unit axis along the spout line, head-local.</summary>
        public Vector3 LidTiltAxisBLocal => lidTiltAxisBLocal;

        /// <summary>Distance from the head bone to the spout mouth, in
        /// metres, for validation.</summary>
        public float SpoutReachMetres => spoutReachMetres;

        public void Configure(
            CityPedestrianAssetRegistry registry,
            Transform configuredLidPivot,
            Transform configuredSpoutAnchor,
            Renderer configuredLidRenderer,
            Renderer configuredKnobRenderer,
            Vector3 configuredLidCentreLocal,
            Vector3 configuredKettleAxisLocal,
            Vector3 configuredLidTiltAxisALocal,
            Vector3 configuredLidTiltAxisBLocal,
            float configuredSpoutReachMetres)
        {
            pedestrianRegistry = registry;
            lidPivot = configuredLidPivot;
            spoutAnchor = configuredSpoutAnchor;
            lidRenderer = configuredLidRenderer;
            knobRenderer = configuredKnobRenderer;
            lidCentreLocal = configuredLidCentreLocal;
            kettleAxisLocal = configuredKettleAxisLocal;
            lidTiltAxisALocal = configuredLidTiltAxisALocal;
            lidTiltAxisBLocal = configuredLidTiltAxisBLocal;
            spoutReachMetres = configuredSpoutReachMetres;
        }

        /// <summary>
        /// Puts the pivot back on the head bone's own frame, which is where
        /// the bind pose drew the lid. Null-safe: a prefab that never got
        /// its pivot is simply left alone.
        /// </summary>
        public void ResetLid()
        {
            if (lidPivot == null)
            {
                return;
            }

            lidPivot.localPosition = Vector3.zero;
            lidPivot.localRotation = Quaternion.identity;
            lidPivot.localScale = Vector3.one;
        }
    }
}
