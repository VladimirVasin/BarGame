using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the cemetery watchman: one authored WatchmanWatch loop —
    /// weight shifts, the disapproving head shake, the chin jut —
    /// through a small manual PlayableGraph, exactly like the weigher
    /// beside her mechanism. He is stationary at his window post; the
    /// authored shuffle stays in the library for a later patrol pass.
    /// The presentation stays passive — the talk stub lives on its
    /// own trigger, nothing here reads input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryWatchmanPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-shrug.</summary>
        public const float MaximumStepSeconds = 0.1f;

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        /// <summary>His talk stub, so whatever he has to say — a
        /// snide line or a job — can be reached from the one handle
        /// the factory hands back.</summary>
        public CemeteryWatchmanInteraction Talk { get; internal set; }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            CemeteryWatchmanStance stance)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The watchman prefab has no Animator.");
            }

            // Idle slot carries the watch loop — the same mapping the
            // art build declares; the walk slot's shuffle is unused
            // until the patrol pass.
            AnimationClip clip = registry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The watchman prefab is missing its watch loop.");
            }

            ActiveClip = clip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            graph = PlayableGraph.Create("Cemetery Watchman");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            AnimationPlayableOutput
                .Create(graph, "Cemetery Watchman Pose",
                    registry.Animator)
                .SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);
            hasGraph = true;
            IsInitialized = true;
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            graph.Evaluate(step * playbackSpeed);
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
