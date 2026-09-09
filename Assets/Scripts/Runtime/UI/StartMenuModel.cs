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
        Quit = 2
    }

    /// <summary>
    /// The launch card's two choices. Committing is one-shot on purpose: the
    /// shared input reader goes quiet the moment a scene load starts, but an
    /// IMGUI button does not consult that policy, so the latch is what keeps a
    /// second click from asking for a second new game.
    /// </summary>
    public sealed class StartMenuModel
    {
        public StartMenuOption SelectedOption { get; private set; }
        public bool IsCommitted { get; private set; }

        public void Open()
        {
            SelectedOption = StartMenuOption.NewGame;
            IsCommitted = false;
        }

        public bool MoveSelection(int delta)
        {
            if (delta == 0 || IsCommitted)
            {
                return false;
            }

            int count = (int)StartMenuOption.Count;
            int next =
                ((int)SelectedOption + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            SelectedOption = (StartMenuOption)next;
            return true;
        }

        public bool SelectOption(StartMenuOption option)
        {
            if (IsCommitted ||
                option < StartMenuOption.NewGame ||
                option >= StartMenuOption.Count ||
                SelectedOption == option)
            {
                return false;
            }

            SelectedOption = option;
            return true;
        }

        public StartMenuAction Confirm()
        {
            if (IsCommitted)
            {
                return StartMenuAction.None;
            }

            IsCommitted = true;
            switch (SelectedOption)
            {
                case StartMenuOption.NewGame:
                    return StartMenuAction.NewGame;
                case StartMenuOption.Quit:
                    return StartMenuAction.Quit;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported start option '{SelectedOption}'.");
            }
        }
    }
}
