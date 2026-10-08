using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded, volume-preserving spring strain on authored tissue. The duel owns its clock.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatBrainTissue : MonoBehaviour
    {
        private Mesh mesh;
        private Transform meshFrame;
        private Vector3[] rest, vertices;
        private Vector3 centre, strain, velocity, supportAxis;
        private Collider support;
        private bool detached;
        internal float Deformation => strain.magnitude;
        internal bool HasSupport => support != null;

        internal void Initialize(Mesh target, Transform frame, Bounds bounds, bool released)
        {
            mesh = target; meshFrame = frame; rest = mesh.vertices; vertices = new Vector3[rest.Length];
            centre = bounds.center; detached = released;
            strain = velocity = supportAxis = Vector3.zero; support = null;
            mesh.MarkDynamic();
        }

        internal void Impulse(Vector3 direction, float strength)
        {
            Vector3 axis = direction.sqrMagnitude > .000001f ? direction.normalized : Vector3.up;
            Vector3 compression = new Vector3(axis.x * axis.x, axis.y * axis.y, axis.z * axis.z);
            velocity += (Vector3.one / 3f - compression) * strength;
            velocity = Vector3.ClampMagnitude(velocity, detached ? 4f : .7f);
        }

        internal void Tick(float seconds, Vector3 inertia)
        {
            if (mesh == null || seconds <= 0f) return;
            // Small substeps also keep diagnostic/finished-round advances stable.
            int steps = Mathf.Clamp(Mathf.CeilToInt(seconds / .012f), 1, 128);
            float step = Mathf.Min(seconds / steps, .012f);
            Vector3 target = support != null ? (Vector3.one / 3f - supportAxis) * .24f : Vector3.zero;
            for (int i = 0; i < steps; i++)
            {
                velocity += ((target - strain) * 150f - velocity * 10f + inertia) * step;
                strain += velocity * step;
                strain = Vector3.ClampMagnitude(strain, detached ? .3f : .025f);
            }
            ApplyShape();
        }

        internal void ResetShape()
        {
            strain = velocity = Vector3.zero; support = null;
            if (mesh != null) ApplyShape();
        }

        private void ApplyShape()
        {
            Vector3 scale = Vector3.one + strain;
            // Equal volume during squeeze; a shared retained centre/strain keeps all cut faces joined.
            scale /= Mathf.Pow(scale.x * scale.y * scale.z, 1f / 3f);
            for (int i = 0; i < rest.Length; i++) vertices[i] = centre + Vector3.Scale(rest[i] - centre, scale);
            mesh.vertices = vertices;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!detached || collision.contactCount == 0) return;
            Vector3 normal = collision.GetContact(0).normal;
            Impulse(meshFrame.InverseTransformDirection(normal), Mathf.Clamp(collision.relativeVelocity.magnitude, .3f, 4f));
            ReadSupport(collision, normal);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (detached && collision.contactCount > 0) ReadSupport(collision, collision.GetContact(0).normal);
        }

        private void ReadSupport(Collision collision, Vector3 normal)
        {
            if (Vector3.Dot(normal, Vector3.up) < .35f) return;
            support = collision.collider;
            Vector3 axis = meshFrame.InverseTransformDirection(normal).normalized;
            supportAxis = new Vector3(axis.x * axis.x, axis.y * axis.y, axis.z * axis.z);
        }

        private void OnCollisionExit(Collision collision)
        {
            if (collision.collider == support) support = null;
        }
    }
}
