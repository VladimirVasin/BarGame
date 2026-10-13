using System;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatWeaponConstraint
    {
        // Value data only: no captures, formatting or allocations in a solve.
        // Keep the authored geometry before any candidate or safe-pose fallback.
        [Serializable]
        internal struct OwnForearmPoseDiagnostic
        {
            public bool ownForearmPenetration;
            public int phase, segment, candidateChecks;
            public float attackElapsed, animationProgress, poseClock, depth, segmentRadius;
            public Vector3 penetrationDirection, segmentA, segmentB;
            public Vector3 capsulePosition, capsuleCenter, capsuleScale;
            public Quaternion capsuleRotation;
            public float capsuleRadius, capsuleHeight;
            public int capsuleDirection;
            public Vector3 handPosition, rawWeaponPosition, correctedWeaponPosition, weaponLocalPosition;
            public Quaternion rawHandRotation, rawHandLocalRotation, correctedHandRotation, correctedHandLocalRotation;
            public Quaternion rawWeaponRotation, correctedWeaponRotation, weaponLocalRotation;
            public Quaternion baseHandLocalRotation;
            public Vector3 forearmPosition;
            public Quaternion forearmRotation;
            public bool desiredSweepBlocked, preview, motionBlocked;
            public float correctionDegrees;
            public Vector3 rawCylinderCentre, rawCylinderAxis, rawPalmNormal, fingersInHand, palmInHand;
            public float rawWristDeviation, rawWristFlexion, rawWristCone, rawWeaponAxisError;
            public float correctedWristDeviation, correctedWristFlexion, correctedWristCone;
            public bool rawWristInvalid;
            public string finalBlockingShape;
            public string requestedBlockingActor, finalBlockingActor;
            public int requestedBlockingColliderId, finalBlockingColliderId;
            public bool requestedOwnGeometryBlocked;
            public int supportState;
            public float supportWeight;
            public string supportReason, supportRejection;
        }

        private OwnForearmPoseDiagnostic desiredOwnForearm;
        internal bool CaptureRejectedPoseDiagnostics { get; set; }
        internal OwnForearmPoseDiagnostic DesiredOwnForearmDiagnostic
        {
            get
            {
                OwnForearmPoseDiagnostic value = desiredOwnForearm;
                if (!CaptureRejectedPoseDiagnostics) return value;
                value.correctedHandRotation = hand.rotation;
                value.correctedHandLocalRotation = hand.localRotation;
                value.correctedWeaponPosition = weapon.position;
                value.correctedWeaponRotation = weapon.rotation;
                value.correctionDegrees = Quaternion.Angle(baseHand, hand.localRotation);
                value.candidateChecks = MaximumCandidateChecksPerApply - remainingCandidateChecks;
                value.desiredSweepBlocked = journalDesiredSweep;
                value.motionBlocked = MotionBlocked;
                value.finalBlockingShape = BlockingShape;
                value.requestedBlockingActor = requestedBlockingActor != null ? requestedBlockingActor.name : null;
                value.finalBlockingActor = BlockingActor != null ? BlockingActor.name : null;
                value.requestedBlockingColliderId = requestedBlockingCollider != null ? requestedBlockingCollider.GetEntityId().GetHashCode() : 0;
                value.finalBlockingColliderId = BlockingCollider != null ? BlockingCollider.GetEntityId().GetHashCode() : 0;
                value.requestedOwnGeometryBlocked = requestedOwnGeometryBlocked;
                if (actor.SupportGrip != null)
                {
                    value.supportState = (int)actor.SupportGrip.State;
                    value.supportWeight = actor.SupportGrip.Weight;
                    value.supportReason = actor.SupportGrip.JournalGripReason;
                    value.supportRejection = actor.SupportGrip.LastPoseRejection;
                }
                RightWristAngles(out value.correctedWristDeviation, out value.correctedWristFlexion,
                    out value.correctedWristCone);
                return value;
            }
        }

        private void BeginOwnForearmDiagnostic()
        {
            desiredOwnForearm = default;
            if (!CaptureRejectedPoseDiagnostics) return;
            CaptureOwnForearmDiagnostic();
        }

        private void CaptureOwnForearmDiagnostic()
        {
            desiredOwnForearm = new OwnForearmPoseDiagnostic
            {
                segment = -1, phase = (int)actor.State.Phase,
                attackElapsed = actor.State.AttackElapsed,
                animationProgress = actor.State.AnimationProgressAt(actor.State.AttackElapsed),
                poseClock = actor.JournalPoseClock, preview = contactPreview,
                handPosition = hand.position, rawHandRotation = hand.rotation,
                rawHandLocalRotation = hand.localRotation, baseHandLocalRotation = baseHand,
                rawWeaponPosition = weapon.position, rawWeaponRotation = weapon.rotation,
                weaponLocalPosition = weapon.localPosition, weaponLocalRotation = weapon.localRotation,
                forearmPosition = forearm.position, forearmRotation = forearm.rotation
            };
            desiredOwnForearm.rawWristInvalid = !RightWristCanHold(hand.rotation);
            desiredOwnForearm.rawCylinderCentre = rightHandPose.CylinderCentre(false);
            desiredOwnForearm.rawCylinderAxis = rightHandPose.CylinderAxis(false);
            desiredOwnForearm.rawPalmNormal = rightHandPose.PalmNormal(false);
            desiredOwnForearm.fingersInHand = rightFingersInHand;
            desiredOwnForearm.palmInHand = rightPalmInHand;
            desiredOwnForearm.rawWeaponAxisError = Vector3.Angle(weapon.up, desiredOwnForearm.rawCylinderAxis);
            RightWristAngles(out desiredOwnForearm.rawWristDeviation, out desiredOwnForearm.rawWristFlexion,
                out desiredOwnForearm.rawWristCone);
        }

        private void RightWristAngles(out float deviation, out float flexion, out float cone)
        {
            Vector3 direction = (hand.position - forearm.position).normalized;
            Vector3 fingers = hand.rotation * rightFingersInHand;
            Vector3 palm = hand.rotation * rightPalmInHand;
            Vector3 across = Vector3.Cross(palm, fingers).normalized;
            deviation = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, across), -1f, 1f)) * Mathf.Rad2Deg);
            flexion = Mathf.Abs(Mathf.Atan2(Vector3.Dot(direction, palm), Vector3.Dot(direction, fingers)) * Mathf.Rad2Deg);
            cone = Vector3.Angle(direction, fingers);
        }

        private void RecordOwnForearmPenetration(int segment, Vector3 a, Vector3 b,
            float radius, DepthShape shape, Vector3 direction, float depth)
        {
            if (!CaptureRejectedPoseDiagnostics) return;
            if (depth <= desiredOwnForearm.depth || !(shape.Collider is CapsuleCollider capsule)) return;
            desiredOwnForearm.ownForearmPenetration = true;
            desiredOwnForearm.segment = segment;
            desiredOwnForearm.segmentA = a; desiredOwnForearm.segmentB = b;
            desiredOwnForearm.segmentRadius = radius;
            desiredOwnForearm.depth = depth; desiredOwnForearm.penetrationDirection = direction;
            desiredOwnForearm.capsulePosition = shape.Pose.position;
            desiredOwnForearm.capsuleRotation = shape.Pose.rotation;
            desiredOwnForearm.capsuleCenter = capsule.center;
            desiredOwnForearm.capsuleScale = capsule.transform.lossyScale;
            desiredOwnForearm.capsuleRadius = capsule.radius;
            desiredOwnForearm.capsuleHeight = capsule.height;
            desiredOwnForearm.capsuleDirection = capsule.direction;
        }

    }
}
