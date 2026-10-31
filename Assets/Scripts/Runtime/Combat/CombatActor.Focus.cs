namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private bool freeLocomotionReleased;

        /// <summary>The hero's voluntary combat stance; the opponent remains engaged independently.</summary>
        public bool CombatFocused { get; private set; } = true;

        internal bool CanBeginStandingInspection => hero != null && isActiveAndEnabled &&
            State.Phase == MeleePhase.Ready && !State.IsBlocking && HasAttackBalance &&
            !HasWeaponSwitchRequest && !IsRagdollActive && !(ImpactMotion?.IsActive ?? false) &&
            !(Firearm?.ReloadPending ?? false) && !(Firearm?.AimRequested ?? false);

        /// <summary>Yield a settled voluntary pose without cancelling any committed combat action.</summary>
        internal void PrepareStandingInspection()
        {
            SetCombatFocused(false);
            ReleaseStandingPresentation();
            freeLocomotionReleased = true;
        }

        internal void RestoreStandingInspectionFocus(bool focused)
        {
            if (isActiveAndEnabled) SetCombatFocused(focused);
            else
            {
                CombatFocused = focused;
                freeLocomotionReleased = !focused;
            }
        }

        // Dedicated directional firearm steps keep the body aimed forwards.
        // Physical recovery and committed actions retain their combat contacts.
        internal bool UsesFirearmWalkLocomotion => hero != null && IsFirearm &&
            State.Phase == MeleePhase.Ready && Firearm != null && !Firearm.ReloadPending &&
            (Firearm.AimRequested || pistolVisualAimProgress > 0f) && BodyDamage.CanStand &&
            !IsKnockedDown && !IsRagdollActive && reaction == null &&
            !(ImpactMotion?.IsActive ?? false) && !(footwork?.RecoveryEpisodeActive ?? false);

        // Target focus is an aiming decision, not a request for the retired
        // crowbar shuffle. Only committed actions/injuries own its lower body.
        private bool NeedsCombatPresentation => IsRestrained || (CombatFocused && (hero == null || IsFirearm)) || State.IsBlocking || (Firearm?.ReloadPending ?? false) ||
            (Firearm?.AimRequested ?? false) || (IsFirearm && pistolVisualAimProgress > 0f) || State.Phase != MeleePhase.Ready ||
            collectSweep || collectShove || collectKick || IsKnockedDown || IsRagdollActive ||
            reaction != null || (ImpactMotion?.IsActive ?? false) || (footwork?.RecoveryEpisodeActive ?? false);

        private bool CommittedActionOwnsFacing => State.IsAttacking || State.IsShoving || State.IsKicking ||
            State.Phase is MeleePhase.Step or MeleePhase.Recovery;

        /// <summary>Changes only voluntary participation: committed actions, injuries and HP survive.</summary>
        public void SetCombatFocused(bool focused, bool preservePistolAim = false)
        {
            if (hero == null || CombatFocused == focused) return;
            if (IsFirearm && CombatFocused && !focused) pistolFreeAimPoint = PistolAimIntentPoint;
            CombatFocused = focused;
            if (!focused && !preservePistolAim) SetPistolAim(false);
            // A rapid off/on toggle cannot grant another aim envelope to an existing swing.
            if (!CommittedActionOwnsFacing) ResetCombatFacing();
            if (!focused)
            {
                bool charging = State.IsCharging;
                State.CancelCharge();
                State.CancelBufferedAction(State.BufferedAction);
                journalQueuedRequest = 0;
                CancelPendingKick("focus_released");
                guardHeld = false;
                State.SetBlocking(false);
                State.CancelParry();
                if (charging) { reaction = null; sweepValid = collectSweep = false; }
                ReleaseCombatAttention();
            }
            else if (NeedsCombatPresentation) freeLocomotionReleased = false;
            Present();
        }

        /// <summary>Yield once after the final action or injury has settled; never reset live recovery each tick.</summary>
        private void ReleaseFreeLocomotion()
        {
            if (hero == null || freeLocomotionReleased) return;
            ReleaseStandingPresentation();
            freeLocomotionReleased = true;
        }

        private void ReleaseStandingPresentation()
        {
            bool ownedPose = hero.OwnsClip(this);
            ReleasePresentation(preservePresentedPose: true);
            supportGrip?.SetTarget(false, false);
            hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
            if (ownedPose) hero.BeginRecoveryPoseTransition(.35f);
            // Keep the weapon in the right palm while ordinary idle and gait own the body.
            handPose.SetGrip(false, weaponDropped ? 0f : 1f);
            if (IsFirearm)
            {
                pistolLeftClosure = 0f;
                handPose.SetGrip(true, 0f);
                if (!weaponDropped && hero.TryAcquireCarryPose(this, pistolRest.name))
                {
                    // Retain the lease for the late palm attachment, without
                    // masking ordinary idle or gait with the bent-arm Rest pose.
                    hero.UpdateCarryPose(this, 0f, 0f);
                    hero.SetCombatFirearm(this, this);
                }
            }
            else if (HasWeaponInHand && hero.TryAcquireCarryPose(this, ready.name))
            {
                // A relaxed two-hand hold above the ordinary walk/run graph;
                // neither combat footwork nor the Ready pelvis owns the legs.
                hero.UpdateCarryPose(this, 0f);
                supportGrip?.SetTarget(false, true);
                hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
            }
        }
    }
}
