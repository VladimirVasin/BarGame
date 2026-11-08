using UnityEngine;

namespace BarPromenade
{
    /// <summary>Measured reach through the open hatch, without competing optical subtargets.</summary>
    public sealed class RidgeKioskTarget : MonoBehaviour
    {
        private Transform kiosk, focus, approach;
        public static RidgeKioskTarget Attach(Transform group, Transform host, Transform focus, Transform approach)
        {
            var target = host.gameObject.AddComponent<RidgeKioskTarget>();
            target.kiosk = group; target.focus = focus; target.approach = approach;
            return target;
        }
        public bool CanReach(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || !interactor || !focus) return false;
            Vector3 ground = interactor.transform.position;
            Vector3 offset = ground - approach.position;
            if (Mathf.Abs(offset.y) > .55f || offset.sqrMagnitude > 2.15f * 2.15f ||
                Vector3.Dot(ground - kiosk.position, kiosk.forward) < 1.34f) return false;
            Vector3 eye = ground + Vector3.up * 1.5f;
            Vector3 ray = focus.position - eye;
            return !Physics.Raycast(eye, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore);
        }
        public bool IsAvailable(PlayerInteractor interactor)
        {
            Camera camera = Camera.main;
            return camera && CanReach(interactor) &&
                Vector3.Dot(camera.transform.forward, (focus.position - camera.transform.position).normalized) > .70f;
        }
    }
}
