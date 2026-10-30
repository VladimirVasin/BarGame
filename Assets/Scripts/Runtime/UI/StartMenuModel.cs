using System;

namespace BarPromenade
{
    public enum StartMenuOption
    {
        NewGame = 0,
        CombatTest = 1,
        Laboratory = 2,
        Vocalizer = 3,
        Quit = 4,
        Debug = 5,
        Back = 6,
        Count = 7
    }

    public enum StartMenuAction
    {
        None = 0,
        NewGame = 1,
        Quit = 2,
        ChooseLocation = 3,
        Back = 4,
        CombatTest = 5,
        ChooseCombatWeapon = 6,
        ChooseLaboratoryEnemy = 7,
        OpenVocalizer = 8,
        ChooseStartTime = 9,
        LocationUnavailable = 10,
        StoryNewGame = 11,
        OpenDebug = 12
    }

    public enum StartTimeOption { Hour, Minute, Continue, Back, Count }

    public enum CombatPreparationOption
    {
        Crowbar, Shotgun, Pistol, Standard, Endless, OpponentLimit, Start, Back, LaboratoryEnemy, Count
    }

    /// <summary>
    /// The launch card and its location and weapon pickers. Committing is one-shot: the
    /// shared input reader goes quiet the moment a scene load starts, but an
    /// IMGUI button does not consult that policy, so the latch is what keeps a
    /// second click from asking for a second new game.
    /// </summary>
    public sealed class StartMenuModel
    {
        private static readonly StartMenuOption[] MainOptions =
            { StartMenuOption.NewGame, StartMenuOption.Debug, StartMenuOption.Quit };
        private static readonly StartMenuOption[] DebugOptions =
            { StartMenuOption.NewGame, StartMenuOption.CombatTest, StartMenuOption.Laboratory,
                StartMenuOption.Vocalizer, StartMenuOption.Back };
        private static readonly CombatPreparationOption[] WarehouseOptions =
        {
            CombatPreparationOption.Crowbar, CombatPreparationOption.Shotgun, CombatPreparationOption.Pistol,
            CombatPreparationOption.Standard, CombatPreparationOption.Endless, CombatPreparationOption.OpponentLimit,
            CombatPreparationOption.Start, CombatPreparationOption.Back
        };
        private static readonly CombatPreparationOption[] LaboratoryOptions =
        {
            CombatPreparationOption.Crowbar, CombatPreparationOption.Shotgun, CombatPreparationOption.Pistol,
            CombatPreparationOption.LaboratoryEnemy, CombatPreparationOption.Start, CombatPreparationOption.Back
        };
        private CombatTestMode warehouseMode = CombatTestMode.Standard;
        private bool laboratoryEnemyBackSelected;

        public StartMenuOption SelectedOption { get; private set; }
        public bool IsCommitted { get; private set; }
        /// <summary>Debug owns its root card and all nested launch/tool selectors.</summary>
        public bool IsDebugMenu { get; private set; }
        public bool IsChoosingStartTime { get; private set; }
        public int SelectedStartMinuteOfDay { get; private set; }
        public StartTimeOption SelectedStartTimeOption { get; private set; }
        public string AvailabilityMessageKey { get; private set; } = string.Empty;
        public bool IsChoosingLocation { get; private set; }
        public bool IsChoosingCombatWeapon { get; private set; }
        public bool IsChoosingLaboratoryEnemy { get; private set; }
        public bool IsVocalizerActive { get; private set; }
        public NewGameLocation SelectedLocation { get; private set; }
        public CombatLoadoutSelection SelectedCombatLoadout { get; private set; }
        public CombatTestMode SelectedCombatMode { get; private set; }
        public CombatEncounterSettings SelectedCombatEncounterSettings { get; private set; }
        public CombatEnemyKind SelectedLaboratoryEnemy { get; private set; }
        public CombatTestSelection SelectedCombatSelection => new CombatTestSelection(
            SelectedCombatLoadout, SelectedCombatMode, SelectedCombatEncounterSettings, SelectedLaboratoryEnemy);
        public CombatWeaponId SelectedCombatWeapon => SelectedCombatLoadout.InitialHeld;
        public CombatPreparationOption SelectedCombatOption { get; private set; }
        public bool IsBackSelected => IsChoosingStartTime ? SelectedStartTimeOption == StartTimeOption.Back :
            IsChoosingLaboratoryEnemy ? laboratoryEnemyBackSelected :
            IsChoosingLocation && SelectedLocation == NewGameLocation.Count ||
            IsChoosingCombatWeapon && SelectedCombatOption == CombatPreparationOption.Back ||
            IsDebugMenu && SelectedOption == StartMenuOption.Back;
        private StartMenuOption[] MenuOptions => IsDebugMenu ? DebugOptions : MainOptions;
        public int CombatOptionCount => CombatOptions.Length;
        private CombatPreparationOption[] CombatOptions =>
            SelectedCombatMode == CombatTestMode.Laboratory ? LaboratoryOptions : WarehouseOptions;
        public CombatPreparationOption CombatOptionAt(int index) => CombatOptions[index];
        public const int LaboratoryEnemyCount = 6;

