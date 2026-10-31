using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal void ApplyOrdinaryZombieArmProportionsToBindPose()
        {
            if (!IsOrdinaryZombie) return;
            // Match solve_long_arm's authored segment lengths while keeping
            // straight bind rotations as the shared joint-limit reference.
            // The ragdoll owner restores the visible pose after construction.
            float upper = OrdinaryZombieVariant == 2 ? 2.4f : 1.2f;
            float forearm = OrdinaryZombieVariant == 2 ? 2.6f : 1.6f;
            foreach (string side in new[] { ".L", ".R" })
            {
                CityPedestrianHandProps.FindSocket(DamageRigRoot, "forearm" + side).localPosition *= upper;
                CityPedestrianHandProps.FindSocket(DamageRigRoot, "hand" + side).localPosition *= forearm;
            }
        }

        // Fit once, before combat caches these shapes. Live melee uses the
        // authored skin surfaces; physical falls use these same measured bodies.
        private void FitOrdinaryZombieTorsoColliders()
        {
            if (!IsOrdinaryZombie) return;
            SkinnedMeshRenderer source = Array.Find(DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                skin => skin.name == "GEO_Torso");
            if (source == null) throw new InvalidOperationException("The degraded body requires its authored torso surface.");
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bind = mesh.bindposes;
            Transform[] bones = source.bones;
            var world = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) world[i] = bones[i].localToWorldMatrix * bind[i];
            foreach (var entry in Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (entry.Value is not (Player3DAnatomicalPart.Pelvis or Player3DAnatomicalPart.LowerTorso or Player3DAnatomicalPart.Torso)) continue;
                if (entry.Key is not BoxCollider box) throw new InvalidOperationException("Torso physics requires its shared box shape.");
                Transform bone = box.transform.parent;
                Matrix4x4 intoBox = box.transform.worldToLocalMatrix;
                Bounds bounds = default;
                bool measured = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight weight = weights[i];
                    float influence = Influence(weight.boneIndex0, weight.weight0) + Influence(weight.boneIndex1, weight.weight1) +
                        Influence(weight.boneIndex2, weight.weight2) + Influence(weight.boneIndex3, weight.weight3);
                    if (influence < .35f) continue;
                    Vector3 point = world[weight.boneIndex0].MultiplyPoint3x4(vertices[i]) * weight.weight0;
                    if (weight.weight1 > 0f) point += world[weight.boneIndex1].MultiplyPoint3x4(vertices[i]) * weight.weight1;
                    if (weight.weight2 > 0f) point += world[weight.boneIndex2].MultiplyPoint3x4(vertices[i]) * weight.weight2;
                    if (weight.weight3 > 0f) point += world[weight.boneIndex3].MultiplyPoint3x4(vertices[i]) * weight.weight3;
                    point = intoBox.MultiplyPoint3x4(point);
                    if (measured) bounds.Encapsulate(point); else { bounds = new Bounds(point, Vector3.zero); measured = true; }
                    float Influence(int index, float amount) => amount > 0f && bones[index] == bone ? amount : 0f;
                }
                // A split torso mesh may leave one anatomical segment empty;
                // its existing shared proxy remains the correct supported joint.
                if (!measured) continue;
                box.center = bounds.center;
                box.size = Vector3.Max(bounds.size, Vector3.one * .015f);
            }
        }
    }
}
