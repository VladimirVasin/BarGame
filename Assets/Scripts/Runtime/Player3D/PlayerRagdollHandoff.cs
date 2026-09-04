using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the balance model hands the ragdoll at the moment of a fall:
    /// the motion the body already had. The topple was a rigid rotation
    /// about the edge of the support the boots made, so the ragdoll's
    /// bodies start with that rotation's velocity field — the centre of
    /// mass keeps its speed, the head is going faster, the feet hardly
    /// move — instead of dropping from a standstill and being shoved.
    /// </summary>
    public readonly struct PlayerRagdollHandoff
    {
        public PlayerRagdollHandoff(
            Vector3 linearVelocity,
            Vector3 angularVelocity,
            Vector3 fallAxis,
            Vector3 pivotPoint,
            float signedDirection)
        {
            LinearVelocity = Sanitize(linearVelocity);
            AngularVelocity = Sanitize(angularVelocity);
            Vector3 axis = Sanitize(fallAxis);
            axis.y = 0f;
            FallAxis = axis.sqrMagnitude > 0.0001f
                ? axis.normalized
                : Vector3.right;
            PivotPoint = Sanitize(pivotPoint);
            SignedDirection = signedDirection < 0f ? -1f : 1f;
        }

        /// <summary>
        /// The old scripted shove for callers that only know a side: no
        /// rotation, so the ragdoll gets its legacy impulses in full.
        /// </summary>
        public static PlayerRagdollHandoff Legacy(
            float signedDirection,
            Transform root)
        {
            float sign = signedDirection < 0f ? -1f : 1f;
            Vector3 right = root != null
                ? Vector3.ProjectOnPlane(root.right, Vector3.up)
                : Vector3.right;
            if (right.sqrMagnitude <= 0.0001f)
            {
                right = Vector3.right;
            }

            return new PlayerRagdollHandoff(
                Vector3.zero,
                Vector3.zero,
                right.normalized * sign,
                root != null ? root.position : Vector3.zero,
                sign);
        }

        /// <summary>World velocity of the centre of mass, m/s.</summary>
        public Vector3 LinearVelocity { get; }

        /// <summary>World angular velocity of the whole body about the support edge, rad/s.</summary>
        public Vector3 AngularVelocity { get; }

        /// <summary>World planar unit direction of the fall.</summary>
        public Vector3 FallAxis { get; }

        /// <summary>World point on the ground the body rotates about.</summary>
        public Vector3 PivotPoint { get; }

        /// <summary>Legacy side of the fall, <c>±1</c>, for the camera and the clip side.</summary>
        public float SignedDirection { get; }

        /// <summary>How fast the body is rotating, rad/s.</summary>
        public float AngularSpeed => AngularVelocity.magnitude;

        /// <summary>
        /// The velocity the rotation gives a point of the body: the
        /// rigid-rotation field <c>ω × (r − pivot)</c>. At the centre of
        /// mass it reproduces the model's own velocity by construction,
        /// so nothing is added on top.
        /// </summary>
        public Vector3 VelocityAt(Vector3 worldPoint)
        {
            return Vector3.Cross(AngularVelocity, worldPoint - PivotPoint);
        }

        private static Vector3 Sanitize(Vector3 value)
        {
            return float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) ||
                   float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z)
                ? Vector3.zero
                : value;
        }
    }
}
