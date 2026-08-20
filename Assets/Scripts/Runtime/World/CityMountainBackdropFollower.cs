using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Keeps the ridge shell centred on the gameplay camera without rotating
    /// it. Translation removes parallax that would expose the shell's finite
    /// radius; retaining world rotation keeps west and south as real compass
    /// directions instead of making the mountains follow the player's look.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class CityMountainBackdropFollower : MonoBehaviour
    {
        private Camera trackedCamera;

        public void Initialize(Camera camera = null)
        {
            trackedCamera = camera;
            AlignToCamera(ResolveCamera());
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering +=
                HandleBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -=
                HandleBeginCameraRendering;
        }

        private void LateUpdate()
        {
            AlignToCamera(ResolveCamera());
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera camera)
        {
            Camera target = ResolveCamera();
            if (target != null && camera == target)
            {
                AlignToCamera(target);
            }
        }

        internal void AlignToCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            transform.position = cameraPosition;
            transform.rotation = Quaternion.identity;
        }

        private Camera ResolveCamera()
        {
            if (trackedCamera != null)
            {
                return trackedCamera;
            }

            trackedCamera = Camera.main;
            if (trackedCamera == null)
            {
                trackedCamera = Object.FindAnyObjectByType<Camera>();
            }

            return trackedCamera;
        }
    }
}
