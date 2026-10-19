using System;

namespace BarPromenade
{
    public enum PauseMenuOption
    {
        Resume = 0,
        Options = 1,
        Restart = 2,
        Quit = 3,
        Cutscenes = 4,
        Count = 5
    }

    public enum PauseMenuPage
    {
        Main = 0,
        Confirmation = 1,
        Options = 2,
        Cutscenes = 3
    }

    public enum PauseMenuOptionsRow
    {
        GraphicsStyle = 0,
        CleanAntialiasing = 1,
        DepthOfField = 2,
        IntoxicationFx = 3,
        Dither = 4,
        Scanlines = 5,
        AspectRatio43 = 6,
        VertexJitter = 7,
        Begotten = 8,
        Back = 9,
        Count = 10
    }

    public enum PauseMenuAction
    {
        None = 0,
        Resume = 1,
        Restart = 2,
        Quit = 3,
        ToggleGraphicsOption = 4,
        StartCutscene = 5,
        CycleGraphicsStyle = 6
    }

    public sealed class PauseMenuModel
    {
        private static readonly PauseMenuOption[] MainOptions =
        {
            PauseMenuOption.Resume,
            PauseMenuOption.Options,
            PauseMenuOption.Restart,
            PauseMenuOption.Quit
        };

        private static readonly PauseMenuOption[] MainOptionsWithCutscenes =
        {
            PauseMenuOption.Resume,
            PauseMenuOption.Options,
            PauseMenuOption.Cutscenes,
            PauseMenuOption.Restart,
            PauseMenuOption.Quit
        };

        public PauseMenuPage Page { get; private set; }
        public PauseMenuOption SelectedOption { get; private set; }
        public PauseMenuOption ConfirmationTarget { get; private set; }
        public bool ConfirmationYesSelected { get; private set; }
        public bool CutscenesAvailable { get; private set; }
        public int CutsceneCount { get; private set; }
        public int SelectedCutsceneIndex { get; private set; }
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
            SelectedOptionsRow = PauseMenuOptionsRow.GraphicsStyle;
            SelectedCutsceneIndex = 0;
        }

        public void ConfigureCutscenes(bool available, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            CutscenesAvailable = available;
            CutsceneCount = available ? count : 0;
            SelectedCutsceneIndex = Math.Min(
                SelectedCutsceneIndex, CutsceneCount);
            if (!available)
            {
                if (Page == PauseMenuPage.Cutscenes)
                {
                    Page = PauseMenuPage.Main;
                }

                if (SelectedOption == PauseMenuOption.Cutscenes)
                {
                    SelectedOption = PauseMenuOption.Resume;
                }
            }
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

            if (Page == PauseMenuPage.Cutscenes)
            {
                int rowCount = CutsceneCount + 1;
                SelectedCutsceneIndex =
                    (SelectedCutsceneIndex + Math.Sign(delta) + rowCount) %
                    rowCount;
                return rowCount > 1;
            }

            PauseMenuOption[] options = CutscenesAvailable
                ? MainOptionsWithCutscenes
                : MainOptions;
            int count = options.Length;
            int next =
                (Array.IndexOf(options, SelectedOption) + Math.Sign(delta)) % count;
            if (next < 0)
            {
                next += count;
            }

            SelectedOption = options[next];
            return true;
        }

        public bool SelectCutscene(int index)
        {
            if (Page != PauseMenuPage.Cutscenes ||
                index < 0 || index > CutsceneCount ||
                SelectedCutsceneIndex == index)
            {
                return false;
            }

            SelectedCutsceneIndex = index;
            return true;
        }

        public bool SelectOptionsRow(PauseMenuOptionsRow row)
        {
            if (Page != PauseMenuPage.Options ||
                row < PauseMenuOptionsRow.GraphicsStyle ||
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
                (option == PauseMenuOption.Cutscenes && !CutscenesAvailable) ||
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
            if (Page == PauseMenuPage.Cutscenes)
            {
                if (SelectedCutsceneIndex == CutsceneCount)
                {
                    Page = PauseMenuPage.Main;
                    return PauseMenuAction.None;
                }

                return PauseMenuAction.StartCutscene;
            }

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

                return SelectedOptionsRow == PauseMenuOptionsRow.GraphicsStyle
                    ? PauseMenuAction.CycleGraphicsStyle
                    : PauseMenuAction.ToggleGraphicsOption;
            }

            switch (SelectedOption)
            {
                case PauseMenuOption.Resume:
                    return PauseMenuAction.Resume;
                case PauseMenuOption.Options:
                    Page = PauseMenuPage.Options;
                    SelectedOptionsRow =
                        PauseMenuOptionsRow.GraphicsStyle;
                    return PauseMenuAction.None;
                case PauseMenuOption.Cutscenes:
                    Page = PauseMenuPage.Cutscenes;
                    SelectedCutsceneIndex = 0;
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

            if (Page == PauseMenuPage.Options ||
                Page == PauseMenuPage.Cutscenes)
            {
                Page = PauseMenuPage.Main;
                return PauseMenuAction.None;
            }

            return PauseMenuAction.Resume;
        }
    }
}
