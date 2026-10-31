using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private enum MimicPhase { Awake, Dormant, Revealing }
        private MimicPhase mimicPhase;
        private AnimationClip zombieDormant, zombieReveal;
        private float mimicClock;
        private int mimicBreakIndex;
        private bool dormantPoseSampled;
        private bool HasZombieDormancy => IsDocker || IsOrdinaryZombie;
        public bool ZombieDormant => HasZombieDormancy && mimicPhase == MimicPhase.Dormant;
        public bool ZombieRevealing => HasZombieDormancy && mimicPhase == MimicPhase.Revealing;
        public bool ZombieAwake => !HasZombieDormancy || mimicPhase == MimicPhase.Awake;
        internal float ZombieRevealElapsed => mimicClock;
        private float ZombieRiseDuration => IsOrdinaryZombie ? CombatZombieAssetProvider.OrdinaryRiseSeconds : CombatZombieAssetProvider.RevealSeconds;

        /// <summary>The encounter explicitly owns waking; distance never changes this state.</summary>
        public void SetOrdinaryZombieDormant(int variant)
        {
            if (!IsOrdinaryZombie) throw new System.InvalidOperationException("Only the ordinary zombie owns the degraded body poses.");
            if ((uint)variant >= 3u) throw new System.ArgumentOutOfRangeException(nameof(variant));
            if (variant != OrdinaryZombieVariant) throw new System.InvalidOperationException("A dormant body keeps its authored anatomy and attack profile.");
            if (State.IsDefeated || IsKnockedDown || IsRagdollActive) return;
            OrdinaryZombieVariant = variant;
            zombieDormant = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.OrdinaryDormantClip(variant));
            zombieReveal = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.OrdinaryRiseClip(variant));
            SetZombieDormant();
        }

        internal void SetZombieDormant()
        {
            if (!HasZombieDormancy || State.IsDefeated) return;
            ResetZombieFrenzy();
            mimicPhase = MimicPhase.Dormant;
            mimicClock = 0f;
            mimicBreakIndex = 0;
            dormantPoseSampled = false;
            locomotionVelocity = Vector3.zero;
            zombieWalkingRequested = false;
            State.CancelAction();
            zombieAudio?.Reset();
            if (IsDocker) SetMimicCapsule(0f);
            CancelPoseBlend();
            Present();
        }

        internal bool TryAwakenZombie(bool playSound = true)
        {
            if (!isActiveAndEnabled || !ZombieDormant || State.IsDefeated || roundEnded) return false;
            mimicPhase = MimicPhase.Revealing;
            mimicClock = 0f;
            mimicBreakIndex = 0;
            dormantPoseSampled = false;
            if (playSound) zombieAudio?.Reveal();
            JournalEvent(IsOrdinaryZombie ? "ordinary_zombie_rise" : "mimic_reveal");
            return true;
        }

        private void ResetZombieMimic()
        {
            mimicPhase = MimicPhase.Awake;
            mimicClock = 0f;
            mimicBreakIndex = 0;
            dormantPoseSampled = false;
            if (IsDocker) SetMimicCapsule(1f);
        }

        private void SetMimicCapsule(float progress)
        {
            if (Body == null) return;
            float height = Mathf.Lerp(.7f, 1.75f, progress);
            Vector3 centre = Vector3.up * (height * .5f);
            if (Mathf.Abs(Body.height - height) < .00001f && Body.center == centre) return;
            Body.height = height;
            Body.center = centre;
            zombieLoad?.RefreshMotorCollisionExclusions();
        }

        // The existing shared combat clock owns this action, including pause and
        // hit-stop. A dormant rig is sampled once; no breathing/fish/AI clocks run.
        private bool AdvanceZombieMimic(float seconds)
        {
            if (!HasZombieDormancy || ZombieAwake || State.IsDefeated) return false;
            if (IsKnockedDown || IsRagdollActive || IsOrdinaryZombie && State.IsStunned)
            {
                if (IsDocker) AdvanceMimicBreaks(CombatZombieAssetProvider.RevealSeconds, false);
                mimicPhase = MimicPhase.Awake;
                zombieAudio?.Activate();
                if (IsDocker) SetMimicCapsule(1f);
                return false;
            }
            if (ZombieDormant || presentationFrozen) return true;
            float nextClock = Mathf.Min(ZombieRiseDuration, mimicClock + seconds);
            if (IsDocker) AdvanceMimicBreaks(nextClock, true);
            mimicClock = nextClock;
            State.Advance(seconds, false);
            if (IsDocker) SetMimicCapsule(Mathf.SmoothStep(0f, 1f, mimicClock / ZombieRiseDuration));
            if (mimicClock + .000001f >= ZombieRiseDuration)
            {
                // The authored endpoint equals ready at zero; no pose blend or
                // locomotion phase can pull this first standing frame elsewhere.
                SampleNpcClip(zombieReveal, ZombieRiseDuration);
                mimicPhase = MimicPhase.Awake;
                poseClock = zombieWorkClock = zombieWalkClock = 0f;
                visibleClip = null;
                CancelPoseBlend();
                zombieAudio?.Activate();
            }
            return true;
        }

        private void AdvanceMimicBreaks(float seconds, bool sampleContact)
        {
            var breaks = CombatZombieAssetProvider.RevealBreaks;
            while (mimicBreakIndex < breaks.Length && breaks[mimicBreakIndex].Seconds <= seconds + .000001f)
            {
                var contact = breaks[mimicBreakIndex++];
                // A large simulation step still releases each board at the
                // authored limb contact, before presenting the final sample.
                if (sampleContact) SampleNpcClip(zombieReveal, contact.Seconds);
                Vector3 direction = transform.TransformDirection(contact.LocalImpulseDirection);
                foreach (int board in contact.BoardNumbers) zombieLoad.BreakMimicPanel(board, direction);
            }
        }

        private bool PresentZombieMimic()
        {
            if (!HasZombieDormancy || ZombieAwake || State.IsDefeated || IsKnockedDown || IsRagdollActive) return false;
            if (ZombieDormant && dormantPoseSampled) return true;
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            AnimationClip clip = ZombieDormant ? zombieDormant : zombieReveal;
            SampleNpcClip(clip, ZombieDormant ? 0f : mimicClock);
            visibleClip = clip.name;
            visibleAttackSequence = State.AttackSequence;
            handPose.SetGrip(false, IsDocker ? .12f : 0f); handPose.SetGrip(true, IsDocker ? .12f : 0f);
            zombieLoad?.RefreshRamProxyState();
            RememberNpcPresentedPose();
            dormantPoseSampled = ZombieDormant;
            return true;
        }

        internal void NotifyZombieWoodHit()
        {
            if (!IsDocker || State.IsDefeated) return;
            TryAwakenZombie();
            zombieAudio?.Hurt(true);
        }

        private void NotifyZombieBodyHit(CombatImpact impact)
        {
            if (!IsZombie || impact.DetachedPart || State.IsDefeated || impact.HealthBefore <= 0f) return;
            TryAwakenZombie();
            if (!impact.IsPellet || impact.PrimaryResponse) zombieAudio?.Hurt(false);
        }
    }
}
