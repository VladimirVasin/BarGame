using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Pose in anatomical metres and axes; the FBX retains its authored unit wrapper.</summary>
    internal sealed class CityLoftPigeonRig
    {
        private readonly struct Joint
        {
            private readonly Transform transform;
            private readonly Quaternion rest, toParent, fromParent;
            private readonly Matrix4x4 translation;
            private readonly Vector3 origin;

            public Joint(Transform part, Transform host)
            {
                transform = part;
                origin = part.localPosition;
                rest = part.localRotation;
                toParent = Quaternion.Inverse(part.parent.rotation) * host.rotation;
                fromParent = Quaternion.Inverse(toParent);
                translation = part.parent.worldToLocalMatrix * host.localToWorldMatrix;
            }

            public void Rotate(Quaternion pose) => transform.localRotation = toParent * pose * fromParent * rest;
            public void Shift(Vector3 metres) => transform.localPosition = origin + translation.MultiplyVector(metres);
        }

        private readonly Joint body, head, leftWing, rightWing, leftFoot, rightFoot, leftTail, rightTail;
        private readonly Transform headTransform;
        private readonly Vector3 localBillTip;
        public Vector3 BillPosition => headTransform.TransformPoint(localBillTip);

        public CityLoftPigeonRig(Transform host, GameObject template, Material plumage, int variant)
        {
            GameObject model = UnityEngine.Object.Instantiate(template, host, false);
            var joints = new Dictionary<string, Transform>(8);
            // Each bird has one fixed row. All eight sections and all six birds use one material.
            var row = new MaterialPropertyBlock();
            row.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, variant / 8f));
            foreach (MeshRenderer part in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                part.sharedMaterial = plumage;
                part.SetPropertyBlock(row);
                part.shadowCastingMode = ShadowCastingMode.On;
                part.reflectionProbeUsage = ReflectionProbeUsage.Off;
                joints.Add(part.name, part.transform);
            }
            if (joints.Count != 8) throw new InvalidOperationException("Articulated pigeon requires eight mesh joints.");
            body = new Joint(joints["Breast"], host);
            head = new Joint(joints["NeckHead"], host);
            headTransform = joints["NeckHead"];
            // The bill tip is authored in the same anatomical metre frame as
            // the rig. Store it in the imported joint's actual unit wrapper.
            localBillTip = headTransform.InverseTransformPoint(host.TransformPoint(new Vector3(0f, .207f, .128f)));
            leftWing = new Joint(joints["LeftWing"], host);
            rightWing = new Joint(joints["RightWing"], host);
            leftFoot = new Joint(joints["LeftFoot"], host);
            rightFoot = new Joint(joints["RightFoot"], host);
            leftTail = new Joint(joints["LeftTail"], host);
            rightTail = new Joint(joints["RightTail"], host);
        }

        public void Pose(float seconds, float spread, float flap, float legsFolded, float flare,
            float step, float peck, float preen, bool tucked, bool moving,
            Vector3? drinkingTarget = null, float drinkContact = 0f)
        {
            float breath = Mathf.Sin(seconds * 1.9f);
            float stride = Mathf.Sin(seconds * 11f);
            float crouch = peck * .044f + (tucked ? .014f : 0f);
            body.Shift(new Vector3(0f, .0015f * breath - crouch + step * Mathf.Abs(stride) * .004f, 0f));
            body.Rotate(Quaternion.Euler(26f * peck + 5f * legsFolded - 12f * flare, 0f, 0f));
            head.Shift(new Vector3(0f, -.025f * peck, .010f * (step * stride + peck)));
            head.Rotate(Quaternion.Euler(54f * peck + 28f * preen, (moving || drinkingTarget.HasValue ? 0f : Mathf.Sin(seconds * .86f) * 13f) +
                94f * preen + (tucked ? 46f : 0f), 10f * preen));
            if (drinkingTarget.HasValue && drinkContact > 0f)
            {
                Vector3 correction = drinkingTarget.Value - BillPosition;
                headTransform.position += Vector3.ClampMagnitude(correction, .035f) * drinkContact;
            }
            leftWing.Rotate(Wing(-1f, spread, flap));
            rightWing.Rotate(Wing(1f, spread, flap));
            float fan = 7f * spread + 13f * flare;
            leftTail.Rotate(Quaternion.Euler(7f * flare - 4f * peck, fan, 0f));
            rightTail.Rotate(Quaternion.Euler(7f * flare - 4f * peck, -fan, 0f));
            Foot(leftFoot, stride);
            Foot(rightFoot, -stride);

            void Foot(Joint joint, float sidePhase)
            {
                joint.Rotate(Quaternion.Euler(67f * legsFolded - 15f * step * sidePhase, 0f, 0f));
                joint.Shift(new Vector3(0f, (Mathf.Max(0f, sidePhase) * .014f * step) + legsFolded * .014f,
                    step * .013f * sidePhase));
            }
        }

        private static Quaternion Wing(float side, float spread, float flap)
        {
            Quaternion folded = Quaternion.AngleAxis(side * 83f, Vector3.up) * Quaternion.AngleAxis(-62f, Vector3.right);
            Quaternion open = Quaternion.AngleAxis(side * flap, Vector3.forward);
            return Quaternion.Slerp(folded, open, spread);
        }
    }
}
