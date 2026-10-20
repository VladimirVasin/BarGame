using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace BarPromenade.Editor
{
    /// <summary>Streams a short manual duel trace without relying on the Profiler history window.</summary>
    [InitializeOnLoad]
    internal static class CombatCpuTimelineCapture
    {
        private const string StartPath = "Tools/Bar Promenade/Diagnostics/Capture Combat CPU Timeline (15 seconds)";
        private const string StopPath = "Tools/Bar Promenade/Diagnostics/Stop Combat CPU Timeline";
        private const double CaptureSeconds = 15d;
        internal static readonly Guid FrameMetadataId = new Guid("55f0aa22-3698-41be-943a-cf0a794ca8d4");
        internal const int FrameMetadataTag = 1;
        private static readonly ProfilerMarker FrameMarker = new ProfilerMarker("BarPromenade.CombatManualFrame");
        private static readonly int[] FrameMetadata = new int[1];
        private static CaptureState active;

        internal static bool IsRunning => active != null;
        internal static string LastCaptureDirectory { get; private set; }

        private sealed class CaptureState
        {
            internal CombatTestRoot Root;
            internal CombatCpuTimelineFrameObserver Observer;
            internal string Directory;
            internal double Started;
            internal bool DriverEnabled, ProfilerEnabled, ProfileEditor, DeepProfiling, Cpu, Binary;
            internal string LogFile;
            internal CaptureManifest Manifest;
        }

        [Serializable]
        private sealed class CaptureManifest
        {
            // Unity's streaming logger appends .raw when the suffix is absent.
            public string trace = "CPU Timeline.raw";
            public string scene = SceneIds.CombatTest;
            public string startedUtc;
            public string finishedUtc;
            public string stopReason;
            public double maximumSeconds = CaptureSeconds;
            public double elapsedSeconds;
            public int startUnityFrame, endUnityFrame, firstMarkedUnityFrame = -1, lastMarkedUnityFrame = -1;
            public int markedFrames;
            public string frameMetadataId = FrameMetadataId.ToString();
            public int frameMetadataTag = FrameMetadataTag;
            public string frameMetadataFormat = "int32[1]: Time.frameCount, emitted from diagnostic LateUpdate";
            public string limitation = "Rendered Editor CPU trace includes profiling overhead. Inclusive hierarchy scopes, EditorLoop and waits overlap and must not be summed or subtracted as independent costs. Frame metadata correlates marked game frames. CSV retains at most 1200 history frames and 150000 main/render-thread nodes >=0.02ms plus GC.Alloc; streamed raw trace is complete.";
            public bool deepProfiling = false, profileEditor = true;
            public string frames = "Frames.csv", samples = "Timeline.csv";
            public bool timelineExportAvailable;
            public int exportedFrames, exportedSamples;
            public string timelineExportError;
        }

        static CombatCpuTimelineCapture()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            EditorApplication.quitting += Quitting;
        }

        [MenuItem(StartPath)]
        private static void Start() => StartCapture();

        [MenuItem(StartPath, true)]
        private static bool CanStart() => !IsRunning && FindRoot() != null;

        [MenuItem(StopPath)]
        private static void Stop() => StopCapture();

        [MenuItem(StopPath, true)]
        private static bool CanStop() => IsRunning;

        private static CombatTestRoot FindRoot()
        {
            if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != SceneIds.CombatTest) return null;
            CombatTestRoot root = UnityEngine.Object.FindAnyObjectByType<CombatTestRoot>();
            return root != null && root.IsInitialized && root.isActiveAndEnabled ? root : null;
        }

        internal static bool StartCapture(string outputDirectory = null)
        {
            if (IsRunning) return false;
            CombatTestRoot root = FindRoot();
            if (root == null) return false;
            if (ProfilerDriver.enabled || Profiler.enabled || Profiler.enableBinaryLog)
            {
                Debug.LogWarning("Combat CPU Timeline was not started: the Profiler is already recording. Stop its recording first.");
                return false;
            }

            var state = new CaptureState
            {
                Root = root,
                DriverEnabled = ProfilerDriver.enabled,
                ProfilerEnabled = Profiler.enabled,
                ProfileEditor = ProfilerDriver.profileEditor,
                DeepProfiling = ProfilerDriver.deepProfiling,
                Cpu = ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU),
                Binary = Profiler.enableBinaryLog,
                LogFile = Profiler.logFile,
                Started = EditorApplication.timeSinceStartup,
                Manifest = new CaptureManifest
                {
                    startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    startUnityFrame = Time.frameCount
                }
            };
            try
            {
                // Milliseconds plus a collision suffix preserve every completed capture.
                string parent = Path.GetFullPath(outputDirectory ?? Path.Combine(Application.dataPath,
                    "..", "TestResults", "Test duel diagnostics"));
                string name = "Manual duel " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff", CultureInfo.InvariantCulture);
                string folder = Path.Combine(parent, name);
                for (int suffix = 2; Directory.Exists(folder); suffix++) folder = Path.Combine(parent, name + " " + suffix);
                Directory.CreateDirectory(folder);
                state.Directory = folder;
                state.Observer = root.gameObject.AddComponent<CombatCpuTimelineFrameObserver>();
                state.Observer.hideFlags = HideFlags.HideAndDontSave;
                active = state;
                ProfilerDriver.deepProfiling = false;
                ProfilerDriver.profileEditor = true;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, true);
                Profiler.logFile = Path.Combine(folder, state.Manifest.trace);
                Profiler.enableBinaryLog = true;
                state.Started = EditorApplication.timeSinceStartup;
                ProfilerDriver.enabled = true;
                Profiler.enabled = true;
                LastCaptureDirectory = folder;
                Debug.Log("Combat CPU Timeline recording for 15 seconds: " + folder);
                return true;
            }
            catch (Exception exception)
            {
                if (active == state) StopCapture("start_failed");
                else if (state.Observer != null) UnityEngine.Object.DestroyImmediate(state.Observer);
                Debug.LogWarning("Combat CPU Timeline could not start: " + exception.Message);
                return false;
            }
        }

        internal static void RecordFrame()
        {
            CaptureState state = active;
            if (state == null || state.Manifest.lastMarkedUnityFrame == Time.frameCount) return;
            using (FrameMarker.Auto())
            {
                FrameMetadata[0] = Time.frameCount;
                Profiler.EmitFrameMetaData(FrameMetadataId, FrameMetadataTag, FrameMetadata);
            }
            if (state.Manifest.firstMarkedUnityFrame < 0) state.Manifest.firstMarkedUnityFrame = Time.frameCount;
            state.Manifest.lastMarkedUnityFrame = Time.frameCount;
            state.Manifest.markedFrames++;
        }

        private static void Update()
        {
            CaptureState state = active;
            if (state == null) return;
            if (!EditorApplication.isPlaying) StopCapture("play_mode_ended");
            else if (state.Root == null || !state.Root.isActiveAndEnabled ||
                SceneManager.GetActiveScene().name != SceneIds.CombatTest) StopCapture("combat_scene_ended");
            else if (EditorApplication.timeSinceStartup - state.Started >= CaptureSeconds) StopCapture("duration_reached");
        }

        private static void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) StopCapture("play_mode_ended");
        }

        private static void BeforeAssemblyReload() => StopCapture("assembly_reload");
        private static void Quitting() => StopCapture("editor_quit");

        internal static void StopCapture(string reason = "stopped")
        {
            CaptureState state = active;
            if (state == null) return;
            active = null;
            try
            {
                // Turning off binary logging closes the streamed file; SaveProfile
                // would instead export only the bounded in-memory history.
                ProfilerDriver.enabled = false;
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = string.Empty;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Combat CPU Timeline could not close its stream: " + exception.Message);
            }
            finally
            {
                ProfilerDriver.deepProfiling = state.DeepProfiling;
                ProfilerDriver.profileEditor = state.ProfileEditor;
                ProfilerDriver.SetAreaEnabled(ProfilerArea.CPU, state.Cpu);
                Profiler.logFile = state.LogFile;
                Profiler.enableBinaryLog = state.Binary;
                ProfilerDriver.enabled = state.DriverEnabled;
                Profiler.enabled = state.ProfilerEnabled;
                if (state.Observer != null) UnityEngine.Object.DestroyImmediate(state.Observer);
            }
            try
            {
                state.Manifest.finishedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                state.Manifest.endUnityFrame = Time.frameCount;
                state.Manifest.elapsedSeconds = Math.Max(0d, EditorApplication.timeSinceStartup - state.Started);
                state.Manifest.stopReason = reason;
                ExportTimeline(state);
                File.WriteAllText(Path.Combine(state.Directory, "Capture.json"), JsonUtility.ToJson(state.Manifest, true));
                Debug.Log("Combat CPU Timeline saved (" + reason + "): " + state.Directory);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Combat CPU Timeline manifest could not be saved: " + exception.Message);
            }
        }

        private static void ExportTimeline(CaptureState state)
        {
            // The streamed trace remains complete. This bounded convenience export
            // happens after profiling has stopped, never inside the measured route.
            const int maximumFrames = 1200, maximumSamples = 150000;
            try
            {
                MethodInfo metadataMethod = null;
                PropertyInfo created = null, length = null, item = null;
                object[] metadataArguments = { FrameMetadataId, FrameMetadataTag };
                object[] firstItem = { 0 };
                using var frames = new StreamWriter(Path.Combine(state.Directory, state.Manifest.frames));
                using var samples = new StreamWriter(Path.Combine(state.Directory, state.Manifest.samples));
                frames.WriteLine("profiler_frame,unity_frame,main_thread_frame_ms");
                samples.WriteLine("profiler_frame,unity_frame,thread,sample,parent,start_ms,inclusive_duration_ms,name,parent_name");
                int last = ProfilerDriver.lastFrameIndex;
                int first = Math.Max(ProfilerDriver.firstFrameIndex, last - maximumFrames + 1);
                for (int frame = first; frame <= last && state.Manifest.exportedSamples < maximumSamples; frame++)
                {
                    int unityFrame = -1;
                    using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    {
                        if (!main.valid) continue;
                        if (metadataMethod == null)
                        {
                            foreach (MethodInfo candidate in main.GetType().GetMethods())
                                if (candidate.Name == "GetFrameMetaData" && candidate.IsGenericMethodDefinition &&
                                    candidate.GetParameters().Length == 2)
                                { metadataMethod = candidate.MakeGenericMethod(typeof(int)); break; }
                        }
                        if (metadataMethod == null) break;
                        // Reflecting the NativeArray avoids adding a Collections
                        // dependency to the Editor authoring assembly for this export.
                        object metadata = metadataMethod.Invoke(main, metadataArguments);
                        if (metadata == null) continue;
                        if (created == null)
                        {
                            Type type = metadata.GetType();
                            created = type.GetProperty("IsCreated"); length = type.GetProperty("Length");
                            item = type.GetProperty("Item");
                        }
                        if (created == null || length == null || item == null ||
                            !(bool)created.GetValue(metadata) || (int)length.GetValue(metadata) == 0) continue;
                        unityFrame = (int)item.GetValue(metadata, firstItem);
                        if (unityFrame < state.Manifest.firstMarkedUnityFrame || unityFrame > state.Manifest.lastMarkedUnityFrame)
                            continue;
                        frames.WriteLine(FormattableString.Invariant($"{frame},{unityFrame},{main.frameTimeMs:F6}"));
                        state.Manifest.exportedFrames++;
                    }
                    for (int thread = 0; thread < 32 && state.Manifest.exportedSamples < maximumSamples; thread++)
                    {
                        using var data = ProfilerDriver.GetRawFrameDataView(frame, thread);
                        if (!data.valid) break;
                        if (data.threadName != "Main Thread" && data.threadName != "Render Thread") continue;
                        var parents = new Stack<(int sample, int end)>();
                        for (int sample = 0; sample < data.sampleCount && state.Manifest.exportedSamples < maximumSamples; sample++)
                        {
                            while (parents.Count > 0 && sample > parents.Peek().end) parents.Pop();
                            int parent = parents.Count > 0 ? parents.Peek().sample : -1;
                            double duration = data.GetSampleTimeMs(sample);
                            string name = data.GetSampleName(sample);
                            if (duration >= .02d || name == "GC.Alloc")
                            {
                                string parentName = parent >= 0 ? data.GetSampleName(parent) : string.Empty;
                                samples.WriteLine(FormattableString.Invariant(
                                    $"{frame},{unityFrame},{Csv(data.threadName)},{sample},{parent},{data.GetSampleStartTimeMs(sample):F6},{duration:F6},{Csv(name)},{Csv(parentName)}"));
                                state.Manifest.exportedSamples++;
                            }
                            int children = data.GetSampleChildrenCountRecursive(sample);
                            if (children > 0) parents.Push((sample, sample + children));
                        }
                    }
                }
                state.Manifest.timelineExportAvailable = state.Manifest.exportedFrames > 0;
                if (!state.Manifest.timelineExportAvailable)
                    state.Manifest.timelineExportError = "No marked frame metadata was available in retained Profiler history; use the streamed raw trace.";
                else if (state.Manifest.exportedSamples >= maximumSamples)
                    state.Manifest.timelineExportError = "Convenience CSV reached its sample bound; the raw trace remains complete.";
            }
            catch (Exception exception)
            {
                state.Manifest.timelineExportAvailable = false;
                state.Manifest.timelineExportError = exception.GetType().Name + ": " + exception.Message;
            }
        }

        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    [DefaultExecutionOrder(32002)]
    internal sealed class CombatCpuTimelineFrameObserver : MonoBehaviour
    {
        private void LateUpdate() => CombatCpuTimelineCapture.RecordFrame();
    }
}
