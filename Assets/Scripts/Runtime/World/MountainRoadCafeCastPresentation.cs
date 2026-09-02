using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Manual Playables presentation for one cafe figure. The controller
    /// supplies absolute role-local action time, so staggered patrons remain
    /// deterministic even after a frame hitch.
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
        private AnimationClipPlayable defaultPlayable;
        private AnimationClipPlayable actionPlayable;
        private AnimationClip actionClip;
        private float initialIdlePhaseSeconds;
        private float fallbackDefaultClipElapsedSeconds;
        private bool hasGraph;
        private bool hasActionPlayable;
        private bool actionStartedFromServiceCarry;
        private MountainRoadCafeCastClipKind currentClipKind;
        private float currentClipTimeSeconds;

        public bool IsInitialized { get; private set; }
        public MountainRoadCafeCastRole Role { get; private set; }
        public MountainRoadCafeCastAssetRegistry Registry => registry;
        public MountainRoadCafeCastClipKind CurrentClipKind =>
            currentClipKind;
        public float CurrentClipTimeSeconds => currentClipTimeSeconds;
        /// <summary>
        /// Current normalized phase of the continuously running default
        /// loop. Passive effects read the same Playable that moves the body,
        /// so they cannot drift onto a separate timer.
        /// </summary>
        public float DefaultClipNormalizedTime
        {
            get
            {
                float length = registry?.IdleClip != null
                    ? registry.IdleClip.length
                    : 0f;
                if (length <= 0f)
                {
                    return 0f;
                }

                double time = hasGraph && defaultPlayable.IsValid()
                    ? defaultPlayable.GetTime()
                    : initialIdlePhaseSeconds +
                      fallbackDefaultClipElapsedSeconds;
                return Mathf.Repeat((float)(time / length), 1f);
            }
        }
        public bool IsBeatPlaying =>
            currentClipKind != registry?.DefaultClipKind;
        public bool CanBeginBeat => IsInitialized &&
                                    currentClipKind ==
                                    registry.DefaultClipKind;

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
                assetRegistry.ModelRoot == null ||
                assetRegistry.Role != role ||
                assetRegistry.IdleClip == null)
            {
                throw new InvalidOperationException(
                    "A cafe cast prefab has an incomplete or mismatched " +
                    "animation registry.");
            }

            if (!assetRegistry.TryGetClip(
                    assetRegistry.DefaultClipKind,
                    out _,
                    out bool defaultLoops) ||
                !defaultLoops)
            {
                throw new InvalidOperationException(
                    "A cafe cast default presentation clip must loop.");
            }

            registry = assetRegistry;
            Role = role;
            initialIdlePhaseSeconds = Mathf.Max(0f, idlePhaseSeconds);
            fallbackDefaultClipElapsedSeconds = 0f;
            currentClipKind = registry.DefaultClipKind;
            currentClipTimeSeconds = initialIdlePhaseSeconds;
            IsInitialized = true;
            if (Application.isPlaying)
            {
                CreateGraph();
            }
        }

        public bool ApplyClip(
            MountainRoadCafeCastClipKind kind,
            float elapsedSeconds)
        {
            if (!IsInitialized ||
                float.IsNaN(elapsedSeconds) ||
                float.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0f ||
                !registry.TryGetClip(kind, out AnimationClip clip, out bool loop))
            {
                return false;
            }

            if (kind != currentClipKind)
            {
                actionStartedFromServiceCarry =
                    IsServiceCarryClip(currentClipKind) &&
                    IsServiceCarryClip(kind);
            }

            currentClipKind = kind;
            currentClipTimeSeconds = loop
                ? Mathf.Repeat(elapsedSeconds, Mathf.Max(0.0001f, clip.length))
                : Mathf.Min(elapsedSeconds, clip.length);
            if (kind == registry.DefaultClipKind)
            {
                // EditMode has no PlayableGraph/LateUpdate. Retain the same
                // deterministic absolute idle phase there so pure controller
                // stepping exercises smoke-window gates without changing the
                // graph-owned PlayMode clock.
                fallbackDefaultClipElapsedSeconds = elapsedSeconds;
            }
            if (Role == MountainRoadCafeCastRole.Attendant)
            {
                registry.SetCoffeePotVisible(
                    (kind == MountainRoadCafeCastClipKind.Walk ||
                     kind == MountainRoadCafeCastClipKind.Pour));
            }

            if (!hasGraph)
            {
                return true;
            }

            if (kind == registry.DefaultClipKind)
            {
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                return true;
            }

            EnsureActionPlayable(kind, clip);
            actionPlayable.SetTime(currentClipTimeSeconds);
            actionPlayable.SetSpeed(0d);
            float actionWeight = ResolveAuthoredActionWeight(
                kind,
                elapsedSeconds,
                clip.length,
                actionStartedFromServiceCarry);
            mixer.SetInputWeight(0, 1f - actionWeight);
            mixer.SetInputWeight(1, actionWeight);
            graph.Evaluate(0f);
            return true;
        }

        private static float ResolveAuthoredActionWeight(
            MountainRoadCafeCastClipKind kind,
            float elapsedSeconds,
            float clipLengthSeconds,
            bool startedFromServiceCarry)
        {
            if (clipLengthSeconds <= 0f ||
                elapsedSeconds < 0f ||
                elapsedSeconds >= clipLengthSeconds)
            {
                return 0f;
            }

            // The pot remains in the same authored carry chain throughout
            // Walk -> Pour -> Walk. Blending those clips back toward Wipe at
            // either edge dragged the right hand and pot through the counter.
            if (kind == MountainRoadCafeCastClipKind.Pour ||
                (kind == MountainRoadCafeCastClipKind.Walk &&
                 startedFromServiceCarry))
            {
                return 1f;
            }

            if (kind == MountainRoadCafeCastClipKind.Walk)
            {
                return Mathf.Clamp01(
                    elapsedSeconds / BeatBlendInSeconds);
            }

            float rise = Mathf.Clamp01(
                elapsedSeconds / BeatBlendInSeconds);
            float fall = Mathf.Clamp01(
                (clipLengthSeconds - elapsedSeconds) /
                BeatBlendOutSeconds);
            return Mathf.Min(rise, fall);
        }

        private static bool IsServiceCarryClip(
            MountainRoadCafeCastClipKind kind)
        {
            return kind == MountainRoadCafeCastClipKind.Walk ||
                   kind == MountainRoadCafeCastClipKind.Pour;
        }

        /// <summary>
        /// Kept as the public blend-envelope contract used by focused asset
        /// tests and older callers. New service playback is phase-clocked and
        /// its authored one-shots begin/end on their matching base poses.
        /// </summary>
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
                (clipLengthSeconds + BeatBlendOutSeconds - elapsedSeconds) /
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

            // Visual playback may take bounded substeps; scheduling and fill
            // state are not clamped and remain hitch-safe in the pure model.
            float remaining = Mathf.Max(0f, Time.deltaTime);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, MaximumStepSeconds);
                graph.Evaluate(step);
                remaining -= step;
            }
        }

        private void CreateGraph()
        {
            if (hasGraph || registry == null)
            {
                return;
            }

            graph = PlayableGraph.Create("Mountain Cafe " + Role);
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                defaultPlayable = AnimationClipPlayable.Create(
                    graph,
                    registry.IdleClip);
                defaultPlayable.SetApplyFootIK(false);
                defaultPlayable.SetApplyPlayableIK(false);
                defaultPlayable.SetTime(Mathf.Repeat(
                    initialIdlePhaseSeconds,
                    Mathf.Max(0.0001f, registry.IdleClip.length)));

                mixer = AnimationMixerPlayable.Create(graph, 2);
                graph.Connect(defaultPlayable, 0, mixer, 0);
                mixer.SetInputWeight(0, 1f);
                mixer.SetInputWeight(1, 0f);
                AnimationPlayableOutput.Create(
                        graph,
                        "Mountain Cafe Pose",
                        registry.Animator)
                    .SetSourcePlayable(mixer);

                graph.Play();
                graph.Evaluate(0f);
                hasGraph = true;
                ApplyClip(currentClipKind, currentClipTimeSeconds);
            }
            catch
            {
                if (graph.IsValid())
                {
                    graph.Destroy();
                }

                hasGraph = false;
                hasActionPlayable = false;
                throw;
            }
        }

        private void EnsureActionPlayable(
            MountainRoadCafeCastClipKind kind,
            AnimationClip clip)
        {
            if (hasActionPlayable &&
                actionClip == clip)
            {
                return;
            }

            if (hasActionPlayable)
            {
                graph.Disconnect(mixer, 1);
                actionPlayable.Destroy();
                hasActionPlayable = false;
                actionClip = null;
            }

            actionPlayable = AnimationClipPlayable.Create(graph, clip);
            actionPlayable.SetApplyFootIK(false);
            actionPlayable.SetApplyPlayableIK(false);
            actionPlayable.SetSpeed(0d);
            graph.Connect(actionPlayable, 0, mixer, 1);
            actionClip = clip;
            hasActionPlayable = true;
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
            hasActionPlayable = false;
            actionClip = null;
            registry?.SetCoffeePotVisible(false);
        }
    }
}
