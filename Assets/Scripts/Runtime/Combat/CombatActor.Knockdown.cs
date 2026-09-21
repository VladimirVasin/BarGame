using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private CombatRecoveryPose knockdownPose;
        private PlayerRagdollLyingPose knockdownLying;
        private bool knockedDown, knockdownFrozen, recoveryPoseBegun, recoveryRegrip;

        /// <summary>Includes physical fall, lying, recovery and the actual supporting regrip.</summary>
        public bool IsKnockedDown => knockedDown;

        internal bool TryBeginKnockdown(CombatImpact impact, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (knockedDown || State.IsDefeated || Ragdoll == null || IsRagdollActive || weaponDropped) return false;
            knockdownPose ??= new CombatRecoveryPose(transform, DamageRigRoot, Body, Ragdoll, handPose, hero == null);
            hero?.SetOwnedPresentationFrozen(this, false);
            if (!Ragdoll.BeginKnockdown(linearVelocity, angularVelocity)) return false;
            weaponConstraint?.Forget();
            EnableHeldWeaponPhysics();
            // Physics has captured the final pose. No saved additive base may restore the
            // pre-hit stance after this point, including during cancellation or a repeat hit.
            supportGrip?.Forget();
            footwork?.Forget();
            damagePose?.ForgetBase();
            bodyMotion?.Forget();
            ImpactMotion?.Forget();
            CancelPoseBlend();
            supportGrip?.SetRecoveryOwned(true);
            supportGrip?.AllowRegrip(false);
            State.BeginKnockdown();
            knockedDown = true;
            recoveryPoseBegun = recoveryRegrip = false;
            reaction = null;
            sweepValid = collectSweep = false;
            return true;
        }

        private bool AdvanceKnockdown(float seconds)
        {
            if (!knockedDown) return false;
            sweepValid = collectSweep = false;
            if (State.IsDefeated) { PromoteKnockdownToDefeat(); return true; }
            if (knockdownFrozen || PauseMenuController.IsAnyPaused || seconds <= 0f) return true;
            poseClock += seconds;
            if ((recoveryPoseBegun || State.Phase == MeleePhase.Rising) && !Ragdoll.IsRecovering)
            {
                // AddImpact already gave the live rising pose to physics. Build the next
                // timeline without restoring the interrupted clip or finishing an old regrip.
                knockdownPose?.Dispose();
                knockdownPose = null;
                recoveryPoseBegun = recoveryRegrip = false;
                supportGrip?.SetRecoveryOwned(true);
                supportGrip?.AllowRegrip(false);
                State.BeginKnockdown();
            }
            if (!Ragdoll.IsRecovering)
            {
                if (!Ragdoll.IsSettled) return true;
                if (!Ragdoll.BeginRecovery(out knockdownLying)) return true;
                DisableHeldWeaponPhysics();
                weaponConstraint?.Forget();
                State.BeginRise();
            }
            knockdownPose ??= new CombatRecoveryPose(transform, DamageRigRoot, Body, Ragdoll, handPose, hero == null);
            if (!recoveryPoseBegun)
            {
                if (!knockdownPose.Begin(knockdownLying)) return true;
                recoveryPoseBegun = true;
                knockdownPose.Present();
            }
            knockdownPose.Advance(seconds);
            supportGrip?.Restore();
            weaponConstraint?.Restore();
            knockdownPose.Present(recoveryRegrip);
            weaponConstraint?.Apply();
            if (weaponConstraint != null && weaponConstraint.MotionBlocked)
            { knockdownPose.RejectAdvance(seconds); return true; }
            if (!recoveryRegrip && knockdownPose.HandsReleased)
            {
                // The boots already accept the weight. Regrip overlaps the last authored
                // rise arc, starting at this hand's live knee pose, without a second dwell.
                supportGrip?.SetRecoveryOwned(false);
                supportGrip?.SetTarget(false, true);
                supportGrip?.AllowRegrip(true);
                recoveryRegrip = true;
            }
            if (recoveryRegrip) supportGrip?.Advance(seconds);
            PresentKnockdown();
            if (weaponConstraint != null && weaponConstraint.MotionBlocked)
            { knockdownPose.RejectAdvance(seconds); return true; }
            if (!knockdownPose.IsComplete || !recoveryRegrip) return true;
            if (supportGrip != null && !supportGrip.IsSupportingWeapon) return true;
            if (!knockdownPose.HandsReleased || !knockdownPose.HasStandingClearance()) return true;
            FinishKnockdown();
            return true;
        }

        private bool PresentKnockdown()
        {
            if (!knockedDown) return false;
            if (recoveryPoseBegun && Ragdoll.IsRecovering)
            {
                supportGrip?.Restore();
                weaponConstraint?.Restore();
                knockdownPose.Present(recoveryRegrip);
                weaponConstraint?.Apply();
                if (recoveryRegrip && (weaponConstraint == null || !weaponConstraint.MotionBlocked)) supportGrip?.Apply();
                weaponConstraint?.CommitPresentedPose(supportGrip);
                visibleClip = knockdownPose.ClipName;
            }
            handPose?.SetGrip(false, 1f);
            if (!recoveryRegrip) handPose?.SetGrip(true, 0f);
            return true;
        }

        private void FinishKnockdown()
        {
            // The final standing/regripped pose stays on screen while the ordinary combat
            // owner resumes. Its normal transition starts from this pose, with the new root.
            knockdownPose.Dispose();
            knockdownPose = null;
            Ragdoll.FinishRecovery();
            if (hero != null) hero.RememberOwnedRecoveryPose(this, poseClock);
            else RememberNpcPresentedPose();
            State.EndKnockdown();
            knockedDown = knockdownFrozen = recoveryPoseBegun = recoveryRegrip = false;
            FinishImpactRecovery();
            BeginPoseBlend(.16f);
        }

        private void PromoteKnockdownToDefeat()
        {
            if (!knockedDown) return;
            // Defeat retains this body's live pose and momentum; replaying CombatDefeat
            // would first pull a lying or rising character upright.
            knockdownPose?.Dispose();
            knockdownPose = null;
            Ragdoll.MakeTerminal();
            supportGrip?.SetRecoveryOwned(true);
            knockedDown = recoveryPoseBegun = recoveryRegrip = false;
            sweepValid = collectSweep = false;
            DropWeapon();
        }

        private void ResetKnockdown()
        {
            DisableHeldWeaponPhysics();
            weaponConstraint?.Forget();
            knockdownPose?.Dispose();
            knockdownPose = null;
            if (knockedDown && !State.IsDefeated) State.EndKnockdown();
            knockedDown = knockdownFrozen = recoveryPoseBegun = recoveryRegrip = false;
            supportGrip?.SetRecoveryOwned(false);
            supportGrip?.AllowRegrip(true);
        }

        private void SetKnockdownFrozen(bool frozen)
        {
            knockdownFrozen = frozen;
            Ragdoll?.SetFrozen(frozen);
        }
    }
}
