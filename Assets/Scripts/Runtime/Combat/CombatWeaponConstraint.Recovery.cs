using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatWeaponConstraint
    {
        // Only numerical contact noise is tolerated on the real bar. The 4 mm
        // presentation clearance is never a licence for shaft penetration.
        private const float RecoveryContactTolerance = .00002f;
        private readonly List<RecoveryFloorContact> recoveryFloorContacts = new List<RecoveryFloorContact>(16);
        private readonly List<float> recoveryFloorDepths = new List<float>(16);
        private bool recoveryContactActive, recoveryEscapeActive;
        private float recoveryEscapeClock;
        private Quaternion recoveryEscapeShoulder;

        internal bool RecoveryEscapeActive => RecoveryContactsEnabled && recoveryEscapeActive;
        private bool RecoveryContactsEnabled => recoveryContactActive && !contactPreview &&
            actor.Ragdoll != null && actor.Ragdoll.IsRecovering && !actor.IsWeaponDropped;

        /// <summary>Capture physics' actual lying pose before presenting the first rise sample.</summary>
        internal void BeginRecoveryContact()
        {
            EndRecoveryContact();
            if (weapon == null || actor.IsWeaponDropped || actor.Ragdoll == null || !actor.Ragdoll.IsRecovering) return;
            Restore();
            GatherObstacles();
            Pose pose = WeaponPose;
            for (int index = 0; index < CombatWeaponGeometry.Segments.Count; index++)
            {
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[index];
                Vector3 a = pose.position + pose.rotation * segment.A;
                Vector3 b = pose.position + pose.rotation * segment.B;
                foreach (Collider shape in obstacles)
                {
                    if (!WorldObstacle(shape)) continue;
                    float depth = RecoveryFloorDepth(segment, a, b, shape, out Vector3 direction);
                    if (depth <= 0f || depth > Skin + RecoveryContactTolerance || direction.y < .65f ||
                        Mathf.Min(a.y, b.y) - segment.Radius > actor.transform.position.y + .06f ||
                        !RecoveryCoreClear(segment, a, b, shape)) continue;
                    recoveryFloorContacts.Add(new RecoveryFloorContact(index, shape, depth));
                    recoveryFloorDepths.Add(depth);
                }
            }
            recoveryContactActive = true;
            // This is a starting pose, not a newly admitted intersecting target.
            // Remember also snapshots live anatomy so the very first authored rise
            // sample cannot skip its path merely because falling called Forget().
            Remember();
        }

        internal void EndRecoveryContact()
        {
            recoveryContactActive = recoveryEscapeActive = false;
            recoveryFloorContacts.Clear();
            recoveryFloorDepths.Clear();
        }

        internal void BeginRecoveryEscape()
        {
            if (!RecoveryContactsEnabled || recoveryEscapeActive) return;
            recoveryEscapeActive = true;
            recoveryEscapeClock = actor.JournalPoseClock;
            recoveryEscapeShoulder = hasLast ? lastUpper : Quaternion.Inverse(actor.transform.rotation) * upper.rotation;
            hasRejectedSearch = false;
        }

        private bool TryRecoveryShoulderEscape(bool constrainSupport, Quaternion desired)
        {
            // Repeat presentation at the same simulation clock repeats this exact
            // attempt. Time, rather than the number of renders/solves, opens the arc.
            float angle = Mathf.Clamp((actor.JournalPoseClock - recoveryEscapeClock) * 72f, .5f, 36f);
            Quaternion start = actor.transform.rotation * recoveryEscapeShoulder;
            for (int i = 0; i < 4 && remainingCandidateChecks > 0; i++)
            {
                Vector3 axis = i < 2 ? actor.transform.right : actor.transform.forward;
                upper.rotation = Quaternion.AngleAxis(i % 2 == 0 ? angle : -angle, axis) * start;
                if (TryCandidate(constrainSupport, out _)) return true;
            }
            upper.rotation = desired;
            return false;
        }

        private bool HasRecoveryFloorContact(int segment, Collider shape)
        {
            if (!RecoveryContactsEnabled) return false;
            foreach (RecoveryFloorContact contact in recoveryFloorContacts)
                if (contact.Segment == segment && contact.Shape == shape) return true;
            return false;
        }

        private bool AllowsRecoveryFloorContact(int segment, Collider shape, float depth)
        {
            if (!RecoveryContactsEnabled || depth > Skin + RecoveryContactTolerance) return false;
            foreach (RecoveryFloorContact contact in recoveryFloorContacts)
                if (contact.Segment == segment && contact.Shape == shape)
                    return depth <= contact.Depth + RecoveryContactTolerance;
            return false;
        }

        private bool RecoveryFloorPathClear(Pose from, Pose to)
        {
            if (!RecoveryContactsEnabled || recoveryFloorContacts.Count == 0) return true;
            float angle = Quaternion.Angle(from.rotation, to.rotation);
            float travel = Vector3.Distance(from.position, to.position) + angle * Mathf.Deg2Rad * .7f;
            // The escape has a tighter spacing than ordinary travel: a clear end
            // must not conceal an intermediate return into its original floor.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(travel / .005f, angle)));
            if (steps > 192)
            { BlockingShape = "recovery floor path budget"; return false; }
            for (int i = 0; i < recoveryFloorContacts.Count; i++)
                recoveryFloorDepths[i] = recoveryFloorContacts[i].Depth;
            for (int step = 0; step <= steps; step++)
            {
                Pose pose = CombatWeaponGeometry.At(from, to, step / (float)steps);
                for (int i = 0; i < recoveryFloorContacts.Count; i++)
                {
                    RecoveryFloorContact contact = recoveryFloorContacts[i];
                    if (contact.Shape == null) continue;
                    CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[contact.Segment];
                    Vector3 a = pose.position + pose.rotation * segment.A;
                    Vector3 b = pose.position + pose.rotation * segment.B;
                    float depth = RecoveryFloorDepth(segment, a, b, contact.Shape, out Vector3 direction);
                    if (depth > recoveryFloorDepths[i] + RecoveryContactTolerance ||
                        depth > Skin + RecoveryContactTolerance ||
                        (depth > 0f && direction.y < .65f) || !RecoveryCoreClear(segment, a, b, contact.Shape))
                    { BlockingShape = contact.Shape.name; return false; }
                    // Noise may not accumulate over many samples or attempts.
                    recoveryFloorDepths[i] = Mathf.Min(recoveryFloorDepths[i], depth);
                }
            }
            return true;
        }

        private void RememberRecoveryFloorContacts()
        {
            if (!RecoveryContactsEnabled) return;
            Pose pose = WeaponPose;
            for (int i = 0; i < recoveryFloorContacts.Count; i++)
            {
                RecoveryFloorContact contact = recoveryFloorContacts[i];
                if (contact.Shape == null) continue;
                CombatWeaponGeometry.Segment segment = CombatWeaponGeometry.Segments[contact.Segment];
                float depth = RecoveryFloorDepth(segment, pose.position + pose.rotation * segment.A,
                    pose.position + pose.rotation * segment.B, contact.Shape, out _);
                recoveryFloorContacts[i] = new RecoveryFloorContact(contact.Segment, contact.Shape, Mathf.Min(contact.Depth, depth));
            }
        }

        private float RecoveryFloorDepth(CombatWeaponGeometry.Segment segment, Vector3 a, Vector3 b,
            Collider shape, out Vector3 direction)
        {
            float radius = probe.radius, height = probe.height;
            probe.radius = segment.Radius + Skin;
            probe.height = Vector3.Distance(a, b) + probe.radius * 2f;
            Quaternion rotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
            actor.JournalPhysicsQuery();
            bool overlapping = Physics.ComputePenetration(probe, (a + b) * .5f, rotation, shape,
                shape.transform.position, shape.transform.rotation, out direction, out float depth);
            probe.radius = radius; probe.height = height;
            return overlapping ? depth : 0f;
        }

        private bool RecoveryCoreClear(CombatWeaponGeometry.Segment segment, Vector3 a, Vector3 b, Collider shape)
        {
            float radius = probe.radius, height = probe.height;
            probe.radius = segment.Radius;
            probe.height = Vector3.Distance(a, b) + probe.radius * 2f;
            Quaternion rotation = (b - a).sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, b - a) : Quaternion.identity;
            actor.JournalPhysicsQuery();
            bool overlapping = Physics.ComputePenetration(probe, (a + b) * .5f, rotation, shape,
                shape.transform.position, shape.transform.rotation, out _, out float depth);
            probe.radius = radius; probe.height = height;
            return !overlapping || depth <= RecoveryContactTolerance;
        }

        private readonly struct RecoveryFloorContact
        {
            internal readonly int Segment;
            internal readonly Collider Shape;
            internal readonly float Depth;
            internal RecoveryFloorContact(int segment, Collider shape, float depth)
            { Segment = segment; Shape = shape; Depth = depth; }
        }
    }
}
