using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure surveillance logic for the long-necked supermarket cashier.
    /// While the hero is anywhere in the shop the pursuit weight rises
    /// toward one — the presentation stretches the neck to carry the
    /// face after him; being looked at triggers a fast guilty retract
    /// with enter/exit hysteresis that also pinches the pupils and
    /// suppresses blinking. The state owns numbers only — the
    /// presentation decides how the neck chain renders them.
    /// </summary>
    public sealed class SupermarketCashierSurveillanceState
    {
        public const float ExtendSpeedPerSecond = 0.9f;
        public const float RetractSpeedPerSecond = 2.4f;
        public const float StartleEnterDotDegrees = 22f;
        public const float StartleExitDotDegrees = 30f;
        public const float StartleEnterHoldSeconds = 0.15f;
        public const float StartleExitHoldSeconds = 0.8f;
        public const float StartleExtensionCap = 0.30f;
        public const float StartleEnterSpeedPerSecond = 6f;
        public const float StartleExitSpeedPerSecond = 1.5f;
        public const float BlinkResumeDelaySeconds = 1.2f;
        public const float WideEyeBaseScale = 1.12f;
        public const float WideEyeStartleScale = 1.30f;

        private static readonly float StartleEnterDot =
            Mathf.Cos(StartleEnterDotDegrees * Mathf.Deg2Rad);
        private static readonly float StartleExitDot =
            Mathf.Cos(StartleExitDotDegrees * Mathf.Deg2Rad);

        private float startleEnterHeld;
        private float startleExitHeld;
        private float blinkResumeRemaining;

        public SupermarketCashierSurveillanceState()
        {
            Reset();
        }

        /// <summary>Smoothed 0..1 pursuit weight.</summary>
        public float Extension { get; private set; }

        /// <summary>Smoothed 0..1 caught-looking weight.</summary>
        public float StartleWeight { get; private set; }

        public bool IsStartled { get; private set; }

        public bool ScanFrozen => IsStartled;

        public bool BlinkSuppressed =>
            IsStartled || blinkResumeRemaining > 0f;

        public float WideEyeScale => Mathf.Lerp(
            WideEyeBaseScale,
            WideEyeStartleScale,
            StartleWeight);

        /// <param name="distanceToPlayer">
        /// Planar distance from the cashier to the hero's body.
        /// </param>
        /// <param name="playerLookDot">
        /// Dot of the hero's planar forward against the planar
        /// direction from the hero to the cashier; 1 means the hero
        /// faces the cashier dead on.
        /// </param>
        public void Update(
            float distanceToPlayer,
            float playerLookDot,
            float deltaTime)
        {
            if (float.IsNaN(distanceToPlayer) ||
                float.IsInfinity(distanceToPlayer) ||
                float.IsNaN(playerLookDot) ||
                float.IsInfinity(playerLookDot) ||
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime))
            {
                return;
            }

            float safeDeltaTime = Mathf.Max(0f, deltaTime);
            AdvanceStartle(playerLookDot, safeDeltaTime);

            // The face follows the hero anywhere in the shop; only a
            // caught glance reels the neck back in.
            float extensionTarget = IsStartled
                ? StartleExtensionCap
                : 1f;
            float extensionSpeed = extensionTarget >= Extension
                ? ExtendSpeedPerSecond
                : RetractSpeedPerSecond;
            Extension = Mathf.MoveTowards(
                Extension,
                extensionTarget,
                extensionSpeed * safeDeltaTime);

            float startleSpeed = IsStartled
                ? StartleEnterSpeedPerSecond
                : StartleExitSpeedPerSecond;
            StartleWeight = Mathf.MoveTowards(
                StartleWeight,
                IsStartled ? 1f : 0f,
                startleSpeed * safeDeltaTime);
        }

        public void Reset()
        {
            Extension = 0f;
            StartleWeight = 0f;
            IsStartled = false;
            startleEnterHeld = 0f;
            startleExitHeld = 0f;
            blinkResumeRemaining = 0f;
        }

        private void AdvanceStartle(
            float playerLookDot,
            float deltaTime)
        {
            if (IsStartled)
            {
                if (playerLookDot < StartleExitDot)
                {
                    startleExitHeld += deltaTime;
                    if (startleExitHeld >= StartleExitHoldSeconds)
                    {
                        IsStartled = false;
                        startleEnterHeld = 0f;
                        blinkResumeRemaining =
                            BlinkResumeDelaySeconds;
                    }
                }
                else
                {
                    startleExitHeld = 0f;
                }

                return;
            }

            blinkResumeRemaining = Mathf.Max(
                0f,
                blinkResumeRemaining - deltaTime);
            if (playerLookDot > StartleEnterDot)
            {
                startleEnterHeld += deltaTime;
                if (startleEnterHeld >= StartleEnterHoldSeconds)
                {
                    IsStartled = true;
                    startleExitHeld = 0f;
                }
            }
            else
            {
                startleEnterHeld = 0f;
            }
        }
    }
}
