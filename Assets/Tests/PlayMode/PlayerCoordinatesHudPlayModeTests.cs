using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class PlayerCoordinatesHudPlayModeTests
    {
        private readonly List<Behaviour> suspended = new List<Behaviour>();
        private Scene previousScene, firstScene, replacementScene;
        private bool previousEnabled, previousTransition, previousBackground;
        private float previousTimeScale;
        private CultureInfo previousCulture;
        private BarMinigameModalLock modal;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousScene = SceneManager.GetActiveScene();
            previousEnabled = PlayerCoordinatesHud.CoordinatesEnabled;
            previousTransition = SceneTransitionService.IsTransitioning;
            previousBackground = Application.runInBackground;
            previousTimeScale = Time.timeScale;
            previousCulture = CultureInfo.CurrentCulture;
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)) Suspend(camera);
            foreach (PlayerCoordinatesHud hud in Object.FindObjectsByType<PlayerCoordinatesHud>(FindObjectsInactive.Exclude)) Suspend(hud);
            foreach (StartMenuRoot menu in Object.FindObjectsByType<StartMenuRoot>(FindObjectsInactive.Exclude)) Suspend(menu);
            Time.timeScale = 1f;
            Application.runInBackground = true;
            SetTransition(false);
            typeof(PlayerCoordinatesHud).GetMethod("ResetSession", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, null);
            firstScene = SceneManager.CreateScene("Coordinates HUD first scene");
            Assert.That(SceneManager.SetActiveScene(firstScene), Is.True);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            modal?.Restore();
            CultureInfo.CurrentCulture = previousCulture;
            PlayerCoordinatesHud.CoordinatesEnabled = previousEnabled;
            SetTransition(previousTransition);
            Application.runInBackground = previousBackground;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (replacementScene.IsValid() && replacementScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(replacementScene);
            if (firstScene.IsValid() && firstScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(firstScene);
            foreach (Behaviour behaviour in suspended)
                if (behaviour != null) behaviour.enabled = true;
            suspended.Clear();
            Time.timeScale = previousTimeScale;
        }

        [UnityTest]
        public IEnumerator Coordinates_DefaultOnInvariantAndPassive_AcrossPlayerReplacement()
        {
            Assert.That(PlayerCoordinatesHud.CoordinatesEnabled, Is.True, "Each application/play session starts enabled.");
            var player = new GameObject("Coordinates test player");
            player.transform.position = new Vector3(-1234.5f, 67.8f, -987.6f);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            PlayerCoordinatesHud hud = player.AddComponent<PlayerCoordinatesHud>();
            hud.RefreshCoordinates();
            Assert.That(hud.SceneLabel, Is.EqualTo(firstScene.name));
            Assert.That(hud.CoordinatesLabel, Is.EqualTo("X -1234.5   Y 67.8   Z -987.6"),
                "Screenshot reports keep explicit axes and decimal points regardless of OS culture.");
            Assert.That(hud.ShouldRender, Is.True);
            AssertLayouts();

            PlayerCoordinatesHud.CoordinatesEnabled = false;
            Assert.That(hud.ShouldRender, Is.False);
            PlayerCoordinatesHud.CoordinatesEnabled = true;
            hud.enabled = false;
            Assert.That(hud.ShouldRender, Is.False);
            hud.enabled = true;
            Assert.That(hud.ShouldRender, Is.True);
            SetTransition(true);
            Assert.That(hud.ShouldRender, Is.False);
            SetTransition(false);
            using (GameTimeScaleRuntime.AcquirePause()) Assert.That(hud.ShouldRender, Is.False);
            Assert.That(hud.ShouldRender, Is.True);
            PlayerInteractor interactor = player.AddComponent<PlayerInteractor>();
            interactor.Initialize(null);
            modal = new BarMinigameModalLock();
            Assert.That(modal.TryCaptureAndDisable(interactor, null, null), Is.True);
            Assert.That(hud.ShouldRender, Is.False);
            Assert.That(modal.Restore(), Is.True);
            Assert.That(hud.ShouldRender, Is.True);

            PlayerCoordinatesHud.CoordinatesEnabled = false;
            hud.enabled = false;
            replacementScene = SceneManager.CreateScene("Coordinates HUD replacement scene");
            Assert.That(SceneManager.SetActiveScene(replacementScene), Is.True);
            var replacementPlayer = new GameObject("Coordinates replacement player");
            replacementPlayer.transform.position = player.transform.position;
            PlayerCoordinatesHud replacement = replacementPlayer.AddComponent<PlayerCoordinatesHud>();
            Assert.That(replacement.ShouldRender, Is.False, "Scene/player replacement retains the selected flag.");
            Assert.That(replacement.SceneLabel, Is.EqualTo(replacementScene.name));
            PlayerCoordinatesHud.CoordinatesEnabled = true;
            Assert.That(replacement.ShouldRender, Is.True);
            Assert.That(hud.ShouldRender, Is.False, "A retained disabled exterior must not draw another panel.");
            string previousLabel = replacement.CoordinatesLabel;
            replacementPlayer.transform.position += Vector3.right;
            float deadline = Time.realtimeSinceStartup + 1f;
            while (replacement.CoordinatesLabel == previousLabel && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(replacement.CoordinatesLabel, Is.EqualTo("X -1233.5   Y 67.8   Z -987.6"),
                "The live presentation samples the player transform without a manual refresh.");
            replacementPlayer.transform.position = player.transform.position;
            replacement.RefreshCoordinates();

            var view = new GameObject("Coordinates screenshot camera").AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.cullingMask = 0;
            var measurement = replacementPlayer.AddComponent<PlayerCoordinatesHudTextMeasure>();
            measurement.Hud = replacement;
            yield return Capture(view, Color.white, "coordinates-bright");
            yield return Capture(view, new Color(.015f, .015f, .015f), "coordinates-dark");
            if (!Application.isBatchMode)
            {
                Assert.That(measurement.HasMeasured, Is.True, "Text fit is measured during an actual GUI repaint.");
                float availableWidth = PlayerCoordinatesHud.ResolvePanelRect(
                    RetroUiTheme.CalculateCanvas(Screen.width, Screen.height)).width - 14f;
                Assert.That(measurement.CoordinatesWidth, Is.LessThanOrEqualTo(availableWidth), "All three axes must fit in a screenshot.");
                Assert.That(measurement.SceneWidth, Is.LessThanOrEqualTo(availableWidth));
            }
        }

        private static void AssertLayouts()
        {
            foreach (Vector2Int resolution in new[] { new Vector2Int(640, 360), new Vector2Int(1280, 720),
                         new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) })
            {
                RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(resolution.x, resolution.y);
                Rect panel = PlayerCoordinatesHud.ResolvePanelRect(canvas);
                Rect screen = canvas.LogicalToScreen(panel);
                Assert.That(screen.xMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(screen.yMin, Is.GreaterThanOrEqualTo(0f));
                Assert.That(screen.xMax, Is.LessThanOrEqualTo(resolution.x));
                Assert.That(screen.yMax, Is.LessThanOrEqualTo(resolution.y));
                Assert.That(resolution.x - screen.xMax, Is.EqualTo(10f * canvas.Scale).Within(1f));
                Assert.That(resolution.y - screen.yMax, Is.EqualTo(10f * canvas.Scale).Within(1f));
                Assert.That(panel.Overlaps(IntoxicationHudView.ResolvePanelRect(canvas)), Is.False);
                Assert.That(panel.Overlaps(PaymentNoticeView.ResolvePanelRect()), Is.False);
                Assert.That(panel.Overlaps(JournalNoticeView.ResolveIconRect(canvas)), Is.False);
                Assert.That(panel.Overlaps(JournalNoticeView.ResolveHintRect(canvas)), Is.False);
                foreach (bool laboratory in new[] { false, true })
                foreach (bool debug in new[] { false, true })
                {
                    var combat = new CombatHudLayout(canvas, laboratory, debug);
                    Assert.That(panel.Overlaps(combat.Laboratory), Is.False);
                    Assert.That(panel.Overlaps(combat.Endless), Is.False);
                    Assert.That(panel.Overlaps(combat.Toolbar), Is.False);
                    Assert.That(panel.Overlaps(combat.Result), Is.False);
                    Assert.That(combat.ContainsControls(panel.center, laboratory, debug, true), Is.False,
                        "The passive coordinate panel cannot reserve combat pointer input.");
                    Rect raised = panel;
                    foreach (Rect occupied in new[] { combat.Help, combat.Opponent,
                                 combat.LaboratoryHelp, combat.Ammunition, combat.Reload,
                                 new Rect(230f, 319f, 180f, 24f), new Rect(60f, 223f, 520f, 120f) })
                    {
                        raised = PlayerCoordinatesHud.AvoidPanel(raised, occupied);
                        Assert.That(raised.Overlaps(occupied), Is.False,
                            "The coordinate panel lifts above bottom prompts and combat instruments.");
                    }
                    Assert.That(raised.x, Is.EqualTo(panel.x), "Avoidance retains the right edge.");
                    Assert.That(raised.yMin, Is.GreaterThanOrEqualTo(canvas.ViewportRect.yMin));
                }
            }
        }

        private static IEnumerator Capture(Camera view, Color background, string name)
        {
            // Camera.Render omits IMGUI; only ScreenCapture from a real Game view proves this HUD.
            if (Application.isBatchMode) yield break;
            view.backgroundColor = background;
#if UNITY_EDITOR
            Type viewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            UnityEditor.EditorWindow gameView = UnityEditor.EditorWindow.GetWindow(viewType);
            gameView.Show();
            gameView.Focus();
            gameView.Repaint();
#endif
            yield return null;
            yield return null;
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "Coordinates", name + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DateTime previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 3f;
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previousWrite) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previousWrite, Is.True,
                "The real Game view must capture the coordinate HUD against both backgrounds.");
        }

        private void Suspend(Behaviour behaviour)
        {
            if (!behaviour.enabled) return;
            suspended.Add(behaviour);
            behaviour.enabled = false;
        }

        private static void SetTransition(bool value) => typeof(SceneTransitionService)
            .GetProperty(nameof(SceneTransitionService.IsTransitioning), BindingFlags.Public | BindingFlags.Static)
            .GetSetMethod(true).Invoke(null, new object[] { value });
    }

    internal sealed class PlayerCoordinatesHudTextMeasure : MonoBehaviour
    {
        internal PlayerCoordinatesHud Hud;
        internal bool HasMeasured;
        internal float CoordinatesWidth, SceneWidth;
        private GUIStyle sceneStyle, coordinatesStyle;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || Hud == null || !Hud.ShouldRender) return;
            sceneStyle ??= RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleLeft, RetroUiTheme.Text);
            coordinatesStyle ??= RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.AccentPale);
            SceneWidth = sceneStyle.CalcSize(new GUIContent(Hud.SceneLabel)).x;
            CoordinatesWidth = coordinatesStyle.CalcSize(new GUIContent(Hud.CoordinatesLabel)).x;
            HasMeasured = true;
        }
    }
}
