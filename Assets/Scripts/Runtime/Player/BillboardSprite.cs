using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps a sprite presentation facing the active camera without tilting it
    /// away from the world's up axis.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class BillboardSprite : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool useMainCameraFallback = true;

        public void Initialize(Camera camera)
        {
            targetCamera = camera;
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
