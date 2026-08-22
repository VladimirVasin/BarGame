using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    public sealed class Ps1CompositeRendererFeature : ScriptableRendererFeature
    {
        private const string ProfileResource = "Rendering/Ps1PresentationProfile";
        private const string MaterialResource = "Materials/Ps1Composite";

        [SerializeField] private Ps1PresentationSettings presentationSettings;
        [SerializeField] private Material compositeMaterial;
        [SerializeField] private RenderPassEvent injectionPoint =
            RenderPassEvent.AfterRenderingPostProcessing;

        private Ps1CompositePass pass;
        private bool loggedMissingResources;

        public Ps1PresentationSettings PresentationSettings =>
            presentationSettings;

        public Material CompositeMaterial => compositeMaterial;

        public override void Create()
        {
            ResolveResources();

            pass?.Dispose();
            pass = new Ps1CompositePass
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            if (cameraData.cameraType != CameraType.Game ||
                !cameraData.resolveFinalTarget ||
                presentationSettings == null ||
                !presentationSettings.EffectEnabled ||
                compositeMaterial == null)
            {
                return;
            }

            int outputWidth =
                cameraData.cameraTargetDescriptor.width;
            int outputHeight =
                cameraData.cameraTargetDescriptor.height;
            // The 4:3 mode crops the widescreen frame to a centered
            // 4:3 window (the exact view of a 4:3 camera with the same
            // vertical FOV) and pillarboxes the upscale. On displays
            // already at or narrower than 4:3 the fraction stays 1.
            float aspectFraction = 1f;
            int effectiveWidth = outputWidth;
            if (GraphicsEffectsSettings.AspectRatio43Enabled)
            {
                float croppedWidth = outputHeight * (4f / 3f);
                if (croppedWidth < outputWidth)
                {
                    aspectFraction = croppedWidth / outputWidth;
                    effectiveWidth =
                        Mathf.Max(1, Mathf.RoundToInt(croppedWidth));
                }
            }

            Vector2Int resolution =
                presentationSettings.GetInternalResolution(
                    effectiveWidth,
                    outputHeight);
            pass.Setup(
                compositeMaterial,
                resolution,
                aspectFraction,
                presentationSettings.QuantizationStrength,
                GraphicsEffectsSettings.DitherEnabled
                    ? presentationSettings.DitherStrength
                    : 0f,
                GraphicsEffectsSettings.ScanlinesEnabled
                    ? presentationSettings.ScanlineIntensity
                    : 0f,
                GraphicsEffectsSettings.RainOnLensEnabled
                    ? presentationSettings.RainLensStrength *
                      RainLensRenderState.Intensity
                    : 0f,
                Time.unscaledTime,
                IntoxicationRenderState.Current);
            renderer.EnqueuePass(pass);
        }

        public void SetConfiguration(
            Ps1PresentationSettings settings,
            Material material)
        {
            presentationSettings = settings;
            compositeMaterial = material;
            Create();
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
        }

        private void ResolveResources()
        {
            if (presentationSettings == null)
            {
                presentationSettings =
                    Resources.Load<Ps1PresentationSettings>(ProfileResource);
            }

            if (compositeMaterial == null)
            {
                compositeMaterial = Resources.Load<Material>(MaterialResource);
            }

            if (presentationSettings != null && compositeMaterial != null)
            {
                loggedMissingResources = false;
                return;
            }

            if (!loggedMissingResources)
            {
                Debug.LogWarning(
                    "PS1 composite is disabled because its shared Resources " +
                    "profile or material is missing.");
                loggedMissingResources = true;
            }
        }

        private sealed class Ps1CompositePass : ScriptableRenderPass
        {
            private const string LowResolutionName =
                "_BarPromenadePs1LowResolution";
            private static readonly int LowResolutionTexelSizeId =
                Shader.PropertyToID("_Ps1LowResolutionTexelSize");
            private static readonly int QuantizationStrengthId =
                Shader.PropertyToID("_Ps1QuantizationStrength");
            private static readonly int DitherStrengthId =
                Shader.PropertyToID("_Ps1DitherStrength");
            private static readonly int ScanlineIntensityId =
                Shader.PropertyToID("_Ps1ScanlineIntensity");
            private static readonly int RainLensId =
                Shader.PropertyToID("_Ps1RainLens");
            private static readonly int RainTimeId =
                Shader.PropertyToID("_Ps1RainTime");
            private static readonly int AspectFractionId =
                Shader.PropertyToID("_Ps1AspectFraction");
            private static readonly int IntoxicationVignetteId =
                Shader.PropertyToID("_IntoxicationVignette");
            private static readonly int IntoxicationGhostPixelsId =
                Shader.PropertyToID("_IntoxicationGhostPixels");
            private static readonly int IntoxicationWarpId =
                Shader.PropertyToID("_IntoxicationWarp");
            private static readonly int IntoxicationWarmthId =
                Shader.PropertyToID("_IntoxicationWarmth");
            private static readonly int IntoxicationExposurePulseId =
                Shader.PropertyToID("_IntoxicationExposurePulse");
            private static readonly int IntoxicationTimeId =
                Shader.PropertyToID("_IntoxicationTime");

            private Material material;
            private Vector2Int resolution;

            public Ps1CompositePass()
            {
                profilingSampler = new ProfilingSampler("PS1 Composite");
            }

            public void Setup(
                Material composite,
                Vector2Int internalResolution,
                float aspectFraction,
                float quantizationStrength,
                float ditherStrength,
                float scanlineIntensity,
                float rainLens,
                float rainTime,
                IntoxicationRenderParameters intoxication)
            {
                material = composite;
                resolution = internalResolution;
                material.SetVector(
                    LowResolutionTexelSizeId,
                    new Vector4(
                        1f / Mathf.Max(1, resolution.x),
                        1f / Mathf.Max(1, resolution.y),
                        resolution.x,
                        resolution.y));
                material.SetFloat(
                    AspectFractionId,
                    Mathf.Clamp(aspectFraction, 0.01f, 1f));
                material.SetFloat(
                    QuantizationStrengthId,
                    Mathf.Clamp01(quantizationStrength));
                material.SetFloat(
                    DitherStrengthId,
                    Mathf.Clamp01(ditherStrength));
                material.SetFloat(
                    ScanlineIntensityId,
                    Mathf.Clamp01(scanlineIntensity));
                material.SetFloat(
                    RainLensId,
                    Mathf.Clamp01(rainLens));
                material.SetFloat(
                    RainTimeId,
                    Mathf.Max(0f, rainTime));
                material.SetFloat(
                    IntoxicationVignetteId,
                    Mathf.Clamp01(
                        intoxication.VignetteStrength));
                material.SetFloat(
                    IntoxicationGhostPixelsId,
                    Mathf.Max(0f, intoxication.GhostPixels));
                material.SetFloat(
                    IntoxicationWarpId,
                    Mathf.Max(0f, intoxication.WarpStrength));
                material.SetFloat(
                    IntoxicationWarmthId,
                    Mathf.Clamp01(intoxication.Warmth));
                material.SetFloat(
                    IntoxicationExposurePulseId,
                    Mathf.Max(0f, intoxication.ExposurePulse));
                material.SetFloat(
                    IntoxicationTimeId,
                    Mathf.Max(0f, intoxication.AnimationTime));
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (material == null)
                {
                    return;
                }

                UniversalCameraData cameraData =
                    frameData.Get<UniversalCameraData>();
                if (cameraData.camera.cameraType != CameraType.Game)
                {
                    return;
                }

                UniversalResourceData resourceData =
                    frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid())
                {
                    return;
                }

                TextureDesc lowDescriptor = renderGraph.GetTextureDesc(source);
                lowDescriptor.sizeMode = TextureSizeMode.Explicit;
                lowDescriptor.width = resolution.x;
                lowDescriptor.height = resolution.y;
                lowDescriptor.depthBufferBits = DepthBits.None;
                lowDescriptor.msaaSamples = MSAASamples.None;
                lowDescriptor.filterMode = FilterMode.Point;
                lowDescriptor.wrapMode = TextureWrapMode.Clamp;
                lowDescriptor.useMipMap = false;
                lowDescriptor.autoGenerateMips = false;
                lowDescriptor.clearBuffer = false;
                lowDescriptor.name = LowResolutionName;
                TextureHandle lowResolution =
                    renderGraph.CreateTexture(lowDescriptor);

                TextureDesc outputDescriptor =
                    renderGraph.GetTextureDesc(source);
                outputDescriptor.depthBufferBits = DepthBits.None;
                outputDescriptor.msaaSamples = MSAASamples.None;
                outputDescriptor.filterMode = FilterMode.Point;
                outputDescriptor.wrapMode = TextureWrapMode.Clamp;
                outputDescriptor.clearBuffer = false;
                outputDescriptor.name = "_BarPromenadePs1Composite";
                TextureHandle destination =
                    renderGraph.CreateTexture(outputDescriptor);

                RenderGraphUtils.BlitMaterialParameters downsample =
                    new RenderGraphUtils.BlitMaterialParameters(
                        source,
                        lowResolution,
                        material,
                        0);
                renderGraph.AddBlitPass(
                    downsample,
                    "PS1 Downsample + RGB555");

                RenderGraphUtils.BlitMaterialParameters upscale =
                    new RenderGraphUtils.BlitMaterialParameters(
                        lowResolution,
                        destination,
                        material,
                        1);
                renderGraph.AddBlitPass(upscale, "PS1 Point Upscale");

                resourceData.cameraColor = destination;
            }

            public void Dispose()
            {
                material = null;
            }
        }
    }
}
