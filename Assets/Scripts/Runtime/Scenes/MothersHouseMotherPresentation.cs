using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the mother: one looping breath, her hips on the cushion, and
    /// one facial cell that nothing ever changes.
    ///
    /// The graph is the shape every ambient character in this game uses -
    /// a manually evaluated <see cref="PlayableGraph"/> stepped in
    /// `LateUpdate`, never an Animator Controller - and it holds a single
    /// clip because she has a single posture.
    ///
    /// SHE DOES NOT ROCK HERSELF. `MothersHouseRockingChairMotion` places
    /// her root and the chair's meshes from one angle, so the woman and the
    /// timber can never disagree. Nothing here reads or reproduces it - this
    /// component only runs AFTER it (execution order 310 against 300), so
    /// the hips are set on a cushion that has already moved this frame
    /// rather than on last frame's.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(310)]
    public sealed class MothersHouseMotherPresentation : MonoBehaviour
    {
        /// <summary>
        /// The distance from her seated hips' underside up to the pelvis
        /// BONE, measured by the generator against the real deformed
        /// meshes and printed as `pelvis lift`. It is not a guess and it is
        /// not derivable at runtime: Unity will not recompute skinned
        /// bounds for a manually driven graph, which is why every measured
        /// offset in this project is baked in Blender and carried across.
        /// </summary>
        public const float PerchPelvisLiftMeters = 0.0526f;

        /// <summary>
        /// The drawn cushion top of the rocking chair, in room space. The
        /// generator's own seat measurement came out `0.5714` against it -
        /// fourteen tenths of a millimetre - so her slippers reach the
        /// boards without the clip being stretched to meet them.
        /// </summary>
        public const float CushionTopY = 0.57f;

        public const float MaximumStepSeconds = 0.1f;

        private CityPedestrianAssetRegistry registry;
        private Player3DFaceAtlasPresenter facePresenter;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private Vector3 modelBasePosition;
        private bool initialized;

        public bool IsInitialized => initialized;

        public CityPedestrianAssetRegistry Registry => registry;

        /// <summary>
        /// The expression she is wearing. Public, and deliberately without
        /// a caller: the atlas ships complete and undriven, exactly as the
        /// stairwell cat's grin ships with no scheduler. A later script is
        /// meant to move this; nothing in the game does today.
        /// </summary>
        public PlayerFacialExpression Expression { get; private set; } =
            PlayerFacialExpression.Neutral;

        public void Initialize(
            CityPedestrianAssetRegistry configuredRegistry,
            float phaseOffsetSeconds)
        {
            registry = configuredRegistry != null
                ? configuredRegistry
                : throw new System.ArgumentNullException(
                    nameof(configuredRegistry));
            if (registry.Animator == null || registry.ModelRoot == null)
            {
                throw new System.ArgumentException(
                    "The mother's prefab needs its animator and model root.",
                    nameof(configuredRegistry));
            }

            if (registry.IdleClip == null)
            {
                throw new System.ArgumentException(
                    "The mother's prefab needs its seated loop.",
                    nameof(configuredRegistry));
            }

            modelBasePosition = registry.ModelRoot.localPosition;
            BuildGraph(phaseOffsetSeconds);

            facePresenter = new Player3DFaceAtlasPresenter();
            facePresenter.Configure(registry.FaceAtlas);
            SetExpression(PlayerFacialExpression.Neutral);

            initialized = true;
            Evaluate(0f);
        }

        /// <summary>
        /// Shows one of the five canonical cells.
        ///
        /// Nothing calls this. It exists because the atlas exists, and the
        /// atlas exists because the face was asked for whole rather than in
        /// the one state the room currently needs. Returns false when the
        /// design carries no configured atlas.
        /// </summary>
        public bool SetExpression(PlayerFacialExpression expression)
        {
            Expression = expression;
            return facePresenter != null && facePresenter.Apply(expression);
        }

        private void BuildGraph(float phaseOffsetSeconds)
        {
            graph = PlayableGraph.Create("Mother's House Mother");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(
                graph,
                registry.IdleClip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(
                Mathf.Repeat(phaseOffsetSeconds, registry.IdleClip.length));
            AnimationPlayableOutput.Create(
                    graph,
                    "Mother's House Mother Pose",
                    registry.Animator)
                .SetSourcePlayable(playable);
            graph.Play();
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }

            Evaluate(Mathf.Min(Time.deltaTime, MaximumStepSeconds));
        }

        private void Evaluate(float deltaTime)
        {
            // Undo the previous frame's correction before advancing, or the
            // vertical offset accumulates: the graph writes the clip's own
            // model position back each evaluation.
            registry.ModelRoot.localPosition = modelBasePosition;
            graph.Evaluate(deltaTime);
            AlignHipsToCushion();
        }

        /// <summary>
        /// Sets the seat of her dress on the cushion.
        ///
        /// ONLY THE VERTICAL is corrected, exactly as the park bench sitters
        /// do it. The factory has already placed her across the chair, and
        /// letting the clip's own lean slide her along the seat would carry
        /// her knees out past the chair's front rail.
        /// </summary>
        private void AlignHipsToCushion()
        {
            Transform pelvis = registry.PelvisAnchor;
            if (pelvis == null)
            {
                return;
            }

            Vector3 moved = registry.ModelRoot.position;
            float targetPelvisY = transform.position.y +
                                  CushionTopY +
                                  PerchPelvisLiftMeters;
            moved.y += targetPelvisY - pelvis.position.y;
            registry.ModelRoot.position = moved;
        }

        private void OnDestroy()
        {
            initialized = false;
            facePresenter?.Reset();
            if (graph.IsValid())
            {
                graph.Destroy();
            }
        }
    }
}
