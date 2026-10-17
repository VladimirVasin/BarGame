using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The launch card, narrative destination picker and isolated combat preparation.
    /// Each flow loads its scene only after the player's explicit confirmation.
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
            new Rect(206f, 134f, 228f, 92f);
        internal static Rect MenuNewGameRect =>
            new Rect(218f, 143f, 204f, 22f);
        internal static Rect MenuCombatTestRect =>
            new Rect(218f, 169f, 204f, 22f);
        internal static Rect MenuQuitRect =>
            new Rect(218f, 195f, 204f, 22f);
        internal static Rect LocationPanelRect => new Rect(166f, 18f, 308f, 324f);
        internal static Rect LocationOptionRect(int index) => new Rect(178f, 54f + index * 22f, 284f, 20f);
        internal static Rect CombatPanelRect => new Rect(154f, 10f, 332f, 340f);
        internal static Rect CombatOptionRect(int index) => new Rect(166f,
            index < 3 ? 106f + index * 22f : index < 5 ? 197f + (index - 3) * 21f :
                242f + (index - 5) * 22f, 308f, 20f);

        private readonly StartMenuModel model = new StartMenuModel();

        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;
        private GUIStyle titleStyle;
        private GUIStyle descriptionStyle;

        public Camera BackdropCamera { get; private set; }
        public bool IsStartingNewGame { get; private set; }
        public bool IsStartingCombatTest { get; private set; }
        public bool QuitRequested { get; private set; }
        public StartMenuOption SelectedOption => model.SelectedOption;
        public bool IsChoosingLocation => model.IsChoosingLocation;
        public bool IsChoosingCombatWeapon => model.IsChoosingCombatWeapon;
        public NewGameLocation SelectedLocation => model.SelectedLocation;
        public CombatWeaponId SelectedCombatWeapon => model.SelectedCombatWeapon;
        public CombatLoadoutSelection SelectedCombatLoadout => model.SelectedCombatLoadout;
        public CombatTestMode SelectedCombatMode => model.SelectedCombatMode;
        public CombatEncounterSettings SelectedCombatEncounterSettings => model.SelectedCombatEncounterSettings;
        public CombatTestSelection SelectedCombatSelection => model.SelectedCombatSelection;
        public CombatPreparationOption SelectedCombatOption => model.SelectedCombatOption;
        public bool IsBackSelected => model.IsBackSelected;

        private bool IsBusy => IsStartingNewGame || IsStartingCombatTest || QuitRequested;

        private void Awake()
        {
            GameLog.SetScene(gameObject.scene.name);
            BackdropCamera = RuntimeSceneSetup.EnsureStartMenu();
            GameTimeScaleRuntime.EnsureInstalled();
            // The launch scene is the session boundary: arriving here from a
            // pause-menu restart must leave no clock running behind the card.
            GameSessionState.BeginNewGame("menu_reset");
            model.Open();
            if (CombatTestStartService.TryConsumePreparationSelection(out CombatTestSelection selection))
                model.OpenCombatPreparation(selection);
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
                case StartMenuAction.ChooseCombatWeapon:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return true;
                case StartMenuAction.Back:
                    RetroAudio.Play(RetroSfxId.UiCancel);
                    return true;
                case StartMenuAction.NewGame:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return BeginNewGame();
                case StartMenuAction.CombatTest:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return BeginCombatTest();
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

        public bool SelectCombatOption(CombatPreparationOption option)
        {
            if (IsBusy || !model.SelectCombatOption(option)) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool FocusCombatOption(CombatPreparationOption option)
        {
            if (IsBusy || !model.FocusCombatOption(option)) return false;
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

        private bool BeginCombatTest()
        {
            IsStartingCombatTest = true;
            CombatTestSelection selection = model.SelectedCombatSelection;
            if (CombatTestStartService.TryStart(selection)) return true;

            IsStartingCombatTest = false;
            model.OpenCombatPreparation(selection);
            model.SelectCombatOption(CombatPreparationOption.Start);
            GameLog.Warning("menu", "combat_test_travel_refused");
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
                if (model.IsChoosingLocation || model.IsChoosingCombatWeapon) ReturnToMainMenu();
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
                if (model.IsChoosingCombatWeapon)
                {
                    DrawCombatPreparation(canvas);
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
                    MenuCombatTestRect,
                    StartMenuOption.CombatTest,
                    "combat.title");
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

        private void DrawCombatPreparation(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(CombatPanelRect, RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter,
                false, 0f, 1f);
            GUI.Label(new Rect(166f, 18f, 308f, 20f), LocalizationService.Get("combat.choose_weapon"), titleStyle);
            GUI.Label(new Rect(166f, 41f, 308f, 12f),
                LocalizationService.Get("combat.loadout.pistol_carried"), descriptionStyle);
            GUI.Label(new Rect(166f, 54f, 308f, 14f),
                LocalizationService.Get("combat.loadout.crowbar_carried"), descriptionStyle);
            GUI.Label(new Rect(166f, 67f, 308f, 14f),
                LocalizationService.Get("combat.loadout.shotgun_carried"), descriptionStyle);
            GUI.Label(new Rect(166f, 90f, 308f, 14f),
                LocalizationService.Get("combat.loadout.initial_held"), descriptionStyle);
            GUI.Label(new Rect(166f, 181f, 308f, 14f),
                LocalizationService.Get("combat.mode.title"), descriptionStyle);
            Vector2 mouse = RetroUiTheme.LogicalMousePosition(canvas);
            for (int index = 0; index < (int)CombatPreparationOption.Count; index++)
            {
                var option = (CombatPreparationOption)index;
                Rect rect = CombatOptionRect(index);
                if (rect.Contains(mouse) && Event.current.type == EventType.MouseMove) FocusCombatOption(option);
                bool selected = model.SelectedCombatOption == option;
                if (selected) RetroUiTheme.DrawSelection(rect, true);
                string key = option switch
                {
                    CombatPreparationOption.Crowbar => "combat.weapon.crowbar",
                    CombatPreparationOption.Pistol => "combat.weapon.pistol",
                    CombatPreparationOption.Shotgun => "combat.weapon.shotgun",
                    CombatPreparationOption.Standard => "combat.mode.standard",
                    CombatPreparationOption.Endless => "combat.mode.endless",
                    CombatPreparationOption.OpponentLimit => "combat.endless.limit",
                    CombatPreparationOption.Start => "combat.start",
                    _ => "opening.back"
                };
                bool choice = option < CombatPreparationOption.OpponentLimit;
                bool chosen = option switch
                {
                    CombatPreparationOption.Crowbar => model.SelectedCombatLoadout.InitialHeld == CombatWeaponId.Crowbar,
                    CombatPreparationOption.Shotgun => model.SelectedCombatLoadout.InitialHeld == CombatWeaponId.Shotgun,
                    CombatPreparationOption.Pistol => model.SelectedCombatLoadout.InitialHeld == CombatWeaponId.Pistol,
                    CombatPreparationOption.Standard => model.SelectedCombatMode == CombatTestMode.Standard,
                    CombatPreparationOption.Endless => model.SelectedCombatMode == CombatTestMode.Endless,
                    _ => false
                };
                string mark = choice ? chosen ? "[x] " : "[ ] " : string.Empty;
                string label = option == CombatPreparationOption.OpponentLimit ?
                    string.Format(LocalizationService.Get(key), model.SelectedCombatEncounterSettings.MaximumLiveOpponents) :
                    LocalizationService.Get(key);
                if (GUI.Button(rect, (selected ? "> " : "  ") + mark + label,
                    selected ? selectedStyle : optionStyle))
                {
                    FocusCombatOption(option);
                    ConfirmSelection();
                    break;
                }
            }
            GUI.Label(new Rect(166f, 333f, 308f, 14f),
                LocalizationService.Get("combat.switch.controls"), descriptionStyle);
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
            descriptionStyle = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleCenter, RetroUiTheme.Muted);
        }
    }
}
