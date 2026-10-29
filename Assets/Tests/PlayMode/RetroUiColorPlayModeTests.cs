using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class RetroUiColorPlayModeTests
    {
        [UnityTest]
        [Explicit("IMGUI pixel regression requires a visible Editor Game view; run without -batchmode.")]
        public IEnumerator HudOverlay_PreservesAuthoredGammaColors()
        {
            if (Application.isBatchMode || !Application.isEditor)
                Assert.Ignore("IMGUI colour readback requires the ordinary Editor Game view.");
#if UNITY_EDITOR
            System.Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Assert.That(viewType, Is.Not.Null);
            UnityEditor.EditorWindow gameView = UnityEditor.EditorWindow.GetWindow(viewType);
            gameView.Show();
            gameView.Focus();
#endif
            var cameraObject = new GameObject("UI colour camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            var uiObject = new GameObject("UI colour probe", typeof(RetroUiColorProbe));
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HudColors");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "hud-colors.png");
            if (File.Exists(path)) File.Delete(path);
            Texture2D frame = null;
            try
            {
                yield return null;
                yield return null;
                ScreenCapture.CaptureScreenshot(path);
                float deadline = Time.realtimeSinceStartup + 10f;
                while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(path), Is.True, "The actual IMGUI overlay must be captured.");
                frame = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Assert.That(frame.LoadImage(File.ReadAllBytes(path)), Is.True);
                RetroUiColorProbe probe = uiObject.GetComponent<RetroUiColorProbe>();
                Color32 untreated = frame.GetPixel(64, frame.height - 64);
                Color32 actual = frame.GetPixel(64, frame.height - 184);
                Debug.Log($"HUD gamma probe: inherited={probe.InheritedSrgbWrite}, untreated sRGB write={untreated}, shared gamma scope={actual}.");
                Assert.That(actual.r, Is.EqualTo(25).Within(2), "Charcoal UI must retain its authored brightness.");
                Assert.That(actual.g, Is.EqualTo(27).Within(2));
                Assert.That(actual.b, Is.EqualTo(27).Within(2));
                Assert.That(probe.RestoredSrgbWrite, Is.True, "The scope must restore its caller's GPU state.");
                Assert.That(probe.NestedScopeKeptGamma, Is.True, "Nested UI must retain gamma writes until the outer scope exits.");
                Color32 text = frame.GetPixel(180, frame.height - 184);
                Assert.That(text.r, Is.EqualTo(205).Within(2));
                Assert.That(text.g, Is.EqualTo(202).Within(2));
                Assert.That(text.b, Is.EqualTo(187).Within(2));
            }
            finally
            {
                Object.Destroy(uiObject);
                Object.Destroy(cameraObject);
                if (frame != null) Object.Destroy(frame);
            }
        }
    }

    public sealed class RetroUiColorProbe : MonoBehaviour
    {
        public bool InheritedSrgbWrite { get; private set; }
        public bool RestoredSrgbWrite { get; private set; }
        public bool NestedScopeKeptGamma { get; private set; }
        private GUIStyle label;
        private Texture2D map;

        private void Awake() => map = Resources.Load<Texture2D>("UI/Maps/CityMapImage");

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            label ??= RetroUiTheme.CreateLabelStyle(22, TextAnchor.MiddleLeft, RetroUiTheme.Text);
            InheritedSrgbWrite = GL.sRGBWrite;
            bool previous = GL.sRGBWrite;
            try
            {
                // Model the Editor's sRGB target state without changing any authored colours.
                GL.sRGBWrite = true;
                DrawRow(24);
                using (RetroUiTheme.BeginGui())
                {
                    using (RetroUiTheme.BeginGui()) DrawRow(144);
                    NestedScopeKeptGamma = !GL.sRGBWrite;
                }
                RestoredSrgbWrite = GL.sRGBWrite;
            }
            finally { GL.sRGBWrite = previous; }
        }

        private void DrawRow(float y)
        {
            RetroUiTheme.FillRect(new Rect(24, y, 100, 80), RetroUiTheme.Panel);
            RetroUiTheme.FillRect(new Rect(140, y, 100, 80), RetroUiTheme.Text);
            RetroUiTheme.FillRect(new Rect(256, y, 100, 80), RetroUiTheme.MapBuilding);
            GUI.Label(new Rect(372, y, 200, 40), "Карта / HUD", label);
            if (map != null) GUI.DrawTexture(new Rect(24, y + 240, 548, 150), map, ScaleMode.ScaleToFit);
        }
    }
}
