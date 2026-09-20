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
        private bool sweepValid;
        private int sweepSequence;
        private Vector3 previousBase, previousTip;

        /// <summary>A registered contact survives interruption of its source in the same simulation step.</summary>
        internal readonly struct Contact
        {
            private readonly CombatActor source, target;
            private readonly bool fromFront;
            private readonly int attackSequence;

            public Contact(CombatActor source, CombatActor target)
            {
                this.source = source; this.target = target;
                attackSequence = source.State.AttackSequence;
                Vector3 incoming = source.transform.position - target.transform.position;
                incoming.y = 0f;
                fromFront = Vector3.Dot(target.transform.forward, incoming.normalized) >= .35f;
            }

            public void Apply()
            {
                MeleeHitResult result = target.Receive(source, fromFront);
                source.State.RecordAttackOutcome(result, attackSequence);
            }
        }

        private void SweepWeapon(float from, float to, int sequence, List<Contact> pending)
        {
            float activeEnd = State.Settings.WindupSeconds + State.Settings.ActiveSeconds;
            if (from >= activeEnd) { sweepValid = false; return; }
            to = Mathf.Min(to, activeEnd);
            if (to < from) from = 0f;
            int samples = Mathf.Max(1, Mathf.CeilToInt((to - from) * 120f));
            if (sweepSequence != sequence) sweepValid = false;
            sweepSequence = sequence;
            for (int i = 0; i <= samples; i++)
            {
                float elapsed = Mathf.Lerp(from, to, i / (float)samples);
                if (!SampleAttack(elapsed / State.Settings.AnimationAttackDurationSeconds)) return;
                Vector3 currentBase = strikeBase.position, currentTip = strikeTip.position;
                sampleContacts.Clear();
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
                previousBase = currentBase; previousTip = currentTip; sweepValid = true;

                // A solid that meets the blade at this sample stops the swing here.
                // Targets from earlier samples already connected; no centre-to-centre ray
                // substitutes for the actual weapon path at corners or low cover.
                foreach (Collider candidate in sampleContacts)
                {
                    if (candidate == null || candidate.isTrigger || candidate.transform.IsChildOf(transform) ||
                        candidate.GetComponentInParent<CombatActor>() != null) continue;
                    if (State.CancelAttackOnObstacle())
                    {
                        reaction = recoil; reactionClock = 0f;
                        RetroAudio.PlayAt(RetroSfxId.SpadeGlance, (currentBase + currentTip) * .5f, 1f);
                    }
                    sweepValid = false;
                    return;
                }

                if (elapsed < State.Settings.WindupSeconds) continue;
                foreach (Collider candidate in sampleContacts)
                {
                    if (candidate == null) continue;
                    CombatActor target = candidate.GetComponentInParent<CombatActor>();
                    if (target == null || target == this || !target.IsAvailable || target.State.IsDefeated ||
                        !State.TryRegisterHit(target.GetEntityId().GetHashCode(), sequence)) continue;
                    pending.Add(new Contact(this, target));
                }
            }
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
            for (int i = 0; i < count; i++) sampleContacts.Add(contactBuffer[i]);
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
            for (int i = 0; i < count; i++) sampleContacts.Add(castBuffer[i].collider);
        }
    }
}
