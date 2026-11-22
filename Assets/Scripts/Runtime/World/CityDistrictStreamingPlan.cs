using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Assigns authored road sections to their district and locates the approaches.</summary>
    internal sealed class CityDistrictStreamingPlan
    {
        // Preload before the summit view's 110 m far plane can see the other side.
        internal const float PreloadDistance = 135f;
        internal const float ReleaseDistance = 157f;
        // A passenger can cover the buffer faster than a walker. Hold both
        // payloads when the route will carry the seated hero through the seam.
        internal const float PassengerPreloadDistance = 215f;
        internal const float PassengerReleaseDistance = 238f;
        private readonly HashSet<CityStreetSupportPatch> ridgeStreet = new();
        private readonly float entranceZ;
        private readonly float tunnelCeiling;

        internal CityDistrictStreamingPlan(CityLayout layout)
        {
            float north = float.NegativeInfinity;
            float streetY = float.PositiveInfinity;
            foreach (CityHillsideStreetRoute route in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                ridgeStreet.UnionWith(route.SurfacePatches);
                Vector3 mouth = layout.GetNodeWorldPosition(route.MouthNode);
                north = Mathf.Max(north, mouth.z);
                streetY = Mathf.Min(streetY, mouth.y);
            }

            foreach (var link in CityHillsideQuarterPlan.ForLayout(layout).Links)
                ridgeStreet.UnionWith(link.SurfacePatches);
            entranceZ = north - layout.RoadWidth * .5f;
            tunnelCeiling = streetY - 2f;
        }

        internal bool IncludeRidgeStreet(CityStreetSupportPatch patch) => ridgeStreet.Contains(patch);
        internal bool IncludeLowerStreet(CityStreetSupportPatch patch) => !IncludeRidgeStreet(patch);

        // Positive towards the lower city. The separate tunnel floor remains
        // in the lower city even where it passes under the hillside in XZ.
        internal float SignedDistance(Vector3 point) => point.y < tunnelCeiling
            ? Mathf.Max(0f, point.z - entranceZ)
            : point.z - entranceZ;

        internal bool IsRidge(Vector3 point) => SignedDistance(point) < 0f;
        internal bool IsLower(Vector3 point) => !IsRidge(point);
        internal bool AtStreetSeam(Vector3 point) =>
            point.y >= tunnelCeiling && Mathf.Abs(point.z - entranceZ) <= 4f;
    }
}
