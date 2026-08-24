using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class PauseMenuController : MonoBehaviour
    {
        private static PauseMenuController activeController;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly PauseMenuModel model =
            new PauseMenuModel();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private Func<bool> additionalCanOpen;
        private GUIStyle titleStyle;
        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;
        private GUIStyle messageStyle;
        private float previousTimeScale = 1f;
        private bool previousAudioPause;
        private bool ownsPauseState;
        private bool closePending;
        private int inputUnlockFrame;
        private int closeRequestedFrame;
        private string pendingCloseReason = string.Empty;

        public static bool IsAnyPaused =>
            activeController != null &&
            activeController.ownsPauseState;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public bool RestartRequested { get; private set; }
        public bool QuitRequested { get; private set; }
        public PauseMenuPage Page => model.Page;
        public PauseMenuOption SelectedOption =>
            model.SelectedOption;
        public PauseMenuOption ConfirmationTarget =>
            model.ConfirmationTarget;
        public bool ConfirmationYesSelected =>
            model.ConfirmationYesSelected;
        public PauseMenuOptionsRow SelectedOptionsRow =>
            model.SelectedOptionsRow;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeController = null;
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud,
            Func<bool> canOpen = null)
        {
            if (playerRuntime.GameObject == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "The pause menu requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The pause menu is already initialized.");
            }

            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            additionalCanOpen = canOpen;
            IsInitialized = true;
        }

        public bool Open()
        {
            if (!CanOpen())
            {
                return false;
            }

            if (!modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                return false;
            }

            previousTimeScale = Time.timeScale;
            previousAudioPause = AudioListener.pause;
            ownsPauseState = true;
            activeController = this;
            closePending = false;
            pendingCloseReason = string.Empty;
            model.Open();
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            RetroAudio.Play(RetroSfxId.MapOpen);
            GameLog.Info(
                "pause",
                "opened",
                GameLog.Field(
                    "scene",
                    gameObject.scene.name));
            return true;
        }

        public bool MoveSelection(int delta)
        {
            if (!IsOpen || closePending ||
                !model.MoveSelection(delta))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool ConfirmSelection()
        {
            if (!IsOpen || closePending)
            {
                return false;
            }

            PauseMenuAction action = model.Confirm();
            RetroAudio.Play(RetroSfxId.UiConfirm);
            Execute(action);
            return true;
        }

        public bool Cancel()
        {
            if (!IsOpen || closePending)
            {
                return false;
            }

            PauseMenuAction action = model.Cancel();
            RetroAudio.Play(RetroSfxId.UiCancel);
            Execute(action);
            return true;
        }

        private bool CanOpen()
        {
            return IsInitialized &&
                   isActiveAndEnabled &&
                   !IsOpen &&
                   !ownsPauseState &&
                   activeController == null &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning &&
                   (additionalCanOpen == null ||
                    additionalCanOpen());
        }

        private void Execute(PauseMenuAction action)
        {
            switch (action)
            {
                case PauseMenuAction.None:
                    return;
                case PauseMenuAction.Resume:
                    BeginResume();
                    return;
                case PauseMenuAction.Restart:
                    RestartGame();
                    return;
                case PauseMenuAction.Quit:
                    QuitGame();
                    return;
                case PauseMenuAction.ToggleGraphicsOption:
                    ToggleGraphicsOption(model.SelectedOptionsRow);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(action),
                        action,
                        null);
            }
        }

        private void BeginResume()
        {
            closePending = true;
            closeRequestedFrame = Time.frameCount;
            pendingCloseReason = "resume";
        }

        private void RestartGame()
        {
            RestartRequested = true;
            GameLog.Info(
                "pause",
                "restart_confirmed");
            RestoreOwnedState("restart");
            if (!SceneTransitionService.RequestLoad(
                    SceneIds.MainMenu))
            {
                GameLog.Warning(
                    "pause",
                    "restart_rejected");
            }
        }

        private void QuitGame()
        {
            QuitRequested = true;
            GameLog.Info(
                "pause",
                "quit_confirmed");
            RestoreOwnedState("quit");
            Application.Quit();
        }

        private void Update()
        {
            if (closePending)
            {
                if (Time.frameCount > closeRequestedFrame)
                {
                    RestoreOwnedState(pendingCloseReason);
                }

                return;
            }

            if (!IsOpen)
            {
                if (WasPauseTogglePressed())
                {
                    Open();
                }

                return;
            }

            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasPauseTogglePressed() || WasCancelPressed())
            {
                Cancel();
                return;
            }

            int delta = ReadSelectionDelta();
            if (delta != 0)
            {
                MoveSelection(delta);
            }

            if (WasConfirmPressed())
            {
                ConfirmSelection();
            }
        }

        private void RestoreOwnedState(string reason)
        {
            if (!ownsPauseState)
            {
                IsOpen = false;
                closePending = false;
                return;
            }

            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            modalLock.Restore();
            ownsPauseState = false;
            IsOpen = false;
            closePending = false;
            pendingCloseReason = string.Empty;
            if (activeController == this)
            {
                activeController = null;
            }

            GameLog.Info(
                "pause",
                "closed",
                GameLog.Field("reason", reason));
        }

        private void OnDisable()
        {
            RestoreOwnedState("disabled");
        }

        private void OnDestroy()
        {
            RestoreOwnedState("destroyed");
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;
            RetroUiTheme.FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                new Color(0.015f, 0.012f, 0.02f, 0.82f));
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                if (Page == PauseMenuPage.Main)
                {
                    DrawMainPage(canvas);
                }
                else if (Page == PauseMenuPage.Options)
                {
                    DrawOptionsPage(canvas);
                }
                else
                {
                    DrawConfirmationPage(canvas);
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawMainPage(RetroUiCanvas canvas)
        {
            Rect panel = new Rect(198f, 52f, 244f, 268f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);
            GUI.Label(
                new Rect(214f, 70f, 212f, 30f),
                LocalizationService.Get("pause.title"),
                titleStyle);

            DrawMainOption(
                canvas,
                new Rect(216f, 118f, 208f, 32f),
                PauseMenuOption.Resume,
                "pause.resume");
            DrawMainOption(
                canvas,
                new Rect(216f, 158f, 208f, 32f),
                PauseMenuOption.Options,
                "pause.options");
            DrawMainOption(
                canvas,
                new Rect(216f, 198f, 208f, 32f),
                PauseMenuOption.Restart,
                "pause.restart");
            DrawMainOption(
                canvas,
                new Rect(216f, 238f, 208f, 32f),
                PauseMenuOption.Quit,
                "pause.quit");
        }

        private void DrawOptionsPage(RetroUiCanvas canvas)
        {
            Rect panel = new Rect(140f, 40f, 360f, 300f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);
            GUI.Label(
                new Rect(156f, 56f, 328f, 28f),
                LocalizationService.Get("pause.options"),
                titleStyle);

            DrawOptionsRow(
                canvas,
                0,
                PauseMenuOptionsRow.DepthOfField,
                "options.dof");
            DrawOptionsRow(
                canvas,
                1,
                PauseMenuOptionsRow.IntoxicationFx,
                "options.intoxication_fx");
            DrawOptionsRow(
                canvas,
                2,
                PauseMenuOptionsRow.Dither,
                "options.dither");
            DrawOptionsRow(
                canvas,
                3,
                PauseMenuOptionsRow.Scanlines,
                "options.scanlines");
            DrawOptionsRow(
                canvas,
                4,
                PauseMenuOptionsRow.RainOnLens,
                "options.rain_lens");
            DrawOptionsRow(
                canvas,
                5,
                PauseMenuOptionsRow.AspectRatio43,
                "options.aspect_4_3");
            DrawOptionsRow(
                canvas,
                6,
                PauseMenuOptionsRow.HighFrameRate,
                "options.frame_rate_60");
            DrawOptionsRow(
                canvas,
                7,
                PauseMenuOptionsRow.Back,
                "options.back");
        }

        private void DrawOptionsRow(
            RetroUiCanvas canvas,
            int index,
            PauseMenuOptionsRow row,
            string localizationKey)
        {
            Rect rect = new Rect(
                156f,
                94f + index * 32f,
                328f,
                28f);
            Vector2 mouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            if (rect.Contains(mouse) &&
                Event.current.type == EventType.MouseMove)
            {
                model.SelectOptionsRow(row);
            }

            bool selected = SelectedOptionsRow == row;
            DrawSelection(rect, selected);
            if (GUI.Button(
                    rect,
                    (selected ? "> " : "  ") +
                    LocalizationService.Get(localizationKey),
                    selected ? selectedStyle : optionStyle))
            {
                model.SelectOptionsRow(row);
                ConfirmSelection();
            }

            if (row != PauseMenuOptionsRow.Back)
            {
                // Drawn after the button so the box sits on top of
                // its background; clicking the box still lands on the
                // row button underneath.
                DrawCheckbox(
                    new Rect(
                        rect.xMax - 28f,
                        rect.y + 5f,
                        18f,
                        18f),
                    IsGraphicsOptionEnabled(row),
                    selected);
            }
        }

        private static void DrawCheckbox(
            Rect box,
            bool value,
            bool selected)
        {
            RetroUiTheme.FillRect(box, RetroUiTheme.PanelInset);
            RetroUiTheme.StrokeRect(
                box,
                1f,
                selected
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.BorderMuted);
            if (value)
            {
                RetroUiTheme.FillRect(
                    new Rect(
                        box.x + 4f,
                        box.y + 4f,
                        box.width - 8f,
                        box.height - 8f),
                    RetroUiTheme.AccentPale);
            }
        }

        private static void ToggleGraphicsOption(
            PauseMenuOptionsRow row)
        {
            switch (row)
            {
                case PauseMenuOptionsRow.DepthOfField:
                    GraphicsEffectsSettings.DepthOfFieldEnabled =
                        !GraphicsEffectsSettings.DepthOfFieldEnabled;
                    return;
                case PauseMenuOptionsRow.IntoxicationFx:
                    GraphicsEffectsSettings
                            .IntoxicationLensFxEnabled =
                        !GraphicsEffectsSettings
                            .IntoxicationLensFxEnabled;
                    return;
                case PauseMenuOptionsRow.Dither:
                    GraphicsEffectsSettings.DitherEnabled =
                        !GraphicsEffectsSettings.DitherEnabled;
                    return;
                case PauseMenuOptionsRow.Scanlines:
                    GraphicsEffectsSettings.ScanlinesEnabled =
                        !GraphicsEffectsSettings.ScanlinesEnabled;
                    return;
                case PauseMenuOptionsRow.RainOnLens:
                    GraphicsEffectsSettings.RainOnLensEnabled =
                        !GraphicsEffectsSettings.RainOnLensEnabled;
                    return;
                case PauseMenuOptionsRow.AspectRatio43:
                    GraphicsEffectsSettings.AspectRatio43Enabled =
                        !GraphicsEffectsSettings
                            .AspectRatio43Enabled;
                    return;
                case PauseMenuOptionsRow.HighFrameRate:
                    GraphicsEffectsSettings.HighFrameRateEnabled =
                        !GraphicsEffectsSettings
                            .HighFrameRateEnabled;
                    // Unlike the other rows, this one is not read back
                    // by a renderer feature each frame - the cap is a
                    // player setting, so push it now.
                    BarPromenadeRuntimeBootstrap
                        .RefreshFrameRateCap();
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(row),
                        row,
                        null);
            }
        }

        private static bool IsGraphicsOptionEnabled(
            PauseMenuOptionsRow row)
        {
            switch (row)
            {
                case PauseMenuOptionsRow.DepthOfField:
                    return GraphicsEffectsSettings
                        .DepthOfFieldEnabled;
                case PauseMenuOptionsRow.IntoxicationFx:
                    return GraphicsEffectsSettings
                        .IntoxicationLensFxEnabled;
                case PauseMenuOptionsRow.Dither:
                    return GraphicsEffectsSettings.DitherEnabled;
                case PauseMenuOptionsRow.Scanlines:
                    return GraphicsEffectsSettings
                        .ScanlinesEnabled;
                case PauseMenuOptionsRow.RainOnLens:
                    return GraphicsEffectsSettings
                        .RainOnLensEnabled;
                case PauseMenuOptionsRow.AspectRatio43:
                    return GraphicsEffectsSettings
                        .AspectRatio43Enabled;
                case PauseMenuOptionsRow.HighFrameRate:
                    return GraphicsEffectsSettings
                        .HighFrameRateEnabled;
                default:
                    return false;
            }
        }

        private void DrawMainOption(
            RetroUiCanvas canvas,
            Rect rect,
            PauseMenuOption option,
            string localizationKey)
        {
            Vector2 mouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            if (rect.Contains(mouse) &&
                Event.current.type == EventType.MouseMove)
            {
                model.SelectOption(option);
            }

            bool selected = SelectedOption == option;
            DrawSelection(rect, selected);
            if (GUI.Button(
                    rect,
                    (selected ? "> " : "  ") +
                    LocalizationService.Get(localizationKey),
                    selected ? selectedStyle : optionStyle))
            {
                model.SelectOption(option);
                ConfirmSelection();
            }
        }

        private void DrawConfirmationPage(
            RetroUiCanvas canvas)
        {
            Rect panel = new Rect(154f, 88f, 332f, 184f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);
            GUI.Label(
                new Rect(174f, 104f, 292f, 28f),
                LocalizationService.Get("pause.title"),
                titleStyle);
            string messageKey =
                ConfirmationTarget == PauseMenuOption.Restart
                    ? "pause.restart_confirm"
                    : "pause.quit_confirm";
            GUI.Label(
                new Rect(178f, 139f, 284f, 56f),
                LocalizationService.Get(messageKey),
                messageStyle);

            DrawConfirmationOption(
                canvas,
                new Rect(194f, 218f, 104f, 30f),
                false,
                "pause.no");
            DrawConfirmationOption(
                canvas,
                new Rect(342f, 218f, 104f, 30f),
                true,
                "pause.yes");
        }

        private void DrawConfirmationOption(
            RetroUiCanvas canvas,
            Rect rect,
            bool yes,
            string localizationKey)
        {
            Vector2 mouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            if (rect.Contains(mouse) &&
                Event.current.type == EventType.MouseMove)
            {
                model.SelectConfirmation(yes);
            }

            bool selected = ConfirmationYesSelected == yes;
            DrawSelection(rect, selected);
            if (GUI.Button(
                    rect,
                    (selected ? "> " : "  ") +
                    LocalizationService.Get(localizationKey),
                    selected ? selectedStyle : optionStyle))
            {
                model.SelectConfirmation(yes);
                ConfirmSelection();
            }
        }

        private static void DrawSelection(Rect rect, bool selected)
        {
            if (!selected)
            {
                return;
            }

            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.PanelInset,
                RetroUiTheme.Accent,
                true,
                1f,
                1f);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                22,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            selectedStyle = RetroUiTheme.CreateButtonStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            optionStyle = RetroUiTheme.CreateButtonStyle(
                12,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Muted,
                true);
            messageStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
        }

        private static bool WasPauseTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.startButton.wasPressedThisFrame;
        }

        private static bool WasCancelPressed()
        {
            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame ||
                    keyboard.wKey.wasPressedThisFrame ||
                    keyboard.leftArrowKey.wasPressedThisFrame ||
                    keyboard.aKey.wasPressedThisFrame)
                {
                    return -1;
                }

                if (keyboard.downArrowKey.wasPressedThisFrame ||
                    keyboard.sKey.wasPressedThisFrame ||
                    keyboard.rightArrowKey.wasPressedThisFrame ||
                    keyboard.dKey.wasPressedThisFrame)
                {
                    return 1;
                }
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return 0;
            }

            if (gamepad.dpad.up.wasPressedThisFrame ||
                gamepad.dpad.left.wasPressedThisFrame ||
                gamepad.leftStick.up.wasPressedThisFrame ||
                gamepad.leftStick.left.wasPressedThisFrame)
            {
                return -1;
            }

            if (gamepad.dpad.down.wasPressedThisFrame ||
                gamepad.dpad.right.wasPressedThisFrame ||
                gamepad.leftStick.down.wasPressedThisFrame ||
                gamepad.leftStick.right.wasPressedThisFrame)
            {
                return 1;
            }

            return 0;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.eKey.wasPressedThisFrame ||
                 keyboard.spaceKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }
    }
}
