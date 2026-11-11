using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    // Water sheets can release their render mesh's CPU data. Rain reads the
    // same footprint and height that created it, including the puddle's live rim.
    internal sealed class CityRainWaterReceiver : MonoBehaviour
    {
        private IReadOnlyList<RuntimeOrientedBox> puddles;
        private float radius;

        internal void SetPuddles(IReadOnlyList<RuntimeOrientedBox> patches) => puddles = patches;
        internal void SetDisc(float radiusInMetres) => radius = radiusInMetres;
        internal IReadOnlyList<RuntimeOrientedBox> Puddles => puddles;

        internal bool TryDisc(Ray ray, float limit, out float distance)
        {
            Vector3 centre = transform.position;
            if (!TryPlane(ray, centre, transform.up, limit, out distance)) return false;
            return (ray.GetPoint(distance) - centre).sqrMagnitude < radius * radius;
        }

        internal static bool TryPuddle(RuntimeOrientedBox patch, Ray ray, float limit,
            out float distance, out Vector3 normal)
        {
            normal = patch.Rotation * Vector3.up;
            return TryPlane(ray, patch.Center, normal, limit, out distance) &&
                PuddleFootstepOverlay.IsWetAt(patch, ray.GetPoint(distance),
                    CityWetSurfaceRegistry.CurrentWetness, CityPuddleWaterResources.EdgeBite);
        }

        private static bool TryPlane(Ray ray, Vector3 point, Vector3 normal, float limit, out float distance)
        {
            distance = 0f;
            float approach = Vector3.Dot(ray.direction, normal);
            if (approach >= -.0001f) return false;
            distance = Vector3.Dot(point - ray.origin, normal) / approach;
            return distance >= 0f && distance < limit;
        }
    }
}
