using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private AnimationClip zombieWalk, zombieAttackRight, zombieAttackLeft, zombieLoadAdjust, zombieWatch;
        private CombatZombieLoad zombieLoad;
        private Pose zombieContactFrom, zombieContactTo;
        private Collider zombieHeadShape;
        private Vector3 zombieHeadFrom, zombieHeadTo;
        private bool zombieContactPoseValid;
        private float zombieWalkClock, zombieGroanRemaining, zombieWorkClock;
        private bool zombieWalkingRequested;
        private CombatZombieAudio zombieAudio;
        private NpcFootstepProfile zombieFootsteps;
        public bool ZombieAttackUsesLeftSide { get; private set; }
        internal bool ZombieWorkPause => IsZombie && State.Phase == MeleePhase.Ready && !roundEnded &&
            (contactTarget == null || (contactTarget.BodyWorldPosition - BodyWorldPosition).sqrMagnitude > 2.3f * 2.3f) &&
            (zombieWorkClock >= 4f && zombieWorkClock < 6.4f || zombieWorkClock >= 9f && zombieWorkClock < 11.8f);

        private void LoadZombieClips()
        {
            zombieDormant = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.DormantClip);
            zombieReveal = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.RevealClip);
            ready = rest = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.IdleClip);
            zombieWalk = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.WalkClip);
            zombieAttackRight = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.AttackRightClip);
            zombieAttackLeft = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.AttackLeftClip);
            zombieLoadAdjust = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.LoadAdjustClip);
            zombieWatch = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.WatchClip);
            hit = guardBreak = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.HitClip);
            // Keep the deformed proportions through the short physical handoff.
            // The ordinary death bank carries normal limb scales and translations.
            defeat = ready;
        }

        private void InitializeZombieHands()
        {
            handPose = npc.RightGrip.GetComponentInParent<NpcHandPose>();
            if (handPose == null) throw new InvalidOperationException("A zombie requires the shared anatomical hands.");
            zombieLoad = gameObject.AddComponent<CombatZombieLoad>();
            zombieLoad.Initialize(this);
            foreach (var shape in Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head) { zombieHeadShape = shape.Key; break; }
            if (zombieHeadShape == null)
                throw new InvalidOperationException("A fused porter requires his original physical head shape.");
            zombieFootsteps = npc.gameObject.AddComponent<NpcFootstepProfile>();
            zombieFootsteps.Configure(CombatZombieAssetProvider.WalkCycleDistance * .5f, .08f, 1f, true);
            handPose.SetGrip(false, .12f); handPose.SetGrip(true, .12f);
            zombieAudio = gameObject.AddComponent<CombatZombieAudio>();
            zombieAudio.Initialize(zombieLoad.Model);
            zombieLoad.BindMaterialAudio(zombieAudio);
            ResetZombieAttack();
        }

        public bool TryZombieAttack()
        {
            if (!IsZombie || !ZombieAwake || roundEnded || State.Phase != MeleePhase.Ready || !IsAvailable ||
                !CanAttemptBodyAction || !HasAttackBalance || !GameInput.CanRead(GameInputContext.Gameplay) ||
                !(BodyDamage.CanUseRightHand || BodyDamage.CanUseLeftHand)) return false;
            if (!State.RequestAttack()) return false;
            ZombieAttackUsesLeftSide = State.Swing == MeleeSwing.Backhand;
            reaction = null;
            zombieContactPoseValid = false;
            Present();
            // Both the exposed head and front timber belong to the fused torso.
            CaptureZombieContactPose();
            JournalEvent("zombie_attack_started", contactTarget?.JournalActorId ?? 0, State.AttackSequence,
                f0: GameLog.Field("left_side", ZombieAttackUsesLeftSide));
            return true;
        }

        private void AdvanceZombiePresentation(float seconds)
        {
            if (State.Phase == MeleePhase.Ready)
            {
                zombieWorkClock = Mathf.Repeat(zombieWorkClock + seconds, 14f);
            }
            zombieGroanRemaining -= seconds;
            if (zombieGroanRemaining <= 0f && State.Phase == MeleePhase.Ready && !roundEnded)
            {
                zombieGroanRemaining = 7f + (JournalActorId % 3) * 2f;
                zombieAudio?.AliveGroan();
            }
        }

        private void PlayZombieLunge()
        {
            zombieAudio?.Attack();
        }

        private void PresentZombie()
        {
            RestoreCorpseStepPose();
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            bool stagger = State.IsStunned;
            AnimationClip chosen = State.IsDefeated ? defeat : stagger ? hit : State.IsAttacking
                ? ZombieAttackUsesLeftSide ? zombieAttackLeft : zombieAttackRight
                : ZombieWorkPause ? zombieWorkClock < 9f ? zombieLoadAdjust : zombieWatch
                : zombieWalkingRequested && !roundEnded ? zombieWalk : ready;
            float time = State.IsDefeated ? Mathf.Min(defeatClock, chosen.length) : stagger
                ? State.PhaseProgress * chosen.length : State.IsAttacking ? State.AttackProgress * chosen.length
                : chosen == zombieLoadAdjust ? zombieWorkClock - 4f : chosen == zombieWatch ? zombieWorkClock - 9f
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
            zombieLoad.RefreshRamProxyState();
            handPose.SetGrip(false, .12f);
            handPose.SetGrip(true, .12f);
            visibleAttackSequence = State.AttackSequence;
            RememberNpcPresentedPose();
            footwork?.CapturePresentedContacts();
        }

        private void CaptureZombieContactPose()
        {
            Pose pose = zombieLoad.ContactPose;
            zombieContactFrom = zombieContactPoseValid ? zombieContactTo : pose;
            Vector3 head = ZombieRamHeadPoint;
            zombieHeadFrom = zombieContactPoseValid ? zombieHeadTo : head;
            zombieHeadTo = head;
            zombieContactTo = pose; zombieContactPoseValid = true;
        }

        internal Vector3 ZombieRamHeadPoint => zombieHeadShape.transform.TransformPoint(zombieHeadShape switch
        {
            BoxCollider box => box.center,
            CapsuleCollider capsule => capsule.center,
            SphereCollider sphere => sphere.center,
            _ => throw new InvalidOperationException("Unsupported porter head shape.")
        });

        internal float ZombieRamHeadRadius
        {
            get
            {
                Vector3 scale = zombieHeadShape.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                if (zombieHeadShape is BoxCollider box)
                {
                    Vector3 size = Vector3.Scale(box.size, scale);
                    return Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * .5f;
                }
                if (zombieHeadShape is CapsuleCollider capsule)
                    return capsule.radius * Mathf.Min(scale[(capsule.direction + 1) % 3], scale[(capsule.direction + 2) % 3]);
                if (zombieHeadShape is SphereCollider sphere)
                    return sphere.radius * Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
                throw new InvalidOperationException("Unsupported porter head shape.");
            }
        }

        private void SweepZombieContact(float from, float to, int sequence, List<Contact> pending)
        {
            if (to < State.AttackWindupSeconds || from >= State.AttackActiveEnd ||
                sequence != State.AttackSequence || !zombieContactPoseValid ||
                !(ZombieAttackUsesLeftSide ? BodyDamage.CanUseLeftHand : BodyDamage.CanUseRightHand)) return;
            float fraction = float.PositiveInfinity;
            CombatActor nearest = null; CombatHurtboxes.Hit bodyHit = default;
            for (int i = 0; i < ContactParticipantCount; i++)
            {
                CombatActor candidate = ContactParticipantAt(i);
                if (candidate == this || !HasPhysicalBody(candidate) ||
                    candidate.IsKnockedDown && candidate.State.Phase != MeleePhase.Rising) continue;
                if (CombatWeaponGeometry.SweepAnatomy(candidate.Hurtboxes, zombieLoad.ContactSegments,
                        zombieContactFrom, zombieContactTo, Mathf.Max(.0001f, to - from), transform.forward,
                        out var found, out float hitFraction, out _) && hitFraction < fraction)
                { nearest = candidate; bodyHit = found; fraction = hitFraction; }
                // The face protrudes ahead of the embedded box. Its conservative
                // sphere is INSIDE the original anatomical head collider.
                if (candidate.Hurtboxes.SweepSphere(zombieHeadFrom, zombieHeadTo,
                        ZombieRamHeadRadius + CombatWeaponGeometry.ContactSkin, transform.forward, out var headHit) &&
                    headHit.Fraction < fraction)
                { nearest = candidate; bodyHit = headHit; fraction = headHit.Fraction; }
            }
            float wallFraction = float.PositiveInfinity;
            Vector3 wallPoint = default, wallNormal = default;
            float headRadius = ZombieRamHeadRadius + CombatWeaponGeometry.ContactSkin;
            GatherWorldSegment(zombieHeadFrom, zombieHeadFrom, headRadius, ref wallFraction, ref wallPoint, ref wallNormal);
            if (wallFraction <= 1f) wallFraction = 0f;
            GatherWorldSweep(zombieHeadFrom, zombieHeadTo, headRadius, ref wallFraction, ref wallPoint, ref wallNormal);
            GatherWorldSegment(zombieHeadTo, zombieHeadTo, headRadius, ref wallFraction, ref wallPoint, ref wallNormal);
            float headThroughWall = float.PositiveInfinity;
            GatherWorldSweep(transform.position + Vector3.up * .8f, zombieHeadTo, .02f,
                ref headThroughWall, ref wallPoint, ref wallNormal);
            if (headThroughWall <= 1f) wallFraction = 0f;
            for (int railIndex = 0; railIndex < zombieLoad.ContactSegments.Count; railIndex++)
            {
                CombatWeaponGeometry.Segment rail = zombieLoad.ContactSegments[railIndex];
                Vector3 a = zombieContactTo.position + zombieContactTo.rotation * rail.A;
                Vector3 b = zombieContactTo.position + zombieContactTo.rotation * rail.B;
                Vector3 oldA = zombieContactFrom.position + zombieContactFrom.rotation * rail.A;
                Vector3 oldB = zombieContactFrom.position + zombieContactFrom.rotation * rail.B;
                float radius = rail.Radius + CombatWeaponGeometry.ContactSkin;
                GatherWorldSegment(oldA, oldB, radius, ref wallFraction, ref wallPoint, ref wallNormal);
                if (wallFraction <= 1f) wallFraction = 0f;
                GatherWorldSweep(oldA, a, radius, ref wallFraction, ref wallPoint, ref wallNormal);
                GatherWorldSweep(oldB, b, radius, ref wallFraction, ref wallPoint, ref wallNormal);
                GatherWorldSegment(a, b, radius, ref wallFraction, ref wallPoint, ref wallNormal);
                // A torso rotated through a wall cannot strike behind it.
                float throughWall = float.PositiveInfinity;
                GatherWorldSweep(transform.position + Vector3.up * .8f, (a + b) * .5f, .02f,
                    ref throughWall, ref wallPoint, ref wallNormal);
                if (throughWall <= 1f) wallFraction = 0f;
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
            zombieLoad.EndRam();
            collectSweep = false; zombieContactPoseValid = false;
        }

        internal void RefreshZombieLoadPhysics() => zombieLoad?.RefreshPhysicsCollisions();

        internal Vector3 LimitZombieLoadTravel(Vector3 travel) => zombieLoad != null
            ? zombieLoad.LimitTravel(travel) : travel;

        internal void SetZombieWalking(bool walking) => zombieWalkingRequested = walking;

        internal float PreviewZombieWalkDistance(float seconds) =>
            CombatZombieAssetProvider.WalkTravel(zombieWalkClock + seconds) - CombatZombieAssetProvider.WalkTravel(zombieWalkClock);

        internal void AdvanceZombieWalkClock(float seconds, float requested, float achieved)
        {
            float elapsed = seconds;
            if (requested > .000001f && achieved + .000001f < requested)
            {
                // Match the same authored root curve after a real wall/crowd
                // limit. A blocked body cannot spend an unsupported foot step.
                float low = 0f, high = seconds;
                for (int i = 0; i < 10; i++)
                {
                    float mid = (low + high) * .5f;
                    if (PreviewZombieWalkDistance(mid) > achieved) high = mid; else low = mid;
                }
                elapsed = low;
            }
            float end = zombieWalkClock + elapsed;
            float rightContact = CombatZombieAssetProvider.WalkCycleSeconds * .42f;
            float leftContact = CombatZombieAssetProvider.WalkCycleSeconds * .92f;
            if (zombieWalkClock < rightContact && end >= rightContact || zombieWalkClock < leftContact && end >= leftContact)
                zombieFootsteps?.NotifyFootfall();
            zombieWalkClock = Mathf.Repeat(end, CombatZombieAssetProvider.WalkCycleSeconds);
        }

        internal Vector3 LimitZombieRamTravel(Vector3 travel, CombatActor target, float seconds)
        {
            zombieLoad.BeginRamAgainst(target.Body);
            Pose from = zombieLoad.ContactPose;
            Pose to = new Pose(from.position + travel, from.rotation);
            float fraction = 1f;
            if (CombatWeaponGeometry.SweepAnatomy(target.Hurtboxes, zombieLoad.ContactSegments,
                from, to, seconds, transform.forward, out _, out float woodFraction, out _))
                fraction = Mathf.Min(fraction, woodFraction);
            Vector3 head = ZombieRamHeadPoint;
            if (target.Hurtboxes.SweepSphere(head, head + travel,
                ZombieRamHeadRadius + CombatWeaponGeometry.ContactSkin, transform.forward, out var headHit))
                fraction = Mathf.Min(fraction, headHit.Fraction);
            return travel * Mathf.Clamp01(fraction);
        }

        private void ResetZombieAttack()
        {
            ResetZombieMimic();
            zombieLoad?.EndRam();
            zombieContactPoseValid = false;
            zombieContactFrom = zombieContactTo = default;
            zombieHeadFrom = zombieHeadTo = default;
            zombieWalkClock = 0f; zombieGroanRemaining = 3f + (JournalActorId % 3) * 2f;
            zombieWalkingRequested = false;
            zombieFootsteps?.ResetContacts();
            zombieWorkClock = JournalActorId % 3;
            ZombieAttackUsesLeftSide = false;
            zombieAudio?.Reset();
            zombieAudio?.Activate();
        }
    }
}
