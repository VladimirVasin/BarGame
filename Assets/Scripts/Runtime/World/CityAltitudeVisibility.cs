using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>One City fog and drawing distance at every street elevation.</summary>
    [DisallowMultipleComponent]
    public sealed partial class CityAltitudeVisibility : MonoBehaviour
    {
        public const float LowlandTop = 5f;
        private static readonly int ValleyFogId = Shader.PropertyToID("_CityValleyFog");
        private static CityAltitudeVisibility current;
        private Camera sceneCamera;
        private CityFogField fogField;

        public float Density => RuntimeSceneSetup.CityFogDensity;

        internal void Initialize(Camera camera, CityFogField field)
        {
            sceneCamera = camera;
            CityStreetViewRange.Install(camera);
            fogField = field;
            Refresh(true);
        }

        public void Refresh(bool force = false)
        {
            if (!isActiveAndEnabled || fogField == null || sceneCamera == null) return;
            if (!force && current == this) return;
            current = this;
            RenderSettings.fogDensity = Density;
            sceneCamera.farClipPlane = RuntimeSceneSetup.CityFarClipPlane;
            Shader.SetGlobalVector(ValleyFogId, Vector4.zero);
        }

        private void OnEnable() => Refresh();

        private void OnDisable()
        {
            if (current != this) return;
            current = null;
            Shader.SetGlobalVector(ValleyFogId, Vector4.zero);
            if (sceneCamera != null) sceneCamera.farClipPlane = RuntimeSceneSetup.CityFarClipPlane;
            if (gameObject.scene == SceneManager.GetActiveScene()) RenderSettings.fogDensity = RuntimeSceneSetup.CityFogDensity;
        }
    }
}
