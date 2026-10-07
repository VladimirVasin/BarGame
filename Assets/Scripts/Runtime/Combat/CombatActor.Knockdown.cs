using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private static readonly ProfilerMarker RecoveryAdvanceMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Advance");
        private static readonly ProfilerMarker RecoveryPresentMarker = new ProfilerMarker("BarPromenade.CombatRecovery.Present");
        private static readonly ProfilerMarker RecoveryWeaponMarker = new ProfilerMarker("BarPromenade.CombatRecovery.WeaponApply");
        private static readonly ProfilerMarker RecoveryCommitMarker = new ProfilerMarker("BarPromenade.CombatRecovery.WeaponCommit");
        private CombatRecoveryPose knockdownPose;
        private PlayerRagdollLyingPose knockdownLying;
        private bool knockedDown, knockdownFrozen, recoveryPoseBegun, recoveryRegrip;
        private string journalClearanceReason, journalClearanceStage;
        private Collider journalClearanceObstacle;
        private int journalClearanceCount;
        private float journalClearanceNext;

        /// <summary>Includes physical fall, lying, recovery and the actual supporting regrip.</summary>
        public bool IsKnockedDown => knockedDown;

        internal bool TryBeginKnockdown(CombatImpact impact, Vector3 linearVelocity, Vector3 angularVelocity)
        {
            if (knockedDown) return JournalKnockdownRejected("already_knocked_down");
            if (State.IsDefeated) return JournalKnockdownRejected("defeated");
            if (Ragdoll == null) return JournalKnockdownRejected("ragdoll_missing");
            if (IsRagdollActive) return JournalKnockdownRejected("ragdoll_active");
            knockdownPose ??= new CombatRecoveryPose(transform, DamageRigRoot, Body, Ragdoll, handPose, hero == null, Weapon.transform);
            hero?.SetOwnedPresentationFrozen(this, false);
            if (!Ragdoll.BeginKnockdown(linearVelocity, angularVelocity)) return JournalKnockdownRejected("ragdoll_begin_refused");
            ForgetPistolAimPose();
            journalFallReason = journalRiseReason = null;
            ResetRecoveryEscape();
            ResetRiseClearanceJournal();
            JournalEvent("knockdown_started", f0: GameLog.Field("impact_seq", LastJournalImpactSequence),
                f1: GameLog.Field("velocity_x", linearVelocity.x), f2: GameLog.Field("velocity_y", linearVelocity.y), f3: GameLog.Field("velocity_z", linearVelocity.z),
                f4: GameLog.Field("angular_x", angularVelocity.x), f5: GameLog.Field("angular_y", angularVelocity.y), f6: GameLog.Field("angular_z", angularVelocity.z));
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
            if (IsPistol) ReleaseWeapon(linearVelocity * .25f, angularVelocity * .25f);
            knockedDown = true;
            recoveryPoseBegun = recoveryRegrip = false;
            reaction = null;
            sweepValid = collectSweep = false;
            collectShove = false;
            ImpactMotion.CancelRecoveryStep();
            ImpactMotion.ClearHandSupport();
            return true;
        }

        private bool AdvanceKnockdown(float seconds)
        {
            if (!knockedDown) return false;
            using var marker = RecoveryAdvanceMarker.Auto();
            sweepValid = collectSweep = false;
            if (State.IsDefeated) { PromoteKnockdownToDefeat(); return true; }
            if (knockdownFrozen || PauseMenuController.IsAnyPaused || seconds <= 0f) return true;
            poseClock += seconds;
            if ((recoveryPoseBegun || State.Phase == MeleePhase.Rising) && !Ragdoll.IsRecovering)
            {
                JournalEvent("rise_interrupted", f0: GameLog.Field("reason", "ragdoll_resumed"), f1: GameLog.Field("impact_seq", LastJournalImpactSequence));
                // AddImpact already gave the live rising pose to physics. Build the next
                // timeline without restoring the interrupted clip or finishing an old regrip.
                knockdownPose?.Dispose();
                knockdownPose = null;
                recoveryPoseBegun = recoveryRegrip = false;
                ResetRecoveryEscape();
                supportGrip?.SetRecoveryOwned(true);
                supportGrip?.AllowRegrip(false);
                State.BeginKnockdown();
            }
            if (!Ragdoll.IsRecovering)
            {
                if (!Ragdoll.IsSettled) return JournalRiseWait("ragdoll_unsettled");
                if (!Ragdoll.BeginRecovery(out knockdownLying)) return JournalRiseWait("ragdoll_recovery_refused");
                DisableHeldWeaponPhysics();
                weaponConstraint?.Forget();
                if (!weaponDropped) weaponConstraint?.BeginRecoveryContact();
                State.BeginRise();
                JournalEvent("rise_started", f0: GameLog.Field("impact_seq", LastJournalImpactSequence));
            }
            knockdownPose ??= new CombatRecoveryPose(transform, DamageRigRoot, Body, Ragdoll, handPose, hero == null, Weapon.transform);
            if (!recoveryPoseBegun)
            {
                if (!knockdownPose.Begin(knockdownLying))
                { JournalRiseClearance("begin"); return JournalRiseWait("recovery_pose_begin_refused"); }
                recoveryPoseBegun = true;
                knockdownPose.Present();
            }
            knockdownPose.Advance(seconds);
            supportGrip?.Restore();
            weaponConstraint?.Restore();
            knockdownPose.Present(recoveryRegrip);
            using (RecoveryWeaponMarker.Auto()) weaponConstraint?.Apply();
            if (weaponConstraint != null && weaponConstraint.MotionBlocked)
            { knockdownPose.RejectAdvance(seconds); return WaitForRecoveryWeapon(seconds); }
            if (!weaponDropped && !recoveryRegrip && knockdownPose.HandsReleased)
            {
                // The boots already accept the weight. Regrip overlaps the last authored
                // rise arc, starting at this hand's live knee pose, without a second dwell.
                supportGrip?.SetRecoveryOwned(false);
                supportGrip?.SetTarget(false, true);
                supportGrip?.AllowRegrip(true);
                recoveryRegrip = true;
            }
            if (recoveryRegrip) supportGrip?.Advance(seconds);
            // This call still has the authored pose and constrained right arm
            // from above. Advancing the support reach changes its state, not
            // those bones; apply the left arm and validate the final contact.
            PresentKnockdown(preparedForThisStep: true);
            if (weaponConstraint != null && weaponConstraint.MotionBlocked)
            { knockdownPose.RejectAdvance(seconds); return WaitForRecoveryWeapon(seconds); }
            recoveryBlockedSeconds = 0f;
            if (!knockdownPose.IsComplete) return JournalRiseWait("pose_incomplete");
            // Standing belongs to the body. A missing or unreachable supporting
            // grip can keep guard unavailable, but cannot imprison a living actor.
            if (!knockdownPose.HandsReleased) return JournalRiseWait("hands_not_released");
            if (!knockdownPose.HasStandingClearance())
            { JournalRiseClearance("finish"); return JournalRiseWait("standing_clearance"); }
            FinishKnockdown();
            return true;
        }

        private bool PresentKnockdown(bool preparedForThisStep = false)
        {
            if (!knockedDown) return false;
            using var marker = RecoveryPresentMarker.Auto();
            if (recoveryPoseBegun && Ragdoll.IsRecovering)
            {
                // Only the synchronous Advance caller can reuse this work.
                // Every external presentation rechecks the current world/pose.
                if (!preparedForThisStep)
                {
                    supportGrip?.Restore();
                    weaponConstraint?.Restore();
                    knockdownPose.Present(recoveryRegrip);
                    using (RecoveryWeaponMarker.Auto()) weaponConstraint?.Apply();
                }
                if (recoveryRegrip && (weaponConstraint == null || !weaponConstraint.MotionBlocked)) supportGrip?.Apply();
                using (RecoveryCommitMarker.Auto()) weaponConstraint?.CommitPresentedPose(supportGrip);
                visibleClip = knockdownPose.ClipName;
            }
            handPose?.SetGrip(false, weaponDropped ? 0f : 1f);
            if (!recoveryRegrip) handPose?.SetGrip(true, 0f);
            return true;
        }

        private void FinishKnockdown()
        {
            JournalEvent("rise_completed", f0: GameLog.Field("impact_seq", LastJournalImpactSequence));
            journalRiseReason = null;
            ResetRecoveryEscape();
            ResetRiseClearanceJournal();
            // The final standing/regripped pose stays on screen while the ordinary combat
            // owner resumes. Its normal transition starts from this pose, with the new root.
            knockdownPose.Dispose();
            knockdownPose = null;
            Ragdoll.FinishRecovery();
            if (hero != null) hero.RememberOwnedRecoveryPose(this, poseClock);
            else RememberNpcPresentedPose();
            State.EndKnockdown();
            knockedDown = knockdownFrozen = recoveryPoseBegun = recoveryRegrip = false;
            supportGrip?.SetRecoveryOwned(false);
            supportGrip?.AllowRegrip(!weaponDropped);
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
            ResetRecoveryEscape();
            ResetRiseClearanceJournal();
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

        private void ResetRiseClearanceJournal()
        {
            journalClearanceReason = journalClearanceStage = null;
            journalClearanceObstacle = null; journalClearanceCount = 0; journalClearanceNext = 0f;
        }

        private void JournalRiseClearance(string stage)
        {
            if (Journal == null || knockdownPose == null || knockdownPose.ClearanceRefusalCount == 0) return;
            CombatRecoveryPose.ClearanceRefusal refusal = knockdownPose.GetClearanceRefusal(0);
            if (journalClearanceStage == stage && journalClearanceReason == refusal.Reason &&
                journalClearanceObstacle == refusal.Obstacle && journalClearanceCount == knockdownPose.ClearanceRefusalCount &&
                poseClock < journalClearanceNext) return;
            journalClearanceStage = stage; journalClearanceReason = refusal.Reason;
            journalClearanceObstacle = refusal.Obstacle; journalClearanceCount = knockdownPose.ClearanceRefusalCount;
            journalClearanceNext = poseClock + 1f;
            // The desired support is the first refusal; the bounded alternatives
            // remain available as exact diagnostic records without per-tick output.
            Collider obstacle = refusal.Obstacle;
            string category = obstacle != null ? obstacle.GetComponentInParent<CombatActor>() != null ? "combat_actor" :
                obstacle.attachedRigidbody != null ? "dynamic_body" : "world" :
                refusal.Reason.EndsWith("buffer_full", System.StringComparison.Ordinal) ? "query_capacity" : "floor";
            long sequence = JournalEvent("rise_clearance_blocked", action: State.AttackSequence,
                f0: GameLog.Field("stage", stage), f1: GameLog.Field("reason", refusal.Reason),
                f2: GameLog.Field("category", category), f3: GameLog.Field("collider_path", RecoveryColliderPath(obstacle)),
                f4: GameLog.Field("collider_type", obstacle != null ? obstacle.GetType().Name : null),
                f5: GameLog.Field("collider_id", obstacle != null ? obstacle.GetEntityId().GetHashCode() : 0),
                f6: GameLog.Field("candidate_index", refusal.CandidateIndex), f7: GameLog.Field("candidates_checked", journalClearanceCount));
            int capsuleRefusals = 0, pathRefusals = 0, floorRefusals = 0, capacityRefusals = 0;
            for (int i = 0; i < journalClearanceCount; i++)
            {
                string reason = knockdownPose.GetClearanceRefusal(i).Reason;
                if (reason.EndsWith("buffer_full", System.StringComparison.Ordinal)) capacityRefusals++;
                else if (reason.StartsWith("capsule", System.StringComparison.Ordinal)) capsuleRefusals++;
                else if (reason.StartsWith("path", System.StringComparison.Ordinal)) pathRefusals++;
                else floorRefusals++;
            }
            CombatRecoveryPose.ClearanceRefusal last = knockdownPose.GetClearanceRefusal(journalClearanceCount - 1);
            JournalEvent("rise_clearance_search", action: State.AttackSequence,
                f0: GameLog.Field("refusal_seq", sequence), f1: GameLog.Field("capsule_rejections", capsuleRefusals),
                f2: GameLog.Field("path_rejections", pathRefusals), f3: GameLog.Field("floor_rejections", floorRefusals),
                f4: GameLog.Field("capacity_rejections", capacityRefusals), f5: GameLog.Field("last_candidate", last.CandidateIndex),
                f6: GameLog.Field("last_reason", last.Reason), f7: GameLog.Field("last_collider_path", RecoveryColliderPath(last.Obstacle)));
            JournalEvent("rise_clearance_candidate", action: State.AttackSequence,
                f0: GameLog.Field("refusal_seq", sequence), f1: GameLog.Field("x", refusal.Candidate.x),
                f2: GameLog.Field("y", refusal.Candidate.y), f3: GameLog.Field("z", refusal.Candidate.z));
            JournalEvent("rise_clearance_path", action: State.AttackSequence,
                f0: GameLog.Field("refusal_seq", sequence), f1: GameLog.Field("tested", refusal.PathTested),
                f2: GameLog.Field("from_x", refusal.PathFrom.x), f3: GameLog.Field("from_y", refusal.PathFrom.y), f4: GameLog.Field("from_z", refusal.PathFrom.z),
                f5: GameLog.Field("to_x", refusal.PathTo.x), f6: GameLog.Field("to_y", refusal.PathTo.y), f7: GameLog.Field("to_z", refusal.PathTo.z));
            JournalEvent("rise_clearance_capsule", action: State.AttackSequence,
                f0: GameLog.Field("refusal_seq", sequence), f1: GameLog.Field("top_x", refusal.CapsuleTop.x),
                f2: GameLog.Field("top_y", refusal.CapsuleTop.y), f3: GameLog.Field("top_z", refusal.CapsuleTop.z),
                f4: GameLog.Field("bottom_x", refusal.CapsuleBottom.x), f5: GameLog.Field("bottom_y", refusal.CapsuleBottom.y),
                f6: GameLog.Field("bottom_z", refusal.CapsuleBottom.z), f7: GameLog.Field("radius", refusal.CapsuleRadius));
            JournalEvent("rise_clearance_floor", action: State.AttackSequence,
                f0: GameLog.Field("refusal_seq", sequence), f1: GameLog.Field("tested", refusal.FloorTested),
                f2: GameLog.Field("collider_path", RecoveryColliderPath(refusal.Floor)), f3: GameLog.Field("x", refusal.FloorPoint.x),
                f4: GameLog.Field("y", refusal.FloorPoint.y), f5: GameLog.Field("z", refusal.FloorPoint.z),
                f6: GameLog.Field("normal_y", refusal.FloorNormal.y), f7: GameLog.Field("capsule_tested", refusal.CapsuleTested));
        }

        private static string RecoveryColliderPath(Collider collider)
        {
            if (collider == null) return null;
            string path = collider.name;
            for (Transform parent = collider.transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }
    }
}
