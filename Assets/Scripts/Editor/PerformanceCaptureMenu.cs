using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade.Editor
{
    internal static class PerformanceCaptureMenu
    {
        private const string StartPath = "Tools/Bar Promenade/Diagnostics/Capture Performance (30 seconds)";
        private const string StopPath = "Tools/Bar Promenade/Diagnostics/Stop Performance Capture";

        [MenuItem(StartPath)]
        private static void Start()
        {
            RuntimePerformanceCapture.StartCapture(new PerformanceCaptureOptions(
                SceneManager.GetActiveScene().name, "editor-manual"));
        }

        [MenuItem(StartPath, true)]
        private static bool CanStart() => Application.isPlaying && !RuntimePerformanceCapture.IsRunning;

        [MenuItem(StopPath)]
        private static void Stop() => RuntimePerformanceCapture.StopCapture();

        [MenuItem(StopPath, true)]
        private static bool CanStop() => Application.isPlaying && RuntimePerformanceCapture.IsRunning;
    }
}
