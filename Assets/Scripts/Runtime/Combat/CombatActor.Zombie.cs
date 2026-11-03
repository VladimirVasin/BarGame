using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private const float ZombieRamImpulse = 85f, ZombieBlockedRamImpulse = 26f;
        public static MeleeCombatSettings OrdinaryZombieCombatSettings { get; } = new MeleeCombatSettings(
            damage: 18f, blockCost: 14f, windupSeconds: .6f, activeSeconds: .22f,
            recoverySeconds: 3.2f, hitRecoverySeconds: 3.2f, blockRecoverySeconds: 3.2f,
            obstacleRecoverySeconds: 3.2f, parriedRecoverySeconds: 3.2f,
            animationRecoverySeconds: .95f, staggerSeconds: .8f,
            isUnarmedZombie: true, incomingDamageScale: .5f);
        public static MeleeCombatSettings DegradedZombieCombatSettings { get; } = new MeleeCombatSettings(
            damage: 18f, blockCost: 14f, windupSeconds: .95f, activeSeconds: .22f,
            recoverySeconds: 3.2f, hitRecoverySeconds: 3.2f, blockRecoverySeconds: 3.2f,
            obstacleRecoverySeconds: 3.2f, parriedRecoverySeconds: 3.2f,
            animationRecoverySeconds: .95f, staggerSeconds: .8f,
            isUnarmedZombie: true, incomingDamageScale: .5f);
        public static MeleeCombatSettings SelfCarrierZombieCombatSettings { get; } = new MeleeCombatSettings(
            damage: 24f, blockCost: 18f, windupSeconds: 1.25f, activeSeconds: .26f,
            recoverySeconds: 4.2f, hitRecoverySeconds: 4.2f, blockRecoverySeconds: 4.2f,
            obstacleRecoverySeconds: 4.2f, parriedRecoverySeconds: 4.2f,
            animationRecoverySeconds: 1.25f, staggerSeconds: .8f,
            isUnarmedZombie: true, incomingDamageScale: .5f);
        public int OrdinaryZombieVariant { get; private set; }
        private float OrdinaryZombieStrideDistance => IsOrdinaryZombie && OrdinaryZombieVariant == 2
            ? CombatZombieAssetProvider.CarrierWalkCycleDistance : CombatZombieAssetProvider.OrdinaryWalkCycleDistance;
        private float OrdinaryZombieStrideSeconds => IsOrdinaryZombie && OrdinaryZombieVariant == 2
            ? CombatZombieAssetProvider.CarrierWalkCycleSeconds : CombatZombieAssetProvider.OrdinaryWalkCycleSeconds;
        private float ZombiePalmRadius => .065f * PresentationScale;
        private AnimationClip zombieWalk, zombieAttackRight, zombieAttackLeft, zombieLoadAdjust, zombieWatch;
        private Transform zombieLeftShoulder, zombieRightShoulder;
        private Vector3 zombieHandFrom, zombieHandTo;
        private CombatZombieLoad zombieLoad;
        internal bool CorpsePropsResting => zombieLoad == null || zombieLoad.CorpsePartsResting;
        internal void CollectCorpsePropRenderers(List<Renderer> target) => zombieLoad?.CollectCorpseRenderers(target);
        private Pose zombieContactFrom, zombieContactTo;
        private Collider zombieHeadShape;
        private Vector3 zombieHeadFrom, zombieHeadTo;
        private bool zombieContactPoseValid;
        private float zombieWalkClock, zombieGroanRemaining, zombieWorkClock, roomIdleClock;
        private bool zombieWalkingRequested;
        private int zombiePresentedCrowbarResponse = -1;
        private CombatZombieAudio zombieAudio;
        private NpcFootstepProfile zombieFootsteps;
        public bool ZombieAttackUsesLeftSide { get; private set; }
        internal bool ZombieWorkPause => IsDocker && !RoomAttackSuppressed && !IsZombieFrenzied && State.Phase == MeleePhase.Ready && !roundEnded &&
            !zombieWalkingRequested && (contactTarget == null || contactTarget.State.IsDefeated) &&
            (zombieWorkClock >= 4f && zombieWorkClock < 6.4f || zombieWorkClock >= 9f && zombieWorkClock < 11.8f);

        private void LoadZombieClips()
        {
            if (IsOrdinaryZombie)
            {
                ready = rest = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.DegradedIdleClip, OrdinaryZombieVariant);
                zombieWalk = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.DegradedWalkClip, OrdinaryZombieVariant);
                zombieAttackRight = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.DegradedAttackRightClip, OrdinaryZombieVariant);
                zombieAttackLeft = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.DegradedAttackLeftClip, OrdinaryZombieVariant);
                hit = guardBreak = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.DegradedHitClip, OrdinaryZombieVariant);
                zombieDormant = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.OrdinaryDormantClip(OrdinaryZombieVariant));
                zombieReveal = CombatZombieAssetProvider.LoadDegradedClip(CombatZombieAssetProvider.OrdinaryRiseClip(OrdinaryZombieVariant));
                defeat = CombatAssetProvider.LoadClip("CombatDefeat", true);
                return;
            }
            if (!IsDocker)
            {
                // Ready/0 remains the canonical anatomical baseline. Only the
                // Room's visible stationary pose uses its own planted loop.
                ready = CombatZombieAssetProvider.LoadOrdinaryClip(CombatZombieAssetProvider.OrdinaryIdleClip);
                rest = IsRoom ? CombatRoomAttackAssetProvider.LoadNpcClip(CombatRoomAttackAssetProvider.IdleClip) : ready;
                zombieWalk = CombatZombieAssetProvider.LoadOrdinaryClip(CombatZombieAssetProvider.OrdinaryWalkClip);
                zombieAttackRight = CombatZombieAssetProvider.LoadOrdinaryClip(CombatZombieAssetProvider.OrdinaryAttackRightClip);
                zombieAttackLeft = CombatZombieAssetProvider.LoadOrdinaryClip(CombatZombieAssetProvider.OrdinaryAttackLeftClip);
                hit = guardBreak = CombatZombieAssetProvider.LoadOrdinaryClip(CombatZombieAssetProvider.OrdinaryHitClip);
                // The shared death/recovery bank uses this same undeformed skeleton.
                defeat = CombatAssetProvider.LoadClip("CombatDefeat", true);
                return;
            }
            zombieDormant = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.DormantClip);
            zombieReveal = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.RevealClip);
            ready = rest = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.IdleClip);
            zombieWalk = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.WalkClip);
            zombieAttackRight = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.AttackRightClip);
            zombieAttackLeft = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.AttackLeftClip);
            zombieLoadAdjust = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.LoadAdjustClip);
            zombieWatch = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.WatchClip);
            zombieFrenzy = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.FrenzyClip);
            hit = guardBreak = CombatZombieAssetProvider.LoadClip(CombatZombieAssetProvider.HitClip);
            // Keep the deformed proportions through the short physical handoff.
            // The ordinary death bank carries normal limb scales and translations.
            defeat = ready;
        }

        private void InitializeZombieHands()
        {
            handPose = npc.RightGrip.GetComponentInParent<NpcHandPose>();
            if (handPose == null) throw new InvalidOperationException("A zombie requires the shared anatomical hands.");
            InitializeRoomHandContacts();
            if (IsDocker)
            {
                zombieLoad = gameObject.AddComponent<CombatZombieLoad>();
                zombieLoad.Initialize(this);
            }
            else
            {
                zombieLeftShoulder = CityPedestrianHandProps.FindSocket(DamageRigRoot, "upper_arm.L");
                zombieRightShoulder = CityPedestrianHandProps.FindSocket(DamageRigRoot, "upper_arm.R");
                if (zombieLeftShoulder == null || zombieRightShoulder == null)
                    throw new InvalidOperationException("An ordinary zombie requires both retained shoulders.");
            }
            foreach (var shape in Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head) { zombieHeadShape = shape.Key; break; }
            if (zombieHeadShape == null)
                throw new InvalidOperationException("A zombie requires his original physical head shape.");
            zombieFootsteps = npc.gameObject.AddComponent<NpcFootstepProfile>();
            zombieFootsteps.Configure((IsDocker ? CombatZombieAssetProvider.WalkCycleDistance :
                OrdinaryZombieStrideDistance * PresentationScale) * .5f, .08f, 1f, true);
            handPose.SetGrip(false, IsDocker ? .12f : 0f); handPose.SetGrip(true, IsDocker ? .12f : 0f);
            zombieAudio = gameObject.AddComponent<CombatZombieAudio>();
            zombieAudio.Initialize(zombieLoad != null ? zombieLoad.Model : npc.transform, IsOrdinaryZombie, OrdinaryZombieVariant);
            zombieLoad?.BindMaterialAudio(zombieAudio);
            if (IsDocker) InitializeZombieFrenzyEntry();
            ResetZombieAttack();
        }

        public bool TryZombieAttack()
        {
            if (!IsZombie || !ZombieAwake || roundEnded || State.Phase != MeleePhase.Ready || !IsAvailable ||
                !CanAttemptBodyAction || !HasAttackBalance || !GameInput.CanRead(GameInputContext.Gameplay) ||
                !(BodyDamage.CanUseRightHand || BodyDamage.CanUseLeftHand)) return false;
            if (!State.RequestAttack()) return false;
            zombieAudio?.Attack();
            // Alternate when both hands work; a surviving hand remains usable.
            ZombieAttackUsesLeftSide = !BodyDamage.CanUseRightHand ||
                BodyDamage.CanUseLeftHand && State.Swing == MeleeSwing.Backhand;
            reaction = null;
            zombieContactPoseValid = false;
            Present();
            // Capture the visible palm or the fused head/timber at strike entry.
            CaptureZombieContactPose();
            JournalEvent("zombie_attack_started", contactTarget?.JournalActorId ?? 0, State.AttackSequence,
                f0: GameLog.Field("left_side", ZombieAttackUsesLeftSide));
            return true;
        }

        /// <summary>Promotion transfers the already visible idle clock after bind-anatomy setup.</summary>
        internal void RestoreOrdinaryZombieIdlePhase(float elapsed)
        {
            if (!IsOrdinaryZombie || !float.IsFinite(elapsed) || elapsed < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            if (!ZombieAwake || State.Phase != MeleePhase.Ready || IsKnockedDown || IsRagdollActive) return;
            poseClock = Mathf.Repeat(elapsed, rest.length);
            visibleClip = null;
            CancelPoseBlend();
            Present();
        }

        private void AdvanceZombiePresentation(float seconds)
        {
            if (roomAttackPoseActive) return;
            if (IsZombieFrenzied) { AdvanceZombieFrenzy(seconds); return; }
            if (IsRoom)
                roomIdleClock = State.Phase == MeleePhase.Ready && !zombieWalkingRequested
                    ? Mathf.Repeat(roomIdleClock + seconds, rest.length) : 0f;
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

        private void PresentZombie()
        {
            RestoreCorpseStepPose();
            footwork?.Restore(); damagePose?.Restore(); bodyMotion?.Restore();
            bool stagger = State.IsStunned;
            bool crowbarRecoil = stagger && (ImpactMotion?.UsesCrowbarContactReaction ?? false);
            bool freshCrowbarContact = crowbarRecoil && zombiePresentedCrowbarResponse != ImpactMotion.RecoverySequence;
            AnimationClip chosen = State.IsDefeated ? defeat : stagger ? hit : IsZombieFrenzied ? zombieFrenzy : State.IsAttacking
                ? ZombieAttackUsesLeftSide ? zombieAttackLeft : zombieAttackRight
                : ZombieWorkPause ? zombieWorkClock < 9f ? zombieLoadAdjust : zombieWatch
                : zombieWalkingRequested && !roundEnded ? zombieWalk : rest;
            float time = State.IsDefeated ? Mathf.Min(defeatClock, chosen.length) : stagger
                ? ZombieHitSampleTime(chosen.length) : IsZombieFrenzied ? ZombieFrenzySampleTime : State.IsAttacking ? State.AttackProgress * chosen.length
                : chosen == zombieLoadAdjust ? zombieWorkClock - 4f : chosen == zombieWatch ? zombieWorkClock - 9f
                : Mathf.Repeat(chosen == zombieWalk ? zombieWalkClock : IsRoom && chosen == rest ? roomIdleClock : poseClock, chosen.length);
            if (freshCrowbarContact)
            {
                // Carry the struck silhouette into the contact frame itself.
                // The remaining blend and hit clip continue on the duel clock;
                // presenting the frozen contact again must not restart either.
                BeginPoseBlend(.05f, force: true);
                poseBlendRemaining = Mathf.Min(poseBlendRemaining, .0125f);
                zombiePresentedCrowbarResponse = ImpactMotion.RecoverySequence;
                visibleClip = chosen.name;
            }
            else if (visibleClip != chosen.name || !IsZombieFrenzied && visibleAttackSequence != State.AttackSequence)
            { BeginPoseBlend(IsZombieFrenzied ? .1f : State.IsAttacking ? .045f : .08f); visibleClip = chosen.name; }
            SampleNpcClip(chosen, time);
            long compositionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            // Corpse clips own their slouch and loose arms; a living fighter's
            // gait/breath layer must not straighten that authored silhouette.
            if (!crowbarRecoil && (ImpactMotion?.IsActive ?? false)) bodyMotion?.Apply();
            PresentDamagePose();
            bool recoveryFeet = footwork != null &&
                (footwork.RecoveryEpisodeActive || (ImpactMotion?.IsActive ?? false));
            if (recoveryFeet) footwork.Apply();
            // Looking back at the hero would erase the impact's head whip.
            // The loop's face stays in its own chest, and the carrier's head
            // belongs to its weight. Target gaze must not straighten either body.
            if (!IsOrdinaryZombie && !IsZombieFrenzied && !crowbarRecoil) ApplyCombatAttention();
            ApplyNpcPoseBlend();
            if (recoveryFeet) footwork.ConstrainContacts();
            ApplyCorpseStepPose();
            zombieLoad?.RefreshRamProxyState();
            handPose.SetGrip(false, IsDocker ? .12f : 0f);
            handPose.SetGrip(true, IsDocker ? .12f : 0f);
            visibleAttackSequence = State.AttackSequence;
            NpcPoseCompositionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - compositionStarted;
            RememberNpcPresentedPose();
            footwork?.CapturePresentedContacts();
        }

        private float ZombieHitSampleTime(float clipLength)
        {
            // Both clips reach their recoil at .09 s. A resolved crowbar contact
            // starts inside that compression and reaches its peak promptly;
            // the remainder still returns over the existing gameplay stun.
            float authoredOnset = Mathf.Min(.09f, clipLength);
            bool crowbarRecoil = ImpactMotion?.UsesCrowbarContactReaction ?? false;
            float onset = Mathf.Min(crowbarRecoil ? .045f : authoredOnset, State.StaggerDuration);
            float elapsed = State.StaggerElapsed;
            if (elapsed <= onset) return crowbarRecoil
                ? Mathf.Lerp(authoredOnset * .65f, authoredOnset, elapsed / Mathf.Max(.0001f, onset))
                : elapsed;
            return Mathf.Lerp(authoredOnset, clipLength,
                (elapsed - onset) / Mathf.Max(.0001f, State.StaggerDuration - onset));
        }

        private void CaptureZombieContactPose()
        {
            if (!IsDocker)
            {
                Vector3 palm = handPose.CylinderCentre(ZombieAttackUsesLeftSide);
                zombieHandFrom = zombieContactPoseValid ? zombieHandTo : palm;
                zombieHandTo = palm;
                if (IsRoom)
                {
                    Vector3 distal = RoomDistalHandContactPoint(ZombieAttackUsesLeftSide);
                    roomHandTipFrom = zombieContactPoseValid ? roomHandTipTo : distal;
                    roomHandTipTo = distal;
                }
                zombieContactPoseValid = true;
                return;
            }
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
            if (IsZombieFrenzied || to < State.AttackWindupSeconds || from >= State.AttackActiveEnd ||
                sequence != State.AttackSequence || !zombieContactPoseValid ||
                !(ZombieAttackUsesLeftSide ? BodyDamage.CanUseLeftHand : BodyDamage.CanUseRightHand)) return;
            if (!IsDocker) { SweepOrdinaryZombieHand(from, to, sequence, pending); return; }
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

        private void SweepOrdinaryZombieHand(float from, float to, int sequence, List<Contact> pending)
        {
            Vector3 a = zombieHandFrom, b = zombieHandTo;
            float fraction = float.PositiveInfinity;
            CombatActor nearest = null; CombatHurtboxes.Hit bodyHit = default;
            for (int path = 0; path < (IsRoom ? 2 : 1); path++)
            {
                Vector3 start = path == 0 ? a : roomHandTipFrom, end = path == 0 ? b : roomHandTipTo;
                for (int i = 0; i < ContactParticipantCount; i++)
                {
                    CombatActor candidate = ContactParticipantAt(i);
                    if (candidate == this || !HasPhysicalBody(candidate) ||
                        candidate.IsKnockedDown && candidate.State.Phase != MeleePhase.Rising ||
                        !candidate.Hurtboxes.SweepSphere(start, end, ZombiePalmRadius, transform.forward, out var found) ||
                        found.Fraction >= fraction) continue;
                    nearest = candidate; bodyHit = found; fraction = found.Fraction;
                }
            }
            float wallFraction = float.PositiveInfinity;
            Vector3 wallPoint = default, wallNormal = default;
            // Shared grow-and-retry buffers also cover a hand already inside a wall.
            for (int path = 0; path < (IsRoom ? 2 : 1); path++)
            {
                Vector3 start = path == 0 ? a : roomHandTipFrom, end = path == 0 ? b : roomHandTipTo;
                float pathWall = float.PositiveInfinity;
                Vector3 pathPoint = default, pathNormal = default;
                GatherWorldSegment(start, start, ZombiePalmRadius, ref pathWall, ref pathPoint, ref pathNormal);
                if (pathWall <= 1f) pathWall = 0f;
                GatherWorldSweep(start, end, ZombiePalmRadius, ref pathWall, ref pathPoint, ref pathNormal);
                GatherWorldSegment(end, end, ZombiePalmRadius, ref pathWall, ref pathPoint, ref pathNormal);
                if (pathWall < wallFraction)
                { wallFraction = pathWall; wallPoint = pathPoint; wallNormal = pathNormal; }
            }
            Transform shoulder = ZombieAttackUsesLeftSide ? zombieLeftShoulder : zombieRightShoulder;
            float armWall = float.PositiveInfinity;
            GatherWorldSweep(shoulder.position, b, .035f, ref armWall, ref wallPoint, ref wallNormal);
            if (armWall <= 1f) wallFraction = 0f;
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
            zombieLoad?.EndRam();
            collectSweep = false; zombieContactPoseValid = false;
        }

        internal void RefreshZombieLoadPhysics() => zombieLoad?.RefreshPhysicsCollisions();

        internal Vector3 LimitZombieLoadTravel(Vector3 travel) => zombieLoad != null
            ? zombieLoad.LimitTravel(travel) : travel;

        internal float GroundObstacleRadius => zombieLoad != null && zombieLoad.IsCarrying ? zombieLoad.NavigationRadius : 0f;
        internal float GroundObstacleHeight => zombieLoad != null && zombieLoad.IsCarrying ? zombieLoad.NavigationHeight : 0f;

        internal void SetZombieWalking(bool walking) => zombieWalkingRequested = walking && ZombieAwake && !RoomAttackSuppressed;

        internal float PreviewZombieWalkDistance(float seconds) => IsDocker
            ? CombatZombieAssetProvider.WalkTravel(zombieWalkClock + seconds) - CombatZombieAssetProvider.WalkTravel(zombieWalkClock)
            : seconds * OrdinaryZombieStrideDistance / OrdinaryZombieStrideSeconds * PresentationScale;

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
            float cycle = IsDocker ? CombatZombieAssetProvider.WalkCycleSeconds : OrdinaryZombieStrideSeconds;
            float rightContact = cycle * (IsDocker ? .42f : .5f);
            float leftContact = cycle * (IsDocker ? .92f : 1f);
            if (zombieWalkClock < rightContact && end >= rightContact || zombieWalkClock < leftContact && end >= leftContact)
                zombieFootsteps?.NotifyFootfall();
            zombieWalkClock = Mathf.Repeat(end, cycle);
        }

        internal Vector3 LimitZombieRamTravel(Vector3 travel, CombatActor target, float seconds)
        {
            if (!IsDocker) return travel;
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
            zombiePresentedCrowbarResponse = -1;
            ResetZombieFrenzy();
            ResetZombieMimic();
            zombieLoad?.EndRam();
            zombieContactPoseValid = false;
            zombieContactFrom = zombieContactTo = default;
            zombieHeadFrom = zombieHeadTo = default;
            zombieHandFrom = zombieHandTo = default;
            zombieWalkClock = 0f; zombieGroanRemaining = 3f + (JournalActorId % 3) * 2f;
            roomIdleClock = 0f;
            zombieWalkingRequested = false;
            zombieFootsteps?.ResetContacts();
            zombieWorkClock = JournalActorId % 3;
            ZombieAttackUsesLeftSide = false;
            zombieAudio?.Reset();
            zombieAudio?.Activate();
        }
    }
}
