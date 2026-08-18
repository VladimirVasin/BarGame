using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the park chess player: one authored ChessBrood loop —
    /// three slow breaths and a deeper settle — through a small manual
    /// PlayableGraph, exactly like the watchman at his window and the
    /// fisherman on his boards. The authored trudge stays in the shared
    /// library for a later pass.
    ///
    /// The one thing this presentation does that the other stationary
    /// drivers do not is seat him. He is the first staged design whose
    /// idle is seated on world furniture, and neither of the existing
    /// grounding rules fits: pinning his lowest sole would drag him
    /// down until he stood on the lawn, and the bus cabin's rule aligns
    /// a pelvis bone to a cushion anchor that a park bench does not
    /// have. So the model is lifted until the underside of his hips
    /// rests on the plank that was actually drawn, and his boots fall
    /// wherever the lawn under the bench happens to be. That matters
    /// here specifically: the chess set beds its feet into a slope, and
    /// the seat plank is the one part of it held at a constant height.
    ///
    /// The presentation stays passive. Nothing here reads input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParkChessPlayerPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-breath.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// How far the pelvis bone rides above the underside of the
        /// seated hips. Measured by the art build rather than guessed —
        /// it is printed as `pelvis lift` by the `perch_seat_height_m`
        /// validator for ChessBrood, against the real deformed meshes —
        /// and it is what turns "his pelvis is somewhere near the seat"
        /// into "the seat of his coat is on the timber".
        /// </summary>
        public const float PerchPelvisLiftMeters = 0.0651f;

        private CityPedestrianAssetRegistry registry;
        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private Vector3 modelBaseLocalPosition;
        private float seatPelvisY;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        /// <summary>
        /// The height the pelvis bone is held at, which is the drawn
        /// plank plus the measured lift. Where he sits across the plank
        /// is the factory's business, not this component's.
        /// </summary>
        public float SeatPelvisY => seatPelvisY;

        public void Initialize(
            CityPedestrianAssetRegistry assetRegistry,
            ParkChessPlayerStance stance)
        {
            if (assetRegistry == null)
            {
                throw new ArgumentNullException(nameof(assetRegistry));
            }

            if (assetRegistry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The chess player prefab has no Animator.");
            }

            if (assetRegistry.ModelRoot == null ||
                assetRegistry.PelvisAnchor == null)
            {
                throw new InvalidOperationException(
                    "The chess player prefab needs a model root and a " +
                    "pelvis anchor to be seated on its plank.");
            }

            // Idle slot carries the brooding loop — the same mapping the
            // art build declares.
            AnimationClip clip = assetRegistry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The chess player prefab is missing its brooding loop.");
            }

            registry = assetRegistry;
            ActiveClip = clip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            modelBaseLocalPosition = registry.ModelRoot.localPosition;
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            seatPelvisY = stance.SeatTopCenter.y + PerchPelvisLiftMeters;

            graph = PlayableGraph.Create("Park Chess Player");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            AnimationPlayableOutput
                .Create(graph, "Park Chess Player Pose",
                    registry.Animator)
                .SetSourcePlayable(playable);
            graph.Play();
            Evaluate(0f);
            hasGraph = true;
            IsInitialized = true;
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            Evaluate(Mathf.Min(Time.deltaTime, MaximumStepSeconds) *
                playbackSpeed);
        }

        private void Evaluate(float deltaTime)
        {
            if (!graph.IsValid())
            {
                return;
            }

            // The seat correction is applied to the model root, so the
            // root has to go back to its authored basis before the clip
            // is sampled or every frame would add to the last one.
            if (registry != null && registry.ModelRoot != null)
            {
                registry.ModelRoot.localPosition = modelBaseLocalPosition;
            }

            graph.Evaluate(deltaTime);
            AlignHipsToPlank();
        }

        private void AlignHipsToPlank()
        {
            if (registry == null ||
                registry.ModelRoot == null ||
                registry.PelvisAnchor == null)
            {
                return;
            }

            // Only the vertical is corrected. The factory already put him
            // across the plank, and letting the clip's own forward lean
            // slide him over the timber would take his elbows off the
            // board rim they were fitted to.
            Vector3 moved = registry.ModelRoot.position;
            moved.y += seatPelvisY - registry.PelvisAnchor.position.y;
            registry.ModelRoot.position = moved;
        }

        private void OnDestroy()
        {
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }
    }
}
