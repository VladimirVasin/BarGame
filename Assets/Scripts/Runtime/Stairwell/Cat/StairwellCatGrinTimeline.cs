using UnityEngine;

namespace BarPromenade
{
    public enum StairwellCatGrinPhase
    {
        Hidden = 0,
        Appearing = 1,
        Held = 2,
        Vanishing = 3
    }

    public readonly struct StairwellCatGrinSample
    {
        public StairwellCatGrinSample(
            float progress,
            StairwellCatGrinPhase phase,
            bool isComplete)
        {
            Progress = progress;
            Phase = phase;
            IsComplete = isComplete;
        }

        public float Progress { get; }
        public StairwellCatGrinPhase Phase { get; }
        public bool IsComplete { get; }
    }

    /// <summary>
    /// The pure appear/hold/vanish arc of the Cheshire grin. Its
    /// attack and release are deliberately asymmetric: the smile
    /// snaps on in under half a second and takes three times as long
    /// to be un-drawn. A timeline started mid-progress scales its
    /// segment durations so an aborted half-grin never crawls.
    /// Evaluate depends only on elapsed time - frame chunking cannot
    /// change the result.
    /// </summary>
    public readonly struct StairwellCatGrinTimeline
    {
        public const float AppearSeconds = 0.4f;
        public const float VanishSeconds = 1.2f;
        public const float DefaultHoldSeconds = 4f;

        private readonly bool isVanish;
        private readonly float startProgress;
        private readonly float holdSeconds;

        private StairwellCatGrinTimeline(
            bool vanish,
            float configuredStartProgress,
            float configuredHoldSeconds)
        {
            isVanish = vanish;
            startProgress = configuredStartProgress;
            holdSeconds = configuredHoldSeconds;
        }

        /// <summary>
        /// Draw the smile in from startProgress, hold it, then
        /// un-draw it. Pass float.PositiveInfinity to hold until an
        /// explicit vanish replaces the timeline.
        /// </summary>
        public static StairwellCatGrinTimeline CreateAppear(
            float holdSeconds = DefaultHoldSeconds,
            float startProgress = 0f)
        {
            float hold =
                float.IsNaN(holdSeconds) || holdSeconds < 0f
                    ? DefaultHoldSeconds
                    : holdSeconds;
            return new StairwellCatGrinTimeline(
                false,
                Mathf.Clamp01(
                    float.IsNaN(startProgress) ? 0f : startProgress),
                hold);
        }

        public static StairwellCatGrinTimeline CreateVanish(
            float startProgress = 1f)
        {
            return new StairwellCatGrinTimeline(
                true,
                Mathf.Clamp01(
                    float.IsNaN(startProgress) ? 0f : startProgress),
                0f);
        }

        public StairwellCatGrinSample Evaluate(float elapsedSeconds)
        {
            float elapsed =
                float.IsNaN(elapsedSeconds) || elapsedSeconds < 0f
                    ? 0f
                    : elapsedSeconds;
            return isVanish
                ? EvaluateVanish(startProgress, elapsed)
                : EvaluateAppear(elapsed);
        }

        private StairwellCatGrinSample EvaluateAppear(float elapsed)
        {
            float appearDuration =
                AppearSeconds * (1f - startProgress);
            if (elapsed < appearDuration)
            {
                float amount = elapsed / appearDuration;
                float eased = 1f - Mathf.Pow(1f - amount, 3f);
                return new StairwellCatGrinSample(
                    Mathf.Lerp(startProgress, 1f, eased),
                    StairwellCatGrinPhase.Appearing,
                    false);
            }

            if (float.IsPositiveInfinity(holdSeconds) ||
                elapsed < appearDuration + holdSeconds)
            {
                return new StairwellCatGrinSample(
                    1f,
                    StairwellCatGrinPhase.Held,
                    false);
            }

            return EvaluateVanish(
                1f,
                elapsed - appearDuration - holdSeconds);
        }

        private static StairwellCatGrinSample EvaluateVanish(
            float fromProgress,
            float elapsed)
        {
            float duration = VanishSeconds * fromProgress;
            if (duration <= 0f || elapsed >= duration)
            {
                return new StairwellCatGrinSample(
                    0f,
                    StairwellCatGrinPhase.Hidden,
                    true);
            }

            float amount = elapsed / duration;
            float eased = amount * amount * (3f - 2f * amount);
            return new StairwellCatGrinSample(
                fromProgress * (1f - eased),
                StairwellCatGrinPhase.Vanishing,
                false);
        }
    }
}
