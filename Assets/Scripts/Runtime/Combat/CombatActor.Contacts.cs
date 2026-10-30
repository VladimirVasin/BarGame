using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private Collider[] contactBuffer = new Collider[32];
        private RaycastHit[] castBuffer = new RaycastHit[32];
        private CombatActor contactTarget;
        private bool collectSweep;
        private float sweepFrom, sweepTo;
        private int pendingSequence;
        private bool sweepValid;
        private int sweepSequence;
        private float lastSweepElapsed;
        private Vector3 previousBase, previousTip;
        private Pose previousWeapon, frozenWeaponFrom, frozenWeaponTo;
        private bool frozenWeaponValid, frozenWeaponActive;
        private int frozenWeaponSequence;
        private float previewWorldFraction;
        private Vector3 previewWorldPoint, previewWorldNormal;
        private Collider previewWorldCollider;
        internal int ContactPoseSamples { get; private set; }
        internal bool HasPendingWeaponContact => collectSweep;
        internal int WeaponClashCount { get; private set; }
        internal int LastWeaponClashSequence { get; private set; }
        internal Vector3 LastWeaponClashPoint { get; private set; }
        internal int BlockedWeaponSwingCount { get; private set; }
        internal float LastRejectedWeaponDepth { get; private set; }
        internal bool LastRejectedWeaponSweep { get; private set; }
        internal CombatWeaponConstraint.OwnForearmPoseDiagnostic LastRejectedWeaponGeometry { get; private set; }
        private int journalContactRejectedSequence = -1;
        private string journalContactRejectedReason;

        internal void SetContactTarget(CombatActor target)
        {
            if (contactTarget == target) return;
            contactTarget = target; weaponConstraint?.SetOpponent(target); heldWeaponPhysics?.SetOpponent(target);
        }
        internal void CaptureContactPose() => CaptureContactPose(true);

        // The root can retain the anatomy already frozen for this substep's
        // aim ray; guard and weapon histories still advance at the contact stage.
        internal void CaptureContactPose(bool captureAnatomy)
        {
            // The final sampled palm can lose support after the input/clock
            // decision. Drop its guard before either actor resolves contacts;
            // restoring a guard still waits for the next presented duel step.
            if (guardHeld && State.IsBlocking && GuardSupportRejection != null) RefreshBlock();
            if (captureAnatomy) Hurtboxes?.Capture();
            if (IsCrane) { CaptureCraneContactPose(); return; }
            if (IsZombie) { CaptureZombieContactPose(); return; }
            Pose pose = new Pose(Weapon.transform.position, Weapon.transform.rotation);
            frozenWeaponFrom = frozenWeaponValid ? frozenWeaponTo : pose;
            frozenWeaponTo = pose; frozenWeaponValid = true;
            frozenWeaponActive = collectSweep && sweepTo >= State.AttackWindupSeconds && sweepFrom < State.AttackActiveEnd;
            frozenWeaponSequence = State.AttackSequence;
        }

        private void ResetWeaponContacts()
        {
            frozenWeaponValid = frozenWeaponActive = false;
            WeaponClashCount = LastWeaponClashSequence = 0; LastWeaponClashPoint = Vector3.zero;
            BlockedWeaponSwingCount = 0;
            LastRejectedWeaponDepth = 0f; LastRejectedWeaponSweep = false;
            LastRejectedWeaponGeometry = default;
        }

        internal bool CollectContacts(List<Contact> pending)
        {
            ContactPoseSamples = 0;
            if (!collectSweep) return false;
            collectSweep = false;
            if (IsCrane) { SweepCraneContact(sweepFrom, sweepTo, pendingSequence, pending); return true; }
            if (IsZombie) { SweepZombieContact(sweepFrom, sweepTo, pendingSequence, pending); return true; }
            SweepWeapon(sweepFrom, sweepTo, pendingSequence, pending);
            return true;
        }

        /// <summary>A registered contact survives interruption of its source in the same simulation step.</summary>
        internal readonly struct Contact
        {
            private readonly CombatActor source, target;
            private readonly bool fromFront;
            private readonly int attackSequence;
            private readonly int request;
            private readonly int sourceGeneration, targetGeneration;
            private readonly Vector3 point, normal, direction;
            private readonly float damage, blockCost, power;
            private readonly MeleeHitLocation location;
            private readonly Player3DAnatomicalPart part;
            private readonly Vector3 localPoint;
            private readonly float weaponSpeed;
            private readonly BodyDamageRegion? bodyRegion;
            private readonly int bodyPatch;
            private readonly bool detachedPart;
            private readonly bool solid, metal, otherActive, physicalBody, guarded;
            private readonly int otherSequence;
            private readonly float time;
            private readonly bool constraintFailure;
            private readonly string constraintShape;
            private readonly Collider worldCollider;

            internal bool IsSolid => solid;
            internal float Time => time;
            internal int Priority => constraintFailure ? 3 : !solid ? 2 : metal ? 1 : 0;
            internal int SourceId => source.GetEntityId().GetHashCode();
            internal bool IsCurrent => source != null && !source.RoomAttackSuppressed && source.ContactGeneration == sourceGeneration && source.isActiveAndEnabled &&
                (target == null || target.ContactGeneration == targetGeneration && target.isActiveAndEnabled);

            public Contact(CombatActor source, CombatActor target, Vector3 point, Vector3 normal, Vector3 direction,
                MeleeHitLocation location, Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso,
                Vector3 localPoint = default, float weaponSpeed = 0f, float time = 0f, bool physicalBody = false,
                BodyDamageRegion? bodyRegion = null, int bodyPatch = -1, bool detachedPart = false)
            {
                this.source = source; this.target = target;
                sourceGeneration = source.ContactGeneration; targetGeneration = target.ContactGeneration;
                this.point = point; this.normal = normal; this.direction = direction;
                this.location = location; this.part = part; this.localPoint = localPoint; this.weaponSpeed = weaponSpeed;
                attackSequence = source.State.AttackSequence;
                request = source.journalActionRequest;
                // The other actor may interrupt this source before Apply.
                // A collected strike keeps the strength that reached its target.
                damage = source.State.AttackDamage;
                blockCost = source.State.AttackBlockCost;
                power = source.State.AttackPower;
                this.time = time; solid = metal = otherActive = false; otherSequence = 0;
                this.physicalBody = physicalBody;
                this.bodyRegion = bodyRegion; this.bodyPatch = bodyPatch; this.detachedPart = detachedPart;
                guarded = false;
                constraintFailure = false; constraintShape = null;
                worldCollider = null;
                fromFront = target.FacingIncomingAttack(source, direction);
            }

            private Contact(CombatActor source, CombatActor target, Vector3 point, Vector3 normal, Vector3 direction,
                float time, bool metal, bool otherActive, Collider worldCollider, float weaponSpeed)
            {
                this.source = source; this.target = target; this.point = point; this.normal = normal; this.direction = direction;
                sourceGeneration = source.ContactGeneration; targetGeneration = target != null ? target.ContactGeneration : 0;
                this.time = time; this.metal = metal; this.otherActive = otherActive; solid = true;
                attackSequence = source.State.AttackSequence; otherSequence = target != null ? target.frozenWeaponSequence : 0;
                request = source.journalActionRequest;
                damage = source.State.AttackDamage; this.weaponSpeed = weaponSpeed;
                blockCost = source.State.AttackBlockCost; power = source.State.AttackPower;
                fromFront = target != null && target.FacingIncomingAttack(source, direction);
                guarded = metal && target != null && target.State.IsBlocking && fromFront;
                physicalBody = false;
                bodyRegion = null; bodyPatch = -1; detachedPart = false;
                location = default; part = default; localPoint = default;
                constraintFailure = false; constraintShape = null;
                this.worldCollider = worldCollider;
            }

            private Contact(CombatActor source, int sequence, string shape)
            {
                this = default;
                this.source = source; sourceGeneration = source.ContactGeneration;
                attackSequence = sequence; constraintFailure = true; constraintShape = shape;
                // Actual contacts from BOTH actors get their normal ordering first.
                time = float.PositiveInfinity;
            }

            internal static Contact RejectedPose(CombatActor source, int sequence, string shape) =>
                new Contact(source, sequence, shape);

            internal static Contact Obstacle(CombatActor source, CombatActor target, Vector3 point, Vector3 normal,
                Vector3 direction, float time, bool metal, bool otherActive = false,
                Collider worldCollider = null, float weaponSpeed = 0f) =>
                new Contact(source, target, point, normal, direction, time, metal, otherActive, worldCollider, weaponSpeed);

            private bool StopsSource(CombatActor actor, int sequence) => solid &&
                ((source == actor && attackSequence == sequence) ||
                 (metal && otherActive && target == actor && otherSequence == sequence));

            internal bool Stops(Contact later) => StopsSource(later.source, later.attackSequence) ||
                (later.metal && later.otherActive && StopsSource(later.target, later.otherSequence));

            public void Apply()
            {
                if (!IsCurrent) return;
                if (constraintFailure)
                {
                    source.RecoverBlockedWeaponSwing(attackSequence, constraintShape);
                    return;
                }
                if (solid)
                {
                    if (!metal && worldCollider != null)
                        CombatZombieLoad.TryReceiveHit(worldCollider, point,
                            CombatImpactMotion.ResolveImpulse(direction, power, weaponSpeed, MeleeHitResult.Hit), damage);
                    MeleeHitResult interception = guarded
                        ? target.ReceiveWeaponGuard(source, attackSequence, point, normal, direction, blockCost, power)
                        : MeleeHitResult.Ignored;
                    if (interception == MeleeHitResult.Parried)
                    {
                        source.State.RecordAttackOutcome(interception, attackSequence);
                        source.ShowParried(point, direction);
                    }
                    else source.StopWeaponOnSolid(attackSequence, point, normal, metal);
                    if (metal && target != null)
                    {
                        if (otherActive) target.StopWeaponOnSolid(otherSequence, point, -normal, true);
                        source.RecordWeaponClash(attackSequence, point);
                        target.RecordWeaponClash(otherSequence, point);
                        source.GetComponentInParent<CombatTestRoot>()?.SparkEffects?.Emit(point, normal, direction);
                    }
                    RetroAudio.PlayAt(source.IsZombie ? RetroSfxId.SpadeBite : RetroSfxId.SpadeGlance, point, .6f);
                    return;
                }
                MeleeHitResult result = target.Receive(source, fromFront, attackSequence, point, normal, direction,
                    damage, blockCost, power, location, part, localPoint, weaponSpeed, physicalBody,
                    bodyRegion, bodyPatch, detachedPart);
                source.JournalEvent("weapon_contact_resolved", target.JournalActorId, attackSequence, request,
                    GameLog.Field("result", (int)result), GameLog.Field("impact_seq", target.LastJournalImpactSequence),
                    GameLog.Field("from_front", fromFront), GameLog.Field("damage_requested", damage));
                source.State.RecordAttackOutcome(result, attackSequence);
                source.PlayCraneContactSound(result);
                if (source.IsCrane) source.ConfirmCraneHookContact(target, attackSequence, result, part, localPoint, point);
                if (result == MeleeHitResult.Parried) source.ShowParried(point, direction);
            }
        }

        /// <summary>Both actors have already collected immutable contacts. Earlier
        /// hard interceptions suppress later body hits and the duplicate other-side
        /// report of a mutual clash, without undoing an earlier real body contact.</summary>
        internal static void ApplyContacts(List<Contact> contacts)
        {
            contacts.Sort((a, b) =>
            {
                int time = a.Time.CompareTo(b.Time);
                if (time != 0) return time;
                int priority = a.Priority.CompareTo(b.Priority);
                return priority != 0 ? priority : a.SourceId.CompareTo(b.SourceId);
            });
            int accepted = 0;
            for (int i = 0; i < contacts.Count; i++)
            {
                Contact contact = contacts[i];
                if (!contact.IsCurrent) continue;
                bool stopped = false;
                for (int j = 0; j < accepted; j++)
                    if (contacts[j].Stops(contact)) { stopped = true; break; }
                if (stopped) continue;
                contact.Apply(); contacts[accepted++] = contact;
            }
            if (accepted < contacts.Count) contacts.RemoveRange(accepted, contacts.Count - accepted);
        }

        private void RecordWeaponClash(int sequence, Vector3 point)
        {
            WeaponClashCount++; LastWeaponClashSequence = sequence; LastWeaponClashPoint = point;
        }

        private MeleeHitResult ReceiveWeaponGuard(CombatActor source, int sequence, Vector3 point, Vector3 normal,
            Vector3 direction, float blockCost, float power)
        {
            if (!FacingIncomingAttack(source, direction)) return MeleeHitResult.Ignored;
            return ReceiveTimedParry(source, sequence, point, normal, direction, power);
        }

        private void StopWeaponOnSolid(int sequence, Vector3 point, Vector3 normal, bool metal)
        {
            if (IsCrane) { if (State.AttackSequence == sequence) StopCraneStrike(); return; }
            if (IsZombie) { if (State.AttackSequence == sequence) StopZombieAttack(); return; }
            if (State.AttackSequence != sequence || !State.CancelAttackOnObstacle()) return;
            reaction = Current.Recoil; reactionClock = 0f; sweepValid = collectSweep = false;
            JournalEvent("weapon_contact_terminal", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                GameLog.Field("reason", metal ? "weapon_blocked" : "world_blocked"),
                GameLog.Field("point_x", point.x), GameLog.Field("point_y", point.y), GameLog.Field("point_z", point.z));
        }

        private void RecoverBlockedWeaponSwing(int sequence, string shape)
        {
            if (State.AttackSequence != sequence ||
                State.AttackOutcome is not (MeleeAttackOutcome.None or MeleeAttackOutcome.Miss) ||
                !State.CancelAttackOnObstacle()) return;
            BlockedWeaponSwingCount++;
            reaction = Current.Recoil; reactionClock = 0f; sweepValid = collectSweep = false;
            // Inherit the physically stopped arm rather than an authored active
            // endpoint that the rejected arm never reached.
            BeginPoseBlend(force: true);
            JournalEvent("weapon_contact_terminal", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                GameLog.Field("reason", "pose_blocked"), GameLog.Field("shape", shape),
                GameLog.Field("desired_depth", LastRejectedWeaponDepth), GameLog.Field("desired_sweep_blocked", LastRejectedWeaponSweep));
        }

        private void SweepWeapon(float from, float to, int sequence, List<Contact> pending)
        {
            float activeEnd = State.AttackActiveEnd;
            if (from >= activeEnd) { sweepValid = false; return; }
            to = Mathf.Min(to, activeEnd);
            if (to < from) from = 0f;
            // Elapsed times are rounded from the double rules clock to floats.
            // Their difference can be just over one step: Ceil then doubles the
            // entire pose/IK/clearance pass despite there being no extra motion.
            int samples = Mathf.Max(1, Mathf.CeilToInt((to - from) / CombatTestRoot.SimulationStep - .0001f));
            if (sweepSequence != sequence) sweepValid = false;
            sweepSequence = sequence;
            // Adjacent 120 Hz intervals share their boundary. Its world-space
            // blade points are already saved below; resampling it runs the whole
            // presentation/IK/clearance stack twice for the same attack instant.
            int firstSample = sweepValid && Mathf.Abs(lastSweepElapsed - from) < .000001f ? 1 : 0;
            // Both callers Present before freezing the hurtboxes and collecting
            // contacts. In the ordinary adjacent interval its sole endpoint is
            // already the complete constrained pose, including the supporting
            // hand. Reuse it; a second IK solve at the same instant is not motion.
            // Initial boundaries and a clipped active-window end still need a
            // preview at their own time, as do longer isolated authoring steps.
            bool usePresentedEndpoint = firstSample == 1 && samples == 1 &&
                to == State.AttackElapsed && State.IsAttacking && reaction == null;
            for (int i = firstSample; i <= samples; i++)
            {
                float elapsed = Mathf.Lerp(from, to, i / (float)samples);
                if (usePresentedEndpoint)
                {
                    previewWorldBlocked = weaponConstraint.WorldBlocked;
                    previewMotionBlocked = weaponConstraint.MotionBlocked;
                    previewBlockingShape = weaponConstraint.BlockingShape;
                    previewDesiredDepth = weaponConstraint.DesiredPoseDepth;
                    previewDesiredSweep = weaponConstraint.DesiredPoseSweepBlocked;
                    previewOwnForearm = weaponConstraint.DesiredOwnForearmDiagnostic;
                    previewBodyContactActor = weaponConstraint.RequestedBodyContactActor;
                    previewRequestedWeapon = weaponConstraint.RequestedWeaponPose;
                    previewWorldFraction = weaponConstraint.WorldContactFraction;
                    previewWorldPoint = weaponConstraint.WorldContactPoint; previewWorldNormal = weaponConstraint.WorldContactNormal;
                    previewWorldCollider = weaponConstraint.WorldContactCollider;
                }
                else
                {
                    ContactPoseSamples++;
                    if (!SampleAttack(State.AnimationProgressAt(elapsed)))
                    {
                        JournalEvent("weapon_contact_terminal", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                            GameLog.Field("reason", "presentation_unavailable"), GameLog.Field("elapsed", elapsed));
                        return;
                    }
                }
                Vector3 currentBase = strikeBase.position, currentTip = strikeTip.position;
                Pose currentWeapon = new Pose(Weapon.transform.position, Weapon.transform.rotation);
                // A native recipient capsule may stop/roll back the very
                // interval which reached its physical skin. Resolve that
                // requested metal path against the frozen real surfaces first.
                // Own arm/core rejection cannot supply this alternative path.
                if (previewMotionBlocked && previewBodyContactActor != null)
                    currentWeapon = previewRequestedWeapon;
                Vector3 travel = sweepValid ? (currentBase + currentTip - previousBase - previousTip) * .5f : transform.forward;
                if (travel.sqrMagnitude < .000001f) travel = transform.forward;
                // The tell may brush a wall; only the live arc is stopped by one.
                if (elapsed < State.AttackWindupSeconds)
                {
                    previousBase = currentBase; previousTip = currentTip; previousWeapon = currentWeapon;
                    lastSweepElapsed = elapsed; sweepValid = true;
                    continue;
                }
                float sampleSeconds = Mathf.Max(.0001f, (to - from) / samples);
                float sampleStart = Mathf.Max(0f, (i - 1f) / samples), sampleEnd = i / (float)samples;
                bool world = GatherWorldContact(currentWeapon, out float solidFraction, out Vector3 solidPoint,
                    out Vector3 solidNormal, out Collider solidCollider);
                if (previewWorldBlocked && previewWorldFraction < solidFraction)
                {
                    world = true; solidFraction = previewWorldFraction; solidPoint = previewWorldPoint; solidNormal = previewWorldNormal;
                    solidCollider = previewWorldCollider;
                }
                bool metal = false;
                CombatActor metalTarget = null;
                for (int participant = 0; participant < ContactParticipantCount; participant++)
                {
                    CombatActor target = ContactParticipantAt(participant);
                    if (target == this || !HasPhysicalBody(target) || target.IsFirearm || target.weaponDropped ||
                        target.State.IsBlocking && !target.frozenWeaponActive && !target.FacingIncomingAttack(this, travel.normalized) ||
                        !target.frozenWeaponValid || !target.Weapon.activeInHierarchy ||
                        !CombatWeaponGeometry.Sweep(sweepValid ? previousWeapon : currentWeapon, currentWeapon,
                            CombatWeaponGeometry.At(target.frozenWeaponFrom, target.frozenWeaponTo, sampleStart),
                            CombatWeaponGeometry.At(target.frozenWeaponFrom, target.frozenWeaponTo, sampleEnd),
                            out float metalFraction, out Vector3 metalPoint, out Vector3 metalNormal) || metalFraction > solidFraction) continue;
                    metalTarget = target;
                    metal = true; world = false; solidFraction = metalFraction; solidPoint = metalPoint; solidNormal = metalNormal;
                    solidCollider = null;
                }
                bool body = FindAnatomicalContact(currentWeapon, travel.normalized, sequence, sampleSeconds,
                    out CombatActor bodyTarget, out CombatHurtboxes.Hit nearest, out float bodyFraction, out float weaponSpeed);
                // Keep the body candidate behind a proposed interception until
                // BOTH sweeps have been collected. The other moving bar may itself
                // have met a wall earlier; only an accepted hard contact occludes.
                if (body && CanDamageParticipant(bodyTarget) && State.TryRegisterHit(bodyTarget.GetEntityId().GetHashCode(), sequence))
                {
                    pending.Add(new Contact(this, bodyTarget, nearest.Point, nearest.Normal, nearest.Direction, nearest.Location,
                        nearest.Part, nearest.LocalPoint, weaponSpeed, Mathf.Lerp(sampleStart, sampleEnd, bodyFraction), physicalBody: true,
                        bodyRegion: nearest.DamageRegion, bodyPatch: nearest.DamagePatch, detachedPart: nearest.IsDetached));
                    JournalEvent("weapon_contact_provisional", bodyTarget.JournalActorId, sequence, journalActionRequest,
                        GameLog.Field("part", (int)nearest.Part), GameLog.Field("fraction", bodyFraction),
                        GameLog.Field("point_x", nearest.Point.x), GameLog.Field("point_y", nearest.Point.y), GameLog.Field("point_z", nearest.Point.z),
                        GameLog.Field("weapon_speed", weaponSpeed), GameLog.Field("damage_requested", State.AttackDamage));
                }
                if (body && !CanDamageParticipant(bodyTarget) && bodyFraction < solidFraction)
                {
                    world = true; metal = false; solidFraction = bodyFraction;
                    solidPoint = nearest.Point; solidNormal = nearest.Normal;
                    solidCollider = null;
                }
                if (world || metal)
                {
                    Vector3 localPoint = Quaternion.Inverse(currentWeapon.rotation) * (solidPoint - currentWeapon.position);
                    float solidSpeed = sweepValid ? Vector3.Distance(solidPoint,
                        previousWeapon.position + previousWeapon.rotation * localPoint) / sampleSeconds : 0f;
                    pending.Add(Contact.Obstacle(this, metal ? metalTarget : null, solidPoint, solidNormal, travel.normalized,
                        Mathf.Lerp(sampleStart, sampleEnd, solidFraction), metal, metal && metalTarget.frozenWeaponActive,
                        solidCollider, solidSpeed));
                    sweepValid = false; return;
                }
                if (previewMotionBlocked)
                {
                    LastRejectedWeaponDepth = previewDesiredDepth; LastRejectedWeaponSweep = previewDesiredSweep;
                    previewOwnForearm.phase = (int)State.Phase;
                    previewOwnForearm.attackElapsed = elapsed;
                    previewOwnForearm.animationProgress = State.AnimationProgressAt(elapsed);
                    LastRejectedWeaponGeometry = previewOwnForearm;
                    pending.Add(Contact.RejectedPose(this, sequence, previewBlockingShape));
                    sweepValid = false; return;
                }
                previousBase = currentBase; previousTip = currentTip; previousWeapon = currentWeapon;
                lastSweepElapsed = elapsed; sweepValid = true;
            }
            if (to >= activeEnd)
                JournalEvent("weapon_contact_terminal", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                    GameLog.Field("reason", "active_window_complete"), GameLog.Field("outcome", (int)State.AttackOutcome));
        }

        private bool FindAnatomicalContact(Pose currentWeapon, Vector3 direction,
            int sequence, float sampleSeconds, out CombatActor target, out CombatHurtboxes.Hit nearest, out float earliest, out float weaponSpeed)
        {
            target = null; nearest = default; earliest = float.PositiveInfinity; weaponSpeed = 0f;
            for (int i = 0; i < ContactParticipantCount; i++)
            {
                CombatActor candidate = ContactParticipantAt(i);
                if (candidate == this || !HasPhysicalBody(candidate) ||
                    !candidate.IsAvailable && !candidate.IsKnockedDown && !candidate.State.IsDefeated ||
                    !CombatWeaponGeometry.SweepAnatomy(candidate.Hurtboxes, sweepValid ? previousWeapon : currentWeapon,
                        currentWeapon, sampleSeconds, direction,
                        out var hit, out float fraction, out float speed) || fraction >= earliest) continue;
                target = candidate; nearest = hit; earliest = fraction; weaponSpeed = speed;
            }
            if (ContactParticipantCount == 0) JournalContactRejected("no_target", sequence);
            return target != null;
        }

        private void JournalContactRejected(string reason, int sequence)
        {
            if (Journal == null || (journalContactRejectedSequence == sequence && journalContactRejectedReason == reason)) return;
            journalContactRejectedSequence = sequence; journalContactRejectedReason = reason;
            JournalEvent("weapon_contact_rejected", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                GameLog.Field("reason", reason), GameLog.Field("phase", (int)State.Phase), GameLog.Field("elapsed", State.AttackElapsed));
        }

        private bool GatherWorldContact(Pose currentWeapon, out float fraction, out Vector3 point, out Vector3 normal,
            out Collider collider)
        {
            fraction = float.PositiveInfinity; point = normal = Vector3.zero;
            collider = null;
            foreach (CombatWeaponGeometry.Segment segment in CombatWeaponGeometry.Segments)
            {
                Vector3 a = currentWeapon.position + currentWeapon.rotation * segment.A;
                Vector3 b = currentWeapon.position + currentWeapon.rotation * segment.B;
                float radius = segment.Radius + CombatWeaponGeometry.ContactSkin;
                GatherWorldSegment(a, b, radius, ref fraction, ref point, ref normal, ref collider);
                if (!sweepValid) continue;
                int intervals = Mathf.CeilToInt(Vector3.Distance(segment.A, segment.B) / radius);
                for (int i = 0; i <= intervals; i++)
                {
                    Vector3 local = Vector3.Lerp(segment.A, segment.B, intervals == 0 ? 0f : i / (float)intervals);
                    GatherWorldSweep(previousWeapon.position + previousWeapon.rotation * local,
                        currentWeapon.position + currentWeapon.rotation * local, radius, ref fraction, ref point, ref normal, ref collider);
                }
            }
            return fraction <= 1f;
        }

        private bool IsContactWorld(Collider candidate) => candidate != null && !candidate.isTrigger &&
            !candidate.transform.IsChildOf(transform) &&
            (CombatZombieLoad.IsLoadCollider(candidate) || candidate.GetComponentInParent<CombatActor>() == null);

        private void GatherWorldSegment(Vector3 a, Vector3 b, float radius, ref float fraction, ref Vector3 point, ref Vector3 normal)
        {
            Collider collider = null;
            GatherWorldSegment(a, b, radius, ref fraction, ref point, ref normal, ref collider);
        }

        private void GatherWorldSegment(Vector3 a, Vector3 b, float radius, ref float fraction, ref Vector3 point,
            ref Vector3 normal, ref Collider collider)
        {
            int count;
            while (true)
            {
                JournalPhysicsQuery();
                count = a == b ? Physics.OverlapSphereNonAlloc(a, radius, contactBuffer, ~0, QueryTriggerInteraction.Collide) :
                    Physics.OverlapCapsuleNonAlloc(a, b, radius, contactBuffer, ~0, QueryTriggerInteraction.Collide);
                if (count < contactBuffer.Length) break;
                JournalQueryBufferFull(1, "weapon_overlap", contactBuffer.Length);
                Array.Resize(ref contactBuffer, contactBuffer.Length * 2);
            }
            for (int i = 0; i < count; i++)
                if (fraction > 1f && IsContactWorld(contactBuffer[i]))
                {
                    collider = contactBuffer[i];
                    Vector3 centre = (a + b) * .5f;
                    bool supportsClosestPoint = collider is BoxCollider || collider is SphereCollider ||
                        collider is CapsuleCollider || collider is MeshCollider mesh && mesh.convex;
                    // The exact overlap above already established the obstruction.
                    // Non-convex meshes/terrain do not support ClosestPoint; their
                    // bounds supply contact metadata only, never a collision test.
                    fraction = 1f;
                    point = supportsClosestPoint ? collider.ClosestPoint(centre) : collider.bounds.ClosestPoint(centre);
                    normal = (centre - point).normalized;
                }
        }

        private void GatherWorldSweep(Vector3 from, Vector3 to, float radius, ref float fraction, ref Vector3 point, ref Vector3 normal)
        {
            Collider collider = null;
            GatherWorldSweep(from, to, radius, ref fraction, ref point, ref normal, ref collider);
        }

        private void GatherWorldSweep(Vector3 from, Vector3 to, float radius, ref float fraction, ref Vector3 point,
            ref Vector3 normal, ref Collider collider)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .00001f) return;
            int count;
            while (true)
            {
                JournalPhysicsQuery();
                count = Physics.SphereCastNonAlloc(from, radius, delta / distance,
                    castBuffer, distance, ~0, QueryTriggerInteraction.Collide);
                if (count < castBuffer.Length) break;
                JournalQueryBufferFull(2, "weapon_sweep", castBuffer.Length);
                Array.Resize(ref castBuffer, castBuffer.Length * 2);
            }
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = castBuffer[i];
                float t = hit.distance / distance;
                if (!IsContactWorld(hit.collider) || t >= fraction) continue;
                fraction = t; point = hit.point; normal = hit.normal;
                collider = hit.collider;
            }
        }
    }
}
