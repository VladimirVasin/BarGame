using System;

namespace BarPromenade
{
    public enum StartMenuOption
    {
        NewGame = 0,
        Quit = 1,
        Count = 2
    }

    public enum StartMenuAction
    {
        None = 0,
        NewGame = 1,
        Quit = 2,
        ChooseLocation = 3,
        Back = 4
    }

    /// <summary>
    /// The launch card and its location picker. Committing is one-shot: the
    /// shared input reader goes quiet the moment a scene load starts, but an
    /// IMGUI button does not consult that policy, so the latch is what keeps a
    /// second click from asking for a second new game.
    /// </summary>
    public sealed class StartMenuModel
    {
        public StartMenuOption SelectedOption { get; private set; }
        public bool IsCommitted { get; private set; }
        public bool IsChoosingLocation { get; private set; }
        public NewGameLocation SelectedLocation { get; private set; }
        public bool IsBackSelected => IsChoosingLocation && SelectedLocation == NewGameLocation.Count;

        public void Open()
        {
            SelectedOption = StartMenuOption.NewGame;
            IsCommitted = false;
            IsChoosingLocation = false;
            SelectedLocation = NewGameLocation.AlpineVillage;
        }

        public bool MoveSelection(int delta)
        {
            if (delta == 0 || IsCommitted)
            {
                return false;
            }

            int count = IsChoosingLocation ? (int)NewGameLocation.Count + 1 : (int)StartMenuOption.Count;
            int selected = IsChoosingLocation ? (int)SelectedLocation : (int)SelectedOption;
            int next = (selected + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            if (IsChoosingLocation) SelectedLocation = (NewGameLocation)next;
            else SelectedOption = (StartMenuOption)next;
            return true;
        }

        public bool SelectOption(StartMenuOption option)
        {
            if (IsCommitted || IsChoosingLocation ||
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
            if (IsCommitted || !IsChoosingLocation || IsBackSelected) return false;
            SelectedLocation = NewGameLocation.Count;
            return true;
        }

        public bool ReturnToMainMenu()
        {
            if (IsCommitted || !IsChoosingLocation) return false;
            IsChoosingLocation = false;
            SelectedOption = StartMenuOption.NewGame;
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

            switch (SelectedOption)
            {
                case StartMenuOption.NewGame:
                    IsChoosingLocation = true;
                    SelectedLocation = NewGameLocation.AlpineVillage;
                    return StartMenuAction.ChooseLocation;
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
