using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPedestrianPresentation
    {
        private Transform busArrivalTarget, busArrivalSpine;
        private float busArrivalLean;
        private Quaternion busArrivalBase;
        private bool busArrivalWritten;

        internal Transform NoticedBus => busArrivalTarget;

        internal void UpdateBusArrival(Transform target, float lean, float delta)
        {
            busArrivalTarget = target;
            busArrivalLean = Mathf.MoveTowards(busArrivalLean, target != null ? lean : 0f, 27f * delta);
        }

        private void ApplyBusArrivalPose()
        {
            if (Mathf.Abs(busArrivalLean) < .01f) return;
            if (!busArrivalSpine)
                busArrivalSpine = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "spine");
            if (!busArrivalSpine) return;
            busArrivalBase = busArrivalSpine.localRotation;
            busArrivalWritten = true;
            Vector3 axis = transform.right;
            if (busArrivalTarget != null && busArrivalLean > 0f)
            {
                Vector3 toward = Vector3.ProjectOnPlane(busArrivalTarget.position - transform.position, Vector3.up);
                axis = Vector3.Cross(Vector3.up, (transform.forward + toward.normalized).normalized);
            }
            busArrivalSpine.rotation = Quaternion.AngleAxis(busArrivalLean, axis) * busArrivalSpine.rotation;
        }

        private void RestoreBusArrivalPose()
        {
            if (busArrivalWritten && busArrivalSpine) busArrivalSpine.localRotation = busArrivalBase;
            busArrivalWritten = false;
        }

        internal void ClearBusArrival()
        {
            RestoreBusArrivalPose();
            busArrivalTarget = null;
            busArrivalLean = 0f;
        }
    }
}
