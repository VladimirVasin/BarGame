using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the park checkers player: one authored CheckersMull loop —
    /// three shallow breaths and an early settle — through a small manual
    /// PlayableGraph, exactly like his neighbour at the other table. The
    /// authored trudge stays in the shared library for a later pass.
    ///
    /// This is deliberately a second copy of the chess player's driver
    /// rather than a base both share. The library duplicates a passive
    /// staged driver per character everywhere else, and the two numbers
    /// that matter here are measurements off two different builds rather
    /// than one shared constant: sharing them is how a silent art bug
    /// gets in, because a wrong perch lift fails no test and merely
    /// sinks a man an inch into his bench. A third bench sitter is the
    /// point at which the extraction earns itself.
    ///
    /// The seating rule is the same one the architecture notes describe:
    /// the model is lifted until the underside of his hips rests on the
    /// plank that was actually drawn, and his boots fall wherever the
    /// lawn under the bench happens to be. That matters here for the
    /// same reason — the chess set beds its feet into a slope, and the
    /// seat plank is the one part of it held at a constant height.
    ///
    /// The presentation stays passive. Nothing here reads input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParkCheckersPlayerPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-breath.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// How far the pelvis bone rides above the underside of the
        /// seated hips. Read off this design's own art build rather than
        /// copied from the chess player's — it is printed as
        /// `pelvis lift` by the `perch_seat_height_m` validator for
        /// CheckersMull, against this model's real deformed meshes. It
        /// comes out identical to his neighbour's `0.0651`, and that is
        /// the proof that the hip and leg geometry really was authored
        /// identically rather than nearly so.
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
            ParkCheckersPlayerStance stance)
        {
            if (assetRegistry == null)
            {
                throw new ArgumentNullException(nameof(assetRegistry));
            }

            if (assetRegistry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The checkers player prefab has no Animator.");
            }

            if (assetRegistry.ModelRoot == null ||
                assetRegistry.PelvisAnchor == null)
            {
                throw new InvalidOperationException(
                    "The checkers player prefab needs a model root and a " +
                    "pelvis anchor to be seated on its plank.");
            }

            // Idle slot carries the mulling loop — the same mapping the
            // art build declares.
            AnimationClip clip = assetRegistry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The checkers player prefab is missing its mulling loop.");
            }

            registry = assetRegistry;
            ActiveClip = clip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            modelBaseLocalPosition = registry.ModelRoot.localPosition;
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            seatPelvisY = stance.SeatTopCenter.y + PerchPelvisLiftMeters;

            graph = PlayableGraph.Create("Park Checkers Player");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            AnimationPlayableOutput
                .Create(graph, "Park Checkers Player Pose",
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
