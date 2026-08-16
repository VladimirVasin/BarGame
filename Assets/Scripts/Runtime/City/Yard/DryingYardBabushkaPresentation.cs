using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    /// <summary>
    /// Drives one drying-yard babushka: a single authored loop —
    /// BabushkaBeat for the two carpet beaters, BabushkaSmoke for the
    /// strolling smoker — through a small manual PlayableGraph,
    /// exactly like the yard wheelchair. A beater also owns her
    /// carpet's reaction: around the authored strike moment the hung
    /// cloth receives a short acceleration pulse away from her. The
    /// smoker walks her corridor back and forth, turning at the ends,
    /// while her loop keeps gesturing. The staged prefab ships both
    /// hand props; this enables only the one her role holds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DryingYardBabushkaPresentation : MonoBehaviour
    {
        /// <summary>A hitch longer than this advances the loop by a
        /// bounded step instead of teleporting mid-swing.</summary>
        public const float MaximumStepSeconds = 0.1f;

        /// <summary>Normalized moment of BabushkaBeat's authored
        /// carpet contact.</summary>
        public const float StrikeNormalizedTime = 0.28f;

        /// <summary>How long the strike pulse shakes the carpet.</summary>
        public const float StrikePulseSeconds = 0.16f;

        /// <summary>Peak cloth acceleration of one strike, away from
        /// the beater.</summary>
        public const float StrikePulseAcceleration = 16f;

        private const float TurnDegreesPerSecond = 220f;

        private static readonly string[] BeaterRendererNames =
        {
            "ACC_BeaterHandle",
            "ACC_BeaterNeck",
            "ACC_BeaterPaddleRise",
            "ACC_BeaterPaddleTip"
        };

        private static readonly string[] CigaretteRendererNames =
        {
            "ACC_Cigarette",
            "ACC_CigaretteEmber"
        };

        private PlayableGraph graph;
        private AnimationClipPlayable playable;
        private float playbackSpeed = 1f;
        private bool hasGraph;

        private Cloth carpet;
        private Vector3 strikeDirection;
        private float previousNormalizedTime;
        private float strikePulseRemaining;

        private Vector3 pathStart;
        private Vector3 pathEnd;
        private float moveSpeed;
        private float pathLength;
        private float pathDistance;
        private bool strolls;

        public bool IsInitialized { get; private set; }
        public DryingYardBabushkaRole Role { get; private set; }
        public AnimationClip ActiveClip { get; private set; }
        public Cloth Carpet => carpet;
        public bool Strolls => strolls;

        public void Initialize(
            CityPedestrianAssetRegistry registry,
            DryingYardBabushkaStance stance)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (registry.Animator == null)
            {
                throw new InvalidOperationException(
                    "The babushka prefab has no Animator.");
            }

            // Idle slot carries the smoking stroll, walk slot the
            // beating loop — the same mapping the art build declares.
            AnimationClip clip =
                stance.Role == DryingYardBabushkaRole.CarpetBeater
                    ? registry.WalkClip
                    : registry.IdleClip;
            if (clip == null)
            {
                throw new InvalidOperationException(
                    "The babushka prefab is missing its " +
                    $"{stance.Role} loop.");
            }

            Role = stance.Role;
            ActiveClip = clip;
            playbackSpeed = Mathf.Max(0.05f, stance.PlaybackSpeed);
            registry.ApplyPaletteVariant(stance.PaletteVariant);
            ApplyPropVisibility(registry, stance.Role);

            if (stance.Role == DryingYardBabushkaRole.CarpetBeater)
            {
                // The beater shakes exactly the carpet she faces; a
                // missing registration leaves the strike silent
                // rather than broken.
                carpet = CityDryingYardCarpetRegistry.Find(
                    stance.CarpetId);
                strikeDirection = stance.Facing;
                if (carpet == null)
                {
                    GameLog.Warning(
                        "city",
                        "drying_yard_carpet_missing",
                        GameLog.Field(
                            "carpet_id",
                            stance.CarpetId ?? "<null>"));
                }
            }

            strolls = stance.Strolls;
            if (strolls)
            {
                pathStart = stance.Position;
                pathEnd = stance.PathEnd;
                moveSpeed = stance.MoveSpeed;
                pathLength = Vector3.Distance(pathStart, pathEnd);
                pathDistance = 0f;
            }

            graph = PlayableGraph.Create(
                $"Drying Yard Babushka {stance.Role}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetTime(Mathf.Repeat(
                stance.PhaseOffsetSeconds,
                clip.length));
            previousNormalizedTime = NormalizedTime;
            AnimationPlayableOutput
                .Create(graph, "Drying Yard Babushka Pose",
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

        private void LateUpdate()
        {
            if (!hasGraph)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            graph.Evaluate(step * playbackSpeed);
            if (Role == DryingYardBabushkaRole.CarpetBeater)
            {
                AdvanceStrikePulse(step);
            }

            if (strolls)
            {
                AdvanceStroll(step);
            }
        }

        private void OnDestroy()
        {
            if (carpet != null)
            {
                carpet.externalAcceleration = Vector3.zero;
            }

            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }

        /// <summary>Fires the carpet pulse when the loop crosses the
        /// authored contact moment and decays it afterwards.</summary>
        private void AdvanceStrikePulse(float deltaTime)
        {
            if (carpet == null)
            {
                return;
            }

            float normalized = NormalizedTime;
            bool crossedStrike =
                previousNormalizedTime <= StrikeNormalizedTime
                    ? normalized >= StrikeNormalizedTime &&
                      previousNormalizedTime < normalized
                    : normalized >= StrikeNormalizedTime &&
                      normalized < previousNormalizedTime;
            if (crossedStrike)
            {
                strikePulseRemaining = StrikePulseSeconds;
            }

            previousNormalizedTime = normalized;
            if (strikePulseRemaining > 0f)
            {
                strikePulseRemaining -= deltaTime;
                float envelope = Mathf.Clamp01(
                    strikePulseRemaining / StrikePulseSeconds);
                carpet.externalAcceleration =
                    strikeDirection *
                    (StrikePulseAcceleration * envelope);
            }
            else
            {
                carpet.externalAcceleration = Vector3.zero;
            }
        }

        /// <summary>Ping-pong stroll along the corridor with a smooth
        /// turn toward the current travel direction.</summary>
        private void AdvanceStroll(float deltaTime)
        {
            if (pathLength < 0.01f)
            {
                return;
            }

            pathDistance += moveSpeed * deltaTime;
            float wrapped = Mathf.Repeat(pathDistance, pathLength * 2f);
            bool returning = wrapped > pathLength;
            float along = returning
                ? pathLength * 2f - wrapped
                : wrapped;
            Vector3 axis = (pathEnd - pathStart) / pathLength;
            transform.position = pathStart + axis * along;
            Vector3 travel = returning ? -axis : axis;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(travel, Vector3.up),
                TurnDegreesPerSecond * deltaTime);
        }

        /// <summary>Shows exactly the prop this role holds: the two
        /// beaters carry no cigarette and the smoker no beater.</summary>
        private static void ApplyPropVisibility(
            CityPedestrianAssetRegistry registry,
            DryingYardBabushkaRole role)
        {
            string[] hidden =
                role == DryingYardBabushkaRole.CarpetBeater
                    ? CigaretteRendererNames
                    : BeaterRendererNames;
            for (int index = 0;
                 index < registry.Renderers.Count;
                 index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                for (int name = 0; name < hidden.Length; name++)
                {
                    if (string.Equals(
                            renderer.name,
                            hidden[name],
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
