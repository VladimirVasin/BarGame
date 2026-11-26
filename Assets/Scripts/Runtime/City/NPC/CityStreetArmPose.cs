using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Street gestures layer over the evaluated walking pose, including between distant animation ticks.</summary>
    internal sealed class CityStreetArmPose
    {
        internal const float WaveDuration = 1.05f;
        private readonly Transform root, head, neck, wrist, elbow, shoulder;
        private readonly AnatomicalHandFrame palm;
        private readonly NpcWardrobe wardrobe;
        private Vector3 hatPoint;
        private bool hasHat, hasCollar, clothingMeasured;
        private Quaternion shoulderBase, elbowBase, wristBase, neckBase;
        private bool applied, neckMoved;

        internal CityStreetArmPose(CityPedestrianAssetRegistry registry)
        {
            root = registry.transform;
            head = registry.HeadAnchor;
            neck = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "neck");
            var hands = registry.GetComponentInChildren<NpcHandPose>(true);
            wardrobe = hands.GetComponent<NpcWardrobe>();
            palm = hands.GetAnatomicalFrame(false);
            wrist = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "hand.R");
            elbow = wrist.parent;
            shoulder = elbow.parent;
        }

        internal void Wave(Vector3 partner, float seconds)
        {
            Restore();
            if (seconds <= 0f || seconds >= WaveDuration) return;
            Capture();
            Vector3 face = Vector3.ProjectOnPlane(partner - head.position, root.up).normalized;
            face = Vector3.RotateTowards(root.forward, face, 65f * Mathf.Deg2Rad, 0f);
            float swing = Mathf.Sin((seconds - .27f) * 14f);
            float reach = LimbTwoBoneIk.ChainLength(shoulder, elbow, wrist);
            Vector3 centre = head.position + reach * (root.right * (.57f + .10f * swing) +
                root.up * .29f + root.forward * .35f);
            Vector3 fingers = Quaternion.AngleAxis(16f * swing, face) * root.up;
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Min(seconds / .27f, (WaveDuration - seconds) / .30f));
            PlacePalm(centre, face, fingers, reach, weight);
        }

        internal void Brace(Vector3 wind, float weight)
        {
            Restore();
            if (weight <= 0f) return;
            if (!clothingMeasured) MeasureClothing();
            Capture();
            neckBase = neck.localRotation;
            neckMoved = true;
            // Turn downwind from the walking direction, with a small chin tuck.
            float side = Vector3.Dot(wind, root.right);
            neck.rotation = Quaternion.AngleAxis(20f * side * weight, root.up) *
                Quaternion.AngleAxis((hasHat ? 8f : 16f) * weight, root.right) * neck.rotation;
            float reach = LimbTwoBoneIk.ChainLength(shoulder, elbow, wrist);
            if (hasHat)
                PlacePalm(head.TransformPoint(hatPoint), -root.up - root.right * .65f,
                    -root.right + root.forward * .35f, reach, weight);
            else if (hasCollar)
                PlacePalm(neck.position + (root.forward * .17f + root.right * .09f) * reach,
                    -root.forward, root.up + root.right * .2f, reach, weight);
        }

        private void MeasureClothing()
        {
            clothingMeasured = true;
            if (!wardrobe) return;
            hasCollar = wardrobe.GetEquippedItem("outerwear") != null || wardrobe.GetEquippedItem("scarf") != null;
            if (wardrobe.GetEquippedItem("headwear") == null) return;
            // The caps are rigidly skinned to the head. Cache a contact on the worn shell in
            // head space, so it follows this wearer's head turns, size and walking bob.
            var vertices = new List<Vector3>();
            Vector3 outward = (root.right + root.up * 1.8f + root.forward * .5f).normalized;
            float best = float.NegativeInfinity;
            foreach (var garment in wardrobe.Garments)
            {
                if (garment.Slot != "headwear" || garment.Renderer is not SkinnedMeshRenderer skin ||
                    !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                int bone = Array.IndexOf(skin.bones, head);
                if (bone < 0) continue;
                Mesh mesh = skin.sharedMesh;
                Matrix4x4 bind = mesh.bindposes[bone];
                mesh.GetVertices(vertices);
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = bind.MultiplyPoint3x4(vertex);
                    float score = Vector3.Dot(head.TransformVector(point), outward);
                    if (score <= best) continue;
                    best = score;
                    hatPoint = point;
                    hasHat = true;
                }
            }
        }

        private void Capture()
        {
            shoulderBase = shoulder.localRotation;
            elbowBase = elbow.localRotation;
            wristBase = wrist.localRotation;
            applied = true;
        }

        private void PlacePalm(Vector3 centre, Vector3 normal, Vector3 fingers, float reach, float weight)
        {
            Quaternion rotation = palm.ResolveHandRotation(normal, fingers);
            LimbTwoBoneIk.Solve(shoulder, elbow, wrist, palm.ResolveHandPosition(centre, rotation), rotation,
                shoulder.position + root.right * reach - root.up * (.45f * reach), weight, .98f, true);
        }

        internal void Restore()
        {
            if (!applied) return;
            shoulder.localRotation = shoulderBase;
            elbow.localRotation = elbowBase;
            wrist.localRotation = wristBase;
            if (neckMoved) neck.localRotation = neckBase;
            applied = neckMoved = false;
        }
    }
}
