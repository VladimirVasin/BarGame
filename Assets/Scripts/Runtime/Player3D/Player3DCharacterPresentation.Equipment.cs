using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BarPromenade
{
    public sealed partial class Player3DCharacterPresentation
    {
        private const int EquipmentClipCacheCapacity = 6;
        private readonly AnimationClip[] equipmentClips = new AnimationClip[EquipmentClipCacheCapacity];
        private readonly AnimationClipPlayable[] equipmentPlayables = new AnimationClipPlayable[EquipmentClipCacheCapacity];
        private object equipmentPoseOwner;
        private AnimationLayerMixerPlayable equipmentLayers;
        private AvatarMask equipmentMask, equipmentRightArmMask;
        private int equipmentClipIndex = -1, equipmentCacheCursor;
        private float equipmentTime, equipmentWeight;
        private bool equipmentRightArmOnly;
        private bool EquipmentPosePhysicsAllowed => !ragdollPoseActive && !risePose.Active && fallAmount <= .001f;

        public bool OwnsEquipmentPose(object owner) => owner != null && ReferenceEquals(equipmentPoseOwner, owner);

        /// <summary>Layers equipment above the current pose without acquiring its root, pelvis, legs or clock.</summary>
        public bool TryAcquireEquipmentPose(object owner, string clipName, bool rightArmOnly = false)
        {
            if (equipmentPoseOwner is Object previousOwner && previousOwner == null) ClearEquipmentPose();
            if (owner == null || owner is Object unityOwner && unityOwner == null ||
                !isActiveAndEnabled || !graph.IsValid() || !EquipmentPosePhysicsAllowed || registry == null ||
                (equipmentPoseOwner != null && !ReferenceEquals(equipmentPoseOwner, owner)) ||
                !TryResolveAnimation(clipName, out Player3DAnimationBinding binding)) return false;

            int index = -1;
            for (int i = 0; i < equipmentClips.Length; i++)
                if (equipmentClips[i] == binding.Clip) { index = i; break; }
            if (index < 0)
            {
                // Events allocate their array only on a new binding, never on a sip sample.
                if (binding.Clip.events.Length != 0) return false;
                index = equipmentCacheCursor;
                if (index == equipmentClipIndex) index = (index + 1) % EquipmentClipCacheCapacity;
                equipmentCacheCursor = (index + 1) % EquipmentClipCacheCapacity;
                if (equipmentPlayables[index].IsValid()) graph.DestroyPlayable(equipmentPlayables[index]);
                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, binding.Clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(0d);
                equipmentClips[index] = binding.Clip;
                equipmentPlayables[index] = playable;
            }

            EnsureEquipmentGraph();
            if (equipmentRightArmOnly != rightArmOnly)
            {
                equipmentRightArmOnly = rightArmOnly;
                equipmentLayers.SetLayerMaskFromAvatarMask(1, rightArmOnly ? equipmentRightArmMask : equipmentMask);
            }
            if (equipmentClipIndex != index)
            {
                if (equipmentLayers.GetInput(1).IsValid()) graph.Disconnect(equipmentLayers, 1);
                graph.Connect(equipmentPlayables[index], 0, equipmentLayers, 1);
                equipmentClipIndex = index;
            }
            equipmentPoseOwner = owner;
            equipmentTime = 0f;
            equipmentWeight = 1f;
            EvaluateGraph(0f);
            return true;
        }

        public bool SampleEquipmentPose(object owner, float elapsedSeconds, float weight = 1f)
        {
            if (!OwnsEquipmentPose(owner) || !isActiveAndEnabled || !graph.IsValid() || !EquipmentPosePhysicsAllowed ||
                equipmentClipIndex < 0 || !equipmentPlayables[equipmentClipIndex].IsValid() ||
                float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) ||
                float.IsNaN(weight) || float.IsInfinity(weight)) return false;
            equipmentTime = Mathf.Max(0f, elapsedSeconds);
            equipmentWeight = Mathf.Clamp01(weight);
            EvaluateGraph(0f);
            return true;
        }

        public void ReleaseEquipmentPose(object owner)
        {
            if (!OwnsEquipmentPose(owner)) return;
            ClearEquipmentPose();
            EvaluateGraph(0f);
        }

        private void EnsureEquipmentGraph()
        {
            if (equipmentLayers.IsValid()) return;
            equipmentLayers = AnimationLayerMixerPlayable.Create(graph, 2);
            AnimationPlayableOutput output = (AnimationPlayableOutput)graph.GetOutput(0);
            output.SetSourcePlayable(equipmentLayers);
            graph.Connect(layerMixer, 0, equipmentLayers, 0);
            equipmentLayers.SetInputWeight(0, 1f);
            equipmentLayers.SetInputWeight(1, 0f);
            equipmentLayers.SetLayerAdditive(1, false);
            Transform root = registry.Animator.transform;
            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            Transform rightClavicle = GetPartBone(Player3DAnatomicalPart.RightUpperArm).parent;
            equipmentMask = new AvatarMask { name = "Hero Owned Equipment Torso", transformCount = bones.Length };
            equipmentRightArmMask = new AvatarMask { name = "Hero Owned Equipment Right Arm", transformCount = bones.Length };
            for (int i = 0; i < bones.Length; i++)
            {
                string path = ColdTransformPath(bones[i], root);
                equipmentMask.SetTransformPath(i, path);
                equipmentMask.SetTransformActive(i, bones[i].IsChildOf(registry.Anchors.Spine));
                equipmentRightArmMask.SetTransformPath(i, path);
                equipmentRightArmMask.SetTransformActive(i, bones[i].IsChildOf(rightClavicle));
            }
            equipmentLayers.SetLayerMaskFromAvatarMask(1, equipmentMask);
        }

        private void SampleEquipmentPose()
        {
            if (!equipmentLayers.IsValid()) return;
            if (!EquipmentPosePhysicsAllowed || equipmentPoseOwner is Object unityOwner && unityOwner == null) ClearEquipmentPose();
            equipmentLayers.SetInputWeight(1, equipmentPoseOwner == null ? 0f : equipmentWeight);
            if (equipmentClipIndex < 0) return;
            AnimationClip clip = equipmentClips[equipmentClipIndex];
            float sampleTime = clip.isLooping && clip.length > 0f
                ? Mathf.Repeat(equipmentTime, clip.length)
                : Mathf.Min(equipmentTime, clip.length);
            equipmentPlayables[equipmentClipIndex].SetTime(sampleTime);
        }

        private void ClearEquipmentPose()
        {
            equipmentPoseOwner = null;
            equipmentTime = equipmentWeight = 0f;
            if (equipmentLayers.IsValid()) equipmentLayers.SetInputWeight(1, 0f);
        }

        private void DisposeEquipmentGraph()
        {
            ClearEquipmentPose();
            if (equipmentMask != null) Destroy(equipmentMask);
            if (equipmentRightArmMask != null) Destroy(equipmentRightArmMask);
            equipmentMask = equipmentRightArmMask = null;
            equipmentRightArmOnly = false;
            equipmentLayers = default;
            equipmentClipIndex = -1;
            equipmentCacheCursor = 0;
            for (int i = 0; i < equipmentClips.Length; i++)
            {
                equipmentClips[i] = null;
                equipmentPlayables[i] = default;
            }
        }
    }
}
