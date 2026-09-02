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
        /// <summary>
        /// How long the neck takes to ease out toward the hero, and back.
        ///
        /// These are `SmoothDamp` times, not speeds, and that is the whole
        /// point: the chain used to run on `MoveTowards`, which is a constant
        /// velocity with a discontinuity at each end - the periscope jerked
        /// into motion and stopped dead at the cap. A critically damped
        /// approach has continuous velocity, so every size change on the neck
        /// eases in and eases out. Roughly matched to the old rates, which
        /// were tuned and worth keeping: extension used to cross its full
        /// range in about `1.1 s` and the guilty retract in about `0.3 s`.
        /// </summary>
        public const float ExtendSmoothTime = 0.55f;

        public const float RetractSmoothTime = 0.18f;
        public const float StartleEnterSmoothTime = 0.10f;
        public const float StartleExitSmoothTime = 0.35f;

        /// <summary>
        /// `SmoothDamp` approaches asymptotically and never quite lands, so
        /// anything inside this of the target is snapped. Small enough to be
        /// invisible, large enough that the neck actually settles instead of
        /// creeping for the rest of the scene.
        /// </summary>
        private const float SettleEpsilon = 0.0005f;

        /// <summary>
        /// Inside this the neck is an ORDINARY NECK. Walk up to the till
        /// and the periscope is simply gone: he is a slightly tall man
        /// behind a counter, and nothing about him is wrong until you
        /// back away again.
        ///
        /// That is the whole joke, and it only works if the retract is
        /// complete rather than partial - a neck that stays half out at
        /// arm's length reads as a bug in the reach, not as a man
        /// pretending. `2 m` is comfortably outside the counter itself,
        /// so it has finished retracting before the hero is close enough
        /// to use the till.
        /// </summary>
        public const float CloseRetractFullMeters = 2f;

        /// <summary>Beyond this he pursues at full stretch again; between
        /// the two it eases, so backing away pays the neck out
        /// smoothly.</summary>
        public const float CloseRetractReleaseMeters = 4f;

        public const float StartleEnterDotDegrees = 22f;
        public const float StartleExitDotDegrees = 30f;
        public const float StartleEnterHoldSeconds = 0.15f;
        public const float StartleExitHoldSeconds = 0.8f;
        public const float StartleExtensionCap = 0.30f;

        /// <summary>
        /// How long he stays pulled in after being caught, measured from the
        /// moment he was noticed.
        ///
        /// Without it the only gate was <see cref="StartleExitHoldSeconds"/>,
        /// so half a second of the hero turning away popped the periscope
        /// straight back out and the beat read as a twitch rather than as
        /// getting caught. Release now needs BOTH: the cooldown elapsed and
        /// the hero looking away for the exit hold. The two are deliberately
        /// not added together - a hero who keeps staring burns the cooldown
        /// while he stares, and only the last look-away has to be held.
        /// </summary>
        public const float StartleCooldownSeconds = 4f;

        public const float BlinkResumeDelaySeconds = 1.2f;
        public const float WideEyeBaseScale = 1.12f;
        public const float WideEyeStartleScale = 1.30f;

        private static readonly float StartleEnterDot =
            Mathf.Cos(StartleEnterDotDegrees * Mathf.Deg2Rad);
        private static readonly float StartleExitDot =
            Mathf.Cos(StartleExitDotDegrees * Mathf.Deg2Rad);

        private float startleEnterHeld;
        private float startleExitHeld;
        private float startleHeldSeconds;
        private float blinkResumeRemaining;
        private float extensionVelocity;
        private float startleVelocity;

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

            // How far out the neck wants to be for the range alone. The
            // face follows the hero across the shop, but it comes home as
            // he walks up: `distanceToPlayer` was passed into this method
            // from the first version and never read.
            float rangeTarget = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    CloseRetractFullMeters,
                    CloseRetractReleaseMeters,
                    distanceToPlayer));

            // Being caught and being close both pull him in, so the more
            // retracted of the two wins rather than one overriding the
            // other - a startle at the counter must not pay the neck back
            // OUT to the startle cap.
            float extensionTarget = IsStartled
                ? Mathf.Min(StartleExtensionCap, rangeTarget)
                : rangeTarget;
            Extension = Ease(
                Extension,
                extensionTarget,
                ref extensionVelocity,
                extensionTarget >= Extension
                    ? ExtendSmoothTime
                    : RetractSmoothTime,
                safeDeltaTime);

            StartleWeight = Ease(
                StartleWeight,
                IsStartled ? 1f : 0f,
                ref startleVelocity,
                IsStartled
                    ? StartleEnterSmoothTime
                    : StartleExitSmoothTime,
                safeDeltaTime);
        }

        /// <summary>
        /// One critically damped step, with the tail snapped off.
        ///
        /// A zero step must be a no-op rather than a divide, because
        /// <c>Update</c> is called with whatever the frame handed it.
        /// </summary>
        private static float Ease(
            float current,
            float target,
            ref float velocity,
            float smoothTime,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            float next = Mathf.SmoothDamp(
                current,
                target,
                ref velocity,
                smoothTime,
                Mathf.Infinity,
                deltaTime);
            if (Mathf.Abs(target - next) <= SettleEpsilon)
            {
                velocity = 0f;
                return target;
            }

            return next;
        }

        public void Reset()
        {
            Extension = 0f;
            StartleWeight = 0f;
            IsStartled = false;
            startleEnterHeld = 0f;
            startleExitHeld = 0f;
            startleHeldSeconds = 0f;
            blinkResumeRemaining = 0f;
            extensionVelocity = 0f;
            startleVelocity = 0f;
        }

        private void AdvanceStartle(
            float playerLookDot,
            float deltaTime)
        {
            if (IsStartled)
            {
                startleHeldSeconds += deltaTime;
                if (playerLookDot < StartleExitDot)
                {
                    startleExitHeld += deltaTime;

                    // Both gates, not either: he has to have been pulled in
                    // for the whole cooldown AND the hero has to have looked
                    // away long enough. The cooldown runs while he is being
                    // stared at too, so a long stare does not stack a second
                    // four seconds on top of itself.
                    if (startleExitHeld >= StartleExitHoldSeconds &&
                        startleHeldSeconds >= StartleCooldownSeconds)
                    {
                        IsStartled = false;
                        startleEnterHeld = 0f;
                        startleHeldSeconds = 0f;
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
                    startleHeldSeconds = 0f;
                }
            }
            else
            {
                startleEnterHeld = 0f;
            }
        }
    }
}
