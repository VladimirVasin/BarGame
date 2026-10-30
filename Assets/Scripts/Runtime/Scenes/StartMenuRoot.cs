using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The story launch card and its separate Debug destination/combat/tool pages.
    /// Each flow loads its scene only after the player's explicit confirmation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StartMenuRoot : MonoBehaviour
    {
        /// <summary>
        /// Default time before the player changes the new-game clock.
        /// </summary>
        public const int VillageMorningMinuteOfDay = NewGameStartService.MorningMinuteOfDay;

        // One compact vertical card, centred on the shared 640x360 canvas.
        // Keep these logical rects together so drawing and pointer hitboxes
        // cannot drift apart when the canvas is scaled.
        internal static Rect MenuPanelRect =>
            new Rect(206f, 132f, 228f, 92f);
        internal static Rect MenuNewGameRect =>
            new Rect(218f, 141f, 204f, 22f);
        internal static Rect MenuDebugRect =>
            new Rect(218f, 167f, 204f, 22f);
        internal static Rect MenuQuitRect =>
            new Rect(218f, 193f, 204f, 22f);
        internal static Rect DebugMenuPanelRect =>
            new Rect(206f, 108f, 228f, 144f);
        internal static Rect DebugNewGameRect =>
            new Rect(218f, 117f, 204f, 22f);
        internal static Rect MenuCombatTestRect =>
            new Rect(218f, 143f, 204f, 22f);
        internal static Rect MenuLaboratoryRect =>
            new Rect(218f, 169f, 204f, 22f);
        internal static Rect MenuVocalizerRect =>
            new Rect(218f, 195f, 204f, 22f);
        internal static Rect DebugBackRect =>
            new Rect(218f, 221f, 204f, 22f);
        internal static Rect LocationPanelRect => new Rect(166f, 18f, 308f, 324f);
        internal static Rect LocationOptionRect(int index) => new Rect(178f, 54f + index * 20f, 284f, 18f);
        internal static Rect StartTimePanelRect => new Rect(166f, 76f, 308f, 208f);
        internal static Rect StartTimeOptionRect(StartTimeOption option) => new Rect(178f,
            option < StartTimeOption.Continue ? 140f + (int)option * 28f :
                212f + ((int)option - (int)StartTimeOption.Continue) * 24f, 284f, 22f);
        internal static Rect CombatPanelRect => new Rect(154f, 10f, 332f, 340f);
        internal static Rect CombatOptionRect(int index) => new Rect(166f,
            index < 3 ? 106f + index * 22f : index < 5 ? 197f + (index - 3) * 21f :
                242f + (index - 5) * 22f, 308f, 20f);
        internal static Rect LaboratoryOptionRect(int index) => new Rect(166f,
            index < 3 ? 106f + index * 22f : index == 3 ? 202f : 264f + (index - 4) * 24f, 308f, 20f);
        internal static Rect LaboratoryEnemyPanelRect => new Rect(166f, 62f, 308f, 236f);
        internal static Rect LaboratoryEnemyOptionRect(int index) => new Rect(178f, 102f + index * 24f, 284f, 22f);

        private readonly StartMenuModel model = new StartMenuModel();

        private GUIStyle selectedStyle;
        private GUIStyle optionStyle;
        private GUIStyle titleStyle;
        private GUIStyle descriptionStyle;

        private Vector2 lastPointerPosition, logicalPointerPosition;
        private bool hasPointerPosition, pointerMoved;

        public Camera BackdropCamera { get; private set; }
        public bool IsStartingNewGame { get; private set; }
        public bool IsStartingCombatTest { get; private set; }
        public bool QuitRequested { get; private set; }
        public StartMenuOption SelectedOption => model.SelectedOption;
        public bool IsDebugMenu => model.IsDebugMenu;
        public bool IsChoosingLocation => model.IsChoosingLocation;
        public bool IsChoosingStartTime => model.IsChoosingStartTime;
        public int SelectedStartMinuteOfDay => model.SelectedStartMinuteOfDay;
        public StartTimeOption SelectedStartTimeOption => model.SelectedStartTimeOption;
        public string AvailabilityMessageKey => model.AvailabilityMessageKey;
        public bool IsChoosingCombatWeapon => model.IsChoosingCombatWeapon;
        public bool IsChoosingLaboratoryEnemy => model.IsChoosingLaboratoryEnemy;
        public bool IsVocalizerActive => model.IsVocalizerActive;
        public RussianVocalizerLab VocalizerLab { get; private set; }
        public NewGameLocation SelectedLocation => model.SelectedLocation;
        public CombatWeaponId SelectedCombatWeapon => model.SelectedCombatWeapon;
        public CombatLoadoutSelection SelectedCombatLoadout => model.SelectedCombatLoadout;
        public CombatTestMode SelectedCombatMode => model.SelectedCombatMode;
        public CombatEnemyKind SelectedLaboratoryEnemy => model.SelectedLaboratoryEnemy;
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

        public bool OpenDebugMenu()
        {
            if (IsBusy || !model.OpenDebugMenu()) return false;
            RetroAudio.Play(RetroSfxId.UiConfirm);
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
                case StartMenuAction.ChooseStartTime:
                case StartMenuAction.ChooseCombatWeapon:
                case StartMenuAction.ChooseLaboratoryEnemy:
                case StartMenuAction.OpenDebug:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return true;
                case StartMenuAction.LocationUnavailable:
                    RetroAudio.Play(RetroSfxId.UiCancel);
                    return false;
                case StartMenuAction.OpenVocalizer:
                    VocalizerLab ??= gameObject.AddComponent<RussianVocalizerLab>();
                    VocalizerLab.Open();
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return true;
                case StartMenuAction.Back:
                    RetroAudio.Play(RetroSfxId.UiCancel);
                    return true;
                case StartMenuAction.NewGame:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return BeginNewGame();
                case StartMenuAction.StoryNewGame:
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return BeginStoryNewGame();
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

        public bool SelectStartTime(int minuteOfDay)
        {
            if (IsBusy || !model.SelectStartTime(minuteOfDay)) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool SelectStartTimeOption(StartTimeOption option)
        {
            if (IsBusy || !model.SelectStartTimeOption(option)) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool AdjustStartTime(int direction)
        {
            if (IsBusy || !model.AdjustStartTime(direction)) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool ReturnToStartTime()
        {
            if (IsBusy || !model.ReturnToStartTime()) return false;
            RetroAudio.Play(RetroSfxId.UiCancel);
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
            if (VocalizerLab != null) VocalizerLab.Close();
            RetroAudio.Play(RetroSfxId.UiCancel);
            return true;
        }

        public bool SelectLaboratoryEnemy(CombatEnemyKind kind)
        {
            if (IsBusy || !model.SelectLaboratoryEnemy(kind)) return false;
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        public bool ReturnToCombatPreparation()
        {
            if (IsBusy || !model.ReturnToCombatPreparation()) return false;
            RetroAudio.Play(RetroSfxId.UiCancel);
            return true;
        }

        private bool BeginNewGame()
        {
            IsStartingNewGame = true;
            NewGameLocation location = model.SelectedLocation;
            int minuteOfDay = model.SelectedStartMinuteOfDay;
            if (NewGameStartService.TryStart(location, minuteOfDay))
            {
                return true;
            }

            // Keep the selected destination available when loading is refused.
            IsStartingNewGame = false;
            model.OpenDebugStartTime(minuteOfDay);
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

        private bool BeginStoryNewGame()
        {
            IsStartingNewGame = true;
            if (NewGameStartService.TryStartStory()) return true;
            IsStartingNewGame = false;
            model.Open();
            GameLog.Warning("menu", "story_start_refused");
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
            // The text field owns spaces, arrows and Enter while the lab is open.
            // Its IMGUI handler accepts only Escape and Ctrl+Enter as commands.
            if (IsBusy || model.IsVocalizerActive)
            {
                return;
            }

            int delta = model.IsChoosingStartTime
                ? GameInput.ReadMenuVerticalSelectionDelta(GameInputContext.Menu)
                : GameInput.ReadMenuSelectionDelta(GameInputContext.Menu);
            if (delta != 0)
            {
                MoveSelection(delta);
            }
            if (model.IsChoosingStartTime)
                AdjustStartTime(GameInput.ReadMenuHorizontalSelectionDelta(GameInputContext.Menu));

            if (GameInput.WasPressed(
                    GameInputAction.Cancel,
                    GameInputContext.Menu))
            {
                if (model.IsChoosingLaboratoryEnemy) ReturnToCombatPreparation();
                else if (model.IsChoosingLocation) ReturnToStartTime();
                else if (model.IsChoosingStartTime || model.IsChoosingCombatWeapon) ReturnToMainMenu();
                else if (model.IsDebugMenu) ReturnToMainMenu();
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

        private void OnDisable()
        {
            hasPointerPosition = false;
            pointerMoved = false;
            if (VocalizerLab != null) VocalizerLab.Close();
            if (model.IsVocalizerActive) model.ReturnToMainMenu();
        }

        private void RefreshPointerHover(RetroUiCanvas canvas)
        {
            pointerMoved = false;
            if (Event.current.type != EventType.Repaint) return;
            if (!GameCursor.ApplicationFocused || !GameInput.CanRead(GameInputContext.Menu))
            {
                hasPointerPosition = false;
                return;
            }
            // Runtime OnGUI receives no MouseMove events. Read the shared
            // Input System pointer once, before GUI.matrix applies the canvas.
            // Its bottom-left screen origin becomes the GUI's top-left origin.
            Vector2 pointer = GameInput.ModalPointerPosition;
            pointer.y = Screen.height - pointer.y;
            logicalPointerPosition = canvas.ScreenToLogical(pointer);
            pointerMoved = hasPointerPosition && (pointer - lastPointerPosition).sqrMagnitude >= .25f;
            if (!hasPointerPosition || pointerMoved) lastPointerPosition = pointer;
            hasPointerPosition = true;
            // First observation establishes a baseline. Retaining it across
            // page changes lets stationary mouse/keyboard focus coexist.
        }

        private void OnGUI()
        {
            using var input = RetroUiInput.Begin();
            if (IsBusy)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -120;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            RefreshPointerHover(canvas);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                if (model.IsVocalizerActive)
                {
                    if (VocalizerLab.Draw()) ReturnToMainMenu();
                    return;
                }
                if (model.IsChoosingLocation)
                {
                    DrawLocations(canvas);
                    return;
                }
                if (model.IsChoosingStartTime)
                {
                    DrawStartTime(canvas);
                    return;
                }
                if (model.IsChoosingLaboratoryEnemy)
                {
                    DrawLaboratoryEnemies(canvas);
                    return;
                }
                if (model.IsChoosingCombatWeapon)
                {
                    DrawCombatPreparation(canvas);
                    return;
                }
                if (model.IsDebugMenu)
                {
                    DrawDebugMenu(canvas);
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
                    MenuDebugRect,
                    StartMenuOption.Debug,
                    "opening.debug");
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

        private void DrawDebugMenu(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(DebugMenuPanelRect, RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter, false, 0f, 1f);
            DrawOption(canvas, DebugNewGameRect, StartMenuOption.NewGame, "opening.debug_location_start");
            DrawOption(canvas, MenuCombatTestRect, StartMenuOption.CombatTest, "combat.title");
            DrawOption(canvas, MenuLaboratoryRect, StartMenuOption.Laboratory, "combat.laboratory.menu");
            DrawOption(canvas, MenuVocalizerRect, StartMenuOption.Vocalizer, "Вокализатор", true);
            DrawOption(canvas, DebugBackRect, StartMenuOption.Back, "opening.back");
        }

        private void DrawLocations(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(LocationPanelRect, RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter,
                false, 0f, 1f);
            GUI.Label(new Rect(178f, 26f, 284f, 22f),
                string.Format(LocalizationService.Get("opening.choose_location_at"),
                    model.SelectedStartMinuteOfDay / 60, model.SelectedStartMinuteOfDay % 60), titleStyle);
            Vector2 mouse = logicalPointerPosition;
            for (int index = 0; index <= NewGameLocationCatalog.Count; index++)
            {
                bool back = index == NewGameLocationCatalog.Count;
                NewGameLocation location = back ? NewGameLocation.Count : NewGameLocationCatalog.Get(index);
                Rect rect = LocationOptionRect(index);
                if (rect.Contains(mouse) && pointerMoved)
                {
                    if (back) SelectBack(); else SelectLocation(location);
                }
                bool selected = model.SelectedLocation == location;
                if (selected) RetroUiTheme.DrawSelection(rect, true);
                string label = LocalizationService.Get(back ? "opening.back" : NewGameLocationCatalog.LabelKey(location));
                if (RetroUiInput.Button(rect, (selected ? "> " : "  ") + label, selected ? selectedStyle : optionStyle))
                {
                    if (back) SelectBack(); else SelectLocation(location);
                    ConfirmSelection();
                    break;
                }
            }
            if (!string.IsNullOrEmpty(model.AvailabilityMessageKey))
                GUI.Label(new Rect(178f, 316f, 284f, 22f),
                    LocalizationService.Get(model.AvailabilityMessageKey), descriptionStyle);
        }

        private void DrawStartTime(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(StartTimePanelRect, RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter,
                false, 0f, 1f);
            GUI.Label(new Rect(178f, 86f, 284f, 22f),
                LocalizationService.Get("opening.choose_start_time"), titleStyle);
            GUI.Label(new Rect(178f, 112f, 284f, 18f),
                string.Format("{0:00}:{1:00}", model.SelectedStartMinuteOfDay / 60,
                    model.SelectedStartMinuteOfDay % 60), titleStyle);
            Vector2 mouse = logicalPointerPosition;
            for (int index = 0; index < (int)StartTimeOption.Count; index++)
            {
                var option = (StartTimeOption)index;
                Rect rect = StartTimeOptionRect(option);
                if (rect.Contains(mouse) && pointerMoved) SelectStartTimeOption(option);
                bool selected = model.SelectedStartTimeOption == option;
                if (selected) RetroUiTheme.DrawSelection(rect, true);
                if (option < StartTimeOption.Continue)
                {
                    int value = option == StartTimeOption.Hour ? model.SelectedStartMinuteOfDay / 60 :
                        model.SelectedStartMinuteOfDay % 60;
                    string key = option == StartTimeOption.Hour ? "opening.start_hour" : "opening.start_minute";
                    if (RetroUiInput.Button(new Rect(rect.x, rect.y, 32f, rect.height), "−", optionStyle))
                    {
                        SelectStartTimeOption(option);
                        AdjustStartTime(-1);
                    }
                    GUI.Label(new Rect(rect.x + 36f, rect.y, rect.width - 72f, rect.height),
                        string.Format(LocalizationService.Get(key), value), titleStyle);
                    if (RetroUiInput.Button(new Rect(rect.xMax - 32f, rect.y, 32f, rect.height), "+", optionStyle))
                    {
                        SelectStartTimeOption(option);
                        AdjustStartTime(1);
                    }
                }
                else if (RetroUiInput.Button(rect, (selected ? "> " : "  ") + LocalizationService.Get(
                    option == StartTimeOption.Continue ? "opening.continue" : "opening.back"),
                    selected ? selectedStyle : optionStyle))
                {
                    SelectStartTimeOption(option);
                    ConfirmSelection();
                    break;
                }
            }
            GUI.Label(new Rect(178f, 266f, 284f, 12f),
                LocalizationService.Get("opening.start_time_controls"), descriptionStyle);
        }

        private void DrawCombatPreparation(RetroUiCanvas canvas)
        {
            bool laboratory = model.SelectedCombatMode == CombatTestMode.Laboratory;
            RetroUiTheme.DrawPanel(CombatPanelRect, RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter,
                false, 0f, 1f);
            GUI.Label(new Rect(166f, 18f, 308f, 20f),
                LocalizationService.Get(laboratory ? "combat.mode.laboratory" : "combat.choose_weapon"), titleStyle);
            GUI.Label(new Rect(166f, 41f, 308f, 12f),
                LocalizationService.Get("combat.loadout.pistol_carried"), descriptionStyle);
            GUI.Label(new Rect(166f, 54f, 308f, 14f),
                LocalizationService.Get("combat.loadout.crowbar_carried"), descriptionStyle);
            GUI.Label(new Rect(166f, 67f, 308f, 14f),
                LocalizationService.Get("combat.loadout.shotgun_carried"), descriptionStyle);
            GUI.Label(new Rect(166f, 90f, 308f, 14f),
                LocalizationService.Get("combat.loadout.initial_held"), descriptionStyle);
            GUI.Label(new Rect(166f, 181f, 308f, 14f),
                LocalizationService.Get(laboratory ? "combat.laboratory.choose_enemy" : "combat.mode.title"), descriptionStyle);
            Vector2 mouse = logicalPointerPosition;
            for (int index = 0; index < model.CombatOptionCount; index++)
            {
                CombatPreparationOption option = model.CombatOptionAt(index);
                Rect rect = laboratory ? LaboratoryOptionRect(index) : CombatOptionRect(index);
                if (rect.Contains(mouse) && pointerMoved) FocusCombatOption(option);
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
                    CombatPreparationOption.LaboratoryEnemy => "combat.laboratory.enemy",
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
                    option == CombatPreparationOption.LaboratoryEnemy ? string.Format(LocalizationService.Get(key),
                        LocalizationService.Get(StartMenuModel.LaboratoryEnemyLabelKey(model.SelectedLaboratoryEnemy))) :
                    LocalizationService.Get(key);
                if (RetroUiInput.Button(rect, (selected ? "> " : "  ") + mark + label,
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

        private void DrawLaboratoryEnemies(RetroUiCanvas canvas)
        {
            RetroUiTheme.DrawPanel(LaboratoryEnemyPanelRect, RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter,
                false, 0f, 1f);
            GUI.Label(new Rect(178f, 72f, 284f, 20f),
                LocalizationService.Get("combat.laboratory.choose_enemy"), titleStyle);
            Vector2 mouse = logicalPointerPosition;
            for (int index = 0; index <= StartMenuModel.LaboratoryEnemyCount; index++)
            {
                bool back = index == StartMenuModel.LaboratoryEnemyCount;
                var kind = (CombatEnemyKind)index;
                Rect rect = LaboratoryEnemyOptionRect(index);
                if (rect.Contains(mouse) && pointerMoved)
                {
                    if (back) SelectBack(); else SelectLaboratoryEnemy(kind);
                }
                bool selected = back ? model.IsBackSelected :
                    !model.IsBackSelected && model.SelectedLaboratoryEnemy == kind;
                if (selected) RetroUiTheme.DrawSelection(rect, true);
                string key = back ? "opening.back" : StartMenuModel.LaboratoryEnemyLabelKey(kind);
                if (RetroUiInput.Button(rect, (selected ? "> " : "  ") + LocalizationService.Get(key),
                    selected ? selectedStyle : optionStyle))
                {
                    if (back) SelectBack(); else SelectLaboratoryEnemy(kind);
                    ConfirmSelection();
                    break;
                }
            }
        }

        private void DrawOption(
            RetroUiCanvas canvas,
            Rect rect,
            StartMenuOption option,
            string localizationKey,
            bool literal = false)
        {
            Vector2 mouse = logicalPointerPosition;
            if (rect.Contains(mouse) &&
                pointerMoved)
            {
                SelectOption(option);
            }

            bool selected = model.SelectedOption == option;
            if (selected)
            {
                RetroUiTheme.DrawSelection(rect, true);
            }

            string prefix = selected ? "> " : "  ";
            if (RetroUiInput.Button(
                    rect,
                    prefix +
                    (literal ? localizationKey : LocalizationService.Get(localizationKey)),
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
