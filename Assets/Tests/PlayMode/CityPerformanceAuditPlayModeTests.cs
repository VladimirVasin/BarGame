using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BarPromenade.Rendering;
#if UNITY_EDITOR
using UnityEditorInternal;
#endif
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Opt-in evidence for one real City. No machine-dependent FPS assertion.</summary>
    public sealed class CityPerformanceAuditPlayModeTests
    {
        private float timeScaleBeforeTest;
        private bool restoreTimeScaleAfterCleanup;

        [UnityTest, Explicit("Bounded offscreen City/port/cannery measurements and separate CPU/GC scope attribution.")]
        public IEnumerator CityIdlePortAndCannery_KeepComparableRenderAndCpuEvidence()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string label = Argument(arguments, "-bp-remediation-label") ?? "audit";
            string workload = Argument(arguments, "-bp-audit-workload") ?? "all";
            CollectionAssert.Contains(new[] { "all", "city-idle", "port-near", "cannery-near" }, workload);
            Assert.That(label.Length, Is.InRange(1, 40));
            int cycles = IntegerArgument(arguments, "-bp-audit-dormancy-cycles", 0, 0, 3);
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "City performance audit",
                SafeName(label) + " " + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            var summary = new AuditSummary { label = label, workload = workload };
            var phases = new List<PhaseResult>(3);
            Camera camera = null;
            CityGameRoot city = null;
            RenderTexture target = null, previousTarget = null;
            PlayerCameraFollow follow = null;
            bool previousFollow = false, previousCameraEnabled = false;
            bool previousMotorInput = false, previousInteractorInput = false;
            bool previousBackground = Application.runInBackground;
            float previousDelta = Time.captureDeltaTime, previousScale = Time.timeScale, previousAspect = 0;
            timeScaleBeforeTest = previousScale;
            restoreTimeScaleAfterCleanup = true;
            Pose previousCameraPose = default;
            float previousFov = 0;
            CpuTrace trace = null;
            try
            {
                Assert.That(RuntimePerformanceCapture.IsRunning, Is.False, "Do not replace another diagnostic capture.");
                GameSessionState.BeginNewGame();
                Application.runInBackground = true;
                Time.captureDeltaTime = 0f;
                double loadingStarted = Time.realtimeSinceStartupAsDouble;
                AsyncOperation loading = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                double deadline = loadingStarted + 30d;
                while (!loading.isDone || city == null || !city.IsInitialized || SceneTransitionService.IsTransitioning)
                {
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "City audit readiness exceeded its bounded wait.");
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    yield return null;
                }
                summary.sceneReadySeconds = Time.realtimeSinceStartupAsDouble - loadingStarted;
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                Assert.That(city.Cannery, Is.Not.Null);
                CityPortController port = city.World.Root.GetComponentInChildren<CityPortController>();
                Assert.That(port, Is.Not.Null);
                camera = city.Camera;
                Assert.That(camera, Is.Not.Null);
                previousTarget = camera.targetTexture;
                previousAspect = camera.aspect;
                previousCameraPose = new Pose(camera.transform.position, camera.transform.rotation);
                previousFov = camera.fieldOfView;
                previousCameraEnabled = camera.enabled;
                follow = camera.GetComponent<PlayerCameraFollow>();
                previousFollow = follow != null && follow.enabled;
                if (follow != null) follow.enabled = false;
                previousMotorInput = city.Player.Motor.InputEnabled;
                previousInteractorInput = city.Player.Interactor.InputEnabled;
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Interactor.SetInputEnabled(false);
                target = new RenderTexture(1280, 720, 24) { name = "City audit fixed offscreen target" };
                Assert.That(target.Create(), Is.True);
                camera.targetTexture = target;
                camera.aspect = 1280f / 720f;
                camera.enabled = true;
                Vector3 idlePosition = city.Player.GameObject.transform.position;
                foreach (string phase in new[] { "city-idle", "port-near", "cannery-near" })
                {
                    if (workload != "all" && workload != phase) continue;
                    StageWorkload(city, port, camera, phase, idlePosition, previousCameraPose, previousFov);
                    // Let ordinary live owners and the real camera settle; do not pause simulation.
                    double settled = Time.realtimeSinceStartupAsDouble + .5d;
                    while (Time.realtimeSinceStartupAsDouble < settled) yield return null;
                    string phaseDirectory = Path.Combine(directory, phase);
                    Assert.That(RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(
                        SceneIds.City, label + "/" + phase, .25d, 2d, 60d, 1280, 720),
                        phaseDirectory, camera), Is.True);
                    deadline = Time.realtimeSinceStartupAsDouble + 8d;
                    while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                    Assert.That(RuntimePerformanceCapture.IsRunning, Is.False, "A local measurement exceeded its bounded window.");
                    string path = RuntimePerformanceCapture.LastReportPath;
                    Assert.That(File.Exists(path), Is.True);
                    PerformanceCaptureReport report = JsonUtility.FromJson<PerformanceCaptureReport>(File.ReadAllText(path));
                    Assert.That(report.reason, Is.EqualTo("completed"));
                    Assert.That(report.resolutionMatched && report.renderWidth == 1280 && report.renderHeight == 720, Is.True);
                    Assert.That(report.renderTarget, Is.EqualTo("render_texture"));
                    Assert.That(report.pausedFrames, Is.Zero, "The audit must preserve live authoritative time.");
                    Assert.That(report.frameIntervalMs.sampleCount, Is.GreaterThan(0));
                    phases.Add(new PhaseResult { phase = phase, report = report });
                    if (Array.IndexOf(arguments, "-bp-audit-frames") >= 0)
                        AreaCaptureFixture.CaptureCurrentCamera(camera, "AuditRemediation", phase);
                }
                // Profiling has overhead: keep attribution separate from the unprofiled windows above.
                trace = new CpuTrace(Path.Combine(directory, "CPU attribution"));
                summary.cpuTraceStatus = trace.Start() ? "recording" : "unavailable_or_already_recording";
                if (trace.Running)
                {
                    double stopAt = Time.realtimeSinceStartupAsDouble + .75d;
                    while (Time.realtimeSinceStartupAsDouble < stopAt)
                    {
                        trace.MarkFrame();
                        yield return null;
                    }
                    trace.Dispose();
                    summary.cpuTraceStatus = trace.Status;
                }
                if (Array.IndexOf(arguments, "-bp-audit-gpu-ab") >= 0)
                {
                    var gpu = new List<PhaseResult>(6);
                    yield return MeasureGpuProviders(camera, directory, label, gpu);
                    summary.gpuPairs = gpu.ToArray();
                }
                summary.beforeDormancy = SnapshotOwners();
                summary.dormancyCycles = cycles;
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    Assert.That(city.EnterDormant(), Is.True);
                    yield return null;
                    city.ResumeFromDormant();
                    if (follow != null) follow.enabled = false;
                    yield return null;
                }
                summary.afterDormancy = SnapshotOwners();
                summary.phases = phases.ToArray();
                File.WriteAllText(Path.Combine(directory, "Audit.json"), JsonUtility.ToJson(summary, true));
                TestContext.Out.WriteLine("CITY_PERFORMANCE_AUDIT_OK " + directory);
            }
            finally
            {
                trace?.Dispose();
                RuntimePerformanceCapture.StopCapture();
                if (city != null && city.IsDormant) city.ResumeFromDormant();
                if (camera != null)
                {
                    camera.targetTexture = previousTarget;
                    camera.aspect = previousAspect;
                    camera.fieldOfView = previousFov;
                    camera.transform.SetPositionAndRotation(previousCameraPose.position, previousCameraPose.rotation);
                    camera.enabled = previousCameraEnabled;
                }
                if (follow != null) follow.enabled = previousFollow;
                if (city != null && city.IsInitialized)
                {
                    city.Player.Motor.SetInputEnabled(previousMotorInput);
                    city.Player.Interactor.SetInputEnabled(previousInteractorInput);
                }
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                Application.runInBackground = previousBackground;
                Time.captureDeltaTime = previousDelta;
                Time.timeScale = previousScale;
            }
        }

        private static void StageWorkload(CityGameRoot city, CityPortController port, Camera camera,
            string phase, Vector3 idlePosition, Pose idleCamera, float idleFov)
        {
            double seconds = 0d;
            Vector3 observer = idlePosition, eye = idleCamera.position, aim = eye + idleCamera.rotation * Vector3.forward;
            if (phase == "port-near")
            {
                observer = port.Plan.ArrivalWorld;
                eye = observer + Vector3.up * 1.6f;
                aim = port.Plan.World(new Vector3(0, CityPortPlan.DeckHeight + 2f, -2f));
                seconds = city.Cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish, 0) +
                    city.Cannery.Cycle.StageDuration(CityFishSupplyStage.LoadFish, 0) * .35d;
            }
            else if (phase == "cannery-near")
            {
                observer = city.Cannery.Plan.World(new Vector3(-1.1f,
                    CityCanneryPlan.FloorTop + PlayerFactory.GroundedRootOffset, 2f));
                eye = observer + Vector3.up * 1.6f;
                aim = city.Cannery.Plan.World(new Vector3(-4f, 1.4f, 0));
                seconds = city.Cannery.Cycle.ProductionStageStart(CityCanneryProductionStage.Fill) +
                    city.Cannery.Cycle.ProductionStageDuration(CityCanneryProductionStage.Fill) * .35d;
            }
            city.Player.Motor.Teleport(observer);
            Assert.That(CityFishSupplySession.TrySetDebugWorkingSeconds(seconds), Is.True);
            city.Cannery.ApplyAt(seconds);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(aim - eye));
            camera.fieldOfView = phase == "city-idle" ? idleFov : 60f;
            Physics.SyncTransforms();
        }

        // Diagnostic toggles only. The live scene/sources and the other providers
        // remain intact; the saved settings/debug state are restored on failure.
        private static IEnumerator MeasureGpuProviders(Camera camera, string directory, string label,
            List<PhaseResult> rows)
        {
            SceneScreenSpaceReflectionFeature ssr = null;
            SceneCompositeRendererFeature composite = null;
            foreach (var item in Resources.FindObjectsOfTypeAll<SceneScreenSpaceReflectionFeature>())
                if (item.isActive) { ssr = item; break; }
            foreach (var item in Resources.FindObjectsOfTypeAll<SceneCompositeRendererFeature>())
                if (item.isActive) { composite = item; break; }
            bool oldTracing = ssr != null && ssr.DebugDisableTracing;
            bool oldVolume = GraphicsEffectsSettings.CleanVolumeEnabled;
            bool oldHalation = GraphicsEffectsSettings.CleanHalationEnabled;
            try
            {
                foreach (string provider in new[] { "ssr", "volume", "halation" })
                {
                    // An absent/disabled provider is not an optimization debt.
                    if (provider == "ssr" && (ssr == null || !GraphicsEffectsSettings.CleanReflectionsEnabled)) continue;
                    if (provider == "volume" && !oldVolume || provider == "halation" && !oldHalation) continue;
                    foreach (bool enabled in new[] { false, true })
                    {
                        if (provider == "ssr") ssr.DebugDisableTracing = !enabled;
                        if (provider == "volume") GraphicsEffectsSettings.CleanVolumeEnabled = enabled;
                        if (provider == "halation") GraphicsEffectsSettings.CleanHalationEnabled = enabled;
                        string phase = "gpu-" + provider + (enabled ? "-on" : "-off");
                        Assert.That(RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(
                            SceneIds.City, label + "/" + phase, .5d, 2d, 60d, 1280, 720),
                            Path.Combine(directory, phase), camera), Is.True);
                        double deadline = Time.realtimeSinceStartupAsDouble + 8d;
                        while (RuntimePerformanceCapture.IsRunning && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                        Assert.That(RuntimePerformanceCapture.IsRunning, Is.False);
                        PerformanceCaptureReport report = JsonUtility.FromJson<PerformanceCaptureReport>(
                            File.ReadAllText(RuntimePerformanceCapture.LastReportPath));
                        Assert.That(report.reason, Is.EqualTo("completed"));
                        Assert.That(report.resolutionMatched, Is.True);
                        bool applied = provider == "ssr" ? ssr.LastCameraReady && ssr.LastCameraEligible :
                            composite != null && (provider == "volume" ? composite.DebugCleanVolumeApplied : composite.DebugCleanHalationApplied);
                        rows.Add(new PhaseResult { phase = phase, providerApplied = applied, report = report });
                        if (provider == "ssr") ssr.DebugDisableTracing = oldTracing;
                        if (provider == "volume") GraphicsEffectsSettings.CleanVolumeEnabled = oldVolume;
                        if (provider == "halation") GraphicsEffectsSettings.CleanHalationEnabled = oldHalation;
                    }
                }
            }
            finally
            {
                if (ssr != null) ssr.DebugDisableTracing = oldTracing;
                GraphicsEffectsSettings.CleanVolumeEnabled = oldVolume;
                GraphicsEffectsSettings.CleanHalationEnabled = oldHalation;
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            RuntimePerformanceCapture.StopCapture();
            Scene city = SceneManager.GetSceneByName(SceneIds.City);
            if (city.IsValid() && city.isLoaded)
            {
                Scene blank = SceneManager.CreateScene("City Performance Audit Cleanup");
                SceneManager.SetActiveScene(blank);
                yield return SceneManager.UnloadSceneAsync(city);
            }
            RetroAudioService.Instance?.StopAll();
            MusicMix.ClearFadeOuts();
            GameSessionState.BeginNewGame();
            if (restoreTimeScaleAfterCleanup)
            {
                Time.timeScale = timeScaleBeforeTest;
                restoreTimeScaleAfterCleanup = false;
            }
        }

        private static OwnerSnapshot SnapshotOwners() => new OwnerSnapshot
        {
            managedBytes = GC.GetTotalMemory(false), nativeAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
            cityRoots = Object.FindObjectsByType<CityGameRoot>(FindObjectsInactive.Include).Length,
            generatedMeshOwners = Object.FindObjectsByType<RuntimeGeneratedMeshOwner>(FindObjectsInactive.Include).Length,
            materials = Resources.FindObjectsOfTypeAll<Material>().Length,
            meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length,
            renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
            audioClips = Resources.FindObjectsOfTypeAll<AudioClip>().Length
        };

        private static string Argument(string[] args, string key)
        {
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return args[index].Substring(key.Length + 1);
                if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase)) return index + 1 < args.Length ? args[index + 1] : string.Empty;
            }
            return null;
        }

        private static int IntegerArgument(string[] args, string key, int fallback, int minimum, int maximum)
        {
            string value = Argument(args, key);
            if (value == null) return fallback;
            Assert.That(int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result), Is.True, key);
            Assert.That(result, Is.InRange(minimum, maximum), key);
            return result;
        }

        private static string SafeName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Replace('/', '_').Replace('\\', '_');
        }

        [Serializable] private sealed class AuditSummary
        {
            public string label, workload, cpuTraceStatus;
            public string mode = "Enabled production camera renders to a fixed 1280x720 offscreen target. Normal live time and simulation; observer input disabled. Screen size and original pacing are recorded independently. Not a Windows player benchmark.";
            public string comparison = "Use identical revision-independent workload, seed, settings, backend and label-separated runs. A label alone does not establish comparable conditions or an optimization.";
            public string ownerLimit = "Optional direct City dormancy/resume cycles only; process-wide material/mesh/memory snapshots include Editor and caches. Not a full interior roundtrip or proof of no leaks.";
            public double sceneReadySeconds;
            public int dormancyCycles;
            public OwnerSnapshot beforeDormancy, afterDormancy;
            public PhaseResult[] phases;
            public PhaseResult[] gpuPairs;
        }
        [Serializable] private sealed class PhaseResult { public string phase; public bool providerApplied; public PerformanceCaptureReport report; }
        [Serializable] private sealed class OwnerSnapshot
        {
            public long managedBytes, nativeAllocatedBytes;
            public int cityRoots, generatedMeshOwners, materials, meshes, renderTextures, audioClips;
        }

        private sealed class CpuTrace : IDisposable
        {
            private static readonly Guid MetadataId = new Guid("2e35f83c-d1ae-48b2-a4a4-e6d896dbcd4e");
            private readonly int[] metadata = new int[1];
            private readonly string directory;
            internal bool Running { get; private set; }
            internal string Status { get; private set; } = "unavailable_in_player";
#if UNITY_EDITOR
            private bool oldDriver, oldEnabled, oldEditor, oldDeep, oldCpu, oldBinary;
            private string oldLog;
#endif
            internal CpuTrace(string folder) { directory = folder; }
            internal bool Start()
            {
#if UNITY_EDITOR
                if (ProfilerDriver.enabled || Profiler.enabled || Profiler.enableBinaryLog) return false;
                Directory.CreateDirectory(directory);
                oldDriver = ProfilerDriver.enabled; oldEnabled = Profiler.enabled;
                oldEditor = ProfilerDriver.profileEditor; oldDeep = ProfilerDriver.deepProfiling;
                oldCpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU);
                oldBinary = Profiler.enableBinaryLog; oldLog = Profiler.logFile;
                Running = true;
                try
                {
                    ProfilerDriver.deepProfiling = false; ProfilerDriver.profileEditor = true;
                    ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                    Profiler.logFile = Path.Combine(directory, "CPU.raw"); Profiler.enableBinaryLog = true;
                    ProfilerDriver.enabled = true; Profiler.enabled = true;
                    Status = "recording";
                    return true;
                }
                catch { Dispose(); throw; }
#else
                return false;
#endif
            }
            internal void MarkFrame()
            {
                if (!Running) return;
                metadata[0] = Time.frameCount;
                Profiler.EmitFrameMetaData(MetadataId, 1, metadata);
            }
            public void Dispose()
            {
                if (!Running) return;
                Running = false;
#if UNITY_EDITOR
                try
                {
                    ProfilerDriver.enabled = false; Profiler.enabled = false;
                    Profiler.enableBinaryLog = false; Profiler.logFile = string.Empty;
                    try { ExportScopes(); }
                    catch (Exception exception)
                    {
                        // A profiler-history limitation cannot invalidate the
                        // completed unprofiled windows or remove the raw trace.
                        Status = "timeline_export_unavailable: " + exception.GetType().Name + ": " + exception.Message;
                        try { File.WriteAllText(Path.Combine(directory, "Interpretation.txt"), Status); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                finally
                {
                    ProfilerDriver.deepProfiling = oldDeep; ProfilerDriver.profileEditor = oldEditor;
                    ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, oldCpu);
                    Profiler.logFile = oldLog; Profiler.enableBinaryLog = oldBinary;
                    ProfilerDriver.enabled = oldDriver; Profiler.enabled = oldEnabled;
                }
#endif
            }
#if UNITY_EDITOR
            private void ExportScopes()
            {
                const int maxFrames = 120, maxRows = 16000;
                int rows = 0, marked = 0;
                using var writer = new StreamWriter(Path.Combine(directory, "Scopes.csv"));
                writer.WriteLine("profiler_frame,unity_frame,thread,inclusive_ms,name,parent_scope");
                int last = ProfilerDriver.lastFrameIndex;
                int first = Math.Max(ProfilerDriver.firstFrameIndex, last - maxFrames + 1);
                for (int frame = first; frame <= last && rows < maxRows; frame++)
                {
                    int unityFrame;
                    using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    {
                        if (!main.valid) continue;
                        var mark = main.GetFrameMetaData<int>(MetadataId, 1);
                        if (!mark.IsCreated || mark.Length == 0) continue;
                        unityFrame = mark[0]; marked++;
                    }
                    for (int thread = 0; thread < 32 && rows < maxRows; thread++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                        if (!data.valid) break;
                        if (data.threadName != "Main Thread" && data.threadName != "Render Thread") continue;
                        var parents = new Stack<(int sample, int end)>();
                        for (int sample = 0; sample < data.sampleCount && rows < maxRows; sample++)
                        {
                            while (parents.Count > 0 && sample > parents.Peek().end) parents.Pop();
                            string name = data.GetSampleName(sample);
                            double duration = data.GetSampleTimeMs(sample);
                            if (name == "GC.Alloc" || duration >= .05d)
                            {
                                string parent = parents.Count > 0 ? data.GetSampleName(parents.Peek().sample) : string.Empty;
                                writer.WriteLine(FormattableString.Invariant($"{frame},{unityFrame},{Csv(data.threadName)},{duration:F6},{Csv(name)},{Csv(parent)}"));
                                rows++;
                            }
                            int children = data.GetSampleChildrenCountRecursive(sample);
                            if (children > 0) parents.Push((sample, sample + children));
                        }
                    }
                }
                Status = "marked_frames=" + marked + ";rows=" + rows + ";row_cap=" + maxRows +
                    ";GC.Alloc parents are profiler scope attribution, not managed callstacks; raw trace includes profiling overhead";
                File.WriteAllText(Path.Combine(directory, "Interpretation.txt"), Status);
            }
            private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
#endif
        }
    }
}
