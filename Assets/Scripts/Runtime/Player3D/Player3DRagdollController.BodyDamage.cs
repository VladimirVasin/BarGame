using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DRagdollController
    {
        private readonly HashSet<Rigidbody> removedCombatBodies = new HashSet<Rigidbody>();
        private readonly Dictionary<ConfigurableJoint, RetiredBodyJoint> retiredBodyJoints = new Dictionary<ConfigurableJoint, RetiredBodyJoint>();
        private CombatBodyDamageState combatBodyDamage;
        private bool authoredBodyCollision;
        private readonly HashSet<Collider> authoredCombatColliders = new HashSet<Collider>();
        private readonly Dictionary<Rigidbody, float> completeCombatMass = new Dictionary<Rigidbody, float>();
        internal void RegisterCombatBodyCollider(Collider collider) => authoredCombatColliders.Add(collider);
        internal bool IsCombatAnatomicalCollider(Collider collider) => partByCollider.ContainsKey(collider) || authoredCombatColliders.Contains(collider);
        public float ActiveCombatMass
        { get { float mass = 0f; foreach (Rigidbody body in bodyList) if (!removedCombatBodies.Contains(body)) mass += body.mass; return mass; } }
        internal Rigidbody CombatBodyForPart(Player3DAnatomicalPart part) => GetBody(part switch
        {
            Player3DAnatomicalPart.LeftHand => Player3DAnatomicalPart.LeftForearm,
            Player3DAnatomicalPart.RightHand => Player3DAnatomicalPart.RightForearm,
            Player3DAnatomicalPart.Neck => Player3DAnatomicalPart.Torso, _ => part
        });

        internal void SetCombatBodyDamage(CombatBodyDamageState state, bool authoredCollision)
        {
            combatBodyDamage = state; authoredBodyCollision = authoredCollision;
            if (state == null)
            {
                foreach (var pair in retiredBodyJoints) if (pair.Key != null) pair.Value.Restore(pair.Key);
                retiredBodyJoints.Clear(); removedCombatBodies.Clear();
                ResetCombatSurvivorArmAim(force: true);
                foreach (var mass in completeCombatMass) if (mass.Key != null) mass.Key.mass = mass.Value;
                completeCombatMass.Clear();
                return;
            }
            foreach (var entry in bodies)
            {
                Rigidbody body = entry.Value;
                if (!completeCombatMass.ContainsKey(body)) completeCombatMass.Add(body, body.mass);
                float loss = 0f;
                for (int patch = 0; patch < 4; patch++) loss += state.TissueLoss(CombatBodyAnatomy.ToRegion(entry.Key), patch) * .25f;
                body.mass = completeCombatMass[body] * Mathf.Lerp(1f, .2f, loss);
                if (state.IsAttached(CombatBodyAnatomy.ToRegion(entry.Key))) continue;
                removedCombatBodies.Add(body);
                if (!body.isKinematic) { body.linearVelocity = body.angularVelocity = Vector3.zero; body.isKinematic = true; }
                body.interpolation = RigidbodyInterpolation.None;
            }
            foreach (ConfigurableJoint joint in joints)
            {
                if (joint == null || retiredBodyJoints.ContainsKey(joint)) continue;
                Rigidbody own = joint.GetComponent<Rigidbody>();
                if (!removedCombatBodies.Contains(own) && !removedCombatBodies.Contains(joint.connectedBody)) continue;
                retiredBodyJoints.Add(joint, new RetiredBodyJoint(joint));
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                joint.xDrive = joint.yDrive = joint.zDrive = default;
                joint.angularXDrive = joint.angularYZDrive = joint.slerpDrive = default;
                joint.projectionMode = JointProjectionMode.None;
            }
            suspendedImpulses.RemoveAll(impulse => removedCombatBodies.Contains(impulse.Body));
            suspendedTorques.RemoveAll(impulse => removedCombatBodies.Contains(impulse.Body));
            foreach (var pair in partByCollider)
                if (pair.Value != Player3DAnatomicalPart.Head && pair.Key != null &&
                    (authoredCollision || !state.IsAttached(CombatBodyAnatomy.ToRegion(pair.Value)))) pair.Key.enabled = false;
        }
        private bool CombatColliderAllowed(Collider collider)
        {
            if (combatBodyDamage == null || !partByCollider.TryGetValue(collider, out Player3DAnatomicalPart part)) return true;
            return combatBodyDamage.IsAttached(CombatBodyAnatomy.ToRegion(part)) &&
                (!authoredBodyCollision || part == Player3DAnatomicalPart.Head);
        }
        private readonly struct RetiredBodyJoint
        {
            private readonly ConfigurableJointMotion x, y, z, ax, ay, az;
            private readonly JointDrive dx, dy, dz, dax, dayz, ds;
            private readonly JointProjectionMode projection;
            internal RetiredBodyJoint(ConfigurableJoint joint)
            {
                x = joint.xMotion; y = joint.yMotion; z = joint.zMotion;
                ax = joint.angularXMotion; ay = joint.angularYMotion; az = joint.angularZMotion;
                dx = joint.xDrive; dy = joint.yDrive; dz = joint.zDrive;
                dax = joint.angularXDrive; dayz = joint.angularYZDrive; ds = joint.slerpDrive; projection = joint.projectionMode;
            }
            internal void Restore(ConfigurableJoint joint)
            {
                joint.xMotion = x; joint.yMotion = y; joint.zMotion = z;
                joint.angularXMotion = ax; joint.angularYMotion = ay; joint.angularZMotion = az;
                joint.xDrive = dx; joint.yDrive = dy; joint.zDrive = dz;
                joint.angularXDrive = dax; joint.angularYZDrive = dayz; joint.slerpDrive = ds; joint.projectionMode = projection;
            }
        }
    }
}
