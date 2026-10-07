using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float MaximumFacingSpeed = 150f;
        internal const float WindupFacingDegrees = 50f;
        internal const float KickFacingDegrees = 30f;
        internal const float EarlyFacingFraction = .25f;
        private float facingVelocity, plantedFacing;
        private MeleePhase facingPhase = MeleePhase.Ready;
        private int facingSequence = -1;
        private bool facingLocked;

        // The duel owns yaw, while the motor retains target-relative translation.
        // Presentation and contact previews only read this accepted world frame.
        private bool OwnsCombatFacing => contactTarget != null && !roundEnded && !winnerPresentationReleased;

        private void ResetCombatFacing()
        {
            facingVelocity = 0f;
            facingPhase = MeleePhase.Ready;
            facingSequence = -1;
            facingLocked = false;
            plantedFacing = transform.eulerAngles.y;
        }

        private void AdvanceCombatFacing(float seconds)
        {
            if (presentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused || seconds <= 0f)
                return;
            if (!OwnsCombatFacing || !contactTarget.isActiveAndEnabled || contactTarget.State.IsDefeated ||
                !IsAvailable || IsKnockedDown || State.IsDefeated ||
                (footwork?.RecoveryEpisodeActive ?? false) ||
                (ImpactMotion != null && ImpactMotion.IsActive && ImpactMotion.BalanceLoad > .45f))
            { facingVelocity = 0f; return; }

            // A rules tick can end the arc while still owing its last contact
            // interval. Recovery must not turn that final sample toward a new line.
            if ((collectSweep && sweepFrom < State.AttackActiveEnd && sweepTo >= State.AttackActiveEnd) ||
                State.Phase is MeleePhase.Step or MeleePhase.Shoving or MeleePhase.GuardImpact)
            { facingVelocity = 0f; return; }

            // A release gets one early aim window. Active and Recovery keep the
            // same planted frame; phase changes cannot grant another correction.
            if (facingSequence != State.AttackSequence ||
                !State.IsAttacking && !State.IsKicking ||
                facingPhase == MeleePhase.Charging && State.Phase == MeleePhase.Windup)
            {
                plantedFacing = transform.eulerAngles.y;
                facingSequence = State.AttackSequence;
                facingLocked = false;
            }
            facingPhase = State.Phase;
            float aimProgress = 0f;
            float aimRemaining = 0f;
            if (State.IsAttacking || State.IsKicking)
            {
                float elapsed = State.IsKicking ? State.KickElapsed : State.AttackElapsed;
                float windup = State.IsKicking ? State.Settings.KickWindupSeconds : State.AttackWindupSeconds;
                aimProgress = elapsed / (windup * EarlyFacingFraction);
                aimRemaining = windup * EarlyFacingFraction - elapsed;
                if (aimProgress >= 1f)
                {
                    facingVelocity = 0f;
                    if (!facingLocked)
                    {
                        facingLocked = true;
                        JournalEvent("aim_locked", contactTarget.JournalActorId, State.AttackSequence, journalActionRequest,
                            GameLog.Field("yaw", transform.eulerAngles.y),
                            GameLog.Field("opening_seconds", windup * EarlyFacingFraction),
                            GameLog.Field("action_kind", State.IsKicking ? "kick" : "weapon"));
                    }
                    return;
                }
            }
            // Aim at the actual rig, including its body lean, rather than a
            // controller centre that can stay behind the visible opponent.
            Vector3 direction = (contactTarget.ImpactMotion?.CentreOfMass ?? contactTarget.transform.position) -
                (ImpactMotion?.CentreOfMass ?? transform.position);
            direction.y = 0f;
            if (direction.sqrMagnitude < .0004f) { facingVelocity = 0f; return; }
            float desiredYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float speed = MaximumFacingSpeed * TurnScale;
            float limit = 180f;
            if (State.IsCharging) speed = MaximumFacingSpeed;
            else if (State.Phase == MeleePhase.Windup)
            {
                speed = MaximumFacingSpeed * (1f - Mathf.SmoothStep(0f, 1f, aimProgress));
                limit = WindupFacingDegrees;
            }
            else if (State.IsKicking)
            {
                speed = MaximumFacingSpeed * (1f - Mathf.SmoothStep(0f, 1f, aimProgress));
                limit = KickFacingDegrees;
            }
            if (limit < 180f)
                desiredYaw = plantedFacing + Mathf.Clamp(Mathf.DeltaAngle(plantedFacing, desiredYaw), -limit, limit);
            float bearing = Mathf.DeltaAngle(transform.eulerAngles.y, desiredYaw);
            // Release may inherit charge rotation. Spend that velocity through
            // finite braking inside the opening, rather than clipping it to a
            // shrinking speed cap or carrying it into the committed strike.
            float braking = State.IsAttacking || State.IsKicking
                ? Mathf.Max(900f, Mathf.Abs(facingVelocity) * 2f / Mathf.Max(seconds, aimRemaining)) : 900f;
            float yaw = PlayerMotor.AdvanceInertialYaw(bearing, speed, seconds, ref facingVelocity, true, braking);
            if (limit < 180f)
            {
                float current = Mathf.DeltaAngle(plantedFacing, transform.eulerAngles.y);
                float accepted = Mathf.Clamp(current + yaw, -limit, limit) - current;
                // Reversing a moving target cannot carry momentum outside the
                // planted support envelope while the body brakes to change sides.
                if (Mathf.Abs(accepted - yaw) > .00001f) facingVelocity = 0f;
                yaw = accepted;
            }
            transform.Rotate(0f, yaw, 0f);
        }
    }
}
