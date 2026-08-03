using System;

namespace BarPromenade
{
    /// <summary>
    /// Pure frame-rate-independent state for the cat's one-shot feeding
    /// sequence. The last frame remains visible for one complete frame step.
    /// </summary>
    public sealed class StairwellCatFeedingTimeline
    {
        public const int FrameCount =
            StairwellCatFeedingSpriteLibrary.FrameCount;
        public const float FramesPerSecond = 6f;
        public const double DurationSeconds =
            FrameCount / (double)FramesPerSecond;

        private double elapsedSeconds;

        public bool IsActive { get; private set; }
        public int FrameIndex { get; private set; } = -1;
        public double ElapsedSeconds => elapsedSeconds;
        public float Progress =>
            IsActive
                ? (float)Math.Min(
                    1d,
                    elapsedSeconds / DurationSeconds)
                : 0f;

        public bool Begin()
        {
            if (IsActive)
            {
                return false;
            }

            IsActive = true;
            elapsedSeconds = 0d;
            FrameIndex = 0;
            return true;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime),
                    deltaTime,
                    "Delta time must be finite and non-negative.");
            }

            if (!IsActive || deltaTime <= 0f)
            {
                return;
            }

            elapsedSeconds += deltaTime;
            if (elapsedSeconds >= DurationSeconds)
            {
                Complete();
                return;
            }

            FrameIndex = Math.Min(
                FrameCount - 1,
                (int)Math.Floor(
                    elapsedSeconds * FramesPerSecond));
        }

        public bool Complete()
        {
            return End();
        }

        public bool Cancel()
        {
            return End();
        }

        public void Reset()
        {
            IsActive = false;
            elapsedSeconds = 0d;
            FrameIndex = -1;
        }

        private bool End()
        {
            if (!IsActive)
            {
                return false;
            }

            Reset();
            return true;
        }
    }
}
