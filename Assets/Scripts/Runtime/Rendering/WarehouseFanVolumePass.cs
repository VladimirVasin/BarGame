using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>One bounded column of lit air, using the real fan's existing shadow atlas.</summary>
    internal sealed class WarehouseFanVolumePass : ScriptableRenderPass, IDisposable
    {
        internal const int MaximumWidth = 320, MaximumHeight = 180, IntegrationSteps = 12;
        private const float HalfWidth = 5.5f, SourceHeight = 11.8f, TopClearance = .35f;
        private static readonly int DepthId = Shader.PropertyToID("_FanDepthTexture");
        private static readonly int VolumeId = Shader.PropertyToID("_FanVolumeTexture");
        private static readonly int VolumeTexelSizeId = Shader.PropertyToID("_FanVolumeTexelSize");
        private static readonly int ScreenBoundsId = Shader.PropertyToID("_FanScreenBounds");
        private static readonly int LightIndexId = Shader.PropertyToID("_FanLightIndex");
        private static readonly int PositionId = Shader.PropertyToID("_FanSourcePosition");
        private static readonly int MinimumId = Shader.PropertyToID("_FanVolumeMinimum");
        private static readonly int MaximumId = Shader.PropertyToID("_FanVolumeMaximum");
        private readonly Plane[] frustum = new Plane[6];
        private Material material;
        private Light fanLight;
#if UNITY_EDITOR
        internal static int DebugViewForTests { get; set; }
        // One-shot Editor evidence only; never inspect internal URP state in a hot loop.
        internal static Action<UniversalShadowData, int, Vector2Int> ShadowLayoutForTests { get; set; }
        private bool diagnosticVariantEnabled;
        private const string DiagnosticKeyword = "_FAN_VOLUME_DIAGNOSTICS";
        private static readonly int DebugViewId = Shader.PropertyToID("_FanDebugView");
#endif

        internal Vector2Int LastResolution { get; private set; }
        internal int LastLightIndex { get; private set; } = -1;

        private sealed class IntegrateData
        {
            public Material Material;
            public TextureHandle Depth;
            public int LightIndex;
            public Vector4 Position, Minimum, Maximum;
        }

        private sealed class ResolveData
        {
            public Material Material;
            public TextureHandle Destination, Depth, Volume;
            public UniversalCameraData CameraData;
            public Matrix4x4 View, Projection;
            public float NearClip;
            public Vector2Int Dimensions;
            public Vector4 TexelSize;
            public int LightIndex;
            public Vector4 Position, Minimum, Maximum;
#if UNITY_EDITOR
            public int DebugView;
#endif
        }

        public WarehouseFanVolumePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
            profilingSampler = new ProfilingSampler("Warehouse Fan Volume");
        }

        /// <summary>The scene owns the light; this pass never changes its power, pose or shadows.</summary>
        public bool Setup(Light source)
        {
            fanLight = source;
            if (!HasSource()) return false;
            if (material != null) return true;
            Shader shader = Resources.Load<Shader>("Shaders/WarehouseFanVolume");
            if (shader == null || !shader.isSupported) return false;
            material = CoreUtils.CreateEngineMaterial(shader);
            return true;
        }

        private bool HasSource() => fanLight != null && fanLight.isActiveAndEnabled &&
            fanLight.type == LightType.Spot && fanLight.intensity > 0f && fanLight.range > 0f;

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            LastLightIndex = -1;
            LastResolution = Vector2Int.zero;
            if (material == null || !HasSource()) return;
#if UNITY_EDITOR
            bool diagnostic = DebugViewForTests != 0;
            if (diagnostic != diagnosticVariantEnabled)
            {
                CoreUtils.SetKeyword(material, DiagnosticKeyword, diagnostic);
                diagnosticVariantEnabled = diagnostic;
            }
#endif
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            Camera camera = cameraData.camera;
            if (camera == null || camera.cameraType != CameraType.Game || !cameraData.resolveFinalTarget ||
                camera.TryGetComponent(out ScenePresentationExclusion _)) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.activeColorTexture;
            TextureHandle depth = resources.cameraDepthTexture;
            TextureHandle shadows = resources.additionalShadowsTexture;
            if (!source.IsValid() || !depth.IsValid() || !shadows.IsValid()) return;

            UniversalLightData lights = frameData.Get<UniversalLightData>();
            int index = FindLightIndex(lights, fanLight);
            if (index < 0) return;
