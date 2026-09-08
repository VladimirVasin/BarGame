using UnityEngine;

namespace BarPromenade
{
    /// <summary>Continuous angular motion: accelerate below water, then unwind into the room shot.</summary>
    public sealed class HomeToiletFlushVortex
    {
        public const float HoldSeconds = 1f;
        public const float TotalDegrees = 720f;
        public const float Duration = HoldSeconds + HomeToiletPlungeTimeline.ExitSeconds;
        private const float PeakSpeed = TotalDegrees * 2f / Duration;
        public bool IsActive { get; private set; }
        public float Elapsed { get; private set; }
        public float RollDegrees => IsActive ? EvaluateRoll(Elapsed) : 0f;
        public float Strength => IsActive ? EvaluateSpeed(Elapsed) / PeakSpeed : 0f;

        public void Begin() { IsActive = true; Elapsed = 0f; }
        public void Advance(float deltaTime)
        {
            if (IsActive) Elapsed = Mathf.Min(Duration, Elapsed + Mathf.Max(0f, deltaTime));
        }
        public void Reset() { IsActive = false; Elapsed = 0f; }

        public static float EvaluateRoll(float seconds)
        {
            float t = Mathf.Clamp(seconds, 0f, Duration);
            if (t <= HoldSeconds) return PeakSpeed * HoldSeconds * IntegratedEase(t / HoldSeconds);
            float u = (t - HoldSeconds) / HomeToiletPlungeTimeline.ExitSeconds;
            return PeakSpeed * (HoldSeconds * .5f + HomeToiletPlungeTimeline.ExitSeconds * (u - IntegratedEase(u)));
        }

        public static float EvaluateSpeed(float seconds)
        {
            if (seconds <= 0f || seconds >= Duration) return 0f;
            return PeakSpeed * (seconds <= HoldSeconds ? HomeToiletPlungeTimeline.Ease(seconds / HoldSeconds) :
                1f - HomeToiletPlungeTimeline.Ease((seconds - HoldSeconds) / HomeToiletPlungeTimeline.ExitSeconds));
        }

        private static float IntegratedEase(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * t * t * (t * t - 3f * t + 2.5f);
        }

        public void ApplyCamera(float travel, Vector3 right, Vector3 forward, ref Vector3 position, ref Quaternion rotation)
        {
            if (!IsActive) return;
            float angle = RollDegrees * Mathf.Deg2Rad;
            float radius = .009f * Strength * HomeToiletPlungeTimeline.Ease(travel);
            position += right * ((Mathf.Cos(angle) - 1f) * radius) + forward * (Mathf.Sin(angle) * radius);
            // Keep unwrapped turns until the exact 720-degree endpoint; quaternion
            // interpolation would choose the shorter route and reverse the whirl.
            rotation *= Quaternion.AngleAxis(RollDegrees, Vector3.forward);
        }
    }
}
