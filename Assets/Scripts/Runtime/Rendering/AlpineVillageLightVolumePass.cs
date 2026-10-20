using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>A real practical and the finite outdoor air it can illuminate.</summary>
    public readonly struct SceneLocalLightSource
    {
        public readonly Light Light;
        public readonly Bounds Bounds;
        // World plane: dot(normal, position) + w >= 0 is the exterior.
        // Zero permits an open-air lamp with no supporting facade.
        public readonly Vector4 ExteriorPlane;

        public SceneLocalLightSource(Light light, Bounds bounds, Vector4 exteriorPlane)
        {
            Light = light;
            Bounds = bounds;
            ExteriorPlane = exteriorPlane;
        }
    }

    public interface IAlpineVillageLightVolumeSource
    {
        int VolumeSourceCount { get; }
        bool TryGetVolumeSource(int index, out SceneLocalLightSource source);
    }

    /// <summary>Fixed snapshots shared by outdoor air and moving snow particles.</summary>
    internal sealed class SceneLocalLightParameters
    {
        internal const int MaximumSources = 4;
        private static readonly int CountId = Shader.PropertyToID("_AlpineLocalLightCount");
        private static readonly int PositionsId = Shader.PropertyToID("_AlpineLocalLightPositions");
        private static readonly int DirectionsId = Shader.PropertyToID("_AlpineLocalLightDirections");
        private static readonly int ColorsId = Shader.PropertyToID("_AlpineLocalLightColors");
        private static readonly int MinimaId = Shader.PropertyToID("_AlpineLocalLightMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_AlpineLocalLightMaxima");
        private static readonly int PlanesId = Shader.PropertyToID("_AlpineLocalLightPlanes");
        internal readonly Vector4[] Positions = new Vector4[MaximumSources];
        internal readonly Vector4[] Directions = new Vector4[MaximumSources];
        internal readonly Vector4[] Colors = new Vector4[MaximumSources];
        internal readonly Vector4[] Minima = new Vector4[MaximumSources];
        internal readonly Vector4[] Maxima = new Vector4[MaximumSources];
        internal readonly Vector4[] Planes = new Vector4[MaximumSources];
        internal int Count;

        internal void CopyFrom(SceneLocalLightParameters source)
        {
            Count = source != null ? source.Count : 0;
            if (Count == 0) return;
            Array.Copy(source.Positions, Positions, Count);
            Array.Copy(source.Directions, Directions, Count);
            Array.Copy(source.Colors, Colors, Count);
            Array.Copy(source.Minima, Minima, Count);
            Array.Copy(source.Maxima, Maxima, Count);
            Array.Copy(source.Planes, Planes, Count);
        }

        internal void BindGlobals(UnsafeCommandBuffer command)
        {
            command.SetGlobalInt(CountId, Count);
            if (Count == 0) return;
            command.SetGlobalVectorArray(PositionsId, Positions);
            command.SetGlobalVectorArray(DirectionsId, Directions);
            command.SetGlobalVectorArray(ColorsId, Colors);
            command.SetGlobalVectorArray(MinimaId, Minima);
            command.SetGlobalVectorArray(MaximaId, Maxima);
            command.SetGlobalVectorArray(PlanesId, Planes);
        }

        internal void BindMaterial(Material material)
        {
            material.SetInt(CountId, Count);
            material.SetVectorArray(PositionsId, Positions);
            material.SetVectorArray(DirectionsId, Directions);
            material.SetVectorArray(ColorsId, Colors);
            material.SetVectorArray(MinimaId, Minima);
            material.SetVectorArray(MaximaId, Maxima);
            material.SetVectorArray(PlanesId, Planes);
        }
    }

    /// <summary>
    /// Four local outdoor light pools, without new lights or shadow renders.
    /// Facade planes exclude interiors; opaque camera depth clips scattering.
    /// Small intervening objects do not receive independent light-space shadows.
    /// </summary>
    internal sealed class AlpineVillageLightVolumePass : ScriptableRenderPass, IDisposable
    {
        internal const int MaximumWidth = 320, MaximumHeight = 180, IntegrationSteps = 10;
        private static readonly int DepthId = Shader.PropertyToID("_AlpineAirDepth");
        private static readonly int VolumeId = Shader.PropertyToID("_AlpineAirVolume");
        private static readonly int TexelSizeId = Shader.PropertyToID("_AlpineAirTexelSize");
        private static readonly int ScreenBoundsId = Shader.PropertyToID("_AlpineAirScreenBounds");
        private readonly SceneLocalLightParameters lights = new SceneLocalLightParameters();
        private readonly Plane[] frustum = new Plane[6];
        private Material material;
        internal SceneLocalLightParameters Parameters => lights;
        internal int LastSourceCount { get; private set; }
        internal Vector2Int LastResolution { get; private set; }

        private sealed class PassData
        {
            internal Material Material;
            internal TextureHandle Depth, Volume, Destination;
            internal Vector4 TexelSize;
            internal UniversalCameraData CameraData;
            internal Matrix4x4 View, Projection;
            internal float NearClip;
            internal readonly SceneLocalLightParameters Lights = new SceneLocalLightParameters();
        }

        internal AlpineVillageLightVolumePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
            profilingSampler = new ProfilingSampler("Alpine Village Local Lit Air");
        }

        internal bool Setup(Camera camera, IAlpineVillageLightVolumeSource presentation)
        {
            lights.Count = 0;
            LastSourceCount = 0;
            LastResolution = Vector2Int.zero;
            if (camera == null || presentation == null) return false;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            int candidates = Mathf.Min(presentation.VolumeSourceCount, SceneLocalLightParameters.MaximumSources);
            for (int i = 0; i < candidates; i++)
            {
                if (!presentation.TryGetVolumeSource(i, out SceneLocalLightSource source)) continue;
                Light light = source.Light;
                if (light == null || !light.isActiveAndEnabled || light.intensity <= .001f || light.range <= 0f ||
                    light.type != LightType.Point && light.type != LightType.Spot ||
                    source.Bounds.size.x <= 0f || source.Bounds.size.y <= 0f || source.Bounds.size.z <= 0f ||
                    !GeometryUtility.TestPlanesAABB(frustum, source.Bounds)) continue;
                int slot = lights.Count++;
                Vector3 position = light.transform.position;
                Vector3 direction = light.transform.forward;
                float outer = light.type == LightType.Spot ? Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad) : -1f;
                float inner = Mathf.Cos(light.innerSpotAngle * .5f * Mathf.Deg2Rad);
                Color color = light.color.linear * light.intensity;
                lights.Positions[slot] = new Vector4(position.x, position.y, position.z, 1f / (light.range * light.range));
                lights.Directions[slot] = new Vector4(direction.x, direction.y, direction.z, outer);
                lights.Colors[slot] = new Vector4(color.r, color.g, color.b,
                    light.type == LightType.Spot ? 1f / Mathf.Max(.001f, inner - outer) : 0f);
                lights.Minima[slot] = source.Bounds.min;
                lights.Maxima[slot] = source.Bounds.max;
                lights.Planes[slot] = source.ExteriorPlane;
            }
            if (lights.Count == 0) return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/AlpineVillageLightVolume");
                if (shader == null || !shader.isSupported) { lights.Count = 0; return false; }
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            return true;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            LastSourceCount = 0;
            LastResolution = Vector2Int.zero;
            if (material == null || lights.Count == 0) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid() ||
                !resources.cameraDepthTexture.IsValid()) return;
            TextureHandle depth = resources.cameraDepthTexture;
            TextureHandle color = resources.activeColorTexture;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            TextureDesc descriptor = graph.GetTextureDesc(color);
            Vector2Int dimensions = descriptor.CalculateFinalDimensions();
            float scale = Mathf.Min(1f, Mathf.Min(MaximumWidth / (float)Mathf.Max(1, dimensions.x),
                MaximumHeight / (float)Mathf.Max(1, dimensions.y)));
            int width = Mathf.Max(1, Mathf.FloorToInt(dimensions.x * scale));
            int height = Mathf.Max(1, Mathf.FloorToInt(dimensions.y * scale));
            descriptor.name = "Alpine Village Local Lit Air";
            descriptor.sizeMode = TextureSizeMode.Explicit;
            descriptor.width = width;
            descriptor.height = height;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.useDynamicScale = false;
            descriptor.useDynamicScaleExplicit = false;
            descriptor.filterMode = FilterMode.Point;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = false;
            TextureHandle volume = graph.CreateTexture(descriptor);
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "Alpine Village Air Integration", out PassData data))
            {
                data.Material = material;
                data.Depth = depth;
                data.Lights.CopyFrom(lights);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(volume, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    pass.Lights.BindMaterial(pass.Material);
                    pass.Material.SetTexture(DepthId, pass.Depth);
                    Blitter.BlitTexture(context.cmd, (RTHandle)pass.Depth,
                        new Vector4(1, 1, 0, 0), pass.Material, 0);
                });
            }
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(
                       "Alpine Village Air Depth Resolve", out PassData data))
            {
                data.Material = material;
                data.Depth = depth;
                data.Volume = volume;
                data.Destination = color;
                data.TexelSize = new Vector4(1f / width, 1f / height, width, height);
                data.CameraData = cameraData;
                data.View = cameraData.GetViewMatrix();
                data.Projection = cameraData.GetProjectionMatrix();
                data.NearClip = cameraData.camera.nearClipPlane;
                data.Lights.CopyFrom(lights);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseTexture(volume, AccessFlags.Read);
                // Additive blending reads the attachment; no color texture sampling.
                builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    pass.Lights.BindMaterial(pass.Material);
                    pass.Material.SetTexture(DepthId, pass.Depth);
                    pass.Material.SetTexture(VolumeId, pass.Volume);
                    pass.Material.SetVector(TexelSizeId, pass.TexelSize);
                    RTHandle output = pass.Destination;
                    bool flipped = pass.CameraData.IsRenderTargetProjectionMatrixFlipped(output);
                    Matrix4x4 projection = GL.GetGPUProjectionMatrix(pass.Projection, flipped);
                    pass.Material.SetVector(ScreenBoundsId, ProjectScreenBounds(pass.Lights,
                        pass.View, projection, pass.NearClip, pass.TexelSize));
                    Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), pass.Material, 1);
                });
            }
            LastSourceCount = lights.Count;
            LastResolution = new Vector2Int(width, height);
        }

        private static Vector4 ProjectScreenBounds(SceneLocalLightParameters sources, Matrix4x4 view,
            Matrix4x4 projection, float nearClip, Vector4 texelSize)
        {
            Matrix4x4 worldToClip = projection * view;
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            for (int i = 0; i < sources.Count; i++)
            {
                Vector4 minimum = sources.Minima[i], maximum = sources.Maxima[i];
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = new Vector3((corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    // A near-plane crossing can cover the complete screen.
                    if (-view.MultiplyPoint3x4(point).z <= nearClip) return new Vector4(0, 0, 1, 1);
                    Vector4 clip = worldToClip * new Vector4(point.x, point.y, point.z, 1f);
                    if (clip.w <= .0001f) return new Vector4(0, 0, 1, 1);
                    float x = clip.x / clip.w * .5f + .5f;
                    float y = clip.y / clip.w * .5f + .5f;
                    if (SystemInfo.graphicsUVStartsAtTop) y = 1f - y;
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                }
            }
            return new Vector4(Mathf.Clamp01(minX - texelSize.x * 2f), Mathf.Clamp01(minY - texelSize.y * 2f),
                Mathf.Clamp01(maxX + texelSize.x * 2f), Mathf.Clamp01(maxY + texelSize.y * 2f));
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            material = null;
            lights.Count = 0;
            LastSourceCount = 0;
            LastResolution = Vector2Int.zero;
        }
    }
}
