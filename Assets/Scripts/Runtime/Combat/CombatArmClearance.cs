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
        private readonly Transform upper, forearm;
        private readonly Collider forearmShape;
        private readonly Collider[] core, upperCore;
        private readonly CapsuleCollider probe;
        internal string LastBlockingShape { get; private set; }

        internal CombatArmClearance(CombatActor actor, Transform upper, Transform forearm)
        {
            if (actor == null || actor.Ragdoll == null || actor.Ragdoll.PhysicsController == null)
                throw new ArgumentException("Arm clearance requires the actor's anatomical rig.", nameof(actor));
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
            core = body.ToArray(); upperCore = shoulder.ToArray();
            var query = new GameObject("Combat right arm clearance query") { hideFlags = HideFlags.HideAndDontSave };
            probe = query.AddComponent<CapsuleCollider>();
            probe.enabled = false; probe.direction = 1;
        }

        internal bool IsClear()
        {
            LastBlockingShape = null;
            if (probe == null || forearmShape == null || upper == null || forearm == null) return false;
            foreach (Collider body in core)
                if (Intersects(forearmShape, forearmShape.transform.position, forearmShape.transform.rotation, body)) return false;

            // The rounded end of the limb collider tapers to the hinge. A small
            // elbow envelope prevents that point from being tucked inside the chest.
            probe.center = Vector3.zero;
            probe.radius = .025f; probe.height = .05f;
            foreach (Collider body in core)
                if (Intersects(probe, forearm.position, Quaternion.identity, body)) return false;

            // Exclude the proximal shoulder, where the arm naturally joins the torso.
            // The distal humerus must still remain outside chest and head when an
            // otherwise clear weapon is lifted or rotated around the shoulder.
            Vector3 start = Vector3.Lerp(upper.position, forearm.position, .45f);
            Vector3 axis = forearm.position - start;
            probe.radius = .045f; probe.height = axis.magnitude + probe.radius * 2f;
            Quaternion rotation = axis.sqrMagnitude > .000001f
                ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
            foreach (Collider body in upperCore)
                if (Intersects(probe, (start + forearm.position) * .5f, rotation, body)) return false;
            return true;
        }

        /// <summary>Matches the author's elbow/forearm centreline envelope for
        /// choosing a supporting elbow; excludes the upper arm's shoulder join.</summary>
        internal bool IsSupportPathClear(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            return SegmentClear(Vector3.Lerp(shoulder, elbow, .65f), elbow) && SegmentClear(elbow, wrist);

            bool SegmentClear(Vector3 start, Vector3 end)
            {
                Vector3 axis = end - start;
                probe.center = Vector3.zero;
                probe.radius = .005f;
                probe.height = axis.magnitude + probe.radius * 2f;
                Quaternion rotation = axis.sqrMagnitude > .000001f
                    ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                foreach (Collider body in core)
                    if (body != null && Physics.ComputePenetration(probe, (start + end) * .5f, rotation,
                        body, body.transform.position, body.transform.rotation, out _, out float depth) && depth > .00001f)
                        return false;
                return true;
            }
        }

        private bool Intersects(Collider arm, Vector3 position, Quaternion rotation, Collider body)
        {
            if (body == null) return false;
            if (!Physics.ComputePenetration(arm, position, rotation, body, body.transform.position,
                body.transform.rotation, out _, out float depth) || depth <= AllowedOverlap) return false;
            LastBlockingShape = body.name;
            return true;
        }

        public void Dispose()
        {
            if (probe != null) UnityEngine.Object.Destroy(probe.gameObject);
        }
    }
}
