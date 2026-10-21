using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>Native URP SSR, restricted to the City's roads and warehouse presentation.</summary>
    public sealed class SceneScreenSpaceReflectionFeature : ScreenSpaceReflectionRendererFeature
    {
        public const float ReflectionStrength = .35f;
        public const float MaximumRayLength = 20f;
        public const int MaximumRaySteps = 32;
        public const int HistoryWarmupFrames = 2;
        private static readonly int ReadyId = Shader.PropertyToID("_SceneScreenSpaceReflectionReady");
        private Camera historyCamera;
        private Vector3 lastPosition;
        private Quaternion lastRotation;
        private RenderTexture lastTarget;
        private float lastFieldOfView;
        private int lastWidth, lastHeight, lastFrame = -1, warmupUntil;
        private uint lastSourceState;
        private ReadyPass readyPass;
        private SceneScreenSpaceReflectionScope materialScope;

        public bool LastCameraEligible { get; private set; }
        public bool LastCameraReady { get; private set; }
        public Camera LastCamera { get; private set; }
        // Focused A/B diagnostics; cubemap reflections and the saved preference stay on.
        public bool DebugDisableTracing { get; set; }

        public override void Create()
        {
            afterOpaque = false;
            readyPass = new ReadyPass { renderPassEvent = RenderPassEvent.BeforeRendering };
            ResetHistory();
            base.Create();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            Camera camera = cameraData.camera;
            ScreenSpaceReflectionVolumeSettings settings = VolumeManager.instance.stack
                .GetComponent<ScreenSpaceReflectionVolumeSettings>();
            LastCamera = camera;
            LastCameraEligible = IsEligible(camera, cameraData.resolveFinalTarget,
                out SceneLightingPresentation presentation, out uint sourceState);
            LastCameraEligible &= !DebugDisableTracing;
            LastCameraReady = false;
            if (LastCameraEligible)
            {
                if (materialScope == null || materialScope.Owner != presentation)
                {
                    if (materialScope != null) materialScope.SetRequested(false);
                    materialScope = presentation.GetComponent<SceneScreenSpaceReflectionScope>();
                    if (materialScope == null)
                        materialScope = presentation.gameObject.AddComponent<SceneScreenSpaceReflectionScope>();
                    materialScope.Initialize(presentation);
                }
                materialScope.SetRequested(true);
                var descriptor = cameraData.cameraTargetDescriptor;
                bool discontinuity = historyCamera != camera || lastFrame < Time.frameCount - 1 ||
                    lastWidth != descriptor.width || lastHeight != descriptor.height ||
                    lastTarget != camera.targetTexture || lastSourceState != sourceState ||
                    (camera.transform.position - lastPosition).sqrMagnitude > 4f ||
                    Quaternion.Angle(camera.transform.rotation, lastRotation) > 35f ||
                    Mathf.Abs(camera.fieldOfView - lastFieldOfView) > 3f;
                if (discontinuity) warmupUntil = Time.frameCount + HistoryWarmupFrames;
                LastCameraReady = Time.frameCount >= warmupUntil;
                historyCamera = camera;
                lastFrame = Time.frameCount;
                lastWidth = descriptor.width; lastHeight = descriptor.height;
                lastTarget = camera.targetTexture; lastSourceState = sourceState;
                lastPosition = camera.transform.position; lastRotation = camera.transform.rotation;
                lastFieldOfView = camera.fieldOfView;
                Configure(settings, LastCameraReady ? ReflectionStrength : 0f);
            }
            else
            {
                // Probe/preview renders may interrupt the game camera within a frame.
                // Clear this camera's contribution without discarding the game's history.
                settings.mode.value = ScreenSpaceReflectionVolumeSettings.ReflectionMode.Disabled;
                if (camera == historyCamera) ResetHistory();
            }
            readyPass.Ready = LastCameraReady;
            renderer.EnqueuePass(readyPass);
            if (LastCameraEligible) base.AddRenderPasses(renderer, ref renderingData);
        }

        private static bool IsEligible(Camera camera, bool finalTarget,
            out SceneLightingPresentation presentation, out uint sourceState)
        {
            presentation = null;
            sourceState = 0;
            if (camera == null || !finalTarget || camera.cameraType != CameraType.Game ||
                camera.orthographic || camera.TryGetComponent(out ScenePresentationExclusion _) ||
                !GraphicsEffectsSettings.CleanReflectionsEnabled) return false;
            presentation = SceneLightingPresentation.Resolve(camera);
            if (presentation is CityLightingPresentation city) return city.Root != null;
            if (!(presentation is WarehouseCleanPresentation warehouse) || warehouse.IsBlackout) return false;
            // Abrupt source removal must not retain the previous lit floor in history.
            // Continuous brightness changes retain native motion reprojection.
            var lights = warehouse.LightSources;
            for (int i = 0; i < lights.Count && i < 24; i++)
            {
                Light light = lights[i];
                if (light != null && light.isActiveAndEnabled && light.intensity > .001f)
                    sourceState |= 1u << i;
            }
            return sourceState != 0;
        }

        private static void Configure(ScreenSpaceReflectionVolumeSettings settings, float strength)
        {
            settings.mode.value = ScreenSpaceReflectionVolumeSettings.ReflectionMode.OpaquesOnly;
            settings.reflectionStrength.value = strength;
            settings.resolution.value = ScreenSpaceReflectionVolumeSettings.Resolution.Half;
            settings.upscalingMethod.value = ScreenSpaceReflectionVolumeSettings.UpscalingMethod.Bilateral;
            settings.marchingMethod.value = ScreenSpaceReflectionVolumeSettings.MarchingMethod.Linear;
            settings.maxRayLength.value = MaximumRayLength;
            settings.maxRaySteps.value = MaximumRaySteps;
            settings.hitRefinementSteps.value = 5;
            settings.objectThickness.value = .325f;
            settings.finalThicknessMultiplier.value = .05f;
            settings.rayLengthFade.value = 2f;
            settings.minimumSmoothness.value = .2f;
            settings.smoothnessFadeStart.value = .45f;
            settings.screenEdgeFadeDistance.value = .2f;
            settings.roughnessFilter.value = ScreenSpaceReflectionVolumeSettings.RoughReflectionsQuality.GaussianBlur;
            settings.reflectSky.value = false;
            settings.temporalFiltering.value = false;
            settings.clampReflectedColor.value = true;
            settings.maxColorValue.value = 8f;
        }

        public void ResetHistory()
        {
            if (materialScope != null) materialScope.SetRequested(false);
            historyCamera = null;
            lastTarget = null;
            lastFrame = -1;
            LastCameraReady = false;
        }

        protected override void Dispose(bool disposing)
        {
            ResetHistory();
            Shader.SetGlobalFloat(ReadyId, 0f);
            base.Dispose(disposing);
        }

        private sealed class ReadyPass : ScriptableRenderPass
        {
            public bool Ready;
            private sealed class PassData { public float Ready; }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                using IUnsafeRenderGraphBuilder builder = graph.AddUnsafePass(
                    "Scene Screen Space Reflection Scope", out PassData data);
                data.Ready = Ready ? 1f : 0f;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData pass, UnsafeGraphContext context) =>
                    context.cmd.SetGlobalFloat(ReadyId, pass.Ready));
            }
        }
    }
}
