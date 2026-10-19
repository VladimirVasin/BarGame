using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Spatial SMAA High over an already-composed live image. The owner keeps
    /// the film source separate and decides which cameras/styles enter here.
    /// </summary>
    internal sealed class CleanSmaaPass : IDisposable
    {
        private static readonly int MetricsId = Shader.PropertyToID("_Metrics");
        private static readonly int AreaId = Shader.PropertyToID("_AreaTexture");
        private static readonly int SearchId = Shader.PropertyToID("_SearchTexture");
        private static readonly int BlendId = Shader.PropertyToID("_BlendTexture");
        private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
        private static readonly int StencilMaskId = Shader.PropertyToID("_StencilMask");
        private const int StencilBit = 64;

        private Material material;
        private Texture2D areaTexture;
        private Texture2D searchTexture;
        private bool disposed;

        internal bool IsAvailable => EnsureResources();
        internal int RecordCount { get; private set; }

        private sealed class PassData
        {
            internal Material Material;
            internal TextureHandle Source;
            internal TextureHandle Blend;
            internal Vector4 Metrics;
            internal int ShaderPass;
        }

        internal TextureHandle Record(RenderGraph graph, TextureHandle source)
        {
            if (!source.IsValid() || !EnsureResources()) return source;

            TextureDesc descriptor = graph.GetTextureDesc(source);
            Vector2Int size = descriptor.CalculateFinalDimensions();
            if (size.x <= 0 || size.y <= 0) return source;
            Vector4 metrics = new Vector4(1f / size.x, 1f / size.y, size.x, size.y);

            // RenderGraph owns these per-camera scratch targets. There is no
            // history, retained framebuffer or change to the scene depth.
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.enableRandomWrite = false;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.clearBuffer = false;
            descriptor.name = "Clean SMAA Image";
            TextureHandle destination = graph.CreateTexture(descriptor);

            TextureDesc edgesDescriptor = descriptor;
            edgesDescriptor.format = GraphicsFormat.R8G8B8A8_UNorm;
            edgesDescriptor.clearBuffer = true;
            edgesDescriptor.clearColor = Color.clear;
            edgesDescriptor.name = "Clean SMAA Edges";
            TextureHandle edges = graph.CreateTexture(edgesDescriptor);

            // Edge detection discards non-edges, so the private stencil and
            // both sparse colour targets must start clear on every render.
            TextureDesc stencilDescriptor = edgesDescriptor;
            stencilDescriptor.format = GraphicsFormatUtility.GetDepthStencilFormat(24);
            stencilDescriptor.name = "Clean SMAA Edge Stencil";
            TextureHandle stencil = graph.CreateTexture(stencilDescriptor);

            TextureDesc weightsDescriptor = edgesDescriptor;
            weightsDescriptor.filterMode = FilterMode.Point;
            weightsDescriptor.name = "Clean SMAA Blend Weights";
            TextureHandle weights = graph.CreateTexture(weightsDescriptor);

            RecordStage(graph, "Clean SMAA Edge Detection", source, edges, stencil,
                default, metrics, 0);
            RecordStage(graph, "Clean SMAA Blend Weights", edges, weights, stencil,
                default, metrics, 1);
            RecordStage(graph, "Clean SMAA Neighborhood Blend", source, destination,
                default, weights, metrics, 2);
            RecordCount++;
            return destination;
        }

        private void RecordStage(RenderGraph graph, string name, TextureHandle source,
            TextureHandle destination, TextureHandle stencil, TextureHandle blend,
            Vector4 metrics, int shaderPass)
        {
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       name, out PassData data))
            {
                data.Material = material;
                data.Source = source;
                data.Blend = blend;
                data.Metrics = metrics;
                data.ShaderPass = shaderPass;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0,
                    shaderPass == 2 ? AccessFlags.WriteAll : AccessFlags.Write);
                if (stencil.IsValid())
                    builder.SetRenderAttachmentDepth(stencil,
                        shaderPass == 0 ? AccessFlags.Write : AccessFlags.Read);
                if (blend.IsValid()) builder.UseTexture(blend, AccessFlags.Read);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) =>
                {
                    // Set camera-dependent metrics at execution, rather than
                    // leaving another camera's pending graph to overwrite them.
                    passData.Material.SetVector(MetricsId, passData.Metrics);
                    if (passData.Blend.IsValid())
                        passData.Material.SetTexture(BlendId, passData.Blend);
                    RTHandle texture = passData.Source;
                    Vector2 scale = texture.useScaling
                        ? new Vector2(texture.rtHandleProperties.rtHandleScale.x,
                            texture.rtHandleProperties.rtHandleScale.y)
                        : Vector2.one;
                    Blitter.BlitTexture(context.cmd, texture, scale,
                        passData.Material, passData.ShaderPass);
                });
            }
        }

        private bool EnsureResources()
        {
            if (disposed) return false;
            if (material != null) return true;
            // Create can precede active-pipeline installation. Retry lazily
            // until its public packaged post-process resources are available;
            // after success there are no recurring lookups or loads.
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
                return false;
            foreach (ScriptableRendererData renderer in pipeline.rendererDataList)
            {
                if (!(renderer is UniversalRendererData universal)) continue;
                PostProcessData resources = universal.postProcessData;
                Shader shader = resources?.shaders?.subpixelMorphologicalAntialiasingPS;
                Texture2D area = resources?.textures?.smaaAreaTex;
                Texture2D search = resources?.textures?.smaaSearchTex;
                if (shader == null || !shader.isSupported || area == null || search == null) continue;

                areaTexture = area;
                searchTexture = search;
                material = CoreUtils.CreateEngineMaterial(shader);
                material.name = "Clean SMAA High";
                material.SetTexture(AreaId, areaTexture);
                material.SetTexture(SearchId, searchTexture);
                material.SetFloat(StencilRefId, StencilBit);
                material.SetFloat(StencilMaskId, StencilBit);
                material.EnableKeyword("_SMAA_PRESET_HIGH");
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
            areaTexture = null;
            searchTexture = null;
            disposed = true;
        }
    }
}
