using UnityEngine;

namespace BarPromenade
{
    public readonly struct DoorTransitionPose
    {
        internal DoorTransitionPose(
            float elapsedTime,
            float normalizedTime,
            float handleTurn,
            float doorOpen,
            float cameraPush,
            float blackOpacity,
            bool isComplete)
        {
            ElapsedTime = elapsedTime;
            NormalizedTime = normalizedTime;
            HandleTurn = handleTurn;
            DoorOpen = doorOpen;
            CameraPush = cameraPush;
            BlackOpacity = blackOpacity;
            IsComplete = isComplete;
        }

        public float ElapsedTime { get; }
        public float NormalizedTime { get; }
        public float HandleTurn { get; }
        public float DoorOpen { get; }
        public float CameraPush { get; }
        public float BlackOpacity { get; }
        public bool IsComplete { get; }
    }

    public static class DoorTransitionTimeline
    {
        // Absolute unscaled seconds. The sequence is short on purpose: the
        // door scene's cost is paid on top of the destination's own build,
        // so every extra second here is pure waiting (decision 2026-09-12,
        // the 3.15 s keyframes scaled by 1.6 / 3.15). The screen is fully
        // black up to RevealStartTime and again from TotalDuration, when
        // activation is released.
        public const float TotalDuration = 1.60f;
        public const float RevealStartTime = 0.06f;
        public const float RevealEndTime = 0.23f;
        public const float HandleStartTime = 0.18f;
        public const float HandleEndTime = 0.30f;
        public const float DoorOpenStartTime = 0.30f;
        public const float DoorOpenEndTime = 1.19f;
        public const float CameraPushStartTime = 1.04f;
        public const float CameraPushEndTime = 1.42f;
        public const float FadeOutStartTime = 1.37f;

        public static DoorTransitionPose Evaluate(
            float unscaledElapsedTime)
        {
            float elapsedTime = SanitizeElapsedTime(
                unscaledElapsedTime);
            float normalizedTime = elapsedTime / TotalDuration;
            float handleTurn = SmoothRange(
                elapsedTime,
                HandleStartTime,
                HandleEndTime);
            float doorOpen = SmootherRange(
                elapsedTime,
                DoorOpenStartTime,
                DoorOpenEndTime);
            float cameraPush = SmootherRange(
                elapsedTime,
                CameraPushStartTime,
                CameraPushEndTime);
            float revealOpacity = 1f - SmoothRange(
                elapsedTime,
                RevealStartTime,
                RevealEndTime);
            float fadeOutOpacity = SmoothRange(
                elapsedTime,
                FadeOutStartTime,
                TotalDuration);

            return new DoorTransitionPose(
                elapsedTime,
                normalizedTime,
                handleTurn,
                doorOpen,
                cameraPush,
                Mathf.Max(revealOpacity, fadeOutOpacity),
                elapsedTime >= TotalDuration);
        }

        private static float SanitizeElapsedTime(float elapsedTime)
        {
            if (float.IsNaN(elapsedTime) || elapsedTime <= 0f)
            {
                return 0f;
            }

            if (float.IsPositiveInfinity(elapsedTime))
            {
                return TotalDuration;
            }

            return Mathf.Min(elapsedTime, TotalDuration);
        }

        private static float SmoothRange(
            float elapsedTime,
            float startTime,
            float endTime)
        {
            float amount = Mathf.InverseLerp(
                startTime,
                endTime,
                elapsedTime);
            return amount * amount * (3f - 2f * amount);
        }

        private static float SmootherRange(
            float elapsedTime,
            float startTime,
            float endTime)
        {
            float amount = Mathf.InverseLerp(
                startTime,
                endTime,
                elapsedTime);
            return amount *
                   amount *
                   amount *
                   (amount * (amount * 6f - 15f) + 10f);
        }
    }
}
