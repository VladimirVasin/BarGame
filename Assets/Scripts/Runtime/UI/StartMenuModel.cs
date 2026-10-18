using System;

namespace BarPromenade
{
    public enum StartMenuOption
    {
        NewGame = 0,
        CombatTest = 1,
        Laboratory = 2,
        Quit = 3,
        Count = 4
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
        ChooseLaboratoryEnemy = 7
    }

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
        public bool IsChoosingLocation { get; private set; }
        public bool IsChoosingCombatWeapon { get; private set; }
        public bool IsChoosingLaboratoryEnemy { get; private set; }
        public NewGameLocation SelectedLocation { get; private set; }
        public CombatLoadoutSelection SelectedCombatLoadout { get; private set; }
        public CombatTestMode SelectedCombatMode { get; private set; }
        public CombatEncounterSettings SelectedCombatEncounterSettings { get; private set; }
        public CombatEnemyKind SelectedLaboratoryEnemy { get; private set; }
        public CombatTestSelection SelectedCombatSelection => new CombatTestSelection(
            SelectedCombatLoadout, SelectedCombatMode, SelectedCombatEncounterSettings, SelectedLaboratoryEnemy);
        public CombatWeaponId SelectedCombatWeapon => SelectedCombatLoadout.InitialHeld;
        public CombatPreparationOption SelectedCombatOption { get; private set; }
        public bool IsBackSelected => IsChoosingLaboratoryEnemy ? laboratoryEnemyBackSelected :
            IsChoosingLocation && SelectedLocation == NewGameLocation.Count ||
            IsChoosingCombatWeapon && SelectedCombatOption == CombatPreparationOption.Back;
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
            IsChoosingLocation = false;
            IsChoosingCombatWeapon = false;
            IsChoosingLaboratoryEnemy = false;
            laboratoryEnemyBackSelected = false;
            SelectedLocation = NewGameLocation.AlpineVillage;
            SelectedCombatLoadout = CombatLoadoutSelection.Default;
            SelectedCombatMode = CombatTestMode.Standard;
            warehouseMode = CombatTestMode.Standard;
            SelectedLaboratoryEnemy = CombatEnemyKind.Zombie;
            SelectedCombatEncounterSettings = CombatEncounterSettings.Default;
            SelectedCombatOption = CombatPreparationOption.Crowbar;
        }

        public void OpenCombatPreparation(CombatWeaponId weapon)
            => OpenCombatPreparation(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public void OpenCombatPreparation(CombatLoadoutSelection loadout)
            => OpenCombatPreparation(new CombatTestSelection(loadout));

        public void OpenCombatPreparation(CombatTestSelection selection)
        {
            CombatTestMode previousWarehouseMode = warehouseMode;
            Open();
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
            if (delta == 0 || IsCommitted)
            {
                return false;
            }

            int count = IsChoosingLaboratoryEnemy ? LaboratoryEnemyCount + 1 :
                IsChoosingLocation ? (int)NewGameLocation.Count + 1 :
                IsChoosingCombatWeapon ? CombatOptionCount : (int)StartMenuOption.Count;
            int selected = IsChoosingLaboratoryEnemy ?
                laboratoryEnemyBackSelected ? LaboratoryEnemyCount : (int)SelectedLaboratoryEnemy :
                IsChoosingLocation ? (int)SelectedLocation :
                IsChoosingCombatWeapon ? Array.IndexOf(CombatOptions, SelectedCombatOption) : (int)SelectedOption;
            int next = (selected + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            if (IsChoosingLaboratoryEnemy)
            {
                laboratoryEnemyBackSelected = next == LaboratoryEnemyCount;
                if (!laboratoryEnemyBackSelected) SelectedLaboratoryEnemy = (CombatEnemyKind)next;
            }
            else if (IsChoosingLocation) SelectedLocation = (NewGameLocation)next;
            else if (IsChoosingCombatWeapon) FocusCombatOption(CombatOptionAt(next));
            else SelectedOption = (StartMenuOption)next;
            return true;
        }

        public bool SelectOption(StartMenuOption option)
        {
            if (IsCommitted || IsChoosingLocation || IsChoosingCombatWeapon ||
                option < StartMenuOption.NewGame ||
                option >= StartMenuOption.Count ||
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
            return true;
        }

        public bool SelectBack()
        {
            if (IsChoosingLaboratoryEnemy)
            {
                if (IsCommitted || laboratoryEnemyBackSelected) return false;
                laboratoryEnemyBackSelected = true;
                return true;
            }
            if (IsChoosingCombatWeapon) return SelectCombatOption(CombatPreparationOption.Back);
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
            if (IsCommitted || (!IsChoosingLocation && !IsChoosingCombatWeapon)) return false;
            SelectedOption = IsChoosingCombatWeapon ? SelectedCombatMode == CombatTestMode.Laboratory ?
                StartMenuOption.Laboratory : StartMenuOption.CombatTest : StartMenuOption.NewGame;
            IsChoosingLocation = false;
            IsChoosingCombatWeapon = false;
            IsChoosingLaboratoryEnemy = false;
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
            if (IsCommitted)
            {
                return StartMenuAction.None;
            }

            if (IsChoosingLaboratoryEnemy)
            {
                bool back = laboratoryEnemyBackSelected;
                ReturnToCombatPreparation();
                return back ? StartMenuAction.Back : StartMenuAction.ChooseCombatWeapon;
            }

            if (IsChoosingLocation)
            {
                if (IsBackSelected)
                {
                    ReturnToMainMenu();
                    return StartMenuAction.Back;
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
                    IsChoosingLocation = true;
                    SelectedLocation = NewGameLocation.AlpineVillage;
                    return StartMenuAction.ChooseLocation;
                case StartMenuOption.CombatTest:
                    OpenCombatPreparation(new CombatTestSelection(SelectedCombatLoadout, warehouseMode,
                        SelectedCombatEncounterSettings, SelectedLaboratoryEnemy));
                    return StartMenuAction.ChooseCombatWeapon;
                case StartMenuOption.Laboratory:
                    OpenCombatPreparation(new CombatTestSelection(SelectedCombatLoadout, CombatTestMode.Laboratory,
                        SelectedCombatEncounterSettings, SelectedLaboratoryEnemy));
                    return StartMenuAction.ChooseCombatWeapon;
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
