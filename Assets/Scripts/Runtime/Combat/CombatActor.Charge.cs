using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        /// <summary>Retained bank references; both sides present the same fixed strike.</summary>
        private struct SwingClips { public AnimationClip Attack, ReleaseLight, ReleaseHeavy, Charge, Recoil; }
        private readonly SwingClips[] swings = new SwingClips[2];
        private SwingClips Current => swings[(int)State.Swing];
        private AnimationClip ReleaseClip => Current.Attack;
        private bool IsRecoil(AnimationClip clip) => clip != null && (clip == swings[0].Recoil || clip == swings[1].Recoil);

        // Historical callers can still compile; charging has no runtime action.
        public bool RequestCharge() => false;
        public bool ReleaseCharge() => false;
        public bool CancelCharge() => false;

        /// <summary>Run after both fighters' collected contacts have resolved.
        /// A new action must never relabel or erase the old swing's final sweep.</summary>
        internal bool ContinueBufferedAttackAfterContacts()
        {
            MeleeBufferedAction action = State.BufferedAction;
            bool freeSwing = action is MeleeBufferedAction.Attack or MeleeBufferedAction.Charge;
            if ((!CombatFocused && !freeSwing || roundEnded) && !CanAttackFinishedBody || RoomAttackSuppressed || presentationFrozen || !IsAvailable ||
                !GameInput.CanRead(GameInputContext.Gameplay) ||
                (CombatFocused && contactTarget != null && contactTarget.State.IsDefeated && !CanAttackFinishedBody)) return false;
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
            // One rules clock samples the complete authored strike directly.
            hero.SampleOwnedClip(this, progress);
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
            SampleNpcClip(clip, seconds);
        }
    }
}
