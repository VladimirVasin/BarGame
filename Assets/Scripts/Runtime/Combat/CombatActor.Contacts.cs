using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private const float WeaponRadius = .10f;
        private Collider[] contactBuffer = new Collider[32];
        private RaycastHit[] castBuffer = new RaycastHit[32];
        private readonly HashSet<Collider> sampleContacts = new HashSet<Collider>();
        private CombatActor contactTarget;
        private bool collectSweep;
        private float sweepFrom, sweepTo;
        private int pendingSequence;
        private bool sweepValid;
        private int sweepSequence;
        private float lastSweepElapsed;
        private Vector3 previousBase, previousTip;

        internal void SetContactTarget(CombatActor target)
        { contactTarget = target; weaponConstraint?.SetOpponent(target); heldWeaponPhysics?.SetOpponent(target); }
        internal void CaptureContactPose() => Hurtboxes?.Capture();

        internal bool CollectContacts(List<Contact> pending)
        {
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
            private readonly Vector3 point, normal, direction;
            private readonly float damage, blockCost, power;
            private readonly MeleeHitLocation location;
            private readonly Player3DAnatomicalPart part;
            private readonly Vector3 localPoint;
            private readonly float weaponSpeed;

            public Contact(CombatActor source, CombatActor target, Vector3 point, Vector3 normal, Vector3 direction,
                MeleeHitLocation location, Player3DAnatomicalPart part = Player3DAnatomicalPart.Torso,
                Vector3 localPoint = default, float weaponSpeed = 0f)
            {
                this.source = source; this.target = target;
                this.point = point; this.normal = normal; this.direction = direction;
                this.location = location; this.part = part; this.localPoint = localPoint; this.weaponSpeed = weaponSpeed;
                attackSequence = source.State.AttackSequence;
                // The other actor may interrupt this source before Apply.
                // A collected strike keeps the strength that reached its target.
                damage = source.State.AttackDamage;
                blockCost = source.State.AttackBlockCost;
                power = source.State.AttackPower;
                Vector3 incoming = source.transform.position - target.transform.position;
                incoming.y = 0f;
                fromFront = Vector3.Dot(target.transform.forward, incoming.normalized) >= .35f;
            }

            public void Apply()
            {
                MeleeHitResult result = target.Receive(source, fromFront, attackSequence, point, normal, direction,
                    damage, blockCost, power, location, part, localPoint, weaponSpeed);
                source.State.RecordAttackOutcome(result, attackSequence);
                if (result == MeleeHitResult.Parried) source.ShowParried(point, direction);
            }
        }

        private void SweepWeapon(float from, float to, int sequence, List<Contact> pending)
        {
            float activeEnd = State.AttackActiveEnd;
            if (from >= activeEnd) { sweepValid = false; return; }
            to = Mathf.Min(to, activeEnd);
            if (to < from) from = 0f;
            int samples = Mathf.Max(1, Mathf.CeilToInt((to - from) * 120f));
            if (sweepSequence != sequence) sweepValid = false;
            sweepSequence = sequence;
            // Adjacent 120 Hz intervals share their boundary. Its world-space
            // blade points are already saved below; resampling it runs the whole
            // presentation/IK/clearance stack twice for the same attack instant.
            int firstSample = sweepValid && Mathf.Abs(lastSweepElapsed - from) < .000001f ? 1 : 0;
            for (int i = firstSample; i <= samples; i++)
            {
                float elapsed = Mathf.Lerp(from, to, i / (float)samples);
                if (!SampleAttack(State.AnimationProgressAt(elapsed))) return;
                Vector3 currentBase = strikeBase.position, currentTip = strikeTip.position;
                sampleContacts.Clear();
                Vector3 travel = sweepValid ? (currentBase + currentTip - previousBase - previousTip) * .5f : transform.forward;
                if (travel.sqrMagnitude < .000001f) travel = transform.forward;
                GatherCapsule(currentBase, currentTip);
                if (sweepValid)
                {
                    // Sweep a row of overlapping spheres along the real moving weapon.
                    // This catches translation/rotation between poses, including thin walls.
                    float length = Mathf.Max(Vector3.Distance(previousBase, previousTip),
                        Vector3.Distance(currentBase, currentTip));
                    int intervals = Mathf.Max(1, Mathf.CeilToInt(length / WeaponRadius));
                    for (int point = 0; point <= intervals; point++)
                    {
                        float t = point / (float)intervals;
                        GatherSweep(Vector3.Lerp(previousBase, previousTip, t),
                            Vector3.Lerp(currentBase, currentTip, t));
                    }
                }
                // The tell may brush a wall; only the live arc is stopped by one.
                if (elapsed < State.AttackWindupSeconds)
                {
                    previousBase = currentBase; previousTip = currentTip; lastSweepElapsed = elapsed; sweepValid = true;
                    continue;
                }
                // A solid that meets the blade at this sample stops the swing here.
                // Targets from earlier samples already connected; no centre-to-centre ray
                // substitutes for the actual weapon path at corners or low cover.
                bool worldBlocked = previewWorldBlocked;
                foreach (Collider candidate in sampleContacts)
                {
                    if (candidate == null || candidate.isTrigger || candidate.transform.IsChildOf(transform) ||
                        candidate.GetComponentInParent<CombatActor>() != null) continue;
                    worldBlocked = true;
                    break;
                }
                // The complete prop may already have been stopped or moved clear
                // by its arm constraint, including the hook/handle outside the blade.
                // Own or opposing anatomy is not a world-obstacle cancellation.
                if (worldBlocked)
                {
                    if (State.CancelAttackOnObstacle())
                    {
                        reaction = Current.Recoil; reactionClock = 0f;
                        RetroAudio.PlayAt(RetroSfxId.SpadeGlance, (currentBase + currentTip) * .5f, .6f);
                    }
                    sweepValid = false;
                    return;
                }

                GatherAnatomicalContact(currentBase, currentTip, travel.normalized, sequence, pending, Mathf.Max(.0001f, (to - from) / samples));
                previousBase = currentBase; previousTip = currentTip; lastSweepElapsed = elapsed; sweepValid = true;
            }
        }

        private void GatherAnatomicalContact(Vector3 currentBase, Vector3 currentTip, Vector3 direction,
            int sequence, List<Contact> pending, float sampleSeconds)
        {
            CombatActor target = contactTarget;
            if (target == null || (!target.IsAvailable && !target.IsKnockedDown) || target.State.IsDefeated || target.Hurtboxes == null) return;
            // End-pose overlap is later than every swept contact. The row of moving
            // spheres retains the existing blade coverage, including translation.
            float weaponSpeed = sweepValid ? ((currentBase - previousBase).magnitude + (currentTip - previousTip).magnitude) * .5f / sampleSeconds : 0f;
            bool found = target.Hurtboxes.SweepSphere(currentBase, currentTip, WeaponRadius, direction, out var nearest);
            float earliest = found ? 1f : float.PositiveInfinity;
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
            if (found && State.TryRegisterHit(target.GetEntityId().GetHashCode(), sequence))
                pending.Add(new Contact(this, target, nearest.Point, nearest.Normal, nearest.Direction, nearest.Location, nearest.Part, nearest.LocalPoint, weaponSpeed));
        }

        private void GatherCapsule(Vector3 a, Vector3 b)
        {
            int count;
            while (true)
            {
                count = Physics.OverlapCapsuleNonAlloc(a, b, WeaponRadius, contactBuffer, ~0,
                    QueryTriggerInteraction.Collide);
                if (count < contactBuffer.Length) break;
                Array.Resize(ref contactBuffer, contactBuffer.Length * 2);
            }
            for (int i = 0; i < count; i++)
                sampleContacts.Add(contactBuffer[i]);
        }

        private void GatherSweep(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < .00001f) return;
            int count;
            while (true)
            {
                count = Physics.SphereCastNonAlloc(from, WeaponRadius, delta / distance,
                    castBuffer, distance, ~0, QueryTriggerInteraction.Collide);
                if (count < castBuffer.Length) break;
                Array.Resize(ref castBuffer, castBuffer.Length * 2);
            }
            for (int i = 0; i < count; i++)
                sampleContacts.Add(castBuffer[i].collider);
        }
    }
}
