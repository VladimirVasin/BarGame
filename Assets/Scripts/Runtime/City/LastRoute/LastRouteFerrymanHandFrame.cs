using System;
using System.Linq;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Skin contact points in the default hand's rotation frame, in metres.</summary>
    internal readonly struct LastRouteFerrymanHandFrame
    {
        private LastRouteFerrymanHandFrame(AnatomicalHandFrame anatomy, Vector3 palm, Vector3 knob)
        {
            Anatomy = anatomy;
            PalmPointInHand = palm;
            KnobPointInHand = knob;
        }

        internal AnatomicalHandFrame Anatomy { get; }
        internal Vector3 PalmPointInHand { get; }
        internal Vector3 KnobPointInHand { get; }
        internal Vector3 FingerAxisInHand => Anatomy.FingerDirectionInHand;
        internal Vector3 ThumbAxisInHand => Anatomy.ThumbDirectionInHand;
        internal Vector3 PalmNormalInHand => Anatomy.PalmNormalInHand;

        internal static LastRouteFerrymanHandFrame Measure(NpcHandPose pose, bool isLeft)
        {
            AnatomicalHandFrame anatomy = pose.GetAnatomicalFrame(isLeft);
            NpcHandPose.HandBinding hand = pose.Hands.Single(binding => binding.IsLeft == isLeft);
            string palmName = isLeft ? "GEO_HandPalm.L" : "GEO_HandPalm.R";
            SkinnedMeshRenderer palm = hand.Renderers.Single(renderer => renderer.name == palmName);
            Mesh mesh = palm.sharedMesh;
            Matrix4x4 bind = mesh.bindposes[Array.IndexOf(palm.bones, hand.Hand)];
            Quaternion inverse = Quaternion.Inverse(hand.Hand.rotation);
            Vector3[] vertices = mesh.vertices;
            Vector3 center = anatomy.PalmCentreInHand;
            float extent = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = inverse * hand.Hand.TransformVector(bind.MultiplyPoint3x4(vertices[i]));
                extent = Mathf.Max(extent, Vector3.Dot(vertices[i] - center, anatomy.FingerDirectionInHand));
            }

            Vector3 normal = anatomy.PalmNormalInHand;
            Vector3 distal = center + anatomy.FingerDirectionInHand * (.58f * extent);
            int[] triangles = mesh.triangles;
            float middleDistance = SkinDistance(center, normal, vertices, triangles);
            float distalDistance = SkinDistance(distal, normal, vertices, triangles);
            if (float.IsPositiveInfinity(middleDistance) || float.IsPositiveInfinity(distalDistance))
                throw new InvalidOperationException("The Ferryman's default palm has no contact surface.");

            return new LastRouteFerrymanHandFrame(anatomy, center + normal * middleDistance,
                distal + normal * distalDistance);
        }

        private static float SkinDistance(Vector3 point, Vector3 normal, Vector3[] vertices, int[] triangles)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (RayTriangle(point, normal, vertices[triangles[i]], vertices[triangles[i + 1]],
                    vertices[triangles[i + 2]], out float distance))
                    nearest = Mathf.Min(nearest, distance);
            }
            return nearest;
        }

        private static bool RayTriangle(
            Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 0.0000000001f)
                return false;
            float inverse = 1f / determinant;
            Vector3 t = origin - a;
            float u = Vector3.Dot(t, p) * inverse;
            if (u < -0.00001f || u > 1.00001f)
                return false;
            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -0.00001f || u + v > 1.00001f)
                return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return !float.IsNaN(distance) && !float.IsInfinity(distance) && distance >= 0f;
        }
    }
}
