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

            Vector2Int resolution =
                presentationSettings.GetInternalResolution(
                    cameraData.cameraTargetDescriptor.width,
                    cameraData.cameraTargetDescriptor.height);
            pass.Setup(
                compositeMaterial,
                resolution,
                presentationSettings.QuantizationStrength);
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

            private Material material;
            private Vector2Int resolution;

            public Ps1CompositePass()
            {
                profilingSampler = new ProfilingSampler("PS1 Composite");
            }

            public void Setup(
                Material composite,
                Vector2Int internalResolution,
                float quantizationStrength)
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
                    QuantizationStrengthId,
                    Mathf.Clamp01(quantizationStrength));
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
