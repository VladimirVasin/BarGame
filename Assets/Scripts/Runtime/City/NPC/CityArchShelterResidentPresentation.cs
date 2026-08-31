using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives one autonomous, non-reactive shelter-resident loop. All motion
    /// is authored on the shared Hero V2-compatible skeleton; nothing here
    /// reads the player, camera or game state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityArchShelterResidentPresentation : MonoBehaviour
    {
        public const float MaximumStepSeconds = 0.1f;

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public CityArchShelterResidentRole Role { get; private set; }
        public AnimationClip ActiveClip { get; private set; }
        public float PlaybackSpeed => playbackSpeed;

        public float NormalizedTime
        {
            get
            {
                if (!playable.IsValid() || ActiveClip == null ||
                    ActiveClip.length <= 0f)
                {
                    return 0f;
                }

                return Mathf.Repeat(
                    (float)playable.GetTime(),
                    ActiveClip.length) / ActiveClip.length;
            }
        }

        public void Initialize(
            CityArchShelterResidentAssetRegistry registry,
            int seed)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null || registry.IdleClip == null)
            {
                throw new InvalidOperationException(
                    "The arch-shelter resident requires an Animator and " +
                    "an authored idle clip.");
            }

            if (hasGraph)
            {
                throw new InvalidOperationException(
                    "The resident presentation is already initialized.");
            }

            Role = registry.Role;
            ActiveClip = registry.IdleClip;
            float phase = HashToUnit(seed ^ ((int)Role + 1) * 7919);
            playbackSpeed = Mathf.Lerp(
                0.96f,
                1.04f,
                HashToUnit(seed ^ ((int)Role + 1) * 104729));

            try
            {
                graph = PlayableGraph.Create(
                    $"Arch Shelter {Role} Idle");
                hasGraph = graph.IsValid();
                if (!hasGraph)
                {
                    throw new InvalidOperationException(
                        "Could not create the shelter resident animation " +
                        "graph.");
                }

                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, ActiveClip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetTime(phase * ActiveClip.length);
                AnimationPlayableOutput.Create(
                        graph,
                        "Arch Shelter Resident Pose",
                        registry.Animator)
                    .SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0f);
                IsInitialized = true;
            }
            catch
            {
                DestroyGraph();
                throw;
            }
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
            DestroyGraph();
        }

        private void DestroyGraph()
        {
            if (graph.IsValid())
            {
                graph.Destroy();
            }

            playable = default;
            hasGraph = false;
            IsInitialized = false;
        }

        private static float HashToUnit(int value)
        {
            unchecked
            {
                uint hash = (uint)value ^ 0x9E3779B9u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
