using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private object carryPoseOwner;
        private AnimationLayerMixerPlayable carryLayers;
        private AnimationClipPlayable carryPlayable;
        private AvatarMask carryMask;
        private float carryTime, carryWeight;
        public bool HasCarryPose => carryPoseOwner != null;

        /// <summary>Owns only the torso above ordinary gait; full-body actions still take precedence.</summary>
        public bool TryAcquireCarryPose(object owner, string clipName)
        {
            if (owner == null || !isActiveAndEnabled || !graph.IsValid() || registry == null ||
                (carryPoseOwner != null && !ReferenceEquals(carryPoseOwner, owner)) ||
                !TryResolveAnimation(clipName, out Player3DAnimationBinding binding) ||
                binding.Clip == null || !binding.Clip.isLooping || binding.Clip.events.Length != 0) return false;
            if (!carryLayers.IsValid())
            {
                Playable ordinary = layerMixer.GetInput(0);
                graph.Disconnect(layerMixer, 0);
                carryLayers = AnimationLayerMixerPlayable.Create(graph, 2);
                graph.Connect(ordinary, 0, carryLayers, 0);
                graph.Connect(carryLayers, 0, layerMixer, 0);
                carryLayers.SetInputWeight(0, 1f);
                Transform animatorRoot = registry.Animator.transform;
                Transform[] bones = animatorRoot.GetComponentsInChildren<Transform>(true);
                carryMask = new AvatarMask { name = "Hero Owned Carry Torso", transformCount = bones.Length };
                for (int i = 0; i < bones.Length; i++)
                {
                    carryMask.SetTransformPath(i, ColdTransformPath(bones[i], animatorRoot));
                    carryMask.SetTransformActive(i, bones[i].IsChildOf(registry.Anchors.Spine));
                }
                carryLayers.SetLayerMaskFromAvatarMask(1, carryMask);
            }
            if (carryPlayable.IsValid())
            {
                graph.Disconnect(carryLayers, 1);
                graph.DestroyPlayable(carryPlayable);
            }
            carryPlayable = AnimationClipPlayable.Create(graph, binding.Clip);
            carryPlayable.SetApplyFootIK(false); carryPlayable.SetApplyPlayableIK(false); carryPlayable.SetSpeed(0d);
            graph.Connect(carryPlayable, 0, carryLayers, 1);
            carryPoseOwner = owner; carryTime = 0f; carryWeight = 1f;
            EvaluateGraph(0f);
            return true;
        }

        public bool UpdateCarryPose(object owner, float elapsedSeconds, float weight = 1f)
        {
            if (!ReferenceEquals(carryPoseOwner, owner) || owner == null || !carryPlayable.IsValid() ||
                float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) ||
                float.IsNaN(weight) || float.IsInfinity(weight)) return false;
            carryTime = Mathf.Max(0f, elapsedSeconds); carryWeight = Mathf.Clamp01(weight);
            EvaluateGraph(0f);
            return true;
        }

        public void ReleaseCarryPose(object owner)
        {
            if (owner == null || !ReferenceEquals(carryPoseOwner, owner)) return;
            ClearCarryPose();
            EvaluateGraph(0f);
        }

        private void InsertOrdinaryPresentation(Playable inserted)
        {
            // Each playable output has one destination. Free the current
            // source before connecting the inserted layer, in either order.
            Playable downstream = layerMixer;
            if (carryLayers.IsValid()) downstream = carryLayers;
            Playable ordinary = downstream.GetInput(0);
            graph.Disconnect(downstream, 0);
            graph.Connect(ordinary, 0, inserted, 0);
            graph.Connect(inserted, 0, downstream, 0);
        }

        private void SampleCarryPose()
        {
            if (!carryLayers.IsValid()) return;
            if (carryPoseOwner is UnityEngine.Object unityOwner && unityOwner == null) ClearCarryPose();
            carryLayers.SetInputWeight(1, carryPoseOwner == null ? 0f : carryWeight);
            if (carryPlayable.IsValid()) carryPlayable.SetTime(carryTime);
        }

        private void ClearCarryPose()
        {
            carryPoseOwner = null; carryTime = carryWeight = 0f;
            if (carryLayers.IsValid()) carryLayers.SetInputWeight(1, 0f);
        }

        private void DisposeCarryGraph()
        {
            ClearCarryPose();
            if (carryMask != null) Destroy(carryMask);
            carryMask = null; carryLayers = default; carryPlayable = default;
        }
    }
}
