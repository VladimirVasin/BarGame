using System;
using UnityEngine;

namespace BarPromenade
{
    public enum PlayerFallAnimationPhase
    {
        None = 0,
        Falling,
        Down,
        Rising
    }

    /// <summary>
    /// Maps the balance fall phases onto one authored 100-frame sequence.
    /// The controller owns unscaled time; the visual rig only receives an
    /// explicit phase and normalized progress.
    /// </summary>
    public static class PlayerFallAnimationTimeline
    {
        public const int FallingFrameCount = 14;
        public const int DownFrameCount = 36;
        public const int RisingFrameCount = 50;
        public const int FrameCount =
            FallingFrameCount + DownFrameCount + RisingFrameCount;

        public static int EvaluateFrame(
            PlayerFallAnimationPhase phase,
            float normalizedProgress)
        {
            switch (phase)
            {
                case PlayerFallAnimationPhase.None:
                    return -1;
                case PlayerFallAnimationPhase.Falling:
                    return EvaluateRange(
                        0,
                        FallingFrameCount,
                        normalizedProgress);
                case PlayerFallAnimationPhase.Down:
                    return EvaluateRange(
                        FallingFrameCount,
                        DownFrameCount,
                        normalizedProgress);
                case PlayerFallAnimationPhase.Rising:
                    return EvaluateRange(
                        FallingFrameCount + DownFrameCount,
                        RisingFrameCount,
                        normalizedProgress);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(phase),
                        phase,
                        "Unknown player fall animation phase.");
            }
        }

        private static int EvaluateRange(
            int firstFrame,
            int frameCount,
            float normalizedProgress)
        {
            float progress = Mathf.Clamp01(normalizedProgress);
            int localFrame = Mathf.Min(
                frameCount - 1,
                Mathf.FloorToInt(progress * frameCount));
            return firstFrame + localFrame;
        }
    }
}
