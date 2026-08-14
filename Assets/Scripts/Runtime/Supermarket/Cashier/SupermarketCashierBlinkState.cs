using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The cashier's rare, slow blink. One deterministic cycle much
    /// longer than the bus driver's; while the surveillance state
    /// suppresses blinking the cycle restarts from zero, so after the
    /// hero looks away the unbroken stare lasts a full cycle again.
    /// Rendering (renderer.forceRenderingOff) belongs to the
    /// presentation; this state owns closure timing only.
    /// </summary>
    public sealed class SupermarketCashierBlinkState
    {
        public const float CycleDurationSeconds = 6.5f;
        public const float CloseDurationSeconds = 0.09f;
        public const float HoldDurationSeconds = 0.16f;
        public const float OpenDurationSeconds = 0.14f;
        public const float BlinkStartSeconds =
            CycleDurationSeconds -
            (CloseDurationSeconds +
             HoldDurationSeconds +
             OpenDurationSeconds);
        public const float EyesClosedThreshold = 0.78f;

        private float cycleTime;

        /// <summary>0 = open, 1 = fully closed.</summary>
        public float Closure { get; private set; }

        public bool EyesClosed => Closure >= EyesClosedThreshold;

        public void Advance(float deltaTime, bool suppressed)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                return;
            }

            if (suppressed)
            {
                Reset();
                return;
            }

            cycleTime += Mathf.Max(0f, deltaTime);
            cycleTime %= CycleDurationSeconds;
            Closure = EvaluateClosure(cycleTime);
        }

        public void Reset()
        {
            cycleTime = 0f;
            Closure = 0f;
        }

        private static float EvaluateClosure(float time)
        {
            float blinkTime = time - BlinkStartSeconds;
            if (blinkTime <= 0f)
            {
                return 0f;
            }

            if (blinkTime < CloseDurationSeconds)
            {
                return SmoothStep01(
                    blinkTime / CloseDurationSeconds);
            }

            blinkTime -= CloseDurationSeconds;
            if (blinkTime < HoldDurationSeconds)
            {
                return 1f;
            }

            blinkTime -= HoldDurationSeconds;
            if (blinkTime < OpenDurationSeconds)
            {
                return 1f - SmoothStep01(
                    blinkTime / OpenDurationSeconds);
            }

            return 0f;
        }

        private static float SmoothStep01(float t)
        {
            float clamped = Mathf.Clamp01(t);
            return clamped * clamped * clamped *
                   (clamped * ((clamped * 6f) - 15f) + 10f);
        }
    }
}
