using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Samples the original Generic clip paths for an upper-body action on a default NPC.</summary>
    internal sealed class NpcHeroUpperBodyPose
    {
        private static readonly string[] BoneNames = { "spine", "chest", "neck", "head",
            "clavicle.R", "upper_arm.R", "forearm.R", "hand.R", "clavicle.L", "upper_arm.L", "forearm.L", "hand.L",
            CityPedestrianHandProps.CigaretteRightSocketName };
        private static readonly int CigaretteIndex = BoneNames.Length - 1;

        internal sealed class Binding
        {
            private readonly Transform[] targets = new Transform[BoneNames.Length];
            private readonly Quaternion[] basePose = new Quaternion[BoneNames.Length];
            private readonly Quaternion[] writtenPose = new Quaternion[BoneNames.Length];
            private Vector3 cigaretteBase, cigaretteWritten;
            private bool wroteCigarette;
            private bool hasPose;

            internal Binding(Transform model)
            {
                for (int i = 0; i < targets.Length; i++)
                    targets[i] = CityPedestrianHandProps.FindSocket(model, BoneNames[i]);
            }

            internal void Restore()
            {
                if (!hasPose) return;
                // A distant presentation can skip its graph evaluation this frame.
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i] && targets[i].localRotation.Equals(writtenPose[i]))
                        targets[i].localRotation = basePose[i];
                Transform cigarette = targets[CigaretteIndex];
                if (wroteCigarette && cigarette && cigarette.localPosition.Equals(cigaretteWritten))
                    cigarette.localPosition = cigaretteBase;
                wroteCigarette = false;
                hasPose = false;
            }

            internal void Apply(Transform[] source, Quaternion[] start, float weight)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    Transform target = targets[i];
                    if (!source[i] || !target) continue;
                    Quaternion sampled = source[i].localRotation;
                    basePose[i] = target.localRotation;
                    if (start != null) sampled = basePose[i] * start[i] * sampled;
                    target.localRotation = Quaternion.Slerp(basePose[i], sampled, weight);
                    writtenPose[i] = target.localRotation;
                }
                Transform cigarette = targets[CigaretteIndex];
                if (start == null && cigarette && source[CigaretteIndex])
                {
                    cigaretteBase = cigarette.localPosition;
                    cigarette.localPosition = Vector3.Lerp(cigaretteBase, source[CigaretteIndex].localPosition, weight);
                    cigaretteWritten = cigarette.localPosition;
                    wroteCigarette = true;
                }
                hasPose = true;
            }

            // Contacts can finish a sampled action after the clip writes its
            // rotations. Restore those writes too when locomotion skips a frame.
            internal void IncludeContactCorrections()
            {
                if (!hasPose) return;
                for (int i = 0; i < targets.Length; i++)
                    if (targets[i]) writtenPose[i] = targets[i].localRotation;
            }
        }

        private readonly GameObject donor;
        private readonly Transform[] bones = new Transform[BoneNames.Length];
        private readonly Pose[] rest = new Pose[BoneNames.Length];
        private readonly Dictionary<AnimationClip, Quaternion[]> entry = new();

        internal NpcHeroUpperBodyPose(Transform parent)
        {
            Player3DAssetRegistry registry = Player3DResources.LoadPrefab().GetComponent<Player3DAssetRegistry>();
            Transform source = registry.Animator.transform;
            donor = new GameObject("NPC action pose source");
            donor.transform.SetParent(parent, false);
            var paths = new Dictionary<Transform, Transform> { { source, donor.transform } };
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = CityPedestrianHandProps.FindSocket(registry.ModelRoot, BoneNames[i]);
                if (!bone) continue;
                bones[i] = CopyPath(bone);
                rest[i] = new Pose(bone.localPosition, bone.localRotation);
            }

            Transform CopyPath(Transform part)
            {
                if (!part) throw new InvalidOperationException("The hero pose bones must descend from the animator.");
                if (paths.TryGetValue(part, out Transform copy)) return copy;
                Transform above = CopyPath(part.parent);
                copy = new GameObject(part.name).transform;
                copy.SetParent(above, false);
                copy.SetLocalPositionAndRotation(part.localPosition, part.localRotation);
                copy.localScale = part.localScale;
                paths.Add(part, copy);
                return copy;
            }
        }

        internal void Apply(Binding binding, AnimationClip clip, float time, float weight, bool additive = true)
        {
            binding.Restore();
            if (!clip || weight <= 0f) return;
            Quaternion[] start = null;
            if (additive && !entry.TryGetValue(clip, out start))
            {
                ResetDonor();
                clip.SampleAnimation(donor, 0f);
                start = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i]) start[i] = Quaternion.Inverse(bones[i].localRotation);
                entry.Add(clip, start);
            }
            ResetDonor();
            clip.SampleAnimation(donor, Mathf.Clamp(time, 0f, clip.length));
            binding.Apply(bones, start, weight);
        }

        private void ResetDonor()
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i]) bones[i].SetLocalPositionAndRotation(rest[i].position, rest[i].rotation);
        }
    }
}
