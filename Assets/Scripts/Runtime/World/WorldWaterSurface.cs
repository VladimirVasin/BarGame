using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Actual authored sea/river sheets expose their footprint and datum without colliders or name searches.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldWaterSurface : MonoBehaviour
    {
        private static readonly List<WorldWaterSurface> Surfaces = new List<WorldWaterSurface>(64);
        private Rect footprint;
        private float south, north;
        private Func<Vector2, float> sample;
        public void Configure(Rect bounds, float southTop, float northTop, Func<Vector2, float> sampleTop)
        {
            footprint = bounds; south = southTop; north = northTop; sample = sampleTop;
            if (!Surfaces.Contains(this)) Surfaces.Add(this);
        }
        public static bool TryCrossing(Vector3 previous, Vector3 current, out Vector3 contact)
        {
            contact = current;
            bool found = false;
            float first = float.PositiveInfinity;
            foreach (WorldWaterSurface water in Surfaces)
            {
                if (water == null || !water.isActiveAndEnabled) continue;
                float before = previous.y - water.Top(previous);
                float after = current.y - water.Top(current);
                if (after > 0f) continue;
                float t;
                if (before > 0f)
                {
                    float low = 0f, high = 1f;
                    for (int step = 0; step < 9; step++)
                    {
                        float middle = (low + high) * .5f;
                        Vector3 probe = Vector3.Lerp(previous, current, middle);
                        if (probe.y > water.Top(probe)) low = middle; else high = middle;
                    }
                    t = (low + high) * .5f;
                }
                else
                {
                    // A bottle released below the datum or entering the volume horizontally is wet too.
                    t = 1f;
                }
                Vector3 crossing = Vector3.Lerp(previous, current, t);
                if (!water.footprint.Contains(new Vector2(crossing.x, crossing.z)) || t >= first) continue;
                crossing.y = water.Top(crossing);
                contact = crossing; first = t; found = true;
            }
            return found;
        }

        internal bool TryRainHit(Ray ray, float limit, out float distance)
        {
            distance = 0f;
            if (ray.direction.y >= -.001f || ray.origin.y <= Top(ray.origin)) return false;
            Vector3 end = ray.GetPoint(limit);
            if (end.y > Top(end)) return false;

            float low = 0f, high = limit;
            for (int step = 0; step < 15; step++)
            {
                float mid = (low + high) * .5f;
                Vector3 point = ray.GetPoint(mid);
                if (point.y > Top(point)) low = mid;
                else high = mid;
            }
            distance = (low + high) * .5f;
            Vector3 hit = ray.GetPoint(distance);
            return footprint.Contains(new Vector2(hit.x, hit.z));
        }
        private float Top(Vector3 position)
        {
            var xz = new Vector2(Mathf.Clamp(position.x, footprint.xMin, footprint.xMax),
                Mathf.Clamp(position.z, footprint.yMin, footprint.yMax));
            return sample != null ? sample(xz) :
                Mathf.Lerp(south, north, Mathf.InverseLerp(footprint.yMin, footprint.yMax, xz.y));
        }

        private void OnEnable() { if (footprint.width > 0f && !Surfaces.Contains(this)) Surfaces.Add(this); }
        private void OnDisable() => Surfaces.Remove(this);
        private void OnDestroy() => Surfaces.Remove(this);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetShared() => Surfaces.Clear();
    }
}
