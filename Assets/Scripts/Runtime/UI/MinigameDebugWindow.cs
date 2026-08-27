using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The F9 debug window. The bar minigames it once launched are cut
    /// from the project; what remains is the debug surface itself:
    /// intoxication adjustment, game-day selection, the City map's
    /// test-teleport toggle and the F8 diagnostics hotkeys, under the same
    /// modal lock.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameDebugWindow : MonoBehaviour
    {
        private const int IntoxicationStep = 20;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private CityMapController cityMap;
        private BarDrinkShopController drinkShop;
        private GUIStyle titleStyle;
        private GUIStyle hintStyle;
        private GUIStyle rowStyle;
        private GUIStyle footerStyle;
        private int inputUnlockFrame;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public string LastLaunchErrorKey { get; private set; } =
            string.Empty;
        public bool DebugTeleportEnabled =>
            cityMap != null && cityMap.DebugTeleportEnabled;
        public int CurrentGameDayNumber =>
            GameSessionState.GameDayNumber;

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud,
            CityMapController map = null,
            BarDrinkShopController activeDrinkShop = null)
        {
            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            cityMap = map;
            drinkShop = activeDrinkShop;
            IsInitialized = player.Interactor != null;
        }

        public bool Open()
        {
            if (!IsInitialized ||
                IsOpen ||
                SceneTransitionService.IsTransitioning ||
                HasCommittedDrinkService())
            {
                LastLaunchErrorKey =
                    "debug.minigames.unavailable";
                return false;
            }

            if (!CloseOtherModalContent())
            {
                LastLaunchErrorKey =
                    "debug.minigames.unavailable";
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                LastLaunchErrorKey =
                    "debug.minigames.unavailable";
                return false;
            }

            inputUnlockFrame = Time.frameCount + 1;
            LastLaunchErrorKey = string.Empty;
            IsOpen = true;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool Close()
        {
            return Close(true);
        }

        public bool Toggle()
        {
            return IsOpen ? Close() : Open();
        }

        public bool ToggleDebugTeleport()
        {
            if (cityMap == null)
            {
                return false;
            }

            cityMap.SetDebugTeleportEnabled(
                !cityMap.DebugTeleportEnabled);
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool TrySetGameDayNumber(int dayNumber)
        {
            if (!IsOpen ||
                !GameSessionState.TrySetDebugGameDay(dayNumber))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        private void Update()
        {
            if (WasOpenLogDirectoryPressed())
            {
                if (GameDiagnosticsSnapshot.TryOpenLogDirectory())
                {
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                }

                return;
            }

            if (WasSnapshotPressed())
            {
                if (GameDiagnosticsSnapshot.Capture("f8_hotkey"))
                {
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                }

                return;
            }

            if (WasTogglePressed())
            {
                Toggle();
                return;
            }

            if (!IsOpen || Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasCancelPressed())
            {
                Close();
                return;
            }

            int intoxicationDelta = ReadIntoxicationDelta();
            if (intoxicationDelta != 0)
            {
                TryAdjustIntoxication(intoxicationDelta);
            }
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;

            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawWindow();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void OnDisable()
        {
            Close(false);
        }

        private void OnDestroy()
        {
            Close(false);
        }

        private bool Close(bool playSound)
        {
            if (!IsOpen)
            {
                return false;
            }

            IsOpen = false;
            modalLock.Restore();
            if (playSound)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            return true;
        }

        private bool CloseOtherModalContent()
        {
            if (HasCommittedDrinkService())
            {
                return false;
            }

            if (cityMap != null && cityMap.IsOpen)
            {
                cityMap.Close();
            }

            if (drinkShop != null && drinkShop.IsOpen)
            {
                drinkShop.Close();
            }

            return true;
        }

        private bool HasCommittedDrinkService()
        {
            return drinkShop != null && drinkShop.IsServing;
        }

        private void DrawWindow()
        {
            RetroUiTheme.FillRect(
                new Rect(
                    0f,
                    0f,
                    RetroUiTheme.LogicalWidth,
                    RetroUiTheme.LogicalHeight),
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.92f));

            Rect panel = new Rect(112f, 24f, 416f, 200f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);
            GUI.Label(
                new Rect(132f, 39f, 376f, 25f),
                LocalizationService.Get(
                    "debug.minigames.title"),
                titleStyle);
            GUI.Label(
                new Rect(132f, 66f, 376f, 28f),
                LocalizationService.Get(
                    "debug.minigames.hint"),
                hintStyle);
            DrawIntoxicationControls();
            DrawDayControls();
            if (cityMap != null)
            {
                DrawDebugTeleportControl();
            }

            if (!string.IsNullOrEmpty(LastLaunchErrorKey))
            {
                GUI.Label(
                    new Rect(132f, 191f, 376f, 20f),
                    LocalizationService.Get(LastLaunchErrorKey),
                    footerStyle);
            }
        }

        private void DrawIntoxicationControls()
        {
            int intoxication = GameSessionState.IntoxicationLevel;
            Rect decreaseButton = new Rect(132f, 96f, 64f, 24f);
            Rect valueLabel = new Rect(202f, 96f, 236f, 24f);
            Rect increaseButton = new Rect(444f, 96f, 64f, 24f);

            DrawIntoxicationButton(
                decreaseButton,
                "←  −20",
                -IntoxicationStep,
                intoxication > 0);
            GUI.Label(
                valueLabel,
                string.Format(
                    LocalizationService.Get(
                        "drinking.intoxication"),
                    intoxication),
                hintStyle);
            DrawIntoxicationButton(
                increaseButton,
                "+20  →",
                IntoxicationStep,
                intoxication < 100);
        }

        private void DrawDebugTeleportControl()
        {
            Rect button = new Rect(132f, 156f, 376f, 24f);
            RetroUiTheme.DrawPanel(
                button,
                DebugTeleportEnabled
                    ? RetroUiTheme.PanelRaised
                    : RetroUiTheme.PanelInset,
                DebugTeleportEnabled
                    ? RetroUiTheme.Good
                    : RetroUiTheme.BorderMuted,
                DebugTeleportEnabled,
                2f,
                1f);
            string label = LocalizationService.Get(
                DebugTeleportEnabled
                    ? "debug.teleport.enabled"
                    : "debug.teleport.disabled");
            if (GUI.Button(button, label, rowStyle))
            {
                ToggleDebugTeleport();
            }
        }

        private void DrawDayControls()
        {
            const float buttonWidth = 39f;
            const float buttonGap = 3f;
            GUI.Label(
                new Rect(132f, 125f, 78f, 24f),
                LocalizationService.Get("debug.day"),
                hintStyle);

            for (int dayNumber =
                     GameSessionState.FirstDebugGameDayNumber;
                 dayNumber <= GameSessionState.LastDebugGameDayNumber;
                 dayNumber++)
            {
                int buttonIndex =
                    dayNumber - GameSessionState.FirstDebugGameDayNumber;
                Rect button = new Rect(
                    214f + buttonIndex * (buttonWidth + buttonGap),
                    125f,
                    buttonWidth,
                    24f);
                bool selected = dayNumber == CurrentGameDayNumber;
                RetroUiTheme.DrawPanel(
                    button,
                    selected
                        ? RetroUiTheme.PanelRaised
                        : RetroUiTheme.PanelInset,
                    selected
                        ? RetroUiTheme.Good
                        : RetroUiTheme.BorderMuted,
                    selected,
                    2f,
                    1f);

                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && !selected;
                if (GUI.Button(
                        button,
                        dayNumber.ToString(),
                        rowStyle))
                {
                    TrySetGameDayNumber(dayNumber);
                }

                GUI.enabled = previousEnabled;
            }
        }

        private void DrawIntoxicationButton(
            Rect rect,
            string label,
            int delta,
            bool enabled)
        {
            RetroUiTheme.DrawPanel(
                rect,
                enabled
                    ? RetroUiTheme.PanelRaised
                    : RetroUiTheme.PanelInset,
                enabled
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.BorderMuted,
                enabled,
                2f,
                1f);

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            if (GUI.Button(rect, label, rowStyle))
            {
                TryAdjustIntoxication(delta);
            }

            GUI.enabled = previousEnabled;
        }

        private bool TryAdjustIntoxication(int delta)
        {
            if (!IsOpen || delta == 0)
            {
                return false;
            }

            int current = GameSessionState.IntoxicationLevel;
            int adjusted = Mathf.Clamp(current + delta, 0, 100);
            if (adjusted == current)
            {
                return false;
            }

            GameSessionState.UpdateDrinkingProgress(
                adjusted,
                GameSessionState.LastAlcoholicDrink,
                GameSessionState.DrinksConsumed);
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                18,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            hintStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
            rowStyle = RetroUiTheme.CreateButtonStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            footerStyle = RetroUiTheme.CreateLabelStyle(
                9,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false,
                true);
        }

        private static bool WasTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.f9Key.wasPressedThisFrame;
        }

        private static bool WasSnapshotPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.f8Key.wasPressedThisFrame;
        }

        private static bool WasOpenLogDirectoryPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.f8Key.wasPressedThisFrame &&
                   (keyboard.leftShiftKey.isPressed ||
                    keyboard.rightShiftKey.isPressed);
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.escapeKey.wasPressedThisFrame;
        }

        private static int ReadIntoxicationDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return 0;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                return -IntoxicationStep;
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                return IntoxicationStep;
            }

            return 0;
        }
    }
}
