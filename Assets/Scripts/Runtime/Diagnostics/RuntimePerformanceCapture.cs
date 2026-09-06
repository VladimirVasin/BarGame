using System;
using System.IO;
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

        public static bool StartCapture(PerformanceCaptureOptions requested, string outputDirectory = null)
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
                if (frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
                return;
            }
            samples.Add(PerformanceCaptureSamples.FrameInterval, Time.unscaledDeltaTime * 1000d);
            samples.Add(PerformanceCaptureSamples.MainThread, ReadMilliseconds(mainThread));
            samples.Add(PerformanceCaptureSamples.RenderThread, ReadMilliseconds(renderThread));
            samples.Add(PerformanceCaptureSamples.GcBytes, ReadValue(gcBytes));
            samples.Add(PerformanceCaptureSamples.FootBake, footTicks * (1000d / Stopwatch.Frequency));
            samples.Add(PerformanceCaptureSamples.Reflection, reflectionTicks * (1000d / Stopwatch.Frequency));
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
            samples = new PerformanceCaptureSamples();
            mainThread = TryRecorder(ProfilerCategory.Internal, "Main Thread");
            renderThread = TryRecorder(ProfilerCategory.Internal, "Render Thread");
            gcBytes = TryRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
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
                qualityLevel = QualitySettings.GetQualityLevel(),
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                renderFrameInterval = OnDemandRendering.renderFrameInterval,
                depthOfField = GraphicsEffectsSettings.DepthOfFieldEnabled,
                intoxicationLens = GraphicsEffectsSettings.IntoxicationLensFxEnabled,
                dither = GraphicsEffectsSettings.DitherEnabled,
                scanlines = GraphicsEffectsSettings.ScanlinesEnabled,
                aspect43 = GraphicsEffectsSettings.AspectRatio43Enabled,
                vertexJitter = GraphicsEffectsSettings.VertexJitterEnabled,
                begotten = GraphicsEffectsSettings.BegottenModeEnabled,
                gameDayAtStart = GameSessionState.GameDayNumber,
                gameMinuteAtStart = GameSessionState.GameMinuteOfDay,
                intoxicationAtStart = GameSessionState.IntoxicationLevel,
                globalWeatherAtStart = GameWeatherRules.EvaluateCurrent().Kind.ToString(),
                gpuTimingEnabled = frameTimingEnabled,
                managedBytesAtStart = GC.GetTotalMemory(false),
                totalAllocatedBytesAtStart = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()
            };
            captureStarted = now;
            sceneHandle = SceneManager.GetActiveScene().handle;
            graphicsSettingsVersion = GraphicsEffectsSettings.Version;
            collecting = true;
            if (frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
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
                report.reason = reason;
                report.measuredSeconds = Time.realtimeSinceStartupAsDouble - captureStarted;
                report.frameIntervalMs = samples.Summarize(PerformanceCaptureSamples.FrameInterval);
                report.mainThreadMs = samples.Summarize(PerformanceCaptureSamples.MainThread);
                report.renderThreadMs = samples.Summarize(PerformanceCaptureSamples.RenderThread);
                report.gpuMs = samples.Summarize(PerformanceCaptureSamples.Gpu);
                report.gcAllocatedBytes = samples.Summarize(PerformanceCaptureSamples.GcBytes);
                report.footBakeMsPerFrame = samples.Summarize(PerformanceCaptureSamples.FootBake);
                report.reflectionMsPerFrame = samples.Summarize(PerformanceCaptureSamples.Reflection);
                report.frameP95WithinBudget = report.frameIntervalMs.sampleCount > 0 &&
                    report.frameIntervalMs.p95 <= report.frameBudgetMilliseconds;
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
            mainThread = renderThread = gcBytes = default;
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
        public int schemaVersion = 1;
        public string interpretation = "Frame intervals include frame pacing; main/render-thread markers can include waits. " +
            "Metric sampleCount=0 means unavailable, not zero cost. CPU work scopes measure only the named operation. " +
            "GPU timings require supported Frame Timing Stats; capture does not enable that Player setting. " +
            "Compare the same scene, route, seed, weather and intoxication with matching focus/pause state. " +
            "Editor results are diagnostic and are not player benchmarks.";
        public string utc, scene, label, reason, unityVersion, applicationVersion, cpu, gpu, graphicsApi, gpuDriver;
        public int seed, width, height, qualityLevel, vSyncCount, targetFrameRate, renderFrameInterval;
        public bool editor, developmentBuild, gpuTimingEnabled, frameP95WithinBudget;
        public bool depthOfField, intoxicationLens, dither, scanlines, aspect43, vertexJitter, begotten;
        public int gameDayAtStart, gameDayAtEnd, gameMinuteAtStart, gameMinuteAtEnd;
        public int intoxicationAtStart, intoxicationAtEnd;
        public string globalWeatherAtStart, globalWeatherAtEnd;
        public float renderScale;
        public double warmupSeconds, requestedSeconds, measuredSeconds, frameBudgetMilliseconds;
        public int footBakeCalls, reflectionCalls, pausedFrames, unfocusedFrames;
        public long managedBytesAtStart, managedBytesAtEnd, totalAllocatedBytesAtStart, totalAllocatedBytesAtEnd;
        public PerformanceDistribution frameIntervalMs, mainThreadMs, renderThreadMs, gpuMs, gcAllocatedBytes;
        public PerformanceDistribution footBakeMsPerFrame, reflectionMsPerFrame;
    }
}
