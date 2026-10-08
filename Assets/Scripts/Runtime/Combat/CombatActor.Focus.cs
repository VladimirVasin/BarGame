namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private bool freeLocomotionReleased;

        /// <summary>The hero's voluntary combat stance; the opponent remains engaged independently.</summary>
        public bool CombatFocused { get; private set; } = true;

        private bool NeedsCombatPresentation => CombatFocused || (Pistol?.ReloadPending ?? false) ||
            (Pistol?.AimRequested ?? false) || (IsPistol && pistolLowerProgress < 1f) || State.Phase != MeleePhase.Ready ||
            collectSweep || collectShove || collectKick || IsKnockedDown || IsRagdollActive ||
            reaction != null || (ImpactMotion?.IsActive ?? false) || (footwork?.RecoveryEpisodeActive ?? false);

        private bool CommittedActionOwnsFacing => State.IsAttacking || State.IsShoving || State.IsKicking ||
            State.Phase is MeleePhase.Step or MeleePhase.Recovery;

        /// <summary>Changes only voluntary participation: committed actions, injuries and HP survive.</summary>
        public void SetCombatFocused(bool focused, bool preservePistolAim = false)
        {
            if (hero == null || CombatFocused == focused) return;
            CombatFocused = focused;
            if (!focused && !preservePistolAim) Pistol?.SetAim(false);
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
                if (charging) { reaction = null; sweepValid = collectSweep = false; }
                ReleaseCombatAttention();
            }
            else freeLocomotionReleased = false;
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
            ReleasePresentation();
            supportGrip?.SetTarget(false, false);
            hero.SetCombatSupportGrip(this, supportGrip, weaponConstraint);
            if (ownedPose) hero.BeginRecoveryPoseTransition(.35f);
            // Keep the weapon in the right palm while ordinary idle and gait own the body.
            handPose.SetGrip(false, weaponDropped ? 0f : 1f);
            if (IsPistol)
            {
                pistolLeftClosure = 0f;
                handPose.SetGrip(true, 0f);
                if (!weaponDropped && hero.TryAcquireCarryPose(this, CombatPistolAssetProvider.RestClip))
                {
                    // Retain the lease for the late palm attachment, without
                    // masking ordinary idle or gait with the bent-arm Rest pose.
                    hero.UpdateCarryPose(this, 0f, 0f);
                    hero.SetCombatFirearm(this, this);
                }
            }
        }
    }
}
