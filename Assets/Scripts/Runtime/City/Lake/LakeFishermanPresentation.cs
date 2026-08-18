using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the lake fisherman: one authored seated loop — the rod dip
    /// and lift, the slow head turn to the float — through a small
    /// manual PlayableGraph, exactly like the watchman on his doorstep.
    /// He never leaves the end of the pier; the authored walk stays in
    /// the library against a later pass. The presentation stays passive:
    /// the talk stub lives on its own trigger and nothing here reads
    /// input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LakeFishermanPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-cast.</summary>
        public const float MaximumStepSeconds = 0.1f;

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            LakeFishermanStance stance)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The fisherman prefab has no Animator.");
            }

            // Idle slot carries the seated loop — the same mapping the
            // art build declares; the walk slot is unused until he is
            // ever given somewhere to go.
            AnimationClip clip = registry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The fisherman prefab is missing its seated loop.");
            }

            ActiveClip = clip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            graph = PlayableGraph.Create("Lake Fisherman");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            AnimationPlayableOutput
                .Create(graph, "Lake Fisherman Pose", registry.Animator)
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
