using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityAltitudeVisibility
    {
        private readonly Plane[] streetViewPlanes = new Plane[6];
        private int streetViewFrame = -1;

        internal bool CanChangeBodyAt(Vector3 feet) => sceneCamera == null || IsStreetBodyHidden(feet);

        // Background arrivals also use nearer entries. Apply the full view
        // test even at lowland fog density before changing one of those bodies.
        internal bool CanChangeAmbientBodyAt(Vector3 feet) => sceneCamera != null && IsStreetBodyHidden(feet);

        internal bool CanChangeAmbientBodyAt(Bounds bounds) => sceneCamera != null && IsStreetBodyHidden(bounds);

        internal bool CanChangeWorkBodyAt(Vector3 feet, CityBuildingEntranceDescriptor entrance)
        {
            if (sceneCamera == null || entrance == null) return false;
            if (IsStreetBodyHidden(feet)) return true;
            Quaternion toLocal = Quaternion.Inverse(entrance.Lot.FacadeRotation);
            return CityIndustrialEntranceGeometry.HidesBody(
                toLocal * (sceneCamera.transform.position - entrance.Threshold),
                toLocal * (feet - entrance.Threshold));
        }

        private bool IsStreetBodyHidden(Vector3 feet) =>
            IsStreetBodyHidden(new Bounds(feet + Vector3.up, new Vector3(2f, 2.5f, 2f)));

        private bool IsStreetBodyHidden(Bounds bounds)
        {
            if (streetViewFrame != Time.frameCount)
            {
                GeometryUtility.CalculateFrustumPlanes(sceneCamera, streetViewPlanes);
                streetViewFrame = Time.frameCount;
            }
            // A renderer can be beyond the radial fog wall and still inside
            // the camera frustum, especially toward the sides of the image.
            float hiddenCenterDistance = sceneCamera.farClipPlane + bounds.extents.magnitude;
            return (bounds.center - sceneCamera.transform.position).sqrMagnitude >=
                hiddenCenterDistance * hiddenCenterDistance ||
                !GeometryUtility.TestPlanesAABB(streetViewPlanes, bounds);
        }
    }
}
