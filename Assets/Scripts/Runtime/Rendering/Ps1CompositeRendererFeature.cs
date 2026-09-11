using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        private Ps1VertexSnapGlobalsPass snapPass;
        private HomeToiletUnderwaterPass toiletUnderwaterPass;
        private AlpineColdFrostPass coldFrostPass;
        private NpcNameplatePass nameplatePass;
        private bool loggedMissingResources;
        private int rampFrame = -1;

        public Ps1PresentationSettings PresentationSettings =>
            presentationSettings;

        public Material CompositeMaterial => compositeMaterial;

        /// <summary>
        /// Test seam for the Begotten print. <c>true</c> prints a fresh
        /// picture on every render, <c>false</c> holds whatever is in the
        /// gate, <c>null</c> lets the projector keep its own time.
        /// </summary>
        internal bool? DebugForceFilmFrame { get; set; }

        /// <summary>The picture last handed to the print pass.</summary>
        internal BegottenFilmFrame DebugFilmState =>
            pass != null ? pass.FilmState : default;

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

        /// <summary>
        /// Puts the projector back to its first foot of stock. The
        /// print's threshold and exposure drift on a five second cycle of
        /// the film's own clock, so a measured picture depends on how
        /// much film has already run through the gate - which is to say
        /// on whatever else rendered first. A test that measures the
        /// print resets the projector so it is looking at a known frame
        /// rather than at the phase it happened to inherit.
        /// </summary>
        internal void DebugResetProjector()
        {
            pass?.DebugResetProjector();
        }

        public override void Create()
        {
            ResolveResources();

            pass?.Dispose();
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
            pass = new Ps1CompositePass
            {
                renderPassEvent = injectionPoint
            };
            // Ahead of the shadow pass and the depth prepass: every pass
            // that transforms world geometry has to read the same snap
            // parameters, or the depth buffer stops matching what the
            // forward pass drew.
            snapPass = new Ps1VertexSnapGlobalsPass
            {
                renderPassEvent = RenderPassEvent.BeforeRendering
            };
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            if (toiletUnderwaterPass != null && cameraData.resolveFinalTarget &&
                toiletUnderwaterPass.IsNeeded(cameraData.camera))
                renderer.EnqueuePass(toiletUnderwaterPass);
            // The ramp is driven here for the same reason the projector is:
            // this runs on every camera, and a driver component would have
            // to be installed into scenes the print's own tests never
            // build.
            //
            // The setting is read on every call, not once a frame, because
            // it used to be read on every call: a fixture that renders
            // several times inside one frame and flips the toggle between
            // them must see each flip, or the picture it photographs is
            // one render stale. Only the CLOCK is advanced once a frame,
            // which is what a frame's worth of time means.
            bool rampFrameIsNew = rampFrame != Time.frameCount;
            rampFrame = Time.frameCount;
            // CalendarDeltaTime is real seconds of play: unscaled, so
            // drunkenness cannot stretch the fifteen, and zero while
            // paused, so a re-opened menu holds the arrival where it is.
            BegottenModeRamp.Advance(
                GraphicsEffectsSettings.BegottenModeEnabled,
                PauseMenuController.IsAnyPaused,
                rampFrameIsNew ? GameTimeScaleRuntime.CalendarDeltaTime : 0f);
            // The composite draws only on a game camera that owns the
            // final image. The vertex snap, though, has to be told its
            // parameters on EVERY camera this renderer serves - a camera
            // that never hears from us would keep whatever grid the last
            // one set, so "off" has to be pushed rather than skipped.
            bool present =
                cameraData.cameraType == CameraType.Game &&
                cameraData.resolveFinalTarget &&
                presentationSettings != null &&
                presentationSettings.EffectEnabled &&
                compositeMaterial != null;
            bool excluded = cameraData.camera.TryGetComponent(
                out Ps1VertexJitterExclusion _);
            // The film print only on the camera that shows the game: the
            // marked cameras (inventory preview, reflection probe) render
            // something else and keep their colour.
            //
            // It is a weight rather than a switch because the mode arrives
            // over fifteen seconds. Zero and one are the two pictures the
            // game has always had; everything between is the arrival.
            float begottenWeight =
                present && !excluded
                    ? Mathf.Clamp01(BegottenModeRamp.Weight)
                    : 0f;
            bool begotten = begottenWeight > 0f;

            int outputWidth =
                cameraData.cameraTargetDescriptor.width;
            int outputHeight =
                cameraData.cameraTargetDescriptor.height;
            // The 4:3 mode crops the widescreen frame to a centered
            // 4:3 window (the exact view of a 4:3 camera with the same
            // vertical FOV) and pillarboxes the upscale. On displays
            // already at or narrower than 4:3 the fraction stays 1. The
            // film is 1.33:1, so the print always takes the 4:3 gate.
            // The gate closes over the ramp rather than snapping shut: the
            // window really narrows and the frame is resampled into it
            // every step, so the picture is squeezed rather than covered.
            // At full weight the exact rounding of AspectFraction43 is
            // taken so the arrived gate is the one it has always been, to
            // the last bit; when the player's own 4:3 switch is already on
            // there is nothing left to narrow and this is a no-op.
            float aspectFraction = 1f;
            int effectiveWidth = outputWidth;
            if (GraphicsEffectsSettings.AspectRatio43Enabled ||
                begottenWeight >= 1f)
            {
                aspectFraction = AspectFraction43(
                    outputWidth,
                    outputHeight,
                    out effectiveWidth);
            }
            else if (begotten)
            {
                float gate = AspectFraction43(
                    outputWidth,
                    outputHeight,
                    out int _);
                aspectFraction = Mathf.Lerp(1f, gate, begottenWeight);
                effectiveWidth = Mathf.Max(
                    1,
                    Mathf.RoundToInt(outputWidth * aspectFraction));
            }

            Vector2Int resolution = presentationSettings != null
                ? presentationSettings.GetInternalResolution(
                    effectiveWidth,
                    outputHeight)
                : Vector2Int.zero;

            // The snap grid is the grid the frame is presented on, which
            // is why it is derived here rather than in the shader: only
            // this method knows both the internal resolution and the 4:3
            // crop. NDC spans two units, so a grid count is half the texel
            // count; in 4:3 the internal width already describes the
            // cropped window, so it is widened back out by the same
            // fraction the composite samples with - otherwise the step
            // would jump every time the player toggles the pillarbox.
            float snapStrength = 0f;
            Vector2 snapGrid = Vector2.zero;
            if (present &&
                GraphicsEffectsSettings.VertexJitterEnabled &&
                !excluded)
            {
                snapStrength = presentationSettings.VertexJitterStrength;
                snapGrid = new Vector2(
                    resolution.x / (2f * Mathf.Max(0.01f, aspectFraction)),
                    resolution.y * 0.5f);
            }

            snapPass.Setup(snapGrid, snapStrength);
            renderer.EnqueuePass(snapPass);

            bool frostNeeded = coldFrostPass != null && cameraData.resolveFinalTarget &&
                coldFrostPass.IsNeeded(cameraData.camera);
            if (frostNeeded)
            {
                // The frost uses the same visible window that the following
                // composite crops, including the arriving Begotten gate.
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

            // The print has no colour depth to quantize and no CRT to
            // draw lines for: those three fade out as it arrives, and are
            // gone exactly when it is. A player who keeps dither or
            // scanlines switched off starts from zero and stays there -
            // the ramp can take an effect away, never add one.
            float retro = 1f - begottenWeight;
            pass.Setup(
                compositeMaterial,
                resolution,
                aspectFraction,
                presentationSettings.QuantizationStrength * retro,
                (GraphicsEffectsSettings.DitherEnabled
                    ? presentationSettings.DitherStrength
                    : 0f) * retro,
                (GraphicsEffectsSettings.ScanlinesEnabled
                    ? presentationSettings.ScanlineIntensity
                    : 0f) * retro,
                intoxication,
                vertigo,
                vertigoShape);
            pass.SetupFilm(
                begottenWeight,
                cameraData.camera,
                effectiveWidth,
                outputHeight,
                DebugForceFilmFrame);
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
            snapPass = null;
            toiletUnderwaterPass?.Dispose();
            toiletUnderwaterPass = null;
            coldFrostPass?.Dispose();
            coldFrostPass = null;
            nameplatePass?.Dispose();
            nameplatePass = null;
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

        /// <summary>
        /// Hands every world shader the pixel grid to round its vertices
        /// onto.
        ///
        /// This is written on the command buffer rather than through
        /// <c>Shader.SetGlobalVector</c> deliberately. A process-wide
        /// write belongs to whichever camera renders next, and this
        /// project renders two cameras that must not jitter - the
        /// inventory preview and the reflection probe - from the same
        /// renderer. A pass is ordered on the GPU timeline, so each camera
        /// gets its own value and nothing leaks between them.
        /// </summary>
        private sealed class Ps1VertexSnapGlobalsPass : ScriptableRenderPass
        {
            private static readonly int SnapParamsId =
                Shader.PropertyToID("_Ps1VertexSnapParams");

            private Vector4 parameters;

            private sealed class PassData
            {
                public Vector4 Parameters;
            }

            public void Setup(Vector2 grid, float strength)
            {
                parameters = new Vector4(
                    grid.x,
                    grid.y,
                    Mathf.Clamp01(strength),
                    0f);
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                using IUnsafeRenderGraphBuilder builder =
                    renderGraph.AddUnsafePass(
                        "PS1 Vertex Snap Globals",
                        out PassData data);
                data.Parameters = parameters;
                // The pass writes no texture, so the graph would prune it.
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(
                    (PassData passData, UnsafeGraphContext context) =>
                        context.cmd.SetGlobalVector(
                            SnapParamsId,
                            passData.Parameters));
            }
        }

        private sealed class Ps1CompositePass : ScriptableRenderPass
        {
            private const string LowResolutionName =
                "_BarPromenadePs1LowResolution";
            private const string FilmFrameName =
                "_BarPromenadeBegottenFrame";
            private const int SoftPassIndex = 2;
            private const int GlowPassIndex = 3;
            private const int LevelsPassIndex = 4;
            private const int PrintPassIndex = 5;

            private static readonly int LowResolutionTexelSizeId =
                Shader.PropertyToID("_Ps1LowResolutionTexelSize");
            private static readonly int QuantizationStrengthId =
                Shader.PropertyToID("_Ps1QuantizationStrength");
            private static readonly int DitherStrengthId =
                Shader.PropertyToID("_Ps1DitherStrength");
            private static readonly int ScanlineIntensityId =
                Shader.PropertyToID("_Ps1ScanlineIntensity");
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
            private static readonly int IntoxicationVertigoId =
                Shader.PropertyToID("_IntoxicationVertigo");
            private static readonly int IntoxicationVertigoShapeId =
                Shader.PropertyToID("_IntoxicationVertigoShape");
            private static readonly int GlowTextureId =
                Shader.PropertyToID("_BegottenGlowTex");
            private static readonly int LevelsTextureId =
                Shader.PropertyToID("_BegottenLevelsTex");
            private static readonly int OutputTexelSizeId =
                Shader.PropertyToID("_BegottenOutputTexelSize");
            private static readonly int SeedId =
                Shader.PropertyToID("_BegottenSeed");
            private static readonly int GateId =
                Shader.PropertyToID("_BegottenGate");
            private static readonly int ThresholdId =
                Shader.PropertyToID("_BegottenThreshold");
            private static readonly int ExposureId =
                Shader.PropertyToID("_BegottenExposure");
            private static readonly int GrainCellId =
                Shader.PropertyToID("_BegottenGrainCell");
            private static readonly int Scratch0Id =
                Shader.PropertyToID("_BegottenScratch0");
            private static readonly int Scratch1Id =
                Shader.PropertyToID("_BegottenScratch1");
            private static readonly int Scratch2Id =
                Shader.PropertyToID("_BegottenScratch2");
            private static readonly int FilmTextureId =
                Shader.PropertyToID("_BegottenFilmTex");
            private static readonly int FilmWeightId =
                Shader.PropertyToID("_BegottenWeight");
            private static readonly int FilmStruckAspectId =
                Shader.PropertyToID("_BegottenStruckAspect");

            private static GraphicsFormat? softFormat;
            private static GraphicsFormat? glowFormat;
            private static GraphicsFormat? levelsFormat;

            private Material material;
            private Vector2Int resolution;

            // The projector and the picture in its gate. The picture is a
            // persistent texture: on a held frame no pass runs and the
            // camera colour is simply pointed at it.
            private BegottenFilmModel film;
            private BegottenFilmFrame filmState;
            private RTHandle filmFrame;
            private Camera filmCamera;
            private int filmDecisionFrame = -1;
            private bool filmEnabled;

            // How much of the print is in the picture, and the gate it was
            // struck in. A held picture may be a tick or two old, from
            // when the window was a hair wider, so the compose needs the
            // fraction it was drawn at or the two sets of bars disagree.
            private float filmWeight;
            private float filmStruckAspect = 1f;
            private float currentAspect = 1f;

            /// <summary>Threads a fresh reel; the next render strikes the
            /// first picture of it.</summary>
            internal void DebugResetProjector()
            {
                film = null;
                filmDecisionFrame = -1;
            }

            private sealed class PrintPassData
            {
                public Material Material;
                public TextureHandle Soft;
                public TextureHandle Glow;
                public TextureHandle Levels;
            }

            private sealed class ComposePassData
            {
                public Material Material;
                public TextureHandle Colour;
                public TextureHandle Film;
            }

            public Ps1CompositePass()
            {
                profilingSampler = new ProfilingSampler("PS1 Composite");
            }

            public BegottenFilmFrame FilmState => filmState;

            public void Setup(
                Material composite,
                Vector2Int internalResolution,
                float aspectFraction,
                float quantizationStrength,
                float ditherStrength,
                float scanlineIntensity,
                IntoxicationRenderParameters intoxication,
                Vector4 vertigo,
                Vector4 vertigoShape)
            {
                material = composite;
                resolution = internalResolution;
                currentAspect = Mathf.Clamp(aspectFraction, 0.01f, 1f);
                // Pushed every frame, like the intoxication parameters
                // above: the material is shared, so a weight left behind
                // by a ramp would keep bleeding a stale print into the
                // ordinary picture long after the mode was switched off.
                // The compose pass overwrites it a moment later when it
                // runs; when it does not run, zero is the truth.
                material.SetFloat(FilmWeightId, 0f);
                material.SetFloat(FilmStruckAspectId, 1f);
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
                requiresIntermediateTexture = true;
            }

            /// <summary>
            /// Advances the projector once per game frame (a camera that
            /// renders twice in a frame prints the same picture twice)
            /// and hands the print pass its picture. A new camera loses
            /// the held frame, so it prints at once.
            /// </summary>
            public void SetupFilm(
                float weight,
                Camera camera,
                int windowWidth,
                int windowHeight,
                bool? debugForce)
            {
                filmWeight = Mathf.Clamp01(weight);
                filmEnabled = filmWeight > 0f;
                if (!filmEnabled)
                {
                    filmStruckAspect = currentAspect;
                    return;
                }

                film ??= new BegottenFilmModel(BegottenFilmRules.DefaultSeed);
                if (debugForce == true)
                {
                    film.ForceNewFrame();
                    filmState = film.Advance(0f);
                }
                else
                {
                    bool cameraChanged = filmCamera != camera;
                    if (filmDecisionFrame != Time.frameCount)
                    {
                        filmDecisionFrame = Time.frameCount;
                        if (cameraChanged)
                        {
                            film.ForceNewFrame();
                        }

                        filmState = film.Advance(Time.unscaledDeltaTime);
                    }
                    else if (cameraChanged)
                    {
                        film.ForceNewFrame();
                        filmState = film.Advance(0f);
                    }

                    if (debugForce == false)
                    {
                        filmState = filmState.AsHeld();
                    }
                }

                filmCamera = camera;
                if (filmState.IsNew)
                {
                    filmStruckAspect = currentAspect;
                }

                int width = Mathf.Max(1, windowWidth);
                int height = Mathf.Max(1, windowHeight);
                material.SetVector(
                    OutputTexelSizeId,
                    new Vector4(1f / width, 1f / height, width, height));
                material.SetFloat(SeedId, filmState.Seed);
                material.SetVector(
                    GateId,
                    new Vector4(
                        filmState.WeaveInternalPixels.x /
                        Mathf.Max(1, resolution.x),
                        filmState.WeaveInternalPixels.y /
                        Mathf.Max(1, resolution.y),
                        filmState.SlipPixels / Mathf.Max(1, resolution.y),
                        0f));
                material.SetFloat(ThresholdId, filmState.Threshold);
                material.SetFloat(ExposureId, filmState.Exposure);
                // Grain is a property of the print, not of the internal
                // grid: just under three output pixels at 1080p.
                material.SetFloat(
                    GrainCellId,
                    Mathf.Max(1.2f, height / 450f * 1.2f));
                material.SetVector(Scratch0Id, filmState.Scratch0);
                material.SetVector(Scratch1Id, filmState.Scratch1);
                material.SetVector(Scratch2Id, filmState.Scratch2);
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

                // Arrived: the print is the picture, exactly as it always
                // was, hold and all.
                if (filmEnabled && filmWeight >= 1f)
                {
                    RecordFilm(renderGraph, resourceData, source);
                    return;
                }

                TextureHandle lowResolution = CreateLowResolution(
                    renderGraph,
                    source);

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

                if (!filmEnabled)
                {
                    RenderGraphUtils.BlitMaterialParameters upscale =
                        new RenderGraphUtils.BlitMaterialParameters(
                            lowResolution,
                            destination,
                            material,
                            1);
                    renderGraph.AddBlitPass(upscale, "PS1 Point Upscale");
                    resourceData.cameraColor = destination;
                    return;
                }

                // Arriving. The print is struck into its own held texture
                // at twenty-four pictures a second, unchanged, and the
                // live colour is composed with it every frame. This is the
                // whole reason the blend is here rather than inside the
                // print: a picture mixed into the gate would be frozen by
                // the hold, and the game would drop to twenty-four the
                // instant the ramp began - the snap this exists to remove.
                // Instead the cadence itself arrives, because what judders
                // is the layer whose weight is rising.
                TextureHandle film = ImportFilmFrame(
                    renderGraph,
                    source,
                    out bool reallocated);
                if (filmState.IsNew || reallocated)
                {
                    RecordPrintChain(renderGraph, lowResolution, film);
                }

                RecordComposedUpscale(
                    renderGraph,
                    lowResolution,
                    film,
                    destination);
                resourceData.cameraColor = destination;
            }

            /// <summary>
            /// The ordinary point upscale with the print laid over it at
            /// the ramp's weight. A raster pass rather than a blit because
            /// a blit binds one texture and this needs two.
            /// </summary>
            private void RecordComposedUpscale(
                RenderGraph renderGraph,
                TextureHandle lowResolution,
                TextureHandle film,
                TextureHandle destination)
            {
                material.SetFloat(FilmWeightId, filmWeight);
                material.SetFloat(FilmStruckAspectId, filmStruckAspect);
                using IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass(
                        "PS1 Point Upscale + Begotten",
                        out ComposePassData data,
                        profilingSampler);
                data.Material = material;
                data.Colour = lowResolution;
                data.Film = film;
                builder.UseTexture(lowResolution);
                builder.UseTexture(film);
                builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc(
                    static (ComposePassData passData, RasterGraphContext context) =>
                    {
                        // A graph texture resolves to a real texture only
                        // inside the render function.
                        passData.Material.SetTexture(
                            FilmTextureId,
                            passData.Film);
                        Blitter.BlitTexture(
                            context.cmd,
                            (RTHandle)passData.Colour,
                            new Vector4(1f, 1f, 0f, 0f),
                            passData.Material,
                            1);
                    });
            }

            /// <summary>
            /// The Begotten branch. The picture lives in an imported,
            /// persistent texture: on a held frame nothing is recorded
            /// and the camera colour is pointed at it; on a new picture
            /// the frame is reduced, softened, blurred and printed
            /// straight into it. A reallocated texture holds nothing, so
            /// it always prints.
            /// </summary>
            private void RecordFilm(
                RenderGraph renderGraph,
                UniversalResourceData resourceData,
                TextureHandle source)
            {
                TextureHandle film = ImportFilmFrame(
                    renderGraph,
                    source,
                    out bool reallocated);

                if (!filmState.IsNew && !reallocated)
                {
                    resourceData.cameraColor = film;
                    return;
                }

                TextureHandle lowResolution = CreateLowResolution(
                    renderGraph,
                    source);
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(
                        source,
                        lowResolution,
                        material,
                        0),
                    "PS1 Downsample");
                RecordPrintChain(renderGraph, lowResolution, film);
                resourceData.cameraColor = film;
            }

            /// <summary>
            /// The picture's own texture: persistent, imported, and never
            /// discarded, because it has to outlive the frame that drew it.
            /// </summary>
            private TextureHandle ImportFilmFrame(
                RenderGraph renderGraph,
                TextureHandle source,
                out bool reallocated)
            {
                TextureDesc filmDescriptor = renderGraph.GetTextureDesc(source);
                // Every field the reallocation check compares is pinned:
                // a stray difference would reallocate every frame and the
                // print would never hold.
                filmDescriptor.sizeMode = TextureSizeMode.Explicit;
                filmDescriptor.slices = 1;
                filmDescriptor.dimension = TextureDimension.Tex2D;
                filmDescriptor.depthBufferBits = DepthBits.None;
                filmDescriptor.msaaSamples = MSAASamples.None;
                filmDescriptor.bindTextureMS = false;
                filmDescriptor.filterMode = FilterMode.Point;
                filmDescriptor.wrapMode = TextureWrapMode.Clamp;
                filmDescriptor.anisoLevel = 1;
                filmDescriptor.mipMapBias = 0f;
                filmDescriptor.useMipMap = false;
                filmDescriptor.autoGenerateMips = false;
                filmDescriptor.useDynamicScale = false;
                filmDescriptor.useDynamicScaleExplicit = false;
                filmDescriptor.memoryless = RenderTextureMemoryless.None;
                filmDescriptor.vrUsage = VRTextureUsage.None;
                filmDescriptor.enableRandomWrite = false;
                filmDescriptor.enableShadingRate = false;
                filmDescriptor.isShadowMap = false;
                filmDescriptor.clearBuffer = false;
                filmDescriptor.discardBuffer = false;
                reallocated = RenderingUtils.ReAllocateHandleIfNeeded(
                    ref filmFrame,
                    filmDescriptor,
                    FilmFrameName);

                ImportResourceParams importParameters =
                    new ImportResourceParams
                    {
                        clearOnFirstUse = reallocated,
                        clearColor = Color.black,
                        // The whole point: the picture outlives the frame.
                        discardOnLastUse = false
                    };
                return renderGraph.ImportTexture(filmFrame, importParameters);
            }

            /// <summary>
            /// Reduces the frame to a soft luminance, blurs it for the
            /// halation and the scene mean, reads its levels and prints it
            /// into the gate. Called only when a new picture is due.
            /// </summary>
            private void RecordPrintChain(
                RenderGraph renderGraph,
                TextureHandle lowResolution,
                TextureHandle film)
            {
                TextureDesc softDescriptor = new TextureDesc(
                    Mathf.Max(1, resolution.x / 2),
                    Mathf.Max(1, resolution.y / 2))
                {
                    format = SoftFormat,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                    name = "_BarPromenadeBegottenSoft"
                };
                TextureHandle soft = renderGraph.CreateTexture(softDescriptor);
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(
                        lowResolution,
                        soft,
                        material,
                        SoftPassIndex),
                    "Begotten Soft Luminance");

                TextureDesc glowDescriptor = softDescriptor;
                glowDescriptor.width = Mathf.Max(1, softDescriptor.width / 2);
                glowDescriptor.height =
                    Mathf.Max(1, softDescriptor.height / 2);
                glowDescriptor.format = GlowFormat;
                glowDescriptor.name = "_BarPromenadeBegottenGlow";
                TextureHandle glow = renderGraph.CreateTexture(glowDescriptor);
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(
                        soft,
                        glow,
                        material,
                        GlowPassIndex),
                    "Begotten Glow");

                // One pixel of scene statistics, so the print is exposed
                // for the scene rather than for a fixed threshold.
                TextureDesc levelsDescriptor = new TextureDesc(1, 1)
                {
                    format = LevelsFormat,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = false,
                    name = "_BarPromenadeBegottenLevels"
                };
                TextureHandle levels =
                    renderGraph.CreateTexture(levelsDescriptor);
                renderGraph.AddBlitPass(
                    new RenderGraphUtils.BlitMaterialParameters(
                        glow,
                        levels,
                        material,
                        LevelsPassIndex),
                    "Begotten Levels");

                using (IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass(
                        "Begotten Print",
                        out PrintPassData data,
                        profilingSampler))
                {
                    data.Material = material;
                    data.Soft = soft;
                    data.Glow = glow;
                    data.Levels = levels;
                    builder.UseTexture(soft);
                    builder.UseTexture(glow);
                    builder.UseTexture(levels);
                    builder.SetRenderAttachment(film, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc(
                        static (PrintPassData passData, RasterGraphContext context) =>
                        {
                            // A graph texture resolves to a real texture
                            // only inside the render function.
                            passData.Material.SetTexture(
                                GlowTextureId,
                                passData.Glow);
                            passData.Material.SetTexture(
                                LevelsTextureId,
                                passData.Levels);
                            Blitter.BlitTexture(
                                context.cmd,
                                (RTHandle)passData.Soft,
                                new Vector4(1f, 1f, 0f, 0f),
                                passData.Material,
                                PrintPassIndex);
                        });
                }
            }

            private TextureHandle CreateLowResolution(
                RenderGraph renderGraph,
                TextureHandle source)
            {
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
                return renderGraph.CreateTexture(lowDescriptor);
            }

            private static GraphicsFormat SoftFormat =>
                Pick(
                    ref softFormat,
                    GraphicsFormat.R8_UNorm,
                    GraphicsFormat.R16_SFloat);

            private static GraphicsFormat GlowFormat =>
                Pick(
                    ref glowFormat,
                    GraphicsFormat.R16_UNorm,
                    GraphicsFormat.R16_SFloat);

            private static GraphicsFormat LevelsFormat =>
                Pick(
                    ref levelsFormat,
                    GraphicsFormat.R16G16B16A16_SFloat,
                    GraphicsFormat.R32G32B32A32_SFloat);

            private static GraphicsFormat Pick(
                ref GraphicsFormat? cache,
                GraphicsFormat preferred,
                GraphicsFormat fallback)
            {
                if (cache.HasValue)
                {
                    return cache.Value;
                }

                if (SystemInfo.IsFormatSupported(
                        preferred,
                        GraphicsFormatUsage.Render))
                {
                    cache = preferred;
                }
                else if (SystemInfo.IsFormatSupported(
                             fallback,
                             GraphicsFormatUsage.Render))
                {
                    cache = fallback;
                }
                else
                {
                    cache = GraphicsFormat.R8G8B8A8_UNorm;
                }

                return cache.Value;
            }

            public void Dispose()
            {
                material = null;
                filmFrame?.Release();
                filmFrame = null;
                filmCamera = null;
                film = null;
            }
        }
    }
}
