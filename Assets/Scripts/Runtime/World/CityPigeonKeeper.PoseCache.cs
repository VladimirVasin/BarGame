using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPigeonKeeper
    {
        private Transform[] heldPoseBones;
        private Vector3[] heldPosePositions, heldPoseScales;
        private Quaternion[] heldPoseRotations;
        private float heldLeftGrip, heldRightGrip;
        private bool heldPoseValid;

        private void InitializeStablePoseCache()
        {
            var found = new HashSet<Transform>();
            var bones = new List<Transform>(40);
            foreach (var renderer in Worker.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Transform weightedBone in renderer.bones)
                    for (Transform bone = weightedBone; bone && bone != Worker.ModelRoot && bone != Worker.transform; bone = bone.parent)
                        if (!bone.name.StartsWith("face.", StringComparison.Ordinal) && found.Add(bone)) bones.Add(bone);
            heldPoseBones = bones.ToArray();
            heldPosePositions = new Vector3[heldPoseBones.Length];
            heldPoseRotations = new Quaternion[heldPoseBones.Length];
            heldPoseScales = new Vector3[heldPoseBones.Length];
        }

        private void CaptureStablePose()
        {
            heldPoseValid = false;
            if (!present || insideHidden || !Worker || !Worker.IsInitialized || heldPoseBones == null) return;
            for (int index = 0; index < heldPoseBones.Length; index++)
            {
                Transform bone = heldPoseBones[index];
                if (!bone) return;
                heldPosePositions[index] = bone.localPosition;
                heldPoseRotations[index] = bone.localRotation;
                heldPoseScales[index] = bone.localScale;
            }
            heldLeftGrip = fingers.LeftGripWeight;
            heldRightGrip = fingers.RightGripWeight;
            heldPoseValid = true;
        }

        private void ReapplyStablePose()
        {
            if (!heldPoseValid) return;
            if (!present || insideHidden || !Worker || !Worker.IsInitialized)
            {
                heldPoseValid = false;
                return;
            }
            // A live Animator can restore its last sampled stream on frames
            // where the bounded action sampler does not run. Preserve the
            // finished body/IK pose after that write, without evaluating clips
            // again. Face articulation retains its independent speech owner.
            for (int index = 0; index < heldPoseBones.Length; index++)
            {
                Transform bone = heldPoseBones[index];
                if (!bone) { heldPoseValid = false; return; }
                bone.SetLocalPositionAndRotation(heldPosePositions[index], heldPoseRotations[index]);
                if (bone.localScale != heldPoseScales[index]) bone.localScale = heldPoseScales[index];
            }
            fingers.SetGrip(true, heldLeftGrip);
            fingers.SetGrip(false, heldRightGrip);
        }
    }
}
