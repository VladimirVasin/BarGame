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
    /// own trigger, nothing here reads input. The one thing he does
    /// notice is the hero: given the player root, he turns his head
    /// after him under the hero's own notice rule, the way every
    /// walker on the street does.
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
        private readonly NpcAttentionHeadLayer attention =
            new NpcAttentionHeadLayer();
        private readonly NpcAttentionNotice notice =
            new NpcAttentionNotice();
        private HeroAttentionFocus hero;

        public bool IsInitialized { get; private set; }
        public AnimationClip ActiveClip { get; private set; }

        /// <summary>Whether the hero holds his head this frame.</summary>
        public bool IsAttending => notice.IsHeld;

        /// <summary>Where his head is asked to look, or <c>null</c>.</summary>
        public Vector3? AttentionFocus => attention.Focus;

        /// <summary>How far the glance is blended in, 0..1.</summary>
        public float AttentionWeight => attention.Weight;

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
            attention.Bind(transform, registry.HeadAnchor, registry.Animator);
            IsInitialized = true;
        }

        /// <summary>
        /// Who he watches for. Without a root he never looks up from
        /// his post; with one, the hero's head bone (or a face height
        /// over the player root) is what his notice cone is tested
        /// against every frame.
        /// </summary>
        public void SetHero(Transform heroRoot)
        {
            hero = heroRoot != null
                ? new HeroAttentionFocus(heroRoot)
                : null;
            if (hero == null)
            {
                notice.Reset();
                attention.SetFocus(null);
            }
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// One frame: the watch loop, then the glance on top of it.
        /// Public so a deterministic check can step him without the
        /// player loop.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (!hasGraph)
            {
                return;
            }

            float step = Mathf.Min(
                float.IsNaN(deltaTime) ? 0f : Mathf.Max(0f, deltaTime),
                MaximumStepSeconds);
            attention.Restore();
            graph.Evaluate(step * playbackSpeed);
            attention.SetFocus(
                notice.Resolve(
                    transform.position,
                    transform.eulerAngles.y,
                    hero != null ? hero.Resolve() : (Vector3?)null));
            attention.Apply(step);
        }

        private void OnDestroy()
        {
            attention.Unbind();
            if (hasGraph && graph.IsValid())
            {
                graph.Destroy();
            }

            hasGraph = false;
        }
    }
}
