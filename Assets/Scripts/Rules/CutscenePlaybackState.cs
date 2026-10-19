using System;

namespace BarPromenade
{
    /// <summary>The presentation and its debug readout sample this one clock.</summary>
    public sealed class CutscenePlaybackState
    {
        public CutscenePlaybackState(double durationSeconds)
        {
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) ||
                durationSeconds <= 0d || durationSeconds > 86400d)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DurationSeconds = durationSeconds;
        }

        public double DurationSeconds { get; }
        public double ElapsedSeconds { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsComplete => ElapsedSeconds >= DurationSeconds;

        public void TogglePause() => IsPaused = !IsPaused;

        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!IsPaused) ElapsedSeconds = Math.Min(DurationSeconds, ElapsedSeconds + seconds);
        }
    }
}
