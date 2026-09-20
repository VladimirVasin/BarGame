using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private const float PoseBlendSeconds = .08f;
        private float poseBlendRemaining;
        private int poseBlendAfterFrame;
        private bool heroPoseBlendOwned;
        private Transform[] npcPoseBones;
        private Vector3[] npcBlendPositions;
        private Quaternion[] npcBlendRotations;
        private Vector3[] npcPresentedPositions;
        private Quaternion[] npcPresentedRotations;
        private bool npcPresentedPoseValid;

        private void InitializeNpcPoseBlend()
        {
            npcPoseBones = npc.Animator.GetComponentsInChildren<Transform>(true);
            npcBlendPositions = new Vector3[npcPoseBones.Length];
            npcBlendRotations = new Quaternion[npcPoseBones.Length];
            npcPresentedPositions = new Vector3[npcPoseBones.Length];
            npcPresentedRotations = new Quaternion[npcPoseBones.Length];
            npcPresentedPoseValid = false;
        }

        private void AdvanceVisualClock(float seconds)
        {
            damagePose?.Advance(seconds, State.Health / State.Settings.MaxHealth);
            poseClock += seconds;
            if (poseBlendRemaining > 0f)
            {
                poseBlendRemaining = Mathf.Max(0f, poseBlendRemaining - seconds);
                if (poseBlendRemaining <= 0f) CancelPoseBlend();
            }
            if (reaction == null) return;
            reactionClock += seconds;
            if (reactionClock >= reaction.length) reaction = null;
        }

        private void BeginPoseBlend()
        {
            if (visibleClip == null || Time.frameCount <= poseBlendAfterFrame) return;
            poseBlendRemaining = PoseBlendSeconds;
            if (hero != null)
            {
                // The shared final-pose blend preserves the last visible velocity,
                // and runs after gait/foot planting just like contextual recovery.
                hero.BeginRecoveryPoseTransition(PoseBlendSeconds);
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
                }
            }
        }

        private void ApplyNpcPoseBlend()
        {
            // Runs after damage: blending a final injured source toward an
            // uninjured target and then adding injury would apply it twice.
            if (poseBlendRemaining <= 0f) return;
            float t = Mathf.SmoothStep(0f, 1f, 1f - poseBlendRemaining / PoseBlendSeconds);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                Transform bone = npcPoseBones[i];
                bone.localPosition = Vector3.Lerp(npcBlendPositions[i], bone.localPosition, t);
                bone.localRotation = Quaternion.Slerp(npcBlendRotations[i], bone.localRotation, t);
            }
        }

        private void RememberNpcPresentedPose()
        {
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                npcPresentedPositions[i] = npcPoseBones[i].localPosition;
                npcPresentedRotations[i] = npcPoseBones[i].localRotation;
            }
            npcPresentedPoseValid = true;
        }

        private void CancelPoseBlend()
        {
            if (heroPoseBlendOwned && hero != null && (hero.OwnsClip(this) || !hero.IsClipActive))
                hero.CancelRecoveryPoseTransition();
            heroPoseBlendOwned = false;
            poseBlendRemaining = 0f;
        }
    }
}
