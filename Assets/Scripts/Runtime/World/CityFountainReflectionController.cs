using System;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// The fountain's Morrowind mirror: a small environment cubemap
    /// rendered from a point over the basin and bound to the shared
    /// basin water material, where the shader reflects it about the
    /// rippled normal.
    ///
    /// The cube is not re-rendered per frame - an environment map has
    /// no parallax to keep fresh - only when the light itself moves:
    /// the first Update after the city builds, and again whenever the
    /// night factor has drifted far enough that the lamps and the sky
    /// in the mirror would be caught lying. Six small faces of a
    /// 48 m, fog-eaten scene, a handful of times per day cycle.
    ///
    /// The probe stands off the statue's axis on purpose: a camera
    /// inside the statue column would cull its own back faces and
    /// hand the water a mirror with no statue in it.
    /// </summary>
    public sealed class CityFountainReflectionController :
        MonoBehaviour
    {
        private const int Resolution = 128;
        private const float NightRerenderDelta = 0.12f;
        private static readonly ProfilerMarker MirrorMarker =
            new ProfilerMarker("BarPromenade.WaterReflectionCube");

        private static readonly int ReflectionCubeId =
            Shader.PropertyToID("_ReflectionCube");

        private RenderTexture cubemap;
        private Camera reflectionCamera;
        private Material target;
        private float lastRenderedNight = float.MinValue;

        public void Initialize(Material material)
        {
            target = material != null
                ? material
                : throw new ArgumentNullException(nameof(material));

            cubemap = new RenderTexture(
                Resolution,
                Resolution,
                16)
            {
                name = "Fountain Reflection Cubemap",
                dimension = TextureDimension.Cube,
                hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false
            };

            var cameraObject = new GameObject(
                "Fountain Reflection Camera");
            cameraObject.transform.SetParent(transform, false);
            reflectionCamera = cameraObject.AddComponent<Camera>();
            // Six cube faces are six different projections, so a
            // screen-space snap would round each face onto its own grid
            // and seam the mirror along every cube edge.
            cameraObject.AddComponent<Rendering.Ps1VertexJitterExclusion>();
            cameraObject.AddComponent<ExteriorCloudCaptureCamera>();
            reflectionCamera.enabled = false;
            reflectionCamera.nearClipPlane =
                RuntimeSceneSetup.GameplayNearClipPlane;
            reflectionCamera.farClipPlane =
                RuntimeSceneSetup.CityFarClipPlane;
            reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectionCamera.backgroundColor =
                RuntimeSceneSetup.CityFogColor;
            UniversalAdditionalCameraData cameraData =
                reflectionCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            target.SetTexture(ReflectionCubeId, cubemap);
        }

        private void Update()
        {
            // A staged city can already contain water before its buildings
            // exist. Keep the first snapshot pending until construction ends.
            if (AreaTravelService.IsComposing)
            {
                return;
            }

            float night = CityWaterResources.NightFactor;
            if (Mathf.Abs(night - lastRenderedNight) <
                NightRerenderDelta)
            {
                return;
            }

            RenderMirror();
        }

        /// <summary>
        /// Renders the six faces now. Update calls this on lighting
        /// drift; edit-mode tooling may call it directly, because a
        /// disabled camera never renders on its own.
        /// </summary>
        public void RenderMirror()
        {
            if (reflectionCamera == null || cubemap == null)
            {
                return;
            }

            // Batch-mode PlayMode tests use the Null graphics device. URP
            // cannot allocate the cubemap render targets there, and asking
            // it to render anyway can crash the native render loop before
            // the test runner has a chance to report a result.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                return;
            }

            lastRenderedNight = CityWaterResources.NightFactor;
            using (MirrorMarker.Auto())
            using (RuntimePerformanceCapture.MeasureReflection())
            {
                reflectionCamera.RenderToCubemap(cubemap);
            }
        }

        private void OnDestroy()
        {
            if (target != null)
            {
                target.SetTexture(ReflectionCubeId, null);
            }

            if (cubemap != null)
            {
                cubemap.Release();
                UnityEngine.Object.DestroyImmediate(cubemap);
                cubemap = null;
            }
        }
    }
}