        public static string LaboratoryEnemyLabelKey(CombatEnemyKind kind) => kind switch
        {
            CombatEnemyKind.Zombie => "combat.zombie",
            CombatEnemyKind.Docker => "combat.mode.docker",
            CombatEnemyKind.Crane => "combat.enemy.crane",
            CombatEnemyKind.Room => "combat.mode.room",
            CombatEnemyKind.Imprint => "combat.mode.imprint",
            CombatEnemyKind.LongExposure => "combat.mode.long-exposure",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public void Open()
        {
            SelectedOption = StartMenuOption.NewGame;
            IsCommitted = false;
            IsDebugMenu = false;
            IsChoosingStartTime = false;
            SelectedStartMinuteOfDay = NewGameStartService.MorningMinuteOfDay;
            SelectedStartTimeOption = StartTimeOption.Continue;
            AvailabilityMessageKey = string.Empty;
            IsChoosingLocation = false;
            IsChoosingCombatWeapon = false;
            IsChoosingLaboratoryEnemy = false;
            IsVocalizerActive = false;
            laboratoryEnemyBackSelected = false;
            SelectedLocation = NewGameLocation.AlpineVillage;
            SelectedCombatLoadout = CombatLoadoutSelection.Default;
            SelectedCombatMode = CombatTestMode.Standard;
            warehouseMode = CombatTestMode.Standard;
            SelectedLaboratoryEnemy = CombatEnemyKind.Zombie;
            SelectedCombatEncounterSettings = CombatEncounterSettings.Default;
            SelectedCombatOption = CombatPreparationOption.Crowbar;
        }

        public bool OpenDebugMenu()
        {
            if (IsCommitted || IsDebugMenu) return false;
            IsDebugMenu = true;
            SelectedOption = StartMenuOption.NewGame;
            return true;
        }

        /// <summary>Explicitly rearms a refused Debug trip without confirming the story launch.</summary>
        public void OpenDebugStartTime(int minuteOfDay)
        {
            if (minuteOfDay < 0 || minuteOfDay >= 24 * 60)
                throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
            Open();
            IsDebugMenu = true;
            IsChoosingStartTime = true;
            SelectedStartMinuteOfDay = minuteOfDay;
        }

        public void OpenCombatPreparation(CombatWeaponId weapon)
            => OpenCombatPreparation(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public void OpenCombatPreparation(CombatLoadoutSelection loadout)
            => OpenCombatPreparation(new CombatTestSelection(loadout));

        public void OpenCombatPreparation(CombatTestSelection selection)
        {
            CombatTestMode previousWarehouseMode = warehouseMode;
            Open();
            IsDebugMenu = true;
            warehouseMode = previousWarehouseMode;
            SelectedOption = selection.Mode == CombatTestMode.Laboratory ?
                StartMenuOption.Laboratory : StartMenuOption.CombatTest;
            IsChoosingCombatWeapon = true;
            SelectedCombatLoadout = selection.Loadout;
            SelectedCombatMode = selection.Mode == CombatTestMode.Crane ? CombatTestMode.Endless : selection.Mode;
            if (SelectedCombatMode != CombatTestMode.Laboratory) warehouseMode = SelectedCombatMode;
            SelectedCombatEncounterSettings = selection.EncounterSettings;
            SelectedLaboratoryEnemy = selection.LaboratoryEnemy;
            SelectedCombatOption = selection.Loadout.InitialHeld switch
            {
                CombatWeaponId.Pistol => CombatPreparationOption.Pistol,
                CombatWeaponId.Shotgun => CombatPreparationOption.Shotgun,
                _ => CombatPreparationOption.Crowbar
            };
        }

        public bool MoveSelection(int delta)
        {
            if (delta == 0 || IsCommitted || IsVocalizerActive)
            {
                return false;
            }

            int count = IsChoosingStartTime ? (int)StartTimeOption.Count :
                IsChoosingLaboratoryEnemy ? LaboratoryEnemyCount + 1 :
                IsChoosingLocation ? (int)NewGameLocation.Count + 1 :
                IsChoosingCombatWeapon ? CombatOptionCount : MenuOptions.Length;
            int selected = IsChoosingStartTime ? (int)SelectedStartTimeOption : IsChoosingLaboratoryEnemy ?
                laboratoryEnemyBackSelected ? LaboratoryEnemyCount : (int)SelectedLaboratoryEnemy :
                IsChoosingLocation ? (int)SelectedLocation :
                IsChoosingCombatWeapon ? Array.IndexOf(CombatOptions, SelectedCombatOption) :
                Array.IndexOf(MenuOptions, SelectedOption);
            int next = (selected + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            if (IsChoosingStartTime) SelectedStartTimeOption = (StartTimeOption)next;
            else if (IsChoosingLaboratoryEnemy)
            {
                laboratoryEnemyBackSelected = next == LaboratoryEnemyCount;
                if (!laboratoryEnemyBackSelected) SelectedLaboratoryEnemy = (CombatEnemyKind)next;
            }
            else if (IsChoosingLocation)
            {
                SelectedLocation = (NewGameLocation)next;
                AvailabilityMessageKey = string.Empty;
            }
            else if (IsChoosingCombatWeapon) FocusCombatOption(CombatOptionAt(next));
            else SelectedOption = MenuOptions[next];
            return true;
        }

        public bool SelectOption(StartMenuOption option)
        {
            if (IsCommitted || IsVocalizerActive || IsChoosingStartTime || IsChoosingLocation || IsChoosingCombatWeapon ||
                Array.IndexOf(MenuOptions, option) < 0 ||
                SelectedOption == option)
            {
                return false;
            }

            SelectedOption = option;
            return true;
        }

        public bool SelectLocation(NewGameLocation location)
        {
            if (IsCommitted || !IsChoosingLocation || location < NewGameLocation.AlpineVillage ||
                location >= NewGameLocation.Count || location == SelectedLocation) return false;
            SelectedLocation = location;
            AvailabilityMessageKey = string.Empty;
            return true;
        }

        public bool SelectStartTime(int minuteOfDay)
        {
            if (IsCommitted || !IsChoosingStartTime || minuteOfDay < 0 || minuteOfDay >= 24 * 60 ||
                SelectedStartMinuteOfDay == minuteOfDay) return false;
            SelectedStartMinuteOfDay = minuteOfDay;
            AvailabilityMessageKey = string.Empty;
            return true;
        }

        public bool SelectStartTimeOption(StartTimeOption option)
        {
            if (IsCommitted || !IsChoosingStartTime || option < StartTimeOption.Hour ||
                option >= StartTimeOption.Count || SelectedStartTimeOption == option) return false;
            SelectedStartTimeOption = option;
            return true;
        }

        public bool AdjustStartTime(int direction)
        {
            if (direction == 0 || IsCommitted || !IsChoosingStartTime) return false;
            int hour = SelectedStartMinuteOfDay / 60;
            int minute = SelectedStartMinuteOfDay % 60;
            if (SelectedStartTimeOption == StartTimeOption.Hour) hour = (hour + Math.Sign(direction) + 24) % 24;
            else if (SelectedStartTimeOption == StartTimeOption.Minute) minute = (minute + Math.Sign(direction) + 60) % 60;
            else return false;
            return SelectStartTime(hour * 60 + minute);
        }

        public bool ReturnToStartTime()
        {
            if (IsCommitted || !IsChoosingLocation) return false;
            IsChoosingLocation = false;
            IsChoosingStartTime = true;
            SelectedStartTimeOption = StartTimeOption.Continue;
            AvailabilityMessageKey = string.Empty;
            return true;
        }

        public bool SelectBack()
        {
            if (IsChoosingStartTime) return SelectStartTimeOption(StartTimeOption.Back);
            if (IsChoosingLaboratoryEnemy)
            {
                if (IsCommitted || laboratoryEnemyBackSelected) return false;
                laboratoryEnemyBackSelected = true;
                return true;
            }
            if (IsChoosingCombatWeapon) return SelectCombatOption(CombatPreparationOption.Back);
            if (IsDebugMenu && !IsChoosingLocation) return SelectOption(StartMenuOption.Back);
            if (IsCommitted || !IsChoosingLocation || IsBackSelected) return false;
            SelectedLocation = NewGameLocation.Count;
            return true;
        }

        public bool SelectCombatOption(CombatPreparationOption option)
        {
            if (IsCommitted || !IsChoosingCombatWeapon || IsChoosingLaboratoryEnemy ||
                Array.IndexOf(CombatOptions, option) < 0) return false;
            CombatPreparationOption previousOption = SelectedCombatOption;
            CombatLoadoutSelection previousLoadout = SelectedCombatLoadout;
            CombatTestMode previousMode = SelectedCombatMode;
            CombatEncounterSettings previousSettings = SelectedCombatEncounterSettings;
            SelectedCombatOption = option;
            if (option == CombatPreparationOption.Crowbar)
                SelectedCombatLoadout = SelectedCombatLoadout.WithInitialHeld(CombatWeaponId.Crowbar);
            else if (option == CombatPreparationOption.Shotgun)
                SelectedCombatLoadout = SelectedCombatLoadout.WithInitialHeld(CombatWeaponId.Shotgun);
            else if (option == CombatPreparationOption.Pistol)
                SelectedCombatLoadout = SelectedCombatLoadout.WithInitialHeld(CombatWeaponId.Pistol);
            else if (option == CombatPreparationOption.Standard)
                SelectedCombatMode = warehouseMode = CombatTestMode.Standard;
            else if (option == CombatPreparationOption.Endless)
                SelectedCombatMode = warehouseMode = CombatTestMode.Endless;
            else if (option == CombatPreparationOption.OpponentLimit)
                SelectedCombatEncounterSettings = SelectedCombatEncounterSettings.WithMaximumLiveOpponents(
                    SelectedCombatEncounterSettings.MaximumLiveOpponents % CombatEncounterSettings.OpponentCapacity + 1);
            return previousOption != option || previousLoadout != SelectedCombatLoadout || previousMode != SelectedCombatMode ||
                previousSettings != SelectedCombatEncounterSettings;
        }

        /// <summary>Navigation highlights a row without silently changing the launch choices.</summary>
        public bool FocusCombatOption(CombatPreparationOption option)
        {
            if (IsCommitted || !IsChoosingCombatWeapon || IsChoosingLaboratoryEnemy ||
                Array.IndexOf(CombatOptions, option) < 0 || option == SelectedCombatOption) return false;
            SelectedCombatOption = option;
            return true;
        }

        public bool ReturnToMainMenu()
        {
            if (IsCommitted) return false;
            if (!IsChoosingStartTime && !IsChoosingLocation && !IsChoosingCombatWeapon && !IsVocalizerActive)
            {
                if (!IsDebugMenu) return false;
                IsDebugMenu = false;
                SelectedOption = StartMenuOption.Debug;
                return true;
            }
            SelectedOption = IsVocalizerActive ? StartMenuOption.Vocalizer :
                IsChoosingCombatWeapon ? SelectedCombatMode == CombatTestMode.Laboratory ?
                StartMenuOption.Laboratory : StartMenuOption.CombatTest : StartMenuOption.NewGame;
            IsChoosingLocation = false;
            IsChoosingStartTime = false;
            AvailabilityMessageKey = string.Empty;
            IsChoosingCombatWeapon = false;
            IsChoosingLaboratoryEnemy = false;
            IsVocalizerActive = false;
            IsDebugMenu = true;
            SelectedLocation = NewGameLocation.AlpineVillage;
            return true;
        }

        public bool SelectLaboratoryEnemy(CombatEnemyKind kind)
        {
            if (IsCommitted || !IsChoosingLaboratoryEnemy || kind < CombatEnemyKind.Zombie ||
                kind > CombatEnemyKind.LongExposure || kind == SelectedLaboratoryEnemy && !laboratoryEnemyBackSelected)
                return false;
            SelectedLaboratoryEnemy = kind;
            laboratoryEnemyBackSelected = false;
            return true;
        }

        public bool ReturnToCombatPreparation()
        {
            if (IsCommitted || !IsChoosingLaboratoryEnemy) return false;
            IsChoosingLaboratoryEnemy = false;
            laboratoryEnemyBackSelected = false;
            SelectedCombatOption = CombatPreparationOption.LaboratoryEnemy;
            return true;
        }

        public StartMenuAction Confirm()
        {
            if (IsCommitted || IsVocalizerActive)
            {
                return StartMenuAction.None;
            }

            if (IsChoosingLaboratoryEnemy)
            {
                bool back = laboratoryEnemyBackSelected;
                ReturnToCombatPreparation();
                return back ? StartMenuAction.Back : StartMenuAction.ChooseCombatWeapon;
            }

            if (IsChoosingStartTime)
            {
                if (IsBackSelected)
                {
                    ReturnToMainMenu();
                    return StartMenuAction.Back;
                }
                if (SelectedStartTimeOption != StartTimeOption.Continue)
                {
                    AdjustStartTime(1);
                    return StartMenuAction.ChooseStartTime;
                }
                IsChoosingStartTime = false;
                IsChoosingLocation = true;
                return StartMenuAction.ChooseLocation;
            }

            if (IsChoosingLocation)
            {
                if (IsBackSelected)
                {
                    ReturnToStartTime();
                    return StartMenuAction.Back;
                }
                if (!NewGameLocationCatalog.IsAvailableAt(SelectedLocation, SelectedStartMinuteOfDay))
                {
                    AvailabilityMessageKey = NewGameLocationCatalog.UnavailableMessageKey(
                        SelectedLocation, SelectedStartMinuteOfDay);
                    return StartMenuAction.LocationUnavailable;
                }
                IsCommitted = true;
                return StartMenuAction.NewGame;
            }

            if (IsChoosingCombatWeapon)
            {
                if (IsBackSelected)
                {
                    ReturnToMainMenu();
                    return StartMenuAction.Back;
                }
                if (SelectedCombatOption == CombatPreparationOption.LaboratoryEnemy)
                {
                    IsChoosingLaboratoryEnemy = true;
                    laboratoryEnemyBackSelected = false;
                    return StartMenuAction.ChooseLaboratoryEnemy;
                }
                if (SelectedCombatOption != CombatPreparationOption.Start)
                {
                    SelectCombatOption(SelectedCombatOption);
                    return StartMenuAction.ChooseCombatWeapon;
                }
                IsCommitted = true;
                return StartMenuAction.CombatTest;
            }

            switch (SelectedOption)
            {
                case StartMenuOption.NewGame:
                    if (!IsDebugMenu)
                    {
                        IsCommitted = true;
                        return StartMenuAction.StoryNewGame;
                    }
                    IsChoosingStartTime = true;
                    SelectedStartTimeOption = StartTimeOption.Continue;
                    SelectedLocation = NewGameLocation.AlpineVillage;
                    return StartMenuAction.ChooseStartTime;
                case StartMenuOption.Debug:
                    OpenDebugMenu();
                    return StartMenuAction.OpenDebug;
                case StartMenuOption.Back:
                    ReturnToMainMenu();
                    return StartMenuAction.Back;
                case StartMenuOption.CombatTest:
                    OpenCombatPreparation(new CombatTestSelection(SelectedCombatLoadout, warehouseMode,
                        SelectedCombatEncounterSettings, SelectedLaboratoryEnemy));
                    return StartMenuAction.ChooseCombatWeapon;
                case StartMenuOption.Laboratory:
                    OpenCombatPreparation(new CombatTestSelection(SelectedCombatLoadout, CombatTestMode.Laboratory,
                        SelectedCombatEncounterSettings, SelectedLaboratoryEnemy));
                    return StartMenuAction.ChooseCombatWeapon;
                case StartMenuOption.Vocalizer:
                    IsVocalizerActive = true;
                    return StartMenuAction.OpenVocalizer;
                case StartMenuOption.Quit:
                    IsCommitted = true;
                    return StartMenuAction.Quit;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported start option '{SelectedOption}'.");
            }
        }
    }
}
