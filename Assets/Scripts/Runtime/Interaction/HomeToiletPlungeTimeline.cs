using System;

namespace BarPromenade
{
    public enum HomeToiletPlungePhase { Idle, Entering, SubmergedHold, Exiting, Completed }

    /// <summary>Owns camera travel only; this stage has no bathroom reward or flush.</summary>
    public sealed class HomeToiletPlungeTimeline
    {
        public const float EnterSeconds = 2.5f;
        public const float HoldSeconds = 3f;
        public const float ExitSeconds = 2.5f;
        private float exitStart = 1f;
        private float exitPeak = 1f;
        private float exitVelocity;
        private float exitAcceleration;
        private float brakeSeconds;
        private float returnSeconds = ExitSeconds;
        private float exitDuration = ExitSeconds;
        public HomeToiletPlungePhase Phase { get; private set; }
        public float PhaseElapsed { get; private set; }
        public bool WasCancelled { get; private set; }
        public bool IsCompleted => Phase == HomeToiletPlungePhase.Completed;
        public float Travel => Phase == HomeToiletPlungePhase.Entering
            ? Ease(PhaseElapsed / EnterSeconds)
            : Phase == HomeToiletPlungePhase.SubmergedHold ? 1f
            : Phase == HomeToiletPlungePhase.Exiting
                ? PhaseElapsed < brakeSeconds
                    ? BrakeTravel(PhaseElapsed / brakeSeconds)
                    : exitPeak * (1f - Ease((PhaseElapsed - brakeSeconds) / returnSeconds))
                : 0f;

        /// <summary>Path units per second, retained when a moving entry is cancelled.</summary>
        public float TravelVelocity
        {
            get
            {
                if (Phase == HomeToiletPlungePhase.Entering)
                    return EaseDerivative(PhaseElapsed / EnterSeconds) / EnterSeconds;
                if (Phase != HomeToiletPlungePhase.Exiting) return 0f;
                if (PhaseElapsed >= brakeSeconds)
                    return -exitPeak * EaseDerivative((PhaseElapsed - brakeSeconds) / returnSeconds) / returnSeconds;
                float u = PhaseElapsed / brakeSeconds;
                float remaining = 1f - u;
                return exitVelocity * (1f - Ease(u)) +
                    exitAcceleration * brakeSeconds * u * remaining * remaining * remaining;
            }
        }

        public void Begin() { Reset(); Phase = HomeToiletPlungePhase.Entering; }

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            float remaining = Math.Max(0f, seconds);
            while (remaining > 0f && Phase != HomeToiletPlungePhase.Idle && !IsCompleted)
            {
                float duration = Phase == HomeToiletPlungePhase.Entering ? EnterSeconds
                    : Phase == HomeToiletPlungePhase.SubmergedHold ? HoldSeconds : exitDuration;
                float step = Math.Min(remaining, duration - PhaseElapsed);
                PhaseElapsed += step;
                remaining -= step;
                if (PhaseElapsed < duration) break;
                PhaseElapsed = 0f;
                Phase++;
            }
        }

        public bool RequestFinish()
        {
            if (Phase != HomeToiletPlungePhase.Entering && Phase != HomeToiletPlungePhase.SubmergedHold)
                return false;
            exitStart = Travel;
            exitVelocity = TravelVelocity;
            exitAcceleration = Phase == HomeToiletPlungePhase.Entering
                ? EaseSecondDerivative(PhaseElapsed / EnterSeconds) / (EnterSeconds * EnterSeconds) : 0f;
            brakeSeconds = 0f;
            if (exitVelocity > .00001f && exitStart < 1f)
            {
                // Carry the current velocity/acceleration into a short deceleration,
                // instead of reversing a moving lens in one frame. Bound the remaining
                // run-on by the available path and keep the braking velocity positive.
                brakeSeconds = Math.Min(.22f, Math.Min(
                    .8f * (1f - exitStart) / exitVelocity,
                    .75f * exitVelocity / Math.Max(.00001f, Math.Abs(exitAcceleration))));
            }
            exitPeak = BrakeTravel(1f);
            returnSeconds = ExitSeconds * Math.Max(.20f, (float)Math.Sqrt(exitPeak));
            exitDuration = brakeSeconds + returnSeconds;
            WasCancelled = true;
            PhaseElapsed = 0f;
            Phase = HomeToiletPlungePhase.Exiting;
            return true;
        }

        public void Reset()
        {
            Phase = HomeToiletPlungePhase.Idle;
            PhaseElapsed = 0f;
            WasCancelled = false;
            exitStart = exitPeak = 1f;
            exitVelocity = exitAcceleration = brakeSeconds = 0f;
            returnSeconds = exitDuration = ExitSeconds;
        }

        private float BrakeTravel(float u)
        {
            float u2 = u * u;
            float u3 = u2 * u;
            float u4 = u3 * u;
            float u5 = u4 * u;
            float u6 = u5 * u;
            // Integral of a smooth velocity falloff with the captured initial
            // acceleration. Its terminal velocity and acceleration are both zero.
            return exitStart + brakeSeconds * (
                exitVelocity * (u - 2.5f * u4 + 3f * u5 - u6) +
                exitAcceleration * brakeSeconds * (.5f * u2 - u3 + .75f * u4 - .2f * u5));
        }

        public static float Ease(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float EaseDerivative(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return 30f * t * t * (1f - t) * (1f - t);
        }

        private static float EaseSecondDerivative(float value)
        {
            float t = Math.Max(0f, Math.Min(1f, value));
            return 60f * t * (1f - t) * (1f - 2f * t);
        }
    }
}
