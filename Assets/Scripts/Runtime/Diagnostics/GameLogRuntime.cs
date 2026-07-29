using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    internal static class GameLogRuntime
    {
        private static readonly HashSet<SceneHandle> LoadedSceneHandles =
            new HashSet<SceneHandle>();
        private static readonly GameLogRepeatGate UnityLogRepeatGate =
            new GameLogRepeatGate();
        private static readonly GameLogRateGate UnityWarningRateGate =
            new GameLogRateGate(
                UnityLogRateWindowMilliseconds,
                UnityWarningEventBudget);
        private static readonly GameLogRateGate UnityErrorRateGate =
            new GameLogRateGate(
                UnityLogRateWindowMilliseconds,
                UnityErrorEventBudget);

        private const int UnityWarningEventBudget = 32;
        private const int UnityErrorEventBudget = 64;
        private const int UnityLogRateWindowMilliseconds = 10000;

        [ThreadStatic]
        private static bool capturingUnityLog;

        private static GameLogRuntimeHost host;
        private static bool shuttingDown;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Unsubscribe();
            shuttingDown = true;
            GameLog.Shutdown("subsystem_reset", false);
            shuttingDown = false;
            host = null;
            LoadedSceneHandles.Clear();
            UnityLogRepeatGate.Reset();
            UnityWarningRateGate.Reset();
            UnityErrorRateGate.Reset();
            capturingUnityLog = false;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeFirstScene()
        {
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
                const bool isEditor = true;
#else
                const bool isEditor = false;
#endif
                GameLogSettings settings = GameLogSettings.CreateRuntime(
                    arguments,
                    Application.isBatchMode,
                    isEditor,
                    Debug.isDebugBuild,
                    Application.dataPath,
                    Application.persistentDataPath);
                GameLog.Initialize(
                    settings,
                    new SystemGameLogClock(),
                    Guid.NewGuid().ToString("N"));
                if (GameLog.Profile == GameLogProfile.Off)
                {
                    return;
                }

                Subscribe();
                GameObject hostObject =
                    new GameObject("[Bar Promenade] Diagnostics");
                hostObject.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(hostObject);
                host = hostObject.AddComponent<GameLogRuntimeHost>();

                Scene activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    GameLog.SetScene(activeScene.name);
                }

                GameLog.Info(
                    "session",
                    "start",
                    BuildSessionStartFields(
                        settings.Profile,
                        isEditor));

                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    ReportSceneLoaded(
                        activeScene,
                        LoadSceneMode.Single,
                        true);
                }
            }
            catch
            {
                GameLog.Shutdown("initialization_failed", false);
            }
        }

        internal static void HandleHostDestroyed(
            GameLogRuntimeHost destroyedHost)
        {
            if (shuttingDown || destroyedHost != host)
            {
                return;
            }

            Shutdown("runtime_stopped");
        }

        internal static bool ShouldCaptureUnityLog(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return true;
                default:
                    return false;
            }
        }

        internal static void HandleApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                GameLog.Flush();
            }
        }

        internal static void HandleApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                GameLog.Flush();
            }
        }

        internal static GameLogField[] BuildSessionStartFields(
            GameLogProfile selectedProfile,
            bool isEditor)
        {
            List<GameLogField> fields = new List<GameLogField>(24)
            {
                GameLog.Field("product", Application.productName),
                GameLog.Field("version", Application.version),
                GameLog.Field("build_guid", Application.buildGUID),
                GameLog.Field("unity", Application.unityVersion),
                GameLog.Field(
                    "platform",
                    Application.platform.ToString()),
                GameLog.Field(
                    "language",
                    Application.systemLanguage.ToString()),
                GameLog.Field(
                    "build_type",
                    GetBuildType(isEditor)),
                GameLog.Field(
                    "profile",
                    FormatProfile(selectedProfile)),
                GameLog.Field("quality", GetQualityName()),
                GameLog.Field(
                    "resolution_width",
                    Math.Max(0, Screen.width)),
                GameLog.Field(
                    "resolution_height",
                    Math.Max(0, Screen.height)),
                GameLog.Field(
                    "fullscreen_mode",
                    Screen.fullScreenMode.ToString())
            };

            if (selectedProfile == GameLogProfile.Verbose)
            {
                fields.Add(
                    GameLog.Field(
                        "operating_system",
                        SystemInfo.operatingSystem));
                fields.Add(
                    GameLog.Field(
                        "device_model",
                        SystemInfo.deviceModel));
                fields.Add(
                    GameLog.Field(
                        "processor",
                        SystemInfo.processorType));
                fields.Add(
                    GameLog.Field(
                        "processor_count",
                        Math.Max(0, SystemInfo.processorCount)));
                fields.Add(
                    GameLog.Field(
                        "system_memory_mb",
                        Math.Max(0, SystemInfo.systemMemorySize)));
                fields.Add(
                    GameLog.Field(
                        "graphics_device",
                        SystemInfo.graphicsDeviceName));
                fields.Add(
                    GameLog.Field(
                        "graphics_vendor",
                        SystemInfo.graphicsDeviceVendor));
                fields.Add(
                    GameLog.Field(
                        "graphics_memory_mb",
                        Math.Max(0, SystemInfo.graphicsMemorySize)));
                fields.Add(
                    GameLog.Field(
                        "graphics_api",
                        SystemInfo.graphicsDeviceType.ToString()));
                fields.Add(
                    GameLog.Field(
                        "graphics_api_version",
                        SystemInfo.graphicsDeviceVersion));
            }

            return fields.ToArray();
        }

        private static void Subscribe()
        {
            Application.logMessageReceivedThreaded -= HandleUnityLog;
            Application.logMessageReceivedThreaded += HandleUnityLog;
            Application.quitting -= HandleApplicationQuitting;
            Application.quitting += HandleApplicationQuitting;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private static void Unsubscribe()
        {
            Application.logMessageReceivedThreaded -= HandleUnityLog;
            Application.quitting -= HandleApplicationQuitting;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        }

        private static void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode mode)
        {
            ReportSceneLoaded(scene, mode, false);
        }

        private static void ReportSceneLoaded(
            Scene scene,
            LoadSceneMode mode,
            bool initial)
        {
            if (!scene.IsValid() ||
                !LoadedSceneHandles.Add(scene.handle))
            {
                return;
            }

            if (mode == LoadSceneMode.Single ||
                scene == SceneManager.GetActiveScene())
            {
                GameLog.SetScene(scene.name);
            }

            GameLog.Info(
                "scene",
                "loaded",
                GameLog.Field("name", scene.name),
                GameLog.Field("build_index", scene.buildIndex),
                GameLog.Field(
                    "load_mode",
                    initial ? "initial" : mode.ToString()));
            host?.ScheduleReady(scene);
        }

        private static void HandleSceneUnloaded(Scene scene)
        {
            LoadedSceneHandles.Remove(scene.handle);
        }

        private static void HandleApplicationQuitting()
        {
            Shutdown("application_quit");
        }

        private static void Shutdown(string reason)
        {
            if (shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            Unsubscribe();
            GameLog.Shutdown(reason, true);
            LoadedSceneHandles.Clear();
            host = null;
        }

        private static void HandleUnityLog(
            string condition,
            string stackTrace,
            LogType type)
        {
            if (!ShouldCaptureUnityLog(type) ||
                capturingUnityLog ||
                GameLog.Profile == GameLogProfile.Off)
            {
                return;
            }

            try
            {
                long monotonicMilliseconds =
                    GetMonotonicMilliseconds();
                string signature = CreateUnityLogSignature(
                    condition,
                    stackTrace,
                    type);
                GameLogRepeatDecision repeat =
                    UnityLogRepeatGate.Observe(
                        signature,
                        monotonicMilliseconds);
                if (repeat.Action ==
                    GameLogRepeatAction.Suppress)
                {
                    return;
                }

                GameLogLevel level = ToGameLogLevel(type);
                bool isWarning =
                    level == GameLogLevel.Warning;
                GameLogRateGate rateGate = isWarning
                    ? UnityWarningRateGate
                    : UnityErrorRateGate;
                GameLogRateDecision rate =
                    rateGate.Observe(monotonicMilliseconds);
                if (rate.EmitSummary)
                {
                    capturingUnityLog = true;
                    ReportUnityRateLimit(
                        rate.DroppedCount,
                        isWarning);
                }

                if (!rate.AllowMessage)
                {
                    return;
                }

                capturingUnityLog = true;
                string eventName = ToUnityEventName(type);
                if (repeat.Action ==
                    GameLogRepeatAction.EmitSummary)
                {
                    GameLog.Write(
                        level,
                        "unity",
                        eventName + "_repeated",
                        new[]
                        {
                            GameLog.Field(
                                "message",
                                condition ?? string.Empty),
                            GameLog.Field("type", type.ToString()),
                            GameLog.Field(
                                "repeat_signature",
                                signature),
                            GameLog.Field(
                                "occurrence_count",
                                repeat.OccurrenceCount),
                            GameLog.Field(
                                "quiet_window_ms",
                                10000)
                        });
                    return;
                }

                if (string.IsNullOrEmpty(stackTrace))
                {
                    GameLog.Write(
                        level,
                        "unity",
                        eventName,
                        new[]
                        {
                            GameLog.Field(
                                "message",
                                condition ?? string.Empty),
                            GameLog.Field("type", type.ToString()),
                            GameLog.Field(
                                "repeat_signature",
                                signature)
                        });
                }
                else
                {
                    GameLog.Write(
                        level,
                        "unity",
                        eventName,
                        new[]
                        {
                            GameLog.Field(
                                "message",
                                condition ?? string.Empty),
                            GameLog.Field("type", type.ToString()),
                            GameLog.Field(
                                "repeat_signature",
                                signature),
                            GameLog.Field("stack_trace", stackTrace)
                        });
                }
            }
            catch
            {
                // Never report diagnostics failures through Unity logging.
            }
            finally
            {
                capturingUnityLog = false;
            }
        }

        private static void ReportUnityRateLimit(
            int droppedCount,
            bool isWarning)
        {
            GameLog.Warning(
                "unity",
                "messages_rate_limited",
                GameLog.Field(
                    "dropped_count",
                    Math.Max(0, droppedCount)),
                GameLog.Field(
                    "event_budget",
                    isWarning
                        ? UnityWarningEventBudget
                        : UnityErrorEventBudget),
                GameLog.Field(
                    "severity_bucket",
                    isWarning ? "warning" : "error"),
                GameLog.Field(
                    "window_ms",
                    UnityLogRateWindowMilliseconds));
        }

        internal static string CreateUnityLogSignature(
            string condition,
            string stackTrace,
            LogType type)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;
                AddToHash(ref hash, condition, prime);
                hash ^= 0xffUL;
                hash *= prime;
                AddToHash(ref hash, stackTrace, prime);
                return type + "-" + hash.ToString("x16");
            }
        }

        private static void AddToHash(
            ref ulong hash,
            string value,
            ulong prime)
        {
            if (value == null)
            {
                return;
            }

            int characterCount = Math.Min(
                value.Length,
                GameLogFormatter.MaxStringCharacters);
            for (int index = 0; index < characterCount; index++)
            {
                hash ^= value[index];
                hash *= prime;
            }

            hash ^= (ulong)value.Length;
            hash *= prime;
        }

        private static long GetMonotonicMilliseconds()
        {
            return Math.Max(
                0L,
                (long)(
                    Stopwatch.GetTimestamp() *
                    (1000d / Stopwatch.Frequency)));
        }

        private static GameLogLevel ToGameLogLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return GameLogLevel.Warning;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return GameLogLevel.Error;
                default:
                    return GameLogLevel.Warning;
            }
        }

        private static string ToUnityEventName(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return "warning";
                case LogType.Error:
                    return "error";
                case LogType.Assert:
                    return "assert";
                case LogType.Exception:
                    return "exception";
                default:
                    return "message";
            }
        }

        private static string GetQualityName()
        {
            string[] names = QualitySettings.names;
            int qualityLevel = QualitySettings.GetQualityLevel();
            return names != null &&
                   qualityLevel >= 0 &&
                   qualityLevel < names.Length
                ? names[qualityLevel]
                : qualityLevel.ToString();
        }

        private static string GetBuildType(bool isEditor)
        {
            if (isEditor)
            {
                return "editor";
            }

            return Debug.isDebugBuild ? "development" : "release";
        }

        private static string FormatProfile(GameLogProfile value)
        {
            switch (value)
            {
                case GameLogProfile.Basic:
                    return "basic";
                case GameLogProfile.Verbose:
                    return "verbose";
                default:
                    return "off";
            }
        }
    }

    internal sealed class GameLogRuntimeHost : MonoBehaviour
    {
        internal void ScheduleReady(Scene scene)
        {
            StartCoroutine(ReportReadyNextFrame(scene));
        }

        private static IEnumerator ReportReadyNextFrame(Scene scene)
        {
            yield return null;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                yield break;
            }

            if (scene == SceneManager.GetActiveScene())
            {
                GameLog.SetScene(scene.name);
            }

            GameLog.Info(
                "scene",
                "ready",
                GameLog.Field("name", scene.name),
                GameLog.Field("build_index", scene.buildIndex),
                GameLog.Field("frames_after_load", 1));
        }

        private void OnDestroy()
        {
            GameLogRuntime.HandleHostDestroyed(this);
        }

        private void OnApplicationPause(bool isPaused)
        {
            GameLogRuntime.HandleApplicationPause(isPaused);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            GameLogRuntime.HandleApplicationFocus(hasFocus);
        }
    }
}
