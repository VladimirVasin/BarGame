using UnityEngine;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private const float ContactTolerance = .001f;
        private const float GaitBlendEpsilon = .0005f;
        private float forwardTravelSpeed, turnIntent, previousTravelSpeed;
        private float forwardGaitWeightVelocity, forwardRunShare, forwardRunShareVelocity;
        private SecondOrderFilter travelPitchFilter = new SecondOrderFilter(17f, .9f);
        private SecondOrderFilter travelRollFilter = new SecondOrderFilter(14f, .9f);
        private SecondOrderFilter gaitTransitionTorso = new SecondOrderFilter(24f, 1f);
        private SecondOrderFilter gaitTransitionArms = new SecondOrderFilter(15f, 1f);
        private float travelPitch, travelRoll;

        private SavedRotation chestBeforeAction, leftShoulderBeforeAction, rightShoulderBeforeAction;
        private SavedRotation leftElbowBeforeAction, rightElbowBeforeAction;
        private SavedRotation leftWristBeforeAction, rightWristBeforeAction, headBeforeAction;
        private Vector3 neckPositionBeforeAction;
        private bool actionWeightApplied;

        internal float GaitTransitionLean => gaitTransitionTorso.Value;

        private float ForwardGaitWeight => gaitWeights[WalkGait] + gaitWeights[RunGait];
        private float TargetForwardGaitWeight => targetGaitWeights[WalkGait] + targetGaitWeights[RunGait];

        private void ApplyForwardGaitWeights(bool immediate, float deltaTime)
        {
            float weight = ForwardGaitWeight;
            float target = TargetForwardGaitWeight;
            // The ratio and the amount of forward motion are independent.
            // Braking between two moving gaits never brings Idle into the mix.
            float targetShare = target > GaitBlendEpsilon ? targetGaitWeights[RunGait] / target : forwardRunShare;
            forwardRunShare = ApproachGaitWeight(forwardRunShare, targetShare, ref forwardRunShareVelocity,
                immediate || weight <= GaitBlendEpsilon, deltaTime);
            weight = ApproachGaitWeight(weight, target, ref forwardGaitWeightVelocity, immediate, deltaTime);
            gaitWeights[RunGait] = weight * forwardRunShare;
            gaitWeights[WalkGait] = weight - gaitWeights[RunGait];
        }

        private static float ApproachGaitWeight(float weight, float target, ref float velocity, bool immediate, float deltaTime)
        {
            if (!immediate)
            {
                if (deltaTime <= 0f) return weight;
                float time = target > weight ? LocomotionBlendInTime : LocomotionBlendOutTime;
                float next = Mathf.SmoothDamp(weight, target, ref velocity, time, Mathf.Infinity, deltaTime);
                float bounded = Mathf.Clamp01(next);
                if (bounded != next) velocity = 0f;
                if (Mathf.Abs(bounded - target) >= GaitBlendEpsilon) return bounded;
            }

            velocity = 0f;
            return target;
        }

        private void AdvanceGaitTransition(float deltaTime)
        {
            float target = 0f;
            float desiredWeight = TargetForwardGaitWeight;
            if (forwardGaitDominant && desiredWeight > GaitBlendEpsilon && fallAmount <= .001f)
            {
                float visibleMotion = Mathf.Min(ForwardGaitWeight, desiredWeight);
                float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, .9f, visibleMotion));
                float change = 4f * (targetGaitWeights[RunGait] / desiredWeight - forwardRunShare);
                target = weight * change / (1f + Mathf.Abs(change));
            }

            // The torso gathers or checks the stride first. The arms finish
            // their swing more slowly and carry their velocity through a reversal.
            gaitTransitionTorso.Advance(target, deltaTime);
            gaitTransitionArms.Advance(target, deltaTime);
        }

        private void ResetGaitTransition()
        {
            gaitTransitionTorso.Reset();
            gaitTransitionArms.Reset();
        }

        private void AdvanceTravelWeight(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            // Use executed velocity so resistance and braking show in the body.
            // An obstructed step cannot accumulate a new movement impulse.
            float acceleration = (forwardTravelSpeed - previousTravelSpeed) / deltaTime;
            previousTravelSpeed = forwardTravelSpeed;
            float free = (1f - coldBodyWeight) * (1f - Mathf.Clamp01(balancePose.Weight));
            float pitch = Mathf.Clamp(acceleration * .36f, -4f, 5f) * free;
            float banking = -turnIntent * Mathf.Clamp01(planarSpeed / FullWalkSpeed) * 4f * free;
            travelPitch = travelPitchFilter.Advance(pitch, deltaTime);
            travelRoll = travelRollFilter.Advance(banking, deltaTime);
        }

        private void ResetTravelWeight()
        {
            previousTravelSpeed = forwardTravelSpeed;
            travelPitch = travelRoll = 0f;
            travelPitchFilter.Reset();
            travelRollFilter.Reset();
        }

        /// <summary>Weight the torso within the sampled action while keeping its prop contacts.</summary>
        private void ApplyActionWeight()
        {
            if (activeClipOwner != ClipOwner.External || !IsClipActive || ragdollPoseActive ||
                combatMotionOwner != null || scopedClipLocomotion || scopedClipBlend.IsValid() ||
                scopedClipUpperTime.IsValid() || chestBone == null || headBone == null || neckBone == null ||
                leftUpperArmBone == null || leftForearmBone == null || leftHandBone == null ||
                rightUpperArmBone == null || rightForearmBone == null || rightHandBone == null) return;
            if (activeClipBinding.Category is "bed" or "cold" or "fall") return;

            float phase = activeClipNormalizedTime;
            if (phase <= 0f || phase >= 1f) return;
            float pulse = Mathf.Sin(phase * Mathf.PI);
            pulse *= pulse;
            Vector3 forward = actorFacingTransform.forward;
            Vector3 right = actorFacingTransform.right;
            Vector3 leftHandTarget = leftHandBone.position;
            Vector3 rightHandTarget = rightHandBone.position;
            Vector3 leftElbow = leftForearmBone.position;
            Vector3 rightElbow = rightForearmBone.position;
            Vector3 chest = chestBone.position;
            float reach = Vector3.Dot((leftHandTarget + rightHandTarget) * .5f - chest, forward);
            float imbalance = Vector3.Dot(rightHandTarget - leftHandTarget, forward);
            float pitch = Mathf.Clamp(reach * 16f, -1.4f, 4f) * pulse;
            float yaw = Mathf.Clamp(imbalance * 11f, -3f, 3f) * pulse;
            if (Mathf.Abs(pitch) + Mathf.Abs(yaw) < .08f) return;

            // Stay away from the straight-arm singularity where a small
            // shoulder motion could make the authored grip unreachable.
            float freedom = Mathf.Min(ReachMargin(leftUpperArmBone, leftForearmBone, leftHandBone),
                                      ReachMargin(rightUpperArmBone, rightForearmBone, rightHandBone));
            if (freedom <= .005f) return;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.005f, .035f, freedom));
            Quaternion leftHandRotation = leftHandBone.rotation;
            Quaternion rightHandRotation = rightHandBone.rotation;
            Vector3 headPosition = headBone.position;
            Quaternion headRotation = headBone.rotation;
            // Retain neck overlap while aligning the head back onto the
            // exact authored mouth and eye frames.
            float arcLength = (Mathf.Abs(pitch) + Mathf.Abs(yaw)) * Mathf.Deg2Rad *
                              Vector3.Distance(chest, headPosition);
            weight = Mathf.Min(weight, .010f / Mathf.Max(.010f, arcLength));
            chestBeforeAction = new SavedRotation(chestBone);
            headBeforeAction = new SavedRotation(headBone);
            neckPositionBeforeAction = neckBone.localPosition;
            leftShoulderBeforeAction = new SavedRotation(leftUpperArmBone);
            rightShoulderBeforeAction = new SavedRotation(rightUpperArmBone);
            leftElbowBeforeAction = new SavedRotation(leftForearmBone);
            rightElbowBeforeAction = new SavedRotation(rightForearmBone);
            leftWristBeforeAction = new SavedRotation(leftHandBone);
            rightWristBeforeAction = new SavedRotation(rightHandBone);
            actionWeightApplied = true;

            chestBone.rotation = Quaternion.AngleAxis(pitch * weight, right) *
                                 Quaternion.AngleAxis(yaw * weight, actorFacingTransform.up) * chestBone.rotation;
            neckBone.position += headPosition - headBone.position;
            headBone.rotation = headRotation;
            LimbTwoBoneIk.Solve(leftUpperArmBone, leftForearmBone, leftHandBone,
                leftHandTarget, leftHandRotation, leftElbow);
            LimbTwoBoneIk.Solve(rightUpperArmBone, rightForearmBone, rightHandBone,
                rightHandTarget, rightHandRotation, rightElbow);
            if ((leftHandBone.position - leftHandTarget).sqrMagnitude > ContactTolerance * ContactTolerance ||
                (rightHandBone.position - rightHandTarget).sqrMagnitude > ContactTolerance * ContactTolerance)
                RestoreActionWeight();
        }

        private static float ReachMargin(Transform shoulder, Transform elbow, Transform wrist) =>
            LimbTwoBoneIk.ChainLength(shoulder, elbow, wrist) - Vector3.Distance(shoulder.position, wrist.position);

        private void RestoreActionWeight()
        {
            if (!actionWeightApplied) return;
            actionWeightApplied = false;
            chestBeforeAction.Restore();
            if (neckBone != null) neckBone.localPosition = neckPositionBeforeAction;
            headBeforeAction.Restore();
            leftShoulderBeforeAction.Restore();
            rightShoulderBeforeAction.Restore();
            leftElbowBeforeAction.Restore();
            rightElbowBeforeAction.Restore();
            leftWristBeforeAction.Restore();
            rightWristBeforeAction.Restore();
        }

        private readonly struct SavedRotation
        {
            private readonly Transform target;
            private readonly Quaternion rotation;

            public SavedRotation(Transform target)
            {
                this.target = target;
                rotation = target.localRotation;
            }

            public void Restore()
            {
                if (target != null) target.localRotation = rotation;
            }
        }
    }
}
