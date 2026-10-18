using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal float PresentationScale => IsRoom ? CombatRoomAssetProvider.PresentationScale : 1f;
        internal float AdditionalArmReach { get; private set; }
        private Transform[] roomArmEndpoints;
        private Vector3[] roomCanonicalArmPositions;
        private Vector3[] roomCanonicalHandScales;
        private Vector3[] roomHandTips;
        private Vector3 roomHandTipFrom, roomHandTipTo;

        private void InitializeRoomArmProportions()
        {
            if (!IsRoom) return;
            string[] names = { "forearm.L", "forearm.R", "hand.L", "hand.R" };
            roomArmEndpoints = new Transform[names.Length];
            roomCanonicalArmPositions = new Vector3[names.Length];
            roomCanonicalHandScales = new Vector3[2];
            for (int i = 0; i < names.Length; i++)
            {
                Transform bone = CityPedestrianHandProps.FindSocket(DamageRigRoot, names[i]);
                if (bone == null) throw new InvalidOperationException("Room requires its retained arm chain: " + names[i]);
                roomArmEndpoints[i] = bone;
                roomCanonicalArmPositions[i] = bone.localPosition;
                if (i >= 2) roomCanonicalHandScales[i - 2] = bone.localScale;
            }
            for (int side = 0; side < 2; side++)
            {
                Transform elbow = roomArmEndpoints[side], hand = roomArmEndpoints[side + 2];
                float extra = Vector3.Distance(elbow.parent.position, elbow.position) * (CombatRoomAssetProvider.UpperArmLengthScale - 1f) +
                    Vector3.Distance(hand.parent.position, hand.position) * (CombatRoomAssetProvider.ForearmLengthScale - 1f);
                AdditionalArmReach = Mathf.Max(AdditionalArmReach, extra);
            }
            ApplyRoomArmProportionsToFreshPose();
        }

        private void RestoreRoomArmSampleBase()
        {
            if (roomArmEndpoints == null) return;
            // Generic clips can omit constant translations. Each sample starts
            // from canonical lengths, including when the same clip is retained.
            for (int i = 0; i < roomArmEndpoints.Length; i++)
                roomArmEndpoints[i].localPosition = roomCanonicalArmPositions[i];
            RestoreRoomHandBindScales();
        }

        internal void RestoreRoomHandBindScales()
        {
            if (roomCanonicalHandScales == null) return;
            for (int side = 0; side < 2; side++) roomArmEndpoints[side + 2].localScale = roomCanonicalHandScales[side];
        }

        internal void ApplyRoomArmProportionsToFreshPose()
        {
            if (roomArmEndpoints == null) return;
            // Called once after a fresh clip sample or restored skin bind pose.
            // Endpoint-linear sleeve weights carry the mesh to the real hands.
            for (int i = 0; i < roomArmEndpoints.Length; i++)
                roomArmEndpoints[i].localPosition *= i < 2
                    ? CombatRoomAssetProvider.UpperArmLengthScale : CombatRoomAssetProvider.ForearmLengthScale;
            for (int side = 0; side < 2; side++)
            {
                Vector3 scale = roomCanonicalHandScales[side];
                scale[CombatRoomAssetProvider.HandLengthAxis] *= CombatRoomAssetProvider.HandLengthScale;
                roomArmEndpoints[side + 2].localScale = scale;
            }
        }

        private void InitializeRoomHandContacts()
        {
            if (!IsRoom) return;
            roomHandTips = new Vector3[2];
            SkinnedMeshRenderer[] skins = DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            float extraHandReach = 0f;
            for (int side = 0; side < 2; side++)
            {
                Transform hand = roomArmEndpoints[side + 2];
                Matrix4x4 toHand = hand.worldToLocalMatrix;
                Bounds bounds = default; bool measured = false;
                string suffix = side == 0 ? ".L" : ".R";
                foreach (SkinnedMeshRenderer skin in skins)
                {
                    if (!skin.name.StartsWith("GEO_Hand", StringComparison.Ordinal) || !skin.name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                    Mesh mesh = skin.sharedMesh;
                    Vector3[] points = mesh.vertices; BoneWeight[] weights = mesh.boneWeights;
                    Matrix4x4[] bind = mesh.bindposes; Transform[] bones = skin.bones;
                    var posed = new Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++) posed[i] = toHand * bones[i].localToWorldMatrix * bind[i];
                    for (int i = 0; i < points.Length; i++)
                    {
                        BoneWeight weight = weights[i]; Vector3 vertex = points[i];
                        Vector3 point = posed[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0;
                        if (weight.weight1 > 0f) point += posed[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
                        if (weight.weight2 > 0f) point += posed[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
                        if (weight.weight3 > 0f) point += posed[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
                        if (!measured) { bounds = new Bounds(point, Vector3.zero); measured = true; }
                        else bounds.Encapsulate(point);
                    }
                }
                if (!measured) throw new InvalidOperationException("Room requires its actual palm and finger source surfaces.");
                Vector3 tip = bounds.center; tip[CombatRoomAssetProvider.HandLengthAxis] = bounds.max[CombatRoomAssetProvider.HandLengthAxis];
                roomHandTips[side] = tip;
                Vector3 axis = Vector3.zero; axis[CombatRoomAssetProvider.HandLengthAxis] = 1f;
                float nativeAxisScale = hand.TransformVector(axis).magnitude / CombatRoomAssetProvider.HandLengthScale;
                extraHandReach = Mathf.Max(extraHandReach, Mathf.Max(0f, tip[CombatRoomAssetProvider.HandLengthAxis]) * nativeAxisScale *
                    (CombatRoomAssetProvider.HandLengthScale - 1f));
            }
            AdditionalArmReach += extraHandReach;
        }

        internal Vector3 RoomHandTipWorld(bool left) => roomArmEndpoints[(left ? 0 : 1) + 2].TransformPoint(roomHandTips[left ? 0 : 1]);

        internal Vector3 RoomDistalHandContactPoint(bool left)
        {
            Transform hand = roomArmEndpoints[(left ? 0 : 1) + 2];
            Vector3 axis = Vector3.zero; axis[CombatRoomAssetProvider.HandLengthAxis] = 1f;
            return RoomHandTipWorld(left) - hand.TransformVector(axis).normalized * ZombiePalmRadius;
        }

        private void FitRoomHeadCollider()
        {
            if (!IsRoom) return;
            CapsuleCollider capsule = null;
            foreach (var shape in Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head) { capsule = shape.Key as CapsuleCollider; break; }
            if (capsule == null) throw new InvalidOperationException("Room requires its retained head capsule.");
            SkinnedMeshRenderer source = Array.Find(DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                skin => skin.name == "GEO_Head");
            if (source == null) throw new InvalidOperationException("Room requires its authored head surface.");
            Mesh mesh = source.sharedMesh;
            Vector3[] points = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bind = mesh.bindposes;
            Transform[] bones = source.bones;
            var posed = new Matrix4x4[bones.Length];
            Matrix4x4 toCollider = capsule.transform.worldToLocalMatrix;
            for (int i = 0; i < bones.Length; i++) posed[i] = toCollider * bones[i].localToWorldMatrix * bind[i];
            Bounds bounds = default;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 vertex = points[i]; BoneWeight weight = weights[i];
                Vector3 point = posed[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0;
                if (weight.weight1 > 0f) point += posed[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
                if (weight.weight2 > 0f) point += posed[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
                if (weight.weight3 > 0f) point += posed[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
                points[i] = point;
                if (i == 0) bounds = new Bounds(point, Vector3.zero); else bounds.Encapsulate(point);
            }
            int axis = capsule.direction;
            float radius = 0f;
            foreach (Vector3 point in points)
            {
                Vector3 offset = point - bounds.center; offset[axis] = 0f;
                radius = Mathf.Max(radius, offset.magnitude);
            }
            radius += .005f;
            float halfLine = 0f;
            foreach (Vector3 point in points)
            {
                Vector3 offset = point - bounds.center;
                float along = Mathf.Abs(offset[axis]); offset[axis] = 0f;
                halfLine = Mathf.Max(halfLine, along - Mathf.Sqrt(Mathf.Max(0f, radius * radius - offset.sqrMagnitude)));
            }
            capsule.center = bounds.center; capsule.radius = radius; capsule.height = 2f * (halfLine + radius);
        }
    }
}
