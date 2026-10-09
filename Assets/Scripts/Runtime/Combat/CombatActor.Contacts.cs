using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        // The generated shaft is 38 mm across. A broad .10 m damage proxy
        // would claim a wound before a real intervening crowbar could meet it.
        private const float WeaponRadius = .019f + CombatWeaponGeometry.ContactSkin;
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
        internal int ContactPoseSamples { get; private set; }
        internal int WeaponClashCount { get; private set; }
        internal int LastWeaponClashSequence { get; private set; }
        internal Vector3 LastWeaponClashPoint { get; private set; }
        private int journalContactRejectedSequence = -1;
        private string journalContactRejectedReason;

        internal void SetContactTarget(CombatActor target)
        { contactTarget = target; weaponConstraint?.SetOpponent(target); heldWeaponPhysics?.SetOpponent(target); }
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
        }

        internal bool CollectContacts(List<Contact> pending)
        {
            ContactPoseSamples = 0;
            if (!collectSweep) return false;
            collectSweep = false;
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

            internal bool IsSolid => solid;
            internal float Time => time;
            internal int Priority => !solid ? 2 : metal ? 1 : 0;
            internal int SourceId => source.GetEntityId().GetHashCode();

            public Contact(CombatActor source, CombatActor target, Vector3 point, Vector3 normal, Vector3 direction,
                MeleeHitLocation location, Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso,
                Vector3 localPoint = default, float weaponSpeed = 0f, float time = 0f, bool physicalBody = false,
                BodyDamageRegion? bodyRegion = null, int bodyPatch = -1, bool detachedPart = false)
            {
                this.source = source; this.target = target;
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
                Vector3 incoming = source.transform.position - target.transform.position;
                incoming.y = 0f;
                fromFront = Vector3.Dot(target.transform.forward, incoming.normalized) >= .35f;
            }

            private Contact(CombatActor source, CombatActor target, Vector3 point, Vector3 normal, Vector3 direction,
                float time, bool metal, bool otherActive)
            {
                this.source = source; this.target = target; this.point = point; this.normal = normal; this.direction = direction;
                this.time = time; this.metal = metal; this.otherActive = otherActive; solid = true;
                attackSequence = source.State.AttackSequence; otherSequence = target != null ? target.frozenWeaponSequence : 0;
                request = source.journalActionRequest;
                damage = weaponSpeed = 0f; blockCost = source.State.AttackBlockCost; power = source.State.AttackPower;
                fromFront = target != null && Vector3.Dot(target.transform.forward,
                    (source.transform.position - target.transform.position).normalized) >= .35f;
                guarded = metal && target != null && target.State.IsBlocking && fromFront;
                physicalBody = false;
                bodyRegion = null; bodyPatch = -1; detachedPart = false;
                location = default; part = default; localPoint = default;
            }

            internal static Contact Obstacle(CombatActor source, CombatActor target, Vector3 point, Vector3 normal,
                Vector3 direction, float time, bool metal, bool otherActive = false) =>
                new Contact(source, target, point, normal, direction, time, metal, otherActive);

            private bool StopsSource(CombatActor actor, int sequence) => solid &&
                ((source == actor && attackSequence == sequence) ||
                 (metal && otherActive && target == actor && otherSequence == sequence));

            internal bool Stops(Contact later) => StopsSource(later.source, later.attackSequence) ||
                (later.metal && later.otherActive && StopsSource(later.target, later.otherSequence));

            public void Apply()
            {
                if (solid)
                {
                    source.StopWeaponOnSolid(attackSequence, point, normal, metal);
                    if (metal && target != null)
                    {
                        if (otherActive) target.StopWeaponOnSolid(otherSequence, point, -normal, true);
                        source.RecordWeaponClash(attackSequence, point);
                        target.RecordWeaponClash(otherSequence, point);
                        if (guarded) target.ReceiveWeaponGuard(source, attackSequence, point, normal, direction, blockCost, power);
                        source.GetComponentInParent<CombatTestRoot>()?.SparkEffects?.Emit(point, normal, direction);
                    }
                    RetroAudio.PlayAt(RetroSfxId.SpadeGlance, point, .6f);
                    return;
                }
                MeleeHitResult result = target.Receive(source, fromFront, attackSequence, point, normal, direction,
                    damage, blockCost, power, location, part, localPoint, weaponSpeed, physicalBody,
                    bodyRegion, bodyPatch, detachedPart);
                source.JournalEvent("weapon_contact_resolved", target.JournalActorId, attackSequence, request,
                    GameLog.Field("result", (int)result), GameLog.Field("impact_seq", target.LastJournalImpactSequence),
                    GameLog.Field("from_front", fromFront), GameLog.Field("damage_requested", damage));
                source.State.RecordAttackOutcome(result, attackSequence);
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

        private void ReceiveWeaponGuard(CombatActor source, int sequence, Vector3 point, Vector3 normal,
            Vector3 direction, float blockCost, float power)
        {
            MeleePhase previous = State.Phase;
            MeleeHitResult result = State.ReceiveWeaponObstacle(blockCost, power);
            if (result == MeleeHitResult.Ignored) return;
            reaction = result == MeleeHitResult.Blocked ? guardImpact : null; reactionClock = 0f;
            PublishImpact(new CombatImpact(source, this, sequence, point, normal, direction, State.Health, State.Health,
                result, attackPower: power, impulse: CombatImpactMotion.ResolveImpulse(direction, power, 0f, result)), previous);
            Present();
        }

        private void StopWeaponOnSolid(int sequence, Vector3 point, Vector3 normal, bool metal)
        {
            if (State.AttackSequence != sequence || !State.CancelAttackOnObstacle()) return;
            reaction = Current.Recoil; reactionClock = 0f; sweepValid = collectSweep = false;
            JournalEvent("weapon_contact_terminal", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                GameLog.Field("reason", metal ? "weapon_blocked" : "world_blocked"),
                GameLog.Field("point_x", point.x), GameLog.Field("point_y", point.y), GameLog.Field("point_z", point.z));
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
                    previewWorldFraction = weaponConstraint.WorldContactFraction;
                    previewWorldPoint = weaponConstraint.WorldContactPoint; previewWorldNormal = weaponConstraint.WorldContactNormal;
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
                bool world = GatherWorldContact(currentBase, currentTip, out float solidFraction, out Vector3 solidPoint, out Vector3 solidNormal);
                if (previewWorldBlocked && previewWorldFraction < solidFraction)
                {
                    world = true; solidFraction = previewWorldFraction; solidPoint = previewWorldPoint; solidNormal = previewWorldNormal;
                }
                bool metal = false;
                CombatActor target = contactTarget;
                if (target != null && !target.IsFirearm && !target.weaponDropped && target.frozenWeaponValid && target.Weapon.activeInHierarchy &&
                    CombatWeaponGeometry.Sweep(sweepValid ? previousWeapon : currentWeapon, currentWeapon,
                        CombatWeaponGeometry.At(target.frozenWeaponFrom, target.frozenWeaponTo, sampleStart),
                        CombatWeaponGeometry.At(target.frozenWeaponFrom, target.frozenWeaponTo, sampleEnd),
                        out float metalFraction, out Vector3 metalPoint, out Vector3 metalNormal) && metalFraction <= solidFraction)
                {
                    metal = true; world = false; solidFraction = metalFraction; solidPoint = metalPoint; solidNormal = metalNormal;
                }
                bool body = FindAnatomicalContact(currentBase, currentTip, travel.normalized, sequence, sampleSeconds,
                    out CombatHurtboxes.Hit nearest, out float bodyFraction, out float weaponSpeed);
                // Keep the body candidate behind a proposed interception until
                // BOTH sweeps have been collected. The other moving bar may itself
                // have met a wall earlier; only an accepted hard contact occludes.
                if (body && State.TryRegisterHit(target.GetEntityId().GetHashCode(), sequence))
                {
                    pending.Add(new Contact(this, target, nearest.Point, nearest.Normal, nearest.Direction, nearest.Location,
                        nearest.Part, nearest.LocalPoint, weaponSpeed, Mathf.Lerp(sampleStart, sampleEnd, bodyFraction), physicalBody: true,
                        bodyRegion: nearest.DamageRegion, bodyPatch: nearest.DamagePatch, detachedPart: nearest.IsDetached));
                    JournalEvent("weapon_contact_provisional", target.JournalActorId, sequence, journalActionRequest,
                        GameLog.Field("part", (int)nearest.Part), GameLog.Field("fraction", bodyFraction),
                        GameLog.Field("point_x", nearest.Point.x), GameLog.Field("point_y", nearest.Point.y), GameLog.Field("point_z", nearest.Point.z),
                        GameLog.Field("weapon_speed", weaponSpeed), GameLog.Field("damage_requested", State.AttackDamage));
                }
                if (world || metal)
                {
                    pending.Add(Contact.Obstacle(this, metal ? target : null, solidPoint, solidNormal, travel.normalized,
                        Mathf.Lerp(sampleStart, sampleEnd, solidFraction), metal, metal && target.frozenWeaponActive));
                    sweepValid = false; return;
                }
                previousBase = currentBase; previousTip = currentTip; previousWeapon = currentWeapon;
                lastSweepElapsed = elapsed; sweepValid = true;
            }
            if (to >= activeEnd)
                JournalEvent("weapon_contact_terminal", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                    GameLog.Field("reason", "active_window_complete"), GameLog.Field("outcome", (int)State.AttackOutcome));
        }

        private bool FindAnatomicalContact(Vector3 currentBase, Vector3 currentTip, Vector3 direction,
            int sequence, float sampleSeconds, out CombatHurtboxes.Hit nearest, out float earliest, out float weaponSpeed)
        {
            nearest = default; earliest = float.PositiveInfinity; weaponSpeed = 0f;
            CombatActor target = contactTarget;
            if (target == null) { JournalContactRejected("no_target", sequence); return false; }
            if (!target.isActiveAndEnabled || !target.IsAvailable && !target.IsKnockedDown && !target.State.IsDefeated)
            { JournalContactRejected("target_unavailable", sequence); return false; }
            if (target.Hurtboxes == null) { JournalContactRejected("hurtboxes_missing", sequence); return false; }
            // End-pose overlap is later than every swept contact. The row of moving
            // spheres retains the existing blade coverage, including translation.
            weaponSpeed = sweepValid ? ((currentBase - previousBase).magnitude + (currentTip - previousTip).magnitude) * .5f / sampleSeconds : 0f;
            bool found = target.Hurtboxes.SweepSphere(currentBase, currentTip, WeaponRadius, direction, out nearest);
            earliest = found ? 1f : float.PositiveInfinity;
            if (sweepValid)
            {
                float length = Mathf.Max(Vector3.Distance(previousBase, previousTip), Vector3.Distance(currentBase, currentTip));
                int intervals = Mathf.Max(1, Mathf.CeilToInt(length / WeaponRadius));
                for (int point = 0; point <= intervals; point++)
                {
                    float t = point / (float)intervals;
                    Vector3 start = Vector3.Lerp(previousBase, previousTip, t);
                    Vector3 end = Vector3.Lerp(currentBase, currentTip, t);
                    Vector3 travel = end - start;
                    if (travel.sqrMagnitude < .0000001f) continue;
                    if (!target.Hurtboxes.SweepSphere(start, end, WeaponRadius, travel.normalized, out var hit) ||
                        hit.Fraction >= earliest) continue;
                    nearest = hit; earliest = hit.Fraction; found = true; weaponSpeed = travel.magnitude / sampleSeconds;
                }
            }
            return found;
        }

        private void JournalContactRejected(string reason, int sequence)
        {
            if (Journal == null || (journalContactRejectedSequence == sequence && journalContactRejectedReason == reason)) return;
            journalContactRejectedSequence = sequence; journalContactRejectedReason = reason;
            JournalEvent("weapon_contact_rejected", contactTarget?.JournalActorId ?? 0, sequence, journalActionRequest,
                GameLog.Field("reason", reason), GameLog.Field("phase", (int)State.Phase), GameLog.Field("elapsed", State.AttackElapsed));
        }

        private bool GatherWorldContact(Vector3 a, Vector3 b, out float fraction, out Vector3 point, out Vector3 normal)
        {
            fraction = float.PositiveInfinity; point = normal = Vector3.zero;
            int count;
            while (true)
            {
                JournalPhysicsQuery();
                count = Physics.OverlapCapsuleNonAlloc(a, b, WeaponRadius, contactBuffer, ~0,
                    QueryTriggerInteraction.Collide);
                if (count < contactBuffer.Length) break;
                JournalQueryBufferFull(1, "weapon_overlap", contactBuffer.Length);
                Array.Resize(ref contactBuffer, contactBuffer.Length * 2);
            }
            for (int i = 0; i < count; i++)
                if (IsContactWorld(contactBuffer[i]))
                {
                    fraction = 1f; point = contactBuffer[i].ClosestPoint((a + b) * .5f);
                    normal = ((a + b) * .5f - point).normalized;
                }
            if (sweepValid)
            {
                int intervals = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Distance(previousBase, previousTip),
                    Vector3.Distance(a, b)) / WeaponRadius));
                for (int i = 0; i <= intervals; i++)
                    GatherWorldSweep(Vector3.Lerp(previousBase, previousTip, i / (float)intervals),
                        Vector3.Lerp(a, b, i / (float)intervals), ref fraction, ref point, ref normal);
            }
            return fraction <= 1f;
        }

        private bool IsContactWorld(Collider candidate) => candidate != null && !candidate.isTrigger &&
            !candidate.transform.IsChildOf(transform) && candidate.GetComponentInParent<CombatActor>() == null;

        private void GatherWorldSweep(Vector3 from, Vector3 to, ref float fraction, ref Vector3 point, ref Vector3 normal)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .00001f) return;
            int count;
            while (true)
            {
                JournalPhysicsQuery();
                count = Physics.SphereCastNonAlloc(from, WeaponRadius, delta / distance,
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
            }
        }
    }
}
