using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal readonly struct RagdollBoneMotion
    {
        public readonly Vector3 Linear, Angular;
        public RagdollBoneMotion(Vector3 linear, Vector3 angular) { Linear = linear; Angular = angular; }
    }

    public sealed partial class Player3DRagdollController
    {
        private bool movingCombatAnchor, simulationSuspended, hasCombatElbowFrames;
        private Vector3[] suspendedLinear, suspendedAngular;
        private readonly List<PendingCombatImpulse> suspendedImpulses = new List<PendingCombatImpulse>(4);

        public bool IsSimulationSuspended => simulationSuspended;

        private bool ActivateFromCurrentPose(bool rebaseCombatElbows = false)
        {
            if (!initialized || IsActive) return false;
            presentation?.BeginRagdollPoseFromLatePose();
            RefreshJointAnchors();
            SetCollidersEnabled(true);
            Physics.SyncTransforms();
            // A reused hero may leave combat before an ordinary fall. Once its
            // elbow frames follow live pronation, every later handoff must do so.
            if (rebaseCombatElbows || hasCombatElbowFrames)
            {
                RebaseCombatElbows(rebaseCombatElbows ? 150f : 120f);
                hasCombatElbowFrames = true;
            }
            foreach (Rigidbody body in bodyList)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.isKinematic = false;
            }
            IsSimulating = true;
            return true;
        }

        private void RebaseCombatElbows(float maximumFlexion)
        {
            RebaseCombatElbow(Player3DAnatomicalPart.LeftUpperArm,
                Player3DAnatomicalPart.LeftForearm, Player3DAnatomicalPart.LeftHand,
                leftElbowAxisInUpperArm, maximumFlexion);
            RebaseCombatElbow(Player3DAnatomicalPart.RightUpperArm,
                Player3DAnatomicalPart.RightForearm, Player3DAnatomicalPart.RightHand,
                rightElbowAxisInUpperArm, maximumFlexion);
        }

        private void RebaseCombatElbow(Player3DAnatomicalPart upperPart,
            Player3DAnatomicalPart forearmPart, Player3DAnatomicalPart handPart,
            Vector3 referenceAxisInUpperArm, float maximumFlexion)
        {
            Rigidbody upper = bodies[upperPart], forearm = bodies[forearmPart];
            Vector3 upperDirection = (forearm.position - upper.position).normalized;
            Vector3 lowerDirection = (bones[handPart].position - forearm.position).normalized;
            if (upperDirection.sqrMagnitude < .5f || lowerDirection.sqrMagnitude < .5f) return;

            // The elbow's plane comes from its three visible joint positions,
            // independently of the forearm's authored pronation. At extension
            // that plane is ambiguous, so use the anatomical parent's frame.
            Vector3 axisWorld = Vector3.Cross(lowerDirection, upperDirection);
            if (axisWorld.sqrMagnitude < .000001f)
                axisWorld = Vector3.ProjectOnPlane(
                    upper.transform.TransformDirection(referenceAxisInUpperArm), lowerDirection);
            if (axisWorld.sqrMagnitude < .000001f)
                axisWorld = Vector3.Cross(lowerDirection,
                    Mathf.Abs(Vector3.Dot(lowerDirection, gameplayRoot.up)) < .9f
                        ? gameplayRoot.up : gameplayRoot.forward);
            axisWorld.Normalize();
            float flexion = Vector3.Angle(upperDirection, lowerDirection);

            for (int index = 0; index < joints.Count; index++)
            {
                ConfigurableJoint previous = joints[index];
                if (previous == null || previous.transform != forearm.transform ||
                    previous.connectedBody != upper) continue;

                // Creating the joint while both bodies still hold the sampled
                // kinematic pose makes that exact pronation its reference.
                ConfigurableJoint current = forearm.gameObject.AddComponent<ConfigurableJoint>();
                current.connectedBody = upper;
                current.autoConfigureConnectedAnchor = false;
                current.anchor = previous.anchor;
                current.connectedAnchor = previous.connectedAnchor;
                current.axis = forearm.transform.InverseTransformDirection(axisWorld).normalized;
                current.secondaryAxis = forearm.transform.InverseTransformDirection(lowerDirection).normalized;
                current.xMotion = current.yMotion = current.zMotion = ConfigurableJointMotion.Locked;
                current.angularXMotion = current.angularYMotion = current.angularZMotion = ConfigurableJointMotion.Limited;
                // With X = lower x upper, the parent's rotation from the child
                // counts increasing flexion positively. Offset the absolute
                // anatomical bounds, not the actual handoff geometry.
                current.lowAngularXLimit = Limit(-5f - flexion, 2f);
                current.highAngularXLimit = Limit(maximumFlexion - flexion, 2f);
                current.angularYLimit = Limit(8f, 2f);
                current.angularZLimit = Limit(8f, 2f);
                current.enableCollision = previous.enableCollision;
                current.enablePreprocessing = previous.enablePreprocessing;
                current.projectionMode = previous.projectionMode;
                current.projectionDistance = previous.projectionDistance;
                current.projectionAngle = previous.projectionAngle;
                current.breakForce = previous.breakForce;
                current.breakTorque = previous.breakTorque;
                current.massScale = previous.massScale;
                current.connectedMassScale = previous.connectedMassScale;
                joints[index] = current;

                // Destroy is deferred. Retire every constraint immediately so
                // an explicit Physics.Simulate in this frame cannot solve both
                // the old bind-pose frame and the new handoff frame.
                previous.xMotion = previous.yMotion = previous.zMotion = ConfigurableJointMotion.Free;
                previous.angularXMotion = previous.angularYMotion = previous.angularZMotion = ConfigurableJointMotion.Free;
                previous.xDrive = previous.yDrive = previous.zDrive = default;
                previous.angularXDrive = previous.angularYZDrive = previous.slerpDrive = default;
                previous.projectionMode = JointProjectionMode.None;
                previous.enableCollision = true;
                Destroy(previous);
                return;
            }
        }

        /// <summary>The supplied whole-body motion already includes the resolved hit impulse.
        /// Preserve limb motion relative to the pelvis, without applying the hit a second time.</summary>
        internal bool BeginCombatSimulation(Vector3 linear, Vector3 angular,
            IReadOnlyDictionary<Transform, RagdollBoneMotion> recordedMotion)
        {
            if (!initialized || IsSimulating) return false;
            // A new hit may interrupt the frozen rise; its visible pose is the new source.
            IsRecovering = IsFrozen = false;
            recoveryStart = null;
            if (!ActivateFromCurrentPose(true)) return false;
            Vector3 centre = PelvisBody.worldCenterOfMass;
            RagdollBoneMotion pelvisMotion = default;
            bool hasPelvis = recordedMotion != null && recordedMotion.TryGetValue(PelvisBody.transform, out pelvisMotion);
            foreach (Rigidbody body in bodyList)
            {
                Vector3 relativeLinear = Vector3.zero, relativeAngular = Vector3.zero;
                if (hasPelvis && recordedMotion.TryGetValue(body.transform, out RagdollBoneMotion bone))
                {
                    relativeLinear = bone.Linear - pelvisMotion.Linear -
                        Vector3.Cross(pelvisMotion.Angular, body.worldCenterOfMass - centre);
                    relativeAngular = bone.Angular - pelvisMotion.Angular;
                }
                body.linearVelocity = Vector3.ClampMagnitude(linear + Vector3.Cross(angular,
                    body.worldCenterOfMass - centre) + Vector3.ClampMagnitude(relativeLinear, 3f), 8f);
                body.angularVelocity = Vector3.ClampMagnitude(angular + Vector3.ClampMagnitude(relativeAngular, 6f), 10f);
            }
            movingCombatAnchor = true;
            return true;
        }

        /// <summary>Follow only with the kinematic anchor. Moving the parent actor would move
        /// every simulated child twice. Ground and wall collision remain owned by the bones.</summary>
        internal void AdvanceCombatAnchor()
        {
            if (!movingCombatAnchor || !IsSimulating || simulationSuspended) return;
            // Contacts arrive on the duel clock and may start hit-stop later in the
            // same Update. PhysX discards pending forces when a body becomes kinematic,
            // so apply them only in the first unfrozen physics step.
            foreach (PendingCombatImpulse impulse in suspendedImpulses)
                if (impulse.Body != null) impulse.Body.AddForceAtPosition(impulse.Impulse, impulse.Point, ForceMode.Impulse);
            suspendedImpulses.Clear();
            // Follow vertically too: an upright impact begins higher than an intoxication
            // topple. Keeping its original anchor height would suspend the pelvis above ground.
            rootAnchorBody.MovePosition(PelvisBody.position);
        }

        internal void AddCombatImpulse(Player3DAnatomicalPart part, Vector3 point, Vector3 impulse)
        {
            if (!IsSimulating || !FiniteCombatVector(point) || !FiniteCombatVector(impulse)) return;
            Player3DAnatomicalPart physicalPart = part switch
            {
                Player3DAnatomicalPart.LeftHand => Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.RightHand => Player3DAnatomicalPart.RightForearm,
                Player3DAnatomicalPart.Neck => Player3DAnatomicalPart.Torso,
                _ => part
            };
            Rigidbody body = GetBody(physicalPart) ?? ChestBody;
            // An outdated local contact must not become an arbitrarily long torque lever.
            point = body.worldCenterOfMass + Vector3.ClampMagnitude(point - body.worldCenterOfMass, .45f);
            impulse = Vector3.ClampMagnitude(impulse, 260f);
            suspendedImpulses.Add(new PendingCombatImpulse(body, point, impulse));
        }

        internal void SetSimulationSuspended(bool suspended)
        {
            if (suspended == simulationSuspended || !IsSimulating) return;
            if (suspended)
            {
                suspendedLinear ??= new Vector3[bodyList.Count];
                suspendedAngular ??= new Vector3[bodyList.Count];
                for (int i = 0; i < bodyList.Count; i++)
                {
                    Rigidbody body = bodyList[i];
                    suspendedLinear[i] = body.linearVelocity;
                    suspendedAngular[i] = body.angularVelocity;
                    body.isKinematic = true;
                    body.interpolation = RigidbodyInterpolation.None;
                }
            }
            else
            {
                for (int i = 0; i < bodyList.Count; i++)
                {
                    Rigidbody body = bodyList[i];
                    body.isKinematic = false;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.linearVelocity = suspendedLinear[i];
                    body.angularVelocity = suspendedAngular[i];
                }
            }
            simulationSuspended = suspended;
        }

        internal void RebaseRecoveryRoot(Vector3 position, Quaternion rotation)
        {
            if (!IsRecovering) return;
            gameplayRoot.SetPositionAndRotation(position, rotation);
            // CapturePose keeps the pelvis in world space and the full joint hierarchy in
            // parent space. Reapply it after rebasing: the visible body has not travelled.
            ApplyRecoveryBlend(0f);
            Physics.SyncTransforms();
        }

        private void ClearCombatSimulation()
        {
            // Cancellation discards held impulses; unfreezing here would apply a stale hit.
            simulationSuspended = false;
            movingCombatAnchor = false;
            suspendedImpulses.Clear();
        }

        private static bool FiniteCombatVector(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        private readonly struct PendingCombatImpulse
        {
            public readonly Rigidbody Body;
            public readonly Vector3 Point, Impulse;
            public PendingCombatImpulse(Rigidbody body, Vector3 point, Vector3 impulse)
            { Body = body; Point = point; Impulse = impulse; }
        }
    }
}
