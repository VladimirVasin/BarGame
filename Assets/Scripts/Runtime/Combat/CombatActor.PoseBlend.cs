using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private const float PoseBlendSeconds = .14f;
        private float poseBlendDuration = PoseBlendSeconds;
        private float poseBlendRemaining;
        private int poseBlendAfterFrame;
        private bool heroPoseBlendOwned;
        private Transform[] npcPoseBones;
        private Vector3[] npcBlendPositions;
        private Quaternion[] npcBlendRotations;
        private Vector3[] npcPresentedPositions;
        private Quaternion[] npcPresentedRotations;
        private Vector3[] npcPreviousPositions, npcBlendVelocities;
        private Quaternion[] npcPreviousRotations;
        private Vector3[] npcBlendAngularVelocities;
        private float npcPresentedClock, npcPreviousClock;
        private bool npcPresentedPoseValid;
        private CombatBodyMotion bodyMotion;
        private Vector3 locomotionVelocity;

        private void InitializeNpcPoseBlend()
        {
            npcPoseBones = npc.Animator.GetComponentsInChildren<Transform>(true);
            npcBlendPositions = new Vector3[npcPoseBones.Length];
            npcBlendRotations = new Quaternion[npcPoseBones.Length];
            npcPresentedPositions = new Vector3[npcPoseBones.Length];
            npcPresentedRotations = new Quaternion[npcPoseBones.Length];
            npcPreviousPositions = new Vector3[npcPoseBones.Length];
            npcPreviousRotations = new Quaternion[npcPoseBones.Length];
            npcBlendVelocities = new Vector3[npcPoseBones.Length];
            npcBlendAngularVelocities = new Vector3[npcPoseBones.Length];
            npcPresentedPoseValid = false;
        }

        private void AdvanceVisualClock(float seconds)
        {
            damagePose?.Advance(seconds, State.Health / State.Settings.MaxHealth);
            supportGrip?.Advance(seconds);
            bodyMotion?.Advance(seconds, motor != null ? motor.PlanarVelocity : locomotionVelocity);
            poseClock += seconds;
            if (poseBlendRemaining > 0f)
            {
                poseBlendRemaining = Mathf.Max(0f, poseBlendRemaining - seconds);
                if (poseBlendRemaining <= 0f) CancelPoseBlend();
                else if (heroPoseBlendOwned) hero.SetOwnedRecoveryPoseClock(this, poseBlendDuration - poseBlendRemaining);
            }
            if (reaction == null) return;
            reactionClock += seconds;
            if (reactionClock >= reaction.length) reaction = null;
        }

        private void BeginPoseBlend(float duration = PoseBlendSeconds)
        {
            if (visibleClip == null || Time.frameCount <= poseBlendAfterFrame) return;
            poseBlendDuration = duration;
            poseBlendRemaining = duration;
            if (hero != null)
            {
                // The shared final-pose blend preserves the last visible velocity,
                // and runs after gait/foot planting just like contextual recovery.
                hero.BeginRecoveryPoseTransition(duration);
                hero.SetOwnedRecoveryPoseClock(this, 0f);
                heroPoseBlendOwned = true;
            }
            else
            {
                if (!npcPresentedPoseValid) { poseBlendRemaining = 0f; return; }
                for (int i = 0; i < npcPoseBones.Length; i++)
                {
                    // Contact sampling and additive restoration have already
                    // touched the live bones. The last final presentation is
                    // the source, including its injury pose and any old blend.
                    npcBlendPositions[i] = npcPresentedPositions[i];
                    npcBlendRotations[i] = npcPresentedRotations[i];
                    float delta = npcPresentedClock - npcPreviousClock;
                    bool moving = delta > .00001f && delta <= .1f;
                    float scale = Mathf.Max(.001f, npcPoseBones[i].parent.lossyScale.magnitude / Mathf.Sqrt(3f));
                    npcBlendVelocities[i] = moving ? Vector3.ClampMagnitude(
                        (npcPresentedPositions[i] - npcPreviousPositions[i]) / delta, 1.5f / scale) : Vector3.zero;
                    npcBlendAngularVelocities[i] = moving ? Vector3.ClampMagnitude(
                        Player3DCharacterPresentation.RecoveryAngularVelocity(npcPreviousRotations[i], npcPresentedRotations[i], delta), 8f) : Vector3.zero;
                }
            }
        }

        private void ApplyNpcPoseBlend()
        {
            // Runs after damage: blending a final injured source toward an
            // uninjured target and then adding injury would apply it twice.
            if (poseBlendRemaining <= 0f) return;
            float elapsed = poseBlendDuration - poseBlendRemaining;
            float u = Mathf.Clamp01(elapsed / poseBlendDuration);
            float t = u * u * u * (u * (u * 6f - 15f) + 10f);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                Transform bone = npcPoseBones[i];
                Vector3 angular = npcBlendAngularVelocities[i];
                Quaternion predicted = angular.sqrMagnitude > .000001f
                    ? Quaternion.AngleAxis(angular.magnitude * elapsed * Mathf.Rad2Deg, angular.normalized) * npcBlendRotations[i]
                    : npcBlendRotations[i];
                bone.localPosition = Vector3.Lerp(npcBlendPositions[i] + npcBlendVelocities[i] * elapsed, bone.localPosition, t);
                bone.localRotation = Quaternion.Slerp(predicted, bone.localRotation, t);
            }
        }

        private void RememberNpcPresentedPose()
        {
            bool advanced = npcPresentedPoseValid && poseClock > npcPresentedClock + .000001f;
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                if (advanced)
                {
                    npcPreviousPositions[i] = npcPresentedPositions[i];
                    npcPreviousRotations[i] = npcPresentedRotations[i];
                }
                npcPresentedPositions[i] = npcPoseBones[i].localPosition;
                npcPresentedRotations[i] = npcPoseBones[i].localRotation;
            }
            if (advanced) npcPreviousClock = npcPresentedClock;
            else if (!npcPresentedPoseValid) npcPreviousClock = poseClock;
            npcPresentedClock = poseClock;
            npcPresentedPoseValid = true;
        }

        private void CancelPoseBlend()
        {
            if (heroPoseBlendOwned && hero != null && (hero.OwnsClip(this) || !hero.IsClipActive))
                hero.CancelRecoveryPoseTransition();
            heroPoseBlendOwned = false;
            poseBlendRemaining = 0f;
        }

        private void ApplyNpcCombatPose()
        {
            bodyMotion?.Apply();
            PresentDamagePose();
            ApplyNpcPoseBlend();
            supportGrip?.Apply();
        }
    }
}
