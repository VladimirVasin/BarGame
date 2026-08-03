using System;

namespace BarPromenade
{
    public enum PauseMenuOption
    {
        Resume = 0,
        Restart = 1,
        Quit = 2,
        Count = 3
    }

    public enum PauseMenuPage
    {
        Main = 0,
        Confirmation = 1
    }

    public enum PauseMenuAction
    {
        None = 0,
        Resume = 1,
        Restart = 2,
        Quit = 3
    }

    public sealed class PauseMenuModel
    {
        public PauseMenuPage Page { get; private set; }
        public PauseMenuOption SelectedOption { get; private set; }
        public PauseMenuOption ConfirmationTarget { get; private set; }
        public bool ConfirmationYesSelected { get; private set; }

        public void Open()
        {
            Page = PauseMenuPage.Main;
            SelectedOption = PauseMenuOption.Resume;
            ConfirmationTarget = PauseMenuOption.Resume;
            ConfirmationYesSelected = false;
        }

        public bool MoveSelection(int delta)
        {
            if (delta == 0)
            {
                return false;
            }

            if (Page == PauseMenuPage.Confirmation)
            {
                ConfirmationYesSelected =
                    !ConfirmationYesSelected;
                return true;
            }

            int count = (int)PauseMenuOption.Count;
            int next =
                ((int)SelectedOption + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            SelectedOption = (PauseMenuOption)next;
            return true;
        }

        public bool SelectOption(PauseMenuOption option)
        {
            if (Page != PauseMenuPage.Main ||
                option < PauseMenuOption.Resume ||
                option >= PauseMenuOption.Count ||
                SelectedOption == option)
            {
                return false;
            }

            SelectedOption = option;
            return true;
        }

        public bool SelectConfirmation(bool yes)
        {
            if (Page != PauseMenuPage.Confirmation ||
                ConfirmationYesSelected == yes)
            {
                return false;
            }

            ConfirmationYesSelected = yes;
            return true;
        }

        public PauseMenuAction Confirm()
        {
            if (Page == PauseMenuPage.Confirmation)
            {
                if (!ConfirmationYesSelected)
                {
                    Page = PauseMenuPage.Main;
                    return PauseMenuAction.None;
                }

                return ConfirmationTarget == PauseMenuOption.Restart
                    ? PauseMenuAction.Restart
                    : PauseMenuAction.Quit;
            }

            switch (SelectedOption)
            {
                case PauseMenuOption.Resume:
                    return PauseMenuAction.Resume;
                case PauseMenuOption.Restart:
                case PauseMenuOption.Quit:
                    ConfirmationTarget = SelectedOption;
                    ConfirmationYesSelected = false;
                    Page = PauseMenuPage.Confirmation;
                    return PauseMenuAction.None;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported pause option '{SelectedOption}'.");
            }
        }

        public PauseMenuAction Cancel()
        {
            if (Page == PauseMenuPage.Confirmation)
            {
                Page = PauseMenuPage.Main;
                ConfirmationYesSelected = false;
                return PauseMenuAction.None;
            }

            return PauseMenuAction.Resume;
        }
    }
}
