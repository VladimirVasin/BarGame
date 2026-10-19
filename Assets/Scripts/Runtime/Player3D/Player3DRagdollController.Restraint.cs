using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DRagdollController
    {
        private readonly Dictionary<ConfigurableJoint, Quaternion> restraintJointReferences =
            new Dictionary<ConfigurableJoint, Quaternion>();
        private readonly List<RestraintJointDrive> restraintJointDrives = new List<RestraintJointDrive>(14);
        private Transform restraintContactBone;
        private Rigidbody restraintContactBody;
        private Vector3 restraintContactLocal, restraintPullTarget;
        private float restraintDriveSeconds;
        private int restraintDrivenLegJoints;

        public bool IsRestraintDriveActive => restraintContactBody != null && restraintJointDrives.Count > 0;
        public int RestraintDrivenJointCount => restraintJointDrives.Count;
        public int RestraintDrivenLegJointCount => restraintDrivenLegJoints;
        public float RestraintDriveTargetTravelDegrees { get; private set; }

        internal bool TryGetCombatBone(Player3DAnatomicalPart part, out Transform bone) => bones.TryGetValue(part, out bone);

        private void CaptureRestraintJointReference(ConfigurableJoint joint, ConfigurableJoint previous = null)
        {
            if (previous != null) restraintJointReferences.Remove(previous);
            if (joint != null && joint.connectedBody != null)
                restraintJointReferences[joint] = Quaternion.Inverse(joint.connectedBody.rotation) * joint.transform.rotation;
        }

        internal bool BeginRestraintDrive(Player3DAnatomicalPart part, Vector3 localPoint, Vector3 target)
        {
            if (!IsSimulating || !bones.TryGetValue(part, out Transform bone)) return false;
            Rigidbody body = CombatBodyForPart(part);
            if (body == null || removedCombatBodies.Contains(body)) return false;
            EndRestraintDrive();
            ResetCombatSurvivorArmAim();
            restraintContactBone = bone;
            restraintContactBody = body;
            restraintContactLocal = localPoint;
            restraintPullTarget = target;
            restraintDriveSeconds = 0f;
            RestraintDriveTargetTravelDegrees = 0f;
            foreach (ConfigurableJoint joint in joints)
            {
                if (joint == null || joint == pelvisTether || joint.connectedBody == null ||
                    !restraintJointReferences.TryGetValue(joint, out Quaternion reference)) continue;
                Rigidbody own = joint.GetComponent<Rigidbody>();
                if (removedCombatBodies.Contains(own) || removedCombatBodies.Contains(joint.connectedBody)) continue;
                if (!TryGetRestraintPart(own, out Player3DAnatomicalPart drivenPart)) continue;
                Quaternion frame = SurvivorJointFrame(joint);
                Quaternion relative = Quaternion.Inverse(joint.connectedBody.rotation) * own.rotation;
                Quaternion targetRotation = Quaternion.Inverse(frame) * Quaternion.Inverse(relative) * reference * frame;
                restraintJointDrives.Add(new RestraintJointDrive(joint, own, drivenPart, targetRotation, reference, frame));
                if (IsRestraintLegPart(drivenPart)) restraintDrivenLegJoints++;
                joint.rotationDriveMode = RotationDriveMode.Slerp;
                joint.angularXDrive = joint.angularYZDrive = default;
                joint.slerpDrive = RestraintMuscleFor(drivenPart);
                joint.targetRotation = targetRotation;
                joint.targetAngularVelocity = Vector3.zero;
                own.WakeUp();
            }
            return true;
        }

        internal void UpdateRestraintDrive(Vector3 target)
        {
            if (FiniteCombatVector(target)) restraintPullTarget = target;
        }

        internal void AdvanceRestraintDrive(float seconds)
        {
            if (restraintContactBody == null || restraintContactBone == null || !IsSimulating || simulationSuspended) return;
            Vector3 point = restraintContactBone.TransformPoint(restraintContactLocal);
            Vector3 delta = restraintPullTarget - point;
            float distance = delta.magnitude;
            Vector3 desired = distance > .001f ? delta / distance * (4.2f * Mathf.Clamp01((distance - .45f) / .85f)) : Vector3.zero;
            float mass = ActiveCombatMass;
            Vector3 force = (desired - restraintContactBody.GetPointVelocity(point)) * (mass * 7f) - Physics.gravity * mass;
            restraintContactBody.AddForceAtPosition(Vector3.ClampMagnitude(force, mass * 28f), point, ForceMode.Force);
            restraintDriveSeconds += seconds;
            float engagement = Mathf.SmoothStep(0f, 1f, restraintDriveSeconds / .22f);
            for (int i = 0; i < restraintJointDrives.Count; i++)
            {
                RestraintJointDrive drive = restraintJointDrives[i];
                if (drive.Joint == null) continue;
                Quaternion intent = RestraintEffortTarget(drive, point);
                Quaternion wanted = Quaternion.Slerp(drive.HeldTarget, LimitRestraintTarget(drive.Joint, intent), engagement);
                // Bound intent velocity too: a changing wound point cannot demand
                // an instantaneous shoulder turn from an otherwise physical body.
                Quaternion next = Quaternion.RotateTowards(drive.Joint.targetRotation, wanted, seconds * 240f);
                RestraintDriveTargetTravelDegrees += Quaternion.Angle(drive.Joint.targetRotation, next);
                drive.Joint.targetRotation = next;
                drive.Body.WakeUp();
                drive.Joint.connectedBody.WakeUp();
            }
        }

        internal void EndRestraintDrive()
        {
            restraintContactBone = null;
            restraintContactBody = null;
            foreach (RestraintJointDrive drive in restraintJointDrives)
            {
                if (drive.Joint == null) continue;
                drive.Joint.rotationDriveMode = drive.Mode;
                drive.Joint.slerpDrive = drive.Drive;
                drive.Joint.angularXDrive = drive.AngularXDrive;
                drive.Joint.angularYZDrive = drive.AngularYZDrive;
                drive.Joint.targetRotation = drive.Target;
                drive.Joint.targetAngularVelocity = drive.AngularVelocity;
            }
            restraintJointDrives.Clear();
            restraintDrivenLegJoints = 0;
        }

        private bool TryGetRestraintPart(Rigidbody body, out Player3DAnatomicalPart part)
        {
            foreach (var pair in bodies)
                if (pair.Value == body) { part = pair.Key; return true; }
            part = default;
            return false;
        }

        private static bool IsRestraintLegPart(Player3DAnatomicalPart part) => part is
            Player3DAnatomicalPart.LeftThigh or Player3DAnatomicalPart.RightThigh or
            Player3DAnatomicalPart.LeftShin or Player3DAnatomicalPart.RightShin or
            Player3DAnatomicalPart.LeftFoot or Player3DAnatomicalPart.RightFoot;

        private static JointDrive RestraintMuscleFor(Player3DAnatomicalPart part)
        {
            float spring, damping, torque;
            switch (part)
            {
                case Player3DAnatomicalPart.LowerTorso:
                case Player3DAnatomicalPart.Torso:
                    spring = 240f; damping = 26f; torque = 210f; break;
                case Player3DAnatomicalPart.LeftThigh:
                case Player3DAnatomicalPart.RightThigh:
                    spring = 260f; damping = 24f; torque = 170f; break;
                case Player3DAnatomicalPart.LeftShin:
                case Player3DAnatomicalPart.RightShin:
                    spring = 220f; damping = 20f; torque = 115f; break;
                case Player3DAnatomicalPart.LeftUpperArm:
                case Player3DAnatomicalPart.RightUpperArm:
                    spring = 170f; damping = 16f; torque = 72f; break;
                case Player3DAnatomicalPart.LeftForearm:
                case Player3DAnatomicalPart.RightForearm:
                    spring = 150f; damping = 12f; torque = 42f; break;
                case Player3DAnatomicalPart.Head:
                    spring = 75f; damping = 9f; torque = 34f; break;
                default:
                    spring = 100f; damping = 9f; torque = 36f; break;
            }
            return new JointDrive { positionSpring = spring, positionDamper = damping, maximumForce = torque };
        }

        private Quaternion RestraintEffortTarget(RestraintJointDrive drive, Vector3 woundPoint)
        {
            float cycle = restraintDriveSeconds * (Mathf.PI * 2f * 1.45f);
            bool left = drive.Part is Player3DAnatomicalPart.LeftThigh or Player3DAnatomicalPart.LeftShin or
                Player3DAnatomicalPart.LeftFoot or Player3DAnatomicalPart.LeftUpperArm or Player3DAnatomicalPart.LeftForearm;
            float phase = cycle + (left ? 0f : Mathf.PI);
            float flex = Mathf.Sin(phase), answer = Mathf.Sin(phase - .65f);
            switch (drive.Part)
            {
                case Player3DAnatomicalPart.LeftThigh:
                case Player3DAnatomicalPart.RightThigh:
                    return Quaternion.Euler(-65f + flex * 28f, (left ? 1f : -1f) * (8f + answer * 7f), 0f);
                case Player3DAnatomicalPart.LeftShin:
                case Player3DAnatomicalPart.RightShin:
                    return Quaternion.Euler(45f + answer * 40f, 0f, 0f);
                case Player3DAnatomicalPart.LeftFoot:
                case Player3DAnatomicalPart.RightFoot:
                    return Quaternion.Euler(flex * 17f, 0f, (left ? 1f : -1f) * 4f);
                case Player3DAnatomicalPart.LowerTorso:
                    return Quaternion.Euler(5f + Mathf.Sin(cycle + .4f) * 4f, Mathf.Sin(cycle * .5f) * 4f, flex * 3f);
                case Player3DAnatomicalPart.Torso:
                    return Quaternion.Euler(3f + Mathf.Sin(cycle) * 5f, Mathf.Sin(cycle * .7f) * 6f, -flex * 4f);
                case Player3DAnatomicalPart.Head:
                    return Quaternion.Euler(-8f + Mathf.Sin(cycle + .8f) * 9f, Mathf.Sin(cycle * .6f) * 16f, flex * 4f);
                case Player3DAnatomicalPart.LeftUpperArm:
                case Player3DAnatomicalPart.RightUpperArm:
                case Player3DAnatomicalPart.LeftForearm:
                case Player3DAnatomicalPart.RightForearm:
                    return RestraintArmEffort(drive, woundPoint, phase);
                default: return drive.HeldTarget;
            }
        }

        private Quaternion RestraintArmEffort(RestraintJointDrive drive, Vector3 woundPoint, float phase)
        {
            Player3DAnatomicalPart end = drive.Part switch
            {
                Player3DAnatomicalPart.LeftUpperArm => Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.RightUpperArm => Player3DAnatomicalPart.RightForearm,
                Player3DAnatomicalPart.LeftForearm => Player3DAnatomicalPart.LeftHand,
                _ => Player3DAnatomicalPart.RightHand
            };
            if (!bones.TryGetValue(end, out Transform tip)) return drive.HeldTarget;
            // Both hands try to reach the actual wound. A small alternating
            // withdrawal makes repeated attempts readable instead of a held limp pose.
            Vector3 reach = woundPoint + Vector3.up * .04f - drive.Body.position;
            Vector3 segment = tip.position - drive.Body.position;
            if (reach.sqrMagnitude < .001f || segment.sqrMagnitude < .001f) return drive.HeldTarget;
            Quaternion world = Quaternion.FromToRotation(segment, reach) * drive.Body.rotation;
            Quaternion relative = Quaternion.Inverse(drive.Joint.connectedBody.rotation) * world;
            Quaternion reaching = drive.InverseFrame * Quaternion.Inverse(relative) * drive.Reference * drive.Frame;
            float attempt = .66f + Mathf.Sin(phase) * .24f;
            Quaternion intent = Quaternion.Slerp(drive.HeldTarget, reaching, attempt);
            return intent * Quaternion.Euler(Mathf.Sin(phase * 1.7f) * 7f, 0f, Mathf.Sin(phase * .8f) * 6f);
        }

        private static Quaternion LimitRestraintTarget(ConfigurableJoint joint, Quaternion target)
        {
            // Slerp drive targets encode the inverse child-relative rotation.
            // The anatomical limits apply to the child from its reference pose:
            // hip flexion is positive, knee flexion negative on this Generic rig.
            Vector3 angles = SignedJointAngles(Quaternion.Inverse(target));
            float low = joint.lowAngularXLimit.limit, high = joint.highAngularXLimit.limit;
            float inset = Mathf.Min(1f, Mathf.Max(0f, (high - low) * .1f));
            angles.x = Mathf.Clamp(angles.x, low + inset, high - inset);
            float y = Mathf.Max(0f, joint.angularYLimit.limit - .5f), z = Mathf.Max(0f, joint.angularZLimit.limit - .5f);
            angles.y = Mathf.Clamp(angles.y, -y, y);
            angles.z = Mathf.Clamp(angles.z, -z, z);
            return Quaternion.Inverse(Quaternion.Euler(angles));
        }

        private readonly struct RestraintJointDrive
        {
            internal readonly ConfigurableJoint Joint;
            internal readonly Rigidbody Body;
            internal readonly Player3DAnatomicalPart Part;
            internal readonly RotationDriveMode Mode;
            internal readonly JointDrive Drive, AngularXDrive, AngularYZDrive;
            internal readonly Quaternion Target, HeldTarget, Reference, Frame, InverseFrame;
            internal readonly Vector3 AngularVelocity;
            internal RestraintJointDrive(ConfigurableJoint joint, Rigidbody body, Player3DAnatomicalPart part,
                Quaternion heldTarget, Quaternion reference, Quaternion frame)
            {
                Joint = joint; Body = body; Part = part;
                Mode = joint.rotationDriveMode; Drive = joint.slerpDrive;
                AngularXDrive = joint.angularXDrive; AngularYZDrive = joint.angularYZDrive;
                AngularVelocity = joint.targetAngularVelocity;
                Target = joint.targetRotation; HeldTarget = heldTarget;
                Reference = reference; Frame = frame; InverseFrame = Quaternion.Inverse(frame);
            }
        }
    }
}
