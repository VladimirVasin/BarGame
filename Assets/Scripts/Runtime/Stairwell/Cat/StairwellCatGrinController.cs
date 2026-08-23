using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The public trigger API of the Cheshire grin. It owns the one
    /// runtime float that exists about the smile - _GrinProgress on
    /// the ACC_Grin renderer, written through a MaterialPropertyBlock
    /// - plus the timeline that animates it. It decides nothing about
    /// WHEN: there is deliberately no scheduler here; a future
    /// trickster script calls BeginGrin/EndGrin. By default the grin
    /// does not exist: the renderer ships disabled at progress zero,
    /// and the shader discards every fragment on top of that.
    ///
    /// It has no Update of its own - the cat actor ticks it inside
    /// AdvancePresentation so tests drive everything with explicit
    /// deltas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StairwellCatGrinController : MonoBehaviour
    {
        private static readonly int GrinProgressId =
            Shader.PropertyToID("_GrinProgress");

        private Renderer grinRenderer;
        private MaterialPropertyBlock propertyBlock;
        private StairwellCatGrinTimeline timeline;
        private bool timelineActive;
        private float timelineElapsed;

        public bool IsInitialized { get; private set; }
        public float GrinProgress { get; private set; }
        public StairwellCatGrinPhase Phase { get; private set; } =
            StairwellCatGrinPhase.Hidden;

        public bool IsGrinVisible => GrinProgress > 0f;

        /// <summary>How far the head has committed to the
        /// over-shoulder turn: a smoothstep of the reveal, so the
        /// turn and the drawing of the smile are one gesture.</summary>
        public float HeadTurnWeight
        {
            get
            {
                float amount = Mathf.Clamp01(GrinProgress);
                return amount * amount * (3f - 2f * amount);
            }
        }

        public void Initialize(Renderer renderer)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The grin controller is already initialized.");
            }

            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            grinRenderer = renderer;
            propertyBlock = new MaterialPropertyBlock();
            IsInitialized = true;
            Apply(0f);
        }

        /// <summary>
        /// Draws the smile in from the current progress, holds it for
        /// holdSeconds, then un-draws it. float.PositiveInfinity holds
        /// until EndGrin. Restarting mid-arc is allowed.
        /// </summary>
        public bool BeginGrin(
            float holdSeconds =
                StairwellCatGrinTimeline.DefaultHoldSeconds)
        {
            if (!IsInitialized ||
                float.IsNaN(holdSeconds) ||
                holdSeconds < 0f)
            {
                return false;
            }

            timeline = StairwellCatGrinTimeline.CreateAppear(
                holdSeconds,
                GrinProgress);
            timelineActive = true;
            timelineElapsed = 0f;
            Phase = StairwellCatGrinPhase.Appearing;
            return true;
        }

        /// <summary>Starts the slow un-drawing from wherever the
        /// smile currently is.</summary>
        public bool EndGrin()
        {
            if (!IsInitialized ||
                (!timelineActive && GrinProgress <= 0f))
            {
                return false;
            }

            timeline = StairwellCatGrinTimeline.CreateVanish(
                GrinProgress);
            timelineActive = true;
            timelineElapsed = 0f;
            Phase = StairwellCatGrinPhase.Vanishing;
            return true;
        }

        /// <summary>Manual override for a future script that wants to
        /// puppet the reveal directly; cancels any running timeline
        /// until the next BeginGrin/EndGrin.</summary>
        public void SetGrinProgress(float progress)
        {
            if (!IsInitialized)
            {
                return;
            }

            float amount = float.IsNaN(progress)
                ? 0f
                : Mathf.Clamp01(progress);
            timelineActive = false;
            Phase = amount > 0f
                ? StairwellCatGrinPhase.Held
                : StairwellCatGrinPhase.Hidden;
            Apply(amount);
        }

        /// <summary>Ticked by the cat actor from AdvancePresentation.</summary>
        public void Advance(float deltaTime)
        {
            if (!IsInitialized ||
                !timelineActive ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return;
            }

            timelineElapsed += deltaTime;
            StairwellCatGrinSample sample =
                timeline.Evaluate(timelineElapsed);
            Phase = sample.Phase;
            Apply(sample.Progress);
            if (sample.IsComplete)
            {
                timelineActive = false;
            }
        }

        private void Apply(float progress)
        {
            GrinProgress = progress;
            if (grinRenderer == null)
            {
                return;
            }

            grinRenderer.enabled = progress > 0f;
            grinRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(GrinProgressId, progress);
            grinRenderer.SetPropertyBlock(propertyBlock);
        }

        private void OnDisable()
        {
            // Default-hidden is a hard guarantee: a disabled cat
            // never leaves a smile hanging in the stairwell.
            if (IsInitialized)
            {
                timelineActive = false;
                Phase = StairwellCatGrinPhase.Hidden;
                Apply(0f);
            }
        }
    }
}
