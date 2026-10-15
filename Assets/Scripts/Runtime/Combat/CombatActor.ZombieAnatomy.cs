using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private static readonly string[] ZombieAnatomyBoneNames =
        {
            "spine", "chest", "neck", "head", "clavicle.L", "clavicle.R",
            "forearm.L", "forearm.R", "hand.L", "hand.R"
        };
        private Transform[] zombieAnatomyBones;
        private Vector3[] zombieOriginalPositions, zombieOriginalScales;
        private Vector3[] zombieAnatomyOffsets, zombieAnatomyScaleRatios;
        private bool zombieAnatomyCaptured;

        private void CacheZombieOriginalAnatomy()
        {
            if (!IsZombie || zombieAnatomyBones != null) return;
            int count = ZombieAnatomyBoneNames.Length;
            zombieAnatomyBones = new Transform[count];
            zombieOriginalPositions = new Vector3[count];
            zombieOriginalScales = new Vector3[count];
            zombieAnatomyOffsets = new Vector3[count];
            zombieAnatomyScaleRatios = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Transform bone = CityPedestrianHandProps.FindSocket(DamageRigRoot, ZombieAnatomyBoneNames[i]);
                if (bone == null) throw new InvalidOperationException("Porter anatomy requires " + ZombieAnatomyBoneNames[i]);
                zombieAnatomyBones[i] = bone;
                zombieOriginalPositions[i] = bone.localPosition;
                zombieOriginalScales[i] = bone.localScale;
                Vector3 scale = bone.localScale;
                if (Mathf.Abs(scale.x) < .000001f || Mathf.Abs(scale.y) < .000001f || Mathf.Abs(scale.z) < .000001f)
                    throw new InvalidOperationException("Porter anatomy requires a nonzero bone scale.");
            }
        }

        private void CaptureZombieAuthoredAnatomy()
        {
            if (zombieAnatomyCaptured) return;
            for (int i = 0; i < zombieAnatomyBones.Length; i++)
            {
                Transform bone = zombieAnatomyBones[i];
                zombieAnatomyOffsets[i] = bone.localPosition - zombieOriginalPositions[i];
                Vector3 original = zombieOriginalScales[i], authored = bone.localScale;
                zombieAnatomyScaleRatios[i] = new Vector3(authored.x / original.x,
                    authored.y / original.y, authored.z / original.z);
            }
            zombieAnatomyCaptured = true;
        }

        private bool IsAuthoredZombieClip(AnimationClip clip) =>
            clip == ready || clip == zombieWalk || clip == zombieAttackRight || clip == zombieAttackLeft ||
            clip == hit || clip == zombieLoadAdjust || clip == zombieWatch || clip == zombieDormant || clip == zombieReveal;

        private void ApplyZombieGenericAnatomy()
        {
            if (!IsZombie || !zombieAnatomyCaptured) return;
            // The caller has just evaluated a generic clip or copied its bind-
            // relative sample. Apply once before recovery support/physics blends.
            // Rotations remain entirely owned by that recovery animation.
            for (int i = 0; i < zombieAnatomyBones.Length; i++)
            {
                Transform bone = zombieAnatomyBones[i];
                bone.localPosition += zombieAnatomyOffsets[i];
                // Recovery leaves scales intact; rebuild from the captured
                // original rather than multiplying its previous presentation.
                bone.localScale = Vector3.Scale(zombieOriginalScales[i], zombieAnatomyScaleRatios[i]);
            }
        }
    }
}
