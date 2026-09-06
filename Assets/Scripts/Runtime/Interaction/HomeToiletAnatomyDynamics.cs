using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded angular springs driven by camera movement and the held shake.</summary>
    public sealed class HomeToiletAnatomyDynamics
    {
        public const float ShaftLimitDegrees = 8f;
        public const float ScrotumLimitDegrees = 22f;
        private const float MaximumStep = 1f / 120f;
        private Vector2 shaft;
        private Vector2 left;
        private Vector2 right;
        private Vector2 shaftVelocity;
        private Vector2 leftVelocity;
        private Vector2 rightVelocity;
        private Quaternion previousCamera;
        private float previousShake;
        private bool initialized;

        public Vector2 ShaftDegrees => shaft;
        public Vector2 LeftDegrees => left;
        public Vector2 RightDegrees => right;
        public float MotionMagnitude => shaft.magnitude + left.magnitude + right.magnitude;

        public void Reset(Quaternion cameraRotation)
        {
            shaft = left = right = shaftVelocity = leftVelocity = rightVelocity = Vector2.zero;
            previousCamera = cameraRotation;
            previousShake = 0f;
            initialized = true;
        }

        public void Advance(float seconds, Quaternion cameraRotation, Quaternion bodyRotation, float heldShake)
        {
            if (!initialized) Reset(cameraRotation);
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Quaternion delta = cameraRotation * Quaternion.Inverse(previousCamera);
            // q and -q represent the same rotation; use the shortest impulse
            // through the 360-degree wrap instead of a complete revolution.
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            Vector3 localDelta = angle > 0.0001f && IsFinite(axis)
                ? Quaternion.Inverse(bodyRotation) * (axis * angle) : Vector3.zero;
            Vector2 cameraRate = Vector2.ClampMagnitude(new Vector2(localDelta.x, localDelta.y) / seconds, 540f);
            float shakeRate = Mathf.Clamp((heldShake - previousShake) / seconds, -360f, 360f);
            previousCamera = cameraRotation;
            previousShake = heldShake;
            Vector2 forcing = -cameraRate;
            Vector2 pendulumForcing = forcing + new Vector2(-shakeRate * 0.7f, 0f);
            // Gravity owns each freely hanging lobe's restoring term (g/L);
            // the held shaft has the stiffer torsional spring of the grip.
            float remaining = Mathf.Min(seconds, 0.25f);
            while (remaining > 0.000001f)
            {
                float step = Mathf.Min(remaining, MaximumStep);
                Integrate(ref shaft, ref shaftVelocity, forcing * 12f, 18f, 0.45f, ShaftLimitDegrees, step);
                Integrate(ref left, ref leftVelocity, pendulumForcing * 45f,
                    Mathf.Sqrt(9.81f / 0.058f), 0.28f, ScrotumLimitDegrees, step);
                Integrate(ref right, ref rightVelocity, pendulumForcing * 40f,
                    Mathf.Sqrt(9.81f / 0.050f), 0.32f, ScrotumLimitDegrees, step);
                remaining -= step;
            }
        }

        private static void Integrate(ref Vector2 angle, ref Vector2 velocity,
            Vector2 forcing, float frequency, float damping, float limit, float step)
        {
            velocity += (forcing - angle * (frequency * frequency) -
                velocity * (2f * damping * frequency)) * step;
            angle += velocity * step;
            if (angle.sqrMagnitude <= limit * limit) return;
            Vector2 normal = angle.normalized;
            angle = normal * limit;
            float outward = Vector2.Dot(velocity, normal);
            if (outward > 0f) velocity -= normal * outward;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
