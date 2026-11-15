using UnityEngine;

namespace BarPromenade
{
    /// <summary>Height-aware oriented footprint. Separate bridge levels and opposing lanes stay independent.</summary>
    public readonly struct CityTrafficHull
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Bounds LocalBounds;
        public readonly Vector3 Centre;
        private readonly Vector2 right, forward;
        private readonly float halfWidth, halfLength, halfHeight;

        public CityTrafficHull(Vector3 position, Quaternion rotation, Bounds bounds, float padding = 0f)
        {
            Position = position; Rotation = rotation; LocalBounds = bounds;
            Centre = position + rotation * bounds.center;
            Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
            forward = new Vector2(z.x, z.z).normalized;
            right = new Vector2(forward.y, -forward.x);
            halfWidth = bounds.extents.x + padding;
            // Pitch can bring the top of a body further along the road than its wheels.
            halfLength = bounds.extents.z + Mathf.Abs(y.x * forward.x + y.z * forward.y) * bounds.extents.y + padding;
            halfHeight = Mathf.Abs(x.y) * bounds.extents.x + Mathf.Abs(y.y) * bounds.extents.y + Mathf.Abs(z.y) * bounds.extents.z;
        }

        public bool Overlaps(CityTrafficHull other)
        {
            if (Mathf.Abs(Centre.y - other.Centre.y) > halfHeight + other.halfHeight) return false;
            Vector2 delta = new Vector2(other.Centre.x - Centre.x, other.Centre.z - Centre.z);
            return OnAxis(delta, right, other) && OnAxis(delta, forward, other) &&
                OnAxis(delta, other.right, other) && OnAxis(delta, other.forward, other);
        }

        private bool OnAxis(Vector2 delta, Vector2 axis, CityTrafficHull other) =>
            Mathf.Abs(Vector2.Dot(delta, axis)) <=
            Mathf.Abs(Vector2.Dot(right, axis)) * halfWidth + Mathf.Abs(Vector2.Dot(forward, axis)) * halfLength +
            Mathf.Abs(Vector2.Dot(other.right, axis)) * other.halfWidth + Mathf.Abs(Vector2.Dot(other.forward, axis)) * other.halfLength;

        public bool ContainsCapsule(Vector3 feet, float radius, float height = 1.8f)
        {
            if (feet.y > Centre.y + halfHeight || feet.y + height < Centre.y - halfHeight) return false;
            Vector2 delta = new Vector2(feet.x - Centre.x, feet.z - Centre.z);
            float dx = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(delta, right)) - halfWidth);
            float dz = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(delta, forward)) - halfLength);
            return dx * dx + dz * dz <= radius * radius;
        }
    }
}
