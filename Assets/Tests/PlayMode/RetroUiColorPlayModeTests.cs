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
                Color32 untreated = At(frame, 64, 64);
                Color32 actual = At(frame, 64, 184);
                Debug.Log($"HUD gamma probe: raw={untreated}, corrected={actual}.");
                AssertColor(actual, new Color32(25, 27, 27, 255), "Charcoal panel");
                AssertColor(At(frame, 180, 184), new Color32(205, 202, 187, 255), "Bone text swatch");
                AssertColor(At(frame, 290, 184), new Color32(60, 90, 120, 255), "sRGB texture");
                AssertColor(At(frame, 10, 284), new Color32(0, 0, 0, 255), "Group clips outside pixels");
                AssertColor(At(frame, 40, 284), new Color32(205, 202, 187, 255), "Group retains its origin");
                AssertColor(At(frame, 180, 284), new Color32(55, 58, 58, 255), "Canvas matrix translation");
                Color32 brightestGlyph = default;
                for (int y = 148; y < 184; y++)
                    for (int x = 372; x < 572; x++)
                    {
                        Color32 pixel = At(frame, x, y);
                        if (pixel.r > brightestGlyph.r) brightestGlyph = pixel;
                    }
                AssertColor(brightestGlyph, new Color32(205, 202, 187, 255), "Actual font glyph");
            }
            finally
            {
                Object.Destroy(uiObject);
                Object.Destroy(cameraObject);
                if (frame != null) Object.Destroy(frame);
            }
        }

        private static Color32 At(Texture2D frame, int x, int y) => frame.GetPixel(x, frame.height - y);
        private static void AssertColor(Color32 actual, Color32 expected, string context)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(2), context + " R");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(2), context + " G");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(2), context + " B");
        }
    }

    public sealed class RetroUiColorProbe : MonoBehaviour
    {
        private GUIStyle label;
        private Texture2D map, swatch;

        private void Awake()
        {
            map = Resources.Load<Texture2D>("UI/Maps/CityMapImage");
            swatch = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            swatch.SetPixel(0, 0, new Color32(60, 90, 120, 255));
            swatch.Apply(false, true);
        }

        private void OnDestroy() { if (swatch != null) Destroy(swatch); }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            label ??= RetroUiTheme.CreateLabelStyle(22, TextAnchor.MiddleLeft, RetroUiTheme.Text);
            Color previous = GUI.color;
            GUI.color = RetroUiTheme.Panel;
            GUI.DrawTexture(new Rect(24, 24, 100, 80), Texture2D.whiteTexture);
            GUI.color = previous;
            RetroUiTheme.FillRect(new Rect(24, 144, 100, 80), RetroUiTheme.Panel);
            RetroUiTheme.FillRect(new Rect(140, 144, 100, 80), RetroUiTheme.Text);
            RetroUiTexture.DrawTexture(new Rect(256, 144, 100, 80), swatch);
            GUI.Label(new Rect(372, 144, 200, 40), "Карта / HUD", label);
            GUI.BeginGroup(new Rect(24, 244, 100, 80));
            try { RetroUiTheme.FillRect(new Rect(-20, 0, 80, 80), RetroUiTheme.Text); }
            finally { GUI.EndGroup(); }
            Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Translate(new Vector3(140, 244, 0)) * matrix;
            try { RetroUiTheme.FillRect(new Rect(0, 0, 100, 80), RetroUiTheme.MapBuilding); }
            finally { GUI.matrix = matrix; }
            if (map != null)
            {
                GUI.DrawTexture(new Rect(600, 24, 480, 350), map, ScaleMode.ScaleToFit);
                RetroUiTexture.DrawTexture(new Rect(600, 394, 480, 350), map, ScaleMode.ScaleToFit);
            }
        }
    }
}
