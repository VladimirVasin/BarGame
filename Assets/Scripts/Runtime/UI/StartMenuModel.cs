using System;

namespace BarPromenade
{
    public enum StartMenuOption
    {
        NewGame = 0,
        CombatTest = 1,
        Quit = 2,
        Count = 3
    }

    public enum StartMenuAction
    {
        None = 0,
        NewGame = 1,
        Quit = 2,
        ChooseLocation = 3,
        Back = 4,
        CombatTest = 5,
        ChooseCombatWeapon = 6
    }

    public enum CombatPreparationOption { Crowbar, Shotgun, Pistol, Secondary, Start, Back, Count }

    /// <summary>
    /// The launch card and its location and weapon pickers. Committing is one-shot: the
    /// shared input reader goes quiet the moment a scene load starts, but an
    /// IMGUI button does not consult that policy, so the latch is what keeps a
    /// second click from asking for a second new game.
    /// </summary>
    public sealed class StartMenuModel
    {
        public StartMenuOption SelectedOption { get; private set; }
        public bool IsCommitted { get; private set; }
        public bool IsChoosingLocation { get; private set; }
        public bool IsChoosingCombatWeapon { get; private set; }
        public NewGameLocation SelectedLocation { get; private set; }
        public CombatLoadoutSelection SelectedCombatLoadout { get; private set; }
        public CombatWeaponId SelectedCombatWeapon => SelectedCombatLoadout.InitialHeld;
        public CombatPreparationOption SelectedCombatOption { get; private set; }
        public bool IsBackSelected => IsChoosingLocation && SelectedLocation == NewGameLocation.Count ||
            IsChoosingCombatWeapon && SelectedCombatOption == CombatPreparationOption.Back;

        public void Open()
        {
            SelectedOption = StartMenuOption.NewGame;
            IsCommitted = false;
            IsChoosingLocation = false;
            IsChoosingCombatWeapon = false;
            SelectedLocation = NewGameLocation.AlpineVillage;
            SelectedCombatLoadout = CombatLoadoutSelection.Default;
            SelectedCombatOption = CombatPreparationOption.Crowbar;
        }

        public void OpenCombatPreparation(CombatWeaponId weapon)
            => OpenCombatPreparation(CombatLoadoutSelection.FromLegacyWeapon(weapon));

        public void OpenCombatPreparation(CombatLoadoutSelection loadout)
        {
            Open();
            SelectedOption = StartMenuOption.CombatTest;
            IsChoosingCombatWeapon = true;
            SelectedCombatLoadout = loadout;
            SelectedCombatOption = loadout.InitialHeld switch
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

            int count = IsChoosingLocation ? (int)NewGameLocation.Count + 1 :
                IsChoosingCombatWeapon ? (int)CombatPreparationOption.Count : (int)StartMenuOption.Count;
            int selected = IsChoosingLocation ? (int)SelectedLocation :
                IsChoosingCombatWeapon ? (int)SelectedCombatOption : (int)SelectedOption;
            int next = (selected + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            if (IsChoosingLocation) SelectedLocation = (NewGameLocation)next;
            else if (IsChoosingCombatWeapon) FocusCombatOption((CombatPreparationOption)next);
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
            if (IsChoosingCombatWeapon) return SelectCombatOption(CombatPreparationOption.Back);
            if (IsCommitted || !IsChoosingLocation || IsBackSelected) return false;
            SelectedLocation = NewGameLocation.Count;
            return true;
        }

        public bool SelectCombatOption(CombatPreparationOption option)
        {
            if (IsCommitted || !IsChoosingCombatWeapon || option < CombatPreparationOption.Crowbar ||
                option >= CombatPreparationOption.Count) return false;
            CombatPreparationOption previousOption = SelectedCombatOption;
            CombatLoadoutSelection previousLoadout = SelectedCombatLoadout;
            SelectedCombatOption = option;
            if (option == CombatPreparationOption.Crowbar)
                SelectedCombatLoadout = SelectedCombatLoadout.WithSecondary(CombatWeaponId.Crowbar);
            else if (option == CombatPreparationOption.Shotgun)
                SelectedCombatLoadout = SelectedCombatLoadout.WithSecondary(CombatWeaponId.Shotgun);
            else if (option == CombatPreparationOption.Pistol)
                SelectedCombatLoadout = SelectedCombatLoadout.WithInitialHeld(CombatWeaponId.Pistol);
            else if (option == CombatPreparationOption.Secondary)
                SelectedCombatLoadout = SelectedCombatLoadout.WithInitialHeld(SelectedCombatLoadout.Secondary);
            return previousOption != option || previousLoadout != SelectedCombatLoadout;
        }

        /// <summary>Navigation highlights a row without silently changing either radio choice.</summary>
        public bool FocusCombatOption(CombatPreparationOption option)
        {
            if (IsCommitted || !IsChoosingCombatWeapon || option < CombatPreparationOption.Crowbar ||
                option >= CombatPreparationOption.Count || option == SelectedCombatOption) return false;
            SelectedCombatOption = option;
            return true;
        }

        public bool ReturnToMainMenu()
        {
            if (IsCommitted || (!IsChoosingLocation && !IsChoosingCombatWeapon)) return false;
            SelectedOption = IsChoosingCombatWeapon ? StartMenuOption.CombatTest : StartMenuOption.NewGame;
            IsChoosingLocation = false;
            IsChoosingCombatWeapon = false;
            SelectedLocation = NewGameLocation.AlpineVillage;
            return true;
        }

        public StartMenuAction Confirm()
        {
            if (IsCommitted)
            {
                return StartMenuAction.None;
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
                    OpenCombatPreparation(SelectedCombatLoadout);
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
