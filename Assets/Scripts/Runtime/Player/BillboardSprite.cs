using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Faces a sprite toward the active camera. The shared default preserves
    /// world up, while authored fixed views can opt into the exact camera
    /// plane to avoid perspective compression.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class BillboardSprite : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool useMainCameraFallback = true;
        [SerializeField]
        private bool alignToCameraPlane;

        public bool CameraPlaneAlignmentEnabled =>
            alignToCameraPlane;

        public void Initialize(Camera camera)
        {
            targetCamera = camera;
            FaceCamera();
        }

        public void SetCameraPlaneAlignment(
            bool enabled)
        {
            alignToCameraPlane = enabled;
            FaceCamera();
        }

        public void FaceCameraNow()
        {
            FaceCamera();
        }

        private void LateUpdate()
        {
            FaceCamera();
        }

        private void FaceCamera()
        {
            Camera camera = ResolveCamera();
            if (camera == null)
            {
                return;
            }

            Vector3 toCamera = camera.transform.position - transform.position;
            Vector3 flatDirection = Vector3.ProjectOnPlane(toCamera, Vector3.up);
            if (flatDirection.sqrMagnitude < 0.0001f)
            {
                flatDirection = Vector3.ProjectOnPlane(-camera.transform.forward, Vector3.up);
            }

            if (flatDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            if (alignToCameraPlane &&
                camera.transform.forward.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    -camera.transform.forward,
                    camera.transform.up);
                return;
            }

            transform.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
        }

        private Camera ResolveCamera()
        {
            if (targetCamera == null && useMainCameraFallback)
            {
                targetCamera = Camera.main;
            }

            return targetCamera;
        }
    }
}
