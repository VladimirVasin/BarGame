using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    public sealed class SceneCompositeRendererFeature : ScriptableRendererFeature
    {
        private const string MaterialResource = "Materials/SceneComposite";

        [SerializeField] private Material compositeMaterial;
        [SerializeField] private RenderPassEvent injectionPoint =
            RenderPassEvent.AfterRenderingPostProcessing;

        private SceneCompositePass pass;
        private CleanGlobalsPass globalsPass;
        private HomeToiletUnderwaterPass toiletUnderwaterPass;
        private AlpineColdFrostPass coldFrostPass;
        private NpcNameplatePass nameplatePass;
        private bool loggedMissingResources;
        private WarehouseFanVolumePass volumePass;
        private AlpineVillageLightVolumePass villageVolumePass;
        private MothersHouseLightVolumePass mothersHouseVolumePass;
        private bool volumeScheduled;
        private bool villageVolumeScheduled;
        private bool mothersHouseVolumeScheduled;

        public Material CompositeMaterial => compositeMaterial;

        internal bool DebugCleanSmaaApplied => pass != null && pass.SmaaApplied;
        internal bool DebugCleanHalationApplied => pass != null && pass.HalationApplied;
        internal bool DebugCleanVolumeApplied =>
            volumeScheduled && volumePass != null && volumePass.LastLightIndex >= 0 ||
            villageVolumeScheduled && villageVolumePass != null && villageVolumePass.LastSourceCount > 0 ||
            mothersHouseVolumeScheduled && mothersHouseVolumePass != null && mothersHouseVolumePass.LastSourceCount > 0;
        internal Vector2Int DebugCleanVolumeResolution => mothersHouseVolumeScheduled
            ? mothersHouseVolumePass?.LastResolution ?? Vector2Int.zero : villageVolumeScheduled
            ? villageVolumePass?.LastResolution ?? Vector2Int.zero
            : volumePass?.LastResolution ?? Vector2Int.zero;
        internal int DebugAlpineVolumeSourceCount => villageVolumeScheduled
            ? villageVolumePass?.LastSourceCount ?? 0 : 0;
        internal int DebugMothersHouseVolumeSourceCount => mothersHouseVolumeScheduled
            ? mothersHouseVolumePass?.LastSourceCount ?? 0 : 0;
        internal Vector2Int DebugMothersHouseVolumeResolution => mothersHouseVolumeScheduled
            ? mothersHouseVolumePass?.LastResolution ?? Vector2Int.zero : Vector2Int.zero;

        internal int DebugNameplateCandidateCount => nameplatePass?.CandidateCount ?? 0;
        internal Rect DebugNameplatePanelRect => nameplatePass?.LastPanelRect ?? default;
        internal float DebugNameplateOpacity => nameplatePass?.LastOpacity ?? 0f;
        internal string DebugNameplateText => nameplatePass?.LastText ?? string.Empty;
        internal bool DebugTryGetNameplate(NpcNameplateTarget target, out Rect rect, out float opacity)
        {
            if (nameplatePass != null) return nameplatePass.TryGetLayout(target, out rect, out opacity);
            rect = default;
            opacity = 0f;
            return false;
        }

        public override void Create()
        {
            ResolveResources();

            pass?.Dispose();
            volumePass?.Dispose();
            volumePass = new WarehouseFanVolumePass();
            villageVolumePass?.Dispose();
            villageVolumePass = new AlpineVillageLightVolumePass();
            mothersHouseVolumePass?.Dispose();
            mothersHouseVolumePass = new MothersHouseLightVolumePass();
            toiletUnderwaterPass?.Dispose();
            coldFrostPass?.Dispose();
            nameplatePass?.Dispose();
            nameplatePass = new NpcNameplatePass { renderPassEvent = injectionPoint };
            toiletUnderwaterPass = new HomeToiletUnderwaterPass();
            coldFrostPass = new AlpineColdFrostPass
            {
                // URP groups 550..599 before its DOF, bloom and tonemapping.
                // Share the composite's after-post event, then enqueue frost
                // first: URP's stable sort preserves that order within it.
                renderPassEvent = injectionPoint
            };
            pass = new SceneCompositePass
            {
                renderPassEvent = injectionPoint
            };
            // Camera-scoped lighting flags also clear for preview and cube captures.
            globalsPass = new CleanGlobalsPass
            {
                renderPassEvent = RenderPassEvent.BeforeRendering
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            volumeScheduled = false;
            villageVolumeScheduled = false;
            mothersHouseVolumeScheduled = false;
            pass?.ResetDebugState();
            CameraData cameraData = renderingData.cameraData;
            if (toiletUnderwaterPass != null && cameraData.resolveFinalTarget &&
                toiletUnderwaterPass.IsNeeded(cameraData.camera))
                renderer.EnqueuePass(toiletUnderwaterPass);
            bool excluded = cameraData.camera.TryGetComponent(
                out ScenePresentationExclusion _);
            bool present = !excluded &&
                cameraData.cameraType == CameraType.Game &&
                cameraData.resolveFinalTarget &&
                compositeMaterial != null;
            int outputWidth = cameraData.cameraTargetDescriptor.width;
            int outputHeight = cameraData.cameraTargetDescriptor.height;
            float aspectFraction = 1f;
            int effectiveWidth = outputWidth;
            if (GraphicsEffectsSettings.AspectRatio43Enabled)
                aspectFraction = AspectFraction43(outputWidth, outputHeight, out effectiveWidth);

            // Lens strengths are authored in a fixed reference pixel space;
            // the picture itself always retains the full camera target resolution.
            Vector2Int referenceResolution = LensReferenceResolution(effectiveWidth, outputHeight);

            SceneLightingPresentation lighting = SceneLightingPresentation.Resolve(cameraData.camera);
            bool cleanWorld = present && !excluded &&
                lighting != null;
            SceneLightingPresentation.ClearCameraGlobals();
            if (lighting != null) lighting.PrepareForCamera(cameraData.camera);
            Vector4 cleanEffects = cleanWorld ? new Vector4(
                GraphicsEffectsSettings.CleanReflectionsEnabled ? 1f : 0f,
                GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled ? 1f : 0f,
                GraphicsEffectsSettings.CleanBounceEnabled ? 1f : 0f, 0f) : Vector4.zero;
            SceneLocalLightParameters localLights = null;
            if (cleanWorld && GraphicsEffectsSettings.CleanVolumeEnabled && villageVolumePass != null &&
                lighting is ISceneLocalLightVolumeSource local && villageVolumePass.Setup(cameraData.camera, local))
            {
                villageVolumeScheduled = true;
                localLights = villageVolumePass.Parameters;
                renderer.EnqueuePass(villageVolumePass);
            }
            if (cleanWorld && GraphicsEffectsSettings.CleanVolumeEnabled && mothersHouseVolumePass != null &&
                lighting is IMothersHouseLightVolumeSource interior && mothersHouseVolumePass.Setup(cameraData.camera, interior))
            {
                mothersHouseVolumeScheduled = true;
                renderer.EnqueuePass(mothersHouseVolumePass);
            }
            globalsPass.Setup(cleanEffects, localLights, mothersHouseVolumeScheduled
                ? mothersHouseVolumePass.ConfiguredSourceCount : 0);
            if (cleanWorld && GraphicsEffectsSettings.CleanVolumeEnabled &&
                volumePass != null && volumePass.Setup(lighting.VolumeLight))
            {
                volumeScheduled = true;
                renderer.EnqueuePass(volumePass);
            }
            renderer.EnqueuePass(globalsPass);

            bool frostNeeded = coldFrostPass != null && cameraData.resolveFinalTarget &&
                coldFrostPass.IsNeeded(cameraData.camera);
            if (frostNeeded)
            {
                // The frost uses the same visible window that the following
                // composite crops.
                coldFrostPass.Setup(present ? aspectFraction : 1f);
                renderer.EnqueuePass(coldFrostPass);
            }

            if (!present)
            {
                EnqueueNameplates(renderer, cameraData, 1f);
                return;
            }

            // The vertigo whirlpool is projected here rather than in
            // gameplay: this runs after every LateUpdate, so the camera pose
            // is final and the eye cannot lag a frame behind an orbiting
            // camera. An excluded camera renders something else entirely -
            // projecting the hero through its frustum would be nonsense.
            IntoxicationRenderParameters intoxication =
                IntoxicationRenderState.Current;
            Vector4 vertigo = Vector4.zero;
            Vector4 vertigoShape = Vector4.zero;
            if (!excluded)
            {
                IntoxicationWhirlpool.TryResolve(
                    cameraData.camera.WorldToViewportPoint(
                        intoxication.VertigoEyeWorldPosition),
                    aspectFraction,
                    effectiveWidth,
                    outputHeight,
                    intoxication.VertigoTwistRadians,
                    intoxication.VertigoCorePixels,
                    out vertigo,
                    out vertigoShape);
            }

            pass.Setup(compositeMaterial, aspectFraction, intoxication,
                vertigo, vertigoShape, referenceResolution,
                GraphicsEffectsSettings.CleanAntialiasingEnabled && !excluded,
                cleanWorld && GraphicsEffectsSettings.CleanHalationEnabled ? lighting : null,
                cameraData.camera);
            if (pass.ShouldRender)
                renderer.EnqueuePass(pass);
            EnqueueNameplates(renderer, cameraData, aspectFraction);
        }

        private void EnqueueNameplates(ScriptableRenderer renderer, CameraData cameraData, float fraction)
        {
            if (nameplatePass == null || !cameraData.resolveFinalTarget ||
                !nameplatePass.IsNeeded(cameraData.camera)) return;
            nameplatePass.Setup(fraction);
            renderer.EnqueuePass(nameplatePass);
        }

        /// <summary>
        /// The fraction of the output width a centered 4:3 window keeps,
        /// and that window's width in pixels. 1 on a display already at
        /// or narrower than 4:3.
        /// </summary>
        public static float AspectFraction43(
            int outputWidth,
            int outputHeight,
            out int windowWidth)
        {
            windowWidth = outputWidth;
            float croppedWidth = outputHeight * (4f / 3f);
            if (croppedWidth >= outputWidth || outputWidth <= 0)
            {
                return 1f;
            }

            windowWidth = Mathf.Max(1, Mathf.RoundToInt(croppedWidth));
            return croppedWidth / outputWidth;
        }

        private static Vector2Int LensReferenceResolution(int width, int height)
        {
            float aspect = Mathf.Max(1, width) / (float)Mathf.Max(1, height);
            return aspect >= 16f / 9f
                ? new Vector2Int(640, Mathf.Max(1, Mathf.RoundToInt(640f / aspect)))
                : new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(360f * aspect)), 360);
        }

        public void SetConfiguration(Material material)
        {
            compositeMaterial = material;
            Create();
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            pass = null;
            volumePass?.Dispose();
            volumePass = null;
            villageVolumePass?.Dispose();
            villageVolumePass = null;
            mothersHouseVolumePass?.Dispose();
            mothersHouseVolumePass = null;
            globalsPass = null;
            toiletUnderwaterPass?.Dispose();
            toiletUnderwaterPass = null;
            coldFrostPass?.Dispose();
            coldFrostPass = null;
            nameplatePass?.Dispose();
            nameplatePass = null;
        }

        private void ResolveResources()
        {
            if (compositeMaterial == null)
            {
                compositeMaterial = Resources.Load<Material>(MaterialResource);
            }

            if (compositeMaterial != null)
            {
                loggedMissingResources = false;
                return;
            }

            if (!loggedMissingResources)
            {
                Debug.LogWarning(
                    "Scene composite is disabled because its shared material is missing.");
                loggedMissingResources = true;
            }
        }

        /// <summary>Keep optional world lighting local to the GPU camera timeline.</summary>
        private sealed class CleanGlobalsPass : ScriptableRenderPass
        {
            private static readonly int CleanEffectsId = Shader.PropertyToID("_WarehouseCleanEffects");
            private static readonly int InteriorAirCountId = Shader.PropertyToID("_MothersHouseAirSourceCount");
            private static readonly int SurfaceReflectionCountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
            private Vector4 cleanEffects;
            private SceneLocalLightParameters localLights;
            private int interiorAirCount;
            private int surfaceReflectionCount;

            private sealed class PassData
            {
                public Vector4 CleanEffects;
                public readonly SceneLocalLightParameters LocalLights = new SceneLocalLightParameters();
                public int InteriorAirCount;
                public int SurfaceReflectionCount;
            }

            public void Setup(Vector4 effects, SceneLocalLightParameters lights, int houseAirCount)
            {
                cleanEffects = effects;
                localLights = lights;
                interiorAirCount = houseAirCount;
                // Providers bind receivers during PrepareForCamera. Retain
                // the count on this camera's GPU timeline, including zero.
                surfaceReflectionCount = Shader.GetGlobalInt(SurfaceReflectionCountId);
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                using IUnsafeRenderGraphBuilder builder =
                    renderGraph.AddUnsafePass(
                        "Scene Lighting Globals",
                        out PassData data);
                data.CleanEffects = cleanEffects;
                data.LocalLights.CopyFrom(localLights);
                data.LocalLights.ResolveShadowIndices(frameData.Get<UniversalLightData>());
                data.InteriorAirCount = interiorAirCount;
                data.SurfaceReflectionCount = surfaceReflectionCount;
                // The pass writes no texture, so the graph would prune it.
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(
                    static (PassData passData, UnsafeGraphContext context) =>
                    {
                        context.cmd.SetGlobalVector(CleanEffectsId, passData.CleanEffects);
                        passData.LocalLights.BindGlobals(context.cmd);
                        context.cmd.SetGlobalInt(InteriorAirCountId, passData.InteriorAirCount);
                        context.cmd.SetGlobalInt(SurfaceReflectionCountId, passData.SurfaceReflectionCount);
                    });
            }
        }

        private sealed class SceneCompositePass : ScriptableRenderPass
        {
            private static readonly int AspectFractionId =
                Shader.PropertyToID("_SceneAspectFraction");
            private static readonly int IntoxicationPixelSizeId = Shader.PropertyToID("_IntoxicationPixelSize");
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
            private static readonly int IntoxicationVertigoId =
                Shader.PropertyToID("_IntoxicationVertigo");
            private static readonly int IntoxicationVertigoShapeId =
                Shader.PropertyToID("_IntoxicationVertigoShape");
            private readonly CleanSmaaPass smaa = new CleanSmaaPass();
            private readonly CleanHalationPass halation = new CleanHalationPass();
            private Material material;
            private bool nativeEffectsNeeded, antialiasing, halationEnabled;
            private float currentAspect = 1f;
            public bool SmaaApplied { get; private set; }
            public bool HalationApplied { get; private set; }
            public bool ShouldRender => nativeEffectsNeeded || antialiasing || halationEnabled;
            public SceneCompositePass() => profilingSampler = new ProfilingSampler("World Composite");
            public void ResetDebugState() { SmaaApplied = false; HalationApplied = false; }

            public void Setup(
                Material composite,
                float aspectFraction,
                IntoxicationRenderParameters intoxication,
                Vector4 vertigo,
                Vector4 vertigoShape,
                Vector2Int referenceResolution,
                bool cleanAntialiasing,
                SceneLightingPresentation lighting, Camera camera)
            {
                material = composite;
                antialiasing = cleanAntialiasing && smaa.IsAvailable;
                SmaaApplied = false;
                currentAspect = Mathf.Clamp(aspectFraction, 0.01f, 1f);
                nativeEffectsNeeded = currentAspect < 1f ||
                    intoxication.VignetteStrength > 0f || intoxication.GhostPixels > 0f ||
                    intoxication.WarpStrength > 0f || intoxication.Warmth > 0f ||
                    intoxication.ExposurePulse > 0f || vertigo.w != 0f ||
                    vertigoShape.z != 0f || vertigoShape.w != 0f;
                Vector4 referenceTexelSize = new Vector4(
                    1f / Mathf.Max(1, referenceResolution.x), 1f / Mathf.Max(1, referenceResolution.y),
                    referenceResolution.x, referenceResolution.y);
                material.SetVector(IntoxicationPixelSizeId, referenceTexelSize);
                material.SetFloat(
                    AspectFractionId,
                    Mathf.Clamp(aspectFraction, 0.01f, 1f));
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
                // Pushed every frame even when still: the material is shared,
                // and a stale eye would leak between cameras and scenes.
                material.SetVector(IntoxicationVertigoId, vertigo);
                material.SetVector(
                    IntoxicationVertigoShapeId,
                    vertigoShape);
                halationEnabled = lighting != null && halation.Setup(camera, lighting);
                HalationApplied = false;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()) return;
                TextureHandle source = resources.activeColorTexture;
                TextureHandle glow = halationEnabled
                    ? halation.BuildMask(graph, source, resources.cameraDepthTexture, frameData.Get<UniversalCameraData>()) : default;
                TextureHandle live = source;
                if (nativeEffectsNeeded)
                {
                    live = CreateTexture(graph, source, "Clean Shared Effects");
                    graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
                        source, live, material, 0), "Clean Shared Effects");
                    if (glow.IsValid())
                    {
                        TextureHandle warpedGlow = CreateTexture(graph, glow, "Clean Warped Light Sources");
                        graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
                            glow, warpedGlow, material, 0), "Clean Light Source Lens");
                        glow = warpedGlow;
                    }
                }
                if (glow.IsValid())
                {
                    live = halation.Composite(graph, live, glow);
                    HalationApplied = true;
                }
                if (antialiasing)
                {
                    live = smaa.Record(graph, live);
                    SmaaApplied = true;
                }
                if (currentAspect < 1f)
                {
                    TextureHandle framed = CreateTexture(graph, source, "Clean Frame Mask");
                    graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
                        live, framed, material, 1), "Clean Frame Mask");
                    live = framed;
                }
                resources.cameraColor = live;
            }

            private static TextureHandle CreateTexture(RenderGraph graph, TextureHandle source, string name)
            {
                TextureDesc descriptor = graph.GetTextureDesc(source);
                descriptor.depthBufferBits = DepthBits.None;
                descriptor.msaaSamples = MSAASamples.None;
                descriptor.clearBuffer = false;
                descriptor.name = name;
                return graph.CreateTexture(descriptor);
            }

            public void Dispose()
            {
                smaa.Dispose();
                halation.Dispose();
                material = null;
            }
        }
    }
}
