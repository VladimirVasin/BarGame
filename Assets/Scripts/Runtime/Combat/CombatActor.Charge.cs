using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        /// <summary>One swing side's clips; the rules' committed side selects the set.</summary>
        private struct SwingClips { public AnimationClip Attack, ReleaseLight, ReleaseHeavy, Charge, Recoil; }
        private readonly SwingClips[] swings = new SwingClips[2];
        private SwingClips Current => swings[(int)State.Swing];
        private AnimationClip ReleaseClip => State.AttackPower > 0f ? Current.ReleaseLight : Current.Attack;
        private bool IsRecoil(AnimationClip clip) => clip != null && (clip == swings[0].Recoil || clip == swings[1].Recoil);

        public bool RequestCharge()
        {
            if (IsFirearm || IsZombie) return false;
            int request = JournalCommand("charge");
            if (!CombatFocused && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "unfocused");
            if (roundEnded && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (!State.IsAttacking && BodyDamage.CanUseLeftHand && CheckShoveRange(request)) return TryBeginShove(request);
            if (!CanAttemptUpperBodyAttack) return JournalCommandResult(request, "rejected", UpperBodyAttackRejection);
            if (!State.RequestCharge(true)) return JournalRulesRejected(request, State.Settings.AttackCost);
            CancelPendingKick("replaced");
            ContinueBufferedAttackAfterContacts();
            if (State.IsCharging) { reaction = null; sweepValid = false; }
            ResumeFinishedBodyAttack();
            Present();
            return JournalCommandResult(request, State.IsCharging ? "started" : "queued", "charge");
        }

        public bool ReleaseCharge()
        {
            if (IsFirearm) return false;
            int request = JournalCommand("charge_release");
            if (!CombatFocused && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "unfocused");
            if (roundEnded && !CanAttackFinishedBody) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (weaponDropped) return JournalCommandResult(request, "rejected", "weapon_missing");
            if (State.HasBufferedCharge)
            {
                // Releasing a queued click never cancels its preceding swing,
                // even when that swing temporarily displaced the support hand.
                State.ReleaseCharge();
                bool started = ContinueBufferedAttackAfterContacts();
                return JournalCommandResult(request, started ? "started" : "queued", "charge_release");
            }
            if (State.IsCharging && BodyDamage.CanUseLeftHand && CheckShoveRange(request)) return TryBeginShove(request);
            if (!CanAttemptUpperBodyAttack)
            {
                CancelCharge();
                return JournalCommandResult(request, "rejected", UpperBodyAttackRejection);
            }
            if (!State.ReleaseCharge()) return JournalCommandResult(request, "rejected", "no_held_or_queued_charge");
            if (State.IsAttacking) { reaction = null; sweepValid = false; }
            ResumeFinishedBodyAttack();
            Present();
            return JournalCommandResult(request, State.IsAttacking ? "started" : "queued", "charge_release");
        }

        public bool CancelCharge()
        {
            int request = JournalCommand("charge_cancel");
            if (!State.CancelCharge()) return JournalCommandResult(request, "rejected", "no_held_or_queued_charge");
            journalQueuedRequest = 0;
            sweepValid = false;
            if (isActiveAndEnabled && gameObject.activeInHierarchy) Present();
            return JournalCommandResult(request, "cancelled", "charge", trackAction: false);
        }

        /// <summary>Run after both fighters' collected contacts have resolved.
        /// A new action must never relabel or erase the old swing's final sweep.</summary>
        internal bool ContinueBufferedAttackAfterContacts()
        {
            if ((!CombatFocused || roundEnded) && !CanAttackFinishedBody || presentationFrozen || !IsAvailable ||
                !GameInput.CanRead(GameInputContext.Gameplay) ||
                (contactTarget != null && contactTarget.State.IsDefeated && !CanAttackFinishedBody)) return false;
            MeleeBufferedAction action = State.BufferedAction;
            if (CanAttackFinishedBody && action is not (MeleeBufferedAction.Attack or MeleeBufferedAction.Charge)) return false;
            if (action == MeleeBufferedAction.None) return false;
            if (action != MeleeBufferedAction.Kick) CancelPendingKick("replaced");
            bool upperBody = action is MeleeBufferedAction.Attack or MeleeBufferedAction.Charge or MeleeBufferedAction.Shove;
            if (action == MeleeBufferedAction.Shove)
            {
                if (!BodyDamage.CanUseLeftHand || !CanAttemptBodyAction) return false;
            }
            else if (upperBody && !CanAttemptUpperBodyAttack) return false;
            if (!State.CanTransitionTo(action, upperBody)) return false;
            if (State.IsShoving && shoveContactPending) return false;
            if (State.Phase == MeleePhase.Step && action != MeleeBufferedAction.Step &&
                !(footwork?.PrepareStepAttackHandoff() ?? false)) return false;
            if (action == MeleeBufferedAction.Kick) return ContinueBufferedKickAfterContacts();
            if (action == MeleeBufferedAction.Shove && !CheckShoveRange())
            {
                State.CancelBufferedAction(action);
                return false;
            }
            bool step = action == MeleeBufferedAction.Step;
            bool recoveringStep = step && !HasAttackBalance;
            if (recoveringStep)
            {
                if (IsKnockedDown || State.IsKnockedDown || IsRagdollActive ||
                    (ImpactMotion?.WantsKnockdown ?? false) || !PrepareRecoveryStep(pendingStepInput)) return false;
            }
            else if (!upperBody && !HasAttackBalance) return false;
            int request = journalQueuedRequest;
            if (!State.TryContinueBufferedAction(upperBody)) return false;
            if (step) BeginStepPresentation(recoveringStep);
            if (action == MeleeBufferedAction.Shove) BeginShovePresentation(request);
            JournalBufferedActionStarted();
            reaction = null;
            reactionClock = 0f;
            sweepValid = collectSweep = false;
            Present();
            return true;
        }

        private void JournalBufferedActionStarted()
        {
            if (journalQueuedRequest == 0) return;
            journalActionRequest = journalQueuedRequest;
            journalQueuedRequest = 0;
            JournalEvent("buffer_started", action: State.AttackSequence, request: journalActionRequest,
                f0: GameLog.Field("phase", (int)State.Phase), f1: GameLog.Field("continuation", State.IsContinuation));
        }

        private void LoadSwingClips(bool forNpc)
        {
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            {
                CombatAssetProvider.SwingClipSet names = CombatAssetProvider.SwingClips(swing);
                swings[(int)swing] = new SwingClips
                {
                    Attack = CombatAssetProvider.LoadClip(names.Attack, forNpc),
                    ReleaseLight = CombatAssetProvider.LoadClip(names.ReleaseLight, forNpc),
                    ReleaseHeavy = CombatAssetProvider.LoadClip(names.ReleaseHeavy, forNpc),
                    Charge = CombatAssetProvider.LoadClip(names.Charge, forNpc),
                    Recoil = CombatAssetProvider.LoadClip(names.Recoil, forNpc)
                };
            }
        }

        private void SampleHeroRelease(float progress)
        {
            hero.SampleOwnedClip(this,
                CombatAssetProvider.ReleaseSourceSeconds(progress * Current.Attack.length, State.AttackPower, State.IsContinuation) / Current.Attack.length);
        }

        private void SampleHeroCharge()
        {
            hero.SampleOwnedClip(this, State.Charge01);
        }

        private void SampleNpcRelease(float progress)
        {
            SampleNpcReleasePose(progress * Current.Attack.length, State.AttackPower);
        }

        private void SampleNpcReleasePose(float seconds, float power)
        {
            AnimationClip clip = Current.Attack;
            SampleNpcClip(clip, CombatAssetProvider.ReleaseSourceSeconds(seconds, power));
        }
    }
}
