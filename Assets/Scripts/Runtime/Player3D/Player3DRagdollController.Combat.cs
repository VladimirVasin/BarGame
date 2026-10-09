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
        private readonly List<PendingCombatTorque> suspendedTorques = new List<PendingCombatTorque>(8);
        private readonly List<PendingBodyImpulse> suspendedBodyImpulses = new List<PendingBodyImpulse>(4);
        private readonly struct PendingBodyImpulse
        {
            internal readonly Player3DAnatomicalPart Part;
            internal readonly Vector3 Point, Impulse;
            internal PendingBodyImpulse(Player3DAnatomicalPart part, Vector3 point, Vector3 impulse)
            { Part = part; Point = point; Impulse = impulse; }
        }
        private bool combatHeadCollisionEnabled = true;

        public bool IsSimulationSuspended => simulationSuspended;

        private bool ActivateFromCurrentPose(bool rebaseCombatElbows = false, bool preservePresentedPose = false)
        {
            if (!initialized || IsActive) return false;
            presentation?.BeginRagdollPoseFromLatePose(preservePresentedPose);
            RefreshJointAnchors();
            SetCollidersEnabled(true);
            if (!combatHeadCollisionEnabled) ApplyCombatHeadCollision();
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
                if (removedCombatBodies.Contains(body)) continue;
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
            if (removedCombatBodies.Contains(upper) || removedCombatBodies.Contains(forearm)) return;
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
                CaptureSurvivorArmJointReference(current, previous);
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

        /// <summary>Starts with caller-owned whole-body motion and preserves relative limb motion.
        /// A temporary fall already includes its hit; a terminal contact can instead retain the
        /// exact presented pose and publish its anatomical impulse once after activation.</summary>
        internal bool BeginCombatSimulation(Vector3 linear, Vector3 angular,
            IReadOnlyDictionary<Transform, RagdollBoneMotion> recordedMotion, bool preservePresentedPose = false)
        {
            if (!initialized || IsSimulating) return false;
            // A new hit may interrupt the frozen rise; its visible pose is the new source.
            IsRecovering = IsFrozen = false;
            recoveryStart = null;
            if (!ActivateFromCurrentPose(true, preservePresentedPose)) return false;
            Vector3 centre = PelvisBody.worldCenterOfMass;
            RagdollBoneMotion pelvisMotion = default;
            bool hasPelvis = recordedMotion != null && recordedMotion.TryGetValue(PelvisBody.transform, out pelvisMotion);
            foreach (Rigidbody body in bodyList)
            {
                if (removedCombatBodies.Contains(body)) continue;
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
            foreach (PendingBodyImpulse impulse in suspendedBodyImpulses) DistributeBodyImpulse(impulse);
            suspendedBodyImpulses.Clear();
            foreach (PendingCombatImpulse impulse in suspendedImpulses)
                if (impulse.Body != null) impulse.Body.AddForceAtPosition(impulse.Impulse, impulse.Point, ForceMode.Impulse);
            suspendedImpulses.Clear();
            foreach (PendingCombatTorque torque in suspendedTorques)
                if (torque.Body != null) torque.Body.AddTorque(torque.Impulse, ForceMode.Impulse);
            suspendedTorques.Clear();
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
            if (removedCombatBodies.Contains(body)) return;
            // An outdated local contact must not become an arbitrarily long torque lever.
            point = body.worldCenterOfMass + Vector3.ClampMagnitude(point - body.worldCenterOfMass, .45f);
            impulse = Vector3.ClampMagnitude(impulse, 260f);
            suspendedImpulses.Add(new PendingCombatImpulse(body, point, impulse));
        }

        /// <summary>One conserved volley budget moves the complete body; a small contact share retains its rotation.</summary>
        internal void AddCombatVolleyImpulse(Player3DAnatomicalPart part, Vector3 point, Vector3 impulse)
        {
            if (!IsSimulating || !FiniteCombatVector(point) || !FiniteCombatVector(impulse)) return;
            impulse = Vector3.ClampMagnitude(impulse, ShotgunSettings.MaximumVolleyMomentum);
            suspendedBodyImpulses.Add(new PendingBodyImpulse(part, point, impulse));
        }

        private void DistributeBodyImpulse(PendingBodyImpulse pending)
        {
            float mass = 0f;
            foreach (Rigidbody body in bodyList) if (!removedCombatBodies.Contains(body)) mass += body.mass;
            if (mass <= 0f) return;
            foreach (Rigidbody body in bodyList)
                if (!removedCombatBodies.Contains(body)) suspendedImpulses.Add(new PendingCombatImpulse(body, body.worldCenterOfMass,
                    pending.Impulse * (.8f * body.mass / mass)));
            Rigidbody contact = CombatBodyForPart(pending.Part);
            if (contact != null && !removedCombatBodies.Contains(contact)) AddCombatImpulse(pending.Part, pending.Point, pending.Impulse * .2f);
            else AddCombatImpulse(Player3DAnatomicalPart.Torso, ChestBody.worldCenterOfMass, pending.Impulse * .2f);
        }

        /// <summary>Contraction of a live anatomical joint. Matching opposite angular
        /// impulses do not add a launch impulse to the complete body.</summary>
        internal void AddCombatContraction(Player3DAnatomicalPart part, float angularImpulse)
        {
            if (!IsSimulating || float.IsNaN(angularImpulse) || float.IsInfinity(angularImpulse)) return;
            Rigidbody body = GetBody(part);
            if (body == null || removedCombatBodies.Contains(body)) return;
            foreach (ConfigurableJoint joint in joints)
            {
                if (joint == null || joint.transform != body.transform || joint.connectedBody == null) continue;
                Vector3 impulse = joint.transform.TransformDirection(joint.axis).normalized *
                    Mathf.Clamp(angularImpulse, -.4f, .4f);
                suspendedTorques.Add(new PendingCombatTorque(body, impulse));
                suspendedTorques.Add(new PendingCombatTorque(joint.connectedBody, -impulse));
                return;
            }
        }

        /// <summary>The damaged-head owner supplies its remaining collision proxies.
        /// Keep the intact shape disabled even when a later corpse shot wakes physics.</summary>
        internal void SetCombatHeadCollisionEnabled(bool enabled)
        {
            combatHeadCollisionEnabled = enabled;
            ApplyCombatHeadCollision();
        }

        private void ApplyCombatHeadCollision()
        {
            foreach (KeyValuePair<Collider, Player3DAnatomicalPart> pair in partByCollider)
                if (pair.Value == Player3DAnatomicalPart.Head && pair.Key != null)
                    pair.Key.enabled = combatHeadCollisionEnabled && (IsSimulating || IsFrozen);
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
                    if (removedCombatBodies.Contains(body)) continue;
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
                    if (removedCombatBodies.Contains(body)) continue;
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
            suspendedTorques.Clear();
            suspendedBodyImpulses.Clear();
            // Recovery/cancellation ends motion, not injury. The head/body
            // destruction owners explicitly restore collision, joints and mass
            // on ResetActor; a later fall must retain this same damaged body.
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

        private readonly struct PendingCombatTorque
        {
            public readonly Rigidbody Body;
            public readonly Vector3 Impulse;
            public PendingCombatTorque(Rigidbody body, Vector3 impulse)
            { Body = body; Impulse = impulse; }
        }
    }
}
