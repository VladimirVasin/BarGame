using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>City street air follows the hero's elevation above sea level.</summary>
    [DisallowMultipleComponent]
    public sealed partial class CityAltitudeVisibility : MonoBehaviour
    {
        public const float LowlandTop = 5f;
        public const float SummitDensity = .025f;
        public const float SummitFarPlane = 110f;
        private static readonly int ValleyFogId = Shader.PropertyToID("_CityValleyFog");
        private static CityAltitudeVisibility current;
        private Camera sceneCamera;
        private CityFogField fogField;
        private DepthOfField sceneDepth;
        private float lowFocusStart;
        private float lowFocusEnd;
        private float lastHeightFactor = -1f;

        public float Density { get; private set; } = RuntimeSceneSetup.CityFogDensity;

        internal void Initialize(Camera camera, CityFogField field, DepthOfField depth)
        {
            sceneCamera = camera;
            fogField = field;
            sceneDepth = depth;
            if (sceneDepth != null)
            {
                lowFocusStart = sceneDepth.gaussianStart.value;
                lowFocusEnd = sceneDepth.gaussianEnd.value;
            }
            Refresh(true);
        }

        public static float HeightFactor(float playerY) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LowlandTop, CityHillsideStreetRoute.SummitTop, playerY));

        public void Refresh(bool force = false)
        {
            if (!isActiveAndEnabled || fogField == null || sceneCamera == null) return;
            float height = Mathf.Clamp(fogField.Player.position.y, LowlandTop, CityHillsideStreetRoute.SummitTop);
            float factor = HeightFactor(height);
            if (!force && Mathf.Abs(factor - lastHeightFactor) < .0001f) return;
            lastHeightFactor = factor;
            current = this;
            Density = Mathf.Lerp(RuntimeSceneSetup.CityFogDensity, SummitDensity, factor);
            RenderSettings.fogDensity = Density;
            sceneCamera.farClipPlane = Mathf.Lerp(RuntimeSceneSetup.CityFarClipPlane, SummitFarPlane, factor);
            fogField.SetLayerOpacity(Mathf.Lerp(1f, .18f, factor));
            CityMountainSurfaceAppearance.SetCityVisibility(Density, sceneCamera.farClipPlane);

            // Mean extra density along a descending sightline. Ground below the
            // quay retains the lowland limit. A level sightline has no extra fog.
            float densityGradient = (RuntimeSceneSetup.CityFogDensity - Density) /
                (2f * Density * Mathf.Max(height - LowlandTop, .01f));
            Shader.SetGlobalVector(ValleyFogId, new Vector4(height, densityGradient, LowlandTop, factor));
            if (sceneDepth != null)
            {
                sceneDepth.gaussianStart.value = Mathf.Lerp(lowFocusStart, 35f, factor);
                sceneDepth.gaussianEnd.value = Mathf.Lerp(lowFocusEnd, 90f, factor);
            }
        }

        private void LateUpdate() => Refresh();

        private void OnDisable()
        {
            lastHeightFactor = -1f;
            if (current != this) return;
            current = null;
            Shader.SetGlobalVector(ValleyFogId, Vector4.zero);
            CityMountainSurfaceAppearance.SetCityVisibility(RuntimeSceneSetup.CityFogDensity, RuntimeSceneSetup.CityFarClipPlane);
            if (sceneCamera != null) sceneCamera.farClipPlane = RuntimeSceneSetup.CityFarClipPlane;
            if (gameObject.scene == SceneManager.GetActiveScene()) RenderSettings.fogDensity = RuntimeSceneSetup.CityFogDensity;
            if (fogField != null) fogField.SetLayerOpacity(1f);
            if (sceneDepth != null)
            {
                sceneDepth.gaussianStart.value = lowFocusStart;
                sceneDepth.gaussianEnd.value = lowFocusEnd;
            }
        }
    }
}
