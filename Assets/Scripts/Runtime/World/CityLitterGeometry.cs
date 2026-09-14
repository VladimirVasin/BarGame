using UnityEngine;

namespace BarPromenade
{
    /// <summary>Footprint and seed arithmetic shared by the eastern strip and the city-wide litter plans.</summary>
    internal static class CityLitterGeometry
    {
        internal static Rect Expand(Rect rect, float amount) => Rect.MinMaxRect(
            rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);

        /// <summary>Conservative XZ body of a rotated, scaled authored box: every corner, plus placement padding.</summary>
        internal static Rect Project(Bounds bounds, Vector3 position, Quaternion rotation, float scale)
        {
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = position + rotation * ((bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))) * scale);
                low = Vector3.Min(low, p); high = Vector3.Max(high, p);
            }
            return Expand(Rect.MinMaxRect(low.x, low.z, high.x, high.z), .025f);
        }

        internal static Rect ToXZ(Bounds bounds) =>
            Rect.MinMaxRect(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);

        /// <summary>XZ body of an oriented box whose size is centred on its own origin.</summary>
        internal static Rect Oriented(Vector3 center, Quaternion rotation, Vector3 size) =>
            Project(new Bounds(Vector3.zero, size), center, rotation, 1f);

        internal static bool Contains(Rect outer, Rect inner) =>
            inner.xMin >= outer.xMin && inner.xMax <= outer.xMax && inner.yMin >= outer.yMin && inner.yMax <= outer.yMax;

        /// <summary>Planar gap between two footprints; zero when they touch or overlap.</summary>
        internal static float Gap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, a.xMin - b.xMax, b.xMin - a.xMax);
            float dz = Mathf.Max(0f, a.yMin - b.yMax, b.yMin - a.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        internal static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>FNV-1a over the id, folded with the layout seed and a per-plan salt so sibling plans never share streams.</summary>
        internal static int Seed(string name, int layoutSeed, uint salt)
        {
            unchecked
            {
                uint value = 2166136261u;
                foreach (char letter in name) value = (value ^ letter) * 16777619u;
                return (int)(value ^ (uint)layoutSeed ^ salt);
            }
        }
    }
}
