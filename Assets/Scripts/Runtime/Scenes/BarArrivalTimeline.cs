using UnityEngine;

namespace BarPromenade
{
    public readonly struct BarArrivalFrame
    {
        internal BarArrivalFrame(
            float elapsedTime,
            float duration,
            float normalizedTime,
            float cameraBlend,
            bool isComplete)
        {
            ElapsedTime = elapsedTime;
            Duration = duration;
            NormalizedTime = normalizedTime;
            CameraBlend = cameraBlend;
            IsComplete = isComplete;
        }

        public float ElapsedTime { get; }
        public float Duration { get; }
        public float NormalizedTime { get; }
        public float CameraBlend { get; }
        public bool IsComplete { get; }
    }

    public static class BarArrivalTimeline
    {
        public const float MinimumDuration = 1.2f;
        public const float DefaultDuration = 1.35f;
        public const float MaximumDuration = 1.5f;

        public static float ClampDuration(float duration)
        {
            if (float.IsNaN(duration))
            {
                return DefaultDuration;
            }

            if (float.IsNegativeInfinity(duration))
            {
                return MinimumDuration;
            }

            if (float.IsPositiveInfinity(duration))
            {
                return MaximumDuration;
            }

            return Mathf.Clamp(
                duration,
                MinimumDuration,
                MaximumDuration);
        }

        public static BarArrivalFrame Evaluate(
            float unscaledElapsedTime,
            float duration = DefaultDuration)
        {
            float safeDuration = ClampDuration(duration);
            float elapsedTime = SanitizeElapsedTime(
                unscaledElapsedTime,
                safeDuration);
            float normalizedTime = elapsedTime / safeDuration;
            float cameraBlend = SmootherStep(normalizedTime);

            return new BarArrivalFrame(
                elapsedTime,
                safeDuration,
                normalizedTime,
                cameraBlend,
                elapsedTime >= safeDuration);
        }

        private static float SanitizeElapsedTime(
            float elapsedTime,
            float duration)
        {
            if (float.IsNaN(elapsedTime) || elapsedTime <= 0f)
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(elapsedTime))
            {
                return duration;
            }

            return Mathf.Min(elapsedTime, duration);
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }
    }
}
