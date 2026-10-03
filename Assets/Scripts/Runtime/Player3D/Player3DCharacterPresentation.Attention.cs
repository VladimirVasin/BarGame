using UnityEngine;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object ownedAttentionOwner;
        private Vector3? ownedAttentionFocus;

        // Only the owner of the current full-body clip may keep the shared
        // attention layer active. Ordinary world scans retain their own focus.
        private bool HasOwnedAttention => ownedAttentionOwner != null && OwnsClip(ownedAttentionOwner);

        // A zero-time graph rebuild still restores and rewrites additive bones.
        // Keep the owner's already-final pose intact during a contact freeze or
        // pause; physics and authored rises retain their separate presentation.
        private bool OwnedAttentionPresentationFrozen => HasOwnedAttention && !ragdollPoseActive &&
            !risePose.Active && (scopedPresentationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused);

        internal void SetOwnedAttentionFocus(object owner, Vector3? focus)
        {
            if (owner == null || (ownedAttentionOwner != null && !ReferenceEquals(ownedAttentionOwner, owner))) return;
            ownedAttentionOwner = owner;
            ownedAttentionFocus = focus;
        }

        internal void ClearOwnedAttentionFocus(object owner)
        {
            if (!ReferenceEquals(ownedAttentionOwner, owner)) return;
            if (!ragdollPoseActive) RestoreAttentionPoseBase();
            else attentionBaseCaptured = false;
            ownedAttentionOwner = null;
            ownedAttentionFocus = null;
            attentionWeight = attentionYaw = attentionPitch = attentionYawVelocity = attentionPitchVelocity = 0f;
        }
    }
}
