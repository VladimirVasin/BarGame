using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Checks a corrected right arm against its own core without treating
    /// the normal shoulder/chest connection as a collision.</summary>
    internal sealed class CombatArmClearance : IDisposable
    {
        private const float AllowedOverlap = .004f;
        private readonly CombatActor actor;
        private readonly Transform upper, forearm;
        private readonly Collider forearmShape;
        private readonly Collider[] core;
        private readonly int[] upperCoreIndices;
        private readonly LiveShape[] liveCore;
        private readonly CapsuleCollider probe;
        private int supportSolveDepth;
        internal int CoreSnapshotCount { get; private set; }
        internal string LastBlockingShape { get; private set; }

        internal CombatArmClearance(CombatActor actor, Transform upper, Transform forearm)
        {
            if (actor == null || actor.Ragdoll == null || actor.Ragdoll.PhysicsController == null)
                throw new ArgumentException("Arm clearance requires the actor's anatomical rig.", nameof(actor));
            this.actor = actor;
            this.upper = upper != null ? upper : throw new ArgumentNullException(nameof(upper));
            this.forearm = forearm != null ? forearm : throw new ArgumentNullException(nameof(forearm));
            var body = new List<Collider>(4);
            var shoulder = new List<Collider>(2);
            foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (entry.Value == Player3DAnatomicalPart.RightForearm) forearmShape = entry.Key;
                if (entry.Value == Player3DAnatomicalPart.Pelvis || entry.Value == Player3DAnatomicalPart.LowerTorso ||
                    entry.Value == Player3DAnatomicalPart.Torso || entry.Value == Player3DAnatomicalPart.Head)
                    body.Add(entry.Key);
                if (entry.Value == Player3DAnatomicalPart.Torso || entry.Value == Player3DAnatomicalPart.Head)
                    shoulder.Add(entry.Key);
            }
            if (forearmShape == null || body.Count != 4 || shoulder.Count != 2)
                throw new InvalidOperationException("Arm clearance requires right forearm, pelvis, both torso segments and head.");
            core = body.ToArray();
            liveCore = new LiveShape[core.Length];
            upperCoreIndices = new int[shoulder.Count];
            for (int i = 0; i < shoulder.Count; i++) upperCoreIndices[i] = Array.IndexOf(core, shoulder[i]);
            var query = new GameObject("Combat right arm clearance query") { hideFlags = HideFlags.HideAndDontSave };
            probe = query.AddComponent<CapsuleCollider>();
            probe.enabled = false; probe.direction = 1;
        }

        internal bool IsClear()
        {
            LastBlockingShape = null;
            if (probe == null || forearmShape == null || upper == null || forearm == null) return false;
            SnapshotCore();
            Vector3 forearmPosition = forearmShape.transform.position;
            Quaternion forearmRotation = forearmShape.transform.rotation;
            Bounds forearmBounds = new CombatWeaponGeometry.ShapeBounds(forearmShape).At(forearmPosition, forearmRotation);
            foreach (LiveShape body in liveCore)
                if (Intersects(forearmShape, forearmPosition, forearmRotation, forearmBounds, body)) return false;

            // The rounded end of the limb collider tapers to the hinge. A small
            // elbow envelope prevents that point from being tucked inside the chest.
            probe.center = Vector3.zero;
            probe.radius = .025f; probe.height = .05f;
            Bounds elbowBounds = new Bounds(forearm.position, Vector3.one * .05f);
            foreach (LiveShape body in liveCore)
                if (Intersects(probe, forearm.position, Quaternion.identity, elbowBounds, body)) return false;

            // Exclude the proximal shoulder, where the arm naturally joins the torso.
            // The distal humerus must still remain outside chest and head when an
            // otherwise clear weapon is lifted or rotated around the shoulder.
            Vector3 start = Vector3.Lerp(upper.position, forearm.position, .45f);
            Vector3 axis = forearm.position - start;
            probe.radius = .045f; probe.height = axis.magnitude + probe.radius * 2f;
            Quaternion rotation = axis.sqrMagnitude > .000001f
                ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
            Bounds upperBounds = SegmentBounds(start, forearm.position, probe.radius);
            foreach (int index in upperCoreIndices)
                if (Intersects(probe, (start + forearm.position) * .5f, rotation, upperBounds, liveCore[index])) return false;
            return true;
        }

        /// <summary>Matches the author's elbow/forearm centreline envelope for
        /// choosing a supporting elbow; excludes the upper arm's shoulder join.</summary>
        internal bool IsSupportPathClear(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            // An elbow search changes only candidate vectors, never core bones.
            // Reuse its synchronous snapshot across candidates; every standalone
            // check and the next solve rereads live pose, scale and geometry.
            if (supportSolveDepth == 0) SnapshotCore();
            return SegmentClear(Vector3.Lerp(shoulder, elbow, .65f), elbow) && SegmentClear(elbow, wrist);

            bool SegmentClear(Vector3 start, Vector3 end)
            {
                Vector3 axis = end - start;
                probe.center = Vector3.zero;
                probe.radius = .005f;
                probe.height = axis.magnitude + probe.radius * 2f;
                Quaternion rotation = axis.sqrMagnitude > .000001f
                    ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                Bounds segmentBounds = SegmentBounds(start, end, probe.radius);
                foreach (LiveShape body in liveCore)
                {
                    if (body.Collider == null || !body.Bounds.Intersects(segmentBounds)) continue;
                    actor.JournalPhysicsQuery();
                    if (Physics.ComputePenetration(probe, (start + end) * .5f, rotation,
                        body.Collider, body.Pose.position, body.Pose.rotation, out _, out float depth) && depth > .00001f)
                        return false;
                }
                return true;
            }
        }

        private bool Intersects(Collider arm, Vector3 position, Quaternion rotation, Bounds armBounds, LiveShape body)
        {
            if (body.Collider == null || !body.Bounds.Intersects(armBounds)) return false;
            actor.JournalPhysicsQuery();
            if (!Physics.ComputePenetration(arm, position, rotation, body.Collider, body.Pose.position,
                body.Pose.rotation, out _, out float depth) || depth <= AllowedOverlap) return false;
            LastBlockingShape = body.Collider.name;
            return true;
        }

        private void SnapshotCore()
        {
            CoreSnapshotCount++;
            for (int i = 0; i < core.Length; i++) liveCore[i] = new LiveShape(core[i]);
        }

        /// <summary>Only for a synchronous search that does not mutate the rig.
        /// No frame/substep cache: Dispose makes the next check live again.</summary>
        internal SupportSolveScope BeginSupportSolve() => new SupportSolveScope(this);

        internal readonly struct SupportSolveScope : IDisposable
        {
            private readonly CombatArmClearance owner;
            internal SupportSolveScope(CombatArmClearance clearance)
            {
                owner = clearance;
                if (owner != null && owner.supportSolveDepth++ == 0) owner.SnapshotCore();
            }
            public void Dispose()
            {
                if (owner != null) owner.supportSolveDepth--;
            }
        }

        private static Bounds SegmentBounds(Vector3 start, Vector3 end, float radius)
        {
            var bounds = new Bounds(start, Vector3.zero);
            bounds.Encapsulate(end);
            bounds.Expand(radius * 2f);
            return bounds;
        }

        private readonly struct LiveShape
        {
            internal readonly Collider Collider;
            internal readonly Pose Pose;
            internal readonly Bounds Bounds;

            internal LiveShape(Collider collider)
            {
                Collider = collider;
                Pose = collider != null ? new Pose(collider.transform.position, collider.transform.rotation) : default;
                Bounds = collider != null
                    ? new CombatWeaponGeometry.ShapeBounds(collider).At(Pose.position, Pose.rotation) : default;
            }
        }

        public void Dispose()
        {
            if (probe != null) UnityEngine.Object.Destroy(probe.gameObject);
        }
    }
}
