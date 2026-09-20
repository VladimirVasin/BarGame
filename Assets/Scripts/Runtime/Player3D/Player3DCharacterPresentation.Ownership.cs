using UnityEngine;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object scopedClipOwner;
        private bool scopedClipLocomotion;
        private AvatarMask scopedTorsoMask, scopedFullMask;

        public bool OwnsClip(object owner) => owner != null && ReferenceEquals(scopedClipOwner, owner);

        public bool CanAcquireClip(object owner) => owner != null && isActiveAndEnabled &&
            !ragdollPoseActive && !interactionHandoffLocked && (activeClipBinding == null || OwnsClip(owner));

        /// <summary>Optional actions cannot replace a contextual action or a fall.</summary>
        public bool TryAcquireClip(object owner, string clipName)
        {
            if (!CanAcquireClip(owner)) return false;
            if (!TryBeginClip(clipName)) return false;
            scopedClipOwner = owner;
            return true;
        }

        public bool SampleOwnedClip(object owner, float progress)
        {
            if (!OwnsClip(owner)) return false;
            SampleActiveClip(progress);
            return true;
        }

        /// <summary>Lets a scoped action retain its torso while the shared gait moves its feet.</summary>
        public bool SetOwnedClipLocomotion(object owner, bool enabled)
        {
            if (!OwnsClip(owner)) return false;
            if (scopedClipLocomotion == enabled) return true;
            if (scopedTorsoMask == null)
            {
                Transform root = registry.Animator.transform;
                Transform[] bones = root.GetComponentsInChildren<Transform>(true);
                scopedTorsoMask = new AvatarMask { name = "Owned Action Torso", transformCount = bones.Length };
                scopedFullMask = new AvatarMask { name = "Owned Action Full Body", transformCount = bones.Length };
                for (int i = 0; i < bones.Length; i++)
                {
                    string path = ColdTransformPath(bones[i], root);
                    scopedTorsoMask.SetTransformPath(i, path);
                    scopedTorsoMask.SetTransformActive(i, bones[i].IsChildOf(registry.Anchors.Spine));
                    scopedFullMask.SetTransformPath(i, path);
                    scopedFullMask.SetTransformActive(i, true);
                }
            }
            scopedClipLocomotion = enabled;
            layerMixer.SetLayerMaskFromAvatarMask(1, enabled ? scopedTorsoMask : scopedFullMask);
            layerMixer.SetInputWeight(0, enabled ? 1f : 0f);
            return true;
        }

        private void ClearOwnedClipLocomotion()
        {
            if (!scopedClipLocomotion) return;
            scopedClipLocomotion = false;
            if (layerMixer.IsValid()) layerMixer.SetLayerMaskFromAvatarMask(1, scopedFullMask);
        }

        private void DisposeOwnedClipMasks()
        {
            scopedClipLocomotion = false;
            if (scopedTorsoMask != null) Destroy(scopedTorsoMask);
            if (scopedFullMask != null) Destroy(scopedFullMask);
            scopedTorsoMask = scopedFullMask = null;
        }

        public void ReleaseOwnedClip(object owner)
        {
            if (OwnsClip(owner)) EndClip();
        }
    }
}
