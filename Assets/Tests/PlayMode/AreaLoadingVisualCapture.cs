using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
#endif

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// Explicit photographs of the real loading scene, including its OnGUI
    /// overlay. Run in a graphics-enabled Editor with Game View visible,
    /// without -batchmode/-nographics. Camera.Render omits this presentation.
    /// </summary>
    public sealed class AreaLoadingVisualCapture
    {
        private const float TimeoutSeconds = 10f;
        private const float Progress = 0.63f;
        private Scene loadingScene;
#if !UNITY_EDITOR
        private int previousWidth;
        private int previousHeight;
        private FullScreenMode previousMode;
        private bool resolutionOwned;
#endif
#if UNITY_EDITOR
        private GameViewResolution gameView;
#endif

        [Serializable]
        private sealed class CaptureReport
        {
            public string unityVersion;
            public string captureMethod = "ScreenCapture.CaptureScreenshot including OnGUI";
            public List<CapturedFrame> frames = new List<CapturedFrame>();
        }

        [Serializable]
        private sealed class CapturedFrame
        {
            public string file;
            public string source;
            public string destination;
            public string resource;
            public int requestedWidth;
            public int requestedHeight;
            public int screenWidth;
            public int screenHeight;
            public int pngWidth;
            public int pngHeight;
            public float progress;
            public Rect track;
        }

        [UnityTest]
        [Explicit("Visual capture: use visible Game View without -batchmode/-nographics; inspect Captures/AreaLoading/.")]
        public IEnumerator FourDirectionsAndThreeAspectRatios()
        {
            if (Application.isBatchMode ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Inconclusive("Loading art uses IMGUI. Run this explicit capture in a " +
                    "graphics-enabled, non-batch Editor with Game View visible. " +
                    "Batch Editor does not render Game View; a camera-only capture would omit the overlay.");
            }

            Assert.That(AreaTravelService.IsTraveling, Is.False);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
#if !UNITY_EDITOR
            previousWidth = Screen.width;
            previousHeight = Screen.height;
            previousMode = Screen.fullScreenMode;
            resolutionOwned = true;
#endif
#if UNITY_EDITOR
            gameView = new GameViewResolution();
#endif

            AsyncOperation load = SceneManager.LoadSceneAsync(
                SceneIds.AreaLoading, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(load.isDone, Is.True, "The empty loading scene did not activate.");
            loadingScene = SceneManager.GetSceneByName(SceneIds.AreaLoading);

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "Captures", "AreaLoading",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            Directory.CreateDirectory(folder);
            var report = new CaptureReport { unityVersion = Application.unityVersion };
            var sources = new[] { GameAreaId.City, GameAreaId.MountainRoad,
                GameAreaId.MountainRoad, GameAreaId.AlpineVillage };
            var destinations = new[] { GameAreaId.MountainRoad, GameAreaId.City,
                GameAreaId.AlpineVillage, GameAreaId.MountainRoad };
            var sizes = new[] { new Vector2Int(1920, 1080),
                new Vector2Int(1280, 960), new Vector2Int(2560, 1080) };

            for (int route = 0; route < sources.Length; route++)
            {
                AreaLoadingRoot root = BarPromenadeRuntimeBootstrap.EnsureAreaLoadingInstalled();
                Assert.That(root.IsBound, Is.False);
                root.Bind(new AreaTravelRequest(destinations[route]), sources[route]);
                root.SetProgress(Progress);
                Assert.That(root.HasArtwork, Is.True, root.ArtResourcePath);

                foreach (Vector2Int size in sizes)
                {
                    // Four artworks at 16:9; one also demonstrates 4:3 and
                    // ultrawide framing without repeating the same contract.
                    if (route != 0 && size != sizes[0]) continue;
#if UNITY_EDITOR
                    gameView.Set(size.x, size.y);
#else
                    Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
#endif
                    deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                    while ((Screen.width != size.x || Screen.height != size.y) &&
                           Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(size),
                        "Capture requires the requested actual framebuffer size; no rescaling or mislabeled aspect is allowed.");
                    yield return null;
                    yield return null;

                    string file = $"{sources[route]}-to-{destinations[route]}-{size.x}x{size.y}.png";
                    string path = Path.Combine(folder, file);
                    ScreenCapture.CaptureScreenshot(path);
                    deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                    Vector2Int pngSize = default;
                    while (!TryReadCompletedPngSize(path, out pngSize) &&
                           Time.realtimeSinceStartup < deadline)
                        yield return null;
                    Assert.That(pngSize, Is.EqualTo(size),
                        "No complete screenshot at the requested size. Keep Game View visible. " + path);

                    report.frames.Add(new CapturedFrame
                    {
                        file = file,
                        source = sources[route].ToString(),
                        destination = destinations[route].ToString(),
                        resource = root.ArtResourcePath,
                        requestedWidth = size.x,
                        requestedHeight = size.y,
                        screenWidth = Screen.width,
                        screenHeight = Screen.height,
                        pngWidth = pngSize.x,
                        pngHeight = pngSize.y,
                        progress = root.DisplayedProgress,
                        track = AreaLoadingRoot.CalculateTrackRect(Screen.width, Screen.height)
                    });
                    File.WriteAllText(Path.Combine(folder, "capture-report.json"),
                        JsonUtility.ToJson(report, true));
                    Debug.Log($"Area loading capture: {path}; requested {size.x}x{size.y}, " +
                        $"Screen {Screen.width}x{Screen.height}, PNG {pngSize.x}x{pngSize.y}.");
                }

                Object.Destroy(root.gameObject);
                yield return null;
            }

            Assert.That(report.frames.Count, Is.EqualTo(6));
            Debug.Log("Inspect all loading-art frames and their bottom progress bars: " + folder);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
#if UNITY_EDITOR
            gameView?.Dispose();
            gameView = null;
#else
            if (resolutionOwned)
                Screen.SetResolution(previousWidth, previousHeight, previousMode);
            resolutionOwned = false;
#endif
            if (loadingScene.IsValid() && loadingScene.isLoaded)
            {
                Scene blank = SceneManager.CreateScene("Area Loading Capture Cleanup");
                SceneManager.SetActiveScene(blank);
                yield return SceneManager.UnloadSceneAsync(loadingScene);
            }
        }

        private static bool TryReadCompletedPngSize(string path, out Vector2Int size)
        {
            size = default;
            if (!File.Exists(path)) return false;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length < 45) return false;
                    var header = new byte[24];
                    if (stream.Read(header, 0, header.Length) != header.Length ||
                        header[0] != 137 || header[1] != 80 || header[2] != 78 || header[3] != 71)
                        return false;
                    stream.Seek(-12, SeekOrigin.End);
                    var end = new byte[12];
                    if (stream.Read(end, 0, end.Length) != end.Length ||
                        end[4] != 73 || end[5] != 69 || end[6] != 78 || end[7] != 68)
                        return false;
                    size = new Vector2Int(ReadBigEndian(header, 16), ReadBigEndian(header, 20));
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static int ReadBigEndian(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
                   (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

#if UNITY_EDITOR
        // Screen.SetResolution does not set Editor Game View. Keep the
        // unavoidable version-dependent reflection confined to this capture.
        // Add one temporary fixed-resolution entry; never persist preferences.
        private sealed class GameViewResolution : IDisposable
        {
            private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            private readonly EditorWindow window;
            private readonly EditorWindow previousFocused;
            private readonly bool createdWindow;
            private readonly object group;
            private readonly object temporarySize;
            private readonly int temporaryIndex;
            private readonly int previousIndex;
            private readonly MethodInfo select;
            private readonly MethodInfo remove;
            private readonly PropertyInfo width;
            private readonly PropertyInfo height;

            public GameViewResolution()
            {
                Type viewType = FindEditorType("UnityEditor.GameView");
                Type sizesType = FindEditorType("UnityEditor.GameViewSizes");
                Type sizeType = FindEditorType("UnityEditor.GameViewSize");
                Type kindType = FindEditorType("UnityEditor.GameViewSizeType");
                object sizes = Required(sizesType.GetProperty("instance", Flags), "GameViewSizes.instance")
                    .GetValue(null);
                group = Required(sizesType.GetProperty("currentGroup", Flags), "GameViewSizes.currentGroup")
                    .GetValue(sizes);
                Type groupType = group.GetType();
                MethodInfo count = Required(groupType.GetMethod("GetTotalCount", Flags), "GetTotalCount");
                MethodInfo add = Required(groupType.GetMethod("AddCustomSize", Flags), "AddCustomSize");
                remove = Required(groupType.GetMethod("RemoveCustomSize", Flags), "RemoveCustomSize");
                select = Required(viewType.GetMethod("SizeSelectionCallback", Flags), "SizeSelectionCallback");
                PropertyInfo selected = Required(viewType.GetProperty("selectedSizeIndex", Flags), "selectedSizeIndex");
                width = Required(sizeType.GetProperty("width", Flags), "GameViewSize.width");
                height = Required(sizeType.GetProperty("height", Flags), "GameViewSize.height");
                temporarySize = Activator.CreateInstance(sizeType,
                    Enum.Parse(kindType, "FixedResolution"), 1920, 1080, "Loading art capture (temporary)");
                previousFocused = EditorWindow.focusedWindow;
                Object[] existing = Resources.FindObjectsOfTypeAll(viewType);
                createdWindow = existing.Length == 0;
                window = createdWindow ? EditorWindow.GetWindow(viewType) : (EditorWindow)existing[0];
                previousIndex = (int)selected.GetValue(window);
                temporaryIndex = (int)count.Invoke(group, null);
                add.Invoke(group, new[] { temporarySize });
            }

            public void Set(int requestedWidth, int requestedHeight)
            {
                width.SetValue(temporarySize, requestedWidth);
                height.SetValue(temporarySize, requestedHeight);
                // Returning through the original selection also refreshes the
                // view when the temporary entry's dimensions have changed.
                select.Invoke(window, new object[] { previousIndex, null });
                select.Invoke(window, new object[] { temporaryIndex, null });
                window.Show();
                window.Focus();
                window.Repaint();
            }

            public void Dispose()
            {
                try
                {
                    if (window != null)
                        select.Invoke(window, new object[] { previousIndex, null });
                }
                finally
                {
                    remove.Invoke(group, new object[] { temporaryIndex });
                    if (createdWindow && window != null) window.Close();
                    if (previousFocused != null) previousFocused.Focus();
                }
            }

            private static T Required<T>(T member, string name) where T : class
            {
                if (member == null)
                    throw new NotSupportedException("Unity " + Application.unityVersion +
                        " does not expose the capture's Game View API: " + name);
                return member;
            }

            private static Type FindEditorType(string name)
            {
                Type type = typeof(EditorWindow).Assembly.GetType(name, false) ??
                    Type.GetType(name + ", UnityEditor", false) ??
                    Type.GetType(name + ", UnityEditor.CoreModule", false);
                if (type != null) return type;
                throw new NotSupportedException("Unity " + Application.unityVersion +
                    " does not expose the capture's Game View type: " + name);
            }
        }
#endif
    }
}
