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
        public bool ZombieDormant => IsZombie && mimicPhase == MimicPhase.Dormant;
        public bool ZombieRevealing => IsZombie && mimicPhase == MimicPhase.Revealing;
        public bool ZombieAwake => !IsZombie || mimicPhase == MimicPhase.Awake;
        internal float ZombieRevealElapsed => mimicClock;

        internal void SetZombieDormant()
        {
            if (!IsZombie || State.IsDefeated) return;
            ResetZombieFrenzy();
            mimicPhase = MimicPhase.Dormant;
            mimicClock = 0f;
            mimicBreakIndex = 0;
            dormantPoseSampled = false;
            locomotionVelocity = Vector3.zero;
            zombieWalkingRequested = false;
            State.CancelAction();
            zombieAudio?.Reset();
            SetMimicCapsule(0f);
            CancelPoseBlend();
            Present();
        }

        internal bool TryAwakenZombie()
        {
            if (!ZombieDormant || State.IsDefeated || roundEnded) return false;
            mimicPhase = MimicPhase.Revealing;
            mimicClock = 0f;
            mimicBreakIndex = 0;
            dormantPoseSampled = false;
            zombieAudio?.Reveal();
            JournalEvent("mimic_reveal");
            return true;
        }

        private void ResetZombieMimic()
        {
            mimicPhase = MimicPhase.Awake;
            mimicClock = 0f;
            mimicBreakIndex = 0;
            dormantPoseSampled = false;
            if (IsZombie) SetMimicCapsule(1f);
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
            if (!IsZombie || ZombieAwake || State.IsDefeated) return false;
            if (IsKnockedDown || IsRagdollActive)
            {
                AdvanceMimicBreaks(CombatZombieAssetProvider.RevealSeconds, false);
                mimicPhase = MimicPhase.Awake;
                zombieAudio?.Activate();
                SetMimicCapsule(1f);
                return false;
            }
            if (ZombieDormant || presentationFrozen) return true;
            float nextClock = Mathf.Min(CombatZombieAssetProvider.RevealSeconds, mimicClock + seconds);
            AdvanceMimicBreaks(nextClock, true);
            mimicClock = nextClock;
            State.Advance(seconds, false);
            SetMimicCapsule(Mathf.SmoothStep(0f, 1f, mimicClock / CombatZombieAssetProvider.RevealSeconds));
            if (mimicClock + .000001f >= CombatZombieAssetProvider.RevealSeconds)
            {
                // The authored endpoint equals ready at zero; no pose blend or
                // locomotion phase can pull this first standing frame elsewhere.
                SampleNpcClip(zombieReveal, CombatZombieAssetProvider.RevealSeconds);
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
            if (!IsZombie || ZombieAwake || State.IsDefeated || IsKnockedDown || IsRagdollActive) return false;
            if (ZombieDormant && dormantPoseSampled) return true;
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            AnimationClip clip = ZombieDormant ? zombieDormant : zombieReveal;
            SampleNpcClip(clip, ZombieDormant ? 0f : mimicClock);
            visibleClip = clip.name;
            visibleAttackSequence = State.AttackSequence;
            handPose.SetGrip(false, .12f); handPose.SetGrip(true, .12f);
            zombieLoad.RefreshRamProxyState();
            RememberNpcPresentedPose();
            dormantPoseSampled = ZombieDormant;
            return true;
        }

        internal void NotifyZombieWoodHit()
        {
            if (!IsZombie || State.IsDefeated) return;
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
