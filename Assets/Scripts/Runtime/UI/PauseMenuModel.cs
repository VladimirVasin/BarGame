using System;

namespace BarPromenade
{
    public enum PauseMenuOption
    {
        Resume = 0,
        Options = 1,
        Restart = 2,
        Quit = 3,
        Count = 4
    }

    public enum PauseMenuPage
    {
        Main = 0,
        Confirmation = 1,
        Options = 2
    }

    public enum PauseMenuOptionsRow
    {
        DepthOfField = 0,
        IntoxicationFx = 1,
        Dither = 2,
        Scanlines = 3,
        RainOnLens = 4,
        AspectRatio43 = 5,
        HighFrameRate = 6,
        Back = 7,
        Count = 8
    }

    public enum PauseMenuAction
    {
        None = 0,
        Resume = 1,
        Restart = 2,
        Quit = 3,
        ToggleGraphicsOption = 4
    }

    public sealed class PauseMenuModel
    {
        public PauseMenuPage Page { get; private set; }
        public PauseMenuOption SelectedOption { get; private set; }
        public PauseMenuOption ConfirmationTarget { get; private set; }
        public bool ConfirmationYesSelected { get; private set; }
        public PauseMenuOptionsRow SelectedOptionsRow
        {
            get;
            private set;
        }

        public void Open()
        {
            Page = PauseMenuPage.Main;
            SelectedOption = PauseMenuOption.Resume;
            ConfirmationTarget = PauseMenuOption.Resume;
            ConfirmationYesSelected = false;
            SelectedOptionsRow = PauseMenuOptionsRow.DepthOfField;
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

            if (Page == PauseMenuPage.Options)
            {
                int rowCount = (int)PauseMenuOptionsRow.Count;
                int nextRow =
                    ((int)SelectedOptionsRow + Math.Sign(delta)) %
                    rowCount;
                if (nextRow < 0)
                {
                    nextRow += rowCount;
                }

                SelectedOptionsRow = (PauseMenuOptionsRow)nextRow;
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

        public bool SelectOptionsRow(PauseMenuOptionsRow row)
        {
            if (Page != PauseMenuPage.Options ||
                row < PauseMenuOptionsRow.DepthOfField ||
                row >= PauseMenuOptionsRow.Count ||
                SelectedOptionsRow == row)
            {
                return false;
            }

            SelectedOptionsRow = row;
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

            if (Page == PauseMenuPage.Options)
            {
                if (SelectedOptionsRow == PauseMenuOptionsRow.Back)
                {
                    Page = PauseMenuPage.Main;
                    return PauseMenuAction.None;
                }

                return PauseMenuAction.ToggleGraphicsOption;
            }

            switch (SelectedOption)
            {
                case PauseMenuOption.Resume:
                    return PauseMenuAction.Resume;
                case PauseMenuOption.Options:
                    Page = PauseMenuPage.Options;
                    SelectedOptionsRow =
                        PauseMenuOptionsRow.DepthOfField;
                    return PauseMenuAction.None;
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

            if (Page == PauseMenuPage.Options)
            {
                Page = PauseMenuPage.Main;
                return PauseMenuAction.None;
            }

            return PauseMenuAction.Resume;
        }
    }
}