#if UNITY_EDITOR
            if (ShadowLayoutForTests != null)
            {
                var observer = ShadowLayoutForTests;
                ShadowLayoutForTests = null;
                for (int visible = 0; visible < lights.visibleLights.Length; visible++)
                    if (lights.visibleLights[visible].light == fanLight)
                    {
                        observer(frameData.Get<UniversalShadowData>(), visible,
                            graph.GetTextureDesc(shadows).CalculateFinalDimensions());
                        break;
                    }
            }
#endif

            Vector3 position = fanLight.transform.position;
            // The warehouse is authored in fixed metres. Keep the volume in
            // its central 11 m square, below the lamp and above the hall floor.
            Vector3 minimum = position + new Vector3(-HalfWidth, -SourceHeight, -HalfWidth);
            Vector3 maximum = position + new Vector3(HalfWidth, -TopClearance, HalfWidth);
            Bounds bounds = new Bounds((minimum + maximum) * .5f, maximum - minimum);
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            if (!GeometryUtility.TestPlanesAABB(frustum, bounds)) return;

            TextureDesc descriptor = graph.GetTextureDesc(source);
            Vector2Int size = descriptor.CalculateFinalDimensions();
            float scale = Mathf.Min(1f, Mathf.Min(MaximumWidth / (float)Mathf.Max(1, size.x),
                MaximumHeight / (float)Mathf.Max(1, size.y)));
            int width = Mathf.Max(1, Mathf.FloorToInt(size.x * scale));
            int height = Mathf.Max(1, Mathf.FloorToInt(size.y * scale));
            TextureDesc volumeDescriptor = descriptor;
            volumeDescriptor.name = "Warehouse Fan Lit Air";
            volumeDescriptor.sizeMode = TextureSizeMode.Explicit;
            volumeDescriptor.width = width;
            volumeDescriptor.height = height;
            volumeDescriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            volumeDescriptor.depthBufferBits = DepthBits.None;
            volumeDescriptor.msaaSamples = MSAASamples.None;
            volumeDescriptor.bindTextureMS = false;
            volumeDescriptor.useDynamicScale = false;
            volumeDescriptor.useDynamicScaleExplicit = false;
            volumeDescriptor.filterMode = FilterMode.Point;
            volumeDescriptor.wrapMode = TextureWrapMode.Clamp;
            volumeDescriptor.clearBuffer = false;
            TextureHandle volume = graph.CreateTexture(volumeDescriptor);

            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "Warehouse Fan Volume Integration", out IntegrateData data))
            {
                data.Material = material;
                data.Depth = depth;
                data.LightIndex = index;
                data.Position = new Vector4(position.x, position.y, position.z, 0f);
                data.Minimum = new Vector4(minimum.x, minimum.y, minimum.z, 0f);
                data.Maximum = new Vector4(maximum.x, maximum.y, maximum.z, 0f);
                builder.UseTexture(depth);
                // This is the actual URP atlas, not a second shadow render.
                // Its producer publishes the matching light/shadow constants.
                builder.UseTexture(shadows);
                builder.SetRenderAttachment(volume, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (IntegrateData pass, RasterGraphContext context) =>
                {
                    pass.Material.SetTexture(DepthId, pass.Depth);
                    pass.Material.SetInteger(LightIndexId, pass.LightIndex);
                    pass.Material.SetVector(PositionId, pass.Position);
                    pass.Material.SetVector(MinimumId, pass.Minimum);
                    pass.Material.SetVector(MaximumId, pass.Maximum);
                    Blitter.BlitTexture(context.cmd, (RTHandle)pass.Depth,
                        new Vector4(1f, 1f, 0f, 0f), pass.Material, 0);
                });
            }

            // Production resolves scattering into the existing color attachment.
            // No shader samples that attachment; blending performs the RGB sum.
            TextureHandle destination = source;
#if UNITY_EDITOR
            if (diagnostic)
            {
                descriptor.name = "Warehouse Fan Volume Diagnostic";
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.bindTextureMS = false;
                descriptor.clearBuffer = false;
                destination = graph.CreateTexture(descriptor);
            }
