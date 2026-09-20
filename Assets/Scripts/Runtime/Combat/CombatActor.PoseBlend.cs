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

        private void InitializeNpcPoseBlend()
        {
            npcPoseBones = npc.Animator.GetComponentsInChildren<Transform>(true);
            npcBlendPositions = new Vector3[npcPoseBones.Length];
            npcBlendRotations = new Quaternion[npcPoseBones.Length];
        }

        private void AdvanceVisualClock(float seconds)
        {
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
                for (int i = 0; i < npcPoseBones.Length; i++)
                {
                    npcBlendPositions[i] = npcPoseBones[i].localPosition;
                    npcBlendRotations[i] = npcPoseBones[i].localRotation;
                }
            }
        }

        private void ApplyNpcPoseBlend()
        {
            if (poseBlendRemaining <= 0f) return;
            float t = Mathf.SmoothStep(0f, 1f, 1f - poseBlendRemaining / PoseBlendSeconds);
            for (int i = 0; i < npcPoseBones.Length; i++)
            {
                Transform bone = npcPoseBones[i];
                bone.localPosition = Vector3.Lerp(npcBlendPositions[i], bone.localPosition, t);
                bone.localRotation = Quaternion.Slerp(npcBlendRotations[i], bone.localRotation, t);
            }
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
