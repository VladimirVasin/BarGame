using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityAltitudeVisibility
    {
        private readonly Plane[] streetViewPlanes = new Plane[6];
        private int streetViewFrame = -1;

        // The old 32 m lowland hand-off was already fully in fog. On the hill
        // keep that optical depth, or wait until the whole body leaves the shot.
        internal bool CanChangeBodyAt(Vector3 feet)
        {
            if (sceneCamera == null || Density >= RuntimeSceneSetup.CityFogDensity) return true;
            if (streetViewFrame != Time.frameCount)
            {
                GeometryUtility.CalculateFrustumPlanes(sceneCamera, streetViewPlanes);
                streetViewFrame = Time.frameCount;
            }
            const float hiddenOpticalDepth = CityPedestrianDirector.MinimumConnectedSpawnDistance * RuntimeSceneSetup.CityFogDensity;
            var bounds = new Bounds(feet + Vector3.up, new Vector3(2f, 2.5f, 2f));
            float nearDepth = streetViewPlanes[4].GetDistanceToPoint(bounds.center) - bounds.extents.magnitude;
            return nearDepth * Density >= hiddenOpticalDepth || !GeometryUtility.TestPlanesAABB(streetViewPlanes, bounds);
        }
    }
}
