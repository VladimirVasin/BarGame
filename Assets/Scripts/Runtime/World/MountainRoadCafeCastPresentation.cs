using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Keeps one bespoke cafe figure in its authored idle and briefly blends
    /// its one visible beat over that loop. Scheduling belongs to the shared
    /// cast controller; this component owns only pose playback and graph
    /// lifetime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadCafeCastPresentation : MonoBehaviour
    {
        public const float MaximumStepSeconds = 0.1f;
        public const float BeatBlendInSeconds = 0.18f;
        public const float BeatBlendOutSeconds = 0.32f;

        private MountainRoadCafeCastAssetRegistry registry;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable beatPlayable;
        private float initialIdlePhaseSeconds;
        private float beatLengthSeconds;
        private float beatElapsedSeconds;
        private bool beatHeldAtEnd;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public MountainRoadCafeCastRole Role { get; private set; }
        public MountainRoadCafeCastAssetRegistry Registry => registry;

        public bool IsBeatPlaying =>
            hasGraph &&
            beatElapsedSeconds <
            beatLengthSeconds + BeatBlendOutSeconds;

        public bool CanBeginBeat =>
            IsInitialized && hasGraph && !IsBeatPlaying;

        public void Initialize(
            MountainRoadCafeCastAssetRegistry assetRegistry,
            MountainRoadCafeCastRole role,
            float idlePhaseSeconds)
        {
            if (assetRegistry == null)
            {
                throw new ArgumentNullException(nameof(assetRegistry));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The cafe cast presentation is already initialized.");
            }

            if (assetRegistry.Animator == null ||
                assetRegistry.ModelRoot == null)
            {
                throw new InvalidOperationException(
                    "A cafe cast prefab requires an Animator and model root.");
            }

            if (assetRegistry.IdleClip == null ||
                assetRegistry.BeatClip == null)
            {
                throw new InvalidOperationException(
                    "A cafe cast prefab requires its Idle and Beat clips.");
            }

            registry = assetRegistry;
            Role = role;
            initialIdlePhaseSeconds = Mathf.Max(0f, idlePhaseSeconds);
            beatLengthSeconds = Mathf.Max(
                0.0001f,
                assetRegistry.BeatClip.length);
            IsInitialized = true;
            if (Application.isPlaying)
            {
                CreateGraph();
            }
        }

        public bool TryBeginBeat()
        {
            if (!CanBeginBeat)
            {
                return false;
            }

            beatElapsedSeconds = 0f;
            beatHeldAtEnd = false;
            beatPlayable.SetTime(0d);
            beatPlayable.SetSpeed(1d);
            return true;
        }

        public static float ResolveBeatWeight(
            float elapsedSeconds,
            float clipLengthSeconds)
        {
            if (clipLengthSeconds <= 0f ||
                elapsedSeconds < 0f ||
                float.IsNaN(elapsedSeconds) ||
                elapsedSeconds >=
                    clipLengthSeconds + BeatBlendOutSeconds)
            {
                return 0f;
            }

            float rise = Mathf.Clamp01(
                elapsedSeconds / BeatBlendInSeconds);
            float fall = Mathf.Clamp01(
                (clipLengthSeconds + BeatBlendOutSeconds -
                 elapsedSeconds) /
                BeatBlendOutSeconds);
            return Mathf.Min(rise, fall);
        }

        private void OnEnable()
        {
            if (Application.isPlaying && IsInitialized && !hasGraph)
            {
                CreateGraph();
            }
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            float step = Mathf.Min(
                Time.deltaTime,
                MaximumStepSeconds);
            if (IsBeatPlaying)
            {
                beatElapsedSeconds += step;
                if (!beatHeldAtEnd &&
                    beatElapsedSeconds >= beatLengthSeconds)
                {
                    beatHeldAtEnd = true;
                    beatPlayable.SetTime(beatLengthSeconds);
                    beatPlayable.SetSpeed(0d);
                }
            }

            float beatWeight = ResolveBeatWeight(
                beatElapsedSeconds,
                beatLengthSeconds);
            mixer.SetInputWeight(0, 1f - beatWeight);
            mixer.SetInputWeight(1, beatWeight);
            graph.Evaluate(step);
        }

        private void CreateGraph()
        {
            if (hasGraph || registry == null)
            {
                return;
            }

            graph = PlayableGraph.Create(
                "Mountain Cafe " + Role);
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                idlePlayable = AnimationClipPlayable.Create(
                    graph,
                    registry.IdleClip);
                idlePlayable.SetApplyFootIK(false);
                idlePlayable.SetApplyPlayableIK(false);
                idlePlayable.SetTime(Mathf.Repeat(
                    initialIdlePhaseSeconds,
                    Mathf.Max(0.0001f, registry.IdleClip.length)));

                beatPlayable = AnimationClipPlayable.Create(
                    graph,
                    registry.BeatClip);
                beatPlayable.SetApplyFootIK(false);
                beatPlayable.SetApplyPlayableIK(false);
                beatPlayable.SetTime(beatLengthSeconds);
                beatPlayable.SetSpeed(0d);

                mixer = AnimationMixerPlayable.Create(graph, 2);
                graph.Connect(idlePlayable, 0, mixer, 0);
                graph.Connect(beatPlayable, 0, mixer, 1);
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                AnimationPlayableOutput.Create(
                        graph,
                        "Mountain Cafe Pose",
                        registry.Animator)
                    .SetSourcePlayable(mixer);

                beatElapsedSeconds =
                    beatLengthSeconds + BeatBlendOutSeconds;
                beatHeldAtEnd = true;
                graph.Play();
                graph.Evaluate(0f);
                hasGraph = true;
            }
            catch
            {
                if (graph.IsValid())
                {
                    graph.Destroy();
                }

                hasGraph = false;
                throw;
            }
        }

        private void OnDisable()
        {
            DestroyGraph();
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

            hasGraph = false;
        }
    }
}
