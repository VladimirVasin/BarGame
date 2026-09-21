using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object scopedClipOwner;
        private bool scopedClipLocomotion;
        private bool scopedPresentationFrozen;
        private AvatarMask scopedTorsoMask, scopedFullMask;
        private AnimationMixerPlayable scopedClipBlend;
        private AnimationClipPlayable scopedSecondaryClip;
        private string scopedSecondaryClipName;

        public bool OwnsClip(object owner) => owner != null && ReferenceEquals(scopedClipOwner, owner);

        internal void SetOwnedPresentationFrozen(object owner, bool frozen)
        {
            if (OwnsClip(owner)) scopedPresentationFrozen = frozen;
        }

        public bool CanAcquireClip(object owner) => owner != null && isActiveAndEnabled &&
            !ragdollPoseActive && !interactionHandoffLocked && (activeClipBinding == null || OwnsClip(owner));

        /// <summary>Optional actions cannot replace a contextual action or a fall.</summary>
        public bool TryAcquireClip(object owner, string clipName, bool preserveRecoveryPose = false)
        {
            if (!CanAcquireClip(owner)) return false;
            if (preserveRecoveryPose && OwnsClip(owner))
            {
                // A quick release can replace its preparation clip before the
                // entry blend finishes. Keep that same visible transition;
                // restarting or cancelling it would snap to the authored pose.
                CaptureClipSpatialState();
                if (!BeginClip(clipName, ClipOwner.External))
                {
                    ResetClipSpatialOffset();
                    return false;
                }
            }
            else if (!TryBeginClip(clipName)) return false;
            scopedClipOwner = owner;
            return true;
        }

        public bool SampleOwnedClip(object owner, float progress)
        {
            if (!OwnsClip(owner)) return false;
            ClearOwnedClipBlend();
            SampleActiveClip(progress);
            return true;
        }

        /// <summary>One deterministic sample owns both the rendered rig and its weapon-contact anchors.</summary>
        public bool SampleOwnedClipBlend(object owner, string secondaryClipName, float secondaryWeight, float normalizedTime)
        {
            if (!OwnsClip(owner) || !activeClipPlayable.IsValid() ||
                float.IsNaN(secondaryWeight) || float.IsInfinity(secondaryWeight) ||
                float.IsNaN(normalizedTime) || float.IsInfinity(normalizedTime) ||
                !TryResolveAnimation(secondaryClipName, out Player3DAnimationBinding secondary)) return false;
            if (!scopedClipBlend.IsValid() || scopedSecondaryClipName != secondaryClipName)
            {
                // AnimationClip.events allocates an array; validate only when
                // binding the pair, not at every 120 Hz weapon sample.
                if (secondary.Clip.events.Length != 0) return false;
                ClearOwnedClipBlend();
                graph.Disconnect(layerMixer, 1);
                scopedClipBlend = AnimationMixerPlayable.Create(graph, 2);
                scopedSecondaryClip = AnimationClipPlayable.Create(graph, secondary.Clip);
                scopedSecondaryClip.SetApplyFootIK(false);
                scopedSecondaryClip.SetApplyPlayableIK(false);
                scopedSecondaryClip.SetSpeed(0d);
                graph.Connect(activeClipPlayable, 0, scopedClipBlend, 0);
                graph.Connect(scopedSecondaryClip, 0, scopedClipBlend, 1);
                graph.Connect(scopedClipBlend, 0, layerMixer, 1);
                scopedSecondaryClipName = secondaryClipName;
            }
            float weight = Mathf.Clamp01(secondaryWeight);
            scopedClipBlend.SetInputWeight(0, 1f - weight);
            scopedClipBlend.SetInputWeight(1, weight);
            scopedSecondaryClip.SetTime(secondary.Clip.length * Mathf.Clamp01(normalizedTime));
            SampleActiveClip(normalizedTime);
            return true;
        }

        private void ClearOwnedClipBlend()
        {
            if (graph.IsValid() && scopedClipBlend.IsValid())
            {
                graph.Disconnect(layerMixer, 1);
                graph.Disconnect(scopedClipBlend, 0);
                if (scopedSecondaryClip.IsValid()) graph.DestroyPlayable(scopedSecondaryClip);
                graph.DestroyPlayable(scopedClipBlend);
                if (activeClipPlayable.IsValid()) graph.Connect(activeClipPlayable, 0, layerMixer, 1);
            }
            scopedClipBlend = default;
            scopedSecondaryClip = default;
            scopedSecondaryClipName = null;
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
            scopedPresentationFrozen = false;
            ClearOwnedClipBlend();
            if (!scopedClipLocomotion) return;
            scopedClipLocomotion = false;
            if (layerMixer.IsValid()) layerMixer.SetLayerMaskFromAvatarMask(1, scopedFullMask);
        }

        private void DisposeOwnedClipMasks()
        {
            ClearOwnedClipBlend();
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
