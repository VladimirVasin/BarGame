using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>Real repaint/hitboxes driven by Input System devices, without native IMGUI events.</summary>
    public sealed class InputSystemUiPlayModeTests
    {
        private InputTestFixture devices;
        private Mouse mouse;
        private Keyboard keyboard;
        private GameObject probeObject, playerObject, pauseObject;
        private InputSystemUiProbe probe;
        private StartMenuRoot disabledPointerMenu;
        private bool pointerMenuWasEnabled;
        private float previousCaptureDelta;
        private string previousClipboard;
#if UNITY_EDITOR
        private UnityEditor.EditorWindow gameView, previousFocused;
        private bool createdGameView;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            Assert.Fail("This regression must run with Input System Package only, not Both.");
#endif
            Assert.That(Application.isBatchMode, Is.False, "Run with a visible Game View: repaint is part of this contract.");
            previousCaptureDelta = Time.captureDeltaTime;
            previousClipboard = GUIUtility.systemCopyBuffer;
            Time.captureDeltaTime = 1f / 30f;
#if UNITY_EDITOR
            previousFocused = UnityEditor.EditorWindow.focusedWindow;
            System.Type viewType = null;
            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                viewType = assembly.GetType("UnityEditor.GameView");
                if (viewType != null) break;
            }
            Assert.That(viewType, Is.Not.Null);
            Object[] windows = Resources.FindObjectsOfTypeAll(viewType);
            createdGameView = windows.Length == 0;
            gameView = createdGameView ? UnityEditor.EditorWindow.GetWindow(viewType) : (UnityEditor.EditorWindow)windows[0];
            gameView.Show();
            gameView.Focus();
