using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DRagdollController
    {
        private readonly Dictionary<ConfigurableJoint, SurvivorArmJoint> survivorArmJoints =
            new Dictionary<ConfigurableJoint, SurvivorArmJoint>();
        private ConfigurableJoint survivorUpperJoint, survivorForearmJoint;
        private Quaternion survivorUpperTarget, survivorForearmTarget;
        private float survivorGoalMuzzleError;
        private int survivorAimRebases;
        internal bool HasActiveSurvivorArmAim => survivorUpperJoint != null &&
            survivorArmJoints.TryGetValue(survivorUpperJoint, out SurvivorArmJoint source) && source.Driving;

        internal string SurvivorArmAimDiagnostics
        {
            get
            {
                Rigidbody upper = GetBody(Player3DAnatomicalPart.RightUpperArm), forearm = GetBody(Player3DAnatomicalPart.RightForearm);
                if (upper == null || forearm == null || survivorUpperJoint == null || survivorForearmJoint == null) return "arm joints unavailable";
                Vector3 wrist = bones[Player3DAnatomicalPart.RightHand].position;
                float flexion = Vector3.SignedAngle(wrist - forearm.position, forearm.position - upper.position,
                    forearm.transform.TransformDirection(survivorForearmJoint.axis));
                return $"goal muzzle={survivorGoalMuzzleError:F2}; flex={flexion:F2}; active rebases={survivorAimRebases}; " +
                    $"upper goal error={Quaternion.Angle(upper.rotation, survivorUpperTarget):F2}, render mismatch={Quaternion.Angle(upper.rotation, upper.transform.rotation):F2}, torque={survivorUpperJoint.currentTorque.magnitude:F2}, " +
                    JointDiagnostic(survivorUpperJoint, upper) + "; " +
                    $"forearm goal error={Quaternion.Angle(forearm.rotation, survivorForearmTarget):F2}, render mismatch={Quaternion.Angle(forearm.rotation, forearm.transform.rotation):F2}, torque={survivorForearmJoint.currentTorque.magnitude:F2}, " +
                    JointDiagnostic(survivorForearmJoint, forearm);
            }
        }

        private string JointDiagnostic(ConfigurableJoint joint, Rigidbody body)
        {
            if (!survivorArmJoints.TryGetValue(joint, out SurvivorArmJoint source)) return "joint reference unavailable";
            Quaternion frame = SurvivorJointFrame(joint);
            Quaternion relative = Quaternion.Inverse(joint.connectedBody.rotation) * body.rotation;
            Quaternion current = Quaternion.Inverse(frame) * Quaternion.Inverse(relative) * source.Reference * frame;
            return $"joint actual={SignedJointAngles(current).ToString("F1")}, target={SignedJointAngles(joint.targetRotation).ToString("F1")}, " +
                $"limits=({joint.lowAngularXLimit.limit:F1},{joint.highAngularXLimit.limit:F1};{joint.angularYLimit.limit:F1};{joint.angularZLimit.limit:F1}), " +
                $"drive=({joint.rotationDriveMode};{joint.slerpDrive.positionSpring:F1},{joint.slerpDrive.positionDamper:F1},{joint.slerpDrive.maximumForce:F1}), " +
                $"motion=({joint.angularXMotion},{joint.angularYMotion},{joint.angularZMotion}), kinematic={body.isKinematic}, asleep={body.IsSleeping()}, " +
                $"mass={body.mass:F2}, COM lever={(body.worldCenterOfMass - body.position).ToString("F3")}, inertia={body.inertiaTensor.ToString("F3")}";
        }

        private static Vector3 SignedJointAngles(Quaternion rotation)
        {
            Vector3 angles = rotation.eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0f, angles.x), Mathf.DeltaAngle(0f, angles.y), Mathf.DeltaAngle(0f, angles.z));
        }

        private static Quaternion SurvivorJointFrame(ConfigurableJoint joint)
        {
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, joint.axis).normalized);
        }

        private sealed class SurvivorArmJoint
        {
            internal Quaternion Reference, Target;
            internal JointDrive Drive;
            internal RotationDriveMode Mode;
            internal bool Driving;
        }

        private void CaptureSurvivorArmJointReference(ConfigurableJoint joint, ConfigurableJoint previous = null)
        {
            if (previous != null)
            {
                if (survivorArmJoints.TryGetValue(previous, out SurvivorArmJoint source) && source.Driving) survivorAimRebases++;
                survivorArmJoints.Remove(previous);
            }
            if (joint == null || joint.connectedBody == null) return;
            if (joint.transform == bodies[Player3DAnatomicalPart.RightUpperArm].transform) survivorUpperJoint = joint;
            else if (joint.transform == bodies[Player3DAnatomicalPart.RightForearm].transform) survivorForearmJoint = joint;
            else return;
            survivorArmJoints[joint] = new SurvivorArmJoint
            {
                Reference = Quaternion.Inverse(joint.connectedBody.rotation) * joint.transform.rotation,
                Target = joint.targetRotation, Drive = joint.slerpDrive, Mode = joint.rotationDriveMode
            };
        }

        internal void ResetCombatSurvivorArmAim(bool force = false)
        {
            foreach (var pair in survivorArmJoints)
            {
                if (pair.Key == null || !force && !pair.Value.Driving) continue;
                pair.Key.rotationDriveMode = pair.Value.Mode;
                pair.Key.slerpDrive = pair.Value.Drive;
                pair.Key.targetRotation = pair.Value.Target;
                pair.Value.Driving = false;
            }
        }

        /// <summary>Muscle impulses aim the existing physical arm. This method
        /// never writes a bone/weapon transform or creates another pose owner.</summary>
        internal void AimCombatSurvivorArm(Vector3 target, Transform muzzle, Transform support, float seconds)
        {
            if (!IsSimulating || muzzle == null || seconds <= 0f || !FiniteCombatVector(target)) return;
            Rigidbody upper = GetBody(Player3DAnatomicalPart.RightUpperArm);
            Rigidbody forearm = GetBody(Player3DAnatomicalPart.RightForearm);
            if (upper == null || forearm == null || removedCombatBodies.Contains(upper) || removedCombatBodies.Contains(forearm)) return;
            Vector3 desired = target - muzzle.position;
            if (desired.sqrMagnitude < .0001f) return;
            desired.Normalize();
            if (survivorUpperJoint == null || survivorForearmJoint == null) return;
            Vector3 wrist = bones[Player3DAnatomicalPart.RightHand].position;
            Vector3 upperDirection = forearm.position - upper.position, lowerDirection = wrist - forearm.position;
            if (upperDirection.sqrMagnitude < .0001f || lowerDirection.sqrMagnitude < .0001f) return;
            Vector3 elbowAxis = forearm.transform.TransformDirection(survivorForearmJoint.axis).normalized;
            Quaternion aim = Quaternion.FromToRotation(muzzle.forward, desired);
            float flexion = Vector3.SignedAngle(lowerDirection, upperDirection, elbowAxis);
            Quaternion bend = Quaternion.AngleAxis(30f - flexion, elbowAxis);
            // Muzzle direction leaves barrel roll free. Use that freedom to
            // keep this anatomical shoulder near its own joint reference and
            // the elbow/wrist clear of the supporting torso and floor.
            aim = SelectSurvivorArmRoll(aim, bend, desired, upper, forearm, wrist);
            Quaternion forearmTarget = aim * forearm.rotation;
            Quaternion upperTarget = aim * bend * upper.rotation;
            survivorUpperTarget = upperTarget; survivorForearmTarget = forearmTarget;
            Quaternion renderedMuzzleLocal = Quaternion.Inverse(forearm.transform.rotation) * muzzle.rotation;
            survivorGoalMuzzleError = Vector3.Angle(forearmTarget * renderedMuzzleLocal * Vector3.forward, desired);
            // One coherent two-segment pose replaces two motors fighting the
            // same muzzle error. The existing limited joints solve the target
            // physically; neither a bone nor a weapon is rotated here.
            DriveSurvivorArmJoint(survivorUpperJoint, upperTarget, ChestBody.rotation, 120f, 12f, 28f);
            DriveSurvivorArmJoint(survivorForearmJoint, forearmTarget, upperTarget, 120f, 6f, 12f);
            AddSurvivorGravitySupport(forearm, upper, 6f, seconds);
            AddSurvivorGravitySupport(upper, ChestBody, 16f, seconds, forearm);
            if (support == null) return;
            Rigidbody left = GetBody(Player3DAnatomicalPart.LeftForearm);
            if (left == null || removedCombatBodies.Contains(left)) return;
            Vector3 leftWrist = bones[Player3DAnatomicalPart.LeftHand].position;
            Vector3 supportForce = Vector3.ClampMagnitude((support.position - leftWrist) * 65f -
                (left.linearVelocity - forearm.linearVelocity) * 4f, 30f) * seconds;
            AddCombatImpulse(Player3DAnatomicalPart.LeftForearm, leftWrist, supportForce);
            AddCombatImpulse(Player3DAnatomicalPart.RightForearm, wrist, -supportForce);
        }

        private Quaternion SelectSurvivorArmRoll(Quaternion aim, Quaternion bend, Vector3 direction,
            Rigidbody upper, Rigidbody forearm, Vector3 wrist)
        {
            if (!survivorArmJoints.TryGetValue(survivorUpperJoint, out SurvivorArmJoint source)) return aim;
            Vector3 upperLocal = Quaternion.Inverse(upper.rotation) * (forearm.position - upper.position);
            Vector3 lowerLocal = Quaternion.Inverse(forearm.rotation) * (wrist - forearm.position);
            float best = float.PositiveInfinity, bestRoll = 0f, clearance = ChestBody.worldCenterOfMass.y + .04f;
            for (int pass = 0; pass < 3; pass++)
            {
                float step = pass == 0 ? 30f : pass == 1 ? 5f : 1f;
                float centre = bestRoll, extent = pass == 0 ? 180f : pass == 1 ? 30f : 5f;
                for (float roll = centre - extent; roll <= centre + extent; roll += step)
                {
                    Quaternion candidate = Quaternion.AngleAxis(roll, direction) * aim;
                    Quaternion upperTarget = candidate * bend * upper.rotation;
                    float jointAngle = Quaternion.Angle(source.Reference,
                        Quaternion.Inverse(ChestBody.rotation) * upperTarget);
                    Vector3 elbow = upper.position + upperTarget * upperLocal;
                    Vector3 hand = elbow + candidate * forearm.rotation * lowerLocal;
                    float elbowBelow = Mathf.Max(0f, clearance - elbow.y), handBelow = Mathf.Max(0f, clearance - hand.y);
                    float cost = jointAngle * jointAngle + (elbowBelow * elbowBelow + handBelow * handBelow) * 1000000f;
                    if (cost >= best) continue;
                    best = cost; bestRoll = roll;
                }
            }
            return Quaternion.AngleAxis(bestRoll, direction) * aim;
        }

        private void DriveSurvivorArmJoint(ConfigurableJoint joint, Quaternion targetWorld,
            Quaternion parentTargetWorld, float spring, float damping, float maximumTorque)
        {
            if (!survivorArmJoints.TryGetValue(joint, out SurvivorArmJoint source)) return;
            Quaternion jointFrame = SurvivorJointFrame(joint);
            Quaternion targetRelative = Quaternion.Inverse(parentTargetWorld) * targetWorld;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = damping, maximumForce = maximumTorque };
            joint.targetRotation = Quaternion.Inverse(jointFrame) * Quaternion.Inverse(targetRelative) * source.Reference * jointFrame;
            source.Driving = true;
            // PhysX does not have to wake a sleeping island merely because a
            // managed target property changed. These are live muscle commands.
            joint.GetComponent<Rigidbody>().WakeUp();
            joint.connectedBody.WakeUp();
        }

        private void AddSurvivorGravitySupport(Rigidbody body, Rigidbody parent, float maximumTorque,
            float seconds, Rigidbody supportedChild = null)
        {
            Vector3 torque = Vector3.zero;
            // An inertia-scaled orientation motor alone cannot hold a loaded
            // forearm against gravity. Muscles carry its real moment about the
            // anatomical pivot; the shoulder also carries the forearm's load.
            if (body.useGravity)
                torque -= Vector3.Cross(body.worldCenterOfMass - body.position, Physics.gravity * body.mass);
            if (supportedChild != null && supportedChild.useGravity)
                torque -= Vector3.Cross(supportedChild.worldCenterOfMass - body.position, Physics.gravity * supportedChild.mass);
            torque = Vector3.ClampMagnitude(torque, maximumTorque) * seconds;
            suspendedTorques.Add(new PendingCombatTorque(body, torque));
            if (parent != null && !removedCombatBodies.Contains(parent)) suspendedTorques.Add(new PendingCombatTorque(parent, -torque));
        }
    }
}
