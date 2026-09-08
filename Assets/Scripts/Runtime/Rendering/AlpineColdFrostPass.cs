using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>The current camera's image frosts before the shared PS1 finish and overlay HUD.</summary>
    internal sealed class AlpineColdFrostPass : ScriptableRenderPass
    {
        private static readonly int WindowId = Shader.PropertyToID("_FrostWindow");
        private static readonly int MaskId = Shader.PropertyToID("_FrostMask");
        private static readonly int BlurStepId = Shader.PropertyToID("_FrostBlurStep");
        private static readonly int BlurTextureId = Shader.PropertyToID("_FrostBlurTexture");
        internal static bool DebugDisableDiffusion { get; set; }
        private Material material;
        private Texture2D mask;
        private float aspectFraction = 1f;

        private sealed class PassData
        {
            public Material Material;
            public TextureHandle Source;
            public TextureHandle Blur;
            public Vector4 Window;
        }

        private sealed class BlurPassData
        {
            public Material Material;
            public TextureHandle Source;
            public Vector4 Window;
            public Vector4 Step;
            public int ShaderPass;
        }

        public AlpineColdFrostPass()
        {
            ConfigureInput(ScriptableRenderPassInput.Color);
            requiresIntermediateTexture = true;
        }

        public bool IsNeeded(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game ||
                camera.TryGetComponent(out Ps1VertexJitterExclusion _) ||
                !AlpineColdExposure.IsVisible(camera) || AlpineColdExposure.FrostAmount <= 0f)
                return false;
            if (mask == null)
                mask = Resources.Load<Texture2D>("Textures/AlpineColdFrostMask");
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/AlpineColdFrost");
                if (shader == null || !shader.isSupported || mask == null)
                    return false;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            if (mask == null)
                return false;
            // A declared material texture property and the retained managed
            // reference survive Single-load unused-asset collection together.
            if (material.GetTexture(MaskId) != mask)
                material.SetTexture(MaskId, mask);
            return true;
        }

        public void Setup(float imageWidthFraction)
        {
            aspectFraction = Mathf.Clamp(imageWidthFraction, 0.01f, 1f);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (material == null || !IsNeeded(cameraData.camera))
                return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer)
                return;
            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid())
                return;

            TextureDesc descriptor = graph.GetTextureDesc(source);
            descriptor.name = "Alpine Cold Frost";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.clearBuffer = false;
            TextureHandle destination = graph.CreateTexture(descriptor);
            float imageAspect = cameraData.cameraTargetDescriptor.width * aspectFraction /
                Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            float amount = Mathf.Clamp01(AlpineColdExposure.FrostAmount);
            Vector4 window = new Vector4(amount, aspectFraction, imageAspect,
                DebugDisableDiffusion ? 0f : 1f);

            Vector2Int sourceSize = descriptor.CalculateFinalDimensions();
            TextureDesc blurDescriptor = descriptor;
            blurDescriptor.sizeMode = TextureSizeMode.Explicit;
            blurDescriptor.width = Mathf.Max(1, (sourceSize.x + 3) / 4);
            blurDescriptor.height = Mathf.Max(1, (sourceSize.y + 3) / 4);
            blurDescriptor.filterMode = FilterMode.Bilinear;
            blurDescriptor.wrapMode = TextureWrapMode.Clamp;
            blurDescriptor.name = "Alpine Frost Horizontal Diffusion";
            TextureHandle horizontal = graph.CreateTexture(blurDescriptor);
            blurDescriptor.name = "Alpine Frost Vertical Diffusion";
            TextureHandle blurred = graph.CreateTexture(blurDescriptor);

            // One sigma in full-frame UV, independent of the quarter-size
            // buffers. Thirteen half-sigma taps reach +/-97 px at 720p/full
            // frost. Crop conversion preserves the same radius in 4:3.
            float sigma = 0.045f * amount;
            RecordBlur(graph, source, horizontal, window,
                new Vector4(sigma * aspectFraction / Mathf.Max(0.1f, imageAspect), 0f, 0f, 0f),
                1, "Alpine Frost Horizontal Diffusion");
            RecordBlur(graph, horizontal, blurred, window,
                new Vector4(0f, sigma, 0f, 0f), 2, "Alpine Frost Vertical Diffusion");
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "Alpine Cold Frost", out PassData data))
            {
                data.Material = material;
                data.Source = source;
                data.Blur = blurred;
                data.Window = window;
                builder.UseTexture(source);
                builder.UseTexture(blurred);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    // Set per-camera data at execution, so another render
                    // cannot overwrite the shared material's pending values.
                    passData.Material.SetVector(WindowId, passData.Window);
                    passData.Material.SetTexture(BlurTextureId, passData.Blur);
                    Blitter.BlitTexture(context.cmd, (RTHandle)passData.Source,
                        new Vector4(1f, 1f, 0f, 0f), passData.Material, 0);
                });
            }
            resources.cameraColor = destination;
        }

        private void RecordBlur(RenderGraph graph, TextureHandle source, TextureHandle destination,
            Vector4 window, Vector4 step, int shaderPass, string passName)
        {
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       passName, out BlurPassData data))
            {
                data.Material = material;
                data.Source = source;
                data.Window = window;
                data.Step = step;
                data.ShaderPass = shaderPass;
                builder.UseTexture(source);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (BlurPassData passData, RasterGraphContext context) =>
                {
                    passData.Material.SetVector(WindowId, passData.Window);
                    passData.Material.SetVector(BlurStepId, passData.Step);
                    Blitter.BlitTexture(context.cmd, (RTHandle)passData.Source,
                        new Vector4(1f, 1f, 0f, 0f), passData.Material, passData.ShaderPass);
                });
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
            mask = null;
        }
    }
}
