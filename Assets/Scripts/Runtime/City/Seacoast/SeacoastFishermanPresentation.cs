using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives the seacoast fisherman: one authored leaning loop — four slow
    /// breaths, one rod correction and a look down the line — through a
    /// small manual PlayableGraph, exactly like the watchman on his
    /// doorstep. He never leaves the end of the pier; the authored
    /// trudge stays in the library against a later pass. The
    /// presentation stays passive: the talk stub lives on its own
    /// trigger, the pipe on its own effect, and nothing here reads
    /// input.
    ///
    /// It also publishes the loop's own phase. The pipe ember and the
    /// plume have to belong to this chest rather than to a second
    /// free-running timer, and the only way to guarantee that is to
    /// read them off the clip that is moving it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeacoastFishermanPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-cast.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>
        /// How many breaths the authored loop contains. This mirrors the
        /// `FishermanLean` key grid in
        /// `tools/build-city-pedestrian-3d-model.py`, which rests at
        /// every quarter of the loop and reaches full inhale at every
        /// eighth between them. Re-timing that clip without re-timing
        /// this constant silently detaches the smoke from the chest.
        /// </summary>
        public const int BreathsPerLoop = 4;

        /// <summary>
        /// Where in a single breath the draw peaks: the ribs are at
        /// their fullest exactly halfway through, because the authored
        /// grid puts the inhale key midway between two rest keys.
        /// </summary>
        public const float InhalePeakPhase = 0.5f;

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private float playbackSpeed = 1f;
        private float clipLengthSeconds = 1f;
        private bool hasGraph;

        public bool IsInitialized { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        /// <summary>
        /// The loop's own position, in `[0, 1)`. Zero before the graph
        /// exists, so a reader never has to special-case initialisation.
        /// </summary>
        public float NormalizedTime
        {
            get
            {
                if (!hasGraph || clipLengthSeconds <= 0f)
                {
                    return 0f;
                }

                return Mathf.Repeat(
                    (float)playable.GetTime() / clipLengthSeconds,
                    1f);
            }
        }

        /// <summary>
        /// Where he is in the current breath, in `[0, 1)`: `0` fully
        /// exhaled, <see cref="InhalePeakPhase"/> the top of the draw.
        /// </summary>
        public float BreathPhase => BreathPhaseAt(NormalizedTime);

        /// <summary>
        /// How open the chest is right now, in `[0, 1]` — a triangle
        /// over the breath rather than a sine, because the authored
        /// clip interpolates between two keys and this has to agree
        /// with what the ribs are actually doing.
        /// </summary>
        public float BreathAmount => BreathAmountAt(BreathPhase);

        /// <summary>Pure: the breath phase at a loop position.</summary>
        public static float BreathPhaseAt(float normalizedTime)
        {
            return Mathf.Repeat(normalizedTime * BreathsPerLoop, 1f);
        }

        /// <summary>Pure: chest opening at a breath phase.</summary>
        public static float BreathAmountAt(float breathPhase)
        {
            float phase = Mathf.Repeat(breathPhase, 1f);
            return phase <= InhalePeakPhase
                ? phase / InhalePeakPhase
                : (1f - phase) / (1f - InhalePeakPhase);
        }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            SeacoastFishermanStance stance)
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

            // Idle slot carries the leaning loop — the same mapping the
            // art build declares; the walk slot is unused until he is
            // ever given somewhere to go.
            AnimationClip clip = registry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The fisherman prefab is missing its leaning loop.");
            }

            ActiveClip = clip;
            clipLengthSeconds = Mathf.Max(0.0001f, clip.length);
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);

            graph = PlayableGraph.Create("Seacoast Fisherman");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            AnimationPlayableOutput
                .Create(graph, "Seacoast Fisherman Pose", registry.Animator)
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
