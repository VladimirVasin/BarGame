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
        private readonly Dictionary<Collider, SurfaceContact> contactSurfaces = new Dictionary<Collider, SurfaceContact>();
        private bool sweepValid;
        private int sweepSequence;
        private Vector3 previousBase, previousTip;

        private readonly struct SurfaceContact
        {
            public readonly Vector3 Point, Normal, Direction;
            public readonly float SweepFraction;
            public SurfaceContact(Vector3 point, Vector3 normal, Vector3 direction, float fraction)
            { Point = point; Normal = normal; Direction = direction; SweepFraction = fraction; }
        }

        /// <summary>A registered contact survives interruption of its source in the same simulation step.</summary>
        internal readonly struct Contact
        {
            private readonly CombatActor source, target;
            private readonly bool fromFront;
            private readonly int attackSequence;
            private readonly Vector3 point, normal, direction;
            private readonly float damage, blockCost, power;

            public Contact(CombatActor source, CombatActor target, Vector3 point, Vector3 normal, Vector3 direction)
            {
                this.source = source; this.target = target;
                this.point = point; this.normal = normal; this.direction = direction;
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
                MeleeHitResult result = target.Receive(source, fromFront, attackSequence, point, normal, direction, damage, blockCost, power);
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
            for (int i = 0; i <= samples; i++)
            {
                float elapsed = Mathf.Lerp(from, to, i / (float)samples);
                if (!SampleAttack(State.AnimationProgressAt(elapsed))) return;
                Vector3 currentBase = strikeBase.position, currentTip = strikeTip.position;
                sampleContacts.Clear();
                contactSurfaces.Clear();
                Vector3 travel = sweepValid ? (currentBase + currentTip - previousBase - previousTip) * .5f : transform.forward;
                if (travel.sqrMagnitude < .000001f) travel = transform.forward;
                GatherCapsule(currentBase, currentTip, travel.normalized);
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

                // The tell may brush a wall; only the live arc is stopped by one.
                if (elapsed < State.AttackWindupSeconds) continue;
                // A solid that meets the blade at this sample stops the swing here.
                // Targets from earlier samples already connected; no centre-to-centre ray
                // substitutes for the actual weapon path at corners or low cover.
                foreach (Collider candidate in sampleContacts)
                {
                    if (candidate == null || candidate.isTrigger || candidate.transform.IsChildOf(transform) ||
                        candidate.GetComponentInParent<CombatActor>() != null) continue;
                    if (State.CancelAttackOnObstacle())
                    {
                        reaction = Current.Recoil; reactionClock = 0f;
                        RetroAudio.PlayAt(RetroSfxId.SpadeGlance, (currentBase + currentTip) * .5f, .6f);
                    }
                    sweepValid = false;
                    return;
                }

                foreach (Collider candidate in sampleContacts)
                {
                    if (candidate == null) continue;
                    CombatActor target = candidate.GetComponentInParent<CombatActor>();
                    if (target == null || target == this || !target.IsAvailable || target.State.IsDefeated ||
                        !State.TryRegisterHit(target.GetEntityId().GetHashCode(), sequence)) continue;
                    // Prefer the actor's solid capsule over a clothing trigger when
                    // both registered the same strike; this affects only presentation.
                    SurfaceContact surface = contactSurfaces.TryGetValue(target.Body, out SurfaceContact bodySurface)
                        ? bodySurface : contactSurfaces[candidate];
                    pending.Add(new Contact(this, target, surface.Point, surface.Normal, surface.Direction));
                }
            }
        }

        private void GatherCapsule(Vector3 a, Vector3 b, Vector3 direction)
        {
            int count;
            while (true)
            {
                count = Physics.OverlapCapsuleNonAlloc(a, b, WeaponRadius, contactBuffer, ~0,
                    QueryTriggerInteraction.Collide);
                if (count < contactBuffer.Length) break;
                Array.Resize(ref contactBuffer, contactBuffer.Length * 2);
            }
            Vector3 segment = b - a;
            float lengthSquared = segment.sqrMagnitude;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = contactBuffer[i];
                sampleContacts.Add(candidate);
                float along = lengthSquared > .000001f ?
                    Mathf.Clamp01(Vector3.Dot(candidate.bounds.center - a, segment) / lengthSquared) : 0f;
                Vector3 bladePoint = a + segment * along;
                Vector3 point = ClosestContactPoint(candidate, bladePoint, direction);
                Vector3 normal = bladePoint - point;
                if (normal.sqrMagnitude < .000001f) normal = -direction;
                contactSurfaces[candidate] = new SurfaceContact(point, normal.normalized, direction, float.PositiveInfinity);
            }
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
            {
                RaycastHit hit = castBuffer[i];
                sampleContacts.Add(hit.collider);
                float fraction = hit.distance / distance;
                if (contactSurfaces.TryGetValue(hit.collider, out SurfaceContact previous) && previous.SweepFraction <= fraction)
                    continue;
                Vector3 point = hit.distance > .00001f ? hit.point : ClosestContactPoint(hit.collider, from, delta / distance);
                Vector3 normal = hit.normal.sqrMagnitude > .000001f ? hit.normal.normalized : -delta / distance;
                contactSurfaces[hit.collider] = new SurfaceContact(point, normal, delta / distance, fraction);
            }
        }

        private static Vector3 ClosestContactPoint(Collider collider, Vector3 point, Vector3 direction)
        {
            // CharacterController is the standing hurtbox. Use its capsule
            // surface even when an overlap sample already lies inside it.
            if (collider is CharacterController body)
            {
                Vector3 axis = body.transform.up, centre = body.transform.TransformPoint(body.center);
                Vector3 scale = body.transform.lossyScale;
                float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float halfSegment = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * .5f - radius);
                Vector3 closestAxis = centre + axis * Mathf.Clamp(Vector3.Dot(point - centre, axis), -halfSegment, halfSegment);
                Vector3 outward = point - closestAxis;
                if (outward.sqrMagnitude < .000001f) outward = -direction;
                return closestAxis + outward.normalized * radius;
            }
            // Non-convex arena meshes only stop attacks; Unity's closest-point
            // API does not support their interior. Their metadata never wounds.
            if (collider is MeshCollider mesh && !mesh.convex) return collider.ClosestPointOnBounds(point);
            return collider.ClosestPoint(point);
        }
    }
}
