using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives one weighbridge attendant: a single authored loop —
    /// WeigherCheck for the weigher beside the mechanism, WeighedPace
    /// for the worker on the deck — through a small manual
    /// PlayableGraph, exactly like the drying-yard babushkas. The
    /// worker's corridor position is a pure function of his loop's
    /// normalized time, so pose and travel can never drift apart: he
    /// walks to the deck centre, stands still through the authored
    /// weighing pause, walks on and turns for the return trip. The
    /// staged prefab ships the weigher's chalk; this enables it only
    /// for her role. The presentation stays passive — the needle
    /// controller polls <see cref="IsWeighingNow"/>, nothing here
    /// pushes into a registry.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeighbridgeAttendantPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-stride.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>Normalized window of WeighedPace's authored
        /// standstill at the deck centre. Must match the pause the
        /// clip is authored with in
        /// tools/build-city-pedestrian-3d-model.py.</summary>
        public const float PauseStartNormalized = 0.36f;
        public const float PauseEndNormalized = 0.64f;

        private const float TurnDegreesPerSecond = 220f;

        private static readonly string[] ChalkRendererNames =
        {
            "ACC_Chalk"
        };

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        private Vector3 pathStart;
        private Vector3 pathEnd;
        private float pathLength;
        private bool strolls;

        public bool IsInitialized { get; private set; }
        public WeighbridgeAttendantRole Role { get; private set; }
        public AnimationClip ActiveClip { get; private set; }
        public bool Strolls => strolls;

        /// <summary>True while the worker stands square at the deck
        /// centre being weighed — the moment the needle answers.</summary>
        public bool IsWeighingNow
        {
            get
            {
                if (Role != WeighbridgeAttendantRole.WeighedWorker ||
                    !IsInitialized)
                {
                    return false;
                }

                float normalized = NormalizedTime;
                return normalized >= PauseStartNormalized &&
                       normalized <= PauseEndNormalized;
            }
        }

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            WeighbridgeAttendantStance stance)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The attendant prefab has no Animator.");
            }

            // Idle slot carries the weigher's check, walk slot the
            // worker's pace — the same mapping the art build declares.
            AnimationClip clip =
                stance.Role == WeighbridgeAttendantRole.WeighedWorker
                    ? registry.WalkClip
                    : registry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The attendant prefab is missing its " +
                    $"{stance.Role} loop.");
            }

            Role = stance.Role;
            ActiveClip = clip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);
            ApplyPropVisibility(registry, stance.Role);

            strolls = stance.Strolls;
            if (strolls)
            {
                pathStart = stance.Position;
                pathEnd = stance.PathEnd;
                pathLength = Vector3.Distance(pathStart, pathEnd);
            }

            graph = PlayableGraph.Create(
                $"Weighbridge Attendant {stance.Role}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            AnimationPlayableOutput
                .Create(graph, "Weighbridge Attendant Pose",
                    registry.Animator)
                .SetSourcePlayable(playable);
            graph.Play();
            graph.Evaluate(0f);
            hasGraph = true;
            IsInitialized = true;
        }

        /// <summary>The active loop's current normalized 0..1 time.</summary>
        public float NormalizedTime
        {
            get
            {
                if (!playable.IsValid() || ActiveClip == null)
                {
                    return 0f;
                }

                return Mathf.Repeat(
                    (float)playable.GetTime(),
                    ActiveClip.length) / ActiveClip.length;
            }
        }

        /// <summary>Maps the pace loop's normalized time to progress
        /// along the current half trip: walk to the centre, hold it
        /// exactly through the authored pause, walk to the far end.
        /// Pure so the hold is testable.</summary>
        public static float EvaluateCorridorProgress(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);
            if (normalized <= PauseStartNormalized)
            {
                return 0.5f * (normalized / PauseStartNormalized);
            }

            if (normalized <= PauseEndNormalized)
            {
                return 0.5f;
            }

            return 0.5f + 0.5f *
                ((normalized - PauseEndNormalized) /
                 (1f - PauseEndNormalized));
        }

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            graph.Evaluate(step * playbackSpeed);
            if (strolls)
            {
                AdvancePace();
            }
        }

        private void OnDestroy()
        {
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }

        /// <summary>Places the worker on the corridor from his loop's
        /// time: one clip iteration is one half round trip, and the
        /// iteration's parity picks the travel direction, with a
        /// smooth turn into each new trip.</summary>
        private void AdvancePace()
        {
            if (pathLength < 0.01f || ActiveClip == null ||
                !playable.IsValid())
            {
                return;
            }

            double time = playable.GetTime();
            int iteration = (int)Math.Floor(time / ActiveClip.length);
            bool returning = (iteration & 1) == 1;
            float progress = EvaluateCorridorProgress(NormalizedTime);
            Vector3 from = returning ? pathEnd : pathStart;
            Vector3 to = returning ? pathStart : pathEnd;
            transform.position = Vector3.Lerp(from, to, progress);
            Vector3 travel = to - from;
            travel.y = 0f;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(
                    travel.normalized,
                    Vector3.up),
                TurnDegreesPerSecond *
                Mathf.Min(Time.deltaTime, MaximumStepSeconds));
        }

        /// <summary>Shows exactly the prop this role holds: only the
        /// weigher carries the chalk, the worker's hands stay free.</summary>
        private static void ApplyPropVisibility(
            CityPedestrianAssetRegistry registry,
            WeighbridgeAttendantRole role)
        {
            if (role == WeighbridgeAttendantRole.Weigher)
            {
                return;
            }

            for (int index = 0;
                 index < registry.Renderers.Count;
                 index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                for (int name = 0;
                     name < ChalkRendererNames.Length;
                     name++)
                {
                    if (string.Equals(
                            renderer.name,
                            ChalkRendererNames[name],
                            StringComparison.Ordinal))
                    {
                        renderer.enabled = false;
                        break;
                    }
                }
            }
        }
    }
}