#endif
            devices = new InputTestFixture();
            devices.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            SceneManager.LoadScene(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return Frames();
            GameInput.HandleApplicationFocus(true);
            RetroUiInput.Reset();
            yield return Pointer(Vector2.zero);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (pauseObject != null) Object.Destroy(pauseObject);
            if (playerObject != null) Object.Destroy(playerObject);
            if (probe != null) GameCursor.Release(probe);
            if (disabledPointerMenu != null) disabledPointerMenu.enabled = pointerMenuWasEnabled;
            disabledPointerMenu = null;
            if (probeObject != null) Object.Destroy(probeObject);
            yield return null;
            GameInput.HandleApplicationFocus(true);
            RetroUiInput.Reset();
            GUIUtility.systemCopyBuffer = previousClipboard;
            devices?.TearDown();
            Time.captureDeltaTime = previousCaptureDelta;
#if UNITY_EDITOR
            if (createdGameView && gameView != null) gameView.Close();
            if (previousFocused != null) previousFocused.Focus();
#endif
        }

        [UnityTest]
        public IEnumerator Pointer_UsesScaledGroupsSingleReleaseScrollAndFocusCleanup()
        {
            disabledPointerMenu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(disabledPointerMenu, Is.Not.Null);
            pointerMenuWasEnabled = disabledPointerMenu.enabled;
            disabledPointerMenu.enabled = false;
            probeObject = new GameObject("Input System repaint probe");
            probe = probeObject.AddComponent<InputSystemUiProbe>();
            GameCursor.Capture(probe, CursorLockMode.None, true);
            yield return Frames();
            Assert.That(probe.Paints, Is.GreaterThan(0), "A headless stand-in cannot prove GUI input.");
            Vector2 inside = InputSystemUiProbe.WindowPoint(InputSystemUiProbe.ButtonRect.center);
            yield return Pointer(inside);
            Assert.That(Vector2.Distance(probe.LocalPointer, InputSystemUiProbe.ButtonRect.center), Is.LessThan(.5f),
                "Canvas scale and GUI.BeginGroup must share the visible hitbox.");
            yield return Pointer(inside, 1);
            Assert.That(probe.Clicks, Is.Zero, "Buttons activate on release.");
            yield return Pointer(inside);
            Assert.That(probe.Clicks, Is.EqualTo(1));
            Assert.That(probe.CoveredClicks, Is.Zero, "A release is consumed once across overlapping controls.");

            yield return Pointer(inside, 1);
            yield return Pointer(inside + Vector2.right * 200f);
            Assert.That(probe.Clicks, Is.EqualTo(1), "Release outside must cancel.");
            yield return Pointer(inside + Vector2.right * 200f, 1);
            yield return Pointer(inside);
            Assert.That(probe.Clicks, Is.EqualTo(1), "A press outside cannot become a click inside.");

            yield return Pointer(inside, 0, new Vector2(0f, 120f));
            Assert.That(probe.LastScroll.y, Is.EqualTo(-3f));
            Assert.That(probe.ScrollAfterConsume, Is.EqualTo(Vector2.zero));
            yield return Pointer(inside, 2);
            yield return Pointer(inside + Vector2.right * 12f, 2);
            Assert.That(probe.RightDrags, Is.GreaterThan(0));
            Assert.That(probe.DragAfterConsume, Is.False);
            yield return Pointer(inside);

            yield return Pointer(inside, 1);
            GameInput.HandleApplicationFocus(false);
            yield return Frames();
            GameInput.HandleApplicationFocus(true);
            yield return Pointer(inside);
            Assert.That(probe.Clicks, Is.EqualTo(1), "Focus loss discards capture and a delayed release.");
            yield return Click(inside);
            Assert.That(probe.Clicks, Is.EqualTo(2));

            yield return Pointer(inside, 1);
            probe.SkipPointerPass = true;
            yield return Pointer(inside, 1);
            yield return Frames();
            probe.SkipPointerPass = false;
            yield return Pointer(inside);
            Assert.That(probe.Clicks, Is.EqualTo(3),
                "A held press survives skipped pointer UI repaints and completes on release.");

            probe.SkipPointerPass = true;
            yield return Pointer(inside, 1);
            yield return Pointer(inside);
            yield return Frames();
            Assert.That(probe.Clicks, Is.EqualTo(3));
            probe.SkipPointerPass = false;
            yield return Frames();
            Assert.That(probe.Clicks, Is.EqualTo(4),
                "A complete click between pointer UI repaints is retained until the same rendered control consumes it.");
            yield return Frames();
            Assert.That(probe.Clicks, Is.EqualTo(4), "A buffered release activates only once.");
            Assert.That(probe.CoveredClicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Pause_UsesItsOwnPointerContextWhileGameplayMenusAreBlocked()
        {
            playerObject = new GameObject("Pointer pause player");
            playerObject.AddComponent<CharacterController>();
            PlayerMotor motor = playerObject.AddComponent<PlayerMotor>();
            motor.Initialize(null, null);
            PlayerInteractor interactor = playerObject.AddComponent<PlayerInteractor>();
            pauseObject = new GameObject("Pointer pause menu");
            var pause = pauseObject.AddComponent<PauseMenuController>();
            pause.Initialize(new PlayerRuntime(playerObject, motor, interactor, null), null, null);
            Assert.That(pause.Open(), Is.True);
            yield return Frames();
            Assert.That(GameInput.CanRead(GameInputContext.Menu), Is.False);
            Assert.That(GameInput.CanRead(GameInputContext.PauseMenu), Is.True);
            // MainMenu has no cutscene catalogue: the real Resume row is y=118.
            yield return Click(LogicalWindowPoint(new Vector2(320f, 134f)));
            yield return Frames();
            Assert.That(pause.IsOpen, Is.False, "The actual pause button must remain clickable while Menu input is blocked.");
        }

        [UnityTest]
        public IEnumerator MainMenu_UsesPointerKeyboardAndRussianTextWithoutLegacyInput()
        {
            StartMenuRoot menu = Object.FindAnyObjectByType<StartMenuRoot>();
            Assert.That(menu, Is.Not.Null);
            yield return Click(LogicalWindowPoint(StartMenuRoot.MenuDebugRect.center));
            Assert.That(menu.IsDebugMenu, Is.True);
            yield return KeyPress(Key.Enter);
            Assert.That(menu.IsChoosingStartTime, Is.True);
            int before = menu.SelectedStartMinuteOfDay;
            yield return Click(LogicalWindowPoint(new Vector2(194f, 151f)));
            Assert.That(menu.SelectedStartMinuteOfDay, Is.EqualTo((before + 23 * 60) % (24 * 60)));
            yield return Click(LogicalWindowPoint(StartMenuRoot.StartTimeOptionRect(StartTimeOption.Continue).center));
            Assert.That(menu.IsChoosingLocation, Is.True);
            yield return Capture("main-menu-start-locations");
            AssertLocationRowUsesAuthoredCharcoal("main-menu-start-locations");
            yield return Click(LogicalWindowPoint(StartMenuRoot.LocationOptionRect(NewGameLocationCatalog.Count).center));
            Assert.That(menu.IsChoosingStartTime, Is.True);
            yield return KeyPress(Key.Escape);
            yield return KeyPress(Key.DownArrow);
            Assert.That(menu.SelectedOption, Is.EqualTo(StartMenuOption.CombatTest), "A stationary pointer cannot steal keyboard selection.");
            yield return KeyPress(Key.Enter);
            Assert.That(menu.IsChoosingCombatWeapon, Is.True);
            yield return KeyPress(Key.Escape);
            yield return Click(LogicalWindowPoint(StartMenuRoot.MenuVocalizerRect.center));
            Assert.That(menu.IsVocalizerActive, Is.True);
            RussianVocalizerLab lab = menu.VocalizerLab;
            lab.Phrase = string.Empty;
            yield return Click(LogicalWindowPoint(new Vector2(100f, 58f)));
            foreach (char letter in "Проверка") InputSystem.QueueTextEvent(keyboard, letter);
            yield return Frames();
            Assert.That(lab.Phrase, Is.EqualTo("Проверка"));
            Key modifier = Application.platform == RuntimePlatform.OSXEditor ? Key.LeftMeta : Key.LeftCtrl;
            yield return KeyPress(modifier, Key.A);
            GUIUtility.systemCopyBuffer = "Первая строка\nВторая строка";
            yield return KeyPress(modifier, Key.V);
            Assert.That(lab.Phrase, Is.EqualTo(GUIUtility.systemCopyBuffer), "Unicode selection/paste must use the owned text editor.");
            yield return KeyPress(Key.Backspace);
            Assert.That(lab.Phrase, Is.EqualTo("Первая строка\nВторая строк"));
            yield return KeyPress(Key.LeftArrow);
            yield return KeyPress(Key.Delete);
            Assert.That(lab.Phrase, Is.EqualTo("Первая строка\nВторая стро"), "Navigation and forward deletion must share native function-key mappings.");
            yield return Click(LogicalWindowPoint(new Vector2(320f, 159f)));
            Assert.That(lab.VoiceFilter, Is.EqualTo(RussianVocalizerLabFilter.Male));
            yield return Capture("vocalizer-input-system");
            yield return KeyPress(Key.Escape);
            Assert.That(menu.IsVocalizerActive, Is.False);
            string closedPhrase = lab.Phrase;
            InputSystem.QueueTextEvent(keyboard, 'Я');
            yield return Frames();
            Assert.That(lab.Phrase, Is.EqualTo(closedPhrase), "Closing releases the text callback.");
            yield return Capture("main-menu-input-system");
        }

        private IEnumerator Pointer(Vector2 topLeftPosition, ushort buttons = 0, Vector2 wheel = default)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = new Vector2(topLeftPosition.x, Screen.height - topLeftPosition.y),
                buttons = buttons,
                scroll = wheel
            });
            yield return Frames();
        }

        private IEnumerator Click(Vector2 point)
        {
            yield return Pointer(point, 1);
            yield return Pointer(point);
        }

        private IEnumerator KeyPress(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return Frames();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return Frames();
        }

        private static IEnumerator Frames()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        private static Vector2 LogicalWindowPoint(Vector2 logical)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            return canvas.ScreenOffset + logical * canvas.Scale;
        }

        private static IEnumerator Capture(string name)
        {
            string directory = Path.Combine(Application.dataPath, "../Captures/InputSystemUi");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 5f;
            do
            {
                yield return Frames();
            }
            while ((!File.Exists(path) || new FileInfo(path).Length == 0) &&
                Time.realtimeSinceStartup < deadline);
            Assert.That(File.Exists(path), Is.True, "The current menu frame must produce a fresh capture: " + path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(0));
            yield return Frames();
        }

        private static void AssertLocationRowUsesAuthoredCharcoal(string name)
        {
            string path = Path.Combine(Application.dataPath, "../Captures/InputSystemUi", name + ".png");
            var capture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(capture.LoadImage(File.ReadAllBytes(path)), Is.True,
                    "The fresh capture must contain a complete PNG.");
                Rect row = StartMenuRoot.LocationOptionRect(1);
                Vector2 screenPoint = LogicalWindowPoint(new Vector2(row.xMax - 12f, row.center.y));
                int x = Mathf.Clamp(Mathf.FloorToInt(screenPoint.x * capture.width / Screen.width), 0, capture.width - 1);
                int y = Mathf.Clamp(capture.height - 1 - Mathf.FloorToInt(screenPoint.y * capture.height / Screen.height),
                    0, capture.height - 1);
                Color pixel = capture.GetPixel(x, y);
                Assert.That(Mathf.Max(pixel.r, pixel.g, pixel.b) * 255f, Is.LessThan(80f),
                    "An unselected location row must retain its charcoal panel; a native grey button covers the text.");
            }
            finally { Object.Destroy(capture); }
        }
    }

    public sealed class InputSystemUiProbe : MonoBehaviour
    {
        internal static readonly Rect ButtonRect = new Rect(24f, 18f, 90f, 24f);
        private static readonly Vector2 Offset = new Vector2(13f, 27f), Group = new Vector2(90f, 60f);
        internal int Paints, Clicks, CoveredClicks, RightDrags;
        internal bool SkipPointerPass;
        internal Vector2 LocalPointer, LastScroll, ScrollAfterConsume;
        internal bool DragAfterConsume;
        internal static Vector2 WindowPoint(Vector2 local) => Offset + (Group + local) * 1.5f;

        private void OnGUI()
        {
            if (SkipPointerPass && Event.current.type == EventType.Repaint) return;
            using var input = RetroUiInput.Begin();
            Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Offset, Quaternion.identity, new Vector3(1.5f, 1.5f, 1f));
            GUI.BeginGroup(new Rect(Group.x, Group.y, 240f, 120f));
            try
            {
                if (Event.current.type == EventType.Repaint)
                {
                    Paints++;
                    LocalPointer = RetroUiInput.MousePosition;
                    if (RetroUiInput.ScrollDelta.sqrMagnitude > 0f)
                    {
                        LastScroll = RetroUiInput.ScrollDelta;
                        RetroUiInput.ConsumeScroll();
                        ScrollAfterConsume = RetroUiInput.ScrollDelta;
                    }
                    if (RetroUiInput.GetPointerDragged(1))
                    {
                        RightDrags++;
                        RetroUiInput.ConsumePointerDrag(1);
                        DragAfterConsume = RetroUiInput.GetPointerDragged(1);
                    }
                }
                if (RetroUiInput.Button(ButtonRect, "Один")) Clicks++;
                if (RetroUiInput.Button(ButtonRect, "Два")) CoveredClicks++;
            }
            finally { GUI.EndGroup(); GUI.matrix = matrix; }
        }

        private void OnDestroy() => GameCursor.Release(this);
    }
}
