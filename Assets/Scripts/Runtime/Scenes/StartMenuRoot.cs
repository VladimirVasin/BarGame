using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The launch card. Two items over a black field: a new game, which starts
    /// the session clock at the village morning and travels to the village
    /// through the ordinary area loading screen, and quit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StartMenuRoot : MonoBehaviour
    {
        /// <summary>
        /// 07:40. The village is the game's first place, and this is the hour
        /// its light is graded for; the retained Home opening still begins at
        /// 05:59 whenever it is entered.
        /// </summary>
        public const int VillageMorningMinuteOfDay = (7 * 60) + 40;

        // One compact vertical card, centred on the shared 640x360 canvas.
        // Keep these logical rects together so drawing and pointer hitboxes
        // cannot drift apart when the canvas is scaled.
        internal static Rect MenuPanelRect =>
            new Rect(206f, 147f, 228f, 66f);
        internal static Rect MenuNewGameRect =>
            new Rect(218f, 156f, 204f, 22f);
        internal static Rect MenuQuitRect =>
            new Rect(218f, 182f, 204f, 22f);

        private readonly StartMenuModel model = new StartMenuModel();

        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;

        public Camera BackdropCamera { get; private set; }
        public bool IsStartingNewGame { get; private set; }
        public bool QuitRequested { get; private set; }
        public StartMenuOption SelectedOption => model.SelectedOption;

        private bool IsBusy => IsStartingNewGame || QuitRequested;

        private void Awake()
        {
            GameLog.SetScene(gameObject.scene.name);
            BackdropCamera = RuntimeSceneSetup.EnsureStartMenu();
            GameTimeScaleRuntime.EnsureInstalled();
            // The launch scene is the session boundary: arriving here from a
            // pause-menu restart must leave no clock running behind the card.
            GameSessionState.BeginNewGame();
            model.Open();
        }

        public bool MoveSelection(int delta)
        {
            if (IsBusy || !model.MoveSelection(delta))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool SelectOption(StartMenuOption option)
        {
            if (IsBusy || !model.SelectOption(option))
            {
                return false;
            }

            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool ConfirmSelection()
        {
            if (IsBusy)
            {
                return false;
            }

            StartMenuAction action = model.Confirm();
            switch (action)
            {
                case StartMenuAction.NewGame:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return BeginNewGame();
                case StartMenuAction.Quit:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    QuitRequested = true;
                    Application.Quit();
                    return true;
                default:
                    return false;
            }
        }

        private bool BeginNewGame()
        {
            IsStartingNewGame = true;
            GameSessionState.TryStartGameTimeAt(
                VillageMorningMinuteOfDay);
            if (AreaTravelService.Request(GameAreaId.AlpineVillage))
            {
                return true;
            }

            // A refused trip must leave a working card rather than a dead
            // screen. The clock stays started; a retry only re-requests.
            IsStartingNewGame = false;
            model.Open();
            GameLog.Warning(
                "menu",
                "new_game_travel_refused",
                GameLog.Field(
                    "destination_area",
                    GameAreaId.AlpineVillage.ToString()));
            return false;
        }

        private void Update()
        {
            if (IsBusy)
            {
                return;
            }

            int delta = GameInput.ReadMenuSelectionDelta(
                GameInputContext.Menu);
            if (delta != 0)
            {
                MoveSelection(delta);
            }

            if (GameInput.WasPressed(
                    GameInputAction.Cancel,
                    GameInputContext.Menu))
            {
                // Escape selects quit without taking it, exactly as the
                // retained waking card does.
                SelectOption(StartMenuOption.Quit);
                return;
            }

            if (GameInput.WasPressed(
                    GameInputAction.Confirm,
                    GameInputContext.Menu))
            {
                ConfirmSelection();
            }
        }

        private void OnGUI()
        {
            if (IsBusy)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -120;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                RetroUiTheme.DrawPanel(
                    MenuPanelRect,
                    RetroUiTheme.PanelInset,
                    RetroUiTheme.FrameOuter,
                    false,
                    0f,
                    1f);
                DrawOption(
                    canvas,
                    MenuNewGameRect,
                    StartMenuOption.NewGame,
                    "opening.new_game");
                DrawOption(
                    canvas,
                    MenuQuitRect,
                    StartMenuOption.Quit,
                    "opening.quit");
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawOption(
            RetroUiCanvas canvas,
            Rect rect,
            StartMenuOption option,
            string localizationKey)
        {
            Vector2 mouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            if (rect.Contains(mouse) &&
                Event.current.type == EventType.MouseMove)
            {
                SelectOption(option);
            }

            bool selected = model.SelectedOption == option;
            if (selected)
            {
                RetroUiTheme.DrawSelection(rect, true);
            }

            string prefix = selected ? "> " : "  ";
            if (GUI.Button(
                    rect,
                    prefix +
                    LocalizationService.Get(localizationKey),
                    selected
                        ? selectedStyle
                        : optionStyle))
            {
                SelectOption(option);
                ConfirmSelection();
            }
        }

        private void EnsureStyles()
        {
            if (selectedStyle != null)
            {
                return;
            }

            selectedStyle = RetroUiTheme.CreateButtonStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.SelectionText,
                false);
            optionStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Muted,
                false);
        }
    }
}
