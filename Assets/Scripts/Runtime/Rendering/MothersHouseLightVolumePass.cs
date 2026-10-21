using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>A real practical and its physical room interior, independent of camera cutaways.</summary>
    public readonly struct SceneInteriorLightSource
    {
        public readonly Light Light;
        public readonly Bounds RoomBounds;
        public SceneInteriorLightSource(Light light, Bounds roomBounds)
        { Light = light; RoomBounds = roomBounds; }
    }

    public interface IMothersHouseLightVolumeSource
    {
        int InteriorVolumeSourceCount { get; }
        bool TryGetInteriorVolumeSource(int index, out SceneInteriorLightSource source);
        int InteriorVolumeBlockerCount { get; }
        bool TryGetInteriorVolumeBlocker(int index, out Bounds blocker);
    }

    /// <summary>Two weak clean-air pools, clipped by rooms and existing real-light shadows.</summary>
    internal sealed class MothersHouseLightVolumePass : ScriptableRenderPass, IDisposable
    {
        internal const int MaximumSources = 2, MaximumBlockerCandidates = 24, MaximumBlockersPerSource = 8;
        internal const int MaximumWidth = 320, MaximumHeight = 180, IntegrationSteps = 10;
        private static readonly int CountId = Shader.PropertyToID("_MothersHouseAirSourceCount");
        private static readonly int PositionsId = Shader.PropertyToID("_MothersHouseAirPositions");
        private static readonly int IndicesId = Shader.PropertyToID("_MothersHouseAirLightIndices");
        private static readonly int MinimaId = Shader.PropertyToID("_MothersHouseAirMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_MothersHouseAirMaxima");
        private static readonly int BlockerMinId = Shader.PropertyToID("_MothersHouseAirBlockerMinima");
        private static readonly int BlockerMaxId = Shader.PropertyToID("_MothersHouseAirBlockerMaxima");
        private static readonly int DepthId = Shader.PropertyToID("_MothersHouseAirDepth");
        private static readonly int VolumeId = Shader.PropertyToID("_MothersHouseAirVolume");
        private static readonly int TexelId = Shader.PropertyToID("_MothersHouseAirTexelSize");
        private static readonly int ScreenId = Shader.PropertyToID("_MothersHouseAirScreenBounds");
        private static readonly int DisableDepthRepairId = Shader.PropertyToID("_MothersHouseAirDisableDepthRepair");
        private readonly Light[] sources = new Light[MaximumSources];
        private readonly Plane[] frustum = new Plane[6];
        private readonly Parameters configured = new Parameters();
        private Material material;
        private bool reportedBlockerOverflow;
        internal int ConfiguredSourceCount => configured.Count;
        internal int LastSourceCount { get; private set; }
        internal Vector2Int LastResolution { get; private set; }
        // Capture-only A/B against the former unsupported-depth black fallback.
        internal bool DebugDisableDepthRepair { get; set; }

        private sealed class Parameters
        {
            internal int Count;
            internal Vector4 Indices;
            internal readonly Vector4[] Positions = new Vector4[MaximumSources];
            internal readonly Vector4[] Minima = new Vector4[MaximumSources];
            internal readonly Vector4[] Maxima = new Vector4[MaximumSources];
            internal readonly Vector4[] BlockerMinima = new Vector4[MaximumSources * MaximumBlockersPerSource];
            internal readonly Vector4[] BlockerMaxima = new Vector4[MaximumSources * MaximumBlockersPerSource];
            internal void CopyFrom(Parameters source)
            {
                Count = source.Count; Indices = source.Indices;
                Array.Copy(source.Positions, Positions, Count);
                Array.Copy(source.Minima, Minima, Count);
                Array.Copy(source.Maxima, Maxima, Count);
                Array.Copy(source.BlockerMinima, BlockerMinima, BlockerMinima.Length);
                Array.Copy(source.BlockerMaxima, BlockerMaxima, BlockerMaxima.Length);
            }
            internal void Bind(Material target)
            {
                target.SetInt(CountId, Count);
                target.SetVector(IndicesId, Indices);
                target.SetVectorArray(PositionsId, Positions);
                target.SetVectorArray(MinimaId, Minima);
                target.SetVectorArray(MaximaId, Maxima);
                target.SetVectorArray(BlockerMinId, BlockerMinima);
                target.SetVectorArray(BlockerMaxId, BlockerMaxima);
            }
        }

        private sealed class PassData
        {
            internal Material Material;
            internal TextureHandle Depth, Volume, Destination;
            internal Vector4 TexelSize;
            internal UniversalCameraData CameraData;
            internal Matrix4x4 View, Projection;
            internal float NearClip;
            internal bool DisableDepthRepair;
            internal readonly Parameters Lights = new Parameters();
        }

        internal MothersHouseLightVolumePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
            profilingSampler = new ProfilingSampler("Mothers House Clean Lit Air");
        }

        internal bool Setup(Camera camera, IMothersHouseLightVolumeSource provider)
        {
            configured.Count = 0; LastSourceCount = 0; LastResolution = Vector2Int.zero;
            if (camera == null || provider == null) return false;
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            if (provider.InteriorVolumeBlockerCount > MaximumBlockerCandidates)
            { ReportOverflow(); return false; }
            for (int i = 0; i < Mathf.Min(provider.InteriorVolumeSourceCount, MaximumSources); i++)
            {
                if (!provider.TryGetInteriorVolumeSource(i, out SceneInteriorLightSource source)) continue;
                Light light = source.Light;
                if (light == null || !light.isActiveAndEnabled || light.intensity <= .001f || light.range <= 0f ||
                    light.shadows == LightShadows.None || light.type != LightType.Point && light.type != LightType.Spot)
                    continue;
                Bounds room = source.RoomBounds;
                Vector3 position = light.transform.position;
                Vector3 radius = Vector3.one * light.range;
                Vector3 minimum = Vector3.Max(room.min, position - radius);
                Vector3 maximum = Vector3.Min(room.max, position + radius);
                if (maximum.x - minimum.x < .05f || maximum.y - minimum.y < .05f || maximum.z - minimum.z < .05f) continue;
                Bounds support = new Bounds((minimum + maximum) * .5f, maximum - minimum);
                if (!GeometryUtility.TestPlanesAABB(frustum, support)) continue;
                int slot = configured.Count, blockers = 0;
                for (int blockerIndex = 0; blockerIndex < provider.InteriorVolumeBlockerCount; blockerIndex++)
                {
                    if (!provider.TryGetInteriorVolumeBlocker(blockerIndex, out Bounds blocker) ||
                        !IntersectsInterior(blocker, room) || !blocker.Intersects(support) ||
                        blocker.SqrDistance(position) > light.range * light.range) continue;
                    if (blockers >= MaximumBlockersPerSource) { ReportOverflow(); configured.Count = 0; return false; }
                    int entry = slot * MaximumBlockersPerSource + blockers++;
                    configured.BlockerMinima[entry] = blocker.min;
                    configured.BlockerMaxima[entry] = blocker.max;
                }
                configured.Positions[slot] = new Vector4(position.x, position.y, position.z, 0f);
                configured.Minima[slot] = new Vector4(minimum.x, minimum.y, minimum.z, blockers);
                configured.Maxima[slot] = maximum;
                sources[slot] = light;
                configured.Count++;
            }
            if (configured.Count == 0) return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/MothersHouseLightVolume");
                if (shader == null || !shader.isSupported) { configured.Count = 0; return false; }
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            return true;
        }

        private static bool IntersectsInterior(Bounds blocker, Bounds room)
        {
            // Boundary walls/slabs are already represented exactly by the
            // room prism. Only genuine interior obstacles enter the GPU loop.
            Vector3 size = Vector3.Min(blocker.max, room.max - Vector3.one * .025f) -
                Vector3.Max(blocker.min, room.min + Vector3.one * .025f);
            return size.x > .025f && size.y > .025f && size.z > .025f;
        }

        private void ReportOverflow()
        {
            if (reportedBlockerOverflow) return;
            reportedBlockerOverflow = true;
            Debug.LogWarning("Mother's house lit air exceeded its structural blocker budget; the pass was disabled.");
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            LastSourceCount = 0; LastResolution = Vector2Int.zero;
            if (material == null || configured.Count == 0) return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            if (cameraData.camera == null || cameraData.camera.cameraType != CameraType.Game ||
                !cameraData.resolveFinalTarget || cameraData.camera.TryGetComponent(out ScenePresentationExclusion _)) return;
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle depth = resources.cameraDepthTexture, color = resources.activeColorTexture;
            TextureHandle shadows = resources.additionalShadowsTexture;
            if (resources.isActiveTargetBackBuffer || !depth.IsValid() || !color.IsValid() || !shadows.IsValid()) return;
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            configured.Indices = new Vector4(-1f, -1f, -1f, -1f);
            int found = 0;
            for (int i = 0; i < configured.Count; i++)
            {
                int index = FindLightIndex(lightData, sources[i]);
                configured.Indices[i] = index;
                if (index >= 0) found++;
            }
            if (found == 0) return;
            TextureDesc descriptor = graph.GetTextureDesc(color);
            Vector2Int dimensions = descriptor.CalculateFinalDimensions();
            float scale = Mathf.Min(1f, Mathf.Min(MaximumWidth / (float)Mathf.Max(1, dimensions.x),
                MaximumHeight / (float)Mathf.Max(1, dimensions.y)));
            int width = Mathf.Max(1, Mathf.FloorToInt(dimensions.x * scale));
            int height = Mathf.Max(1, Mathf.FloorToInt(dimensions.y * scale));
            descriptor.name = "Mothers House Clean Lit Air";
            descriptor.sizeMode = TextureSizeMode.Explicit; descriptor.width = width; descriptor.height = height;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthBufferBits = DepthBits.None; descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false; descriptor.useDynamicScale = false; descriptor.useDynamicScaleExplicit = false;
            descriptor.filterMode = FilterMode.Point; descriptor.wrapMode = TextureWrapMode.Clamp; descriptor.clearBuffer = false;
            TextureHandle volume = graph.CreateTexture(descriptor);
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass("Mothers House Air Integration", out PassData data))
            {
                FillData(data, cameraData, depth, volume, configured, width, height);
                builder.UseTexture(depth, AccessFlags.Read); builder.UseTexture(shadows, AccessFlags.Read);
                builder.SetRenderAttachment(volume, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    Bind(pass, pass.Destination);
                    Blitter.BlitTexture(context.cmd, (RTHandle)pass.Depth, new Vector4(1, 1, 0, 0), pass.Material, 0);
                });
            }
            using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass("Mothers House Air Depth Resolve", out PassData data))
            {
                FillData(data, cameraData, depth, color, configured, width, height);
                data.Volume = volume;
                builder.UseTexture(depth, AccessFlags.Read); builder.UseTexture(volume, AccessFlags.Read);
                // Unsupported native silhouettes reuse the actual URP atlas;
                // the exact-ray fallback does not render another shadow map.
                builder.UseTexture(shadows, AccessFlags.Read);
                builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    Bind(pass, pass.Destination);
                    pass.Material.SetTexture(VolumeId, pass.Volume);
                    Blitter.BlitTexture(context.cmd, new Vector4(1, 1, 0, 0), pass.Material, 1);
                });
            }
            LastSourceCount = found; LastResolution = new Vector2Int(width, height);
        }

        private void FillData(PassData data, UniversalCameraData camera, TextureHandle depth,
            TextureHandle destination, Parameters parameters, int width, int height)
        {
            data.Material = material; data.Depth = depth; data.Destination = destination;
            data.TexelSize = new Vector4(1f / width, 1f / height, width, height);
            data.CameraData = camera; data.View = camera.GetViewMatrix(); data.Projection = camera.GetProjectionMatrix();
            data.NearClip = camera.camera.nearClipPlane; data.Lights.CopyFrom(parameters);
            data.DisableDepthRepair = DebugDisableDepthRepair;
        }

        private static void Bind(PassData pass, RTHandle output)
        {
            pass.Lights.Bind(pass.Material);
            pass.Material.SetTexture(DepthId, pass.Depth); pass.Material.SetVector(TexelId, pass.TexelSize);
            pass.Material.SetFloat(DisableDepthRepairId, pass.DisableDepthRepair ? 1f : 0f);
            bool flipped = pass.CameraData.IsRenderTargetProjectionMatrixFlipped(output);
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(pass.Projection, flipped);
            pass.Material.SetVector(ScreenId, ProjectBounds(pass.Lights, pass.View, projection, pass.NearClip, pass.TexelSize));
        }

        private static Vector4 ProjectBounds(Parameters lights, Matrix4x4 view, Matrix4x4 projection, float near, Vector4 texel)
        {
            Matrix4x4 worldToClip = projection * view;
            float minX = 1, minY = 1, maxX = 0, maxY = 0;
            for (int i = 0; i < lights.Count; i++)
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector4 minimum = lights.Minima[i], maximum = lights.Maxima[i];
                    Vector3 point = new Vector3((corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y, (corner & 4) == 0 ? minimum.z : maximum.z);
                    if (-view.MultiplyPoint3x4(point).z <= near) return new Vector4(0, 0, 1, 1);
                    Vector4 clip = worldToClip * new Vector4(point.x, point.y, point.z, 1f);
                    if (clip.w <= .0001f) return new Vector4(0, 0, 1, 1);
                    float x = clip.x / clip.w * .5f + .5f, y = clip.y / clip.w * .5f + .5f;
                    if (SystemInfo.graphicsUVStartsAtTop) y = 1f - y;
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y); maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                }
            return new Vector4(Mathf.Clamp01(minX - texel.x * 2f), Mathf.Clamp01(minY - texel.y * 2f),
                Mathf.Clamp01(maxX + texel.x * 2f), Mathf.Clamp01(maxY + texel.y * 2f));
        }

        private static int FindLightIndex(UniversalLightData data, Light source)
        {
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
            CoreUtils.Destroy(material); material = null; configured.Count = 0;
            Array.Clear(sources, 0, sources.Length);
            LastSourceCount = 0; LastResolution = Vector2Int.zero;
        }
    }
}
