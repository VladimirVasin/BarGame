using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The drawn palm's anatomical frame and surface, measured once from the rig.
    /// Offsets are world metres expressed by rotation, independent of FBX scale.
    /// Forward is fingers, up is thumb; the palm faces right on the left hand,
    /// and left on the right hand, as in the shared shower/brushing hand frame.
    /// </summary>
    internal readonly struct LastRouteFerrymanHandFrame
    {
        private readonly bool isLeft;

        private LastRouteFerrymanHandFrame(
            Quaternion rotationInHand, Vector3 palmPointInHand, Vector3 knobPointInHand, bool left)
        {
            RotationInHand = rotationInHand;
            PalmPointInHand = palmPointInHand;
            KnobPointInHand = knobPointInHand;
            isLeft = left;
        }

        internal Quaternion RotationInHand { get; }
        internal Vector3 PalmPointInHand { get; }
        /// <summary>Palmar skin toward the fingers, for a small knob's side grip.</summary>
        internal Vector3 KnobPointInHand { get; }
        internal Vector3 FingerAxisInHand => RotationInHand * Vector3.forward;
        internal Vector3 ThumbAxisInHand => RotationInHand * Vector3.up;
        internal Vector3 PalmNormalInHand =>
            (isLeft ? 1f : -1f) * (RotationInHand * Vector3.right);

        internal static bool TryCreate(
            Transform hand,
            Transform gripSocket,
            bool isLeft,
            Transform presentationRoot,
            out LastRouteFerrymanHandFrame frame)
        {
            frame = default;
            if (hand == null || gripSocket == null || presentationRoot == null)
                return false;

            string side = isLeft ? ".L" : ".R";
            SkinnedMeshRenderer palm = null;
            SkinnedMeshRenderer thumb = null;
            foreach (SkinnedMeshRenderer renderer in
                     presentationRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name == "GEO_Hand" + side) palm = renderer;
                else if (renderer.name == "GEO_Thumb" + side) thumb = renderer;
            }
            if (palm == null || thumb == null ||
                palm.sharedMesh == null || thumb.sharedMesh == null)
                return false;

            var sample = new Mesh { name = "Ferryman hand contact measurement" };
            try
            {
                var palmVertices = new List<Vector3>();
                var thumbVertices = new List<Vector3>();
                if (!ReadWorldVertices(palm, sample, palmVertices, out Vector3 palmCenter))
                    return false;
                int[] triangles = sample.triangles;
                if (!ReadWorldVertices(thumb, sample, thumbVertices, out Vector3 thumbCenter))
                    return false;

                Vector3 fingers = gripSocket.position - hand.position;
                if (!IsFinite(fingers) || fingers.sqrMagnitude < 0.000001f)
                    return false;
                fingers.Normalize();
                Vector3 towardThumb = Vector3.ProjectOnPlane(thumbCenter - palmCenter, fingers);
                if (!IsFinite(towardThumb) || towardThumb.sqrMagnitude < 0.00000001f)
                    return false;
                towardThumb.Normalize();
                Quaternion physicalFrame = Quaternion.LookRotation(fingers, towardThumb);
                Vector3 palmNormal = (isLeft ? 1f : -1f) * (physicalFrame * Vector3.right);

                // The anchor used for the coin is the centre of this volume.
                // Find the actual skin in the palm direction, not that centre.
                float nearest = float.PositiveInfinity;
                float distalExtent = 0f;
                foreach (Vector3 vertex in palmVertices)
                    distalExtent = Mathf.Max(distalExtent, Vector3.Dot(vertex - palmCenter, fingers));
                Vector3 knobRayOrigin = palmCenter + fingers * (0.6f * distalExtent);
                float nearestKnob = float.PositiveInfinity;
                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    int a = triangles[index];
                    int b = triangles[index + 1];
                    int c = triangles[index + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= palmVertices.Count ||
                        b >= palmVertices.Count || c >= palmVertices.Count)
                        return false;
                    if (RayTriangle(palmCenter, palmNormal, palmVertices[a],
                            palmVertices[b], palmVertices[c], out float distance))
                        nearest = Mathf.Min(nearest, distance);
                    if (RayTriangle(knobRayOrigin, palmNormal, palmVertices[a],
                            palmVertices[b], palmVertices[c], out float knobDistance))
                        nearestKnob = Mathf.Min(nearestKnob, knobDistance);
                }
                if (float.IsPositiveInfinity(nearest))
                    return false;

                Quaternion inverseHand = Quaternion.Inverse(hand.rotation);
                Vector3 palmPoint = palmCenter + palmNormal * nearest;
                Vector3 knobPoint = float.IsPositiveInfinity(nearestKnob) ? palmPoint :
                    knobRayOrigin + palmNormal * nearestKnob;
                frame = new LastRouteFerrymanHandFrame(
                    inverseHand * physicalFrame,
                    inverseHand * (palmPoint - hand.position),
                    inverseHand * (knobPoint - hand.position),
                    isLeft);
                return true;
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(sample);
                else Object.DestroyImmediate(sample);
            }
        }

        private static bool ReadWorldVertices(
            SkinnedMeshRenderer renderer, Mesh sample, List<Vector3> vertices,
            out Vector3 center)
        {
            center = Vector3.zero;
            sample.Clear(false);
            // Match HomeTeethBrushingArmPose and the production foot probe:
            // this imported hierarchy requires useScale=true before world conversion.
            renderer.BakeMesh(sample, true);
            sample.GetVertices(vertices);
            if (vertices.Count == 0)
                return false;
            Matrix4x4 world = renderer.transform.localToWorldMatrix;
            for (int index = 0; index < vertices.Count; index++)
            {
                Vector3 point = world.MultiplyPoint3x4(vertices[index]);
                if (!IsFinite(point))
                    return false;
                vertices[index] = point;
                center += point;
            }
            center /= vertices.Count;
            return true;
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

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
