using System;
using System.Globalization;
using System.IO;
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
            public string limitation = "Rendered Editor CPU trace includes profiling overhead; Editor and game frames differ. Use frame metadata to correlate marked game frames with duel.ndjson.";
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
                File.WriteAllText(Path.Combine(state.Directory, "Capture.json"), JsonUtility.ToJson(state.Manifest, true));
                Debug.Log("Combat CPU Timeline saved (" + reason + "): " + state.Directory);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Combat CPU Timeline manifest could not be saved: " + exception.Message);
            }
        }
    }

    [DefaultExecutionOrder(32002)]
    internal sealed class CombatCpuTimelineFrameObserver : MonoBehaviour
    {
        private void LateUpdate() => CombatCpuTimelineCapture.RecordFrame();
    }
}
