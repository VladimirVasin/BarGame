using System;
using System.IO;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// Optional bounded capture. Never changes resolution, timing, gameplay or rendering.
    /// Main/render-thread counters can include waits; frame intervals include the frame cap.
    /// GPU timings are only reported when Frame Timing Stats is supported and enabled.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class RuntimePerformanceCapture : MonoBehaviour
    {
        private static RuntimePerformanceCapture active;
        private PerformanceCaptureOptions options;
        private string outputDirectory;
        private PerformanceCaptureSamples samples;
        private ProfilerRecorder mainThread;
        private ProfilerRecorder renderThread;
        private ProfilerRecorder gcBytes;
        private ProfilerRecorder editorLoop, waitTargetFps, waitPresent;
        private const int MaximumNpcCounterActors = 6;
        private CombatTestRoot combatRoot;
        private IReadOnlyList<CombatActor> combatOpponents;
        private readonly NpcCounterSnapshot[] npcCounters = new NpcCounterSnapshot[MaximumNpcCounterActors];
        private int npcObservedCount;
        private CombatCounterSnapshot combatCounters;
        private WarehouseReflectionController warehouseProbe;
        private ProbeCounterSnapshot probeCounters;
        private readonly FrameTiming[] gpuTiming = new FrameTiming[1];
        private PerformanceCaptureReport report;
        private double requestedAt;
        private double warmupStarted = -1;
        private double captureStarted;
        private long footTicks;
        private long reflectionTicks;
        private int footCalls;
        private int reflectionCalls;
        private ulong lastGpuTimestamp;
        private bool collecting;
        private bool finished;
        private bool frameTimingEnabled;
        private Camera requestedCamera;
        private Camera captureCamera;
        private RenderTexture captureTarget;
        private bool hadCaptureCamera;
        private SceneHandle sceneHandle;
        private int graphicsSettingsVersion;
        private bool recordersSettled;

        public static bool IsRunning => active != null && !active.finished;
        public static string LastReportPath { get; private set; } = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // A disabled domain reload must not keep native recorder handles alive.
            if (active != null)
            {
                active.collecting = false;
                active.finished = true;
                active.DisposeRecorders();
                Destroy(active.gameObject);
            }
            active = null;
            LastReportPath = string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ReadCommandLine()
        {
            try
            {
                PerformanceCaptureOptions requested = PerformanceCaptureOptions.FromArguments(
                    Environment.GetCommandLineArgs());
                if (requested != null) StartCapture(requested);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Performance capture was not started: " + exception.Message);
            }
        }

        public static bool StartCapture(PerformanceCaptureOptions requested, string outputDirectory = null,
            Camera renderCamera = null)
        {
            if (requested == null) throw new ArgumentNullException(nameof(requested));
            if (!Application.isPlaying || IsRunning) return false;
            string directory = Path.GetFullPath(outputDirectory ??
                Path.Combine(Application.persistentDataPath, "PerformanceCaptures"));
            LastReportPath = string.Empty;
            var host = new GameObject("[Bar Promenade] Performance Capture");
            DontDestroyOnLoad(host);
            active = host.AddComponent<RuntimePerformanceCapture>();
            active.options = requested;
            active.outputDirectory = directory;
            active.requestedCamera = renderCamera;
            active.requestedAt = Time.realtimeSinceStartupAsDouble;
            Debug.Log("Performance capture armed for " + requested.Scene +
                      "; reports: " + directory);
            return true;
        }

        public static void StopCapture()
        {
            if (IsRunning) active.Finish("stopped");
        }

        // No clock reads or allocations when capture is off. Profiler markers at the
        // call sites remain available independently in a development player/Editor.
        internal static WorkScope MeasureFootBake() => new WorkScope(active, false);
        internal static WorkScope MeasureReflection() => new WorkScope(active, true);

        internal readonly struct WorkScope : IDisposable
        {
            private readonly RuntimePerformanceCapture owner;
            private readonly bool reflection;
            private readonly long started;

            internal WorkScope(RuntimePerformanceCapture candidate, bool isReflection)
            {
                owner = candidate != null && candidate.collecting ? candidate : null;
                reflection = isReflection;
                started = owner != null ? Stopwatch.GetTimestamp() : 0;
            }

            public void Dispose()
            {
                if (owner == null || !owner.collecting) return;
                long ticks = Stopwatch.GetTimestamp() - started;
                if (reflection)
                {
                    owner.reflectionTicks += ticks;
                    owner.reflectionCalls++;
                }
                else
                {
                    owner.footTicks += ticks;
                    owner.footCalls++;
                }
            }
        }

        private void LateUpdate()
        {
            if (finished || options == null) return;
            Scene scene = SceneManager.GetActiveScene();
            double now = Time.realtimeSinceStartupAsDouble;
            if (!collecting)
            {
                if (now - requestedAt >= PerformanceCaptureOptions.MaximumWaitSeconds)
                {
                    Finish("scene_wait_timeout");
                    return;
                }
                if (!string.Equals(scene.name, options.Scene, StringComparison.Ordinal) ||
                    SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling)
                {
                    warmupStarted = -1;
                    return;
                }
                if (warmupStarted < 0) warmupStarted = now;
                if (now - warmupStarted >= options.WarmupSeconds) BeginSamples(now);
                return;
            }

            if (scene.handle != sceneHandle)
            {
                Finish("scene_changed");
                return;
            }
            if (!MatchesContext())
            {
                Finish("render_context_changed");
                return;
            }
            if (!recordersSettled)
            {
                // Exclude the setup frame: allocating the bounded buffers and
                // opening native recorders must not manufacture a measured hitch.
                recordersSettled = true;
                captureStarted = now;
                footTicks = reflectionTicks = 0;
                footCalls = reflectionCalls = 0;
                SnapshotCounterBaselines();
                report.secondaryMotionAtStart = CaptureSecondaryMotion();
                if (frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
                return;
            }
            samples.Add(PerformanceCaptureSamples.FrameInterval, Time.unscaledDeltaTime * 1000d);
            samples.Add(PerformanceCaptureSamples.MainThread, ReadMilliseconds(mainThread));
            samples.Add(PerformanceCaptureSamples.RenderThread, ReadMilliseconds(renderThread));
            samples.Add(PerformanceCaptureSamples.GcBytes, ReadValue(gcBytes));
            samples.Add(PerformanceCaptureSamples.FootBake, footTicks * (1000d / Stopwatch.Frequency));
            samples.Add(PerformanceCaptureSamples.Reflection, reflectionTicks * (1000d / Stopwatch.Frequency));
            samples.Add(PerformanceCaptureSamples.EditorLoop, ReadMilliseconds(editorLoop));
            samples.Add(PerformanceCaptureSamples.WaitTargetFps, ReadMilliseconds(waitTargetFps));
            samples.Add(PerformanceCaptureSamples.WaitPresent, ReadMilliseconds(waitPresent));
            SampleWorkCounters();
            report.footBakeCalls += footCalls;
            report.reflectionCalls += reflectionCalls;
            if (Time.timeScale == 0) report.pausedFrames++;
            if (!Application.isFocused) report.unfocusedFrames++;
            footTicks = reflectionTicks = 0;
            footCalls = reflectionCalls = 0;
            SampleGpu();
            if (samples.IsFull || now - captureStarted >= options.DurationSeconds)
                Finish(samples.IsFull ? "sample_limit" : "completed");
        }

        private void BeginSamples(double now)
        {
            // Bound ordinary windows at a generous diagnostic ceiling; hitting it
            // ends with sample_limit rather than silently dropping frame samples.
            int capacity = Math.Min(PerformanceCaptureOptions.MaximumSamples,
                (int)Math.Ceiling(options.DurationSeconds * 1000d) + 2);
            frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            // Explicit render requests can use a disabled camera, which Camera.main
            // omits. Keep its actual target size instead of falling back to the Editor window.
            captureCamera = requestedCamera != null ? requestedCamera : Camera.main;
            hadCaptureCamera = captureCamera != null;
            captureTarget = captureCamera != null ? captureCamera.targetTexture : null;
            report = new PerformanceCaptureReport
            {
                utc = DateTime.UtcNow.ToString("O"),
                scene = options.Scene,
                label = options.Label,
                seed = GameSessionState.CitySeed,
                warmupSeconds = options.WarmupSeconds,
                requestedSeconds = options.DurationSeconds,
                frameBudgetMilliseconds = 1000d / options.TargetFramesPerSecond,
                unityVersion = Application.unityVersion,
                applicationVersion = Application.version,
                editor = Application.isEditor,
                developmentBuild = Debug.isDebugBuild,
                cpu = SystemInfo.processorType,
                gpu = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                gpuDriver = SystemInfo.graphicsDeviceVersion,
                width = Screen.width,
                height = Screen.height,
                renderScale = RenderScale(),
                renderWidth = captureCamera != null ? captureCamera.pixelWidth : Screen.width,
                renderHeight = captureCamera != null ? captureCamera.pixelHeight : Screen.height,
                requestedRenderWidth = options.RequestedRenderWidth,
                requestedRenderHeight = options.RequestedRenderHeight,
                renderTarget = captureTarget != null ? "render_texture" : "screen",
                cleanAntialiasing = GraphicsEffectsSettings.CleanAntialiasingEnabled,
                cleanReflections = GraphicsEffectsSettings.CleanReflectionsEnabled,
                cleanVolume = GraphicsEffectsSettings.CleanVolumeEnabled,
                cleanHalation = GraphicsEffectsSettings.CleanHalationEnabled,
                cleanDirectionalHighlights = GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled,
                cleanBounce = GraphicsEffectsSettings.CleanBounceEnabled,
                qualityLevel = QualitySettings.GetQualityLevel(),
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                renderFrameInterval = OnDemandRendering.renderFrameInterval,
                depthOfField = GraphicsEffectsSettings.DepthOfFieldEnabled,
                intoxicationLens = GraphicsEffectsSettings.IntoxicationLensFxEnabled,
                aspect43 = GraphicsEffectsSettings.AspectRatio43Enabled,
                gameDayAtStart = GameSessionState.GameDayNumber,
                gameMinuteAtStart = GameSessionState.GameMinuteOfDay,
                intoxicationAtStart = GameSessionState.IntoxicationLevel,
                globalWeatherAtStart = GameWeatherRules.EvaluateCurrent().Kind.ToString(),
                gpuTimingEnabled = frameTimingEnabled,
                sampleCapacity = capacity,
                metricCount = PerformanceCaptureSamples.MetricCount,
                managedBytesAtStart = GC.GetTotalMemory(false),
                totalAllocatedBytesAtStart = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()
            };
            report.resolutionMatched = options.MatchesRenderResolution(report.renderWidth, report.renderHeight);
            if (!report.resolutionMatched)
            {
                // A label such as 4K must never turn a smaller Editor view into
                // a successful benchmark. Retain the rejected context, with no samples.
                Finish("resolution_mismatch");
                return;
            }
            samples = new PerformanceCaptureSamples(capacity);
            mainThread = TryRecorder(ProfilerCategory.Internal, "Main Thread");
            renderThread = TryRecorder(ProfilerCategory.Internal, "Render Thread");
            gcBytes = TryRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            editorLoop = TryRecorder(ProfilerCategory.Internal, "EditorLoop");
            waitTargetFps = TryRecorder(ProfilerCategory.Internal, "WaitForTargetFPS");
            waitPresent = TryRecorder(ProfilerCategory.Internal, "Gfx.WaitForPresentOnGfxThread");
            // The only world lookup; source/list references are retained for the window.
            combatRoot = UnityEngine.Object.FindAnyObjectByType<CombatTestRoot>();
            combatOpponents = combatRoot != null ? combatRoot.Opponents : null;
            warehouseProbe = WarehouseCleanPresentation.Current != null ? WarehouseCleanPresentation.Current.Reflection : null;
            report.recorders = new[]
            {
                DescribeRecorder(mainThread, "Internal", "Main Thread"),
                DescribeRecorder(renderThread, "Internal", "Render Thread"),
                DescribeRecorder(gcBytes, "Memory", "GC Allocated In Frame"),
                DescribeRecorder(editorLoop, "Internal", "EditorLoop"),
                DescribeRecorder(waitTargetFps, "Internal", "WaitForTargetFPS"),
                DescribeRecorder(waitPresent, "Internal", "Gfx.WaitForPresentOnGfxThread")
            };
            captureStarted = now;
            sceneHandle = SceneManager.GetActiveScene().handle;
            graphicsSettingsVersion = GraphicsEffectsSettings.Version;
            collecting = true;
            if (frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
        }

        private readonly struct CombatCounterSnapshot
        {
            internal readonly long Total, Pose, Capture, Contact, PhysicsSync, Flight, Effects, Substeps, HitStop;
            internal readonly double Dropped;
            internal CombatCounterSnapshot(CombatTestRoot source)
            {
                Total = source.TotalSimulationTicks; Pose = source.SimulationPoseTicks;
                Capture = source.SimulationCaptureTicks; Contact = source.SimulationContactTicks;
                PhysicsSync = source.SimulationPhysicsSyncTicks; Flight = source.SimulationFlightTicks;
                Effects = source.SimulationEffectsTicks; Substeps = source.SimulationSubsteps;
                HitStop = source.SimulationHitStopSubsteps; Dropped = source.SimulationDroppedSeconds;
            }
        }

        private readonly struct NpcCounterSnapshot
        {
            internal readonly CombatActor Actor;
            internal readonly long Animation, Composition, Snapshot;
            internal NpcCounterSnapshot(CombatActor source)
            {
                Actor = source; Animation = source.NpcAnimationSampleTicks;
                Composition = source.NpcPoseCompositionTicks; Snapshot = source.NpcPoseSnapshotTicks;
            }
        }

        private readonly struct ProbeCounterSnapshot
        {
            internal readonly int Starts, Completed, Failures, Cancelled, Timeouts;
            internal readonly long Submit, Poll;
            internal readonly double Latency;
            internal ProbeCounterSnapshot(WarehouseReflectionController source)
            {
                Starts = source.CaptureCount; Completed = source.CaptureCompletedCount;
                Failures = source.CaptureFailureCount; Cancelled = source.CaptureCancelledCount;
                Timeouts = source.CaptureTimeoutCount; Submit = source.CaptureSubmitTicks;
                Poll = source.CapturePollTicks; Latency = source.TotalCaptureLatencySeconds;
            }
        }

        private void SnapshotCounterBaselines()
        {
            if (combatRoot != null) combatCounters = new CombatCounterSnapshot(combatRoot);
            for (int i = 0; i < npcCounters.Length; i++) npcCounters[i] = default;
            npcObservedCount = combatOpponents != null ? combatOpponents.Count : 0;
            if (combatOpponents != null)
            {
                report.npcPopulationTruncated = combatOpponents.Count > MaximumNpcCounterActors;
                for (int i = 0; i < combatOpponents.Count && i < npcCounters.Length; i++)
                    if (combatOpponents[i] != null) npcCounters[i] = new NpcCounterSnapshot(combatOpponents[i]);
            }
            if (warehouseProbe != null) probeCounters = new ProbeCounterSnapshot(warehouseProbe);
        }

        private static double TickDeltaMilliseconds(long current, long previous) => current >= previous
            ? (current - previous) * (1000d / Stopwatch.Frequency) : -1d;

        private static long CountDelta(long current, long previous) => current >= previous ? current - previous : 0;

        private void SampleWorkCounters()
        {
            if (combatRoot != null)
            {
                CombatCounterSnapshot current = new CombatCounterSnapshot(combatRoot);
                samples.Add(PerformanceCaptureSamples.CombatTotal, TickDeltaMilliseconds(current.Total, combatCounters.Total));
                samples.Add(PerformanceCaptureSamples.CombatPose, TickDeltaMilliseconds(current.Pose, combatCounters.Pose));
                samples.Add(PerformanceCaptureSamples.CombatCapture, TickDeltaMilliseconds(current.Capture, combatCounters.Capture));
                samples.Add(PerformanceCaptureSamples.CombatContact, TickDeltaMilliseconds(current.Contact, combatCounters.Contact));
                samples.Add(PerformanceCaptureSamples.CombatPhysicsSync, TickDeltaMilliseconds(current.PhysicsSync, combatCounters.PhysicsSync));
                samples.Add(PerformanceCaptureSamples.CombatFlight, TickDeltaMilliseconds(current.Flight, combatCounters.Flight));
                samples.Add(PerformanceCaptureSamples.CombatEffects, TickDeltaMilliseconds(current.Effects, combatCounters.Effects));
                report.combatSimulationSubsteps += CountDelta(current.Substeps, combatCounters.Substeps);
                report.combatHitStopSubsteps += CountDelta(current.HitStop, combatCounters.HitStop);
                if (current.Dropped >= combatCounters.Dropped && !double.IsNaN(current.Dropped) && !double.IsInfinity(current.Dropped))
                    report.combatDroppedSeconds += current.Dropped - combatCounters.Dropped;
                if (current.Total < combatCounters.Total) report.combatCounterResets++;
                combatCounters = current;
            }
            SampleNpcCounters();
            if (warehouseProbe != null)
            {
                ProbeCounterSnapshot current = new ProbeCounterSnapshot(warehouseProbe);
                samples.Add(PerformanceCaptureSamples.WarehouseProbeSubmit, TickDeltaMilliseconds(current.Submit, probeCounters.Submit));
                samples.Add(PerformanceCaptureSamples.WarehouseProbePoll, TickDeltaMilliseconds(current.Poll, probeCounters.Poll));
                report.warehouseProbeCaptureStarts += CountDelta(current.Starts, probeCounters.Starts);
                long completed = CountDelta(current.Completed, probeCounters.Completed);
                report.warehouseProbeCaptureCompleted += completed;
                report.warehouseProbeCaptureFailures += CountDelta(current.Failures, probeCounters.Failures);
                report.warehouseProbeCaptureCancelled += CountDelta(current.Cancelled, probeCounters.Cancelled);
                report.warehouseProbeCaptureTimeouts += CountDelta(current.Timeouts, probeCounters.Timeouts);
                if (current.Latency >= probeCounters.Latency && !double.IsNaN(current.Latency) && !double.IsInfinity(current.Latency))
                    report.warehouseProbeLatencyTotalSeconds += current.Latency - probeCounters.Latency;
                if (completed > 0) report.warehouseProbeLatencyLastSeconds = warehouseProbe.LastCaptureLatencySeconds;
                probeCounters = current;
            }
        }

        private void SampleNpcCounters()
        {
            if (combatRoot == null || combatOpponents == null) return;
            int count = combatOpponents.Count;
            bool valid = count > 0 && count <= npcCounters.Length && count == npcObservedCount;
            bool hasActor = false;
            if (count != npcObservedCount) report.npcCounterRebaselines++;
            npcObservedCount = count;
            long animation = 0, composition = 0, snapshot = 0;
            for (int i = 0; i < count && i < npcCounters.Length; i++)
            {
                CombatActor actor = combatOpponents[i];
                NpcCounterSnapshot previous = npcCounters[i];
                if (actor == null)
                {
                    if (previous.Actor != null) valid = false;
                    npcCounters[i] = default;
                    continue;
                }
                hasActor = true;
                NpcCounterSnapshot current = new NpcCounterSnapshot(actor);
                if (actor != previous.Actor || current.Animation < previous.Animation ||
                    current.Composition < previous.Composition || current.Snapshot < previous.Snapshot)
                {
                    valid = false;
                    report.npcCounterRebaselines++;
                }
                else
                {
                    animation += current.Animation - previous.Animation;
                    composition += current.Composition - previous.Composition;
                    snapshot += current.Snapshot - previous.Snapshot;
                }
                npcCounters[i] = current;
            }
            for (int i = count; i < npcCounters.Length; i++) npcCounters[i] = default;
            if (!valid || !hasActor)
            {
                report.npcPopulationTruncated |= count > npcCounters.Length;
                return; // Omit an incomplete frame instead of manufacturing a zero-cost observation.
            }
            double milliseconds = 1000d / Stopwatch.Frequency;
            samples.Add(PerformanceCaptureSamples.NpcAnimation, animation * milliseconds);
            samples.Add(PerformanceCaptureSamples.NpcComposition, composition * milliseconds);
            samples.Add(PerformanceCaptureSamples.NpcSnapshot, snapshot * milliseconds);
        }

        private static PerformanceRecorderObservation DescribeRecorder(ProfilerRecorder recorder, string category, string name) =>
            new PerformanceRecorderObservation { category = category, name = name, valid = recorder.Valid };

        private void FinishAvailability()
        {
            int[] counts = { report.mainThreadMs.sampleCount, report.renderThreadMs.sampleCount,
                report.gcAllocatedBytes.sampleCount, report.editorLoopMs.sampleCount,
                report.waitForTargetFpsMs.sampleCount, report.waitForPresentMs.sampleCount };
            for (int i = 0; i < counts.Length; i++)
            {
                report.recorders[i].sampleCount = counts[i];
                report.recorders[i].available = report.recorders[i].valid && counts[i] > 0;
            }
            report.mainThreadAvailable = counts[0] > 0;
            report.renderThreadAvailable = counts[1] > 0;
            report.gcCounterAvailable = counts[2] > 0;
            report.gpuTimingAvailable = report.gpuMs.sampleCount > 0;
            report.combatCountersAvailable = report.combatTotalCpuMs.sampleCount > 0;
            report.npcCountersAvailable = report.npcAnimationCpuMs.sampleCount > 0;
            report.warehouseProbeCountersAvailable = report.warehouseProbeSubmitCpuMs.sampleCount > 0;
        }

        private static PerformanceSecondaryMotionSnapshot CaptureSecondaryMotion()
        {
            SecondaryMotionBakeRuntime.DiagnosticsSnapshot value = SecondaryMotionBakeRuntime.Diagnostics;
            return new PerformanceSecondaryMotionSnapshot
            {
                cacheHits = value.CacheHits, cacheMisses = value.CacheMisses, decodeCount = value.DecodeCount,
                loadTicks = value.LoadTicks, decodeTicks = value.DecodeTicks, largestDecodeTicks = value.LargestDecodeTicks,
                decodedValues = value.DecodedValues, residentFloatValues = value.ResidentFloatValues,
                evictions = value.Evictions, guardBuildCount = value.GuardBuildCount,
                residentClips = value.ResidentClips, lastDecodedKey = value.LastDecodedKey
            };
        }

        private void SampleGpu()
        {
            if (!frameTimingEnabled) return;
            uint count = FrameTimingManager.GetLatestTimings(1, gpuTiming);
            if (count > 0 && gpuTiming[0].frameStartTimestamp != lastGpuTimestamp)
            {
                lastGpuTimestamp = gpuTiming[0].frameStartTimestamp;
                if (gpuTiming[0].gpuFrameTime > 0)
                    samples.Add(PerformanceCaptureSamples.Gpu, gpuTiming[0].gpuFrameTime);
            }
            FrameTimingManager.CaptureFrameTimings();
        }

        private bool MatchesContext() => Screen.width == report.width &&
            Screen.height == report.height && RenderScale() == report.renderScale &&
            (!hadCaptureCamera || captureCamera != null && captureCamera.targetTexture == captureTarget &&
                captureCamera.pixelWidth == report.renderWidth && captureCamera.pixelHeight == report.renderHeight) &&
            QualitySettings.GetQualityLevel() == report.qualityLevel &&
            QualitySettings.vSyncCount == report.vSyncCount &&
            Application.targetFrameRate == report.targetFrameRate &&
            OnDemandRendering.renderFrameInterval == report.renderFrameInterval &&
            GraphicsEffectsSettings.Version == graphicsSettingsVersion;

        private static float RenderScale() =>
            GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline
                ? pipeline.renderScale : 1f;

        private static ProfilerRecorder TryRecorder(ProfilerCategory category, string name)
        {
            try { return ProfilerRecorder.StartNew(category, name, 1); }
            catch (Exception) { return default; }
        }

        private static double ReadValue(ProfilerRecorder recorder) =>
            recorder.Valid && recorder.Count > 0 ? recorder.LastValue : -1;

        private static double ReadMilliseconds(ProfilerRecorder recorder)
        {
            double value = ReadValue(recorder);
            return value < 0 ? -1 : value * 0.000001;
        }

        private void Finish(string reason)
        {
            if (finished) return;
            finished = true;
            collecting = false;
            DisposeRecorders();
            if (samples != null)
            {
                report.measuredSeconds = Time.realtimeSinceStartupAsDouble - captureStarted;
                report.frameIntervalMs = samples.Summarize(PerformanceCaptureSamples.FrameInterval);
                report.mainThreadMs = samples.Summarize(PerformanceCaptureSamples.MainThread);
                report.renderThreadMs = samples.Summarize(PerformanceCaptureSamples.RenderThread);
                report.gpuMs = samples.Summarize(PerformanceCaptureSamples.Gpu);
                report.gcAllocatedBytes = samples.Summarize(PerformanceCaptureSamples.GcBytes);
                report.footBakeMsPerFrame = samples.Summarize(PerformanceCaptureSamples.FootBake);
                report.reflectionMsPerFrame = samples.Summarize(PerformanceCaptureSamples.Reflection);
                report.combatTotalCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatTotal);
                report.combatPoseCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatPose);
                report.combatCaptureCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatCapture);
                report.combatContactCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatContact);
                report.combatPhysicsSyncCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatPhysicsSync);
                report.combatFlightCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatFlight);
                report.combatEffectsCpuMs = samples.Summarize(PerformanceCaptureSamples.CombatEffects);
                report.npcAnimationCpuMs = samples.Summarize(PerformanceCaptureSamples.NpcAnimation);
                report.npcCompositionCpuMs = samples.Summarize(PerformanceCaptureSamples.NpcComposition);
                report.npcSnapshotCpuMs = samples.Summarize(PerformanceCaptureSamples.NpcSnapshot);
                report.editorLoopMs = samples.Summarize(PerformanceCaptureSamples.EditorLoop);
                report.waitForTargetFpsMs = samples.Summarize(PerformanceCaptureSamples.WaitTargetFps);
                report.waitForPresentMs = samples.Summarize(PerformanceCaptureSamples.WaitPresent);
                report.warehouseProbeSubmitCpuMs = samples.Summarize(PerformanceCaptureSamples.WarehouseProbeSubmit);
                report.warehouseProbePollCpuMs = samples.Summarize(PerformanceCaptureSamples.WarehouseProbePoll);
                FinishAvailability();
                report.secondaryMotionAtEnd = CaptureSecondaryMotion();
                report.frameP95WithinBudget = report.frameIntervalMs.sampleCount > 0 &&
                    report.frameIntervalMs.p95 <= report.frameBudgetMilliseconds;
            }
            if (report != null)
            {
                report.reason = reason;
                report.managedBytesAtEnd = GC.GetTotalMemory(false);
                report.totalAllocatedBytesAtEnd = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                report.gameDayAtEnd = GameSessionState.GameDayNumber;
                report.gameMinuteAtEnd = GameSessionState.GameMinuteOfDay;
                report.intoxicationAtEnd = GameSessionState.IntoxicationLevel;
                report.globalWeatherAtEnd = GameWeatherRules.EvaluateCurrent().Kind.ToString();
                try
                {
                    LastReportPath = WriteReport(report, outputDirectory);
                    Debug.Log("Performance capture " + reason + ": " + LastReportPath);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Performance report could not be written: " + exception.Message);
                }
            }
            else Debug.Log("Performance capture ended without samples: " + reason);
            samples = null;
            if (active == this) active = null;
            Destroy(gameObject);
        }

        private static string WriteReport(PerformanceCaptureReport result, string directory)
        {
            Directory.CreateDirectory(directory);
            // Eight fixed filenames bound disk usage without enumerating or deleting unrelated files.
            string selected = null;
            DateTime oldest = DateTime.MaxValue;
            for (int index = 0; index < 8; index++)
            {
                string candidate = Path.Combine(directory, "capture-" + index + ".json");
                if (!File.Exists(candidate)) { selected = candidate; break; }
                DateTime changed = File.GetLastWriteTimeUtc(candidate);
                if (changed < oldest) { oldest = changed; selected = candidate; }
            }
            File.WriteAllText(selected, JsonUtility.ToJson(result, true));
            return selected;
        }

        private void DisposeRecorders()
        {
            if (mainThread.Valid) mainThread.Dispose();
            if (renderThread.Valid) renderThread.Dispose();
            if (gcBytes.Valid) gcBytes.Dispose();
            if (editorLoop.Valid) editorLoop.Dispose();
            if (waitTargetFps.Valid) waitTargetFps.Dispose();
            if (waitPresent.Valid) waitPresent.Dispose();
            mainThread = renderThread = gcBytes = default;
            editorLoop = waitTargetFps = waitPresent = default;
        }

        private void OnApplicationQuit() { Finish("application_quit"); }
        private void OnDestroy()
        {
            DisposeRecorders();
            if (active == this) active = null;
        }
    }

    [Serializable]
    internal sealed class PerformanceCaptureReport
    {
        public int schemaVersion = 3;
        public string interpretation = "Frame intervals include frame pacing; main/render-thread markers can include waits. " +
            "Metric sampleCount=0 means unavailable, not zero cost. Recorder observations retain exact requested names/categories; " +
            "absent optional markers are not subtracted or inferred. CPU counters are per-frame inclusive deltas after setup/warmup; " +
            "stages and NPC timers overlap combat total and must not be summed. NPC animation covers shared sampling; " +
            "composition/snapshot cover generic and zombie paths, not every special composition. Changed NPC slots omit an incomplete frame. " +
            "reflectionCalls/reflectionMsPerFrame are City fountain work only, never warehouse probes. Warehouse submit/poll are CPU work; " +
            "probe latency is completed wall time and can include a start before the measured window. Secondary motion snapshots are " +
            "process-cumulative boundaries in Stopwatch ticks; their difference includes only the captured window. " +
            "sampleCapacity is fixed at min(MaximumSamples, ceil(requestedSeconds*1000)+2); a full buffer ends as sample_limit. " +
            "GPU timings require supported Frame Timing Stats; capture does not enable that Player setting. " +
            "requestedRenderWidth/Height=0 means no resolution contract; a mismatch is rejected before recording. " +
            "renderWidth/Height are the actual camera target dimensions; width/height describe the screen. " +
            "render_texture windows are offscreen diagnostics, not screen/player resolution comparisons. " +
            "Compare the same scene, route, seed, weather and intoxication with matching focus/pause state. " +
            "Editor results are diagnostic and are not player benchmarks.";
        public string utc, scene, label, reason, unityVersion, applicationVersion, cpu, gpu, graphicsApi, gpuDriver, renderTarget;
        public int seed, width, height, renderWidth, renderHeight, qualityLevel, vSyncCount, targetFrameRate, renderFrameInterval;
        public int requestedRenderWidth, requestedRenderHeight;
        public bool resolutionMatched;
        public bool editor, developmentBuild, gpuTimingEnabled, frameP95WithinBudget;
        public bool mainThreadAvailable, renderThreadAvailable, gcCounterAvailable, gpuTimingAvailable;
        public bool combatCountersAvailable, npcCountersAvailable, warehouseProbeCountersAvailable, npcPopulationTruncated;
        public bool depthOfField, intoxicationLens, aspect43;
        public bool cleanAntialiasing, cleanReflections, cleanVolume, cleanHalation, cleanDirectionalHighlights, cleanBounce;
        public int gameDayAtStart, gameDayAtEnd, gameMinuteAtStart, gameMinuteAtEnd;
        public int intoxicationAtStart, intoxicationAtEnd;
        public string globalWeatherAtStart, globalWeatherAtEnd;
        public float renderScale;
        public double warmupSeconds, requestedSeconds, measuredSeconds, frameBudgetMilliseconds;
        public int footBakeCalls, reflectionCalls, pausedFrames, unfocusedFrames;
        public int sampleCapacity, metricCount, combatCounterResets, npcCounterRebaselines;
        public long stopwatchFrequency = Stopwatch.Frequency;
        public long combatSimulationSubsteps, combatHitStopSubsteps;
        public double combatDroppedSeconds;
        public long warehouseProbeCaptureStarts, warehouseProbeCaptureCompleted, warehouseProbeCaptureFailures;
        public long warehouseProbeCaptureCancelled, warehouseProbeCaptureTimeouts;
        public double warehouseProbeLatencyTotalSeconds, warehouseProbeLatencyLastSeconds;
        public long managedBytesAtStart, managedBytesAtEnd, totalAllocatedBytesAtStart, totalAllocatedBytesAtEnd;
        public PerformanceDistribution frameIntervalMs, mainThreadMs, renderThreadMs, gpuMs, gcAllocatedBytes;
        public PerformanceDistribution footBakeMsPerFrame, reflectionMsPerFrame;
        public PerformanceDistribution combatTotalCpuMs, combatPoseCpuMs, combatCaptureCpuMs, combatContactCpuMs;
        public PerformanceDistribution combatPhysicsSyncCpuMs, combatFlightCpuMs, combatEffectsCpuMs;
        public PerformanceDistribution npcAnimationCpuMs, npcCompositionCpuMs, npcSnapshotCpuMs;
        public PerformanceDistribution editorLoopMs, waitForTargetFpsMs, waitForPresentMs;
        public PerformanceDistribution warehouseProbeSubmitCpuMs, warehouseProbePollCpuMs;
        public PerformanceRecorderObservation[] recorders;
        public PerformanceSecondaryMotionSnapshot secondaryMotionAtStart, secondaryMotionAtEnd;
    }

    [Serializable]
    internal sealed class PerformanceRecorderObservation
    {
        public string category, name;
        public bool valid, available;
        public int sampleCount;
    }

    [Serializable]
    internal sealed class PerformanceSecondaryMotionSnapshot
    {
        public long cacheHits, cacheMisses, decodeCount, loadTicks, decodeTicks, largestDecodeTicks;
        public long decodedValues, residentFloatValues, evictions, guardBuildCount;
        public int residentClips;
        public string lastDecodedKey;
    }
}
