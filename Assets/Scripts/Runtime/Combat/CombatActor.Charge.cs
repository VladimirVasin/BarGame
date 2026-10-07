using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        /// <summary>One swing side's clips; the rules' committed side selects the set.</summary>
        private struct SwingClips { public AnimationClip Attack, ReleaseLight, ReleaseHeavy, Charge, Recoil; }
        private readonly SwingClips[] swings = new SwingClips[2];
        private Vector3[] npcReleasePositions;
        private Quaternion[] npcReleaseRotations;
        private bool[] npcReleaseUpperBody;
        private SwingClips Current => swings[(int)State.Swing];
        private AnimationClip ReleaseClip => State.AttackPower > 0f ? Current.ReleaseLight : Current.Attack;
        private bool IsRecoil(AnimationClip clip) => clip != null && (clip == swings[0].Recoil || clip == swings[1].Recoil);

        public bool RequestCharge()
        {
            int request = JournalCommand("charge");
            if (roundEnded) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            // A following press owns one queue slot while the old swing still
            // owns its weapon contacts. The free support hand never delays it.
            if (!State.IsAttacking)
            {
                if (!HasAttackBalance)
                {
                    if (!State.RequestRecoveryCharge()) return JournalCommandResult(request, "rejected", AttackBalanceRejection);
                    return JournalCommandResult(request, "queued", "balance_buffer");
                }
                if (!State.IsKicking && CheckShoveRange(request)) return TryBeginShove(request);
            }
            if (!State.RequestCharge()) return JournalRulesRejected(request, State.Settings.AttackCost, true);
            ContinueBufferedAttackAfterContacts();
            if (State.IsCharging) { reaction = null; sweepValid = false; }
            Present();
            return JournalCommandResult(request, State.IsCharging ? "started" : "queued", "charge");
        }

        public bool ReleaseCharge()
        {
            int request = JournalCommand("charge_release");
            if (roundEnded) return JournalCommandResult(request, "rejected", "round_ended");
            if (!IsAvailable) return JournalCommandResult(request, "rejected", "actor_unavailable");
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return JournalCommandResult(request, "rejected", "input_gate");
            if (State.HasBufferedCharge)
            {
                // Releasing a queued click never cancels its preceding swing,
                // even when that swing temporarily displaced the support hand.
                State.ReleaseCharge();
                bool started = ContinueBufferedAttackAfterContacts();
                return JournalCommandResult(request, started ? "started" : "queued", "charge_release");
            }
            if (State.IsCharging && CheckShoveRange(request)) return TryBeginShove(request);
            if (!HasAttackBalance)
            {
                // A true loss of balance still interrupts the held action.
                // Losing only the left-hand contact releases a one-handed swing.
                CancelCharge();
                return JournalCommandResult(request, "rejected", AttackBalanceRejection);
            }
            if (!State.ReleaseCharge()) return JournalCommandResult(request, "rejected", "no_held_or_queued_charge");
            if (State.IsAttacking) { reaction = null; sweepValid = false; }
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
            if (roundEnded || presentationFrozen || HasPendingKick || !IsAvailable ||
                !GameInput.CanRead(GameInputContext.Gameplay) ||
                (contactTarget != null && contactTarget.State.IsDefeated)) return false;
            bool step = State.HasBufferedStep;
            bool recoveringStep = step && !HasAttackBalance;
            if (recoveringStep)
            {
                if (State.Phase != MeleePhase.Ready || IsKnockedDown || State.IsKnockedDown || IsRagdollActive ||
                    (ImpactMotion?.WantsKnockdown ?? false) || !PrepareRecoveryStep(pendingStepInput)) return false;
            }
            else if (!HasAttackBalance) return false;
            if (!(step ? State.TryContinueBufferedStep() : State.TryContinueAttack())) return false;
            if (step) BeginStepPresentation(recoveringStep);
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

        private void InitializeNpcChargeBlend()
        {
            npcReleasePositions = new Vector3[npcPoseBones.Length];
            npcReleaseRotations = new Quaternion[npcPoseBones.Length];
            npcReleaseUpperBody = new bool[npcPoseBones.Length];
            Transform spine = null;
            foreach (Transform bone in npcPoseBones)
                if (bone.name == "spine") { spine = bone; break; }
            for (int i = 0; i < npcPoseBones.Length; i++)
                npcReleaseUpperBody[i] = spine != null && npcPoseBones[i].IsChildOf(spine);
        }

        private void SampleHeroRelease(float progress)
        {
            hero.SampleOwnedClipUpperTime(this, Current.Attack.name, progress,
                CombatAssetProvider.ReleaseSourceSeconds(progress * Current.Attack.length, State.AttackPower, State.IsContinuation) / Current.Attack.length);
        }

        private void SampleHeroCharge()
        {
            hero.SampleOwnedClipUpperTime(this, Current.Attack.name, 0f,
                CombatAssetProvider.ReleaseSourceSeconds(0f, State.Charge01, State.IsContinuation) / Current.Attack.length);
        }

        private void SampleNpcRelease(float progress)
        {
            SampleNpcReleasePose(progress * Current.Attack.length, State.AttackPower);
        }

        private void SampleNpcReleasePose(float seconds, float power)
        {
            AnimationClip clip = Current.Attack;
            clip.SampleAnimation(npc.Animator.gameObject, seconds);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                if (npcReleaseUpperBody[i]) continue;
                npcReleasePositions[i] = npcPoseBones[i].localPosition;
                npcReleaseRotations[i] = npcPoseBones[i].localRotation;
            }
            clip.SampleAnimation(npc.Animator.gameObject, CombatAssetProvider.ReleaseSourceSeconds(seconds, power, State.IsContinuation));
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                if (npcReleaseUpperBody[i]) continue;
                npcPoseBones[i].localPosition = npcReleasePositions[i];
                npcPoseBones[i].localRotation = npcReleaseRotations[i];
            }
        }
    }
}
