using System;
using BarPromenade.Rendering;
using UnityEngine;

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
        private CutscenePlaybackController cutscenes;
        private CutscenePlaybackController pendingCutsceneController;
        private string pendingCutsceneId;
        private GUIStyle titleStyle;
        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;
        private GUIStyle messageStyle;
        private IDisposable timePause;
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
                (PauseMenuOptionsRow.CleanAntialiasing, "options.clean_aa"),
                (PauseMenuOptionsRow.DepthOfField, "options.dof"),
                (PauseMenuOptionsRow.IntoxicationFx,
                    "options.intoxication_fx"),
                (PauseMenuOptionsRow.AspectRatio43, "options.aspect_4_3"),
                (PauseMenuOptionsRow.CleanReflections, "options.clean_reflections"),
                (PauseMenuOptionsRow.CleanVolume, "options.clean_volume"),
                (PauseMenuOptionsRow.CleanHalation, "options.clean_halation"),
                (PauseMenuOptionsRow.CleanDirectionalHighlights, "options.clean_directional_highlights"),
                (PauseMenuOptionsRow.CleanBounce, "options.clean_bounce"),
                (PauseMenuOptionsRow.Back, "options.back")
            };

        private int optionsScroll;
        private int cutscenesScroll;
        private int cutscenesScrollSelection = -1;
        private int lastCutsceneSelection;
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
        public int SelectedCutsceneIndex => model.SelectedCutsceneIndex;
        public string CutsceneMessageKey { get; private set; } = string.Empty;

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

        public void BindCutscenes(CutscenePlaybackController controller)
        {
            cutscenes = controller;
            lastCutsceneSelection = 0;
            RefreshCutscenes();
        }

        public bool OpenCutscenes()
        {
            RefreshCutscenes();
            if (!model.CutscenesAvailable || closePending ||
                (!IsOpen && !Open()))
            {
                return false;
            }

            if (model.Page != PauseMenuPage.Cutscenes)
            {
                model.Open();
                model.SelectOption(PauseMenuOption.Cutscenes);
                model.Confirm();
            }

            model.SelectCutscene(Mathf.Clamp(
                lastCutsceneSelection, 0, model.CutsceneCount));
            cutscenesScrollSelection = -1;
            return true;
        }

        public bool TryStartCutscene(string id)
        {
            if (!IsOpen || closePending)
            {
                return false;
            }

            RefreshCutscenes();
            if (!model.CutscenesAvailable || cutscenes.IsPlaying ||
                string.IsNullOrEmpty(id))
            {
                CutsceneMessageKey = "cutscene.unavailable";
                return false;
            }

            int index = 0;
            for (; index < cutscenes.Catalog.Count; index++)
            {
                if (string.Equals(
                        cutscenes.Catalog[index].Id, id,
                        StringComparison.Ordinal))
                {
                    break;
                }
            }

            if (index == cutscenes.Catalog.Count)
            {
                CutsceneMessageKey = "cutscene.unavailable";
                return false;
            }

            lastCutsceneSelection = index;
            pendingCutsceneController = cutscenes;
            pendingCutsceneId = id;
            closePending = true;
            closeRequestedFrame = Time.frameCount;
            pendingCloseReason = "cutscene";
            CutsceneMessageKey = string.Empty;
            return true;
        }

        private void RefreshCutscenes()
        {
            bool available = cutscenes != null &&
                cutscenes.isActiveAndEnabled && cutscenes.Catalog != null;
            model.ConfigureCutscenes(
                available, available ? cutscenes.Catalog.Count : 0);
        }

        public bool Open()
        {
            if (!CanOpen())
            {
                return false;
            }

            if (!BarMinigameModalLock.AllowsPauseMenu && !modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                return false;
            }

            timePause = GameTimeScaleRuntime.AcquirePause();
            previousAudioPause = AudioListener.pause;
            ownsPauseState = true;
            activeController = this;
            closePending = false;
            pendingCloseReason = string.Empty;
            pendingCutsceneId = null;
            pendingCutsceneController = null;
            CutsceneMessageKey = string.Empty;
            RefreshCutscenes();
            model.Open();
            cutscenesScroll = 0;
            cutscenesScrollSelection = -1;
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
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
            RefreshCutscenes();
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

            RefreshCutscenes();
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
                   (!BarMinigameModalLock.IsAnyLocked || BarMinigameModalLock.AllowsPauseMenu) &&
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
                case PauseMenuAction.StartCutscene:
                    if (cutscenes != null && cutscenes.Catalog != null &&
                        model.SelectedCutsceneIndex < cutscenes.Catalog.Count)
                    {
                        TryStartCutscene(
                            cutscenes.Catalog[model.SelectedCutsceneIndex].Id);
                    }
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
                    CutscenePlaybackController target = pendingCutsceneController;
                    string cutsceneId = pendingCutsceneId;
                    RestoreOwnedState(pendingCloseReason);

                    // Restore the old snapshot before playback captures its
                    // own. Both operations happen before any gameplay Update.
                    if (cutsceneId != null)
                    {
                        if (target == null || target != cutscenes ||
                            !target.isActiveAndEnabled || !target.TryPlay(cutsceneId))
                        {
                            if (!OpenCutscenes())
                            {
                                Open();
                            }
                            CutsceneMessageKey = "cutscene.unavailable";
                        }

                        return;
                    }
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

            RefreshCutscenes();
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
            if (delta != 0) MoveSelection(delta);

            if (WasConfirmPressed())
            {
                ConfirmSelection();
            }
        }

        private void RestoreOwnedState(string reason)
        {
            pendingCutsceneId = null;
            pendingCutsceneController = null;
            if (!ownsPauseState)
            {
                IsOpen = false;
                closePending = false;
                return;
            }

            timePause?.Dispose();
            timePause = null;
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
            using var presentation = RetroUiTheme.BeginGui();
            using var input = RetroUiInput.Begin(GameInputContext.PauseMenu);
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            RefreshCutscenes();
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
                else if (Page == PauseMenuPage.Cutscenes)
                {
                    DrawCutscenesPage(canvas);
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
            bool hasCutscenes = model.CutscenesAvailable;
            Rect panel = new Rect(
                198f, hasCutscenes ? 32f : 52f,
                244f, hasCutscenes ? 308f : 268f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            GUI.Label(
                new Rect(214f, panel.y + 18f, 212f, 30f),
                LocalizationService.Get("pause.title"),
                titleStyle);

            DrawMainOption(
                canvas,
                new Rect(216f, panel.y + 66f, 208f, 32f),
                PauseMenuOption.Resume,
                "pause.resume");
            DrawMainOption(
                canvas,
                new Rect(216f, panel.y + 106f, 208f, 32f),
                PauseMenuOption.Options,
                "pause.options");
            if (hasCutscenes)
            {
                DrawMainOption(
                    canvas,
                    new Rect(216f, panel.y + 146f, 208f, 32f),
                    PauseMenuOption.Cutscenes,
                    "pause.cutscenes");
            }

            float additionalHeight = hasCutscenes ? 40f : 0f;
            DrawMainOption(
                canvas,
                new Rect(216f, panel.y + 146f + additionalHeight, 208f, 32f),
                PauseMenuOption.Restart,
                "pause.restart");
            DrawMainOption(
                canvas,
                new Rect(216f, panel.y + 186f + additionalHeight, 208f, 32f),
                PauseMenuOption.Quit,
                "pause.quit");

            if (!string.IsNullOrEmpty(CutsceneMessageKey))
            {
                GUI.Label(
                    new Rect(214f, panel.yMax - 40f, 212f, 28f),
                    LocalizationService.Get(CutsceneMessageKey),
                    messageStyle);
            }
        }

        private void DrawCutscenesPage(RetroUiCanvas canvas)
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
                LocalizationService.Get("pause.cutscenes"),
                titleStyle);

            int count = model.CutsceneCount;
            if (count == 0)
            {
                GUI.Label(
                    new Rect(164f, 110f, 312f, 76f),
                    LocalizationService.Get("cutscene.empty"),
                    messageStyle);
                DrawCutsceneRow(canvas, 5, 0, "cutscene.back");
            }
            else
            {
                bool hasMessage = !string.IsNullOrEmpty(CutsceneMessageKey);
                int visibleRows = hasMessage
                    ? VisibleOptionsRows - 1
                    : VisibleOptionsRows;
                int rowCount = count + 1;
                int maximumScroll = Mathf.Max(0, rowCount - visibleRows);
                cutscenesScroll = Mathf.Clamp(cutscenesScroll, 0, maximumScroll);
                if (cutscenesScrollSelection != model.SelectedCutsceneIndex)
                {
                    cutscenesScrollSelection = model.SelectedCutsceneIndex;
                    if (cutscenesScrollSelection < cutscenesScroll)
                    {
                        cutscenesScroll = cutscenesScrollSelection;
                    }
                    else if (cutscenesScrollSelection >= cutscenesScroll + visibleRows)
                    {
                        cutscenesScroll = cutscenesScrollSelection - visibleRows + 1;
                    }
                }

                if (RetroUiInput.ScrollDelta.sqrMagnitude > 0f &&
                    panel.Contains(RetroUiTheme.LogicalMousePosition(canvas)))
                {
                    cutscenesScroll = Mathf.Clamp(
                        cutscenesScroll +
                        (RetroUiInput.ScrollDelta.y > 0f ? 1 : -1),
                        0,
                        maximumScroll);
                    RetroUiInput.ConsumeScroll();
                }

                for (int slot = 0; slot < visibleRows; slot++)
                {
                    int index = cutscenesScroll + slot;
                    if (index >= rowCount)
                    {
                        break;
                    }

                    DrawCutsceneRow(
                        canvas,
                        slot,
                        index,
                        index == count
                            ? "cutscene.back"
                            : cutscenes.Catalog[index].TitleKey);
                }

                DrawScrollHint(
                    new Rect(panel.center.x - 8f, 84f, 16f, 8f),
                    cutscenesScroll > 0);
                DrawScrollHint(
                    new Rect(
                        panel.center.x - 8f,
                        96f + visibleRows * 32f,
                        16f,
                        8f),
                    cutscenesScroll < maximumScroll,
                    true);
            }

            if (!string.IsNullOrEmpty(CutsceneMessageKey))
            {
                GUI.Label(
                    new Rect(156f, 304f, 328f, 28f),
                    LocalizationService.Get(CutsceneMessageKey),
                    messageStyle);
            }
        }

        private void DrawCutsceneRow(
            RetroUiCanvas canvas,
            int slot,
            int index,
            string titleKey)
        {
            Rect rect = new Rect(156f, 94f + slot * 32f, 328f, 28f);
            Vector2 mouse = RetroUiTheme.LogicalMousePosition(canvas);
            if (rect.Contains(mouse) &&
                RetroUiInput.PointerMoved)
            {
                model.SelectCutscene(index);
            }

            bool selected = model.SelectedCutsceneIndex == index;
            DrawSelection(rect, selected);
            if (RetroUiInput.Button(
                    rect,
                    (selected ? "> " : "  ") + LocalizationService.Get(titleKey),
                    selected ? selectedStyle : optionStyle))
            {
                model.SelectCutscene(index);
                ConfirmSelection();
            }
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

            if (RetroUiInput.ScrollDelta.sqrMagnitude > 0f &&
                panel.Contains(RetroUiTheme.LogicalMousePosition(canvas)))
            {
                optionsScroll = Mathf.Clamp(
                    optionsScroll +
                    (RetroUiInput.ScrollDelta.y > 0f ? 1 : -1),
                    0,
                    maximumScroll);
                RetroUiInput.ConsumeScroll();
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
        // rather than relying on a font glyph.
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
                RetroUiInput.PointerMoved)
            {
                model.SelectOptionsRow(row);
            }

            bool selected = SelectedOptionsRow == row;
            DrawSelection(rect, selected);
            string label = LocalizationService.Get(localizationKey);
            if (RetroUiInput.Button(
                    rect,
                    (selected ? "> " : "  ") + label,
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
                case PauseMenuOptionsRow.CleanAntialiasing:
                    GraphicsEffectsSettings.CleanAntialiasingEnabled =
                        !GraphicsEffectsSettings.CleanAntialiasingEnabled;
                    return;
                case PauseMenuOptionsRow.CleanReflections:
                    GraphicsEffectsSettings.CleanReflectionsEnabled =
                        !GraphicsEffectsSettings.CleanReflectionsEnabled;
                    return;
                case PauseMenuOptionsRow.CleanVolume:
                    GraphicsEffectsSettings.CleanVolumeEnabled =
                        !GraphicsEffectsSettings.CleanVolumeEnabled;
                    return;
                case PauseMenuOptionsRow.CleanHalation:
                    GraphicsEffectsSettings.CleanHalationEnabled =
                        !GraphicsEffectsSettings.CleanHalationEnabled;
                    return;
                case PauseMenuOptionsRow.CleanDirectionalHighlights:
                    GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled =
                        !GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled;
                    return;
                case PauseMenuOptionsRow.CleanBounce:
                    GraphicsEffectsSettings.CleanBounceEnabled =
                        !GraphicsEffectsSettings.CleanBounceEnabled;
                    return;
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
                case PauseMenuOptionsRow.AspectRatio43:
                    GraphicsEffectsSettings.AspectRatio43Enabled =
                        !GraphicsEffectsSettings
                            .AspectRatio43Enabled;
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
                case PauseMenuOptionsRow.CleanAntialiasing:
                    return GraphicsEffectsSettings.CleanAntialiasingEnabled;
                case PauseMenuOptionsRow.CleanReflections:
                    return GraphicsEffectsSettings.CleanReflectionsEnabled;
                case PauseMenuOptionsRow.CleanVolume:
                    return GraphicsEffectsSettings.CleanVolumeEnabled;
                case PauseMenuOptionsRow.CleanHalation:
                    return GraphicsEffectsSettings.CleanHalationEnabled;
                case PauseMenuOptionsRow.CleanDirectionalHighlights:
                    return GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled;
                case PauseMenuOptionsRow.CleanBounce:
                    return GraphicsEffectsSettings.CleanBounceEnabled;
                case PauseMenuOptionsRow.DepthOfField:
                    return GraphicsEffectsSettings
                        .DepthOfFieldEnabled;
                case PauseMenuOptionsRow.IntoxicationFx:
                    return GraphicsEffectsSettings
                        .IntoxicationLensFxEnabled;
                case PauseMenuOptionsRow.AspectRatio43:
                    return GraphicsEffectsSettings
                        .AspectRatio43Enabled;
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
                RetroUiInput.PointerMoved)
            {
                model.SelectOption(option);
            }

            bool selected = SelectedOption == option;
            DrawSelection(rect, selected);
            if (RetroUiInput.Button(
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
                RetroUiInput.PointerMoved)
            {
                model.SelectConfirmation(yes);
            }

            bool selected = ConfirmationYesSelected == yes;
            DrawSelection(rect, selected);
            if (RetroUiInput.Button(
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
            return GameInput.WasPressed(
                GameInputAction.Pause, GameInputContext.PauseMenu);
        }

        private static bool WasCancelPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.GamepadCancel, GameInputContext.PauseMenu);
        }

        private static int ReadSelectionDelta()
        {
            return GameInput.ReadMenuSelectionDelta(GameInputContext.PauseMenu);
        }

        private static bool WasConfirmPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Confirm, GameInputContext.PauseMenu);
        }
    }
}
