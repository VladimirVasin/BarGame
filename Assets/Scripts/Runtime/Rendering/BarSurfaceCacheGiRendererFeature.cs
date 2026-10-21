#if SURFACE_CACHE
using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>Explicit Bar-only pilot around the pinned native preview provider.</summary>
    public sealed class BarSurfaceCacheGiRendererFeature : SurfaceCacheGIRendererFeature
    {
        public bool ResourcesReady { get; private set; }
        public string FailureReason { get; private set; } = string.Empty;
        public int EnqueuedCameraFrames { get; private set; }

        public override void Create()
        {
            base.Dispose(true);
            ResourcesReady = false;
            if (!isActive || BarSurfaceCacheGiPilot.Current == null || FailureReason.Length != 0) return;
            if (!SystemInfo.supportsComputeShaders)
            {
                FailureReason = "The current graphics device does not support compute shaders.";
                return;
            }
#if UNITY_EDITOR
            if (UnityEditor.PlayerSettings.GetStaticBatchingForPlatform(UnityEditor.EditorUserBuildSettings.activeBuildTarget))
            {
                FailureReason = k_StaticBatchingErrorMesssage;
                return;
            }
#endif
            try
            {
                base.Create();
                ResourcesReady = true;
            }
            catch (Exception exception)
            {
                FailureReason = exception.GetType().Name + ": " + exception.Message;
                base.Dispose(true);
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            BarSurfaceCacheGiPilot pilot = BarSurfaceCacheGiPilot.Current;
            Camera camera = renderingData.cameraData.camera;
            if (pilot == null || !GraphicsEffectsSettings.CleanBounceEnabled)
            {
                pilot?.ReleaseCamera(camera);
                ReleasePilotResources();
                return;
            }
            // Cubemap/preview renders must not discard the gameplay history or
            // force a native allocation on the next gameplay camera.
            if (!pilot.IsForCamera(camera)) return;
            if (!ResourcesReady) Create();
            if (!ResourcesReady) { pilot.ReleaseCamera(camera); return; }
            base.AddRenderPasses(renderer, ref renderingData);
            pilot.PrepareForCamera(camera, this);
            EnqueuedCameraFrames++;
        }

        public void ReleasePilotResources()
        {
            if (!ResourcesReady) return;
            base.Dispose(true);
            ResourcesReady = false;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            ResourcesReady = false;
        }
    }
}
#endif
