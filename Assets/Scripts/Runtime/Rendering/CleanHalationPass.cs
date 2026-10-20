using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>Bounded source-masked optical diffusion, after the shared lens and before SMAA/UI.</summary>
    internal sealed class CleanHalationPass : IDisposable
    {
        private const int MaximumSources = 24;
        private static readonly int SourcesId = Shader.PropertyToID("_CleanLightSources");
        private static readonly int StrengthsId = Shader.PropertyToID("_CleanLightSourceStrengths");
        private static readonly int SourceCountId = Shader.PropertyToID("_CleanLightSourceCount");
        private static readonly int DepthId = Shader.PropertyToID("_CleanSceneDepth");
        private static readonly int GlowId = Shader.PropertyToID("_CleanHalationTexture");
        private static readonly int DirectionId = Shader.PropertyToID("_CleanBlurDirection");
        private readonly Vector4[] sources = new Vector4[MaximumSources];
        private readonly float[] strengths = new float[MaximumSources];
        private readonly float[] distances = new float[MaximumSources];
        private Material material;
        private int sourceCount;

        private sealed class PassData
        {
            internal Material Material;
            internal TextureHandle Source, Depth, Glow;
            internal Vector4 Direction;
            internal int Pass;
        }

        private sealed class MaskData
        {
            internal Material Material;
            internal TextureHandle Source, Depth, Destination;
            internal UniversalCameraData CameraData;
            internal Matrix4x4 View, Projection;
            // RenderGraph pools pass data; these fixed arrays are allocated only
            // when that pool grows, then reused without per-frame array creation.
            internal readonly Vector4[] WorldSources = new Vector4[MaximumSources];
            internal readonly Vector4[] ProjectedSources = new Vector4[MaximumSources];
            internal readonly float[] Strengths = new float[MaximumSources];
            internal int Count;
        }

        internal bool Setup(Camera camera, SceneLightingPresentation presentation)
        {
            sourceCount = 0;
            if (camera == null || presentation == null || !presentation.IsForCamera(camera)) return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/CleanHalation");
                if (shader == null || !shader.isSupported) return false;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            float scale = 1f / (2f * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f));
            Vector3 cameraPosition = camera.transform.position;
            int candidateCount = presentation.HalationSourceCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!presentation.TryGetHalationSource(i, out SceneHalationSource source) ||
                    !(source.Strength > 0f) || !(source.Radius > 0f)) continue;
                Vector3 viewport = camera.WorldToViewportPoint(source.Position);
                if (viewport.z <= camera.nearClipPlane || viewport.x < -.05f || viewport.x > 1.05f ||
                    viewport.y < -.05f || viewport.y > 1.05f) continue;
                float projectedRadius = camera.orthographic
                    ? source.Radius / (2f * Mathf.Max(.01f, camera.orthographicSize))
                    : source.Radius * scale / viewport.z;
                float radius = Mathf.Clamp(projectedRadius, .003f, .025f);
                float distance = (source.Position - cameraPosition).sqrMagnitude;
                // Keep the nearest visible candidates in a fixed array. Strict
                // comparison retains provider order for equal distances.
                int slot = sourceCount;
                while (slot > 0 && distance < distances[slot - 1]) slot--;
                if (slot >= MaximumSources) continue;
                int last = Mathf.Min(sourceCount, MaximumSources - 1);
                for (int next = last; next > slot; next--)
                {
                    sources[next] = sources[next - 1];
                    strengths[next] = strengths[next - 1];
                    distances[next] = distances[next - 1];
                }
                // Preserve world sources until the pass snapshots the actual
                // per-camera GPU projection and target's texture Y convention.
                Vector3 position = source.Position;
                sources[slot] = new Vector4(position.x, position.y, position.z, radius);
                strengths[slot] = Mathf.Clamp01(source.Strength);
                distances[slot] = distance;
                sourceCount = Mathf.Min(sourceCount + 1, MaximumSources);
            }
            return sourceCount > 0;
        }

        internal TextureHandle BuildMask(RenderGraph graph, TextureHandle source, TextureHandle depth,
            UniversalCameraData cameraData)
        {
            if (sourceCount == 0 || !depth.IsValid() || cameraData == null) return default;
            TextureDesc descriptor = graph.GetTextureDesc(source);
            Vector2Int size = descriptor.CalculateFinalDimensions();
            float scale = Mathf.Min(.25f, 512f / Mathf.Max(1, size.x), 288f / Mathf.Max(1, size.y));
            descriptor.sizeMode = TextureSizeMode.Explicit;
            descriptor.width = Mathf.Max(1, Mathf.CeilToInt(size.x * scale));
            descriptor.height = Mathf.Max(1, Mathf.CeilToInt(size.y * scale));
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = false;
            descriptor.name = "Clean Visible Light Sources";
            TextureHandle mask = graph.CreateTexture(descriptor);
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "Clean Light Source Mask", out MaskData data))
            {
                data.Material = material;
                data.Source = source;
                data.Depth = depth;
                data.Destination = mask;
                data.CameraData = cameraData;
                data.View = cameraData.GetViewMatrix();
                data.Projection = cameraData.GetProjectionMatrix();
                data.Count = sourceCount;
                Array.Copy(sources, data.WorldSources, sourceCount);
                Array.Copy(strengths, data.Strengths, sourceCount);
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                {
                    RTHandle output = pass.Destination;
                    bool flipped = pass.CameraData.IsRenderTargetProjectionMatrixFlipped(output);
                    Matrix4x4 worldToClip = GL.GetGPUProjectionMatrix(pass.Projection, flipped) * pass.View;
                    for (int i = 0; i < pass.Count; i++)
                    {
                        Vector4 sourcePoint = pass.WorldSources[i];
                        Vector4 point = new Vector4(sourcePoint.x, sourcePoint.y, sourcePoint.z, 1f);
                        Vector4 clip = worldToClip * point;
                        float inverseW = 1f / Mathf.Max(clip.w, .0001f);
                        float x = clip.x * inverseW * .5f + .5f;
                        float y = clip.y * inverseW * .5f + .5f;
                        if (SystemInfo.graphicsUVStartsAtTop) y = 1f - y;
                        float sourceDepth = -(pass.View * point).z;
                        pass.ProjectedSources[i] = new Vector4(x, y, sourceDepth, sourcePoint.w * sourcePoint.w);
                    }
                    pass.Material.SetVectorArray(SourcesId, pass.ProjectedSources);
                    pass.Material.SetFloatArray(StrengthsId, pass.Strengths);
                    pass.Material.SetInt(SourceCountId, pass.Count);
                    pass.Material.SetTexture(DepthId, pass.Depth);
                    Blitter.BlitTexture(context.cmd, (RTHandle)pass.Source,
                        new Vector4(1, 1, 0, 0), pass.Material, 0);
                });
            }
            return mask;
        }

        internal TextureHandle Composite(RenderGraph graph, TextureHandle source, TextureHandle mask)
        {
            TextureDesc descriptor = graph.GetTextureDesc(mask);
            Vector2Int size = descriptor.CalculateFinalDimensions();
            descriptor.name = "Clean Halation Horizontal";
            TextureHandle horizontal = graph.CreateTexture(descriptor);
            descriptor.name = "Clean Halation Vertical";
            TextureHandle blurred = graph.CreateTexture(descriptor);
            Record(graph, "Clean Halation Horizontal", mask, horizontal, default, default,
                new Vector4(1f / size.x, 0, 0, 0), 1);
            Record(graph, "Clean Halation Vertical", horizontal, blurred, default, default,
                new Vector4(0, 1f / size.y, 0, 0), 1);
            descriptor = graph.GetTextureDesc(source);
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = false;
            descriptor.name = "Clean Halation Composite";
            TextureHandle destination = graph.CreateTexture(descriptor);
            Record(graph, "Clean Halation Composite", source, destination, default, blurred, Vector4.zero, 2);
            return destination;
        }

        private void Record(RenderGraph graph, string name, TextureHandle source, TextureHandle destination,
            TextureHandle depth, TextureHandle glow, Vector4 direction, int shaderPass)
        {
            using IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(name, out PassData data);
            data.Material = material;
            data.Source = source;
            data.Depth = depth;
            data.Glow = glow;
            data.Direction = direction;
            data.Pass = shaderPass;
            builder.UseTexture(source, AccessFlags.Read);
            if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
            if (glow.IsValid()) builder.UseTexture(glow, AccessFlags.Read);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                if (pass.Glow.IsValid()) pass.Material.SetTexture(GlowId, pass.Glow);
                pass.Material.SetVector(DirectionId, pass.Direction);
                Blitter.BlitTexture(context.cmd, (RTHandle)pass.Source,
                    new Vector4(1, 1, 0, 0), pass.Material, pass.Pass);
            });
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
            sourceCount = 0;
        }
    }
}
