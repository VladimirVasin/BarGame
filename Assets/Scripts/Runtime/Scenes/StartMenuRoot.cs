using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The launch card and a second screen choosing where the fresh session
    /// starts. Only confirming a destination starts the clock and loading.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StartMenuRoot : MonoBehaviour
    {
        /// <summary>
        /// 07:40 for every selectable start. The village remains the first
        /// choice; the retained Home waking opening uses its own earlier hour.
        /// </summary>
        public const int VillageMorningMinuteOfDay = NewGameStartService.MorningMinuteOfDay;

        // One compact vertical card, centred on the shared 640x360 canvas.
        // Keep these logical rects together so drawing and pointer hitboxes
        // cannot drift apart when the canvas is scaled.
        internal static Rect MenuPanelRect =>
            new Rect(206f, 147f, 228f, 66f);
        internal static Rect MenuNewGameRect =>
            new Rect(218f, 156f, 204f, 22f);
        internal static Rect MenuQuitRect =>
            new Rect(218f, 182f, 204f, 22f);
        internal static Rect LocationPanelRect => new Rect(166f, 18f, 308f, 324f);
        internal static Rect LocationOptionRect(int index) => new Rect(178f, 54f + index * 22f, 284f, 20f);

        private readonly StartMenuModel model = new StartMenuModel();

        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;
        private GUIStyle titleStyle;

        public Camera BackdropCamera { get; private set; }
        public bool IsStartingNewGame { get; private set; }
        public bool QuitRequested { get; private set; }
        public StartMenuOption SelectedOption => model.SelectedOption;
        public bool IsChoosingLocation => model.IsChoosingLocation;
        public NewGameLocation SelectedLocation => model.SelectedLocation;
        public bool IsBackSelected => model.IsBackSelected;

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
            // The card is now interactive and will sit idle for as long as
            // the player reads it: fetch the hero and pooled pedestrian
            // prefabs in the background so the first composition finds them
            // resident. Every load is asynchronous; a restart that returns
            // here skips what is already warm.
            AreaAssetWarmup.BeginFromMenu();
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
                case StartMenuAction.ChooseLocation:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return true;
                case StartMenuAction.Back:
                    RetroAudio.Play(RetroSfxId.UiCancel);
                    return true;
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

        public bool SelectLocation(NewGameLocation location)
        {
            if (IsBusy || !model.SelectLocation(location)) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool SelectBack()
        {
            if (IsBusy || !model.SelectBack()) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool ReturnToMainMenu()
        {
            if (IsBusy || !model.ReturnToMainMenu()) return false;
            RetroAudio.Play(RetroSfxId.UiCancel);
            return true;
        }

        private bool BeginNewGame()
        {
            IsStartingNewGame = true;
            NewGameLocation location = model.SelectedLocation;
            if (NewGameStartService.TryStart(location))
            {
                return true;
            }

            // Keep the selected destination available when loading is refused.
            IsStartingNewGame = false;
            model.Open();
            model.Confirm();
            model.SelectLocation(location);
            GameLog.Warning(
                "menu",
                "new_game_travel_refused",
                GameLog.Field(
                    "destination_location",
                    location.ToString()));
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
                if (model.IsChoosingLocation) ReturnToMainMenu();
                else SelectOption(StartMenuOption.Quit);
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
                if (model.IsChoosingLocation)
                {
                    DrawLocations(canvas);
                    return;
                }
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

        private void DrawLocations(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(LocationPanelRect, RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter,
                false, 0f, 1f);
            GUI.Label(new Rect(178f, 26f, 284f, 22f),
                LocalizationService.Get("opening.choose_location"), titleStyle);
            Vector2 mouse = RetroUiTheme.LogicalMousePosition(canvas);
            for (int index = 0; index <= NewGameLocationCatalog.Count; index++)
            {
                bool back = index == NewGameLocationCatalog.Count;
                NewGameLocation location = back ? NewGameLocation.Count : NewGameLocationCatalog.Get(index);
                Rect rect = LocationOptionRect(index);
                if (rect.Contains(mouse) && Event.current.type == EventType.MouseMove)
                {
                    if (back) SelectBack(); else SelectLocation(location);
                }
                bool selected = model.SelectedLocation == location;
                if (selected) RetroUiTheme.DrawSelection(rect, true);
                string label = LocalizationService.Get(back ? "opening.back" : NewGameLocationCatalog.LabelKey(location));
                if (GUI.Button(rect, (selected ? "> " : "  ") + label, selected ? selectedStyle : optionStyle))
                {
                    if (back) SelectBack(); else SelectLocation(location);
                    ConfirmSelection();
                    break;
                }
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
            titleStyle = RetroUiTheme.CreateLabelStyle(13, TextAnchor.MiddleCenter, RetroUiTheme.Text, true);
        }
    }
}