#endif
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "Warehouse Fan Volume Depth Resolve", out ResolveData data))
            {
                data.Material = material;
                data.Destination = destination;
                data.CameraData = cameraData;
                data.View = cameraData.GetViewMatrix();
                data.Projection = cameraData.GetProjectionMatrix();
                data.NearClip = camera.nearClipPlane;
                data.Dimensions = size;
                data.Depth = depth;
                data.Volume = volume;
                data.TexelSize = new Vector4(1f / width, 1f / height, width, height);
                data.LightIndex = index;
                data.Position = new Vector4(position.x, position.y, position.z, 0f);
                data.Minimum = new Vector4(minimum.x, minimum.y, minimum.z, 0f);
                data.Maximum = new Vector4(maximum.x, maximum.y, maximum.z, 0f);
#if UNITY_EDITOR
                data.DebugView = DebugViewForTests;
#endif
                builder.UseTexture(depth);
                builder.UseTexture(volume);
                // Unsupported foreground silhouettes retrace their own ray
                // against the same existing atlas as the coarse integration.
                builder.UseTexture(shadows);
#if UNITY_EDITOR
                builder.SetRenderAttachment(destination, 0, diagnostic ? AccessFlags.WriteAll : AccessFlags.ReadWrite);
#else
                builder.SetRenderAttachment(destination, 0, AccessFlags.ReadWrite);
#endif
                builder.SetRenderFunc(static (ResolveData pass, RasterGraphContext context) =>
                {
                    pass.Material.SetTexture(DepthId, pass.Depth);
                    pass.Material.SetTexture(VolumeId, pass.Volume);
                    pass.Material.SetVector(VolumeTexelSizeId, pass.TexelSize);
                    RTHandle output = pass.Destination;
                    bool flipped = pass.CameraData.IsRenderTargetProjectionMatrixFlipped(output);
                    Matrix4x4 projection = GL.GetGPUProjectionMatrix(pass.Projection, flipped);
                    pass.Material.SetVector(ScreenBoundsId, ProjectScreenBounds(pass.View, projection,
                        pass.Minimum, pass.Maximum, pass.NearClip, pass.TexelSize, pass.Dimensions));
                    pass.Material.SetInteger(LightIndexId, pass.LightIndex);
                    pass.Material.SetVector(PositionId, pass.Position);
                    pass.Material.SetVector(MinimumId, pass.Minimum);
                    pass.Material.SetVector(MaximumId, pass.Maximum);
#if UNITY_EDITOR
                    if (pass.DebugView != 0) pass.Material.SetInteger(DebugViewId, pass.DebugView);
#endif
                    // Source-less overload binds no camera color texture as an input.
#if UNITY_EDITOR
                    int shaderPass = pass.DebugView != 0 ? 2 : 1;
#else
                    const int shaderPass = 1;
#endif
                    Blitter.BlitTexture(context.cmd, new Vector4(1f, 1f, 0f, 0f),
                        pass.Material, shaderPass);
                });
            }
            resources.cameraColor = destination;
            LastLightIndex = index;
            LastResolution = new Vector2Int(width, height);
        }

        private static Vector4 ProjectScreenBounds(Matrix4x4 view, Matrix4x4 projection,
            Vector3 minimum, Vector3 maximum, float nearClip, Vector4 texelSize, Vector2Int dimensions)
        {
            Matrix4x4 worldToClip = projection * view;
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3((corner & 1) == 0 ? minimum.x : maximum.x,
                    (corner & 2) == 0 ? minimum.y : maximum.y,
                    (corner & 4) == 0 ? minimum.z : maximum.z);
                // Perspective bounds crossing the near plane are unbounded on
                // screen. The full rectangle is a safe, allocation-free fallback.
                if (-view.MultiplyPoint3x4(point).z <= nearClip) return new Vector4(0, 0, 1, 1);
                Vector4 clip = worldToClip * new Vector4(point.x, point.y, point.z, 1f);
                if (clip.w <= .0001f) return new Vector4(0, 0, 1, 1);
                float x = clip.x / clip.w * .5f + .5f;
                float y = clip.y / clip.w * .5f + .5f;
                if (SystemInfo.graphicsUVStartsAtTop) y = 1f - y;
                minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
            }
            // The outer ring reaches two coarse texels away. Include its full
            // footprint plus one output pixel; this guard cannot trim a safe tap.
            float padX = texelSize.x * 2.5f + 1f / Mathf.Max(1, dimensions.x);
            float padY = texelSize.y * 2.5f + 1f / Mathf.Max(1, dimensions.y);
            return new Vector4(Mathf.Clamp01(minX - padX), Mathf.Clamp01(minY - padY),
                Mathf.Clamp01(maxX + padX), Mathf.Clamp01(maxY + padY));
        }

        private static int FindLightIndex(UniversalLightData data, Light source)
        {
            // Forward+ packs all visible lights except its main directional.
            // The fan's index can change with the camera and other sources.
            int additional = 0;
            for (int visible = 0; visible < data.visibleLights.Length && additional < data.additionalLightsCount; visible++)
            {
                if (visible == data.mainLightIndex) continue;
                if (data.visibleLights[visible].light == source) return additional;
                additional++;
            }
            return -1;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
#if UNITY_EDITOR
            diagnosticVariantEnabled = false;
#endif
            fanLight = null;
            LastResolution = Vector2Int.zero;
            LastLightIndex = -1;
        }
    }
}
