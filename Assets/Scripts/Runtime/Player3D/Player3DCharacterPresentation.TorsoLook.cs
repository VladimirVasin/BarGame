using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private PlayerTorsoLookPose torsoLookPose;
        private object torsoLookOwner;
        internal Vector3 TorsoLookAnglesDegrees => torsoLookPose != null ? torsoLookPose.AnglesDegrees : Vector3.zero;

        internal void SetTorsoLookIntent(object owner, Vector3 worldDirection, float neutralPitchDegrees, bool ordinaryLook)
        {
            if (owner == null || torsoLookPose == null ||
                torsoLookOwner != null && !ReferenceEquals(torsoLookOwner, owner)) return;
            if (!CanPresentTorsoLook(owner)) { ClearTorsoLook(owner); return; }
            torsoLookOwner = owner;
            torsoLookPose.SetIntent(worldDirection, neutralPitchDegrees, ordinaryLook);
        }

        internal void AdvanceTorsoLook(object owner, float seconds)
        {
            if (!ReferenceEquals(torsoLookOwner, owner)) return;
            if (scopedPresentationFrozen || GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) return;
            if (!CanPresentTorsoLook(owner)) { ClearTorsoLook(owner); return; }
            torsoLookPose?.Advance(seconds);
        }

        internal void ClearTorsoLook(object owner)
        {
            if (owner == null || !ReferenceEquals(torsoLookOwner, owner)) return;
            ResetTorsoLook();
        }

        // An inspection camera is finalized after the ordinary rig late pass.
        // Reapply just this accepted additive bend, without evaluating the graph twice.
        internal void ReapplyTorsoLook(object owner)
        {
            if (ReferenceEquals(torsoLookOwner, owner)) ApplyTorsoLookPose();
        }

        private bool CanPresentTorsoLook(object owner) => isActiveAndEnabled &&
            owner is CombatActor actor && actor != null && actor.CanPresentTorsoLook &&
            !ragdollPoseActive && !risePose.Active && !interactionHandoffLocked && fallAmount <= .01f &&
            (!IsClipActive || OwnsClip(owner));

        private void ApplyTorsoLookPose()
        {
            if (torsoLookOwner == null) return;
            // Pause disables manual input, making CombatActor.IsAvailable false.
            // Keep the already accepted bend on the frozen graph instead of
            // treating that temporary input lock as a new animation owner.
            if (scopedPresentationFrozen || GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused)
            {
                torsoLookPose?.Apply();
                return;
            }
            if (!CanPresentTorsoLook(torsoLookOwner)) { ResetTorsoLook(); return; }
            torsoLookPose?.Apply();
        }

        private void RestoreTorsoLookPose() => torsoLookPose?.Restore();

        private void ResetTorsoLook()
        {
            // Recovery remembers the whole final target, including this bend.
            // Unwind that composite first so a later recovery cleanup cannot
            // write the old bend over the released torso's authored base.
            if (torsoLookOwner != null) RestoreRecoveryPoseTransition();
            torsoLookPose?.Reset();
            torsoLookOwner = null;
        }

        private void ForgetTorsoLook()
        {
            torsoLookPose?.Forget();
            torsoLookOwner = null;
        }
    }
}
