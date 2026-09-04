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

        // How many option rows fit between the title and the bottom of
        // the panel. Rows start at y = 94 and stand 32 apart inside a
        // panel that ends at y = 340, which leaves room for seven plus
        // the scroll hints above and below them.
        private const int VisibleOptionsRows = 7;

        // The order the options page draws. One table rather than a run
        // of hand-numbered calls: the row indices used to be written out
        // at every call site, so adding a row meant renumbering each one
        // below it - which is how the list quietly grew past the panel.
        private static readonly (PauseMenuOptionsRow Row, string Key)[]
            OptionsRows =
            {
                (PauseMenuOptionsRow.DepthOfField, "options.dof"),
                (PauseMenuOptionsRow.IntoxicationFx,
                    "options.intoxication_fx"),
                (PauseMenuOptionsRow.Dither, "options.dither"),
                (PauseMenuOptionsRow.Scanlines, "options.scanlines"),
                (PauseMenuOptionsRow.AspectRatio43, "options.aspect_4_3"),
                (PauseMenuOptionsRow.VertexJitter,
                    "options.vertex_jitter"),
                (PauseMenuOptionsRow.Begotten, "options.begotten"),
                (PauseMenuOptionsRow.Back, "options.back")
            };

        private int optionsScroll;
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
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.88f));
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
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
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
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            GUI.Label(
                new Rect(156f, 56f, 328f, 28f),
                LocalizationService.Get("pause.options"),
                titleStyle);

            int rowCount = OptionsRows.Length;
            int selectedIndex = 0;
            for (int index = 0; index < rowCount; index++)
            {
                if (OptionsRows[index].Row == SelectedOptionsRow)
                {
                    selectedIndex = index;
                    break;
                }
            }

            // The list outgrew the panel, so it scrolls. The window
            // follows the selection rather than the mouse: this menu is
            // driven by the keyboard and the pad, and a row you have
            // selected but cannot see is the one bug a scrolling list
            // must not have.
            int maximumScroll = Mathf.Max(0, rowCount - VisibleOptionsRows);
            optionsScroll = Mathf.Clamp(optionsScroll, 0, maximumScroll);
            if (selectedIndex < optionsScroll)
            {
                optionsScroll = selectedIndex;
            }
            else if (selectedIndex >= optionsScroll + VisibleOptionsRows)
            {
                optionsScroll = selectedIndex - VisibleOptionsRows + 1;
            }

            if (Event.current.type == EventType.ScrollWheel &&
                panel.Contains(RetroUiTheme.LogicalMousePosition(canvas)))
            {
                optionsScroll = Mathf.Clamp(
                    optionsScroll +
                    (Event.current.delta.y > 0f ? 1 : -1),
                    0,
                    maximumScroll);
                Event.current.Use();
            }

            for (int slot = 0; slot < VisibleOptionsRows; slot++)
            {
                int index = optionsScroll + slot;
                if (index >= rowCount)
                {
                    break;
                }

                DrawOptionsRow(
                    canvas,
                    slot,
                    OptionsRows[index].Row,
                    OptionsRows[index].Key);
            }

            DrawScrollHint(
                new Rect(panel.center.x - 8f, 84f, 16f, 8f),
                optionsScroll > 0);
            DrawScrollHint(
                new Rect(
                    panel.center.x - 8f,
                    96f + VisibleOptionsRows * 32f,
                    16f,
                    8f),
                optionsScroll < maximumScroll,
                true);
        }

        // A stack of dots narrowing to a point, drawn as filled rects
        // rather than a glyph so it survives the retro font and the
        // RGB555 pass.
        private static void DrawScrollHint(
            Rect area,
            bool visible,
            bool pointingDown = false)
        {
            if (!visible)
            {
                return;
            }

            const int rows = 3;
            for (int row = 0; row < rows; row++)
            {
                float width = area.width * (row + 1) / rows;
                float y = pointingDown
                    ? area.y + (rows - 1 - row) * 3f
                    : area.y + row * 3f;
                RetroUiTheme.FillRect(
                    new Rect(
                        area.center.x - width * 0.5f,
                        y,
                        width,
                        2f),
                    RetroUiTheme.FrameInner);
            }
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
            RetroUiTheme.DrawFrame(
                box,
                selected
                    ? RetroUiTheme.SelectionText
                    : RetroUiTheme.FrameOuter,
                RetroUiTheme.FrameInner);
            if (value)
            {
                RetroUiTheme.FillRect(
                    new Rect(
                        box.x + 4f,
                        box.y + 4f,
                        box.width - 8f,
                        box.height - 8f),
                    RetroUiTheme.SelectionText);
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
                case PauseMenuOptionsRow.AspectRatio43:
                    GraphicsEffectsSettings.AspectRatio43Enabled =
                        !GraphicsEffectsSettings
                            .AspectRatio43Enabled;
                    return;
                case PauseMenuOptionsRow.VertexJitter:
                    GraphicsEffectsSettings.VertexJitterEnabled =
                        !GraphicsEffectsSettings
                            .VertexJitterEnabled;
                    return;
                case PauseMenuOptionsRow.Begotten:
                    GraphicsEffectsSettings.BegottenModeEnabled =
                        !GraphicsEffectsSettings
                            .BegottenModeEnabled;
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
                case PauseMenuOptionsRow.AspectRatio43:
                    return GraphicsEffectsSettings
                        .AspectRatio43Enabled;
                case PauseMenuOptionsRow.VertexJitter:
                    return GraphicsEffectsSettings
                        .VertexJitterEnabled;
                case PauseMenuOptionsRow.Begotten:
                    return GraphicsEffectsSettings
                        .BegottenModeEnabled;
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
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
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

            RetroUiTheme.DrawSelection(rect, true);
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
                RetroUiTheme.Text,
                false);
            selectedStyle = RetroUiTheme.CreateButtonStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.SelectionText,
                false);
            optionStyle = RetroUiTheme.CreateButtonStyle(
                12,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Muted,
                false);
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
