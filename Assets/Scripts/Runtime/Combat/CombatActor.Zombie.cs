using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private AnimationClip zombieWalk, zombieAttackRight, zombieAttackLeft;
        private Transform zombieShoulder, zombieLeftShoulder, zombieRightShoulder;
        private Vector3 zombieHandFrom, zombieHandTo;
        private bool zombieHandPoseValid;
        private float zombieWalkClock, zombieGroanRemaining;
        private AudioSource zombieVoice;
        public bool ZombieAttackUsesLeftHand { get; private set; }
        private const float ZombiePalmRadius = .065f;

        private void LoadZombieClips()
        {
            ready = rest = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.IdleClip);
            zombieWalk = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.WalkClip);
            zombieAttackRight = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.AttackRightClip);
            zombieAttackLeft = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.AttackLeftClip);
            hit = guardBreak = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.HitClip);
            // Death and rise retain the existing anatomical handoff to the same rig.
            defeat = CombatAssetProvider.LoadClip("CombatDefeat", true);
        }

        private void InitializeZombieHands()
        {
            handPose = npc.RightGrip.GetComponentInParent<NpcHandPose>();
            if (handPose == null) throw new InvalidOperationException("A zombie requires the shared anatomical hands.");
            zombieLeftShoulder = CityPedestrianHandProps.FindSocket(DamageRigRoot, "upper_arm.L");
            zombieRightShoulder = CityPedestrianHandProps.FindSocket(DamageRigRoot, "upper_arm.R");
            handPose.SetGrip(false, 0f); handPose.SetGrip(true, 0f);
            CombatZombieAssetProvider.LoadGroan(); CombatZombieAssetProvider.LoadLunge();
            zombieVoice = gameObject.AddComponent<AudioSource>();
            zombieVoice.playOnAwake = false; zombieVoice.spatialBlend = 1f;
            zombieVoice.rolloffMode = AudioRolloffMode.Linear;
            zombieVoice.minDistance = 1f; zombieVoice.maxDistance = 14f;
            GameAudioMixer.Route(zombieVoice, GameAudioGroup.SfxGameplay);
            ResetZombieAttack();
        }

        public bool TryZombieAttack()
        {
            if (!IsZombie || roundEnded || State.Phase != MeleePhase.Ready || !IsAvailable ||
                !CanAttemptBodyAction || !HasAttackBalance || !GameInput.CanRead(GameInputContext.Gameplay) ||
                !(BodyDamage.CanUseRightHand || BodyDamage.CanUseLeftHand)) return false;
            if (!State.RequestAttack()) return false;
            ZombieAttackUsesLeftHand = State.Swing == MeleeSwing.Backhand;
            reaction = null;
            zombieShoulder = ZombieAttackUsesLeftHand ? zombieLeftShoulder : zombieRightShoulder;
            zombieHandPoseValid = false;
            Present();
            // The first swept interval starts at this exact visible hand, not the previous attack.
            zombieHandFrom = zombieHandTo = handPose.CylinderCentre(ZombieAttackUsesLeftHand);
            zombieHandPoseValid = true;
            JournalEvent("zombie_attack_started", contactTarget?.JournalActorId ?? 0, State.AttackSequence,
                f0: GameLog.Field("left_hand", ZombieAttackUsesLeftHand));
            return true;
        }

        private void AdvanceZombiePresentation(float seconds)
        {
            if (State.Phase == MeleePhase.Ready)
                zombieWalkClock += locomotionVelocity.magnitude * seconds /
                    CombatZombieAssetProvider.WalkCycleDistance * zombieWalk.length;
            zombieGroanRemaining -= seconds;
            if (zombieGroanRemaining <= 0f && State.Phase == MeleePhase.Ready && !roundEnded)
            {
                zombieGroanRemaining = 7f + (JournalActorId % 3) * 2f;
                if (zombieVoice != null && !zombieVoice.isPlaying)
                { zombieVoice.clip = CombatZombieAssetProvider.LoadGroan(); zombieVoice.volume = .3f; zombieVoice.Play(); }
            }
        }

        private void PlayZombieLunge()
        {
            if (zombieVoice == null) return;
            zombieVoice.clip = CombatZombieAssetProvider.LoadLunge();
            zombieVoice.volume = .6f; zombieVoice.Play();
        }

        private void PresentZombie()
        {
            RestoreCorpseStepPose();
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            bool stagger = State.IsStunned;
            AnimationClip chosen = State.IsDefeated ? defeat : stagger ? hit : State.IsAttacking
                ? ZombieAttackUsesLeftHand ? zombieAttackLeft : zombieAttackRight
                : locomotionVelocity.sqrMagnitude > .0025f && !roundEnded ? zombieWalk : ready;
            float time = State.IsDefeated ? Mathf.Min(defeatClock, chosen.length) : stagger
                ? State.PhaseProgress * chosen.length : State.IsAttacking ? State.AttackProgress * chosen.length
                : Mathf.Repeat(chosen == zombieWalk ? zombieWalkClock : poseClock, chosen.length);
            if (visibleClip != chosen.name || visibleAttackSequence != State.AttackSequence)
            { BeginPoseBlend(); visibleClip = chosen.name; }
            SampleNpcClip(chosen, time);
            // Corpse clips own their slouch and loose arms; a living fighter's
            // gait/breath layer must not straighten that authored silhouette.
            if (ImpactMotion?.IsActive ?? false) bodyMotion?.Apply();
            PresentDamagePose();
            bool recoveryFeet = footwork != null &&
                (footwork.RecoveryEpisodeActive || (ImpactMotion?.IsActive ?? false));
            if (recoveryFeet) footwork.Apply();
            ApplyCombatAttention(); ApplyNpcPoseBlend();
            if (recoveryFeet) footwork.ConstrainContacts();
            ApplyCorpseStepPose();
            handPose.SetGrip(false, 0f); handPose.SetGrip(true, 0f);
            visibleAttackSequence = State.AttackSequence;
            RememberNpcPresentedPose();
            footwork?.CapturePresentedContacts();
        }

        private void CaptureZombieHandPose()
        {
            Vector3 palm = handPose.CylinderCentre(ZombieAttackUsesLeftHand);
            zombieHandFrom = zombieHandPoseValid ? zombieHandTo : palm;
            zombieHandTo = palm; zombieHandPoseValid = true;
        }

        private void SweepZombieHand(float from, float to, int sequence, List<Contact> pending)
        {
            if (to < State.AttackWindupSeconds || from >= State.AttackActiveEnd ||
                sequence != State.AttackSequence || !zombieHandPoseValid ||
                !(ZombieAttackUsesLeftHand ? BodyDamage.CanUseLeftHand : BodyDamage.CanUseRightHand)) return;
            Vector3 a = zombieHandFrom, b = zombieHandTo;
            float fraction = float.PositiveInfinity;
            CombatActor nearest = null; CombatHurtboxes.Hit bodyHit = default;
            for (int i = 0; i < ContactParticipantCount; i++)
            {
                CombatActor candidate = ContactParticipantAt(i);
                if (candidate == this || !HasPhysicalBody(candidate) ||
                    candidate.IsKnockedDown && candidate.State.Phase != MeleePhase.Rising ||
                    !candidate.Hurtboxes.SweepSphere(a, b, ZombiePalmRadius, transform.forward, out var found) ||
                    found.Fraction >= fraction) continue;
                nearest = candidate; bodyHit = found; fraction = found.Fraction;
            }
            float wallFraction = float.PositiveInfinity;
            Vector3 wallPoint = default, wallNormal = default;
            // Shared grow-and-retry NonAlloc buffers also cover a hand already inside a wall.
            GatherWorldSegment(a, a, ZombiePalmRadius, ref wallFraction, ref wallPoint, ref wallNormal);
            if (wallFraction <= 1f) wallFraction = 0f;
            GatherWorldSweep(a, b, ZombiePalmRadius, ref wallFraction, ref wallPoint, ref wallNormal);
            GatherWorldSegment(b, b, ZombiePalmRadius, ref wallFraction, ref wallPoint, ref wallNormal);
            // An arm protruding through a wall before the active phase cannot hit behind it.
            if (zombieShoulder != null)
            {
                float armWall = float.PositiveInfinity;
                GatherWorldSweep(zombieShoulder.position, b, .035f, ref armWall, ref wallPoint, ref wallNormal);
                if (armWall <= 1f) wallFraction = 0f;
            }
            if (wallFraction <= 1f && (nearest == null || wallFraction <= fraction))
            {
                pending.Add(Contact.Obstacle(this, null, wallPoint, wallNormal, transform.forward,
                    Mathf.Lerp(from, to, wallFraction), false));
                return;
            }
            if (nearest == null) return;
            if (!CanDamageParticipant(nearest) || nearest.State.IsDefeated)
            {
                pending.Add(Contact.Obstacle(this, nearest, bodyHit.Point, bodyHit.Normal,
                    transform.forward, Mathf.Lerp(from, to, fraction), false));
                return;
            }
            if (!State.TryRegisterHit(nearest.GetEntityId().GetHashCode(), sequence)) return;
            pending.Add(new Contact(this, nearest, bodyHit.Point, bodyHit.Normal, transform.forward,
                bodyHit.Location, bodyHit.Part, bodyHit.LocalPoint,
                time: Mathf.Lerp(from, to, fraction), bodyRegion: bodyHit.DamageRegion,
                bodyPatch: bodyHit.DamagePatch));
        }

        private void StopZombieAttack()
        {
            if (!State.CancelAttackOnObstacle()) return;
            collectSweep = false; zombieHandPoseValid = false;
        }

        private void ResetZombieAttack()
        {
            zombieHandPoseValid = false; zombieShoulder = null;
            zombieHandFrom = zombieHandTo = Vector3.zero;
            zombieWalkClock = 0f; zombieGroanRemaining = 3f + (JournalActorId % 3) * 2f;
            ZombieAttackUsesLeftHand = false;
            if (zombieVoice != null) zombieVoice.Stop();
        }
    }
}
